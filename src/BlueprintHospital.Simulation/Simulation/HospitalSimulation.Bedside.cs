using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Red monitors: when a patient's monitor goes red (NEWS2 7 or more; the triage category doesn't matter)
/// a healthcare assistant comes to sit with them one-to-one until it isn't red (NEWS2 back to 5 or less), and a
/// doctor comes to review them and then moves on. Only with both do they get better; until then they keep
/// getting worse, even if they've had the right medicine. With no healthcare assistants, a nurse sits with them.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>The highest priority on the board: above a new Red patient's first assessment.</summary>
    private const double BedsideBand = RedImmediate + 200;

    /// <summary>How close (in tiles) someone must be to count as with the patient.</summary>
    private const int BedsideReach = 3;

    /// <summary>Reviewed, with someone there, but no effective treatment yet: a red patient still improves slowly.</summary>
    private const double NaturalRecoveryShare = 0.5;

    private const double MinimumNaturalRecoveryPerHour = 0.1;

    /// <summary>Once the right treatment is in, a red patient is turned round within an hour or two.</summary>
    private const double MinimumTreatedRecoveryPerHour = 0.25;

    /// <summary>A nurse sits with a red patient themselves when no healthcare assistant has come for this long.</summary>
    public static readonly TimeSpan NurseSitsAfter = TimeSpan.FromMinutes(10);

    public bool HasHealthcareAssistants => _staff.Any(s => s.Role == StaffRole.HealthcareAssistant);

    /// <summary>Whoever is sitting with this patient, if they are actually with them right now.</summary>
    public StaffMember? CarerAtBedside(Patient patient) =>
        _staff.FirstOrDefault(s => s.BedsidePatientId == patient.Id && s.IsOnDuty && IsNear(s.Tile, patient.Tile));

    /// <summary>Whoever is assigned to sit with this patient, whether or not they are at the bed this moment.</summary>
    public StaffMember? BedsideCarerFor(Patient patient) => _staff.FirstOrDefault(s => s.BedsidePatientId == patient.Id);

    private static bool IsNear(GridCoord a, GridCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) <= BedsideReach;

    /// <summary>The right treatment works at the condition's own pace (never slower than getting better without it).</summary>
    private static double RedRecoveryPerHour(Patient patient)
    {
        var natural = Math.Max(MinimumNaturalRecoveryPerHour, patient.Condition.RecoveryPerHour * NaturalRecoveryShare);
        return patient.EffectiveTreatmentGiven ? Math.Max(MinimumTreatedRecoveryPerHour, patient.Condition.RecoveryPerHour) : natural;
    }

    /// <summary>A nurse takes the one-to-one themselves when no healthcare assistant has come.</summary>
    private bool NurseCanSit(ClinicalTask task, StaffMember member) =>
        task.Kind == TaskKind.StayWithPatient && member.Role == StaffRole.Nurse && Now - task.CreatedAt >= NurseSitsAfter;

    /// <summary>
    /// Calls someone to sit with, and a doctor to review, every red monitor; stands the sitter down once their
    /// patient isn't red any more.
    /// </summary>
    private void UpdateRedMonitors()
    {
        foreach (var carer in _staff.Where(s => s.BedsidePatientId is not null).ToList())
        {
            var patient = FindPatient(carer.BedsidePatientId!.Value);
            if (patient is null || !patient.IsInDepartment || !carer.IsOnDuty)
            {
                LeaveBedside(carer);
                continue;
            }

            if (!patient.UpdateMonitorRed())
            {
                patient.Log(Now, $"Monitor no longer red (NEWS2 {patient.LiveNews2}); {carer.Name} has left the bedside.");
                LeaveBedside(carer);
            }
        }

        foreach (var patient in _patients.Values)
        {
            patient.UpdateMonitorRed();
            var calls = _tasks.Values.Where(t => t.PatientId == patient.Id && t.Kind is TaskKind.StayWithPatient or TaskKind.RedReview).ToList();
            if (!patient.MonitorIsRed)
            {
                foreach (var call in calls)
                {
                    RemoveTask(call);
                }

                continue;
            }

            var news = patient.LiveNews2;
            var called = false;
            if (BedsideCarerFor(patient) is null && calls.All(t => t.Kind != TaskKind.StayWithPatient))
            {
                AddTask(TaskKind.StayWithPatient, HasHealthcareAssistants ? StaffRole.HealthcareAssistant : StaffRole.Nurse, patient, 0.5, "Sit with patient (1:1)");
                called = true;
            }

            if (!patient.RedReviewed && calls.All(t => t.Kind != TaskKind.RedReview))
            {
                AddTask(TaskKind.RedReview, StaffRole.Doctor, patient, 10, "Red monitor review");
                called = true;
            }

            if (called)
            {
                patient.Log(Now, $"Monitor red (NEWS2 {news}): needs someone to sit with them and a doctor's review.", important: true);
            }
        }
    }

    private void CompleteRedReview(StaffMember doctor, Patient patient)
    {
        if (!patient.RedReviewed)
        {
            MarkRedReviewed(doctor, patient);
        }
    }

    /// <summary>The doctor has seen the red patient: with someone sitting with them, they can now get better.</summary>
    private void MarkRedReviewed(StaffMember doctor, Patient patient)
    {
        patient.RedReviewed = true;
        patient.FirstSeenByDoctorAt ??= Now;
        doctor.Caseload.Add(patient.Id);
        patient.Log(Now, $"Red monitor reviewed at the bedside by {doctor.Name} (NEWS2 {patient.LiveNews2}).", important: true);
        foreach (var review in _tasks.Values.Where(t => t.PatientId == patient.Id && t.Kind == TaskKind.RedReview && t.AssignedStaffId is null).ToList())
        {
            RemoveTask(review);
        }
    }

    /// <summary>The carer has reached the patient and will sit with them until the monitor isn't red.</summary>
    private void ArriveAtBedside(StaffMember carer, Patient patient)
    {
        if (BedsideCarerFor(patient) is { } other && other != carer)
        {
            return;
        }

        carer.BedsidePatientId = patient.Id;
        patient.Log(Now, $"{carer.Name} is sitting with them until the monitor is no longer red (NEWS2 {patient.LiveNews2}).");
    }

    private void LeaveBedside(StaffMember carer)
    {
        carer.BedsidePatientId = null;
        if (carer.CurrentTaskId is null && carer.IsOnDuty && !carer.IsOnBreak)
        {
            carer.Activity = StaffActivity.Idle;
            carer.StatusText = "Available";
        }
    }

    /// <summary>Work the sitter does for their own patient: their own role's jobs, and obs.</summary>
    private bool IsBedsideWork(ClinicalTask task, StaffMember carer, Patient patient) =>
        task.PatientId == patient.Id
        && task.Kind is not (TaskKind.StayWithPatient or TaskKind.RedReview or TaskKind.MovePatient)
        && (task.Role == carer.Role || task.Kind == TaskKind.Observations)
        && ServiceRoomKind(task) is null;

    /// <summary>Nothing to do for their patient: sit at the bed (following them if they are being moved).</summary>
    private void StayAtBedside(StaffMember carer, Patient patient)
    {
        var spot = BedsideSpot(patient);
        if (!carer.IsMoving && carer.Tile != spot)
        {
            WalkTo(carer, spot);
        }

        carer.Activity = StaffActivity.Working;
        carer.StatusText = $"Sitting with {patient.FullName} (1:1) · NEWS2 {patient.LiveNews2}";
    }

    private GridCoord BedsideSpot(Patient patient)
    {
        if (BedOf(patient) is { } bed)
        {
            return bed.StaffSpot;
        }

        if (patient.CareSpaceId is { } spaceId && FindSpace(spaceId) is { } space && space.PatientSpot == (patient.Destination ?? patient.Tile))
        {
            return space.StaffSpot;
        }

        return BesideTile(patient.Destination ?? patient.Tile);
    }

    /// <summary>The carer walks to the bed (following the patient if they are moving) and takes over once there.</summary>
    private void ProgressBedsideCall(StaffMember carer, ClinicalTask task, Patient patient)
    {
        var spot = BedsideSpot(patient);
        if (!IsNear(carer.Tile, patient.Tile) || (carer.IsMoving && carer.Destination != spot))
        {
            if (!carer.IsMoving || carer.Destination != spot)
            {
                WalkTo(carer, spot);
            }

            return;
        }

        _tasks.Remove(task.Id);
        carer.CurrentTaskId = null;
        carer.TasksCompleted++;
        ArriveAtBedside(carer, patient);
        StayAtBedside(carer, patient);
    }
}
