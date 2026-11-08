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

    public bool HasPharmacy => SpacesOfKind(CareSpaceKind.Pharmacy).Any();

    /// <summary>Whether the hospital can do this test now (imaging needs its room).</summary>
    public bool CanDo(InvestigationDefinition test) => test.Room is not { } room || SpacesOfKind(room).Any();

    /// <summary>The kind of room a task is done in by that room's own staff, if any.</summary>
    private CareSpaceKind? ServiceRoomKind(ClinicalTask task) => task.Kind switch
    {
        TaskKind.Imaging => Database.Investigations[task.Detail!].Room,
        TaskKind.Surgery => CareSpaceKind.Theatre,
        TaskKind.DispenseMedicines => CareSpaceKind.Pharmacy,
        _ => null,
    };

    /// <summary>Whether this member of staff's team does the task: service work in their own room, ward work on their ward, or A&amp;E work.</summary>
    private bool IsForMember(ClinicalTask task, StaffMember member)
    {
        if (ServiceRoomKind(task) is not { } kind)
        {
            return TeamFor(task) == member.BaseRoomId;
        }

        return member.BaseRoomId is { } id && FindSpace(id) is { } room && room.Kind == kind
               && (room.ReservedByStaffId is null || room.ReservedByStaffId == member.Id);
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
        patient.Log(Now, $"{test.Name} done by {radiographer.Name}; waiting for the radiologist's report.");
    }

    // ---------------------------------------------------------------- theatres

    /// <summary>The doctor has decided the patient needs an operation.</summary>
    private void RequestSurgery(Patient patient, ConditionDefinition diagnosed)
    {
        var operation = diagnosed.Operation!;
        if (!SpacesOfKind(CareSpaceKind.Theatre).Any())
        {
            ArrangeTransfer(patient, diagnosed, $"Needs {operation.Name.ToLowerInvariant()}, but there is no operating theatre on site, so they are being transferred to a neighbouring hospital.");
            return;
        }

        patient.Stage = PatientStage.AwaitingSurgery;
        patient.Log(Now, $"{diagnosed.DispositionNote} Booked for theatre.", important: true);
        Notifications.Add(Now, NotificationLevel.Info, $"{patient.FullName} is waiting for theatre ({operation.Name.ToLowerInvariant()}).", patient.Id, patient.CareSpaceId, patient.Triage);
        AddTask(TaskKind.Surgery, StaffRole.Surgeon, patient, operation.Minutes, operation.Name, diagnosed.Id);
    }

    private void CompleteSurgery(StaffMember surgeon, Patient patient)
    {
        var planned = Database.Conditions[patient.WorkingDiagnosis!.ConditionId];
        var operation = planned.Operation!;
        if (planned.Id == patient.Condition.Id)
        {
            patient.Log(Now, $"{operation.Name} performed by {surgeon.Name}. The operation went well.", important: true);
        }
        else
        {
            // The surgeon finds what is really wrong and the team change the plan.
            patient.WorkingDiagnosis = new Diagnosis(patient.Condition.Id, 0.95, Now, surgeon.Id);
            patient.Log(Now, $"{operation.Name} by {surgeon.Name}: the findings did not fit {planned.Name.ToLowerInvariant()}. The surgeon found {patient.Condition.Name.ToLowerInvariant()}.", important: true);
            Notifications.Add(Now, NotificationLevel.Warning, $"Wrong diagnosis found in theatre: {patient.FullName} actually had {patient.Condition.Name.ToLowerInvariant()}.", patient.Id);
        }

        patient.EffectiveTreatmentGiven = true;
        patient.Severity = Math.Min(patient.Severity, 0.6) * 0.7;
        Stats.OperationsDone++;
        Book(MoneyCategory.Operations, OperationTariff);
        ReturnFromServiceRoom(patient);
        RequestWardBed(patient, Database.Conditions[patient.WorkingDiagnosis.ConditionId], "Back from theatre; needs a ward bed to recover.");
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
        foreach (var task in _tasks.Values.Where(t => t.AssignedStaffId is null && ServiceRoomKind(t) is { } kind && !SpacesOfKind(kind).Any()).ToList())
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
                    ArrangeTransfer(patient, Database.Conditions[patient.WorkingDiagnosis!.ConditionId], "The operating theatre has closed, so they are being transferred to a neighbouring hospital.");
                    break;
                case TaskKind.DispenseMedicines:
                    patient.MedicinesReadyAt = Now + OutsidePharmacyDelay;
                    patient.Log(Now, "The pharmacy has closed: medicines to take home ordered from an outside pharmacy.");
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- room teams

    /// <summary>
    /// Wards and service rooms bring their own staff, who join when the room is built and leave when it
    /// is demolished: each ward has a nurse for every <see cref="PatientsPerWardNurse"/> beds and a doctor;
    /// each X-ray or CT room a radiographer; each theatre a surgeon; each pharmacy a pharmacist.
    /// </summary>
    private void SyncRoomStaff()
    {
        var rooms = _spaces.Values.Where(s => s.IsWard || s.IsServiceRoom).ToDictionary(s => s.Id);
        foreach (var member in _staff.Where(s => s.BaseRoomId is { } id && !rooms.ContainsKey(id)).ToList())
        {
            if (member.CurrentTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task))
            {
                RemoveTask(task);
            }

            _staff.Remove(member);
        }

        foreach (var room in rooms.Values)
        {
            var team = _staff.Where(s => s.BaseRoomId == room.Id).ToList();
            if (room.IsWard)
            {
                var nurses = Math.Max(1, (room.Beds.Count + PatientsPerWardNurse - 1) / PatientsPerWardNurse);
                var haveNurses = team.Count(s => s.Role == StaffRole.Nurse);
                for (var i = haveNurses; i < nurses; i++)
                {
                    Hire(StaffRole.Nurse, i == 0 ? "Ward sister" : "Ward nurse", room);
                }

                foreach (var spare in team.Where(s => s.Role == StaffRole.Nurse && s.CurrentTaskId is null).Skip(nurses).ToList())
                {
                    _staff.Remove(spare);
                }

                if (!team.Any(s => s.Role == StaffRole.Doctor))
                {
                    Hire(StaffRole.Doctor, "Ward doctor (FY1)", room);
                }

                continue;
            }

            var (role, grade) = room.Kind switch
            {
                CareSpaceKind.XRay => (StaffRole.Radiographer, "Radiographer"),
                CareSpaceKind.CT => (StaffRole.Radiographer, "CT radiographer"),
                CareSpaceKind.Theatre => (StaffRole.Surgeon, "Consultant surgeon"),
                _ => (StaffRole.Pharmacist, "Pharmacist"),
            };
            if (team.Count == 0)
            {
                Hire(role, grade, room);
            }
        }

        CancelUnavailableWork();
    }

    private void Hire(StaffRole role, string grade, CareSpace room)
    {
        var (skill, years, salary) = grade switch
        {
            "Ward sister" => (0.8, 10, 43_000),
            "Ward nurse" => (0.6, 3, 31_000),
            "Ward doctor (FY1)" => (0.55, 1, 36_000),
            "Radiographer" => (0.7, 5, 35_000),
            "CT radiographer" => (0.75, 8, 41_000),
            "Consultant surgeon" => (0.85, 15, 105_000),
            _ => (0.75, 6, 46_000),
        };
        var member = MakeStaff(role, grade, skill, 1.0, years, salary, room.DepartmentId, room.Id);
        _staff.Add(member);
        member.PlaceAt(HomeTile(member));
    }

    /// <summary>Where a service room's staff wait between tasks.</summary>
    private static GridCoord ServiceRoomHome(CareSpace room) => room.StaffSpot;
}
