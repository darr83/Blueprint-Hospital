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
/// longer to go home from the wards.
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
        _ => null,
    };

    /// <summary>Whether this member of staff's team does the task: service work in their own room, ward work on their ward, or A&amp;E work.</summary>
    private bool IsForMember(ClinicalTask task, StaffMember member)
    {
        if (task.Kind == TaskKind.MovePatient && member.Role == StaffRole.Porter)
        {
            return true;
        }

        if (ServiceRoomKind(task) is not { } kind)
        {
            return TeamFor(task) == member.BaseRoomId || CoveredByEmergencyTeam(task, member);
        }

        return member.BaseRoomId is { } id && FindSpace(id) is { } room && room.Kind == kind
               && (room.ReservedByStaffId is null || room.ReservedByStaffId == member.Id)
               && (task.Kind != TaskKind.Surgery || kind != CareSpaceKind.Theatre || (SurgeryDepartment(task.Detail) is { } dept && room.AllowedDepartments.Contains(dept)));
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
        if (task.Kind is TaskKind.Imaging or TaskKind.Surgery)
        {
            ReleaseSeat(patient);
            WalkTo(patient, room.PatientSpot);
            patient.Log(Now, task.Kind == TaskKind.Surgery ? $"Taken to {room.Name} for {task.Label.ToLowerInvariant()}." : $"Taken to {room.Name} for {task.Label}.");
        }
    }

    /// <summary>After a scan or an operation the patient goes back to their cubicle (or the waiting room).</summary>
    private void ReturnFromServiceRoom(Patient patient)
    {
        if (patient.CareSpaceId is { } id && FindSpace(id) is { IsTreatmentSpace: true } cubicle)
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
        ReturnFromServiceRoom(patient);
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
        var place = operation.Room == CareSpaceKind.CathLab ? "the cath lab" : "theatre";
        if (!HasProcedureRoomFor(operation, diagnosed.Department))
        {
            var why = operation.Room != CareSpaceKind.Theatre
                ? "there is no staffed cath lab on site"
                : SpacesOfKind(CareSpaceKind.Theatre).Any(IsStaffed)
                    ? $"no theatre here takes {DepartmentName(diagnosed.Department)} patients"
                    : "there is no operating theatre on site";
            ArrangeTransfer(patient, diagnosed, $"Needs {LowerFirst(operation.Name)}, but {why}, so they are being transferred to a neighbouring hospital.");
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
        var operation = planned.Operation!;
        var inTheatre = operation.Room == CareSpaceKind.Theatre;
        if (planned.Id == patient.Condition.Id)
        {
            patient.Log(Now, $"{operation.Name} performed by {surgeon.Name}. The {(inTheatre ? "operation" : "procedure")} went well.", important: true);
        }
        else
        {
            // The surgeon finds what is really wrong and the team change the plan.
            patient.WorkingDiagnosis = new Diagnosis(patient.Condition.Id, 0.95, Now, surgeon.Id);
            patient.Log(Now, $"{operation.Name} by {surgeon.Name}: the findings did not fit {planned.Name.ToLowerInvariant()}. The {(inTheatre ? "surgeon" : "team")} found {patient.Condition.Name.ToLowerInvariant()}.", important: true);
            Notifications.Add(Now, NotificationLevel.Warning, $"Wrong diagnosis found in {(inTheatre ? "theatre" : "the cath lab")}: {patient.FullName} actually had {patient.Condition.Name.ToLowerInvariant()}.", patient.Id);
        }

        patient.EffectiveTreatmentGiven = true;
        patient.HadSurgery = true;
        patient.Severity = Math.Min(patient.Severity, 0.6) * 0.7;
        Stats.OperationsDone++;
        Book(MoneyCategory.Operations, OperationTariff);
        ReturnFromServiceRoom(patient);
        RequestWardBed(patient, Database.Conditions[patient.WorkingDiagnosis.ConditionId], $"Back from {(inTheatre ? "theatre" : "the cath lab")}; needs a ward bed to recover.");
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
        patient.MedicinesReadyAt = Now;
        patient.Log(Now, $"Medicines to take home checked and dispensed by {pharmacist.Name}.");
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
                case TaskKind.Surgery when patient.Stage == PatientStage.AwaitingSurgery:
                    ArrangeTransfer(patient, Database.Conditions[patient.WorkingDiagnosis!.ConditionId], "No operating theatre or cath lab here can take them any more, so they are being transferred to a neighbouring hospital.");
                    break;
                case TaskKind.DispenseMedicines:
                    patient.MedicinesReadyAt = Now + OutsidePharmacyDelay;
                    patient.Log(Now, "The pharmacy has closed: medicines to take home ordered from an outside pharmacy.");
                    break;
            }
        }
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
