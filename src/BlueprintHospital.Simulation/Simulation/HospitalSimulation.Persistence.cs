using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Where an agent stands and where it was walking to.</summary>
public sealed record PositionState(int X, int Y, int? ToX = null, int? ToY = null);

public sealed record ObservationState(DateTime Time, Observations Obs);

/// <summary>Everything about one patient, including their hidden condition and full record.</summary>
public sealed class PatientState
{
    public int Id { get; init; }

    public string HospitalNumber { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string Surname { get; init; } = string.Empty;

    public Sex Sex { get; init; }

    public DateTime DateOfBirth { get; init; }

    public int Age { get; init; }

    public List<string> PastHistory { get; init; } = new();

    public List<string> RegularMedications { get; init; } = new();

    public List<string> Allergies { get; init; } = new();

    public List<string> AllergyGroups { get; init; } = new();

    public int PreviousVisits { get; init; }

    public ArrivalMethod ArrivalMethod { get; init; }

    public DateTime ArrivedAt { get; init; }

    public string PresentingComplaint { get; init; } = string.Empty;

    public List<string> Symptoms { get; init; } = new();

    public string Condition { get; init; } = string.Empty;

    public Observations Baseline { get; init; } = null!;

    public double InitialSeverity { get; init; }

    public double Severity { get; init; }

    public string? AmbulanceHandover { get; init; }

    public PatientStage Stage { get; init; }

    public PatientOutcome Outcome { get; init; }

    public string OutcomeNote { get; init; } = string.Empty;

    public DateTime? LeftAt { get; init; }

    public TriageCategory? Triage { get; init; }

    public string TriageReason { get; init; } = string.Empty;

    public DateTime? TriagedAt { get; init; }

    public List<string> KnownSymptoms { get; init; } = new();

    public bool HistoryTaken { get; init; }

    public DateTime? FirstSeenByDoctorAt { get; init; }

    public List<InvestigationOrder> Investigations { get; init; } = new();

    public Diagnosis? WorkingDiagnosis { get; init; }

    public List<TreatmentRecord> TreatmentPlan { get; init; } = new();

    public bool EffectiveTreatmentGiven { get; init; }

    public double Analgesia { get; init; }

    public string? CareSpaceId { get; init; }

    public GridCoord? Seat { get; init; }

    public int? BusyWithTaskId { get; init; }

    public DateTime QueuedSince { get; init; }

    public DateTime? TransferAmbulanceDueAt { get; init; }

    public int? BedIndex { get; init; }

    public string? ReservedWardId { get; init; }

    public int? ReservedBedIndex { get; init; }

    public DateTime? BedRequestedAt { get; init; }

    public DateTime? LeftEmergencyAt { get; init; }

    public DateTime? AdmittedAt { get; init; }

    public DateTime? DischargeDueAt { get; init; }

    public DateTime? MedicinesReadyAt { get; init; }

    public List<TimelineEntry> Timeline { get; init; } = new();

    public List<ObservationState> ObservationHistory { get; init; } = new();

    public PositionState Position { get; init; } = new(0, 0);
}

public sealed class StaffState
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public StaffRole Role { get; init; }

    public string Grade { get; init; } = string.Empty;

    public string DepartmentId { get; init; } = string.Empty;

    public string? WardId { get; init; }

    public int Age { get; init; }

    public int YearsExperience { get; init; }

    public double Skill { get; init; }

    public double Speed { get; init; }

    public int Salary { get; init; }

    public double Fatigue { get; init; }

    public double Morale { get; init; }

    public StaffActivity Activity { get; init; }

    public int? CurrentTaskId { get; init; }

    public double WorkRemainingSeconds { get; init; }

    public DateTime? BreakEndsAt { get; init; }

    public string StatusText { get; init; } = string.Empty;

    public List<int> Caseload { get; init; } = new();

    public int TasksCompleted { get; init; }

    public PositionState Position { get; init; } = new(0, 0);
}

public sealed class AmbulanceSave
{
    public int Id { get; init; }

    public string CallSign { get; init; } = string.Empty;

    public AmbulancePurpose Purpose { get; init; }

    public int PatientId { get; init; }

    public AmbulanceState State { get; init; }

    public int? BaySlot { get; init; }

    public DateTime? ArrivedAtBayAt { get; init; }

    public DateTime? HandoverCompletedAt { get; init; }

    public PositionState Position { get; init; } = new(0, 0);
}

/// <summary>Who is in each room and bed.</summary>
public sealed record SpaceUseState(string Id, int? OccupantId, int? ReservedByStaffId, List<int?> Beds);

public sealed record OutcomeReviewState(DateTime Due, int PatientId);

public sealed record DayAccountsState(DateTime Date, Dictionary<MoneyCategory, long> Amounts);

public sealed class StatsState
{
    public Dictionary<string, long> Counters { get; init; } = new();

    public Dictionary<TriageCategory, int> TriagedTotal { get; init; } = new();

    public List<double> RecentTimeToDoctor { get; init; } = new();

    public List<double> RecentHandoverMinutes { get; init; } = new();
}

/// <summary>Everyone and everything happening in the hospital, for saving (format 3 onwards).</summary>
public sealed class PeopleState
{
    public List<PatientState> Patients { get; init; } = new();

    /// <summary>Recent patients who have left, kept so their records can still be read.</summary>
    public List<PatientState> Departed { get; init; } = new();

    public List<StaffState> Staff { get; init; } = new();

    public List<ClinicalTask> Tasks { get; init; } = new();

    public List<AmbulanceSave> Ambulances { get; init; } = new();

    public List<int> ReceptionQueue { get; init; } = new();

    public List<OutcomeReviewState> OutcomeReviews { get; init; } = new();

    public List<SpaceUseState> Spaces { get; init; } = new();

    public List<string> CapacityAlerts { get; init; } = new();

    public StatsState Stats { get; init; } = new();

    public List<DayAccountsState> Accounts { get; init; } = new();

    public double UnpaidWages { get; init; }

    public double UnpaidUpkeep { get; init; }

    public List<Notification> Notifications { get; init; } = new();

    public int NextNotificationId { get; init; }

    public int NextPatientId { get; init; }

    public int NextStaffId { get; init; }

    public int NextTaskId { get; init; }

    public int NextAmbulanceId { get; init; }
}

/// <summary>Saving and loading everyone in the hospital.</summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Departed patients kept in a save.</summary>
    private const int DepartedKeptInSave = 100;

    public PeopleState CapturePeople() => new()
    {
        Patients = _patients.Values.OrderBy(p => p.Id).Select(SavePatient).ToList(),
        Departed = _departed.Take(DepartedKeptInSave).Select(SavePatient).ToList(),
        Staff = _staff.Select(SaveStaff).ToList(),
        Tasks = _tasks.Values.OrderBy(t => t.Id).ToList(),
        Ambulances = _ambulances.Select(a => new AmbulanceSave
        {
            Id = a.Id, CallSign = a.CallSign, Purpose = a.Purpose, PatientId = a.PatientId, State = a.State, BaySlot = a.BaySlot,
            ArrivedAtBayAt = a.ArrivedAtBayAt, HandoverCompletedAt = a.HandoverCompletedAt, Position = PositionOf(a),
        }).ToList(),
        ReceptionQueue = _receptionQueue.ToList(),
        OutcomeReviews = _pendingOutcomeReviews.Select(r => new OutcomeReviewState(r.Due, r.Patient.Id)).ToList(),
        Spaces = _spaces.Values.OrderBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => new SpaceUseState(s.Id, s.OccupantId, s.ReservedByStaffId, s.Beds.Select(b => b.OccupantId).ToList()))
            .ToList(),
        CapacityAlerts = _activeCapacityAlerts.ToList(),
        Stats = CaptureStats(),
        Accounts = _days.Select(d => new DayAccountsState(d.Date, new Dictionary<MoneyCategory, long>(d.Amounts))).ToList(),
        UnpaidWages = _unpaidWages,
        UnpaidUpkeep = _unpaidUpkeep,
        Notifications = Notifications.Recent.Take(60).ToList(),
        NextNotificationId = Notifications.NextId,
        NextPatientId = _generator.NextId,
        NextStaffId = _nextStaffId,
        NextTaskId = _nextTaskId,
        NextAmbulanceId = _nextAmbulanceId,
    };

    /// <summary>
    /// Puts back everyone from a save. Call after <see cref="RestoreLayout"/> and before the game runs.
    /// Anything that refers to a room, condition or test that no longer exists is dropped.
    /// </summary>
    public void RestorePeople(PeopleState state)
    {
        _patients.Clear();
        _departed.Clear();
        _staff.Clear();
        _tasks.Clear();
        _ambulances.Clear();
        _receptionQueue.Clear();
        _pendingOutcomeReviews.Clear();
        _activeCapacityAlerts.Clear();

        foreach (var saved in state.Patients)
        {
            if (LoadPatient(saved) is { } patient)
            {
                _patients[patient.Id] = patient;
            }
        }

        foreach (var saved in state.Departed)
        {
            if (LoadPatient(saved) is { } patient)
            {
                _departed.Add(patient);
            }
        }

        foreach (var saved in state.Staff)
        {
            _staff.Add(LoadStaff(saved));
        }

        foreach (var task in state.Tasks.Where(t => _patients.ContainsKey(t.PatientId)))
        {
            _tasks[task.Id] = task;
        }

        foreach (var saved in state.Ambulances.Where(a => _patients.ContainsKey(a.PatientId) || a.State == AmbulanceState.Departing))
        {
            var ambulance = new AmbulanceVehicle
            {
                Id = saved.Id, CallSign = saved.CallSign, Purpose = saved.Purpose, PatientId = saved.PatientId, State = saved.State,
                BaySlot = saved.BaySlot, ArrivedAtBayAt = saved.ArrivedAtBayAt, HandoverCompletedAt = saved.HandoverCompletedAt,
            };
            ambulance.PlaceAt(new GridCoord(saved.Position.X, saved.Position.Y));
            _ambulances.Add(ambulance);
            ResumeAmbulance(ambulance);
        }

        _receptionQueue.AddRange(state.ReceptionQueue.Where(_patients.ContainsKey));
        foreach (var review in state.OutcomeReviews)
        {
            if (FindPatient(review.PatientId) is { } patient)
            {
                _pendingOutcomeReviews.Add((review.Due, patient));
            }
        }

        foreach (var use in state.Spaces)
        {
            if (FindSpace(use.Id) is not { } space)
            {
                continue;
            }

            space.OccupantId = use.OccupantId is { } o && _patients.ContainsKey(o) ? o : null;
            space.ReservedByStaffId = use.ReservedByStaffId is { } s && _staff.Any(m => m.Id == s) ? s : null;
            for (var i = 0; i < Math.Min(space.Beds.Count, use.Beds.Count); i++)
            {
                space.Beds[i].OccupantId = use.Beds[i] is { } b && _patients.ContainsKey(b) ? b : null;
            }
        }

        foreach (var alert in state.CapacityAlerts)
        {
            _activeCapacityAlerts.Add(alert);
        }

        RestoreStats(state.Stats);
        _days.Clear();
        foreach (var day in state.Accounts)
        {
            var accounts = new DayAccounts { Date = day.Date };
            foreach (var (category, amount) in day.Amounts)
            {
                accounts.Add(category, amount);
            }

            _days.Add(accounts);
        }

        _unpaidWages = state.UnpaidWages;
        _unpaidUpkeep = state.UnpaidUpkeep;
        Notifications.Restore(state.Notifications, state.NextNotificationId);
        _generator.SetNextId(state.NextPatientId);
        _nextStaffId = Math.Max(state.NextStaffId, _staff.Count == 0 ? 1 : _staff.Max(s => s.Id) + 1);
        _nextTaskId = Math.Max(state.NextTaskId, _tasks.Count == 0 ? 1 : _tasks.Keys.Max() + 1);
        _nextAmbulanceId = Math.Max(state.NextAmbulanceId, 1);

        // A saved game can't carry on the old random sequence, so start a new one.
        Random.Reseed(HashCode.Combine(Random.Seed, Now.Ticks));

        // Anything a dropped room or person left behind is cleaned up, and walkers set off again.
        foreach (var patient in _patients.Values)
        {
            if (patient.CareSpaceId is { } id && FindSpace(id) is null)
            {
                patient.CareSpaceId = null;
                patient.BedIndex = null;
            }

            if (patient.BusyWithTaskId is { } taskId && !_tasks.ContainsKey(taskId))
            {
                patient.BusyWithTaskId = null;
            }
        }

        foreach (var member in _staff.Where(m => m.CurrentTaskId is { } t && !_tasks.ContainsKey(t)))
        {
            member.CurrentTaskId = null;
            member.Activity = StaffActivity.Idle;
            member.StatusText = "Available";
        }

        SyncRoomStaff();
        Resume(state.Patients.Select(p => (p.Id, p.Position)), id => _patients.GetValueOrDefault(id));
        Resume(state.Staff.Select(s => (s.Id, s.Position)), id => FindStaff(id));
    }

    // ------------------------------------------------------------------ patients

    private PatientState SavePatient(Patient p) => new()
    {
        Id = p.Id, HospitalNumber = p.HospitalNumber, FirstName = p.FirstName, Surname = p.Surname, Sex = p.Sex,
        DateOfBirth = p.DateOfBirth, Age = p.Age, PastHistory = p.PastHistory.ToList(), RegularMedications = p.RegularMedications.ToList(),
        Allergies = p.Allergies.ToList(), AllergyGroups = p.AllergyGroups.ToList(), PreviousVisits = p.PreviousVisits,
        ArrivalMethod = p.ArrivalMethod, ArrivedAt = p.ArrivedAt, PresentingComplaint = p.PresentingComplaint, Symptoms = p.Symptoms.ToList(),
        Condition = p.Condition.Id, Baseline = p.BaselineObservations, InitialSeverity = p.InitialSeverity, Severity = p.Severity,
        AmbulanceHandover = p.AmbulanceHandover, Stage = p.Stage, Outcome = p.Outcome, OutcomeNote = p.OutcomeNote, LeftAt = p.LeftAt,
        Triage = p.Triage, TriageReason = p.TriageReason, TriagedAt = p.TriagedAt, KnownSymptoms = p.KnownSymptoms.ToList(),
        HistoryTaken = p.HistoryTaken, FirstSeenByDoctorAt = p.FirstSeenByDoctorAt, Investigations = p.Investigations.ToList(),
        WorkingDiagnosis = p.WorkingDiagnosis, TreatmentPlan = p.TreatmentPlan.ToList(), EffectiveTreatmentGiven = p.EffectiveTreatmentGiven,
        Analgesia = p.Analgesia, CareSpaceId = p.CareSpaceId, Seat = p.Seat, BusyWithTaskId = p.BusyWithTaskId, QueuedSince = p.QueuedSince,
        TransferAmbulanceDueAt = p.TransferAmbulanceDueAt, BedIndex = p.BedIndex, ReservedWardId = p.ReservedWardId,
        ReservedBedIndex = p.ReservedBedIndex, BedRequestedAt = p.BedRequestedAt, LeftEmergencyAt = p.LeftEmergencyAt,
        AdmittedAt = p.AdmittedAt, DischargeDueAt = p.DischargeDueAt, MedicinesReadyAt = p.MedicinesReadyAt, Timeline = p.Timeline.ToList(),
        ObservationHistory = p.ObservationHistory.Select(o => new ObservationState(o.Time, o.Obs)).ToList(),
        Position = PositionOf(p),
    };

    private Patient? LoadPatient(PatientState s)
    {
        if (!Database.Conditions.TryGetValue(s.Condition, out var condition))
        {
            return null;
        }

        var p = new Patient
        {
            Id = s.Id, HospitalNumber = s.HospitalNumber, FirstName = s.FirstName, Surname = s.Surname, Sex = s.Sex,
            DateOfBirth = s.DateOfBirth, Age = s.Age, PastHistory = s.PastHistory, RegularMedications = s.RegularMedications,
            Allergies = s.Allergies, AllergyGroups = s.AllergyGroups, PreviousVisits = s.PreviousVisits, ArrivalMethod = s.ArrivalMethod,
            ArrivedAt = s.ArrivedAt, PresentingComplaint = s.PresentingComplaint, Symptoms = s.Symptoms, Condition = condition,
            BaselineObservations = s.Baseline, InitialSeverity = s.InitialSeverity,
        };
        p.Severity = s.Severity;
        p.AmbulanceHandover = s.AmbulanceHandover;
        p.Stage = s.Stage;
        p.Outcome = s.Outcome;
        p.OutcomeNote = s.OutcomeNote;
        p.LeftAt = s.LeftAt;
        p.Triage = s.Triage;
        p.TriageReason = s.TriageReason;
        p.TriagedAt = s.TriagedAt;
        p.KnownSymptoms.UnionWith(s.KnownSymptoms);
        p.HistoryTaken = s.HistoryTaken;
        p.FirstSeenByDoctorAt = s.FirstSeenByDoctorAt;
        p.Investigations.AddRange(s.Investigations.Where(i => Database.Investigations.ContainsKey(i.InvestigationId)));
        p.WorkingDiagnosis = s.WorkingDiagnosis is { } d && Database.Conditions.ContainsKey(d.ConditionId) ? d : null;
        p.TreatmentPlan.AddRange(s.TreatmentPlan);
        p.EffectiveTreatmentGiven = s.EffectiveTreatmentGiven;
        p.Analgesia = s.Analgesia;
        p.CareSpaceId = s.CareSpaceId;
        p.Seat = s.Seat;
        p.BusyWithTaskId = s.BusyWithTaskId;
        p.QueuedSince = s.QueuedSince;
        p.TransferAmbulanceDueAt = s.TransferAmbulanceDueAt;
        p.BedIndex = s.BedIndex;
        p.ReservedWardId = s.ReservedWardId;
        p.ReservedBedIndex = s.ReservedBedIndex;
        p.BedRequestedAt = s.BedRequestedAt;
        p.LeftEmergencyAt = s.LeftEmergencyAt;
        p.AdmittedAt = s.AdmittedAt;
        p.DischargeDueAt = s.DischargeDueAt;
        p.MedicinesReadyAt = s.MedicinesReadyAt;
        p.RestoreTimeline(s.Timeline);
        foreach (var o in s.ObservationHistory)
        {
            p.RecordObservations(o.Time, o.Obs);
        }

        p.PlaceAt(new GridCoord(s.Position.X, s.Position.Y));
        return p;
    }

    // ------------------------------------------------------------------ staff

    private static StaffState SaveStaff(StaffMember m) => new()
    {
        Id = m.Id, Name = m.Name, Role = m.Role, Grade = m.Grade, DepartmentId = m.DepartmentId, WardId = m.BaseRoomId, Age = m.Age,
        YearsExperience = m.YearsExperience, Skill = m.Skill, Speed = m.Speed, Salary = m.Salary, Fatigue = m.Fatigue, Morale = m.Morale,
        Activity = m.Activity, CurrentTaskId = m.CurrentTaskId, WorkRemainingSeconds = m.WorkRemainingSeconds, BreakEndsAt = m.BreakEndsAt,
        StatusText = m.StatusText, Caseload = m.Caseload.ToList(), TasksCompleted = m.TasksCompleted, Position = PositionOf(m),
    };

    private static StaffMember LoadStaff(StaffState s)
    {
        var m = new StaffMember
        {
            Id = s.Id, Name = s.Name, Role = s.Role, Grade = s.Grade, DepartmentId = s.DepartmentId, BaseRoomId = s.WardId, Age = s.Age,
            YearsExperience = s.YearsExperience, Skill = s.Skill, Speed = s.Speed, Salary = s.Salary,
        };
        m.Fatigue = s.Fatigue;
        m.Morale = s.Morale;
        m.Activity = s.Activity;
        m.CurrentTaskId = s.CurrentTaskId;
        m.WorkRemainingSeconds = s.WorkRemainingSeconds;
        m.BreakEndsAt = s.BreakEndsAt;
        m.StatusText = s.StatusText;
        m.Caseload.UnionWith(s.Caseload);
        m.TasksCompleted = s.TasksCompleted;
        m.PlaceAt(new GridCoord(s.Position.X, s.Position.Y));
        return m;
    }

    // ------------------------------------------------------------------ positions and statistics

    private static PositionState PositionOf(Agent agent) =>
        new(agent.Tile.X, agent.Tile.Y, agent.Destination?.X, agent.Destination?.Y);

    /// <summary>Sets walkers off again towards where they were going.</summary>
    private void Resume(IEnumerable<(int Id, PositionState Position)> saved, Func<int, Agent?> find)
    {
        foreach (var (id, position) in saved)
        {
            if (find(id) is not { } agent || position.ToX is not { } x || position.ToY is not { } y)
            {
                continue;
            }

            var path = Nav.FindPath(agent.Tile, new GridCoord(x, y));
            if (path.Count > 0)
            {
                agent.FollowPath(path);
            }
            else
            {
                agent.PlaceAt(new GridCoord(x, y));
            }
        }
    }

    /// <summary>Puts an ambulance back on the road it was taking.</summary>
    private void ResumeAmbulance(AmbulanceVehicle ambulance)
    {
        switch (ambulance.State)
        {
            case AmbulanceState.Driving when ambulance.BaySlot is { } slot:
                var parking = AmbulanceParkingTiles[slot];
                ambulance.FollowPath(StraightRoute(ambulance.Tile, new[] { new GridCoord(AmbulanceRoadEntry.X, parking.Y), parking }));
                break;
            case AmbulanceState.Driving:
                RouteAmbulanceToBay(ambulance);
                break;
            case AmbulanceState.Departing:
                ambulance.FollowPath(StraightRoute(ambulance.Tile, new[] { new GridCoord(AmbulanceRoadEntry.X, ambulance.Tile.Y), new GridCoord(AmbulanceRoadEntry.X, 0) }));
                break;
        }
    }

    private StatsState CaptureStats()
    {
        var s = Stats;
        return new StatsState
        {
            Counters = new Dictionary<string, long>
            {
                ["arrivals"] = s.ArrivalsTotal, ["ambulance_arrivals"] = s.AmbulanceArrivalsTotal, ["departed"] = s.DepartedTotal,
                ["within_four_hours"] = s.DepartedWithinFourHours, ["discharged"] = s.Discharged, ["transferred"] = s.Transferred,
                ["admitted"] = s.Admitted, ["ward_discharges"] = s.WardDischarges, ["ward_deaths"] = s.WardDeaths,
                ["ward_corrections"] = s.WardDiagnosesCorrected, ["deaths"] = s.Deaths, ["left_unseen"] = s.LeftWithoutBeingSeen,
                ["diagnoses"] = s.DiagnosesMade, ["diagnoses_correct"] = s.DiagnosesCorrect, ["missed"] = s.MissedDiagnoses,
                ["reputation_penalties"] = s.ReputationPenalties, ["investigation_cost"] = s.InvestigationCost, ["medication_cost"] = s.MedicationCost,
                ["scans"] = s.ScansDone, ["operations"] = s.OperationsDone,
            },
            TriagedTotal = new Dictionary<TriageCategory, int>(s.TriagedTotal),
            RecentTimeToDoctor = s.RecentTimeToDoctor.ToList(),
            RecentHandoverMinutes = s.RecentHandoverMinutes.ToList(),
        };
    }

    private void RestoreStats(StatsState state)
    {
        var s = Stats;
        int I(string key) => (int)state.Counters.GetValueOrDefault(key);
        s.ArrivalsTotal = I("arrivals");
        s.AmbulanceArrivalsTotal = I("ambulance_arrivals");
        s.DepartedTotal = I("departed");
        s.DepartedWithinFourHours = I("within_four_hours");
        s.Discharged = I("discharged");
        s.Transferred = I("transferred");
        s.Admitted = I("admitted");
        s.WardDischarges = I("ward_discharges");
        s.WardDeaths = I("ward_deaths");
        s.WardDiagnosesCorrected = I("ward_corrections");
        s.ScansDone = I("scans");
        s.OperationsDone = I("operations");
        s.Deaths = I("deaths");
        s.LeftWithoutBeingSeen = I("left_unseen");
        s.DiagnosesMade = I("diagnoses");
        s.DiagnosesCorrect = I("diagnoses_correct");
        s.MissedDiagnoses = I("missed");
        s.ReputationPenalties = I("reputation_penalties");
        s.InvestigationCost = state.Counters.GetValueOrDefault("investigation_cost");
        s.MedicationCost = state.Counters.GetValueOrDefault("medication_cost");
        foreach (var (category, count) in state.TriagedTotal)
        {
            s.TriagedTotal[category] = count;
        }

        s.RecentTimeToDoctor.Clear();
        foreach (var v in state.RecentTimeToDoctor)
        {
            s.RecentTimeToDoctor.Enqueue(v);
        }

        s.RecentHandoverMinutes.Clear();
        foreach (var v in state.RecentHandoverMinutes)
        {
            s.RecentHandoverMinutes.Enqueue(v);
        }
    }
}
