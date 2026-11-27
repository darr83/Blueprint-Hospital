using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>A mental health ward patient's spell in the ward lounge or the garden.</summary>
public sealed class DayRoomVisit
{
    public required Patient Patient { get; init; }

    public required string RoomId { get; init; }

    /// <summary>The seat (sofa, chair or bench) they sit on.</summary>
    public required GridCoord Seat { get; init; }

    /// <summary>When they head back to their room.</summary>
    public DateTime Until { get; set; }

    /// <summary>Whether they have got there and sat down.</summary>
    public bool Arrived { get; set; }
}

/// <summary>
/// Mental Health, stage 2: the adult mental health ward. Patients who need admitting stay in single
/// en-suite bedrooms for a few days, looked after by mental health nurses, a psychiatrist and support
/// workers. Other patients never go there, and mental health patients go nowhere else.
///
/// Every patient has a wellbeing meter (settled, unsettled or distressed) that shows on their monitor in
/// place of the NEWS2 colour, and one of four observation levels. On general (hourly) and intermittent
/// (every 15 minutes) observations a nurse or support worker looks in on them wherever they are. On
/// within-eyesight and arm's-length observations someone stays with them all the time, which ties up a
/// member of staff. By day patients spend time in the ward lounge and the garden, which helps them settle.
/// The psychiatrist reviews everyone once a day and steps observations up or down. Nobody goes home until
/// they are settled and back on general observations.
///
/// After an overdose, a patient on a medical ward is seen by the liaison team before going home, when the
/// hospital has a staffed mental health assessment room.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Mental health nurses on a mental health ward: one for every six beds.</summary>
    public const int PatientsPerMentalHealthNurse = 6;

    /// <summary>Support workers on a mental health ward: one for every six beds (more help with close observations).</summary>
    public const int PatientsPerSupportWorker = 6;

    /// <summary>How often a patient on general observations is checked on.</summary>
    public static readonly TimeSpan GeneralObsEvery = TimeSpan.FromHours(1);

    /// <summary>How often a patient on intermittent observations is checked on.</summary>
    public static readonly TimeSpan IntermittentObsEvery = TimeSpan.FromMinutes(15);

    /// <summary>A check this late counts as missed, and unsettles the patient.</summary>
    private static readonly TimeSpan ObsCheckLate = TimeSpan.FromMinutes(20);

    /// <summary>How long a discharge is put off when the patient isn't ready yet.</summary>
    public static readonly TimeSpan MentalHealthDischargeHold = TimeSpan.FromHours(12);

    /// <summary>On average a patient who is up to it goes to the lounge or garden about once in this many minutes, by day.</summary>
    private const double DayRoomEveryMinutes = 60;

    // Wellbeing change per hour.
    private const double WellbeingDriftPerHour = 0.005;
    private const double DayRoomWellbeingPerHour = 0.015;
    private const double UnwatchedWellbeingPerHour = -0.03;
    private const double MissedCheckWellbeingPerHour = -0.02;
    private const double UnsettledMomentChancePerHour = 0.02;
    private const double UnsettledMomentDrop = 0.15;
    private const double ReviewWellbeingBoost = 0.08;

    /// <summary>Below this wellbeing someone is put on (or kept on) close observations.</summary>
    public const double CloseObservationWellbeing = 0.2;

    private readonly Dictionary<Patient, DayRoomVisit> _dayRoomVisits = new();
    private readonly Dictionary<int, DateTime> _lastDayRoomAt = new();
    private GameRandom? _mentalHealthRandom;

    /// <summary>A separate random stream, so the mental health ward doesn't change anything else that happens.</summary>
    private GameRandom MentalHealthRandom => _mentalHealthRandom ??= new GameRandom(_options.Seed + 577_215);

    /// <summary>A ward or unit that belongs to Mental Health.</summary>
    public bool IsMentalHealthWard(CareSpace space) => space.IsWard && space.DepartmentId == MentalHealthDepartmentId;

    public IEnumerable<CareSpace> MentalHealthWards => Wards.Where(IsMentalHealthWard);

    /// <summary>The ward lounges and garden courtyards.</summary>
    public IEnumerable<CareSpace> DayRooms => _spaces.Values.Where(s => s.Kind is CareSpaceKind.MentalHealthLounge or CareSpaceKind.MentalHealthGarden).OrderBy(s => s.Id, StringComparer.Ordinal);

    /// <summary>Patients in the lounge or garden (or on their way) right now.</summary>
    public IReadOnlyCollection<DayRoomVisit> DayRoomVisits => _dayRoomVisits.Values;

    /// <summary>The lounge or garden a patient is sitting in (once they have got there).</summary>
    public CareSpace? DayRoomOf(Patient patient) =>
        _dayRoomVisits.TryGetValue(patient, out var visit) && visit.Arrived && patient.Tile == visit.Seat ? FindSpace(visit.RoomId) : null;

    private bool InDayRoom(Patient patient) => _dayRoomVisits.ContainsKey(patient);

    public bool IsOnMentalHealthWard(Patient patient) =>
        patient.Stage == PatientStage.OnWard && patient.CareSpaceId is { } id && FindSpace(id) is { } ward && IsMentalHealthWard(ward);

    /// <summary>Within eyesight or arm's length: someone must be with them all the time.</summary>
    public bool NeedsCloseObservation(Patient patient) =>
        patient.Wellbeing is not null && patient.ObservationLevel >= ObservationLevel.WithinEyesight && patient.IsInDepartment && IsOnMentalHealthWard(patient);

    public static string ObservationLevelName(ObservationLevel level) => level switch
    {
        ObservationLevel.General => "General",
        ObservationLevel.Intermittent => "Intermittent",
        ObservationLevel.WithinEyesight => "Within eyesight",
        _ => "Arm's length",
    };

    public static string WellbeingName(WellbeingBand band) => band switch
    {
        WellbeingBand.Settled => "Settled",
        WellbeingBand.Unsettled => "Unsettled",
        _ => "Distressed",
    };

    /// <summary>A support worker does close observations when the ward has one; otherwise a mental health nurse.</summary>
    private StaffRole CloseObservationRole(Patient patient) =>
        _staff.Any(s => s.Role == StaffRole.SupportWorker && s.BaseRoomId == patient.CareSpaceId && s.IsOnDuty) ? StaffRole.SupportWorker : StaffRole.MentalHealthNurse;

    private void CallForCloseObservation(Patient patient)
    {
        var level = ObservationLevelName(patient.ObservationLevel).ToLowerInvariant();
        AddTask(TaskKind.StayWithPatient, CloseObservationRole(patient), patient, 0.5, $"Close observation ({level})");
    }

    /// <summary>Mental health nurses do the nursing (and support workers' jobs) on their own ward.</summary>
    private bool MentalHealthNurseCanCover(ClinicalTask task, StaffMember member) =>
        member.Role == StaffRole.MentalHealthNurse && OnMentalHealthWardTeam(member)
        && (task.Role is StaffRole.Nurse or StaffRole.SupportWorker || (task.Role == StaffRole.HealthcareAssistant && task.Kind != TaskKind.StayWithPatient))
        && task.Kind is not (TaskKind.MovePatient or TaskKind.CarryBaby or TaskKind.DeliverMedicines or TaskKind.LiaisonReview);

    /// <summary>Support workers do the healthcare assistants' jobs, close observations and look-ins on their own ward.</summary>
    private bool SupportWorkerCanCover(ClinicalTask task, StaffMember member) =>
        member.Role == StaffRole.SupportWorker && OnMentalHealthWardTeam(member)
        && (task.Role == StaffRole.HealthcareAssistant || task.Kind is TaskKind.StayWithPatient or TaskKind.ObservationCheck);

    /// <summary>Based on a mental health ward.</summary>
    private bool OnMentalHealthWardTeam(StaffMember member) => member.BaseRoomId is { } id && FindSpace(id) is { } ward && IsMentalHealthWard(ward);

    /// <summary>A mental health nurse or psychiatrist based in an assessment room: the liaison team.</summary>
    private bool IsLiaisonMember(StaffMember member) =>
        IsMentalHealthRole(member.Role) && member.BaseRoomId is { } id && FindSpace(id) is { Kind: CareSpaceKind.MentalHealthAssessment };

    private bool HasLiaisonTeam => _staff.Any(IsLiaisonMember);

    /// <summary>Patients the liaison team saw on a medical ward after an overdose.</summary>
    public int LiaisonWardReviews => (int)HistoryStore.Total(StatsPeriod.AllTime, Now, StatKeys.LiaisonWardReviews);

    // ---------------------------------------------------------------- admission

    /// <summary>Starts the wellbeing meter and sets the observation level for someone arriving on the ward.</summary>
    private void AdmitToMentalHealthWard(Patient patient, ConditionDefinition diagnosed)
    {
        var risky = patient.Symptoms.Contains("thoughts_of_suicide") || patient.Symptoms.Contains("self_inflicted_cuts");
        var start = diagnosed.Id switch
        {
            "severe_depression" => 0.34,
            "first_episode_psychosis" => 0.36,
            "mania" => 0.32,
            _ => 0.38,
        };

        // Most arrive unsettled; about one in four (more if they are at risk) arrive distressed and need someone with them.
        patient.Wellbeing = Math.Clamp(start + MentalHealthRandom.Range(-0.1, 0.1) - (risky ? 0.08 : 0), 0.05, 0.5);
        patient.ObservationLevel = patient.WellbeingBand == WellbeingBand.Distressed
            ? risky && MentalHealthRandom.Chance(0.3) ? ObservationLevel.ArmsLength : ObservationLevel.WithinEyesight
            : ObservationLevel.Intermittent;
        patient.NextObsCheckAt = Now + ObsInterval(patient.ObservationLevel);
        patient.LastMentalHealthReviewAt = null;
        patient.Mobility = Mobility.Walking;
        patient.Log(Now, $"Settling in to their own room. Wellbeing: {WellbeingName(patient.WellbeingBand!.Value).ToLowerInvariant()}. Observations: {ObservationLevelName(patient.ObservationLevel).ToLowerInvariant()}.", important: true);
    }

    private static TimeSpan ObsInterval(ObservationLevel level) => level == ObservationLevel.General ? GeneralObsEvery : IntermittentObsEvery;

    // ---------------------------------------------------------------- every tick

    private void UpdateMentalHealthWards(double dt)
    {
        foreach (var visit in _dayRoomVisits.Values.ToList())
        {
            ProgressDayRoomVisit(visit);
        }

        var hours = dt / 3600;
        foreach (var patient in _patients.Values.Where(p => p.Wellbeing is not null).OrderBy(p => p.Id).ToList())
        {
            if (!IsOnMentalHealthWard(patient) || !patient.IsAlive)
            {
                continue;
            }

            UpdateWellbeing(patient, hours);
            ScheduleObservationCheck(patient);
            ScheduleMentalHealthReview(patient);
        }

        SendDayRoomStraysBack();

        if (Now.Hour is >= 8 and < 21 && DayRooms.Any())
        {
            var chance = dt / (DayRoomEveryMinutes * 60);
            foreach (var patient in _patients.Values.Where(CanGoToDayRoom).OrderBy(p => p.Id).ToList())
            {
                if (MentalHealthRandom.Chance(chance))
                {
                    StartDayRoomVisit(patient);
                }
            }
        }
    }

    private void UpdateWellbeing(Patient patient, double hours)
    {
        var before = patient.WellbeingBand!.Value;
        var change = WellbeingDriftPerHour;
        if (DayRoomOf(patient) is not null)
        {
            change += DayRoomWellbeingPerHour;
        }

        if (NeedsCloseObservation(patient) && CarerAtBedside(patient) is null)
        {
            change += UnwatchedWellbeingPerHour;
        }

        if (_tasks.Values.Any(t => t.PatientId == patient.Id && t.Kind == TaskKind.ObservationCheck && Now - t.CreatedAt > ObsCheckLate))
        {
            change += MissedCheckWellbeingPerHour;
        }

        patient.Wellbeing = Math.Clamp(patient.Wellbeing!.Value + (change * hours), 0, 1);
        if (MentalHealthRandom.Chance(UnsettledMomentChancePerHour * hours))
        {
            patient.Wellbeing = Math.Max(0, patient.Wellbeing.Value - UnsettledMomentDrop);
            patient.Log(Now, "Had a difficult spell and became unsettled.");
        }

        if (patient.Wellbeing < CloseObservationWellbeing && patient.ObservationLevel < ObservationLevel.WithinEyesight)
        {
            patient.ObservationLevel = ObservationLevel.WithinEyesight;
            patient.Log(Now, "Distressed: put on within-eyesight observations, so someone stays with them.", important: true);
        }

        var after = patient.WellbeingBand!.Value;
        if (after != before)
        {
            LogRoom(patient.CareSpaceId, RoomLogKind.RedMonitor, $"{patient.FullName} is now {WellbeingName(after).ToLowerInvariant()}");
        }
    }

    private void ScheduleObservationCheck(Patient patient)
    {
        if (patient.ObservationLevel >= ObservationLevel.WithinEyesight)
        {
            // Someone is with them all the time instead.
            RemoveTasks(patient, TaskKind.ObservationCheck);
            return;
        }

        patient.NextObsCheckAt ??= Now + ObsInterval(patient.ObservationLevel);
        if (Now >= patient.NextObsCheckAt && !HasTask(patient, TaskKind.ObservationCheck))
        {
            AddTask(TaskKind.ObservationCheck, StaffRole.MentalHealthNurse, patient, 1, patient.ObservationLevel == ObservationLevel.General ? "Hourly check" : "15-minute check");
        }
    }

    private void ScheduleMentalHealthReview(Patient patient)
    {
        var due = patient.LastMentalHealthReviewAt is not { } last || Now - last >= TimeSpan.FromHours(20);
        if (due && Now.Hour is >= 9 and < 17 && !HasTask(patient, TaskKind.MentalHealthReview) && !HasTask(patient, TaskKind.WardClerking)
            && patient.AdmittedAt is { } admitted && Now - admitted >= TimeSpan.FromHours(2))
        {
            AddTask(TaskKind.MentalHealthReview, StaffRole.Psychiatrist, patient, 20, "Psychiatrist's review");
        }
    }

    private void CompleteObservationCheck(StaffMember member, Patient patient)
    {
        patient.NextObsCheckAt = Now + ObsInterval(patient.ObservationLevel);
        if (patient.Wellbeing is { } w)
        {
            // A friendly face helps a little.
            patient.Wellbeing = Math.Min(1, w + 0.001);
        }

        var where = DayRoomOf(patient) is { } room ? $" in {room.Name}" : string.Empty;
        patient.Log(Now, $"Checked on{where} by {member.Name} ({ObservationLevelName(patient.ObservationLevel).ToLowerInvariant()} observations).");
    }

    /// <summary>The psychiatrist sees how they are and steps observations up or down.</summary>
    private void CompleteMentalHealthReview(StaffMember member, Patient patient)
    {
        patient.LastMentalHealthReviewAt = Now;
        if (patient.Wellbeing is not { } w)
        {
            return;
        }

        w = Math.Min(1, w + (ReviewWellbeingBoost * (0.5 + member.Skill)));
        patient.Wellbeing = w;
        var was = patient.ObservationLevel;
        var level = w >= Patient.SettledWellbeing ? ObservationLevel.General
            : w >= 0.35 ? ObservationLevel.Intermittent
            : w >= CloseObservationWellbeing ? (ObservationLevel)Math.Min((int)was, (int)ObservationLevel.WithinEyesight)
            : (ObservationLevel)Math.Max((int)was, (int)ObservationLevel.WithinEyesight);
        patient.ObservationLevel = level;
        patient.NextObsCheckAt = Now + ObsInterval(level);
        var change = level == was ? $"stays on {ObservationLevelName(level).ToLowerInvariant()} observations"
            : level < was ? $"observations stepped down to {ObservationLevelName(level).ToLowerInvariant()}"
            : $"observations stepped up to {ObservationLevelName(level).ToLowerInvariant()}";
        patient.Log(Now, $"Reviewed by {member.Name}: {WellbeingName(patient.WellbeingBand!.Value).ToLowerInvariant()}, {change}.", important: level != was);
    }

    /// <summary>
    /// Not ready for home yet (not settled, or not back on general observations): the team look again in 12 hours.
    /// Returns true when the discharge is put off.
    /// </summary>
    private bool HoldMentalHealthDischarge(Patient patient)
    {
        if (patient.Wellbeing is null || !IsOnMentalHealthWard(patient)
            || (patient.WellbeingBand == WellbeingBand.Settled && patient.ObservationLevel == ObservationLevel.General))
        {
            return false;
        }

        patient.DischargeDueAt = Now + MentalHealthDischargeHold;
        patient.Log(Now, $"Not ready for home yet ({WellbeingName(patient.WellbeingBand!.Value).ToLowerInvariant()}, {ObservationLevelName(patient.ObservationLevel).ToLowerInvariant()} observations). The team will look again at {patient.DischargeDueAt:HH:mm} {patient.DischargeDueAt:ddd}.");
        return true;
    }

    // ---------------------------------------------------------------- the liaison team on the medical wards

    /// <summary>
    /// After an overdose the liaison team see the patient on their ward before they go home. Returns true while
    /// that review is still to come (and asks for it). Without a liaison team they simply go home.
    /// </summary>
    private bool WaitForLiaisonReview(Patient patient)
    {
        if (patient.LiaisonReviewed || patient.WorkingDiagnosis?.ConditionId != "paracetamol_overdose" || !HasLiaisonTeam)
        {
            return false;
        }

        if (!HasTask(patient, TaskKind.LiaisonReview))
        {
            AddTask(TaskKind.LiaisonReview, StaffRole.MentalHealthNurse, patient, 30, "Liaison review on the ward");
            patient.Log(Now, "Medically fit for home: the mental health liaison team have been asked to see them first.");
        }

        return true;
    }

    private void CompleteLiaisonReview(StaffMember member, Patient patient)
    {
        patient.LiaisonReviewed = true;
        Tally(StatKeys.LiaisonWardReviews);
        patient.Log(Now, $"Seen on the ward by {member.Name} from the mental health liaison team. Home with the crisis team's number and a follow-up.", important: true);
    }

    // ---------------------------------------------------------------- the lounge and the garden

    /// <summary>On a mental health ward, walking, in their room with nothing being done, and not been out lately.</summary>
    private bool CanGoToDayRoom(Patient p) =>
        p.Wellbeing is not null && IsOnMentalHealthWard(p) && p.IsAlive && !p.InArrest && !p.OnFloorAfterFall
        && p.Mobility == Mobility.Walking && !p.IsMoving && p.BusyWithTaskId is null && p.PendingMove is null
        && BedOf(p) is { } bed && p.Tile == bed.PatientSpot
        && !_tasks.Values.Any(t => t.PatientId == p.Id && t.Kind is not (TaskKind.ObservationCheck or TaskKind.StayWithPatient))
        && !_dayRoomVisits.ContainsKey(p) && StrollOf(p) is null
        && !(_lastDayRoomAt.TryGetValue(p.Id, out var at) && Now - at < TimeSpan.FromHours(2));

    private void StartDayRoomVisit(Patient patient)
    {
        _lastDayRoomAt[patient.Id] = Now;
        var floor = Site.FloorOf(patient.Tile);
        var rooms = DayRooms.Where(r => Site.FloorOf(r.Rect) == floor && r.Seats.Count > 0).ToList();
        if (rooms.Count == 0)
        {
            return;
        }

        // The garden is for dry days; the lounge any time.
        if (Weather is { Sky: Sky.Rain or Sky.HeavyRain or Sky.Thunderstorm or Sky.Snow })
        {
            rooms = rooms.Where(r => r.Kind != CareSpaceKind.MentalHealthGarden).ToList();
        }

        var room = rooms.Count == 0 ? null : rooms[MentalHealthRandom.Range(0, rooms.Count - 1)];
        var free = room?.Seats.Where(s => !_dayRoomVisits.Values.Any(v => v.Seat == s) && !IsSpotTaken(s, patient)).ToList();
        if (room is null || free is null || free.Count == 0)
        {
            return;
        }

        var seat = free[MentalHealthRandom.Range(0, free.Count - 1)];
        _dayRoomVisits[patient] = new DayRoomVisit { Patient = patient, RoomId = room.Id, Seat = seat };
        WalkTo(patient, seat);
    }

    private void ProgressDayRoomVisit(DayRoomVisit visit)
    {
        var p = visit.Patient;
        var called = _tasks.Values.Any(t => t.PatientId == p.Id && t.Kind is not (TaskKind.ObservationCheck or TaskKind.StayWithPatient));
        var heading = p.Destination is { } to && to != visit.Seat;
        if (!IsOnMentalHealthWard(p) || !p.IsAlive || p.InArrest || p.PendingMove is not null || heading || FindSpace(visit.RoomId) is null)
        {
            _dayRoomVisits.Remove(p);
            return;
        }

        if (called || (visit.Arrived && Now >= visit.Until))
        {
            EndDayRoomVisit(visit);
            return;
        }

        if (!visit.Arrived && !p.IsMoving)
        {
            if (p.Tile != visit.Seat)
            {
                // Couldn't get there (the room was rebuilt): back to their room.
                EndDayRoomVisit(visit);
                return;
            }

            visit.Arrived = true;
            visit.Until = Now + TimeSpan.FromMinutes(MentalHealthRandom.Range(30, 90));
            var room = FindSpace(visit.RoomId)!;
            var what = room.Kind == CareSpaceKind.MentalHealthGarden
                ? MentalHealthRandom.Chance(0.5) ? "sat in the garden for some fresh air" : "pottered about the planters in the garden"
                : MentalHealthRandom.Range(0, 2) switch
                {
                    0 => "watched television in the lounge",
                    1 => "did a jigsaw in the lounge",
                    _ => "chatted with other patients in the lounge",
                };
            p.Log(Now, $"Went out of their room and {what}.");
        }
    }

    private void EndDayRoomVisit(DayRoomVisit visit)
    {
        _dayRoomVisits.Remove(visit.Patient);
        if (BedOf(visit.Patient) is { } bed)
        {
            WalkTo(visit.Patient, bed.PatientSpot);
        }
    }

    /// <summary>A ward patient left in the lounge or garden without a visit (after loading a game) goes back to their room.</summary>
    private void SendDayRoomStraysBack()
    {
        var rooms = DayRooms.ToList();
        if (rooms.Count == 0)
        {
            return;
        }

        foreach (var p in _patients.Values)
        {
            if (p.Stage == PatientStage.OnWard && !p.IsMoving && p.PendingMove is null && !_dayRoomVisits.ContainsKey(p) && StrollOf(p) is null
                && BedOf(p) is { } bed && p.Tile != bed.PatientSpot && rooms.Any(r => r.Rect.Contains(p.Tile)))
            {
                WalkTo(p, bed.PatientSpot);
            }
        }
    }
}
