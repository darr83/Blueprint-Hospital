using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Shared services with their own rooms and staff: imaging (X-ray and CT, run by radiographers),
/// operating theatres (a surgeon) and the pharmacy (a pharmacist). Each room brings its own staff
/// when it is built. Without imaging, doctors diagnose without scans; without a theatre, patients
/// who need an operation are transferred; without a pharmacy, medicines cost more and patients wait
/// longer to go home from the wards. Collecting prescriptions and carrying ward medicines are in the
/// Pharmacy part.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How long ward patients wait for medicines to take home when there is no pharmacy on site.</summary>
    public static readonly TimeSpan OutsidePharmacyDelay = TimeSpan.FromHours(4);

    public bool HasPharmacy => SpacesOfKind(CareSpaceKind.Pharmacy).Any(IsStaffed);

    /// <summary>Whether the hospital can do this test now (imaging needs its room).</summary>
    public bool CanDo(InvestigationDefinition test) => test.Room is not { } room || SpacesOfKind(room).Any(IsStaffed);

    /// <summary>The kind of room a task is done in by that room's own staff, if any.</summary>
    private CareSpaceKind? ServiceRoomKind(ClinicalTask task) => task.Kind switch
    {
        TaskKind.Imaging => Database.Investigations[task.Detail!].Room,
        TaskKind.Surgery => OperationRoom(task.Detail),
        TaskKind.DispenseMedicines => CareSpaceKind.Pharmacy,
        TaskKind.Procedure when task.Role is not (StaffRole.Nurse or StaffRole.Doctor) => StepRoom(task),
        _ => null,
    };

    /// <summary>The specialist room a treatment step is done in, if it names one.</summary>
    private CareSpaceKind? StepRoom(ClinicalTask task) =>
        FindPatient(task.PatientId) is { } patient
        && int.TryParse(task.Detail, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index)
        && index >= 0 && index < patient.TreatmentPlan.Count
            ? patient.TreatmentPlan[index].Step.Room
            : null;

    /// <summary>Whether this member of staff's team does the task: service work in their own room, ward work on their ward, or A&amp;E work.</summary>
    private bool IsForMember(ClinicalTask task, StaffMember member)
    {
        if (task.Kind is TaskKind.MovePatient or TaskKind.DeliverMedicines && member.Role == StaffRole.Porter)
        {
            return true;
        }

        if (task.Kind == TaskKind.Physio)
        {
            return member.Role == StaffRole.Physiotherapist;
        }

        if (task.Kind == TaskKind.OutreachReview)
        {
            return member.Role == StaffRole.OutreachNurse;
        }

        if (task.Kind == TaskKind.CarryBaby)
        {
            return IsCarryForMember(task, member);
        }

        if (ServiceRoomKind(task) is not { } kind)
        {
            return TeamFor(task) == member.BaseRoomId || CoveredByEmergencyTeam(task, member);
        }

        return member.BaseRoomId is { } id && FindSpace(id) is { } room && room.Kind == kind
               && (room.ReservedByStaffId is null || room.ReservedByStaffId == member.Id)
               && !_recovering.Values.Any(r => r.RoomId == room.Id)
               && DeliveryRoomFreeFor(room, task.PatientId)
               && (task.Kind != TaskKind.Surgery || kind != CareSpaceKind.Theatre
                   || (SurgeryDepartment(task.Detail) is { } dept && room.AllowedDepartments.Contains(dept) && FreeAnaesthetist(room) is not null));
    }

    /// <summary>The member of staff starts work in their room; for a scan or an operation the patient is taken there.</summary>
    private void StartServiceTask(StaffMember member, ClinicalTask task, Patient patient)
    {
        if (member.BaseRoomId is not { } id || FindSpace(id) is not { } room)
        {
            return;
        }

        task.SpaceId = room.Id;
        room.ReservedByStaffId = member.Id;
        if (room.Kind == CareSpaceKind.DeliveryRoom)
        {
            // She has the room to herself through labour and the birth.
            room.OccupantId = patient.Id;
        }

        if (task.Kind is TaskKind.Imaging or TaskKind.Surgery or TaskKind.Procedure)
        {
            ReleaseSeat(patient);
            WalkTo(patient, room.PatientSpot);
            if (task.Kind == TaskKind.Surgery && room.Kind == CareSpaceKind.Theatre)
            {
                JoinAnaesthetist(task, room, patient);
            }

            patient.Log(Now, task.Kind == TaskKind.Surgery ? $"Taken to {room.Name} for {task.Label.ToLowerInvariant()}." : $"Taken to {room.Name} for {task.Label}.");
        }
    }

    /// <summary>After a scan or an operation the patient goes back to their cubicle (or the waiting room).</summary>
    private void ReturnFromServiceRoom(Patient patient)
    {
        if (BedOf(patient) is { } bed)
        {
            WalkTo(patient, bed.PatientSpot);
        }
        else if (patient.CareSpaceId is { } id && FindSpace(id) is { IsTreatmentSpace: true } cubicle)
        {
            WalkTo(patient, cubicle.PatientSpot);
        }
        else
        {
            SendToWaitingRoom(patient);
        }
    }

    // ---------------------------------------------------------------- imaging

    private void CompleteImaging(StaffMember radiographer, Patient patient, string investigationId)
    {
        var order = patient.Investigations.FirstOrDefault(o => o.InvestigationId == investigationId && o.Status == InvestigationStatus.Requested);
        if (Database.Investigations[investigationId].Room == CareSpaceKind.Endoscopy && radiographer.BaseRoomId is { } scopeRoom)
        {
            StartRecovery(patient, scopeRoom);
        }
        else
        {
            ReturnFromServiceRoom(patient);
        }

        if (order is null)
        {
            return;
        }

        var test = Database.Investigations[investigationId];
        order.Status = InvestigationStatus.SampleTaken;
        order.SampleTakenAt = Now;
        order.ResultDueAt = Now.AddMinutes(test.TurnaroundMinutes);
        Stats.ScansDone++;
        var reporter = test.Room is CareSpaceKind.XRay or CareSpaceKind.CT ? "the radiologist's report" : "the report";
        patient.Log(Now, $"{test.Name} done by {radiographer.Name}; waiting for {reporter}.");
    }

    // ---------------------------------------------------------------- theatres

    /// <summary>The doctor has decided the patient needs an operation.</summary>
    private void RequestSurgery(Patient patient, ConditionDefinition diagnosed)
    {
        var operation = diagnosed.Operation!;
        var place = ProcedurePlace(operation.Room);
        if (!HasProcedureRoomFor(operation, diagnosed.Department))
        {
            var why = operation.Room != CareSpaceKind.Theatre
                ? "there is no staffed cath lab on site"
                : SpacesOfKind(CareSpaceKind.Theatre).Any(IsStaffed)
                    ? $"no theatre here takes {DepartmentName(diagnosed.Department)} patients"
                    : SpacesOfKind(CareSpaceKind.Theatre).Any(t => _staff.Any(s => s.BaseRoomId == t.Id))
                        ? "the theatre needs both a surgeon and an anaesthetist"
                        : "there is no operating theatre on site";
            ArrangeTransfer(patient, diagnosed, $"Needs {LowerFirst(operation.Name)}, but {why}, so they are being transferred to a neighbouring hospital.");
            return;
        }

        if (operation.Room == CareSpaceKind.Theatre && FreeAssessmentBed(diagnosed.Department) is { } unit)
        {
            // Waits for theatre on the assessment unit, freeing their A&E cubicle.
            patient.PreOp = true;
            patient.Stage = PatientStage.AwaitingBed;
            patient.BedRequestedAt = Now;
            unit.Ward.Beds[unit.Index].OccupantId = patient.Id;
            patient.ReservedWardId = unit.Ward.Id;
            patient.ReservedBedIndex = unit.Index;
            patient.Log(Now, $"{diagnosed.DispositionNote} Booked for theatre; waits on {unit.Ward.Name}, bed {unit.Index + 1}.", important: true);
            Notifications.Add(Now, NotificationLevel.Info, $"{patient.FullName} is going to {unit.Ward.Name} to wait for theatre ({LowerFirst(operation.Name)}).", patient.Id, patient.CareSpaceId, patient.Triage);
            AddTask(TaskKind.EscortToWard, StaffRole.Nurse, patient, 5, $"Take to {unit.Ward.Name}");
            return;
        }

        patient.Stage = PatientStage.AwaitingSurgery;
        patient.Log(Now, $"{diagnosed.DispositionNote} Booked for {place}.", important: true);
        Notifications.Add(Now, NotificationLevel.Info, $"{patient.FullName} is waiting for {place} ({LowerFirst(operation.Name)}).", patient.Id, patient.CareSpaceId, patient.Triage);
        AddTask(TaskKind.Surgery, RoleForRoom(operation.Room), patient, operation.Minutes, operation.Name, diagnosed.Id);
    }

    private void CompleteSurgery(StaffMember surgeon, Patient patient)
    {
        var planned = Database.Conditions[patient.WorkingDiagnosis!.ConditionId];
        if (planned.IsMaternity)
        {
            CompleteBirth(surgeon, patient);
            return;
        }

        var operation = planned.Operation!;
        var inTheatre = operation.Room == CareSpaceKind.Theatre;
        var place = ProcedurePlace(operation.Room);
        if (planned.Id == patient.Condition.Id)
        {
            patient.Log(Now, $"{operation.Name} performed by {surgeon.Name}. The {(inTheatre ? "operation" : "procedure")} went well.", important: true);
        }
        else
        {
            // The surgeon finds what is really wrong and the team change the plan.
            patient.WorkingDiagnosis = new Diagnosis(patient.Condition.Id, 0.95, Now, surgeon.Id);
            patient.Log(Now, $"{operation.Name} by {surgeon.Name}: the findings did not fit {planned.Name.ToLowerInvariant()}. The {(inTheatre ? "surgeon" : "team")} found {patient.Condition.Name.ToLowerInvariant()}.", important: true);
            Notifications.Add(Now, NotificationLevel.Warning, $"Wrong diagnosis found in {place}: {patient.FullName} actually had {patient.Condition.Name.ToLowerInvariant()}.", patient.Id);
        }

        patient.EffectiveTreatmentGiven = true;
        patient.HadSurgery = true;
        patient.PreOp = false;
        patient.Severity = Math.Min(patient.Severity, 0.6) * 0.7;
        Stats.OperationsDone++;
        if (planned.IsPlanned)
        {
            CompletePlanned(patient, planned);
        }
        else
        {
            Book(MoneyCategory.Operations, OperationTariff);
        }

        ReturnFromServiceRoom(patient);

        // Big operations recover on HDU; small ones go home the same day from day surgery.
        var done = Database.Conditions[patient.WorkingDiagnosis.ConditionId];
        var after = planned.Id == done.Id ? operation : done.Operation ?? operation;
        patient.CareLevel = after.Major && HasHighDependencyBeds ? Math.Max(patient.CareLevel, 2) : 0;
        patient.DayCase = after.DayCase && Wards.Any(w => UnitOf(w) == WardUnit.DaySurgery);
        var needs = after.Major ? "high dependency care to recover" : patient.DayCase ? "a few hours on the day surgery unit" : "a ward bed to recover";
        RequestWardBed(patient, done, $"Back from {place}; needs {needs}.");
    }

    // ---------------------------------------------------------------- pharmacy

    /// <summary>The ward round says the patient can go: their medicines to take home are got ready first.</summary>
    private void ArrangeMedicinesToTakeHome(Patient patient)
    {
        if (HasPharmacy)
        {
            AddTask(TaskKind.DispenseMedicines, StaffRole.Pharmacist, patient, 10, "Dispense medicines to take home");
            return;
        }

        patient.MedicinesReadyAt = Now + OutsidePharmacyDelay;
        patient.Log(Now, $"No pharmacy on site: medicines to take home ordered from an outside pharmacy, ready in about {OutsidePharmacyDelay.TotalHours:0} hours.");
    }

    private void CompleteDispensing(StaffMember pharmacist, Patient patient)
    {
        patient.Log(Now, $"Medicines to take home checked and dispensed by {pharmacist.Name}; {(HasPorters ? "a porter" : "a nurse")} will bring them to the ward.");
        AddTask(TaskKind.DeliverMedicines, HasPorters ? StaffRole.Porter : StaffRole.Nurse, patient, 0, "Take medicines to the ward", pharmacist.BaseRoomId);
    }

    // ---------------------------------------------------------------- when rooms come and go

    /// <summary>Work that can no longer be done because its room has gone is cancelled or sent elsewhere.</summary>
    private void CancelUnavailableWork()
    {
        bool CanStillBeDone(ClinicalTask t) => t.Kind == TaskKind.Surgery
            ? t.Detail is { } id && Database.Conditions.TryGetValue(id, out var c) && c.Operation is { } op && HasProcedureRoomFor(op, c.Department)
            : ServiceRoomKind(t) is not { } kind || SpacesOfKind(kind).Any(IsStaffed);

        foreach (var task in _tasks.Values.Where(t => t.AssignedStaffId is null && !CanStillBeDone(t)).ToList())
        {
            RemoveTask(task);
            if (FindPatient(task.PatientId) is not { } patient)
            {
                continue;
            }

            switch (task.Kind)
            {
                case TaskKind.Imaging:
                    patient.Investigations.RemoveAll(o => o.InvestigationId == task.Detail && o.Status == InvestigationStatus.Requested);
                    patient.Log(Now, $"{task.Label} cancelled: there is no longer a room to do it in.");
                    RequestReviewIfResultsComplete(patient);
                    break;
                case TaskKind.Surgery when patient.Condition.IsPlanned && patient.PreOp && _plannedBookings.FirstOrDefault(b => b.PatientId == patient.Id) is { } booking:
                    CancelPlanned(booking, patient.Condition, "the room for it has closed", patient);
                    break;
                case TaskKind.Surgery when patient.Stage == PatientStage.AwaitingSurgery || patient.PreOp:
                    patient.PreOp = false;
                    ArrangeTransfer(patient, Database.Conditions[patient.WorkingDiagnosis!.ConditionId], "No operating theatre or cath lab here can take them any more, so they are being transferred to a neighbouring hospital.");
                    break;
                case TaskKind.Procedure when int.TryParse(task.Detail, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index) && index < patient.TreatmentPlan.Count:
                    var step = patient.TreatmentPlan[index].Step;
                    AddTask(TaskKind.Procedure, step.PerformedBy == ClinicalRole.Doctor ? StaffRole.Doctor : StaffRole.Nurse, patient, step.Minutes, step.Name!, task.Detail);
                    patient.Log(Now, $"{task.Label} will be done in A&E instead: the specialist room is no longer available.");
                    break;
                case TaskKind.DispenseMedicines:
                    patient.MedicinesReadyAt = Now + OutsidePharmacyDelay;
                    patient.Log(Now, "The pharmacy has closed: medicines to take home ordered from an outside pharmacy.");
                    break;
            }
        }
    }

    /// <summary>A free bed on one of the department's assessment units (e.g. the surgical assessment unit).</summary>
    private (CareSpace Ward, int Index)? FreeAssessmentBed(string departmentId)
    {
        foreach (var ward in WardsOf(departmentId).Where(w => UnitOf(w) == WardUnit.Assessment))
        {
            if (FreeBedIndex(ward) is var index and >= 0)
            {
                return (ward, index);
            }
        }

        return null;
    }

    /// <summary>The room an operation is done in (theatre unless the condition says otherwise).</summary>
    private CareSpaceKind OperationRoom(string? conditionId) =>
        conditionId is not null && Database.Conditions.TryGetValue(conditionId, out var condition) && condition.Operation is { } op ? op.Room : CareSpaceKind.Theatre;

    /// <summary>Whether a staffed room can do this operation for this department's patient.</summary>
    public bool HasProcedureRoomFor(OperationDefinition operation, string departmentId) =>
        operation.Room == CareSpaceKind.Theatre ? HasTheatreFor(departmentId) : SpacesOfKind(operation.Room).Any(IsStaffed);

    /// <summary>"Primary PCI" becomes "primary PCI", "Appendicectomy" becomes "appendicectomy".</summary>
    private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    /// <summary>Where a service room's staff wait between tasks.</summary>
    private static GridCoord ServiceRoomHome(CareSpace room) => room.StaffSpot;
}
