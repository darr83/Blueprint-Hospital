using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Wards: admitting patients from A&amp;E to a bed, the ward team who look after them, and discharge home.
/// A patient who needs admitting waits in their A&amp;E cubicle until a bed is free (as in a real hospital,
/// a full ward blocks A&amp;E). If there is no ward, or no bed comes free in time, they are transferred to
/// a neighbouring hospital instead.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How long a patient waits in A&amp;E for a bed before being sent to another hospital.</summary>
    public static readonly TimeSpan MaxWaitForBed = TimeSpan.FromHours(6);

    /// <summary>Patients each ward nurse looks after.</summary>
    public const int PatientsPerWardNurse = 6;

    /// <summary>A patient is well enough to go home once their illness has settled below this.</summary>
    private const double FitForDischargeSeverity = 0.35;

    public IEnumerable<CareSpace> Wards => SpacesOfKind(CareSpaceKind.Ward);

    /// <summary>The ward bed a patient is in or heading to.</summary>
    public WardBed? BedOf(Patient patient) =>
        patient.CareSpaceId is { } id && FindSpace(id) is { IsWard: true } ward && patient.BedIndex is { } index && index < ward.Beds.Count
            ? ward.Beds[index]
            : null;

    /// <summary>The ward whose team does the task: the patient's ward once they are on it, otherwise null (A&amp;E).</summary>
    private string? TeamFor(ClinicalTask task) =>
        _patients.TryGetValue(task.PatientId, out var patient) && patient.Stage == PatientStage.OnWard ? patient.CareSpaceId : null;

    // ---------------------------------------------------------------- referral

    /// <summary>The doctor has decided the patient needs admitting.</summary>
    private void RequestWardBed(Patient patient, ConditionDefinition diagnosed, string? note = null)
    {
        if (patient.Stage == PatientStage.OnWard)
        {
            // Already in a ward bed (e.g. back from theatre to the surgical assessment unit).
            if (patient.DayCase && patient.CareLevel < 2 && patient.CareSpaceId is { } wardId && FindSpace(wardId) is { } ward && UnitOf(ward) == WardUnit.DaySurgery)
            {
                // A planned day case is already on the day surgery unit: a few hours there, then home.
                patient.DischargeDueAt = StayOn(patient, diagnosed, WardUnit.DaySurgery, moved: false);
                patient.Log(Now, note ?? diagnosed.DispositionNote);
                return;
            }

            patient.DischargeDueAt = null;
            RequestWardMove(patient, note ?? $"{diagnosed.DispositionNote} Needs {LevelName(patient.CareLevel)}.");
            return;
        }

        var level = diagnosed.NeedsIntensiveCare && !patient.HadSurgery ? 3 : LevelFor(patient.Severity);
        patient.CareLevel = Math.Max(patient.CareLevel, level);
        if (patient.CareLevel == 2 && !HasHighDependencyBeds)
        {
            // No HDU or ICU here: they are looked after on an ordinary ward, as before.
            patient.CareLevel = 0;
        }

        if (WardsFor(patient).Count == 0)
        {
            ArrangeTransfer(patient, diagnosed, NoWardReason(patient));
            return;
        }

        patient.Stage = PatientStage.AwaitingBed;
        patient.BedRequestedAt = Now;
        var team = patient.CareLevel >= 2 ? "the critical care" : $"the {DepartmentName(DepartmentOf(patient))}";
        patient.Log(Now, note ?? $"{diagnosed.DispositionNote} Referred to {team} team; waiting for {(patient.CareLevel >= 2 ? LevelName(patient.CareLevel) : "a ward bed")}.", important: true);
        Notifications.Add(Now, NotificationLevel.Info, $"{patient.FullName} needs a ward bed.", patient.Id, patient.CareSpaceId, patient.Triage);
    }

    /// <summary>Calls a transfer ambulance to take the patient to another hospital.</summary>
    private void ArrangeTransfer(Patient patient, ConditionDefinition diagnosed, string? reason = null)
    {
        patient.Stage = PatientStage.AwaitingTransfer;

        // Time-critical transfers (e.g. a heart attack going for PCI) get an emergency ambulance.
        var timeCritical = diagnosed.CanBeFatal && patient.Triage == TriageCategory.Red;
        var wait = timeCritical ? Random.Range(10, 25) : Random.Range(30, 75);
        patient.TransferAmbulanceDueAt = Now.AddMinutes(wait);
        var note = reason ?? diagnosed.DispositionNote;
        patient.Log(Now, $"{note} Ambulance expected in about {wait} minutes.", important: true);
        Notifications.Add(Now, NotificationLevel.Info, $"Transfer arranged for {patient.FullName}: {note}", patient.Id, patient.CareSpaceId, patient.Triage);
    }

    // ---------------------------------------------------------------- every tick

    private void UpdateWards()
    {
        var waiting = _patients.Values
            .Where(p => p.Stage == PatientStage.AwaitingBed && p.IsAlive)
            .OrderBy(p => p.Triage)
            .ThenBy(p => p.BedRequestedAt)
            .ToList();

        foreach (var patient in waiting)
        {
            // A reserved bed whose ward has gone is no longer held.
            if (patient.ReservedWardId is { } heldWard && FindSpace(heldWard) is null)
            {
                patient.ReservedWardId = null;
                patient.ReservedBedIndex = null;
                RemoveTasks(patient, TaskKind.EscortToWard);
            }

            if (patient.ReservedWardId is not null)
            {
                continue;
            }

            var diagnosed = Database.Conditions[patient.WorkingDiagnosis!.ConditionId];
            if (patient.PreOp)
            {
                // Their bed on the assessment unit has gone: they wait for theatre in A&E instead.
                patient.PreOp = false;
                RequestSurgery(patient, diagnosed);
                continue;
            }

            var wards = WardsFor(patient);
            if (wards.Count == 0 || Now - patient.BedRequestedAt > MaxWaitForBed)
            {
                var reason = wards.Count > 0
                    ? $"No ward bed after {MaxWaitForBed.TotalHours:0} hours, so they are being transferred to a neighbouring hospital."
                    : NoWardReason(patient);
                ArrangeTransfer(patient, diagnosed, reason);
                continue;
            }

            if (FreeWardBed(patient) is { } free)
            {
                _activeCapacityAlerts.Remove("wards");
                free.Bed.OccupantId = patient.Id;
                patient.ReservedWardId = free.Ward.Id;
                patient.ReservedBedIndex = free.Index;
                var outlier = free.Ward.DepartmentId == EmergencyDepartmentId && patient.CareLevel < 2 && DepartmentOf(patient) != EmergencyDepartmentId && !patient.ForObservation
                    ? $" Their {DepartmentName(DepartmentOf(patient))} ward is full, so they go to the A&E ward for now."
                    : string.Empty;
                patient.Log(Now, $"Bed {free.Index + 1} on {free.Ward.Name} allocated.{outlier}");
                AddTask(TaskKind.EscortToWard, StaffRole.Nurse, patient, 5, $"Take to {free.Ward.Name}");
            }
            else if (Now - patient.BedRequestedAt > TimeSpan.FromHours(1) && patient.CareLevel >= 2)
            {
                RaiseCapacityAlert("critical_care", $"Critical care is full: {patient.FullName} has waited over an hour in A&E for {LevelName(patient.CareLevel)}. Build more ICU or HDU beds, or move patients down.", patient);
            }
            else if (Now - patient.BedRequestedAt > TimeSpan.FromHours(1))
            {
                RaiseCapacityAlert("wards", $"The wards are full: {patient.FullName} has waited over an hour in A&E for a bed. Build more ward beds or patients will be sent elsewhere.", patient);
            }
        }

        UpdateWardMoves();

        foreach (var patient in _patients.Values.Where(p => p.Stage == PatientStage.OnWard && p.IsAlive && p.BusyWithTaskId is null))
        {
            if (patient.MedicinesReadyAt is { } ready)
            {
                if (Now >= ready && !HasTask(patient, TaskKind.WardDischarge))
                {
                    AddTask(TaskKind.WardDischarge, StaffRole.Nurse, patient, 15, "Discharge home");
                }
            }
            else if (Now >= patient.DischargeDueAt && !HasTask(patient, TaskKind.WardRound) && !HasTask(patient, TaskKind.WardClerking) && !HasTask(patient, TaskKind.DispenseMedicines) && !HasTask(patient, TaskKind.DeliverMedicines))
            {
                AddTask(TaskKind.WardRound, StaffRole.Doctor, patient, 10, "Ward round review");
            }
        }
    }

    private void ReleaseReservedBed(Patient patient)
    {
        if (patient.ReservedWardId is { } wardId && FindSpace(wardId) is { } ward && patient.ReservedBedIndex is { } index
            && index < ward.Beds.Count && ward.Beds[index].OccupantId == patient.Id)
        {
            ward.Beds[index].OccupantId = null;
        }

        patient.ReservedWardId = null;
        patient.ReservedBedIndex = null;
    }

    private void RemoveTasks(Patient patient, TaskKind kind)
    {
        foreach (var task in _tasks.Values.Where(t => t.PatientId == patient.Id && t.Kind == kind).ToList())
        {
            RemoveTask(task);
        }
    }

    // ---------------------------------------------------------------- moving to the ward

    /// <summary>The nurse sets off: the patient leaves their cubicle and goes up to the ward bed.</summary>
    private void StartEscortToWard(Patient patient)
    {
        if (patient.ReservedWardId is not { } wardId || FindSpace(wardId) is not { } ward || patient.ReservedBedIndex is not { } index)
        {
            return;
        }

        var from = patient.Stage == PatientStage.OnWard && patient.CareSpaceId is { } oldId ? FindSpace(oldId) : null;
        ReleaseSpace(patient);
        ward.Beds[index].OccupantId = patient.Id;
        patient.CareSpaceId = ward.Id;
        patient.BedIndex = index;
        WalkTo(patient, ward.Beds[index].PatientSpot);
        patient.Log(Now, from is not null ? $"Moving from {from.Name} to {ward.Name}." : $"Left A&E for {ward.Name}.");
    }

    private void CompleteEscortToWard(StaffMember nurse, Patient patient)
    {
        if (BedOf(patient) is null || patient.CareSpaceId is not { } wardId || FindSpace(wardId) is not { } ward)
        {
            return;
        }

        var diagnosed = Database.Conditions[patient.WorkingDiagnosis!.ConditionId];
        var unit = UnitOf(ward);
        if (patient.AdmittedAt is not null && patient.Stage == PatientStage.OnWard)
        {
            // A move between wards: no new admission, but a new team takes over.
            patient.ReservedWardId = null;
            patient.ReservedBedIndex = null;
            patient.WardMoveRequestedAt = null;
            patient.DischargeDueAt = StayOn(patient, diagnosed, unit, moved: true);
            patient.Log(Now, $"Moved to {ward.Name}, bed {patient.BedIndex + 1}, by {nurse.Name}.", important: true);
            if (unit is WardUnit.IntensiveCare or WardUnit.HighDependency)
            {
                AddTask(TaskKind.WardClerking, StaffRole.Doctor, patient, 15, unit == WardUnit.IntensiveCare ? "ICU admission" : "HDU admission");
            }

            return;
        }

        patient.Stage = PatientStage.OnWard;
        patient.ReservedWardId = null;
        patient.ReservedBedIndex = null;
        patient.AdmittedAt = Now;
        patient.LeftEmergencyAt = Now;
        patient.DischargeDueAt = patient.PreOp ? null : StayOn(patient, diagnosed, unit, moved: false);
        var how = patient.SameDay ? "Moved for same-day care" : patient.ForObservation ? "Moved for observation" : patient.PreOp ? "Moved to wait for theatre" : "Admitted";
        patient.Log(Now, $"{how} to {ward.Name}, bed {patient.BedIndex + 1}, handed over by {nurse.Name}. Time in A&E {(Now - patient.ArrivedAt).TotalHours:0.0} hours.", important: true);
        Stats.RecordAdmission(patient, Now);
        PayForAttendance(patient);

        foreach (var doctor in _staff.Where(s => s.BaseRoomId is null))
        {
            doctor.Caseload.Remove(patient.Id);
        }

        AddTask(TaskKind.WardClerking, StaffRole.Doctor, patient, 15, "Admission clerking");
        if (patient.PreOp)
        {
            var operation = diagnosed.Operation!;
            AddTask(TaskKind.Surgery, RoleForRoom(operation.Room), patient, operation.Minutes, operation.Name, diagnosed.Id);
        }
    }

    /// <summary>When the team next review whether the patient can go: hours on ICU/HDU, a few hours after a day case, or the usual stay.</summary>
    private DateTime StayOn(Patient patient, ConditionDefinition diagnosed, WardUnit unit, bool moved)
    {
        if (unit is WardUnit.IntensiveCare or WardUnit.HighDependency)
        {
            return Now.AddHours(Random.Range(6.0, 10.0));
        }

        if (patient.DayCase && unit == WardUnit.DaySurgery)
        {
            return Now.AddHours(Random.Range(3.0, 5.0));
        }

        if (moved && patient.DischargeDueAt is { } due && due > Now)
        {
            // Out of an assessment unit or down from critical care: the plan they already had stands.
            return due;
        }

        var stay = patient.SameDay && diagnosed.AmbulatoryHours is { } sameDay ? sameDay
            : patient.ForObservation && diagnosed.ObserveHours is { } observe ? observe
            : diagnosed.LengthOfStayHours;
        var hours = Random.Range(stay[0], stay[1]);
        return Now.AddHours(moved ? hours * 0.6 : hours);
    }

    // ---------------------------------------------------------------- care on the ward

    /// <summary>
    /// The ward doctor sees the new admission. A skilled doctor can spot a wrong A&amp;E diagnosis and put
    /// the treatment right; otherwise the patient carries on deteriorating until someone does.
    /// </summary>
    private void CompleteClerking(StaffMember doctor, Patient patient)
    {
        doctor.Caseload.Add(patient.Id);
        var wrong = patient.WorkingDiagnosis?.ConditionId != patient.Condition.Id;
        if (wrong && Random.Chance(doctor.Skill * (1 - (doctor.Fatigue * 0.5)) * 0.8))
        {
            patient.WorkingDiagnosis = new Diagnosis(patient.Condition.Id, 0.9, Now, doctor.Id);
            patient.EffectiveTreatmentGiven = true;
            Stats.WardDiagnosesCorrected++;
            patient.Log(Now, $"Clerked by {doctor.Name}, who revised the diagnosis to {patient.Condition.Name.ToLowerInvariant()} and started the right treatment.", important: true);
            Notifications.Add(Now, NotificationLevel.Warning, $"Ward team corrected {patient.FullName}'s diagnosis to {patient.Condition.Name.ToLowerInvariant()}.", patient.Id, patient.CareSpaceId);
            return;
        }

        if (!wrong && !patient.EffectiveTreatmentGiven)
        {
            // Something was withheld in A&E (an allergy): the ward team find an alternative.
            patient.EffectiveTreatmentGiven = true;
            patient.Log(Now, $"Clerked by {doctor.Name}. Alternative treatment started on the ward.");
            return;
        }

        patient.Log(Now, $"Clerked by {doctor.Name}. Plan: continue treatment, observations on the ward, review on the ward round.");
    }

    private void CompleteWardRound(StaffMember doctor, Patient patient)
    {
        if (CriticalUnitOfPatient(patient) is { } critical)
        {
            CriticalCareRound(doctor, patient, critical);
            return;
        }

        if (EscalateOnWardRound(doctor, patient))
        {
            return;
        }

        if (patient.DayCase)
        {
            if (patient.Severity <= SameDayFitSeverity)
            {
                patient.Log(Now, $"Ward round with {doctor.Name}: recovered from the anaesthetic and well enough to go home today.");
                ArrangeMedicinesToTakeHome(patient);
                return;
            }

            patient.DayCase = false;
            patient.Log(Now, $"Ward round with {doctor.Name}: not ready for home after the day case operation, so kept in overnight.", important: true);
        }

        if (patient.SameDay && patient.Severity <= SameDayFitSeverity)
        {
            patient.Log(Now, $"Ward round with {doctor.Name}: well enough to finish treatment at home, with a review at the ambulatory unit tomorrow.");
            ArrangeMedicinesToTakeHome(patient);
            return;
        }

        if (patient.SameDay)
        {
            patient.SameDay = false;
            patient.Log(Now, $"Ward round with {doctor.Name}: not well enough for home after same-day care, so admitted after all.", important: true);
        }

        if (patient.Severity <= FitForDischargeSeverity)
        {
            patient.Log(Now, $"Ward round with {doctor.Name}: well enough to go home.");
            ArrangeMedicinesToTakeHome(patient);
            return;
        }

        patient.DischargeDueAt = Now.AddHours(12);
        patient.Log(Now, $"Ward round with {doctor.Name}: not yet well enough to go home. Review in 12 hours.");
    }

    private void CompleteWardDischarge(StaffMember nurse, Patient patient)
    {
        var ward = patient.CareSpaceId is { } id ? FindSpace(id) : null;
        var days = (Now - (patient.AdmittedAt ?? Now)).TotalDays;
        patient.OutcomeNote = $"Admitted to {ward?.Name ?? "a ward"} and discharged home after {days:0.0} days.";
        patient.Log(Now, $"Discharged home from {ward?.Name ?? "the ward"} by {nurse.Name} after {days:0.0} days.");
        EndVisit(patient, PatientOutcome.Discharged);
        if (patient.Maternity)
        {
            SendBabiesHomeWith(patient);
        }
        WalkTo(patient, WalkInSpawn);
    }

    // ---------------------------------------------------------------- ward teams

    /// <summary>Where ward staff wait between tasks: spread along the aisle inside the ward door.</summary>
    private GridCoord WardHome(StaffMember member, CareSpace ward)
    {
        var team = _staff.Where(s => s.BaseRoomId == ward.Id).ToList();
        var index = Math.Max(0, team.IndexOf(member));
        var spot = ward.StaffSpot;
        var r = ward.Rect;
        var x = Math.Clamp(spot.X + ((index % 2 == 0 ? 1 : -1) * ((index + 1) / 2) * 2), r.X + 1, r.Right - 2);
        return new GridCoord(x, Math.Clamp(spot.Y, r.Y + 1, r.Bottom - 2));
    }
}
