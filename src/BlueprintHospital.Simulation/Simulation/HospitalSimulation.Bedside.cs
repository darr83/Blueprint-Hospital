using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Red monitors: when a patient's monitor goes red (NEWS2 7 or more, or a Red triage nobody has stabilised yet)
/// a doctor goes to the bedside and stays there until it isn't red (NEWS2 back to 5 or less). While they are there the patient gets
/// better; without a doctor they keep getting worse, even if they've had the right medicine.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>The highest priority on the board: above a new Red patient's first assessment.</summary>
    private const double BedsideBand = RedImmediate + 200;

    /// <summary>How close (in tiles) a doctor must be to count as with the patient.</summary>
    private const int BedsideReach = 3;

    /// <summary>With a doctor there but no effective treatment yet, a red patient still improves slowly.</summary>
    private const double NaturalRecoveryShare = 0.5;

    private const double MinimumNaturalRecoveryPerHour = 0.1;

    /// <summary>Once the right treatment is in, a doctor at the bedside turns a red patient round within an hour or two.</summary>
    private const double MinimumTreatedRecoveryPerHour = 0.25;

    /// <summary>Nurse jobs the bedside doctor does themselves for their patient rather than wait for a nurse.</summary>
    private static readonly TaskKind[] BedsideNurseWork = { TaskKind.Observations, TaskKind.GiveMedication, TaskKind.TakeBloods, TaskKind.BedsideTest };

    /// <summary>The doctor staying with this patient, if they are actually with them right now.</summary>
    public StaffMember? DoctorAtBedside(Patient patient) =>
        _staff.FirstOrDefault(s => s.BedsidePatientId == patient.Id && s.IsOnDuty && IsNear(s.Tile, patient.Tile));

    /// <summary>The doctor assigned to stay with this patient, whether or not they are at the bed this moment.</summary>
    public StaffMember? BedsideDoctorFor(Patient patient) => _staff.FirstOrDefault(s => s.BedsidePatientId == patient.Id);

    private static bool IsNear(GridCoord a, GridCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) <= BedsideReach;

    /// <summary>The right treatment works at the condition's own pace (never slower than getting better without it).</summary>
    private static double RedRecoveryPerHour(Patient patient)
    {
        var natural = Math.Max(MinimumNaturalRecoveryPerHour, patient.Condition.RecoveryPerHour * NaturalRecoveryShare);
        return patient.EffectiveTreatmentGiven ? Math.Max(MinimumTreatedRecoveryPerHour, patient.Condition.RecoveryPerHour) : natural;
    }

    /// <summary>Calls a doctor to every red monitor, and stands doctors down once their patient isn't red any more.</summary>
    private void UpdateRedMonitors()
    {
        foreach (var doctor in _staff.Where(s => s.BedsidePatientId is not null).ToList())
        {
            var patient = FindPatient(doctor.BedsidePatientId!.Value);
            if (patient is null || !patient.IsInDepartment || !doctor.IsOnDuty)
            {
                LeaveBedside(doctor);
                continue;
            }

            var news = patient.LiveNews2;
            var here = IsNear(doctor.Tile, patient.Tile);
            if (here && patient.Triage == TriageCategory.Red && !patient.RedStabilised && patient.FirstSeenByDoctorAt is not null && news <= Patient.MonitorClearNews2)
            {
                patient.RedStabilised = true;
                patient.Log(Now, $"Stabilised by {doctor.Name} (NEWS2 {news}).", important: true);
            }

            if (!patient.UpdateMonitorRed())
            {
                patient.Log(Now, $"Monitor no longer red (NEWS2 {news}); {doctor.Name} has left the bedside.");
                LeaveBedside(doctor);
            }
        }

        foreach (var patient in _patients.Values)
        {
            patient.UpdateMonitorRed();
            var calls = _tasks.Values.Where(t => t.PatientId == patient.Id && t.Kind == TaskKind.StayWithPatient).ToList();
            if (!patient.MonitorIsRed)
            {
                foreach (var call in calls)
                {
                    RemoveTask(call);
                }

                continue;
            }

            if (calls.Count == 0 && BedsideDoctorFor(patient) is null)
            {
                AddTask(TaskKind.StayWithPatient, StaffRole.Doctor, patient, 0.5, "Stay with patient", patient.LiveNews2.ToString(System.Globalization.CultureInfo.InvariantCulture));
                patient.Log(Now, $"Monitor red (NEWS2 {patient.LiveNews2}): a doctor is needed at the bedside.", important: true);
            }
        }
    }

    /// <summary>The doctor has reached the patient and will stay until the monitor isn't red.</summary>
    private void ArriveAtBedside(StaffMember doctor, Patient patient)
    {
        if (BedsideDoctorFor(patient) is { } other && other != doctor)
        {
            return;
        }

        doctor.BedsidePatientId = patient.Id;
        doctor.Caseload.Add(patient.Id);
        patient.Log(Now, $"{doctor.Name} is staying at the bedside until the monitor is no longer red (NEWS2 {patient.LiveNews2}).");
    }

    private void LeaveBedside(StaffMember doctor)
    {
        doctor.BedsidePatientId = null;
        if (doctor.CurrentTaskId is null && doctor.IsOnDuty && !doctor.IsOnBreak)
        {
            doctor.Activity = StaffActivity.Idle;
            doctor.StatusText = "Available";
        }
    }

    /// <summary>Work a bedside doctor can do: anything for their own patient a doctor (or that nurse work) would do there.</summary>
    private bool IsBedsideWork(ClinicalTask task, StaffMember doctor, Patient patient) =>
        task.PatientId == patient.Id
        && task.Kind != TaskKind.StayWithPatient
        && (task.Role == doctor.Role || (task.Role == StaffRole.Doctor && doctor.Role == StaffRole.Anaesthetist) || BedsideNurseWork.Contains(task.Kind))
        && ServiceRoomKind(task) is null;

    /// <summary>Nothing to do for their patient: stand at the bed (following them if they are being moved).</summary>
    private void StayAtBedside(StaffMember doctor, Patient patient)
    {
        var spot = BedsideSpot(patient);
        if (!doctor.IsMoving && doctor.Tile != spot)
        {
            WalkTo(doctor, spot);
        }

        doctor.Activity = StaffActivity.Working;
        doctor.StatusText = $"Staying with {patient.FullName} · NEWS2 {patient.LiveNews2}";
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

    /// <summary>The doctor walks to the bed (following the patient if they are moving) and takes over once there.</summary>
    private void ProgressBedsideCall(StaffMember doctor, ClinicalTask task, Patient patient)
    {
        var spot = BedsideSpot(patient);
        if (!IsNear(doctor.Tile, patient.Tile) || (doctor.IsMoving && doctor.Destination != spot))
        {
            if (!doctor.IsMoving || doctor.Destination != spot)
            {
                WalkTo(doctor, spot);
            }

            return;
        }

        _tasks.Remove(task.Id);
        doctor.CurrentTaskId = null;
        doctor.TasksCompleted++;
        ArriveAtBedside(doctor, patient);
        StayAtBedside(doctor, patient);
    }
}
