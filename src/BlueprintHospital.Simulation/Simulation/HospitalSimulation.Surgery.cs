using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>What a theatre is doing right now, for the Surgery panel.</summary>
public enum TheatreState
{
    /// <summary>Can't operate: something it needs is missing (see <see cref="TheatreBoardEntry.Problems"/>).</summary>
    Closed,

    /// <summary>Open, with nobody in it.</summary>
    Ready,

    /// <summary>The next patient is on their way to the anaesthetic room.</summary>
    Preparing,

    /// <summary>The anaesthetist is putting the patient to sleep in the anaesthetic room.</summary>
    Anaesthetic,

    /// <summary>The patient is being wheeled through into theatre.</summary>
    IntoTheatre,

    /// <summary>The surgeon is operating.</summary>
    Operating,

    /// <summary>Being cleaned and set up again after a case.</summary>
    Cleaning,

    /// <summary>The last patient is waking up on the table (no recovery bay was free).</summary>
    WakingUp,
}

/// <summary>A patient booked for an operation in theatre, for the Surgery panel's waiting list.</summary>
public sealed record SurgeryCase(
    Patient Patient,
    string Operation,
    string DepartmentId,
    SurgeryUrgency Urgency,
    DateTime BookedAt,
    TimeSpan Waited,
    TimeSpan? Target,
    bool Consented,
    bool BloodsBack,
    bool Fasted,
    TimeSpan FastedFor,
    string? TheatreId,
    SurgeryPhase Phase,
    bool InProgress,
    string? Problem);

/// <summary>One finished (or running) case on today's theatre timeline.</summary>
public sealed record TheatreCaseLog(string TheatreId, int PatientId, string Patient, string Operation, SurgeryUrgency Urgency, DateTime Start, DateTime? End);

/// <summary>A theatre's column on the Surgery panel's board.</summary>
public sealed record TheatreBoardEntry(
    CareSpace Theatre,
    CareSpace? AnaestheticRoom,
    TheatreState State,
    SurgeryCase? Current,
    double Progress,
    TimeSpan? Remaining,
    IReadOnlyList<StaffMember> Team,
    IReadOnlyList<string> Problems,
    IReadOnlyList<SurgeryCase> Next,
    DateTime? CleanUntil);

/// <summary>The day's theatre figures.</summary>
public sealed class SurgeryDay
{
    public DateTime Date { get; set; }

    public int Operations { get; set; }

    public int Breaches { get; set; }

    public int Postponed { get; set; }

    public long Income { get; set; }

    public int Complications { get; set; }
}

/// <summary>Saved theatre state.</summary>
public sealed class SurgeryState
{
    public Dictionary<string, DateTime> CleanUntil { get; init; } = new();

    public List<TheatreCaseLog> Log { get; init; } = new();

    public SurgeryDay? Today { get; init; }

    /// <summary>Each theatre's weekly sessions (only theatres with a session given to a planned list).</summary>
    public Dictionary<string, List<string>> Sessions { get; init; } = new();

    public List<TheatreRecoveryEntry> Recovery { get; init; } = new();
}

/// <summary>
/// Surgery. Every operating theatre needs an anaesthetic room built against it, joined by automatic
/// doors, as well as a surgeon and an anaesthetist. A porter brings the patient to the anaesthetic room,
/// the anaesthetist puts them to sleep there, then wheels them through onto the table and the surgeon
/// operates. Afterwards the theatre is cleaned and set up for the next case; meanwhile the next patient
/// can already be put to sleep next door.
/// <para>
/// Every operation has an urgency (Immediate within the hour, Urgent within 6 hours, Soon within 24
/// hours, or Planned) and an operation can't start until the patient is ready: consent taken, a group
/// and save blood sample back, and nil by mouth for 6 hours (an Immediate case goes straight in).
/// Starting late costs reputation. An Immediate case can take a theatre from a planned or Soon case
/// that hasn't been put to sleep yet; that patient is postponed and goes later.
/// </para>
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Game minutes to put a patient to sleep, for an average anaesthetist.</summary>
    public const double AnaestheticMinutes = 10;

    /// <summary>Game minutes to clean a theatre and set it up again after each case.</summary>
    public const double TheatreTurnaroundMinutes = 10;

    /// <summary>Hours since food before an anaesthetic is safe (nil by mouth).</summary>
    public const double FastingHours = 6;

    /// <summary>Minutes after booking before the surgeon has taken consent.</summary>
    public const double ConsentMinutes = 10;

    /// <summary>Minutes for a group and save blood sample to come back from the lab.</summary>
    public const double GroupAndSaveMinutes = 30;

    private readonly Dictionary<string, DateTime> _theatreCleanUntil = new();
    private readonly List<TheatreCaseLog> _theatreLog = new();
    private SurgeryDay _surgeryDay = new();

    /// <summary>The longest an operation of this urgency should wait.</summary>
    public static TimeSpan? UrgencyTarget(SurgeryUrgency urgency) => urgency switch
    {
        SurgeryUrgency.Immediate => TimeSpan.FromHours(1),
        SurgeryUrgency.Urgent => TimeSpan.FromHours(6),
        SurgeryUrgency.Soon => TimeSpan.FromHours(24),
        _ => null,
    };

    public static string UrgencyName(SurgeryUrgency urgency) => urgency switch
    {
        SurgeryUrgency.Immediate => "Immediate",
        SurgeryUrgency.Urgent => "Urgent",
        SurgeryUrgency.Soon => "Soon",
        _ => "Planned",
    };

    /// <summary>Today's theatre figures.</summary>
    public SurgeryDay SurgeryToday => _surgeryDay.Date == Now.Date ? _surgeryDay : new SurgeryDay { Date = Now.Date };

    /// <summary>Operations started or finished today, for the theatre timeline.</summary>
    public IReadOnlyList<TheatreCaseLog> TheatreLog => _theatreLog;

    private NavGrid BuildNav()
    {
        TheatreSuite.Link(_spaces.Values);
        return EmergencyDepartmentLayout.BuildNavGrid(Site, _spaces.Values);
    }

    /// <summary>The anaesthetic room joined to this theatre, if there is one.</summary>
    public CareSpace? AnaestheticRoomOf(CareSpace theatre) =>
        theatre.Kind == CareSpaceKind.Theatre && theatre.LinkedRoomId is { } id && FindSpace(id) is { Kind: CareSpaceKind.AnaestheticRoom } room ? room : null;

    /// <summary>The theatre this anaesthetic room is joined to, if any.</summary>
    public CareSpace? TheatreOf(CareSpace anaestheticRoom) =>
        anaestheticRoom.Kind == CareSpaceKind.AnaestheticRoom && anaestheticRoom.LinkedRoomId is { } id && FindSpace(id) is { Kind: CareSpaceKind.Theatre } theatre ? theatre : null;

    /// <summary>What a theatre still needs before it can operate (empty when it is open).</summary>
    public IReadOnlyList<string> TheatreProblems(CareSpace theatre)
    {
        var problems = new List<string>();
        if (!_staff.Any(s => s.BaseRoomId == theatre.Id && s.Role == StaffRole.Surgeon))
        {
            problems.Add("a surgeon");
        }

        if (!_staff.Any(s => s.BaseRoomId == theatre.Id && s.Role == StaffRole.Anaesthetist))
        {
            problems.Add("an anaesthetist");
        }

        if (AnaestheticRoomOf(theatre) is null)
        {
            problems.Add("an anaesthetic room built against it");
        }

        return problems;
    }

    /// <summary>An anaesthetic room must be built against the wall of a theatre that hasn't got one.</summary>
    private void CheckAnaestheticRoom(CareSpace room, CareSpace? replacing, List<string> problems)
    {
        var theatres = _spaces.Values.Where(s => s.Kind == CareSpaceKind.Theatre && s != replacing).ToList();
        var touching = theatres.Where(t => TheatreSuite.SharedDoor(room.Rect, t.Rect) is not null).ToList();
        if (touching.Count == 0)
        {
            problems.Add("Must be built against an operating theatre's wall (sharing at least 1 m of wall away from the corners).");
            return;
        }

        bool Free(CareSpace t) => t.LinkedRoomId is null || t.LinkedRoomId == replacing?.Id;
        if (!touching.Any(Free))
        {
            problems.Add($"{touching[0].Name} already has an anaesthetic room.");
        }
    }

    // ---------------------------------------------------------------- booking and getting ready

    /// <summary>The urgency of an operation for this condition.</summary>
    private SurgeryUrgency UrgencyOf(string? conditionId) =>
        conditionId is not null && Database.Conditions.TryGetValue(conditionId, out var condition)
            ? condition.IsPlanned ? SurgeryUrgency.Planned : condition.Operation?.Urgency ?? SurgeryUrgency.Urgent
            : SurgeryUrgency.Urgent;

    /// <summary>
    /// The patient is booked for theatre: they go nil by mouth, the surgeon will take consent and a group
    /// and save sample goes to the lab. Planned patients come in fasted with all of that done at pre-op clinic.
    /// </summary>
    private void BookSurgery(Patient patient, string? conditionId)
    {
        if (patient.SurgeryBookedAt is not null)
        {
            return;
        }

        var urgency = UrgencyOf(conditionId);
        patient.SurgeryBookedAt = Now;
        patient.SurgeryUrgency = urgency;
        patient.SurgeryBreached = false;
        patient.LastAteAt ??= LastMealBeforeArrival(patient);
        switch (urgency)
        {
            case SurgeryUrgency.Planned:
                patient.ConsentAt = Now;
                patient.GroupAndSaveAt = Now;
                patient.LastAteAt = Now - TimeSpan.FromHours(FastingHours + 2);
                break;
            case SurgeryUrgency.Immediate:
                // No time to wait: consent on the way, emergency O negative blood if needed.
                patient.ConsentAt = Now;
                patient.GroupAndSaveAt = null;
                break;
            default:
                patient.ConsentAt = Now + TimeSpan.FromMinutes(ConsentMinutes);
                patient.GroupAndSaveAt = Now + TimeSpan.FromMinutes(GroupAndSaveMinutes);
                break;
        }

        if (urgency != SurgeryUrgency.Planned)
        {
            var target = UrgencyTarget(urgency)!.Value;
            patient.Log(Now, $"{UrgencyName(urgency)} operation (within {(target.TotalHours >= 1 ? $"{target.TotalHours:0} hour{(target.TotalHours == 1 ? string.Empty : "s")}" : $"{target.TotalMinutes:0} minutes")}). Nil by mouth from now.");
        }
    }

    /// <summary>When the patient last ate before coming in: 2 to 10 hours before arrival, fixed by who they are (no random draws).</summary>
    private static DateTime LastMealBeforeArrival(Patient patient)
    {
        var hours = 2 + ((patient.Id * 7919 % 81) / 10.0);
        var meal = patient.ArrivedAt - TimeSpan.FromHours(hours);
        return patient.LastRefreshmentAt is { } snack && snack > meal ? snack : meal;
    }

    private bool Consented(Patient p) => p.ConsentAt is { } at && Now >= at;

    private bool BloodsBack(Patient p) => p.GroupAndSaveAt is not { } at || Now >= at;

    private TimeSpan FastedFor(Patient p) => Now - (p.LastAteAt ?? LastMealBeforeArrival(p));

    private bool Fasted(Patient p) => p.SurgeryUrgency == SurgeryUrgency.Immediate || FastedFor(p).TotalHours >= FastingHours;

    /// <summary>Consent taken, bloods back and nil by mouth long enough (always, for an Immediate case).</summary>
    public bool ReadyForTheatre(Patient patient) =>
        patient.SurgeryBookedAt is null || (Consented(patient) && BloodsBack(patient) && Fasted(patient));

    /// <summary>The patient's operation is finished (or they've left): they can eat again.</summary>
    private void EndSurgeryBooking(Patient patient)
    {
        patient.SurgeryBookedAt = null;
        patient.LastAteAt = null;
    }

    // ---------------------------------------------------------------- the anaesthetic room

    /// <summary>The anaesthetic room this operation goes through, when it is in a theatre that has one.</summary>
    private CareSpace? AnaestheticRoomFor(ClinicalTask task) =>
        task.Kind == TaskKind.Surgery && task.SpaceId is { } id && FindSpace(id) is { Kind: CareSpaceKind.Theatre } theatre ? AnaestheticRoomOf(theatre) : null;

    /// <summary>
    /// Takes an operation through the anaesthetic room: the patient is brought in and put to sleep, then
    /// the anaesthetist wheels them through the automatic doors onto the table (once the theatre is clean).
    /// Returns true while that is still going on; then the surgeon's operation runs as usual.
    /// </summary>
    private bool ProgressAnaesthetic(StaffMember surgeon, ClinicalTask task, Patient patient, double dt)
    {
        if (task.Kind != TaskKind.Surgery || task.Started || task.Phase == SurgeryPhase.Operating)
        {
            return false;
        }

        if (AnaestheticRoomFor(task) is not { } room || FindSpace(task.SpaceId!) is not { } theatre)
        {
            task.Phase = SurgeryPhase.Operating;
            return false;
        }

        if (task.Phase == SurgeryPhase.Waiting)
        {
            task.Phase = SurgeryPhase.Anaesthetic;
        }

        // The surgeon scrubs and waits in theatre.
        if (!surgeon.IsMoving && surgeon.Tile != theatre.StaffSpot)
        {
            WalkTo(surgeon, theatre.StaffSpot);
        }

        surgeon.StatusText = task.Phase == SurgeryPhase.Anaesthetic ? $"Scrubbing for {task.Label.ToLowerInvariant()}: {patient.FullName}" : $"{task.Label}: {patient.FullName}";
        var anaesthetist = _staff.FirstOrDefault(s => s.AssistingTaskId == task.Id);
        if (anaesthetist is null)
        {
            JoinAnaesthetist(task, theatre, patient);
            return true;
        }

        if (task.Phase == SurgeryPhase.Anaesthetic)
        {
            if (!anaesthetist.IsMoving && anaesthetist.Tile != room.StaffSpot && task.AnaestheticSecondsLeft is null)
            {
                WalkTo(anaesthetist, room.StaffSpot);
            }

            var onSpot = !patient.IsMoving && patient.PendingMove is null && patient.Tile == room.PatientSpot;
            if (onSpot && room.LinkDoors.Count > 0)
            {
                // Lined up on the trolley towards the sliding doors, ready to be wheeled straight through.
                var towards = room.LinkDoors[0].Side switch
                {
                    WallSide.North => (0.0, -1.0),
                    WallSide.South => (0.0, 1.0),
                    WallSide.West => (-1.0, 0.0),
                    _ => (1.0, 0.0),
                };
                if (patient.Facing != towards)
                {
                    patient.FollowAt(patient.X, patient.Y, towards);
                }
            }

            var inPlace = onSpot && !anaesthetist.IsMoving && anaesthetist.Tile == room.StaffSpot;
            if (task.AnaestheticSecondsLeft is null)
            {
                if (!inPlace)
                {
                    return true;
                }

                task.AnaestheticSecondsLeft = AnaestheticMinutes * 60 * anaesthetist.DurationFactor;
                anaesthetist.StatusText = $"Putting {patient.FullName} to sleep";
                patient.Log(Now, $"Anaesthetic given by {anaesthetist.Name} in {room.Name}, after checking their name, consent and when they last ate.");
            }

            task.AnaestheticSecondsLeft -= dt;
            if (task.AnaestheticSecondsLeft > 0)
            {
                return true;
            }

            task.Phase = SurgeryPhase.IntoTheatre;
        }

        // Asleep: through the automatic doors once the theatre is clean and set up.
        if ((_theatreCleanUntil.TryGetValue(theatre.Id, out var clean) && Now < clean) || TheatreHeldBy(theatre, patient))
        {
            anaesthetist.StatusText = $"Waiting for {theatre.Name} to be cleaned";
            return true;
        }

        if (patient.PushedByStaffId is null && patient.Tile != theatre.PatientSpot)
        {
            if (patient.IsMoving)
            {
                return true;
            }

            patient.Mobility = Mobility.Trolley;
            patient.PendingMove = null;
            anaesthetist.PushingPatientId = patient.Id;
            anaesthetist.StatusText = $"Wheeling {patient.FullName} into {theatre.Name}";
            patient.PushedByStaffId = anaesthetist.Id;
            patient.WalkSpeed = anaesthetist.WalkSpeed = Agents.Agent.DefaultWalkSpeed * PushSpeedFactor;
            _pushTrails[patient.Id] = new List<(double X, double Y)> { (patient.X, patient.Y) };
            Go(patient, theatre.PatientSpot);
            return true;
        }

        if (patient.IsMoving)
        {
            return true;
        }

        if (patient.PushedByStaffId == anaesthetist.Id)
        {
            EndPush(patient);
        }

        anaesthetist.StatusText = $"Anaesthetic: {patient.FullName}";
        WalkTo(anaesthetist, AnaesthetistSpot(theatre));
        task.Phase = SurgeryPhase.Operating;
        return false;
    }

    /// <summary>The surgeon has begun: note the start, and whether the patient waited longer than they should have.</summary>
    private void OperationStarted(ClinicalTask task, Patient patient)
    {
        if (task.Kind != TaskKind.Surgery || task.SpaceId is not { } theatreId || FindSpace(theatreId) is not { Kind: CareSpaceKind.Theatre })
        {
            return;
        }

        task.Phase = SurgeryPhase.Operating;
        task.OperationStartedAt = Now;
        var urgency = patient.SurgeryUrgency ?? UrgencyOf(task.Detail);
        RollSurgeryDay();
        _theatreLog.Add(new TheatreCaseLog(theatreId, patient.Id, patient.FullName, task.Label, urgency, Now, null));
        if (patient.SurgeryBookedAt is { } booked && UrgencyTarget(urgency) is { } target && Now - booked > target && !patient.SurgeryBreached)
        {
            patient.SurgeryBreached = true;
            _surgeryDay.Breaches++;
            Stats.ReputationPenalties++;
            patient.Log(Now, $"Went to theatre {FormatWait(Now - booked)} after booking, longer than the {FormatWait(target)} an {UrgencyName(urgency).ToLowerInvariant()} operation should wait.", important: true);
            Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName} waited {FormatWait(Now - booked)} for {LowerFirst(task.Label)} ({UrgencyName(urgency)}: within {FormatWait(target)}).", patient.Id);
        }
    }

    /// <summary>The operation is over: the theatre is cleaned before the next patient comes in, and the day's figures go up.</summary>
    private void OperationFinished(StaffMember surgeon, Patient patient, long income)
    {
        RollSurgeryDay();
        _surgeryDay.Operations++;
        _surgeryDay.Income += income;
        EndSurgeryBooking(patient);
        if (surgeon.BaseRoomId is { } theatreId && FindSpace(theatreId) is { Kind: CareSpaceKind.Theatre })
        {
            _theatreCleanUntil[theatreId] = Now + TimeSpan.FromMinutes(TheatreTurnaroundMinutes);
            var i = _theatreLog.FindLastIndex(l => l.TheatreId == theatreId && l.PatientId == patient.Id && l.End is null);
            if (i >= 0)
            {
                _theatreLog[i] = _theatreLog[i] with { End = Now };
            }
        }
    }

    /// <summary>What an operation pays.</summary>
    private int OperationFee(OperationDefinition operation) => operation.Tariff > 0 ? operation.Tariff : OperationTariff;

    private void RollSurgeryDay()
    {
        if (_surgeryDay.Date != Now.Date)
        {
            _surgeryDay = new SurgeryDay { Date = Now.Date };
            _theatreLog.RemoveAll(l => (l.End ?? Now).Date < Now.Date);
        }
    }

    private static string FormatWait(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00}" : $"{Math.Max(0, (int)t.TotalMinutes)} min";

    // ---------------------------------------------------------------- each tick

    private void UpdateSurgery()
    {
        RollSurgeryDay();
        foreach (var expired in _theatreCleanUntil.Where(c => Now >= c.Value || FindSpace(c.Key) is null).Select(c => c.Key).ToList())
        {
            _theatreCleanUntil.Remove(expired);
        }

        UpdateTheatreRecovery();
        if (Now.Second % 30 == 0)
        {
            MakeRoomForImmediateCases();
        }

        if (Now.Second == 0)
        {
            TidyTheatreSessions();
        }
    }

    /// <summary>
    /// An Immediate case waiting with every theatre that takes it busy can take one whose patient (a
    /// planned or Soon case) hasn't been put to sleep yet. That patient goes back and waits.
    /// </summary>
    private void MakeRoomForImmediateCases()
    {
        foreach (var task in _tasks.Values.Where(t => t.Kind == TaskKind.Surgery && t.AssignedStaffId is null).ToList())
        {
            if (FindPatient(task.PatientId) is not { } patient || (patient.SurgeryUrgency ?? UrgencyOf(task.Detail)) != SurgeryUrgency.Immediate
                || SurgeryDepartment(task.Detail) is not { } dept)
            {
                continue;
            }

            var theatres = SpacesOfKind(CareSpaceKind.Theatre).Where(t => IsStaffed(t) && t.AllowedDepartments.Contains(dept)).ToList();
            if (theatres.Count == 0 || theatres.Any(t => t.ReservedByStaffId is null))
            {
                continue;
            }

            var bump = _tasks.Values.FirstOrDefault(t => t.Kind == TaskKind.Surgery && t.AssignedStaffId is not null && !t.Started
                && t.AnaestheticSecondsLeft is null && t.Phase is SurgeryPhase.Waiting or SurgeryPhase.Anaesthetic
                && theatres.Any(th => th.Id == t.SpaceId)
                && FindPatient(t.PatientId) is { } other && (other.SurgeryUrgency ?? UrgencyOf(t.Detail)) is SurgeryUrgency.Planned or SurgeryUrgency.Soon);
            if (bump is null || FindPatient(bump.PatientId) is not { } bumped)
            {
                continue;
            }

            Postpone(bump, bumped, $"{FindSpace(bump.SpaceId!)!.Name} was needed for an emergency ({patient.FullName})");
        }
    }

    /// <summary>Hands an operation that hasn't started back to the waiting list.</summary>
    private void Postpone(ClinicalTask task, Patient patient, string why)
    {
        if (task.AssignedStaffId is { } surgeonId && FindStaff(surgeonId) is { } surgeon && surgeon.CurrentTaskId == task.Id)
        {
            surgeon.CurrentTaskId = null;
            surgeon.Activity = StaffActivity.Idle;
            surgeon.StatusText = "Available";
        }

        if (_staff.FirstOrDefault(s => s.AssistingTaskId == task.Id) is { } anaesthetist)
        {
            anaesthetist.AssistingTaskId = null;
            anaesthetist.Activity = StaffActivity.Idle;
            anaesthetist.StatusText = "Available";
        }

        if (task.SpaceId is { } id && FindSpace(id) is { } theatre && theatre.ReservedByStaffId == task.AssignedStaffId)
        {
            theatre.ReservedByStaffId = null;
        }

        task.AssignedStaffId = null;
        task.SpaceId = null;
        task.Phase = SurgeryPhase.Waiting;
        task.AnaestheticSecondsLeft = null;
        if (patient.BusyWithTaskId == task.Id)
        {
            patient.BusyWithTaskId = null;
        }

        ReturnFromServiceRoom(patient);
        RollSurgeryDay();
        _surgeryDay.Postponed++;
        patient.Log(Now, $"Operation postponed: {why}. They will go to theatre later.", important: true);
        Notifications.Add(Now, NotificationLevel.Info, $"{patient.FullName}'s {LowerFirst(task.Label)} postponed: {why}.", patient.Id);
    }

    // ---------------------------------------------------------------- the Surgery panel

    /// <summary>Everyone booked for an operation in theatre, most urgent first.</summary>
    public IReadOnlyList<SurgeryCase> SurgeryWaitingList() =>
        _tasks.Values.Where(t => t.Kind == TaskKind.Surgery && OperationRoom(t.Detail) == CareSpaceKind.Theatre)
            .Select(CaseOf)
            .Where(c => c is not null)
            .Select(c => c!)
            .OrderBy(c => c.Urgency)
            .ThenByDescending(c => c.Target is { } target ? c.Waited.TotalMinutes / target.TotalMinutes : 0)
            .ThenBy(c => c.BookedAt)
            .ToList();

    private SurgeryCase? CaseOf(ClinicalTask task)
    {
        if (FindPatient(task.PatientId) is not { } patient)
        {
            return null;
        }

        var urgency = patient.SurgeryUrgency ?? UrgencyOf(task.Detail);
        var booked = patient.SurgeryBookedAt ?? task.CreatedAt;
        var dept = SurgeryDepartment(task.Detail) ?? string.Empty;
        string? problem = null;
        if (task.AssignedStaffId is null)
        {
            if (!SpacesOfKind(CareSpaceKind.Theatre).Any(t => t.AllowedDepartments.Contains(dept)))
            {
                problem = $"No theatre takes {DepartmentName(dept)} patients";
            }
            else if (!HasTheatreFor(dept))
            {
                problem = "No open theatre takes them";
            }
            else if (!ReadyForTheatre(patient))
            {
                problem = "Getting ready";
            }
            else if (SpacesOfKind(CareSpaceKind.Theatre).Where(t => IsStaffed(t) && t.AllowedDepartments.Contains(dept)).All(t => !SessionTakes(t, task)))
            {
                problem = "Waiting for an emergency session";
            }
        }

        return new SurgeryCase(patient, task.Label, dept, urgency, booked, Now - booked, UrgencyTarget(urgency),
            Consented(patient), BloodsBack(patient), Fasted(patient), FastedFor(patient), task.AssignedStaffId is null ? null : task.SpaceId,
            task.Phase, task.AssignedStaffId is not null, problem);
    }

    /// <summary>Every operating theatre: what it is doing, its team, and who is next.</summary>
    public IReadOnlyList<TheatreBoardEntry> TheatreBoard()
    {
        var waiting = SurgeryWaitingList();
        var board = new List<TheatreBoardEntry>();
        foreach (var theatre in SpacesOfKind(CareSpaceKind.Theatre).OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var problems = TheatreProblems(theatre);
            var task = _tasks.Values.FirstOrDefault(t => t.Kind == TaskKind.Surgery && t.SpaceId == theatre.Id && t.AssignedStaffId is not null);
            var current = task is null ? null : CaseOf(task);
            var clean = _theatreCleanUntil.TryGetValue(theatre.Id, out var until) && Now < until ? until : (DateTime?)null;
            var progress = 0.0;
            TimeSpan? remaining = null;
            TheatreState state;
            if (task is not null)
            {
                state = task.Phase switch
                {
                    _ when task.Started => TheatreState.Operating,
                    SurgeryPhase.Anaesthetic when task.AnaestheticSecondsLeft is not null => TheatreState.Anaesthetic,
                    SurgeryPhase.IntoTheatre or SurgeryPhase.Operating => TheatreState.IntoTheatre,
                    _ => TheatreState.Preparing,
                };
                if (task.Started && FindStaff(task.AssignedStaffId!.Value) is { } surgeon)
                {
                    remaining = TimeSpan.FromSeconds(Math.Max(0, surgeon.WorkRemainingSeconds));
                    var total = (Now - (task.OperationStartedAt ?? Now)).TotalSeconds + surgeon.WorkRemainingSeconds;
                    progress = total > 0 ? Math.Clamp(1 - (surgeon.WorkRemainingSeconds / total), 0, 1) : 0;
                }
                else if (task.AnaestheticSecondsLeft is { } left)
                {
                    remaining = TimeSpan.FromSeconds(Math.Max(0, left));
                }
            }
            else if (problems.Count > 0)
            {
                state = TheatreState.Closed;
            }
            else
            {
                state = WakingOnTable(theatre) ? TheatreState.WakingUp : clean is not null || TheatreHeldBy(theatre) ? TheatreState.Cleaning : TheatreState.Ready;
            }

            var team = _staff.Where(s => s.BaseRoomId == theatre.Id).OrderBy(s => s.Role).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
            var next = waiting.Where(c => !c.InProgress && theatre.AllowedDepartments.Contains(c.DepartmentId)).Take(4).ToList();
            board.Add(new TheatreBoardEntry(theatre, AnaestheticRoomOf(theatre), state, current, progress, remaining, team, problems, next, clean));
        }

        return board;
    }

    // ---------------------------------------------------------------- saving

    private SurgeryState CaptureSurgery() => new()
    {
        CleanUntil = new Dictionary<string, DateTime>(_theatreCleanUntil),
        Log = _theatreLog.ToList(),
        Today = _surgeryDay,
        Sessions = _theatreSessions.ToDictionary(s => s.Key, s => s.Value.ToList()),
        Recovery = _theatreRecovery.Values.ToList(),
    };

    private void RestoreSurgery(SurgeryState? state)
    {
        _theatreCleanUntil.Clear();
        _theatreLog.Clear();
        _theatreSessions.Clear();
        _theatreRecovery.Clear();
        _surgeryDay = new SurgeryDay { Date = Now.Date };
        if (state is null)
        {
            return;
        }

        foreach (var (id, until) in state.CleanUntil)
        {
            _theatreCleanUntil[id] = until;
        }

        _theatreLog.AddRange(state.Log);
        foreach (var (id, slots) in state.Sessions.Where(s => s.Value.Count == TheatreSessions.PerWeek))
        {
            _theatreSessions[id] = slots.ToArray();
        }

        foreach (var entry in state.Recovery)
        {
            _theatreRecovery[entry.PatientId] = entry;
        }

        if (state.Today is { } today)
        {
            _surgeryDay = today;
        }
    }
}
