using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>What each member of the crash team is there to do.</summary>
public enum CrashRole
{
    /// <summary>First on the scene: starts chest compressions.</summary>
    Compressions,

    /// <summary>Fetches the nearest resus trolley (defibrillator and drugs) and pushes it to the bed.</summary>
    Trolley,

    /// <summary>A doctor runs the arrest.</summary>
    Leader,

    /// <summary>An anaesthetist manages the airway.</summary>
    Airway,

    /// <summary>A second nurse takes turns with the compressions.</summary>
    SecondNurse,
}

public enum ArrestOutcome
{
    InProgress,

    /// <summary>Return of spontaneous circulation: the heart restarted.</summary>
    Rosc,

    Died,
}

/// <summary>One member of a crash team.</summary>
public sealed class CrashResponder
{
    public int StaffId { get; init; }

    public CrashRole Role { get; init; }

    public DateTime CalledAt { get; init; }

    public DateTime? ArrivedAt { get; set; }

    /// <summary>Where they stand round the bed.</summary>
    public GridCoord Spot { get; set; }
}

/// <summary>A cardiac arrest, from the moment the heart stops until it restarts or the team stop.</summary>
public sealed class CardiacArrest
{
    public int PatientId { get; init; }

    public DateTime StartedAt { get; init; }

    public ArrestRhythm Rhythm { get; set; }

    /// <summary>Where it happened, for the crash panel ("Ward 2, Bed 4").</summary>
    public string Place { get; init; } = string.Empty;

    /// <summary>Someone was there (or a monitor alarmed) when it happened.</summary>
    public bool Witnessed { get; init; }

    /// <summary>When someone noticed and put out the crash call. Unwitnessed arrests on a ward wait to be found.</summary>
    public DateTime CrashCallAt { get; init; }

    public bool CrashCalled { get; set; }

    public DateTime? CprStartedAt { get; set; }

    public DateTime? TrolleyArrivedAt { get; set; }

    public DateTime? LeaderArrivedAt { get; set; }

    public DateTime? AirwayArrivedAt { get; set; }

    public DateTime? FirstShockAt { get; set; }

    public DateTime? LastShockAt { get; set; }

    public int Shocks { get; set; }

    public int? TrolleyId { get; set; }

    public List<CrashResponder> Team { get; init; } = new();

    public ArrestOutcome Outcome { get; set; }

    public DateTime? EndedAt { get; set; }

    /// <summary>The rough chance of them leaving hospital alive, as the crash panel shows it.</summary>
    public double Chance { get; set; }

    public bool IsActive => Outcome == ArrestOutcome.InProgress;

    public double MinutesSince(DateTime now) => ((EndedAt ?? now) - StartedAt).TotalMinutes;

    /// <summary>Minutes with nobody doing CPR so far (all of it, until someone starts).</summary>
    public double NoFlowMinutes(DateTime now) => ((CprStartedAt ?? EndedAt ?? now) - StartedAt).TotalMinutes;
}

/// <summary>
/// A resuscitation trolley (defibrillator, drugs and airway kit). Every resus bay and ward starts with one; the
/// player can move them anywhere and buy more. In an arrest the nearest is pushed to the bed, then back again.
/// </summary>
public sealed class ResusTrolley
{
    public int Id { get; init; }

    /// <summary>The room it came with (null for one the player bought).</summary>
    public string? HomeRoomId { get; init; }

    /// <summary>Which of that room's trolleys it is.</summary>
    public int HomeIndex { get; init; }

    /// <summary>Where it is kept.</summary>
    public GridCoord Home { get; set; }

    /// <summary>The player has moved it from where the room put it.</summary>
    public bool Moved { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double PreviousX { get; set; }

    public double PreviousY { get; set; }

    /// <summary>Which way it faces (the way it was last pushed).</summary>
    public (double X, double Y) Facing { get; set; } = (0, 1);

    public int? PushedByStaffId { get; set; }

    /// <summary>Taken to an arrest: it stays by the bed until it is pushed home.</summary>
    public int? AtArrestPatientId { get; set; }

    public GridCoord Tile => new((int)Math.Floor(X), (int)Math.Floor(Y));

    public bool IsHome => !PushedByStaffId.HasValue && AtArrestPatientId is null && Tile == Home;
}

public sealed record CrashCallStarted(CardiacArrest Arrest) : IGameEvent;

public sealed record CrashCallEnded(CardiacArrest Arrest) : IGameEvent;

/// <summary>
/// Cardiac arrests and the crash team. When a patient's heart stops (their illness has gone too far, or a sudden
/// emergency), a crash call goes out: the nearest nurse or healthcare assistant runs to start CPR, another fetches
/// the nearest resus trolley, the nearest doctor leads and an anaesthetist (if there is one) takes the airway.
/// Whether the heart restarts depends on how quickly CPR started, how quickly the defibrillator got there (for a
/// shockable rhythm) and the team (see <see cref="ResusOdds"/>). If it does they go to intensive care; if not, the
/// team stop after about 25 minutes. Arrests on a monitor (resus, ICU, HDU, a red monitor) or with someone at
/// the bedside are seen at once; on a ward they may not be found for a few minutes.
/// </summary>
public sealed partial class HospitalSimulation
{
    public const string ResusTrolleyItem = "resus_trolley";

    /// <summary>Crash team members run.</summary>
    public const double RunSpeed = Agent.DefaultWalkSpeed * 2.0;

    /// <summary>The team keep a patient whose heart has restarted stable for this long.</summary>
    public static readonly TimeSpan StabilisedAfterRosc = TimeSpan.FromHours(3);

    /// <summary>Severity after the heart restarts: sick enough for intensive care.</summary>
    public const double SeverityAfterRosc = 0.82;

    private readonly List<CardiacArrest> _arrests = new();
    private readonly List<ResusTrolley> _trolleys = new();
    private int _nextTrolleyId = 1;
    private int _trolleySyncCountdown;

    /// <summary>Arrests going on now, and the last few that ended (for the crash panel).</summary>
    public IReadOnlyList<CardiacArrest> Arrests => _arrests;

    public IEnumerable<CardiacArrest> ActiveArrests => _arrests.Where(a => a.IsActive);

    public IReadOnlyList<ResusTrolley> ResusTrolleys => _trolleys;

    public CardiacArrest? ArrestOf(Patient patient) => _arrests.LastOrDefault(a => a.PatientId == patient.Id && a.IsActive);

    public CardiacArrest? ArrestOf(int patientId) => _arrests.LastOrDefault(a => a.PatientId == patientId && a.IsActive);

    public int ResusTrolleyCost => Database.Furniture.TryGetValue(ResusTrolleyItem, out var t) ? t.Cost : 3_800;

    public ResusTrolley? TrolleyAt(GridCoord tile) => _trolleys.FirstOrDefault(t => t.Tile == tile);

    // ---------------------------------------------------------------- starting an arrest

    /// <summary>Patients lying in a bed, cubicle, bay, trolley or corridor space: anyone else who dies goes straight away.</summary>
    private static bool CanBeResuscitated(Patient patient) =>
        patient.IsInDepartment && (patient.CareSpaceId is not null || patient.CorridorSpot is not null || patient.Stage == PatientStage.OnWard);

    /// <summary>The patient's heart stops. Starts the arrest and (once someone notices) the crash call.</summary>
    public CardiacArrest StartArrest(Patient patient, string? cause = null)
    {
        if (ArrestOf(patient) is { } already)
        {
            return already;
        }

        // Whatever they were doing stops: the trolley they were on stops where it is.
        if (patient.PushedByStaffId is not null)
        {
            EndPush(patient);
        }

        if (patient.IsMoving)
        {
            patient.StopMoving();
        }

        foreach (var task in _tasks.Values.Where(t => t.PatientId == patient.Id && t.AssignedStaffId is not null).ToList())
        {
            if (task.Kind is TaskKind.StayWithPatient)
            {
                continue;
            }

            if (FindStaff(task.AssignedStaffId!.Value) is { } member && member.CurrentTaskId == task.Id)
            {
                ReleaseFromTask(member, task);
            }
        }

        patient.BusyWithTaskId = null;
        patient.ArrestStartedAt = Now;
        patient.ArrestCount++;
        patient.Severity = 1.0;

        var cardiac = patient.Condition.Department is "cardiology" || patient.Condition.Id.Contains("myocardial", StringComparison.Ordinal);
        var rhythm = Random.Chance(cardiac ? ResusOdds.ShockableShareCardiac : ResusOdds.ShockableShare) ? ArrestRhythm.Shockable : ArrestRhythm.NonShockable;
        var witnessed = IsWitnessed(patient);
        var delay = witnessed ? 0 : Random.Range(30, 240) / (1.0 + (0.5 * NursesNear(patient)));
        var arrest = new CardiacArrest
        {
            PatientId = patient.Id,
            StartedAt = Now,
            Rhythm = rhythm,
            Place = ArrestPlace(patient).Display,
            Witnessed = witnessed,
            CrashCallAt = Now.AddSeconds(Math.Round(delay)),
        };
        _arrests.Add(arrest);
        Stats.CardiacArrests++;
        patient.Log(Now, $"Cardiac arrest{(cause is null ? string.Empty : $" ({cause})")} at {arrest.Place}.{(witnessed ? string.Empty : " Nobody was with them.")}", important: true);
        if (witnessed)
        {
            PutOutCrashCall(arrest, patient);
        }

        return arrest;
    }

    /// <summary>Seen at once: on a monitor that alarms (resus, majors, ICU, HDU, theatre, a red monitor) or with someone close by.</summary>
    private bool IsWitnessed(Patient patient)
    {
        if (patient.MonitorIsRed || CriticalUnitOfPatient(patient) is not null || BedsideCarerFor(patient) is not null)
        {
            return true;
        }

        if (patient.CareSpaceId is { } id && FindSpace(id) is { } space && (space.IsTreatmentSpace || space.IsServiceRoom))
        {
            return true;
        }

        return _staff.Any(s => s.IsOnDuty && !s.IsOnBreak && Site.FloorOf(s.Tile) == Site.FloorOf(patient.Tile) && Chebyshev(s.Tile, patient.Tile) <= 5);
    }

    /// <summary>Nurses and healthcare assistants on the same ward: the more there are, the sooner someone finds them.</summary>
    private int NursesNear(Patient patient)
    {
        var room = patient.CareSpaceId is { } id ? FindSpace(id) : null;
        return _staff.Count(s => s.IsOnDuty && !s.IsOnBreak && s.Role is StaffRole.Nurse or StaffRole.HealthcareAssistant or StaffRole.Midwife
            && (room is not null ? room.Rect.Contains(s.Tile) : Chebyshev(s.Tile, patient.Tile) <= 12));
    }

    private static int Chebyshev(GridCoord a, GridCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void PutOutCrashCall(CardiacArrest arrest, Patient patient)
    {
        arrest.CrashCalled = true;
        var place = ArrestPlace(patient);
        var adult = patient.Age >= 16;
        var who = patient.Maternity ? "Obstetric emergency" : adult ? "Adult cardiac arrest" : "Paediatric cardiac arrest";
        Notifications.Add(Now, NotificationLevel.Critical, $"Crash call: {who.ToLowerInvariant()}, {arrest.Place}. {Describe(patient)}.", patient.Id, patient.CareSpaceId, patient.Triage);
        patient.Log(Now, arrest.Witnessed ? "Crash call put out." : $"Found unresponsive {Minutes(Now - arrest.StartedAt)} later. Crash call put out.", important: true);
        Events.Publish(new Announcement(AnnouncementKind.CardiacArrest, $"{who}, {place.Spoken}.", new[] { place.Clip(patient) }, patient.Id));
        Events.Publish(new CrashCallStarted(arrest));
        CallCrashTeam(arrest, patient);
    }

    private static string Minutes(TimeSpan t) => t.TotalMinutes < 1.5 ? "a minute" : $"{t.TotalMinutes:0} minutes";

    /// <summary>Where the arrest is: for the crash panel, the tannoy and the recorded line.</summary>
    internal ArrestPlaceName ArrestPlace(Patient patient)
    {
        var space = patient.CareSpaceId is { } id ? FindSpace(id) : null;
        if (patient.CorridorSpot is not null && (space is null || !space.IsTreatmentSpace))
        {
            return new ArrestPlaceName("A&E corridor", "A and E", "ae", null);
        }

        if (space is null)
        {
            return new ArrestPlaceName("the hospital", "the hospital", "x", null);
        }

        if (space.IsTreatmentSpace)
        {
            var (spoken, key) = PlaceName(space);
            return new ArrestPlaceName(space.Name, spoken, key, null);
        }

        if (space.IsWard)
        {
            var bed = patient.BedIndex is { } i ? $", Bed {i + 1}" : string.Empty;
            var template = space.TemplateId;
            var spoken = Database.RoomTemplates.TryGetValue(template, out var t) ? t.Name : space.Name;
            return new ArrestPlaceName(space.Name + bed, spoken, template, template);
        }

        return new ArrestPlaceName(space.Name, space.Name, space.Kind.ToString().ToLowerInvariant(), null);
    }

    // ---------------------------------------------------------------- the crash team

    /// <summary>Clinical staff who can be called away to an arrest right now.</summary>
    private bool CanRespond(StaffMember s, Patient patient) =>
        s.IsOnDuty && !s.IsOnBreak && s.CrashPatientId is null && s.AssistingTaskId is null && s.PushingPatientId is null
        && (s.BedsidePatientId is null || s.BedsidePatientId == patient.Id)
        && s.Role is StaffRole.Nurse or StaffRole.HealthcareAssistant or StaffRole.Midwife or StaffRole.OutreachNurse or StaffRole.Doctor or StaffRole.Anaesthetist
        && !IsClinicStaff(s)
        && !(s.CurrentTaskId is { } t && _tasks.TryGetValue(t, out var task) && task.Kind == TaskKind.Surgery && task.Started);

    private static bool IsNurseLike(StaffMember s) => s.Role is StaffRole.Nurse or StaffRole.HealthcareAssistant or StaffRole.Midwife or StaffRole.OutreachNurse;

    /// <summary>Walking distance in tiles (a long way when there is no route).</summary>
    private int DistanceTo(StaffMember s, GridCoord to)
    {
        if (s.Tile == to)
        {
            return 0;
        }

        var path = Nav.FindPath(s.Tile, to);
        return path.Count > 0 ? path.Count : 100_000;
    }

    /// <summary>Calls whoever is missing from the team: the first call, then again as people become free.</summary>
    private void CallCrashTeam(CardiacArrest arrest, Patient patient)
    {
        var target = patient.Tile;
        var pool = _staff.Where(s => CanRespond(s, patient)).ToList();
        var distance = new Dictionary<StaffMember, int>();
        int D(StaffMember s) => distance.TryGetValue(s, out var d) ? d : distance[s] = DistanceTo(s, target);

        bool Has(CrashRole role) => arrest.Team.Any(r => r.Role == role);

        void Call(StaffMember member, CrashRole role)
        {
            pool.Remove(member);
            var responder = new CrashResponder { StaffId = member.Id, Role = role, CalledAt = Now };
            arrest.Team.Add(responder);
            member.CrashPatientId = patient.Id;
            if (member.CurrentTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task))
            {
                ReleaseFromTask(member, task);
            }

            member.PagedUntil = null;
            member.WalkSpeed = RunSpeed;
            member.Activity = StaffActivity.Working;
            member.StatusText = role == CrashRole.Trolley ? $"Crash call: fetching the resus trolley for {arrest.Place}" : $"Crash call: running to {arrest.Place}";
            AssignCrashSpots(arrest, patient);
            if (role != CrashRole.Trolley)
            {
                WalkTo(member, responder.Spot);
            }
        }

        // Whoever is already sitting with them starts CPR at once.
        if (!Has(CrashRole.Compressions) && pool.FirstOrDefault(s => s.BedsidePatientId == patient.Id) is { } sitter)
        {
            Call(sitter, CrashRole.Compressions);
        }

        if (!Has(CrashRole.Compressions) && pool.OrderBy(D).ThenBy(s => s.Id).FirstOrDefault(s => D(s) < 100_000) is { } first)
        {
            Call(first, CrashRole.Compressions);
        }

        if (!Has(CrashRole.Leader) && pool.Where(s => s.Role == StaffRole.Doctor && D(s) < 100_000)
                .OrderBy(s => D(s) * (s.Grade is "Registrar" or "Consultant" ? 0.75 : 1.0)).ThenBy(s => s.Id).FirstOrDefault() is { } leader)
        {
            Call(leader, CrashRole.Leader);
        }

        if (!Has(CrashRole.Trolley) && arrest.TrolleyId is null && NearestFreeTrolley(target) is { } trolley)
        {
            var fetcher = pool.Where(s => IsNurseLike(s) && DistanceTo(s, trolley.Tile) < 100_000)
                .OrderBy(s => DistanceTo(s, trolley.Tile)).ThenBy(s => s.Id).FirstOrDefault();
            if (fetcher is not null)
            {
                arrest.TrolleyId = trolley.Id;
                trolley.AtArrestPatientId = patient.Id;
                Call(fetcher, CrashRole.Trolley);
                WalkTo(fetcher, trolley.Tile);
            }
        }

        if (!Has(CrashRole.Airway) && pool.Where(s => s.Role == StaffRole.Anaesthetist && D(s) < 100_000).OrderBy(D).ThenBy(s => s.Id).FirstOrDefault() is { } anaesthetist)
        {
            Call(anaesthetist, CrashRole.Airway);
        }

        if (!Has(CrashRole.SecondNurse) && pool.Where(s => IsNurseLike(s) && D(s) < 100_000).OrderBy(D).ThenBy(s => s.Id).FirstOrDefault() is { } second)
        {
            Call(second, CrashRole.SecondNurse);
        }
    }

    /// <summary>Lets go of the job they were doing, so someone (maybe them) can pick it up again later.</summary>
    private void ReleaseFromTask(StaffMember member, ClinicalTask task)
    {
        if (task.Kind == TaskKind.MovePatient && FindPatient(task.PatientId) is { } pushed && pushed.PushedByStaffId == member.Id)
        {
            EndPush(pushed);
        }

        task.AssignedStaffId = null;
        task.Started = false;
        if (FindPatient(task.PatientId) is { } patient && patient.BusyWithTaskId == task.Id)
        {
            patient.BusyWithTaskId = null;
        }

        if (task.SpaceId is { } spaceId && FindSpace(spaceId) is { } room && room.ReservedByStaffId == member.Id)
        {
            room.ReservedByStaffId = null;
        }

        member.CurrentTaskId = null;
        member.WorkRemainingSeconds = 0;
    }

    /// <summary>Round the bed: compressions beside the patient's chest, the others round about.</summary>
    private void AssignCrashSpots(CardiacArrest arrest, Patient patient)
    {
        var centre = patient.Tile;
        var beds = new HashSet<GridCoord>(_spaces.Values.SelectMany(s => s.Beds.Select(b => b.PatientSpot)).Concat(_spaces.Values.Select(s => s.PatientSpot)));
        var candidates = new List<GridCoord>();
        if (BedOf(patient) is not null || (patient.CareSpaceId is { } sid && FindSpace(sid) is { } sp && sp.PatientSpot == centre))
        {
            // Beside a bed: either side of the patient's chest, then by the head and foot.
            candidates.AddRange(new[] { (-2, 0), (2, 0), (-2, -1), (2, -1), (-2, 1), (2, 1), (0, 3), (-1, 3), (1, 3), (0, -3) }
                .Select(d => new GridCoord(centre.X + d.Item1, centre.Y + d.Item2)));
        }

        for (var r = 1; r <= 3; r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r)
                    {
                        candidates.Add(new GridCoord(centre.X + dx, centre.Y + dy));
                    }
                }
            }
        }

        var free = candidates.Distinct()
            .Where(c => c != centre && Nav.IsWalkable(c) && !beds.Contains(c) && Site.FloorOf(c) == Site.FloorOf(centre))
            .ToList();
        var used = new HashSet<GridCoord>();
        foreach (var responder in arrest.Team.OrderBy(r => r.Role))
        {
            var open = free.Where(c => !used.Contains(c)).ToList();
            responder.Spot = open.Count > 0 ? open[0] : free.Count > 0 ? free[0] : BesideTile(centre);
            used.Add(responder.Spot);
        }
    }

    // ---------------------------------------------------------------- each tick

    private void UpdateArrests()
    {
        if (--_trolleySyncCountdown <= 0)
        {
            SyncResusTrolleys();
            _trolleySyncCountdown = 10;
        }

        foreach (var trolley in _trolleys)
        {
            trolley.PreviousX = trolley.X;
            trolley.PreviousY = trolley.Y;
            if (trolley.PushedByStaffId is { } pusherId && FindStaff(pusherId) is { } pusher)
            {
                // Pushed in front of whoever has it.
                var (fx, fy) = pusher.Facing;
                trolley.X = pusher.X + (fx * 0.8);
                trolley.Y = pusher.Y + (fy * 0.8);
                if (pusher.IsMoving)
                {
                    trolley.Facing = (fx, fy);
                }
            }
        }

        foreach (var arrest in _arrests.Where(a => a.IsActive).ToList())
        {
            UpdateArrest(arrest);
        }

        // Keep the last few for the crash panel and the stats.
        while (_arrests.Count(a => !a.IsActive) > 20)
        {
            _arrests.Remove(_arrests.First(a => !a.IsActive));
        }

        foreach (var patient in _patients.Values.Where(p => p.PostArrestDeathAt is { } due && Now >= due && p.IsInDepartment && !p.InArrest).ToList())
        {
            patient.PostArrestDeathAt = null;
            var where = patient.Stage == PatientStage.OnWard && patient.CareSpaceId is { } wardId ? $"on {FindSpace(wardId)?.Name ?? "the ward"}" : "in A&E";
            PatientDied(patient, $"Despite intensive care, they did not recover from the cardiac arrest. Died peacefully {where} at {Now:HH:mm}.");
        }
    }

    private void UpdateArrest(CardiacArrest arrest)
    {
        var patient = FindPatient(arrest.PatientId);
        if (patient is null || !patient.IsInDepartment)
        {
            EndArrest(arrest, ArrestOutcome.Died, patient);
            return;
        }

        if (!arrest.CrashCalled)
        {
            if (Now >= arrest.CrashCallAt)
            {
                PutOutCrashCall(arrest, patient);
            }

            return;
        }

        var elapsed = (Now - arrest.StartedAt).TotalSeconds;
        if (elapsed % 30 < 1)
        {
            CallCrashTeam(arrest, patient);
        }

        // Who is here.
        var here = arrest.Team.Where(r => r.ArrivedAt is not null && FindStaff(r.StaffId) is { } s && s.CrashPatientId == patient.Id).ToList();
        var hands = here.Count(r => r.Role is CrashRole.Compressions or CrashRole.SecondNurse or CrashRole.Trolley || (r.Role == CrashRole.Leader && here.Count == 1));
        if (here.Count > 0 && arrest.CprStartedAt is null)
        {
            arrest.CprStartedAt = Now;
            var first = FindStaff(here[0].StaffId)!;
            Stats.RecordTimeToCpr((Now - arrest.StartedAt).TotalMinutes);
            patient.Log(Now, $"CPR started by {first.Name}.", important: true);
        }

        var leader = here.FirstOrDefault(r => r.Role == CrashRole.Leader) is { } l ? FindStaff(l.StaffId) : null;
        var airway = here.Any(r => r.Role == CrashRole.Airway);
        var trolleyHere = arrest.TrolleyArrivedAt is not null;
        var team = ResusOdds.TeamFactor(Math.Max(hands, here.Count > 0 ? 1 : 0), leader?.Skill, leader?.Fatigue ?? 0, airway);
        var age = ResusOdds.AgeFactor(patient.Age);
        var cprMinutes = arrest.CprStartedAt is { } c ? (Now - c).TotalMinutes : 0;
        var quality = ResusOdds.Quality(arrest.NoFlowMinutes(Now));
        arrest.Chance = ResusOdds.EstimateSurvival(arrest.Rhythm, cprMinutes, arrest.NoFlowMinutes(Now), arrest.CprStartedAt is not null, trolleyHere, team, age);

        if (arrest.CprStartedAt is null)
        {
            if ((Now - arrest.StartedAt).TotalMinutes >= ResusOdds.HopelessWithoutCprAfterMinutes)
            {
                patient.Log(Now, "Nobody was able to start CPR in time.", important: true);
                EndArrest(arrest, ArrestOutcome.Died, patient);
            }

            return;
        }

        // A shockable rhythm left too long without a shock decays to asystole.
        if (arrest.Rhythm == ArrestRhythm.Shockable && arrest.Shocks == 0 && (Now - arrest.StartedAt).TotalMinutes >= ResusOdds.ShockableDecaysAfterMinutes)
        {
            arrest.Rhythm = ArrestRhythm.NonShockable;
            patient.Log(Now, "Rhythm now asystole: no longer shockable.");
        }

        if (arrest.Rhythm == ArrestRhythm.Shockable && trolleyHere)
        {
            var due = arrest.LastShockAt is { } last ? last.AddMinutes(ResusOdds.MinutesBetweenShocks) : arrest.TrolleyArrivedAt!.Value.AddSeconds(20);
            if (Now >= due)
            {
                arrest.Shocks++;
                arrest.LastShockAt = Now;
                if (arrest.FirstShockAt is null)
                {
                    arrest.FirstShockAt = Now;
                    Stats.RecordTimeToShock((Now - arrest.StartedAt).TotalMinutes);
                }

                patient.Log(Now, $"Shock {arrest.Shocks} delivered.");
                if (Random.Chance(ResusOdds.ShockSuccess(quality, team, age)))
                {
                    Rosc(arrest, patient, quality, age);
                    return;
                }
            }
        }
        else if (elapsed % 10 < 1)
        {
            var perMinute = ResusOdds.NonShockablePerMinute(quality, team, age, trolleyHere);
            if (Random.Chance(1 - Math.Pow(1 - perMinute, 1.0 / 6)))
            {
                Rosc(arrest, patient, quality, age);
                return;
            }
        }

        if (cprMinutes >= ResusOdds.StopAfterMinutesOfCpr)
        {
            var by = leader is not null ? $"{leader.Name} stopped resuscitation" : "Resuscitation was stopped";
            patient.Log(Now, $"{by} after {cprMinutes:0} minutes of CPR{(arrest.Shocks > 0 ? $" and {arrest.Shocks} shock{(arrest.Shocks == 1 ? string.Empty : "s")}" : string.Empty)}.", important: true);
            EndArrest(arrest, ArrestOutcome.Died, patient);
        }
    }

    /// <summary>The heart restarts: off to intensive care, kept stable for a while.</summary>
    private void Rosc(CardiacArrest arrest, Patient patient, double quality, double age)
    {
        var minutes = (Now - arrest.StartedAt).TotalMinutes;
        patient.Log(Now, $"Return of circulation (ROSC) after {minutes:0} minutes.", important: true);
        Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName}'s heart has restarted after {minutes:0} minutes. They need intensive care.", patient.Id, patient.CareSpaceId, patient.Triage);
        Stats.ArrestsRosc++;
        patient.Severity = Math.Min(patient.Severity, SeverityAfterRosc);
        patient.StabilisedUntil = Now + StabilisedAfterRosc;
        if (!Random.Chance(ResusOdds.SurvivalAfterRosc(quality, age, arrest.Rhythm)))
        {
            patient.PostArrestDeathAt = Now.AddHours(Random.Range(3.0, 30.0));
        }

        EndArrest(arrest, ArrestOutcome.Rosc, patient);
        SendUpAfterRosc(patient);
    }

    /// <summary>After ROSC a ward patient is moved up to ICU (or transferred out); in A&amp;E they are referred to ICU.</summary>
    private void SendUpAfterRosc(Patient patient)
    {
        patient.CareLevel = 3;
        if (patient.Stage == PatientStage.OnWard)
        {
            if (CriticalUnitOfPatient(patient) != WardUnit.IntensiveCare)
            {
                patient.WardMoveRequestedAt = null;
                RequestWardMove(patient, "After the cardiac arrest: needs intensive care.");
            }
        }
        else
        {
            patient.Log(Now, "After the cardiac arrest: needs intensive care once admitted.");
        }
    }

    private void EndArrest(CardiacArrest arrest, ArrestOutcome outcome, Patient? patient)
    {
        arrest.Outcome = outcome;
        arrest.EndedAt = Now;
        if (patient is not null)
        {
            patient.ArrestStartedAt = null;
        }

        foreach (var responder in arrest.Team)
        {
            if (FindStaff(responder.StaffId) is not { } member || member.CrashPatientId != arrest.PatientId)
            {
                continue;
            }

            // The one with the trolley takes it home; everyone else goes back to work.
            if (responder.Role == CrashRole.Trolley && arrest.TrolleyId is { } tid && _trolleys.FirstOrDefault(t => t.Id == tid) is { } trolley && !trolley.IsHome)
            {
                member.StatusText = "Taking the resus trolley back";
                continue;
            }

            StandDown(member);
        }

        if (arrest.TrolleyId is { } id && _trolleys.FirstOrDefault(t => t.Id == id) is { } used && used.PushedByStaffId is null)
        {
            // Nobody to take it back: it waits by the bed until someone fetches it next time.
            used.AtArrestPatientId = null;
        }

        if (outcome == ArrestOutcome.Died && patient is not null && patient.IsAlive)
        {
            PatientDied(patient);
        }

        Events.Publish(new CrashCallEnded(arrest));
    }

    private void StandDown(StaffMember member)
    {
        member.CrashPatientId = null;
        member.WalkSpeed = Agent.DefaultWalkSpeed;
        member.Activity = StaffActivity.Idle;
        member.StatusText = "Available";
    }

    /// <summary>
    /// A crash team member: runs to the bed (fetching the trolley first if that's their job), then stays until the
    /// arrest ends. Returns true while they are busy with it, so they do nothing else.
    /// </summary>
    private bool UpdateCrashResponder(StaffMember member)
    {
        if (member.CrashPatientId is not { } patientId)
        {
            return false;
        }

        var arrest = _arrests.LastOrDefault(a => a.PatientId == patientId && a.Team.Any(r => r.StaffId == member.Id));
        var responder = arrest?.Team.FirstOrDefault(r => r.StaffId == member.Id);
        if (arrest is null || responder is null)
        {
            StandDown(member);
            return false;
        }

        var trolley = responder.Role == CrashRole.Trolley && arrest.TrolleyId is { } tid ? _trolleys.FirstOrDefault(t => t.Id == tid) : null;
        if (!arrest.IsActive)
        {
            // Pushing the trolley home.
            if (trolley is null || trolley.IsHome)
            {
                StandDown(member);
                return false;
            }

            if (trolley.PushedByStaffId != member.Id)
            {
                if (Chebyshev(member.Tile, trolley.Tile) <= 1 && !member.IsMoving)
                {
                    trolley.PushedByStaffId = member.Id;
                    trolley.AtArrestPatientId = null;
                }
                else if (!member.IsMoving)
                {
                    WalkTo(member, trolley.Tile);
                }

                return true;
            }

            var home = trolley.Home;
            if (member.Tile == home && !member.IsMoving)
            {
                trolley.PushedByStaffId = null;
                trolley.X = home.X + 0.5;
                trolley.Y = home.Y + 0.5;
                StandDown(member);
                return false;
            }

            member.WalkSpeed = Agent.DefaultWalkSpeed;
            if (!member.IsMoving)
            {
                WalkTo(member, home);
            }

            return true;
        }

        var patient = FindPatient(patientId)!;
        if (trolley is not null && arrest.TrolleyArrivedAt is null)
        {
            if (trolley.PushedByStaffId != member.Id)
            {
                // Going to get it.
                if (Chebyshev(member.Tile, trolley.Tile) <= 1 && !member.IsMoving)
                {
                    trolley.PushedByStaffId = member.Id;
                    member.StatusText = $"Crash call: pushing the resus trolley to {arrest.Place}";
                    WalkTo(member, responder.Spot);
                }
                else if (!member.IsMoving || member.Destination != trolley.Tile)
                {
                    WalkTo(member, trolley.Tile);
                }

                return true;
            }

            if (!member.IsMoving && Chebyshev(member.Tile, patient.Tile) <= 3)
            {
                arrest.TrolleyArrivedAt = Now;
                responder.ArrivedAt ??= Now;
                trolley.PushedByStaffId = null;
                ParkTrolleyBeside(trolley, member, patient, arrest);
                patient.Log(Now, $"Resus trolley brought by {member.Name}. Rhythm: {(arrest.Rhythm == ArrestRhythm.Shockable ? "VF, shockable" : "asystole, not shockable")}.");
                if (arrest.Rhythm != ArrestRhythm.Shockable)
                {
                    patient.Log(Now, "Adrenaline given.");
                }
            }
            else if (!member.IsMoving)
            {
                WalkTo(member, responder.Spot);
            }

            return true;
        }

        if (responder.ArrivedAt is null)
        {
            if (!member.IsMoving && Chebyshev(member.Tile, patient.Tile) <= 3)
            {
                responder.ArrivedAt = Now;
                switch (responder.Role)
                {
                    case CrashRole.Leader:
                        arrest.LeaderArrivedAt = Now;
                        patient.Log(Now, $"{member.Name} ({member.Grade}) is leading the arrest.");
                        break;
                    case CrashRole.Airway:
                        arrest.AirwayArrivedAt = Now;
                        patient.Log(Now, $"Anaesthetist {member.Name} has arrived and secured the airway.");
                        break;
                }
            }
            else if (!member.IsMoving)
            {
                WalkTo(member, responder.Spot);
            }

            return true;
        }

        member.Activity = StaffActivity.Working;
        member.StatusText = responder.Role switch
        {
            CrashRole.Leader => $"Leading the arrest: {patient.FullName}",
            CrashRole.Airway => $"Managing the airway: {patient.FullName}",
            _ => $"Doing CPR: {patient.FullName}",
        };
        return true;
    }

    /// <summary>
    /// Parks the trolley by the patient but never on a bed: alongside the bed towards its head or foot, so it
    /// does not hide the patient and whoever is doing CPR, as near as possible to whoever brought it.
    /// </summary>
    private void ParkTrolleyBeside(ResusTrolley trolley, StaffMember member, Patient patient, CardiacArrest arrest)
    {
        var centre = patient.Tile;
        var spots = arrest.Team.Select(r => r.Spot).ToHashSet();
        var bedSpots = _spaces.Values.SelectMany(s => s.Beds.Select(b => b.PatientSpot)).ToList();
        var floor = Site.FloorOf(centre);
        var best = Enumerable.Range(-3, 7).SelectMany(dx => Enumerable.Range(-3, 7).Select(dy => new GridCoord(centre.X + dx, centre.Y + dy)))
            .Where(t => Chebyshev(t, centre) >= 2 && !spots.Contains(t) && Nav.IsWalkable(t) && Site.FloorOf(t) == floor)
            .Where(t => !bedSpots.Any(b => Math.Abs(b.X - t.X) <= 1 && Math.Abs(b.Y - t.Y) <= 2))
            .OrderBy(t => Math.Abs(t.Y - centre.Y) >= 2 ? 0 : 1)
            .ThenBy(t => Chebyshev(t, member.Tile))
            .ThenBy(t => Chebyshev(t, centre))
            .Cast<GridCoord?>()
            .FirstOrDefault();
        if (best is { } tile)
        {
            trolley.X = trolley.PreviousX = tile.X + 0.5;
            trolley.Y = trolley.PreviousY = tile.Y + 0.5;
        }
    }

    /// <summary>The member of the team doing chest compressions right now (they take turns every two minutes).</summary>
    public StaffMember? CompressingNow(CardiacArrest arrest)
    {
        var hands = arrest.Team.Where(r => r.ArrivedAt is not null && r.Role is CrashRole.Compressions or CrashRole.SecondNurse or CrashRole.Trolley)
            .Select(r => FindStaff(r.StaffId)).Where(s => s is not null && s.CrashPatientId == arrest.PatientId && !s.IsMoving).ToList();
        if (hands.Count == 0)
        {
            return arrest.Team.Where(r => r.ArrivedAt is not null).Select(r => FindStaff(r.StaffId)).FirstOrDefault(s => s is not null && !s.IsMoving);
        }

        var turn = arrest.CprStartedAt is { } c ? (int)((Now - c).TotalMinutes / 2) : 0;
        return hands[turn % hands.Count];
    }

    // ---------------------------------------------------------------- resus trolleys

    private ResusTrolley? NearestFreeTrolley(GridCoord to) =>
        _trolleys.Where(t => t.PushedByStaffId is null && t.AtArrestPatientId is null && Site.FloorOf(t.Tile) == Site.FloorOf(to))
            .OrderBy(t => Chebyshev(t.Tile, to)).FirstOrDefault()
        ?? _trolleys.Where(t => t.PushedByStaffId is null && t.AtArrestPatientId is null).OrderBy(t => Chebyshev(t.Tile, to) + (Math.Abs(Site.FloorOf(t.Tile) - Site.FloorOf(to)) * 200)).FirstOrDefault();

    /// <summary>
    /// Every resus bay and ward gets the trolley its room plan puts in it; trolleys go with their room when it is
    /// knocked down. One the player has moved stays where they put it.
    /// </summary>
    private void SyncResusTrolleys()
    {
        var wanted = new Dictionary<(string Room, int Index), GridCoord>();
        foreach (var space in _spaces.Values)
        {
            var i = 0;
            foreach (var item in space.Furniture)
            {
                if (item.ItemId == ResusTrolleyItem)
                {
                    wanted[(space.Id, i++)] = item.Tile;
                }
            }
        }

        foreach (var trolley in _trolleys.Where(t => t.HomeRoomId is not null).ToList())
        {
            if (!wanted.TryGetValue((trolley.HomeRoomId!, trolley.HomeIndex), out var tile))
            {
                if (trolley.PushedByStaffId is null && trolley.AtArrestPatientId is null)
                {
                    _trolleys.Remove(trolley);
                }

                continue;
            }

            wanted.Remove((trolley.HomeRoomId!, trolley.HomeIndex));
            if (!trolley.Moved && trolley.Home != tile)
            {
                var wasHome = trolley.IsHome;
                trolley.Home = tile;
                if (wasHome || trolley.PushedByStaffId is null && trolley.AtArrestPatientId is null)
                {
                    PlaceTrolley(trolley, tile);
                }
            }
            else if (trolley.Moved && !TrolleyFloorOk(trolley.Home, trolley))
            {
                // The room around it has changed: it goes back to its room's place.
                trolley.Moved = false;
                trolley.Home = tile;
                if (trolley.PushedByStaffId is null && trolley.AtArrestPatientId is null)
                {
                    PlaceTrolley(trolley, tile);
                }
            }
        }

        foreach (var ((room, index), tile) in wanted)
        {
            var trolley = new ResusTrolley { Id = _nextTrolleyId++, HomeRoomId = room, HomeIndex = index, Home = tile };
            PlaceTrolley(trolley, tile);
            _trolleys.Add(trolley);
        }
    }

    private static void PlaceTrolley(ResusTrolley trolley, GridCoord tile)
    {
        trolley.X = trolley.PreviousX = tile.X + 0.5;
        trolley.Y = trolley.PreviousY = tile.Y + 0.5;
    }

    /// <summary>Why a resus trolley can't stand here, or null if it can.</summary>
    private string? TrolleyProblem(GridCoord tile, ResusTrolley? moving)
    {
        if (!Nav.IsWalkable(tile) || !Site.Buildings.Any(b => b.Contains(Site.Local(tile))) && !_spaces.Values.Any(s => s.Rect.Contains(tile)))
        {
            return "Resus trolleys go inside the hospital, on the floor of a room or corridor.";
        }

        var room = _spaces.Values.FirstOrDefault(s => s.HasWalls && s.Rect.Contains(tile));
        if (room is not null && (room.IsShaft || room.IsToilet))
        {
            return "Not in a lift, stairwell or toilet.";
        }

        if (room is not null && (room.Beds.Any(b => b.PatientSpot == tile || b.StaffSpot == tile) || room.PatientSpot == tile || room.StaffSpot == tile))
        {
            return "Keep beds and the space round them clear.";
        }

        if (room is not null && room.Beds.Any(b => Math.Abs(b.PatientSpot.X - tile.X) <= 1 && Math.Abs(b.PatientSpot.Y - tile.Y) <= 2))
        {
            return "Keep beds and the space round them clear.";
        }

        if (DoorwayTiles().Contains(tile) || (room is not null && WaitingRoomLayout.DoorTiles(room.Rect, room.Doors).Contains(tile)))
        {
            return "Keep doorways clear.";
        }

        if (ChairAt(tile) is not null)
        {
            return "There is a chair there.";
        }

        if (_trolleys.Any(t => t != moving && t.Home == tile))
        {
            return "There is already a trolley there.";
        }

        return null;
    }

    private bool TrolleyFloorOk(GridCoord tile, ResusTrolley trolley) => TrolleyProblem(tile, trolley) is null;

    /// <summary>Whether a new resus trolley can go here (bought), or <paramref name="moving"/> can be moved here (free).</summary>
    public BuildCheck CheckResusTrolley(GridCoord tile, ResusTrolley? moving = null)
    {
        var problems = new List<string>();
        if (moving is null && !IsRoomUnlocked(Data.BuildMenu.ResusTrolleys))
        {
            problems.Add($"Resus trolleys unlock at level {LevelOfRoom(Data.BuildMenu.ResusTrolleys)}.");
        }
        else if (TrolleyProblem(tile, moving) is { } problem)
        {
            problems.Add(problem);
        }

        var cost = moving is null ? ResusTrolleyCost : 0;
        if (cost > 0 && !CanAfford(cost))
        {
            problems.Add($"Not enough money: costs £{cost:N0}, you have £{Balance:N0}.");
        }

        if (moving is not null && (moving.PushedByStaffId is not null || moving.AtArrestPatientId is not null))
        {
            problems.Add("It's being used at a cardiac arrest.");
        }

        return new BuildCheck(cost, problems);
    }

    /// <summary>Buys a resus trolley and puts it here.</summary>
    public BuildCheck BuyResusTrolley(GridCoord tile)
    {
        var check = CheckResusTrolley(tile);
        if (!check.CanBuild)
        {
            return check;
        }

        var trolley = new ResusTrolley { Id = _nextTrolleyId++, Home = tile, Moved = true };
        PlaceTrolley(trolley, tile);
        _trolleys.Add(trolley);
        Book(MoneyCategory.Construction, -check.Cost);
        Events.Publish(new LayoutChanged());
        return check;
    }

    /// <summary>Moves a resus trolley to where the player wants it kept.</summary>
    public BuildCheck MoveResusTrolley(int trolleyId, GridCoord tile)
    {
        if (_trolleys.FirstOrDefault(t => t.Id == trolleyId) is not { } trolley)
        {
            return new BuildCheck(0, new[] { "That trolley has gone." });
        }

        var check = CheckResusTrolley(tile, trolley);
        if (!check.CanBuild)
        {
            return check;
        }

        trolley.Home = tile;
        trolley.Moved = true;
        PlaceTrolley(trolley, tile);
        Events.Publish(new LayoutChanged());
        return check;
    }

    /// <summary>Sells a trolley the player bought (half the price back). The ones that come with rooms can only be moved.</summary>
    public bool SellResusTrolley(int trolleyId)
    {
        if (_trolleys.FirstOrDefault(t => t.Id == trolleyId) is not { HomeRoomId: null, PushedByStaffId: null, AtArrestPatientId: null } trolley)
        {
            return false;
        }

        _trolleys.Remove(trolley);
        Book(MoneyCategory.Construction, ResusTrolleyCost / 2);
        Events.Publish(new LayoutChanged());
        return true;
    }

    // ---------------------------------------------------------------- saving

    internal List<ResusTrolleyState> SaveTrolleys() => _trolleys.Select(t => new ResusTrolleyState
    {
        Id = t.Id, HomeRoomId = t.HomeRoomId, HomeIndex = t.HomeIndex, HomeX = t.Home.X, HomeY = t.Home.Y, Moved = t.Moved,
    }).ToList();

    internal void RestoreTrolleys(List<ResusTrolleyState>? saved, List<CardiacArrest>? arrests)
    {
        _trolleys.Clear();
        _arrests.Clear();
        foreach (var s in saved ?? new List<ResusTrolleyState>())
        {
            var trolley = new ResusTrolley { Id = s.Id, HomeRoomId = s.HomeRoomId, HomeIndex = s.HomeIndex, Home = new GridCoord(s.HomeX, s.HomeY), Moved = s.Moved };
            PlaceTrolley(trolley, trolley.Home);
            _trolleys.Add(trolley);
        }

        _nextTrolleyId = _trolleys.Count == 0 ? 1 : _trolleys.Max(t => t.Id) + 1;

        // An arrest going on when the game was saved: the team are called again as it loads.
        foreach (var arrest in arrests ?? new List<CardiacArrest>())
        {
            if (!arrest.IsActive || FindPatient(arrest.PatientId) is not { IsInDepartment: true } patient)
            {
                continue;
            }

            var resumed = new CardiacArrest
            {
                PatientId = arrest.PatientId, StartedAt = arrest.StartedAt, Rhythm = arrest.Rhythm, Place = arrest.Place, Witnessed = arrest.Witnessed,
                CrashCallAt = arrest.CrashCallAt, CprStartedAt = arrest.CprStartedAt, Shocks = arrest.Shocks, LastShockAt = arrest.LastShockAt, FirstShockAt = arrest.FirstShockAt,
            };
            _arrests.Add(resumed);
            patient.ArrestStartedAt = arrest.StartedAt;
            if (arrest.CrashCalled)
            {
                resumed.CrashCalled = true;
                CallCrashTeam(resumed, patient);
            }
        }

        _trolleySyncCountdown = 0;
    }
}

/// <summary>Where an arrest is: shown, spoken on the tannoy, and the recorded line's key.</summary>
internal sealed record ArrestPlaceName(string Display, string Spoken, string Key, string? WardTemplate)
{
    /// <summary>The recorded tannoy line: "arrest_resus_2", "arrest_medical_ward", "paed_arrest", "obstetric_emergency".</summary>
    public string Clip(Patient patient) => patient.Maternity ? "obstetric_emergency" : patient.Age < 16 ? "paed_arrest" : $"arrest_{Key}";
}

/// <summary>A resus trolley as stored in a save.</summary>
public sealed class ResusTrolleyState
{
    public int Id { get; init; }

    public string? HomeRoomId { get; init; }

    public int HomeIndex { get; init; }

    public int HomeX { get; init; }

    public int HomeY { get; init; }

    public bool Moved { get; init; }
}
