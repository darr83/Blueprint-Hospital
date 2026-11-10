using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

public sealed partial class HospitalSimulation
{
    /// <summary>Green patients who wait longer than this for a cubicle may give up and leave.</summary>
    private static readonly TimeSpan LeaveWithoutBeingSeenAfter = TimeSpan.FromHours(4);

    // ---------------------------------------------------------------- ambulances

    private AmbulanceVehicle DispatchAmbulance(Patient patient, AmbulancePurpose purpose)
    {
        var ambulance = new AmbulanceVehicle
        {
            Id = _nextAmbulanceId,
            CallSign = $"Echo {100 + (_nextAmbulanceId % 900)}",
            Purpose = purpose,
            PatientId = patient.Id,
        };
        _nextAmbulanceId++;
        ambulance.PlaceAt(AmbulanceRoadEntry);
        _ambulances.Add(ambulance);
        RouteAmbulanceToBay(ambulance);
        return ambulance;
    }

    private void RouteAmbulanceToBay(AmbulanceVehicle ambulance)
    {
        var used = _ambulances.Where(a => a.BaySlot.HasValue && a.State != AmbulanceState.Gone).Select(a => a.BaySlot!.Value).ToHashSet();
        var slot = Enumerable.Range(0, AmbulanceParkingTiles.Count).FirstOrDefault(i => !used.Contains(i), -1);
        if (slot < 0)
        {
            ambulance.State = AmbulanceState.WaitingForBay;
            if (ambulance.Purpose == AmbulancePurpose.Transfer && _patients.TryGetValue(ambulance.PatientId, out var leaving) && !HasTask(leaving, TaskKind.EscortToTransfer))
            {
                // Every bay is taken: the crew walk in and collect the patient from their cubicle instead.
                leaving.Log(Now, $"Transfer ambulance {ambulance.CallSign} is here but the bays are full; the crew are coming in.");
                AddTask(TaskKind.EscortToTransfer, StaffRole.Nurse, leaving, 10, "Hand over to transfer crew");
            }

            var queueIndex = _ambulances.Count(a => a.State == AmbulanceState.WaitingForBay) - 1;
            var waitTile = new GridCoord(AmbulanceRoadEntry.X, AmbulanceParkingTiles[^1].Y + 8 + (queueIndex * 6));
            ambulance.FollowPath(StraightRoute(ambulance.Tile, new[] { waitTile }));
            return;
        }

        ambulance.BaySlot = slot;
        ambulance.State = AmbulanceState.Driving;
        var parking = AmbulanceParkingTiles[slot];
        ambulance.FollowPath(StraightRoute(ambulance.Tile, new[] { new GridCoord(AmbulanceRoadEntry.X, parking.Y), parking }));
    }

    private static List<GridCoord> StraightRoute(GridCoord from, IEnumerable<GridCoord> waypoints)
    {
        var route = new List<GridCoord>();
        var current = from;
        foreach (var target in waypoints)
        {
            while (current != target)
            {
                current = new GridCoord(current.X + Math.Sign(target.X - current.X), current.Y + Math.Sign(target.Y - current.Y));
                route.Add(current);
            }
        }

        return route;
    }

    private void UpdateAmbulances()
    {
        foreach (var ambulance in _ambulances)
        {
            switch (ambulance.State)
            {
                case AmbulanceState.WaitingForBay when !ambulance.IsMoving:
                    RouteAmbulanceToBay(ambulance);
                    break;

                case AmbulanceState.Driving when !ambulance.IsMoving && ambulance.BaySlot is { } slot:
                    ambulance.State = AmbulanceState.AtBay;
                    ambulance.ArrivedAtBayAt = Now;
                    OnAmbulanceAtBay(ambulance, slot);
                    break;

                case AmbulanceState.Departing when !ambulance.IsMoving:
                    ambulance.State = AmbulanceState.Gone;
                    break;
            }
        }

        _ambulances.RemoveAll(a => a.State == AmbulanceState.Gone);
    }

    private void OnAmbulanceAtBay(AmbulanceVehicle ambulance, int slot)
    {
        if (!_patients.TryGetValue(ambulance.PatientId, out var patient))
        {
            SendAmbulanceAway(ambulance);
            return;
        }

        if (ambulance.Purpose == AmbulancePurpose.Arrival)
        {
            patient.PlaceAt(AmbulanceBayPatientTiles[slot]);
            UpdateMobility(patient);
            patient.QueuedSince = Now;
            patient.Log(Now, $"Arrived by ambulance ({ambulance.CallSign}). Waiting for handover to A&E staff.");
            Stats.AmbulanceArrived();
            AddTask(TaskKind.AmbulanceHandover, StaffRole.Nurse, patient, 10, "Ambulance handover and triage");
        }
        else
        {
            patient.Log(Now, $"Transfer ambulance {ambulance.CallSign} has arrived.");
            if (!HasTask(patient, TaskKind.EscortToTransfer))
            {
                AddTask(TaskKind.EscortToTransfer, StaffRole.Nurse, patient, 10, "Hand over to transfer crew");
            }
        }
    }

    internal void SendAmbulanceAway(AmbulanceVehicle ambulance)
    {
        ambulance.State = AmbulanceState.Departing;
        ambulance.BaySlot = null;
        ambulance.FollowPath(StraightRoute(ambulance.Tile, new[] { new GridCoord(AmbulanceRoadEntry.X, ambulance.Tile.Y), new GridCoord(AmbulanceRoadEntry.X, 0) }));
    }

    internal AmbulanceVehicle? AmbulanceFor(Patient patient) =>
        _ambulances.FirstOrDefault(a => a.PatientId == patient.Id && a.State != AmbulanceState.Departing && a.State != AmbulanceState.Gone);

    // ---------------------------------------------------------------- physiology

    private void UpdatePhysiology(double dt)
    {
        var hours = dt / 3600.0;
        foreach (var patient in _patients.Values.Where(p => p.IsAlive && p.Stage != PatientStage.Gone).ToList())
        {
            var condition = patient.Condition;

            // In cardiac arrest the crash team decide what happens (see the arrests file).
            if (patient.InArrest)
            {
                continue;
            }

            // Just after their heart restarted, the team keep them stable: they don't get worse for a while.
            var stabilised = patient.StabilisedUntil is { } until && Now < until;
            var support = CriticalCareFactor(patient, hours);
            if (patient.MonitorIsRed)
            {
                // A red monitor needs someone sitting with them and a doctor's review: with both they get better
                // (quickly once the right treatment is in, slowly otherwise); without them they carry on getting worse.
                if (patient.RedReviewed && CarerAtBedside(patient) is not null)
                {
                    patient.Severity = Math.Max(0, patient.Severity - (RedRecoveryPerHour(patient) * hours));
                }
                else if (!stabilised)
                {
                    patient.Severity = Math.Min(condition.CanBeFatal ? 1.0 : 0.9, patient.Severity + (condition.DeteriorationPerHour * support * hours));
                }
            }
            else if (patient.EffectiveTreatmentGiven)
            {
                patient.Severity = Math.Max(0, patient.Severity - (condition.RecoveryPerHour * hours));
            }
            else if (!stabilised && (patient.Stage != PatientStage.Leaving || !condition.CanBeFatal))
            {
                patient.Severity = Math.Min(condition.CanBeFatal ? 1.0 : 0.9, patient.Severity + (condition.DeteriorationPerHour * support * hours));
            }

            if (condition.CanBeFatal && patient.Severity >= 1.0 && patient.Stage != PatientStage.Leaving)
            {
                // In a bed, cubicle or trolley the heart stops and the crash team come; anywhere else it's too late.
                if (CanBeResuscitated(patient))
                {
                    StartArrest(patient);
                }
                else
                {
                    PatientDied(patient);
                }
            }
        }
    }

    private void PatientDied(Patient patient, string? note = null)
    {
        var resuscitated = patient.ArrestCount > 0 || patient.InArrest;
        patient.ArrestStartedAt = null;
        patient.Outcome = PatientOutcome.Died;
        var where = patient.Stage == PatientStage.OnWard && patient.CareSpaceId is { } wardId ? $"on {FindSpace(wardId)?.Name ?? "the ward"}" : "in A&E";
        patient.OutcomeNote = $"Died {where}. Underlying condition: {patient.Condition.Name.ToLowerInvariant()}.";
        patient.Log(Now, note ?? (resuscitated
            ? $"Resuscitation was unsuccessful; time of death {Now:HH:mm}."
            : $"Cardiac arrest. Resuscitation was unsuccessful; time of death {Now:HH:mm}."), important: true);
        Notifications.Add(Now, NotificationLevel.Critical, $"{patient.FullName} has sadly died {where}. Their care can be reviewed in their record.", patient.Id, patient.CareSpaceId, patient.Triage);
        EndVisit(patient, PatientOutcome.Died);
        patient.StopMoving();
    }

    /// <summary>Removes every trace of a patient from rooms, queues and the task board.</summary>
    internal void EndVisit(Patient patient, PatientOutcome outcome)
    {
        if (outcome != PatientOutcome.Died && patient.ArrestCount > 0 && patient.Outcome != PatientOutcome.Died)
        {
            Stats.ArrestSurvivors++;
        }

        patient.Outcome = outcome;
        patient.LeftAt = Now;
        LeaveCorridor(patient);
        ReleaseSpace(patient);
        ReleaseReservedBed(patient);
        ReleaseSeat(patient);
        _receptionQueue.Remove(patient.Id);
        CancelTasksFor(patient);
        foreach (var doctor in _staff)
        {
            doctor.Caseload.Remove(patient.Id);
            if (doctor.BedsidePatientId == patient.Id)
            {
                LeaveBedside(doctor);
            }
        }

        if (AmbulanceFor(patient) is { } ambulance && ambulance.Purpose == AmbulancePurpose.Arrival)
        {
            SendAmbulanceAway(ambulance);
        }

        patient.Stage = outcome == PatientOutcome.Died ? PatientStage.Gone : PatientStage.Leaving;
        if (patient.LeftEmergencyAt is null)
        {
            PayForAttendance(patient);
        }
        else
        {
            PayForAdmission(patient);
        }

        Stats.RecordDeparture(patient, Now);
        Events.Publish(new PatientLeft(patient, outcome));
    }

    // ---------------------------------------------------------------- patient flow

    private void UpdatePatientFlow()
    {
        // Keep the reception queue shuffling forward.
        for (var i = 0; i < _receptionQueue.Count; i++)
        {
            var p = _patients[_receptionQueue[i]];
            var tile = QueueTile(i);
            if (p.Stage == PatientStage.WalkingIn && !p.IsMoving)
            {
                p.Stage = PatientStage.WaitingToRegister;
                p.QueuedSince = Now;
            }

            if (p.Stage == PatientStage.WaitingToRegister && !p.IsMoving && p.Tile != tile && p.BusyWithTaskId is null)
            {
                WalkTo(p, tile);
            }
        }

        if (_receptionQueue.Count > 0)
        {
            var first = _patients[_receptionQueue[0]];
            if (first.Stage == PatientStage.WaitingToRegister && !HasTask(first, TaskKind.Register))
            {
                AddTask(TaskKind.Register, StaffRole.Receptionist, first, 3, "Register at reception");
            }
        }

        foreach (var patient in _patients.Values.ToList())
        {
            switch (patient.Stage)
            {
                case PatientStage.Leaving when !patient.IsMoving:
                    patient.Stage = PatientStage.Gone;
                    break;

                case PatientStage.WaitingForSpace when patient.Triage == TriageCategory.Green
                                                       && patient.CorridorSpot is null
                                                       && Now - patient.QueuedSince > LeaveWithoutBeingSeenAfter
                                                       && patient.BusyWithTaskId is null
                                                       && Random.Chance(WalkOutFactor(patient) / 3600):
                    patient.Log(Now, "Left the department before being seen.", important: true);
                    Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName} left without being seen after waiting {(Now - patient.ArrivedAt).TotalHours:0.0} hours.", patient.Id, null, patient.Triage);
                    patient.OutcomeNote = "Left without being seen.";
                    EndVisit(patient, PatientOutcome.LeftWithoutBeingSeen);
                    WalkTo(patient, WalkInSpawn);
                    break;

                case PatientStage.AwaitingTransfer when patient.TransferAmbulanceDueAt is { } due && Now >= due:
                    patient.TransferAmbulanceDueAt = null;
                    var ambulance = DispatchAmbulance(patient, AmbulancePurpose.Transfer);
                    Notifications.Add(Now, NotificationLevel.Info, $"Transfer ambulance {ambulance.CallSign} on site for {patient.FullName}.", patient.Id, patient.CareSpaceId, patient.Triage);
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- results and observations

    private void ProcessScheduledResults()
    {
        foreach (var patient in _patients.Values.Where(p => p.IsInDepartment))
        {
            var changed = false;
            foreach (var order in patient.Investigations.Where(o => o.Status == InvestigationStatus.SampleTaken && o.ResultDueAt <= Now))
            {
                ResultInvestigation(patient, order);
                changed = true;
            }

            if (changed)
            {
                RequestReviewIfResultsComplete(patient);
            }
        }
    }

    internal void ResultInvestigation(Patient patient, InvestigationOrder order)
    {
        var test = Database.Investigations[order.InvestigationId];
        InvestigationSystem.GenerateResult(test, patient, order, Random);
        order.Status = InvestigationStatus.Resulted;
        order.ResultedAt = Now;
        Stats.InvestigationCost += test.Cost;
        Book(MoneyCategory.Tests, -test.Cost);
        patient.Log(Now, $"{test.Name}: {order.Summary}", important: order.Abnormal);
        if (order.Abnormal)
        {
            Notifications.Add(Now, NotificationLevel.Warning, $"Abnormal result for {patient.FullName}: {test.Name}.", patient.Id, patient.CareSpaceId, patient.Triage);

            if (test.EscalateToRedIfAbnormal && patient.Triage is { } current && current != TriageCategory.Red)
            {
                patient.Triage = TriageCategory.Red;
                patient.TriageReason = $"Abnormal {test.Name}: {order.Summary}";
                patient.Log(Now, $"Upgraded from {current} to Red: abnormal {test.Name}.", important: true);
                Notifications.Add(Now, NotificationLevel.Critical, $"{patient.FullName} upgraded {current} → Red: abnormal {test.Name}.", patient.Id, patient.CareSpaceId, TriageCategory.Red);
                Events.Publish(new PatientTriaged(patient, TriageCategory.Red, IsUpgrade: true));
            }
        }
    }

    internal void RequestReviewIfResultsComplete(Patient patient)
    {
        // Review when everything is back, or straight away when an abnormal result needs acting on.
        var waitingFor = patient.Investigations.Any(o => o.Status != InvestigationStatus.Resulted);
        var unreviewed = patient.Investigations.Any(o => o.Status == InvestigationStatus.Resulted && !o.Reviewed);
        var urgent = patient.Investigations.Any(o => o.Status == InvestigationStatus.Resulted && !o.Reviewed && o.Abnormal);
        if ((!waitingFor || urgent) && unreviewed && patient.WorkingDiagnosis is null && !HasTask(patient, TaskKind.DoctorReview) && patient.HistoryTaken)
        {
            AddTask(TaskKind.DoctorReview, StaffRole.Doctor, patient, 8, "Review results");
        }
    }

    private void ScheduleObservations()
    {
        foreach (var patient in _patients.Values)
        {
            if (patient.Triage is null || patient.LatestObservations is not { } latest || HasTask(patient, TaskKind.Observations))
            {
                continue;
            }

            if (patient.Stage is not (PatientStage.WaitingForSpace or PatientStage.InTreatmentSpace or PatientStage.AwaitingTransfer or PatientStage.InAmbulanceQueue or PatientStage.AwaitingBed or PatientStage.AwaitingSurgery or PatientStage.OnWard))
            {
                continue;
            }

            var interval = News2.ObservationInterval(latest.News2);
            if (patient.Triage == TriageCategory.Red && patient.Stage != PatientStage.OnWard)
            {
                interval = TimeSpan.FromMinutes(Math.Min(interval.TotalMinutes, 15));
            }

            if (Now - patient.LastObservationsAt >= interval)
            {
                AddTask(TaskKind.Observations, StaffRole.Nurse, patient, 5, "Observations");
            }
        }
    }

    /// <summary>
    /// Records a fresh set of observations and re-triages. Patients are only ever upgraded here;
    /// downgrades need a doctor's reassessment.
    /// </summary>
    internal void TakeObservations(Patient patient, bool allowDowngrade = false)
    {
        Observations obs;
        if (patient.LastObservationsAt is { } last && Now - last < TimeSpan.FromMinutes(5) && patient.LatestObservations is { } recent)
        {
            // Just recorded: reuse them rather than writing a duplicate set.
            obs = recent.Obs;
        }
        else
        {
            obs = patient.CurrentObservations();
            patient.RecordObservations(Now, obs);
            patient.Log(Now, $"Observations: {obs}. NEWS2 {patient.LatestObservations!.Value.News2.Total}.", important: patient.LatestObservations!.Value.News2.Total >= 5);
        }

        var news = patient.LatestObservations!.Value.News2;
        var decision = TriageSystem.Assess(obs, patient.HistoryTaken ? patient.Symptoms : patient.KnownSymptoms, Database);

        if (patient.Triage is not { } current)
        {
            return;
        }

        if (decision.Category.IsMoreUrgentThan(current))
        {
            patient.Triage = decision.Category;
            patient.TriageReason = decision.Reason;
            patient.Log(Now, $"Upgraded from {current} to {decision.Category}: {decision.Reason}", important: true);
            Notifications.Add(Now, decision.Category == TriageCategory.Red ? NotificationLevel.Critical : NotificationLevel.Warning,
                $"{patient.FullName} deteriorating: upgraded {current} → {decision.Category}. {decision.Reason}", patient.Id, patient.CareSpaceId, decision.Category);
            Events.Publish(new PatientTriaged(patient, decision.Category, IsUpgrade: true));
        }
        else if (allowDowngrade && current.IsMoreUrgentThan(decision.Category))
        {
            patient.Triage = decision.Category;
            patient.TriageReason = decision.Reason;
            patient.Log(Now, $"Reassessed by doctor and downgraded from {current} to {decision.Category}: {decision.Reason}");
        }
    }

    // ---------------------------------------------------------------- treatment spaces

    private static CareSpaceKind[] SpacePreference(TriageCategory category) => category switch
    {
        TriageCategory.Red => new[] { CareSpaceKind.Resus, CareSpaceKind.Majors },
        TriageCategory.Amber => new[] { CareSpaceKind.Majors, CareSpaceKind.Resus },
        _ => new[] { CareSpaceKind.Minors, CareSpaceKind.Majors },
    };

    private static int Acuity(CareSpaceKind kind) => kind switch
    {
        CareSpaceKind.Resus => 3,
        CareSpaceKind.Majors => 2,
        CareSpaceKind.Minors => 1,
        _ => 0,
    };

    private void AllocateSpaces()
    {
        var waiting = _patients.Values
            .Where(p => p.Triage.HasValue && p.BusyWithTaskId is null && p.IsAlive
                        && (p.Stage == PatientStage.WaitingForSpace
                            || (p.Stage == PatientStage.InAmbulanceQueue && p.TriagedAt.HasValue)
                            || (p.Stage == PatientStage.InTreatmentSpace && NeedsMoreAcuteSpace(p))))
            .OrderBy(p => p.Triage!.Value)
            .ThenBy(p => p.QueuedSince)
            .ToList();

        var redWaiting = waiting.Any(p => p.Triage == TriageCategory.Red && p.Stage != PatientStage.InTreatmentSpace);
        var amberWaiting = waiting.Any(p => p.Triage == TriageCategory.Amber && p.Stage != PatientStage.InTreatmentSpace);

        foreach (var patient in waiting)
        {
            var category = patient.Triage!.Value;
            var current = patient.CareSpaceId is { } cid ? FindSpace(cid) : null;
            CareSpace? target = null;

            foreach (var kind in SpacePreference(category))
            {
                // Don't use resus for an Amber patient while a Red one is waiting, or majors for Green while Amber wait.
                if (category == TriageCategory.Amber && kind == CareSpaceKind.Resus && redWaiting)
                {
                    continue;
                }

                if (category == TriageCategory.Green && kind == CareSpaceKind.Majors && amberWaiting)
                {
                    continue;
                }

                if (current is not null && Acuity(kind) <= Acuity(current.Kind))
                {
                    continue;
                }

                target = _spaces.Values.Where(s => s.Kind == kind && IsVacant(s)).OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
                if (target is not null)
                {
                    break;
                }
            }

            if (target is null)
            {
                if (category == TriageCategory.Red && current?.Kind != CareSpaceKind.Resus)
                {
                    RaiseCapacityAlert("resus", $"No resus space for Red patient {patient.FullName}. Staff are prioritising their emergency care.", patient);
                }

                continue;
            }

            if (category == TriageCategory.Red && target.Kind == CareSpaceKind.Resus)
            {
                _activeCapacityAlerts.Remove("resus");
            }

            MoveToSpace(patient, target);
        }
    }

    private bool NeedsMoreAcuteSpace(Patient patient)
    {
        if (patient.CareSpaceId is not { } id || patient.Triage is not { } category)
        {
            return false;
        }

        return FindSpace(id) is { } space && Acuity(SpacePreference(category)[0]) > Acuity(space.Kind);
    }

    private void RaiseCapacityAlert(string key, string text, Patient patient)
    {
        if (_activeCapacityAlerts.Add(key))
        {
            Notifications.Add(Now, NotificationLevel.Critical, text, patient.Id, null, patient.Triage);
            AnnounceFullCapacity();
        }
    }

    private void MoveToSpace(Patient patient, CareSpace space)
    {
        var from = patient.Stage;
        var previous = patient.CareSpaceId;
        LeaveCorridor(patient);
        ReleaseSpace(patient);
        ReleaseSeat(patient);
        space.OccupantId = patient.Id;
        patient.CareSpaceId = space.Id;
        patient.Stage = PatientStage.InTreatmentSpace;
        WalkTo(patient, space.PatientSpot);

        if (from == PatientStage.InAmbulanceQueue && AmbulanceFor(patient) is { } ambulance)
        {
            ambulance.HandoverCompletedAt = Now;
            Stats.RecordHandoverTime(Now - (ambulance.ArrivedAtBayAt ?? Now));
            SendAmbulanceAway(ambulance);
            patient.Log(Now, $"Moved from the ambulance to {space.Name}. Crew released after {(Now - (ambulance.ArrivedAtBayAt ?? Now)).TotalMinutes:0} minutes.");
        }
        else
        {
            patient.Log(Now, previous is null ? $"Moved to {space.Name}." : $"Moved from {FindSpace(previous)?.Name ?? "another room"} to {space.Name}.");
        }

        if (patient.FirstSeenByDoctorAt is null && !HasTask(patient, TaskKind.DoctorAssessment))
        {
            AddTask(TaskKind.DoctorAssessment, StaffRole.Doctor, patient, 12, "Doctor's assessment");
        }
    }
}
