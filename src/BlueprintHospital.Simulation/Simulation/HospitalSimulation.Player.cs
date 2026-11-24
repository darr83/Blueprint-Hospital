using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>A test the player's doctor could request for a patient, and whether they can.</summary>
public sealed record TestOption(InvestigationDefinition Test, bool AlreadyRequested, bool Available, string Note, bool Relevant);

/// <summary>The answer to something the player's doctor tried to do: whether it happened, and what to tell them.</summary>
public sealed record PlayerResult(bool Ok, string Message)
{
    public static PlayerResult Fail(string why) => new(false, why);

    public static PlayerResult Done(string what) => new(true, what);
}

/// <summary>
/// Be A Doctor: the player takes over one doctor. That doctor stops choosing work for themselves; the
/// player walks them about, picks their tasks and makes the clinical decisions (which tests to request
/// and what the diagnosis is). Every other member of staff carries on as usual.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How close (in tiles) the player's doctor must be to the place a task is done to start it.</summary>
    public const int PlayerReach = 2;

    /// <summary>How far the player's doctor can step back from a job in progress before it stops.</summary>
    private const int PlayerWorkRange = 3;

    /// <summary>How near the walls and furniture the player's doctor can get, in tiles.</summary>
    private const double PlayerRadius = 0.28;

    /// <summary>True while "Decide for me" is running, so the doctor's own judgement is used.</summary>
    private bool _delegating;

    /// <summary>Patients left without a plan are handed back to the other doctors on the next tick.</summary>
    private bool _undecidedSweepDue = true;

    /// <summary>The doctor the player is playing as, if any. Not saved: a loaded game starts in the manager's seat.</summary>
    public int? PlayerDoctorId { get; private set; }

    public StaffMember? PlayerDoctor => PlayerDoctorId is { } id ? FindStaff(id) : null;

    private bool IsPlayer(StaffMember member) => PlayerDoctorId == member.Id;

    /// <summary>Doctors the player could play as right now: at work, and not running a clinic or at a crash call.</summary>
    public IEnumerable<StaffMember> PlayableDoctors => _staff.Where(CanBePlayed);

    public bool CanBePlayed(StaffMember member) =>
        member.Role == StaffRole.Doctor && member.IsOnDuty && member.Shift == CurrentShift
        && member.CrashPatientId is null && member.AssistingTaskId is null && !IsClinicStaff(member);

    /// <summary>The player takes over <paramref name="staffId"/>. They drop what they were about to do and wait for the player.</summary>
    public PlayerResult TakeControl(int staffId)
    {
        if (FindStaff(staffId) is not { } member || member.Role != StaffRole.Doctor)
        {
            return PlayerResult.Fail("Only a doctor can be played.");
        }

        if (!CanBePlayed(member))
        {
            return PlayerResult.Fail(!member.IsOnDuty || member.Shift != CurrentShift
                ? $"{member.Name} is not on shift."
                : $"{member.Name} can't be taken over right now.");
        }

        ReleaseControl();
        if (member.CurrentTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task))
        {
            ReleaseFromTask(member, task);
        }

        if (member.IsOnBreak)
        {
            FinishBreak(member);
            member.BreakEndsAt = null;
        }

        member.CurrentTaskId = null;
        member.PagedUntil = null;
        member.StopMoving();
        member.Activity = StaffActivity.Idle;
        member.StatusText = "Played by you";
        PlayerDoctorId = member.Id;
        return PlayerResult.Done($"You are now {member.Name}.");
    }

    /// <summary>Hands the doctor back: they pick their own work again, and patients left without a plan are reviewed.</summary>
    public void ReleaseControl()
    {
        if (PlayerDoctor is { } member)
        {
            if (member.CurrentTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task))
            {
                ReleaseFromTask(member, task);
            }

            member.CurrentTaskId = null;
            member.StopMoving();
            if (member.IsOnDuty)
            {
                member.Activity = StaffActivity.Idle;
                member.StatusText = "Available";
            }
        }

        PlayerDoctorId = null;
        _undecidedSweepDue = true;
        _decisionWaitingSince.Clear();
        _handedOn.Clear();
    }

    /// <summary>
    /// A patient who has been seen but has no diagnosis, no tests outstanding and no review waiting would
    /// sit there for ever. With nobody playing a doctor, each gets a review for the doctors to pick up.
    /// </summary>
    private void RescueUndecidedPatients()
    {
        if (!_undecidedSweepDue || PlayerDoctorId is not null)
        {
            return;
        }

        _undecidedSweepDue = false;
        foreach (var patient in _patients.Values.Where(NeedsDecision).ToList())
        {
            AddTask(TaskKind.DoctorReview, StaffRole.Doctor, patient, 8, "Review and decide");
        }
    }

    /// <summary>Seen by a doctor, nothing outstanding, and still no diagnosis: someone has to decide what happens next.</summary>
    public bool NeedsDecision(Patient patient) =>
        patient.HistoryTaken && patient.WorkingDiagnosis is null && patient.IsInDepartment && patient.IsInEmergency
        && !patient.Maternity && !patient.Condition.IsPlanned && !patient.InArrest
        && patient.Investigations.All(o => o.Status == InvestigationStatus.Resulted)
        && !HasTask(patient, TaskKind.DoctorReview) && !HasTask(patient, TaskKind.DoctorAssessment);

    /// <summary>How long a patient waits for the player's decision before a colleague is asked to step in.</summary>
    public static readonly TimeSpan PlayerDecisionPatience = TimeSpan.FromMinutes(90);

    private readonly Dictionary<int, DateTime> _decisionWaitingSince = new();

    /// <summary>True for a patient the player's doctor has seen: their results and decisions wait for the player.</summary>
    public bool IsPlayersPatient(Patient patient) =>
        PlayerDoctor is { } member && member.Caseload.Contains(patient.Id) && !_handedOn.Contains(patient.Id);

    private readonly HashSet<int> _handedOn = new();

    /// <summary>A patient the player has left undecided for too long is put on the board for any doctor to review.</summary>
    private void HandOnForgottenDecisions()
    {
        var waiting = PlayerDecisionsWaiting();
        foreach (var gone in _decisionWaitingSince.Keys.Where(id => waiting.All(p => p.Id != id)).ToList())
        {
            _decisionWaitingSince.Remove(gone);
        }

        foreach (var patient in waiting)
        {
            if (!_decisionWaitingSince.TryGetValue(patient.Id, out var since))
            {
                _decisionWaitingSince[patient.Id] = Now;
            }
            else if (Now - since >= PlayerDecisionPatience)
            {
                _decisionWaitingSince.Remove(patient.Id);
                _handedOn.Add(patient.Id);
                AddTask(TaskKind.DoctorReview, StaffRole.Doctor, patient, 8, "Review and decide");
                Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName} has waited {PlayerDecisionPatience.TotalMinutes:0} minutes for your decision, so a colleague has been asked to review.", patient.Id);
            }
        }
    }

    // ---------------------------------------------------------------- each tick

    /// <summary>Runs the player's doctor for one tick. Returns true when the staff AI should leave them alone.</summary>
    private bool UpdatePlayerDoctor(StaffMember member, double dt)
    {
        if (!IsPlayer(member))
        {
            return false;
        }

        if (!member.IsOnDuty || member.Shift != CurrentShift)
        {
            Notifications.Add(Now, NotificationLevel.Info, $"{member.Name}'s shift has ended, so you are back in the manager's seat.");
            ReleaseControl();
            return false;
        }

        HandOnForgottenDecisions();
        var task = member.CurrentTaskId is { } id && _tasks.TryGetValue(id, out var found) ? found : null;
        if (task is null)
        {
            member.CurrentTaskId = null;
        }

        var working = task is { Started: true };
        member.Fatigue = Math.Clamp(member.Fatigue + ((working ? FatigueWhenWorkingPerHour : FatigueWhenIdlePerHour) * dt / 3600.0), 0, 1);

        if (task is null)
        {
            member.Activity = member.IsMoving ? StaffActivity.WalkingToTask : StaffActivity.Idle;
            member.StatusText = "Played by you";
            return true;
        }

        if (!_patients.TryGetValue(task.PatientId, out var patient) || !patient.IsInDepartment)
        {
            RemoveTask(task);
            return true;
        }

        if (!working)
        {
            member.Activity = member.IsMoving ? StaffActivity.WalkingToTask : StaffActivity.Idle;
            member.StatusText = $"{task.Label}: {patient.FullName}";
            return true;
        }

        if (patient.InArrest || !WithinReach(member, StaffPositionFor(task, patient), PlayerWorkRange))
        {
            // Walked off (or the patient has arrested): the job stops and waits.
            task.Started = false;
            if (patient.BusyWithTaskId == task.Id)
            {
                patient.BusyWithTaskId = null;
            }

            return true;
        }

        member.Activity = StaffActivity.Working;
        member.WorkRemainingSeconds -= dt;
        if (member.WorkRemainingSeconds > 0)
        {
            return true;
        }

        _tasks.Remove(task.Id);
        member.CurrentTaskId = null;
        member.Activity = StaffActivity.Idle;
        member.StatusText = "Played by you";
        member.TasksCompleted++;
        if (patient.BusyWithTaskId == task.Id)
        {
            patient.BusyWithTaskId = null;
        }

        if (task.Kind == TaskKind.DoctorAssessment)
        {
            member.Caseload.Add(patient.Id);
        }

        CompleteTask(member, task, patient);
        return true;
    }

    // ---------------------------------------------------------------- walking

    /// <summary>
    /// Walks the player's doctor freely: (<paramref name="dx"/>, <paramref name="dy"/>) is how far to
    /// move in grid tiles. Walls, closed edges and furniture stop them; they slide along what they hit.
    /// </summary>
    public void PlayerMove(double dx, double dy)
    {
        if (PlayerDoctor is not { } member || (dx == 0 && dy == 0))
        {
            return;
        }

        if (member.IsMoving)
        {
            member.StopMoving();
        }

        var (x, y) = (member.X, member.Y);

        // Small steps, so a fast frame can't jump through a wall.
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) / 0.2));
        for (var i = 0; i < steps; i++)
        {
            var nx = x + (dx / steps);
            if (CanStandAt(x, y, nx, y))
            {
                x = nx;
            }

            var ny = y + (dy / steps);
            if (CanStandAt(x, y, x, ny))
            {
                y = ny;
            }
        }

        var length = Math.Sqrt((dx * dx) + (dy * dy));
        member.FollowAt(x, y, (dx / length, dy / length));
    }

    /// <summary>Whether someone standing at (fromX, fromY) can shuffle to (x, y): every tile their body would touch can be stepped onto.</summary>
    private bool CanStandAt(double fromX, double fromY, double x, double y)
    {
        var here = new GridCoord((int)Math.Floor(fromX), (int)Math.Floor(fromY));
        foreach (var (ox, oy) in new[] { (-PlayerRadius, -PlayerRadius), (PlayerRadius, -PlayerRadius), (-PlayerRadius, PlayerRadius), (PlayerRadius, PlayerRadius) })
        {
            var tile = new GridCoord((int)Math.Floor(x + ox), (int)Math.Floor(y + oy));
            if (tile == here)
            {
                continue;
            }

            if (!Nav.IsWalkable(tile))
            {
                return false;
            }

            // A wall between here and there: check the straight steps that lead to that tile.
            var viaX = new GridCoord(tile.X, here.Y);
            var viaY = new GridCoord(here.X, tile.Y);
            var straight = tile.X == here.X || tile.Y == here.Y;
            if (straight ? !Nav.CanStep(here, tile)
                : !((Nav.CanStep(here, viaX) && Nav.CanStep(viaX, tile)) || (Nav.CanStep(here, viaY) && Nav.CanStep(viaY, tile))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The player's doctor walks themselves to <paramref name="destination"/> by the usual route (lifts and stairs included).</summary>
    public bool PlayerWalkTo(GridCoord destination)
    {
        if (PlayerDoctor is not { } member)
        {
            return false;
        }

        member.StopMoving();
        var path = PathFor(member, destination);
        if (path.Count == 0)
        {
            return member.Tile == destination;
        }

        member.FollowPath(path);
        return true;
    }

    /// <summary>The route the player's doctor would walk to <paramref name="destination"/>, for the arrows on the floor.</summary>
    public IReadOnlyList<GridCoord> PlayerRouteTo(GridCoord destination) =>
        PlayerDoctor is { } member ? Nav.FindRoute(member.Tile, destination) : Array.Empty<GridCoord>();

    /// <summary>Lifts and stairs the player's doctor is standing at, and the tile each would take them to.</summary>
    public IReadOnlyList<GridCoord> PlayerFloorLinks() =>
        PlayerDoctor is { } member
            ? Nav.Neighbours(member.Tile).Where(n => n.IsLink).Select(n => n.Tile).ToList()
            : Array.Empty<GridCoord>();

    /// <summary>Takes the lift or stairs the player's doctor is standing at to <paramref name="to"/>.</summary>
    public bool PlayerTakeLink(GridCoord to)
    {
        if (PlayerDoctor is not { } member || !PlayerFloorLinks().Contains(to))
        {
            return false;
        }

        member.PlaceAt(to);
        return true;
    }

    private bool WithinReach(StaffMember member, GridCoord spot, int reach) =>
        Site.FloorOf(member.Tile) == Site.FloorOf(spot) && Chebyshev(member.Tile, spot) <= reach;

    /// <summary>True when the player's doctor is at <paramref name="patient"/>'s side, close enough to examine them and make decisions.</summary>
    public bool PlayerIsWith(Patient patient) =>
        PlayerDoctor is { } member && WithinReach(member, patient.Tile, PlayerReach + 1);

    // ---------------------------------------------------------------- tasks

    /// <summary>
    /// The work waiting for the player's doctor: every doctor's task for their team that nobody else has
    /// taken, most urgent first. The job they have picked comes first.
    /// </summary>
    public IReadOnlyList<ClinicalTask> PlayerTasks()
    {
        if (PlayerDoctor is not { } member)
        {
            return Array.Empty<ClinicalTask>();
        }

        return _tasks.Values
            .Where(t => t.AssignedStaffId == member.Id || ((t.AssignedStaffId is null || OnTheirWay(t) is not null) && IsPlayerWork(t, member)))
            .Where(t => _patients.TryGetValue(t.PatientId, out var p) && p.IsInDepartment)
            .OrderByDescending(t => t.AssignedStaffId == member.Id)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.Id)
            .ToList();
    }

    /// <summary>
    /// The doctor who has picked up <paramref name="task"/> but not started it (still walking there), if any.
    /// The player may take such a job over: otherwise a well-staffed team leaves them nothing to do.
    /// </summary>
    public StaffMember? OnTheirWay(ClinicalTask task) =>
        !task.Started && task.AssignedStaffId is { } id && id != PlayerDoctorId && FindStaff(id) is { Role: StaffRole.Doctor, CrashPatientId: null } other
        && other.CurrentTaskId == task.Id
            ? other
            : null;

    private bool IsPlayerWork(ClinicalTask task, StaffMember member) =>
        task.Role == StaffRole.Doctor && ServiceRoomKind(task) is null && IsForMember(task, member);

    /// <summary>Bedside jobs for <paramref name="patient"/> that a nurse would do but the player's doctor may do themselves (giving a medicine, a simple procedure).</summary>
    public IReadOnlyList<ClinicalTask> PlayerExtraTasksFor(Patient patient) =>
        PlayerDoctor is null
            ? Array.Empty<ClinicalTask>()
            : _tasks.Values
                .Where(t => t.PatientId == patient.Id && t.AssignedStaffId is null && t.Role == StaffRole.Nurse
                            && t.Kind is TaskKind.GiveMedication or TaskKind.Procedure or TaskKind.BedsideTest or TaskKind.Observations or TaskKind.TakeBloods
                            && ServiceRoomKind(t) is null)
                .OrderBy(t => t.Id)
                .ToList();

    /// <summary>Patients the player's doctor has seen who now need them to decide what happens next.</summary>
    public IReadOnlyList<Patient> PlayerDecisionsWaiting() =>
        PlayerDoctor is { } member
            ? _patients.Values.Where(p => member.Caseload.Contains(p.Id) && NeedsDecision(p)).OrderBy(p => p.Id).ToList()
            : Array.Empty<Patient>();

    /// <summary>Patients the player's doctor is looking after: seen by them and still in the department.</summary>
    public IReadOnlyList<Patient> PlayerPatients() =>
        PlayerDoctor is { } member
            ? _patients.Values.Where(p => member.Caseload.Contains(p.Id) && p.IsInDepartment).OrderBy(p => p.Id).ToList()
            : Array.Empty<Patient>();

    /// <summary>Where the player's doctor needs to stand to do <paramref name="task"/>.</summary>
    public GridCoord? PlayerTaskSpot(ClinicalTask task) =>
        _patients.TryGetValue(task.PatientId, out var patient) ? StaffPositionFor(task, patient) : null;

    /// <summary>Where the player's doctor stands to be at <paramref name="patient"/>'s side.</summary>
    public GridCoord PlayerBedsideSpot(Patient patient)
    {
        if (patient.CorridorSpot is { } parked && patient.Tile == parked && !patient.IsMoving)
        {
            return CorridorStaffSpot(parked);
        }

        if (BedOf(patient) is { } bed && bed.PatientSpot == patient.Tile)
        {
            return bed.StaffSpot;
        }

        if (patient.CareSpaceId is { } spaceId && FindSpace(spaceId) is { } space && space.PatientSpot == patient.Tile)
        {
            return space.StaffSpot;
        }

        return BesideTile(patient.Tile);
    }

    /// <summary>True when the player's doctor is close enough to start <paramref name="task"/>.</summary>
    public bool PlayerCanReach(ClinicalTask task) =>
        PlayerDoctor is { } member && _patients.TryGetValue(task.PatientId, out var patient) && WithinReach(member, StaffPositionFor(task, patient), PlayerReach);

    /// <summary>The player picks <paramref name="taskId"/> as their next job, so no other doctor takes it.</summary>
    public PlayerResult PlayerClaim(int taskId)
    {
        if (PlayerDoctor is not { } member)
        {
            return PlayerResult.Fail("You are not playing a doctor.");
        }

        if (!_tasks.TryGetValue(taskId, out var task) || !_patients.TryGetValue(task.PatientId, out var patient))
        {
            return PlayerResult.Fail("That job has gone.");
        }

        var colleague = OnTheirWay(task);
        if (task.AssignedStaffId is { } other && other != member.Id && colleague is null)
        {
            return PlayerResult.Fail("Someone else has already started that.");
        }

        var allowed = IsPlayerWork(task, member) || PlayerExtraTasksFor(patient).Contains(task) || task.AssignedStaffId == member.Id;
        if (!allowed)
        {
            return PlayerResult.Fail("That isn't a job for this doctor.");
        }

        if (colleague is not null)
        {
            // The colleague who was walking there turns back and finds something else.
            ReleaseFromTask(colleague, task);
            colleague.StopMoving();
            colleague.Activity = StaffActivity.Idle;
            colleague.StatusText = "Available";
        }

        if (member.CurrentTaskId is { } currentId && currentId != taskId && _tasks.TryGetValue(currentId, out var current))
        {
            ReleaseFromTask(member, current);
        }

        task.AssignedStaffId = member.Id;
        member.CurrentTaskId = task.Id;
        member.StatusText = $"{task.Label}: {patient.FullName}";
        return PlayerResult.Done($"{task.Label}: {patient.FullName}");
    }

    /// <summary>Puts the picked job back on the board for anyone to take.</summary>
    public void PlayerDropTask()
    {
        if (PlayerDoctor is { } member && member.CurrentTaskId is { } id && _tasks.TryGetValue(id, out var task))
        {
            ReleaseFromTask(member, task);
            member.CurrentTaskId = null;
        }
    }

    /// <summary>Starts work on <paramref name="taskId"/>. The doctor has to be at the bedside and the patient there and free.</summary>
    public PlayerResult PlayerStartTask(int taskId)
    {
        var claimed = PlayerClaim(taskId);
        if (!claimed.Ok)
        {
            return claimed;
        }

        var member = PlayerDoctor!;
        var task = _tasks[taskId];
        var patient = _patients[task.PatientId];
        if (task.Started)
        {
            return PlayerResult.Done("Already under way.");
        }

        if (patient.InArrest)
        {
            return PlayerResult.Fail($"{patient.FullName} is in cardiac arrest: the crash team are with them.");
        }

        if (patient.BusyWithTaskId is { } busy && busy != task.Id)
        {
            var what = _tasks.TryGetValue(busy, out var other) ? other.Label.ToLowerInvariant() : "something else";
            return PlayerResult.Fail($"{patient.FullName} is busy ({what}). Try again in a moment.");
        }

        var patientInPlace = !patient.IsMoving && patient.PendingMove is null && patient.Tile == PatientPositionFor(task, patient);
        if (!patientInPlace)
        {
            return PlayerResult.Fail($"{patient.FullName} is not at their bed yet.");
        }

        if (!WithinReach(member, StaffPositionFor(task, patient), PlayerReach))
        {
            return PlayerResult.Fail($"Walk to {patient.FullName} first.");
        }

        member.StopMoving();
        patient.BusyWithTaskId = task.Id;
        task.Started = true;
        member.Activity = StaffActivity.Working;
        member.WorkRemainingSeconds = task.Minutes * 60 * member.DurationFactor;
        if (task.Kind == TaskKind.DoctorAssessment)
        {
            member.Caseload.Add(patient.Id);
        }

        return PlayerResult.Done($"{task.Label} started.");
    }

    /// <summary>How far through the job in hand the player's doctor is, 0 to 1 (null when not working).</summary>
    public double? PlayerWorkProgress()
    {
        if (PlayerDoctor is not { } member || member.CurrentTaskId is not { } id || !_tasks.TryGetValue(id, out var task) || !task.Started)
        {
            return null;
        }

        var total = task.Minutes * 60 * member.DurationFactor;
        return total <= 0 ? 1 : Math.Clamp(1 - (member.WorkRemainingSeconds / total), 0, 1);
    }

    // ---------------------------------------------------------------- clinical decisions

    /// <summary>Whether the player's doctor may make decisions about <paramref name="patient"/> now, and if not, why not.</summary>
    public PlayerResult PlayerCanDecide(Patient patient)
    {
        if (PlayerDoctor is null)
        {
            return PlayerResult.Fail("You are not playing a doctor.");
        }

        if (!patient.IsInDepartment)
        {
            return PlayerResult.Fail($"{patient.FullName} has left.");
        }

        if (patient.WorkingDiagnosis is not null)
        {
            return PlayerResult.Fail("A diagnosis has already been made.");
        }

        if (!patient.HistoryTaken)
        {
            return PlayerResult.Fail("Assess the patient first: take the history and examine them.");
        }

        if (!PlayerIsWith(patient))
        {
            return PlayerResult.Fail($"Walk to {patient.FullName} first.");
        }

        if (patient.BusyWithTaskId is { } busy && _tasks.TryGetValue(busy, out var task) && task.AssignedStaffId != PlayerDoctorId && task.Role == StaffRole.Doctor)
        {
            return PlayerResult.Fail("Another doctor is with this patient.");
        }

        return PlayerResult.Done(string.Empty);
    }

    /// <summary>Every test, and whether it can be requested for <paramref name="patient"/>. Tests that bear on what they came in with are marked relevant.</summary>
    public IReadOnlyList<TestOption> TestOptionsFor(Patient patient)
    {
        var relevant = Database.Conditions.Values
            .Where(c => c.PresentingComplaint == patient.PresentingComplaint && !c.IsMaternity && !c.IsPlanned)
            .SelectMany(c => c.KeyInvestigations)
            .Concat(patient.KnownSymptoms.SelectMany(s => Database.Symptoms[s].TriageInvestigations))
            .ToHashSet();
        return Database.Investigations.Values
            .Where(t => InvestigationSystem.AppliesTo(t, patient))
            .Select(t =>
            {
                var requested = patient.Investigations.Any(o => o.InvestigationId == t.Id);
                var can = CanDo(t);
                var note = requested ? "already requested"
                    : !can ? "no staffed room for it here"
                    : t.Category == InvestigationCategory.Imaging ? "patient is taken to the room"
                    : t.Category == InvestigationCategory.Blood ? $"result in about {t.TurnaroundMinutes} min"
                    : "done at the bedside";
                return new TestOption(t, requested, can && !requested, note, relevant.Contains(t.Id));
            })
            .OrderByDescending(o => o.Relevant)
            .ThenBy(o => o.Test.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The player's doctor requests these tests for <paramref name="patient"/>.</summary>
    public PlayerResult PlayerOrderTests(int patientId, IReadOnlyCollection<string> investigationIds)
    {
        if (FindPatient(patientId) is not { } patient)
        {
            return PlayerResult.Fail("That patient has gone.");
        }

        var can = PlayerCanDecide(patient);
        if (!can.Ok)
        {
            return can;
        }

        var options = TestOptionsFor(patient).Where(o => o.Available).Select(o => o.Test.Id).ToHashSet();
        var ids = investigationIds.Distinct().Where(options.Contains).ToList();
        if (ids.Count == 0)
        {
            return PlayerResult.Fail("None of those tests can be requested.");
        }

        var doctor = PlayerDoctor!;
        MarkResultsReviewed(patient);
        OrderInvestigations(patient, ids);
        var names = string.Join(", ", ids.Select(i => Database.Investigations[i].Name));
        patient.Log(Now, $"Requested by {doctor.Name}: {names}.");
        return PlayerResult.Done($"Requested: {names}.");
    }

    /// <summary>Diagnoses the player's doctor can choose from; the ones that share the patient's presenting complaint come first.</summary>
    public IReadOnlyList<(ConditionDefinition Condition, bool SameComplaint)> DiagnosisOptionsFor(Patient patient) =>
        Database.Conditions.Values
            .Where(c => !c.IsMaternity && !c.IsPlanned)
            .Select(c => (Condition: c, SameComplaint: c.PresentingComplaint == patient.PresentingComplaint))
            .OrderByDescending(c => c.SameComplaint)
            .ThenBy(c => c.Condition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The player's doctor commits to a diagnosis; the treatment for it is planned straight away.</summary>
    public PlayerResult PlayerDiagnose(int patientId, string conditionId)
    {
        if (FindPatient(patientId) is not { } patient)
        {
            return PlayerResult.Fail("That patient has gone.");
        }

        var can = PlayerCanDecide(patient);
        if (!can.Ok)
        {
            return can;
        }

        if (!Database.Conditions.TryGetValue(conditionId, out var condition) || condition.IsMaternity || condition.IsPlanned)
        {
            return PlayerResult.Fail("That isn't a diagnosis that can be made here.");
        }

        var doctor = PlayerDoctor!;
        var differential = _diagnosis.Differential(patient);
        var confidence = differential.FirstOrDefault(d => d.Condition.Id == conditionId)?.Probability ?? 0;
        MarkResultsReviewed(patient);
        foreach (var review in _tasks.Values.Where(t => t.PatientId == patient.Id && t.Kind == TaskKind.DoctorReview).ToList())
        {
            RemoveTask(review);
        }

        doctor.Caseload.Add(patient.Id);
        patient.WorkingDiagnosis = new Diagnosis(condition.Id, confidence, Now, doctor.Id);
        Stats.RecordDiagnosis(condition.Id == patient.Condition.Id);
        RecordJournalDiagnosis(patient, condition.Id == patient.Condition.Id);
        patient.Log(Now, $"Diagnosis by {doctor.Name}: {condition.Name}.", important: true);
        TakeObservations(patient, allowDowngrade: true);
        PlanTreatment(patient, condition);
        return PlayerResult.Done($"Diagnosis recorded: {condition.Name}. Treatment has been prescribed.");
    }

    /// <summary>"Decide for me": the doctor's own judgement (their skill, tiredness and workload) chooses the next step.</summary>
    public PlayerResult PlayerDecideForMe(int patientId)
    {
        if (FindPatient(patientId) is not { } patient)
        {
            return PlayerResult.Fail("That patient has gone.");
        }

        var can = PlayerCanDecide(patient);
        if (!can.Ok)
        {
            return can;
        }

        var doctor = PlayerDoctor!;
        var before = patient.Timeline.Count;
        MarkResultsReviewed(patient);
        foreach (var review in _tasks.Values.Where(t => t.PatientId == patient.Id && t.Kind == TaskKind.DoctorReview).ToList())
        {
            RemoveTask(review);
        }

        doctor.Caseload.Add(patient.Id);
        _delegating = true;
        try
        {
            ApplyDecision(doctor, patient);
        }
        finally
        {
            _delegating = false;
        }

        var said = patient.Timeline.Skip(before).Select(t => t.Text).FirstOrDefault(t => t.StartsWith("Diagnosis", StringComparison.Ordinal) || t.StartsWith("Differential", StringComparison.Ordinal));
        return PlayerResult.Done(said ?? $"{doctor.Name} has decided.");
    }

    private static void MarkResultsReviewed(Patient patient)
    {
        foreach (var order in patient.Investigations.Where(o => o.Status == InvestigationStatus.Resulted))
        {
            order.Reviewed = true;
        }
    }

    /// <summary>What a thoughtful colleague would point out about <paramref name="patient"/>: clues, never the answer.</summary>
    public ClinicalHint HintFor(Patient patient) => ClinicalHints.Build(patient, Database, _diagnosis, CanDo);
}
