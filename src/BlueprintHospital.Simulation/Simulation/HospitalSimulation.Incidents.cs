using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>How often major incidents happen (a New Game choice).</summary>
public enum MajorIncidentFrequency
{
    Off,

    /// <summary>About one a month of game time.</summary>
    Rare,

    /// <summary>About one every week or two, and the big ones too.</summary>
    Realistic,
}

/// <summary>One casualty of a major incident, from the phone call until they leave A&amp;E.</summary>
public sealed class IncidentCasualty
{
    public int Number { get; set; }

    /// <summary>"driver", "child".</summary>
    public string Label { get; set; } = string.Empty;

    public string ConditionId { get; set; } = string.Empty;

    public int Age { get; set; }

    /// <summary>The crew's colour on scene.</summary>
    public TriageCategory Triage { get; set; }

    public CasualtyTransport Transport { get; set; }

    public bool FireCrew { get; set; }

    public DateTime ArriveAt { get; set; }

    /// <summary>An air casualty with no helipad to land on: the helicopter set down nearby and they are coming the rest of the way by road.</summary>
    public bool ByRoad { get; set; }

    /// <summary>Set once they are here.</summary>
    public int? PatientId { get; set; }
}

/// <summary>How a major incident went, shown when the last casualty has left A&amp;E.</summary>
public sealed class IncidentDebrief
{
    public int Survived { get; set; }

    public int Total { get; set; }

    /// <summary>The longest a red casualty waited to see a doctor (null if there were none).</summary>
    public double? RedToDoctorMinutes { get; set; }

    public long OvertimeCost { get; set; }

    /// <summary>1 to 5.</summary>
    public int Stars { get; set; }

    public string Verdict { get; set; } = string.Empty;
}

/// <summary>
/// A major incident: ambulance control phones ahead (standby, with the METHANE message), the player gets the
/// department ready, the casualties come in a few minutes apart, and when the last has left A&amp;E there is a debrief.
/// </summary>
public sealed class MajorIncident
{
    public int Id { get; set; }

    /// <summary>The kind (see <see cref="IncidentDefinition"/>).</summary>
    public string IncidentId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public DateTime DeclaredAt { get; set; }

    public List<IncidentCasualty> Casualties { get; set; } = new();

    /// <summary>The get-ready actions taken (see <see cref="HospitalSimulation.IncidentActions"/>).</summary>
    public List<string> ActionsDone { get; set; } = new();

    /// <summary>Off-duty staff called in, paid overtime until it is over.</summary>
    public List<int> CalledInStaffIds { get; set; } = new();

    /// <summary>The trauma team waiting in Resus.</summary>
    public List<int> TraumaTeamIds { get; set; } = new();

    public DateTime? TraumaTeamCalledAt { get; set; }

    /// <summary>When the whole team was in Resus.</summary>
    public DateTime? TraumaTeamReadyAt { get; set; }

    public double OvertimeCost { get; set; }

    /// <summary>When the last casualty left A&amp;E and the department stood down.</summary>
    public DateTime? StoodDownAt { get; set; }

    public IncidentDebrief? Debrief { get; set; }

    public bool IsActive => StoodDownAt is null;

    public DateTime FirstArrival => Casualties.Count == 0 ? DeclaredAt : Casualties.Min(c => c.ArriveAt);

    /// <summary>Before the first casualty is here.</summary>
    public bool OnStandby(DateTime now) => IsActive && Casualties.All(c => c.PatientId is null) && now < FirstArrival;

    public int Count(TriageCategory colour) => Casualties.Count(c => c.Triage == colour);
}

public sealed record MajorIncidentDeclared(MajorIncident Incident) : IGameEvent;

public sealed record MajorIncidentStoodDown(MajorIncident Incident) : IGameEvent;

/// <summary>A get-ready action as the incident panel shows it.</summary>
public sealed record IncidentActionInfo(string Id, string Text, string Button, bool Done, bool Available);

public sealed partial class HospitalSimulation
{
    public const string CancelListsAction = "cancel_lists";
    public const string CallInStaffAction = "call_in";
    public const string DischargeEarlyAction = "discharge";
    public const string ClearResusAction = "clear";
    public const string TraumaTeamAction = "trauma_team";

    public static readonly IReadOnlyList<string> IncidentActions = new[] { CancelListsAction, CallInStaffAction, DischargeEarlyAction, ClearResusAction, TraumaTeamAction };

    /// <summary>The most off-duty staff that are phoned to come in.</summary>
    public const int MaxCalledIn = 6;

    /// <summary>Overtime is paid at time and a half.</summary>
    public const double OvertimeRate = 1.5;

    /// <summary>Paid hours in a year, to turn a salary into an hourly rate.</summary>
    private const double HoursPerYear = 37.5 * 52;

    private GameRandom _incidentRandom = new(1);
    private MajorIncident? _incident;
    private DateTime _incidentCheckedTo;
    private DateTime _noIncidentBefore;
    private int _nextIncidentId = 1;
    private double _unpaidOvertime;
    private readonly HashSet<int> _incidentPriority = new();

    /// <summary>How often major incidents happen.</summary>
    public MajorIncidentFrequency MajorIncidents { get; set; }

    /// <summary>The incident on now, or just stood down (until the next one; its debrief is on it).</summary>
    public MajorIncident? Incident => _incident;

    /// <summary>The incident on now (null once it has stood down).</summary>
    public MajorIncident? ActiveIncident => _incident is { IsActive: true } i ? i : null;

    /// <summary>Whether major incidents can happen in this game now (on, and the level has red patients and ambulances).</summary>
    public bool IncidentsPossible => MajorIncidents != MajorIncidentFrequency.Off && Database.Incidents.Count > 0
                                     && IsFeatureOn(LevelFeatures.Ambulances) && IsFeatureOn(LevelFeatures.RedPatients);

    /// <summary>The average days between incidents.</summary>
    public double DaysBetweenIncidents => MajorIncidents == MajorIncidentFrequency.Realistic ? 10 : 30;

    /// <summary>Whether a staff member is with the trauma team waiting in Resus.</summary>
    public bool OnTraumaStandby(StaffMember member) => ActiveIncident is { } i && i.TraumaTeamIds.Contains(member.Id);

    /// <summary>Called in from home for the major incident.</summary>
    public bool IsCalledIn(StaffMember member) => ActiveIncident is { } i && i.CalledInStaffIds.Contains(member.Id);

    private void SetUpIncidents(int seed)
    {
        _incidentRandom = new GameRandom(unchecked((seed * 7919) ^ 0x51ED27));
        _incidentCheckedTo = Now;
        _noIncidentBefore = Now.AddDays(2);
    }

    // ---------------------------------------------------------------- starting one

    /// <summary>Each hour, a small chance of a major incident (more on icy or foggy days).</summary>
    private void UpdateIncidents(double dt)
    {
        if (_incident is not null)
        {
            UpdateIncident(_incident, dt);
        }

        if (Now < _incidentCheckedTo || Now - _incidentCheckedTo > TimeSpan.FromDays(1))
        {
            _incidentCheckedTo = Now;
        }

        if (Now - _incidentCheckedTo < TimeSpan.FromHours(1))
        {
            return;
        }

        _incidentCheckedTo = Now;
        if (ScriptedIncidentDue() is { } scripted)
        {
            _scriptedIncidentDone = true;
            StartIncident(scripted);
            return;
        }

        if (!IncidentsPossible || ActiveIncident is not null || Now < _noIncidentBefore || !IsEmergencyOpen || !HasResus)
        {
            return;
        }

        var kinds = PossibleIncidents().ToList();
        if (kinds.Count > 0 && _incidentRandom.NextDouble() < IncidentChancePerHour())
        {
            var kind = _incidentRandom.PickWeighted(kinds, k => k.Weight).Kind;
            StartIncident(kind.Id);
        }
    }

    /// <summary>The chance of a major incident starting in the next hour: bad weather makes some kinds likelier, and so incidents in general.</summary>
    public double IncidentChancePerHour()
    {
        var kinds = PossibleIncidents().ToList();
        var usual = kinds.Sum(k => k.Kind.Weight);
        var today = kinds.Sum(k => k.Weight);
        return kinds.Count == 0 ? 0 : today / Math.Max(usual, 0.0001) / (DaysBetweenIncidents * 24);
    }

    private bool HasResus => UsableSpaces.Any(s => s.Kind == CareSpaceKind.Resus);

    /// <summary>Big incidents only on Realistic, and in a career from level 7.</summary>
    private bool BigIncidentsAllowed => MajorIncidents == MajorIncidentFrequency.Realistic && (!IsCareer || Level >= 7);

    private IEnumerable<(IncidentDefinition Kind, double Weight)> PossibleIncidents()
    {
        var weather = WeatherOn(Now.Date)?.EffectKeys().ToList() ?? new List<string>();
        foreach (var kind in Database.Incidents.Values.OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            if ((kind.Big && !BigIncidentsAllowed) || (kind.Months.Count > 0 && !kind.Months.Contains(Now.Month)))
            {
                continue;
            }

            yield return (kind, kind.Weight * weather.Aggregate(1.0, (w, k) => w * kind.Weather.GetValueOrDefault(k, 1)));
        }
    }

    /// <summary>Ambulance control declares a major incident: the casualties are on their way.</summary>
    public MajorIncident? StartIncident(string? incidentId = null)
    {
        var kind = incidentId is not null && Database.Incidents.TryGetValue(incidentId, out var k)
            ? k
            : Database.Incidents.Values.OrderBy(i => i.Id, StringComparer.Ordinal).FirstOrDefault();
        if (kind is null)
        {
            return null;
        }

        if (ActiveIncident is { } running)
        {
            EndIncident(running);
        }

        var incident = new MajorIncident
        {
            Id = _nextIncidentId++,
            IncidentId = kind.Id,
            Name = kind.Name,
            Location = _incidentRandom.Pick(kind.Locations),
            DeclaredAt = Now,
        };

        // Each casualty in their own ambulance a few minutes apart, the sickest first; the walking wounded later.
        var first = Now.AddMinutes(_incidentRandom.Range(15, 30));
        var list = new List<IncidentCasualty>();
        foreach (var c in kind.Casualties)
        {
            var count = _incidentRandom.Range(c.Count[0], c.Count[1]);
            for (var i = 0; i < count; i++)
            {
                list.Add(new IncidentCasualty
                {
                    Label = c.Label,
                    ConditionId = _incidentRandom.Pick(c.Conditions),
                    Age = _incidentRandom.Range(c.Age[0], c.Age[1]),
                    Triage = c.Triage switch { "red" => TriageCategory.Red, "green" => TriageCategory.Green, _ => TriageCategory.Amber },
                    Transport = c.Transport,
                    FireCrew = c.FireCrew,
                });
            }
        }

        var at = first;
        var number = 1;
        foreach (var c in list.OrderBy(c => c.Transport == CasualtyTransport.Walking).ThenBy(c => c.Triage).ThenBy(_ => _incidentRandom.NextDouble()).ToList())
        {
            c.Number = number++;
            c.ArriveAt = c.Transport == CasualtyTransport.Walking ? at.AddMinutes(_incidentRandom.Range(5, 15)) : at;
            if (c.Transport != CasualtyTransport.Walking)
            {
                at = at.AddMinutes(_incidentRandom.Range(2, 7));
            }

            incident.Casualties.Add(c);
        }

        _incident = incident;
        _noIncidentBefore = Now.AddDays(3);
        var minutes = (int)Math.Round((first - Now).TotalMinutes);
        var text = $"Major incident standby: {LowerFirst(kind.Name)}, {incident.Location}. {incident.Casualties.Count} casualties "
                   + $"({incident.Count(TriageCategory.Red)} red, {incident.Count(TriageCategory.Amber)} amber, {incident.Count(TriageCategory.Green)} green), first in {minutes} minutes.";
        Notifications.Add(Now, NotificationLevel.Critical, text);
        Events.Publish(new Announcement(AnnouncementKind.General, "Major incident standby. Major incident standby. All staff to their areas.", new[] { "major_incident_standby" }));
        Events.Publish(new MajorIncidentDeclared(incident));
        return incident;
    }

    // ---------------------------------------------------------------- while it is on

    private void UpdateIncident(MajorIncident incident, double dt)
    {
        if (!incident.IsActive)
        {
            return;
        }

        foreach (var casualty in incident.Casualties.Where(c => c.PatientId is null && Now >= c.ArriveAt))
        {
            BringInCasualty(incident, casualty);
        }

        PayOvertime(incident, dt);
        UpdateTraumaTeam(incident);

        // Stand down once everyone is here and has left A&E.
        if (incident.Casualties.All(c => c.PatientId is { } id && (FindPatient(id) is not { } p || !p.IsInDepartment || p.LeftEmergencyAt is not null)))
        {
            EndIncident(incident);
        }
    }

    private void BringInCasualty(MajorIncident incident, IncidentCasualty casualty)
    {
        if (casualty.Transport == CasualtyTransport.Air && Helipad is null && !casualty.ByRoad)
        {
            // Nowhere to land at the hospital: it puts down in a field nearby and a road ambulance does the rest.
            casualty.ByRoad = true;
            casualty.ArriveAt = Now + NoHelipadDelay;
            Notifications.Add(Now, NotificationLevel.Warning, $"The air ambulance has no helipad here: it has landed nearby and casualty {casualty.Number} is coming by road, about {NoHelipadDelay.TotalMinutes:0} minutes. Build a helipad (A&E in the build bar) so it can land at the hospital.");
            return;
        }

        var method = casualty.Transport == CasualtyTransport.Walking ? ArrivalMethod.WalkIn : ArrivalMethod.Ambulance;
        var conditionId = Database.Conditions.ContainsKey(casualty.ConditionId) ? casualty.ConditionId : null;
        var patient = _generator.Generate(Now, method, conditionId, casualty.Age);
        casualty.PatientId = patient.Id;
        patient.FireCrewEscort = casualty.FireCrew;
        patient.Log(Now, $"Casualty {casualty.Number} of the {LowerFirst(incident.Name)} at {incident.Location} ({ColourWord(casualty.Triage)} at the scene).", important: true);
        if (casualty.Transport == CasualtyTransport.Air)
        {
            if (Helipad is not null)
            {
                ArriveByAir(patient);
                return;
            }

            patient.AirAmbulance = true;
            patient.Log(Now, "The air ambulance landed nearby (the hospital has no helipad); a road ambulance brought them the rest of the way.");
        }

        if (method == ArrivalMethod.WalkIn && TryArriveByCar(patient))
        {
            return;
        }

        AdmitNewPatient(patient);
    }

    private static string ColourWord(TriageCategory colour) => colour switch { TriageCategory.Red => "red", TriageCategory.Green => "green", _ => "amber" };

    /// <summary>Called-in staff are paid time and a half for every hour until they go home.</summary>
    private void PayOvertime(MajorIncident incident, double dt)
    {
        foreach (var id in incident.CalledInStaffIds)
        {
            if (FindStaff(id) is { IsOnDuty: true } member)
            {
                var cost = member.Salary / HoursPerYear * OvertimeRate * dt / 3600.0;
                incident.OvertimeCost += cost;
                _unpaidOvertime += cost;
            }
        }

        if (_unpaidOvertime >= 10)
        {
            var due = (long)Math.Floor(_unpaidOvertime);
            _unpaidOvertime -= due;
            Book(MoneyCategory.Staff, -due);
        }
    }

    private void EndIncident(MajorIncident incident)
    {
        incident.StoodDownAt = Now;
        foreach (var id in incident.TraumaTeamIds.ToList())
        {
            if (FindStaff(id) is { } member)
            {
                LeaveTraumaTeam(member);
            }
        }

        incident.TraumaTeamIds.Clear();
        _incidentPriority.Clear();

        var casualties = incident.Casualties.Select(c => (Casualty: c, Patient: c.PatientId is { } id ? FindPatient(id) : null)).ToList();
        var survived = casualties.Count(c => c.Patient is null || (c.Patient.IsAlive && c.Patient.Outcome != PatientOutcome.Died));
        var red = casualties.Where(c => c.Casualty.Triage == TriageCategory.Red && c.Patient is not null).ToList();
        double? redWait = red.Count == 0 ? null : red.Max(c => ((c.Patient!.FirstSeenByDoctorAt ?? Now) - c.Patient.ArrivedAt).TotalMinutes);
        var ready = incident.TraumaTeamReadyAt is { } r && r <= incident.FirstArrival;
        var stars = 5 - (incident.Casualties.Count - survived) * 2 - (redWait > 15 ? 1 : 0) - (redWait > 30 ? 1 : 0) + (ready ? 0 : -1);
        stars = Math.Clamp(stars, 1, 5);
        var verdict = stars >= 5 ? "Well handled." : stars >= 4 ? "Handled well." : stars >= 3 ? "Got through it." : "A hard one.";
        var notes = new List<string> { verdict };
        notes.Add(ready ? "The trauma team was ready before the first ambulance." : incident.TraumaTeamCalledAt is null ? "The trauma team wasn't called to Resus." : "The trauma team wasn't all in Resus before the first ambulance.");
        if (incident.OvertimeCost >= 1)
        {
            notes.Add($"Overtime cost £{incident.OvertimeCost:N0}.");
        }

        incident.Debrief = new IncidentDebrief
        {
            Survived = survived,
            Total = incident.Casualties.Count,
            RedToDoctorMinutes = redWait,
            OvertimeCost = (long)Math.Round(incident.OvertimeCost),
            Stars = stars,
            Verdict = string.Join(" ", notes),
        };

        Tally(StatKeys.IncidentsHandled);
        Tally(StatKeys.IncidentSurvivors, survived);
        Notifications.Add(Now, NotificationLevel.Info, $"Major incident stood down: {survived} of {incident.Casualties.Count} survived. {verdict}");
        Events.Publish(new Announcement(AnnouncementKind.General, "Major incident stand down. Major incident stand down. Thank you, everyone.", new[] { "major_incident_stand_down" }));

        // The called-in staff go home once they are free (unless it is their shift now).
        incident.CalledInStaffIds.Clear();
        Events.Publish(new MajorIncidentStoodDown(incident));
    }

    // ---------------------------------------------------------------- get-ready actions

    /// <summary>The five get-ready actions as the panel shows them, with what each would do now.</summary>
    public IReadOnlyList<IncidentActionInfo> IncidentActionsNow()
    {
        var incident = ActiveIncident;
        bool Done(string id) => incident?.ActionsDone.Contains(id) == true;
        var lists = CancellablePlanned().Count();
        var offDuty = CallableStaff().Count();
        var fit = FitToGoEarly().Count();
        var waiting = AwaitingBedInEmergency().Count();
        return new[]
        {
            new IncidentActionInfo(CancelListsAction, lists > 0 ? $"Cancel today's planned operations ({lists} on the list, costs reputation)" : "Cancel today's planned operations (none left today)", "Cancel", Done(CancelListsAction), incident is not null && lists > 0),
            new IncidentActionInfo(CallInStaffAction, $"Call in off-duty staff ({Math.Min(offDuty, MaxCalledIn)} available, overtime ×1.5)", "Call in", Done(CallInStaffAction), incident is not null && offDuty > 0),
            new IncidentActionInfo(DischargeEarlyAction, $"Discharge fit patients early ({fit} ready to go)", "Discharge", Done(DischargeEarlyAction), incident is not null && fit > 0),
            new IncidentActionInfo(ClearResusAction, $"Clear Resus and majors: patients waiting for a bed go up first ({waiting} waiting)", "Clear", Done(ClearResusAction), incident is not null && waiting > 0),
            new IncidentActionInfo(TraumaTeamAction, "Trauma team to Resus (tannoy)", "Call", Done(TraumaTeamAction), incident is not null && HasResus),
        };
    }

    /// <summary>Takes a get-ready action; returns what happened.</summary>
    public string DoIncidentAction(string action)
    {
        if (ActiveIncident is not { } incident)
        {
            return "There is no major incident on.";
        }

        if (incident.ActionsDone.Contains(action))
        {
            return "Already done.";
        }

        var result = action switch
        {
            CancelListsAction => CancelTodaysLists(),
            CallInStaffAction => CallInStaff(incident),
            DischargeEarlyAction => DischargeEarly(),
            ClearResusAction => ClearForIncident(),
            TraumaTeamAction => CallTraumaTeam(incident),
            _ => null,
        };
        if (result is null)
        {
            return "Nothing to do.";
        }

        incident.ActionsDone.Add(action);
        Notifications.Add(Now, NotificationLevel.Info, result);
        return result;
    }

    private IEnumerable<PlannedBooking> CancellablePlanned() =>
        _plannedBookings.Where(b => b.ArrivesAt.Date == Now.Date && b.Status is PlannedStatus.Booked or PlannedStatus.Admitted
                                    && !(b.PatientId is { } id && _tasks.Values.Any(t => t.PatientId == id && t.Kind == TaskKind.Surgery && t.AssignedStaffId is not null)));

    private string? CancelTodaysLists()
    {
        var bookings = CancellablePlanned().ToList();
        if (bookings.Count == 0)
        {
            return null;
        }

        foreach (var booking in bookings)
        {
            CancelPlanned(booking, Database.Conditions[booking.ConditionId], "cancelled for the major incident", booking.PatientId is { } id ? FindPatient(id) : null);
        }

        return $"Cancelled {bookings.Count} planned operation{(bookings.Count == 1 ? "" : "s")} for the major incident.";
    }

    /// <summary>Off-duty clinical staff who could come in: not on this shift, at home.</summary>
    private IEnumerable<StaffMember> CallableStaff() =>
        _staff.Where(s => s.Shift != CurrentShift && s.Activity == StaffActivity.OffDuty && !IsPlayer(s)
                          && s.Role is StaffRole.Doctor or StaffRole.Nurse or StaffRole.HealthcareAssistant or StaffRole.Porter or StaffRole.Radiographer
                              or StaffRole.Anaesthetist or StaffRole.Surgeon or StaffRole.OutreachNurse)
            .OrderBy(s => s.Role switch { StaffRole.Doctor => 0, StaffRole.Nurse => 1, StaffRole.Anaesthetist => 2, StaffRole.Radiographer => 3, StaffRole.Surgeon => 4, _ => 5 })
            .ThenBy(s => s.Id);

    private string? CallInStaff(MajorIncident incident)
    {
        var coming = CallableStaff().Take(MaxCalledIn).ToList();
        if (coming.Count == 0)
        {
            return null;
        }

        foreach (var member in coming)
        {
            incident.CalledInStaffIds.Add(member.Id);
            StartShift(member);
            member.StatusText = "Called in for the major incident";
        }

        return $"Called in {coming.Count} off-duty staff for the major incident, on overtime.";
    }

    /// <summary>Ward patients due home in the next day who are well: they can go now.</summary>
    private IEnumerable<Patient> FitToGoEarly() =>
        _patients.Values.Where(p => p.Stage == PatientStage.OnWard && p.IsAlive && !p.PreOp && p.DischargeDueAt is { } due && due > Now
                                    && due - Now < TimeSpan.FromHours(24) && p.LiveNews2 <= 2 && p.MedicinesReadyAt is null);

    private string? DischargeEarly()
    {
        var going = FitToGoEarly().ToList();
        if (going.Count == 0)
        {
            return null;
        }

        foreach (var patient in going)
        {
            patient.DischargeDueAt = Now;
            patient.Log(Now, "Discharged early to free a bed for the major incident.");
        }

        return $"{going.Count} patient{(going.Count == 1 ? " is" : "s are")} being discharged early to free beds.";
    }

    private IEnumerable<Patient> AwaitingBedInEmergency() =>
        _patients.Values.Where(p => p.Stage == PatientStage.AwaitingBed && p.IsAlive && p.LeftEmergencyAt is null);

    private string? ClearForIncident()
    {
        var waiting = AwaitingBedInEmergency().ToList();
        if (waiting.Count == 0)
        {
            return null;
        }

        foreach (var patient in waiting)
        {
            _incidentPriority.Add(patient.Id);
        }

        return $"{waiting.Count} patient{(waiting.Count == 1 ? "" : "s")} in Resus and majors waiting for a bed will go up to the wards first.";
    }

    /// <summary>Patients cleared out of A&amp;E for the major incident go up first.</summary>
    internal bool HasIncidentPriority(Patient patient) => _incidentPriority.Contains(patient.Id);

    // ---------------------------------------------------------------- the trauma team

    private bool CanJoinTraumaTeam(StaffMember s) =>
        !IsPlayer(s) && s.IsOnDuty && !s.IsOnBreak && s.CrashPatientId is null && s.AssistingTaskId is null && s.PushingPatientId is null
        && s.BedsidePatientId is null && !IsClinicStaff(s)
        && !(s.CurrentTaskId is { } t && _tasks.TryGetValue(t, out var task) && task.Kind == TaskKind.Surgery && task.Started);

    /// <summary>"Trauma team to Resus": a senior doctor, an anaesthetist, two nurses and a radiographer wait in Resus.</summary>
    private string? CallTraumaTeam(MajorIncident incident)
    {
        var resus = UsableSpaces.Where(s => s.Kind == CareSpaceKind.Resus).OrderBy(s => s.OccupantId is null ? 0 : 1).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        if (resus.Count == 0)
        {
            return null;
        }

        var pool = _staff.Where(CanJoinTraumaTeam).ToList();
        var team = new List<StaffMember>();
        void Take(Func<StaffMember, bool> fits, Func<StaffMember, double> order, int count = 1)
        {
            foreach (var m in pool.Where(fits).OrderBy(order).ThenBy(s => s.Id).Take(count).ToList())
            {
                team.Add(m);
                pool.Remove(m);
            }
        }

        Take(s => s.Role is StaffRole.Doctor, s => s.Grade is "Consultant" ? 0 : s.Grade is "Registrar" ? 1 : 2);
        Take(s => s.Role is StaffRole.Anaesthetist, _ => 0);
        Take(s => s.Role is StaffRole.Nurse, _ => 0, 2);

        // A radiographer only when there is another to keep X-ray going.
        if (_staff.Count(s => s.Role == StaffRole.Radiographer && s.IsOnDuty) > 1)
        {
            Take(s => s.Role is StaffRole.Radiographer, _ => 0);
        }

        if (team.Count == 0)
        {
            return null;
        }

        var spots = resus.SelectMany(r => r.Rect.Tiles())
            .Where(t => Nav.IsWalkable(t) && resus.All(r => r.PatientSpot != t && r.StaffSpot != t))
            .ToList();
        incident.TraumaTeamCalledAt = Now;
        for (var i = 0; i < team.Count; i++)
        {
            var member = team[i];
            if (member.CurrentTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task))
            {
                ReleaseFromTask(member, task);
            }

            incident.TraumaTeamIds.Add(member.Id);
            member.PagedUntil = null;
            member.Activity = StaffActivity.Working;
            member.StatusText = "Trauma team: waiting in Resus for the major incident";
            WalkTo(member, spots.Count > 0 ? spots[i * 3 % spots.Count] : resus[0].StaffSpot);
        }

        var place = PlaceName(resus[0]);
        Events.Publish(new Announcement(AnnouncementKind.TraumaCall, $"Trauma team to {place.Spoken}.", new[] { $"trauma_{place.Key}" }));
        var names = string.Join(", ", team.Select(m => m.Name));
        return $"Trauma team called to Resus: {names}.";
    }

    /// <summary>The team stays in Resus until the first casualty is there (then they get to work), or it stands down.</summary>
    private void UpdateTraumaTeam(MajorIncident incident)
    {
        if (incident.TraumaTeamIds.Count == 0)
        {
            return;
        }

        var team = incident.TraumaTeamIds.Select(FindStaff).ToList();
        if (incident.TraumaTeamReadyAt is null && team.All(m => m is null || (!m.IsMoving && InResus(m.Tile))))
        {
            incident.TraumaTeamReadyAt = Now;
        }

        var arrived = incident.Casualties.Any(c => c.PatientId is { } id && FindPatient(id) is { } p && p.CareSpaceId is { } sid && FindSpace(sid) is { Kind: CareSpaceKind.Resus });
        var tooLong = incident.TraumaTeamCalledAt is { } called && Now - called > TimeSpan.FromHours(2);
        if (!arrived && !tooLong)
        {
            return;
        }

        foreach (var member in team.OfType<StaffMember>())
        {
            LeaveTraumaTeam(member);
        }

        incident.TraumaTeamIds.Clear();
    }

    private bool InResus(GridCoord tile) => _spaces.Values.Any(s => s.Kind == CareSpaceKind.Resus && s.Rect.Contains(tile));

    private void LeaveTraumaTeam(StaffMember member)
    {
        if (member.Activity == StaffActivity.Working && member.CurrentTaskId is null && member.CrashPatientId is null)
        {
            member.Activity = StaffActivity.Idle;
            member.StatusText = "Available";
        }
    }

    /// <summary>A trauma team member waiting in Resus does nothing else (a cardiac arrest still takes them).</summary>
    private bool UpdateTraumaResponder(StaffMember member)
    {
        if (!OnTraumaStandby(member))
        {
            return false;
        }

        if (member.CrashPatientId is not null || !member.IsOnDuty)
        {
            _incident!.TraumaTeamIds.Remove(member.Id);
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- saving

    internal void RestoreIncident(MajorIncident? incident, int nextId, DateTime? noneBefore)
    {
        _incident = incident;
        _nextIncidentId = Math.Max(nextId, (incident?.Id ?? 0) + 1);
        if (noneBefore is { } t)
        {
            _noIncidentBefore = t;
        }

        if (incident is { IsActive: true })
        {
            foreach (var member in incident.TraumaTeamIds.Select(FindStaff).OfType<StaffMember>())
            {
                member.Activity = StaffActivity.Working;
                member.StatusText = "Trauma team: waiting in Resus for the major incident";
            }
        }
    }

    internal (MajorIncident? Incident, int NextId, DateTime NoneBefore) SaveIncident() => (_incident, _nextIncidentId, _noIncidentBefore);

    // ---------------------------------------------------------------- cheat

    private string IncidentCheat(string kind)
    {
        if (kind is "list" or "")
        {
            return "Incidents: " + string.Join(", ", Database.Incidents.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        if (kind == "end")
        {
            if (ActiveIncident is { } on)
            {
                EndIncident(on);
                return "Major incident stood down.";
            }

            return "There is no major incident on.";
        }

        if (!Database.Incidents.ContainsKey(kind))
        {
            return $"Unknown incident '{kind}'. Try: incident list";
        }

        var incident = StartIncident(kind)!;
        return $"Major incident: {incident.Name}, {incident.Casualties.Count} casualties, first in {(incident.FirstArrival - Now).TotalMinutes:0} minutes.";
    }
}
