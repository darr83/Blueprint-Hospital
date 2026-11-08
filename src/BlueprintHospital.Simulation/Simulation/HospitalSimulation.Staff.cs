using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Staff AI: the task board, priorities, choosing work, walking to it and doing it.</summary>
public sealed partial class HospitalSimulation
{
    private const double FatigueWhenWorkingPerHour = 0.10;
    private const double FatigueWhenIdlePerHour = 0.02;
    private const double BreakThreshold = 0.75;
    private static readonly TimeSpan BreakLength = TimeSpan.FromMinutes(20);

    // Priority bands, highest first (see the specification's staff priorities).
    private const double RedImmediate = 1000;
    private const double AmberDeteriorating = 800;
    private const double RedOngoing = 700;
    private const double Handover = 650;
    private const double TriageBand = 600;
    private const double AmberRoutine = 500;
    private const double GreenRoutine = 300;
    private const double Administrative = 100;

    internal ClinicalTask AddTask(TaskKind kind, StaffRole role, Patient patient, double minutes, string label, string? detail = null)
    {
        var task = new ClinicalTask
        {
            Id = _nextTaskId++,
            Kind = kind,
            Role = role,
            PatientId = patient.Id,
            CreatedAt = Now,
            Minutes = minutes,
            Label = label,
            Detail = detail,
        };
        task.Priority = PriorityOf(task, patient);
        _tasks[task.Id] = task;
        return task;
    }

    internal bool HasTask(Patient patient, TaskKind kind) =>
        _tasks.Values.Any(t => t.PatientId == patient.Id && t.Kind == kind);

    internal void CancelTasksFor(Patient patient)
    {
        foreach (var task in _tasks.Values.Where(t => t.PatientId == patient.Id).ToList())
        {
            RemoveTask(task);
        }

        patient.BusyWithTaskId = null;
    }

    private void RemoveTask(ClinicalTask task)
    {
        _tasks.Remove(task.Id);
        if (task.AssignedStaffId is { } staffId && FindStaff(staffId) is { } member && member.CurrentTaskId == task.Id)
        {
            member.CurrentTaskId = null;
            member.Activity = StaffActivity.Idle;
            member.StatusText = "Available";
        }

        if (FindPatient(task.PatientId) is { } patient && patient.BusyWithTaskId == task.Id)
        {
            patient.BusyWithTaskId = null;
        }

        if (task.Kind == TaskKind.MovePatient && FindPatient(task.PatientId) is { } pushed && pushed.PushedByStaffId is { } pusher && pusher == task.AssignedStaffId)
        {
            EndPush(pushed);
        }

        if (task.SpaceId is { } spaceId && FindSpace(spaceId) is { } room && room.ReservedByStaffId == task.AssignedStaffId)
        {
            room.ReservedByStaffId = null;
        }
    }

    /// <summary>
    /// Clinical urgency first: Red emergencies, deteriorating Amber patients, ongoing Red care,
    /// then other Amber, Green and finally administrative work. Within a band, longer waits come first.
    /// </summary>
    private double PriorityOf(ClinicalTask task, Patient patient)
    {
        double band;
        switch (task.Kind)
        {
            case TaskKind.Register:
                band = Administrative;
                break;
            case TaskKind.AmbulanceHandover:
                band = Handover;
                break;
            case TaskKind.Triage:
                band = TriageBand;
                break;
            default:
                var news = patient.LatestObservations?.News2.Total ?? 0;
                band = patient.Triage switch
                {
                    TriageCategory.Red when patient.FirstSeenByDoctorAt is null || task.Kind == TaskKind.Observations => RedImmediate,
                    TriageCategory.Red => RedOngoing,
                    TriageCategory.Amber when news >= 5 => AmberDeteriorating,
                    TriageCategory.Amber => AmberRoutine,
                    _ => GreenRoutine,
                };

                if (task.Kind is TaskKind.Discharge or TaskKind.WardDischarge)
                {
                    band -= 50;
                }

                break;
        }

        var waitedMinutes = (Now - task.CreatedAt).TotalMinutes;
        return band + Math.Min(waitedMinutes, 150);
    }

    private void UpdateTaskPriorities()
    {
        foreach (var task in _tasks.Values)
        {
            if (FindPatient(task.PatientId) is { } patient)
            {
                task.Priority = PriorityOf(task, patient);
            }
        }
    }

    // ---------------------------------------------------------------- staff loop

    /// <summary>Shifts change at 08:00 and 20:00. Each post is covered by a fresh team member.</summary>
    private static readonly int[] ShiftChangeHours = { 8, 20 };

    private void HandleShiftChange()
    {
        if (Now.Minute != 0 || Now.Second != 0 || !ShiftChangeHours.Contains(Now.Hour))
        {
            return;
        }

        foreach (var member in _staff)
        {
            member.Fatigue = Math.Min(member.Fatigue, Random.Range(0.0, 0.1));
        }

        Notifications.Add(Now, NotificationLevel.Info, Now.Hour == 8 ? "Day shift has started: staff handed over and are refreshed." : "Night shift has started: staff handed over and are refreshed.");
    }

    private void UpdateStaff(double dt)
    {
        HandleShiftChange();
        foreach (var member in _staff)
        {
            var working = member.CurrentTaskId is not null || member.Activity == StaffActivity.Working;
            member.Fatigue = Math.Clamp(member.Fatigue + ((working ? FatigueWhenWorkingPerHour : FatigueWhenIdlePerHour) * dt / 3600.0), 0, 1);

            switch (member.Activity)
            {
                case StaffActivity.WalkingToBreak when !member.IsMoving:
                    member.Activity = StaffActivity.OnBreak;
                    member.StatusText = "On a break";
                    break;

                case StaffActivity.OnBreak:
                    member.Fatigue = Math.Max(0.1, member.Fatigue - (dt / BreakLength.TotalSeconds * 0.7));
                    if (Now >= member.BreakEndsAt)
                    {
                        member.Activity = StaffActivity.Idle;
                        member.StatusText = "Available";
                        member.BreakEndsAt = null;
                    }

                    continue;
            }

            if (member.Activity is StaffActivity.WalkingToBreak)
            {
                continue;
            }

            if (IsClinicStaff(member))
            {
                UpdateClinicStaff(member);
                continue;
            }

            if (member.CurrentTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task))
            {
                ProgressTask(member, task, dt);
            }
            else
            {
                member.CurrentTaskId = null;
                ChooseTask(member);
            }
        }
    }

    private void ChooseTask(StaffMember member)
    {
        var candidates = _tasks.Values
            .Where(t => t.AssignedStaffId is null && (t.Role == member.Role || NurseCanCover(t, member)) && IsForMember(t, member) && CanStart(t))
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.Id);

        var best = candidates.FirstOrDefault();

        if (member.Role != StaffRole.Receptionist && member.Fatigue >= BreakThreshold && (best is null || best.Priority < RedOngoing))
        {
            TakeBreak(member);
            return;
        }

        if (best is null)
        {
            member.Activity = StaffActivity.Idle;
            member.StatusText = "Available";
            WalkTo(member, HomeTile(member));
            return;
        }

        best.AssignedStaffId = member.Id;
        member.CurrentTaskId = best.Id;
        member.Activity = StaffActivity.WalkingToTask;
        var patient = _patients[best.PatientId];
        member.StatusText = $"{best.Label}: {patient.FullName}";
        if (best.Kind == TaskKind.MovePatient)
        {
            WalkTo(member, StaffPositionFor(best, patient));
            return;
        }

        patient.BusyWithTaskId = best.Id;

        if (best.Kind == TaskKind.Triage && FreeTriageRoom() is { } triage)
        {
            triage.ReservedByStaffId = member.Id;
            best.SpaceId = triage.Id;
            ReleaseSeat(patient);
            WalkTo(patient, triage.PatientSpot);
            patient.Log(Now, "Called through to triage.");
        }
        else if (ServiceRoomKind(best) is not null)
        {
            StartServiceTask(member, best, patient);
        }
        else if (best.Kind == TaskKind.EscortToWard)
        {
            StartEscortToWard(patient);
        }
        else if (best.Kind == TaskKind.EscortToTransfer && AmbulanceFor(patient) is { BaySlot: { } slot })
        {
            ReleaseSpace(patient);
            WalkTo(patient, AmbulanceBayPatientTiles[slot]);
        }

        if (best.Kind == TaskKind.DoctorAssessment)
        {
            member.Caseload.Add(patient.Id);
        }

        WalkTo(member, StaffPositionFor(best, patient));
    }

    /// <summary>A nurse pushes a patient themselves when the move has waited too long for a porter.</summary>
    private bool NurseCanCover(ClinicalTask task, StaffMember member) =>
        task.Kind == TaskKind.MovePatient && member.Role == StaffRole.Nurse && Now - task.CreatedAt >= NurseCoversPorterAfter;

    private void TakeBreak(StaffMember member)
    {
        var seats = SpacesOfKind(CareSpaceKind.StaffRoom).SelectMany(s => s.Seats).ToList();
        var seat = seats.Count > 0 ? seats[(member.Id - 1) % seats.Count] : HomeTile(member);
        member.Activity = StaffActivity.WalkingToBreak;
        member.BreakEndsAt = Now + BreakLength + TimeSpan.FromMinutes(Random.Range(0, 5));
        member.StatusText = "Going on a break";
        WalkTo(member, seat);
    }

    /// <summary>Whether the task can be picked up now (patient free and not walking, room available).</summary>
    private bool CanStart(ClinicalTask task)
    {
        if (!_patients.TryGetValue(task.PatientId, out var patient) || !patient.IsInDepartment && patient.Stage != PatientStage.WalkingIn)
        {
            return false;
        }

        if (task.Kind == TaskKind.MovePatient)
        {
            return patient.PendingMove is not null;
        }

        if (patient.BusyWithTaskId is not null && patient.BusyWithTaskId != task.Id)
        {
            return false;
        }

        return task.Kind switch
        {
            TaskKind.Register => patient.Stage == PatientStage.WaitingToRegister && _receptionQueue.Count > 0 && _receptionQueue[0] == patient.Id,
            TaskKind.Triage => FreeTriageRoom() is not null && !patient.IsMoving,
            TaskKind.EscortToTransfer => AmbulanceFor(patient) is { State: Transport.AmbulanceState.AtBay },
            _ => !patient.IsMoving || patient.Stage == PatientStage.InTreatmentSpace,
        };
    }

    /// <summary>Where staff stand to do the task.</summary>
    private GridCoord StaffPositionFor(ClinicalTask task, Patient patient)
    {
        switch (task.Kind)
        {
            case TaskKind.Register:
                return Waiting.StaffSpot;
            case TaskKind.Triage when task.SpaceId is { } triageId && FindSpace(triageId) is { } triage:
                return triage.StaffSpot;
            case TaskKind.Imaging or TaskKind.Surgery or TaskKind.DispenseMedicines when task.SpaceId is { } roomId && FindSpace(roomId) is { } room:
                return room.StaffSpot;
            case TaskKind.MovePatient:
                // Beside the patient where they are now, not where they are going.
                if (patient.CareSpaceId is { } here && FindSpace(here) is { } current && current.PatientSpot == patient.Tile)
                {
                    return current.StaffSpot;
                }

                if (BedOf(patient) is { } lyingIn && lyingIn.PatientSpot == patient.Tile)
                {
                    return lyingIn.StaffSpot;
                }

                if (_spaces.Values.FirstOrDefault(s => s.IsServiceRoom && s.PatientSpot == patient.Tile) is { } table)
                {
                    return table.StaffSpot;
                }

                return BesideTile(patient.Tile);
        }

        if (BedOf(patient) is { } bed)
        {
            return bed.StaffSpot;
        }

        if (patient.CareSpaceId is { } spaceId && task.Kind != TaskKind.EscortToTransfer && FindSpace(spaceId) is { } space)
        {
            return space.StaffSpot;
        }

        return BesideTile(patient.Destination ?? patient.Tile);
    }

    private GridCoord BesideTile(GridCoord target)
    {
        foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
        {
            var t = new GridCoord(target.X + dx, target.Y + dy);
            if (Nav.CanStep(target, t))
            {
                return t;
            }
        }

        return target;
    }

    /// <summary>A triage room no nurse is using.</summary>
    private CareSpace? FreeTriageRoom() => SpacesOfKind(CareSpaceKind.Triage).FirstOrDefault(t => t.ReservedByStaffId is null);

    private GridCoord PatientPositionFor(ClinicalTask task, Patient patient) => task.Kind switch
    {
        TaskKind.Register => QueueTile(0),
        TaskKind.Triage when task.SpaceId is { } id && FindSpace(id) is { } triage => triage.PatientSpot,
        TaskKind.Imaging or TaskKind.Surgery when task.SpaceId is { } id && FindSpace(id) is { } room => room.PatientSpot,
        _ => patient.PendingMove ?? patient.Destination ?? patient.Tile,
    };

    private void ProgressTask(StaffMember member, ClinicalTask task, double dt)
    {
        if (!_patients.TryGetValue(task.PatientId, out var patient) || (!patient.IsInDepartment && task.Kind != TaskKind.Register))
        {
            RemoveTask(task);
            return;
        }

        if (task.Kind == TaskKind.MovePatient)
        {
            ProgressPush(member, task, patient);
            return;
        }

        if (!task.Started)
        {
            var staffSpot = StaffPositionFor(task, patient);
            if (!member.IsMoving && member.Tile != staffSpot)
            {
                WalkTo(member, staffSpot);
            }

            var patientInPlace = !patient.IsMoving && patient.PendingMove is null && patient.Tile == PatientPositionFor(task, patient);
            if (member.IsMoving || !patientInPlace)
            {
                return;
            }

            task.Started = true;
            member.Activity = StaffActivity.Working;
            member.WorkRemainingSeconds = task.Minutes * 60 * member.DurationFactor * Random.Range(0.85, 1.15);
        }

        member.WorkRemainingSeconds -= dt;
        if (member.WorkRemainingSeconds > 0)
        {
            return;
        }

        _tasks.Remove(task.Id);
        member.CurrentTaskId = null;
        member.Activity = StaffActivity.Idle;
        member.StatusText = "Available";
        member.TasksCompleted++;
        patient.BusyWithTaskId = null;
        if (task.SpaceId is { } spaceId && FindSpace(spaceId) is { } usedRoom && usedRoom.ReservedByStaffId == member.Id)
        {
            usedRoom.ReservedByStaffId = null;
        }

        CompleteTask(member, task, patient);
    }
}
