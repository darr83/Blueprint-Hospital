using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>A patient coming round after sedation for endoscopy, in a recovery bay or (with no bay free) on the endoscopy trolley.</summary>
public sealed record RecoveryState(int PatientId, DateTime Until, string RoomId);

/// <summary>
/// Physiotherapy on the wards, recovery after endoscopy and same-day emergency care (ambulatory care).
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How long a patient takes to come round after sedation.</summary>
    public static readonly TimeSpan SedationRecovery = TimeSpan.FromMinutes(30);

    /// <summary>Most physiotherapy sessions a ward patient has in one stay.</summary>
    private const int MaxPhysioSessions = 4;

    /// <summary>
    /// Same-day emergency care is for patients no sicker than this. They go home from the ambulatory unit
    /// at this level too, finishing treatment at home with a review the next day.
    /// </summary>
    private const double SameDayFitSeverity = 0.5;

    private readonly Dictionary<int, RecoveryState> _recovering = new();

    public IReadOnlyCollection<RecoveryState> Recovering => _recovering.Values;

    // ---------------------------------------------------------------- recovery after endoscopy

    /// <summary>After endoscopy the patient needs half an hour to come round: in a free recovery bay, or else on the trolley, which keeps the room out of use.</summary>
    private void StartRecovery(Patient patient, string endoscopyRoomId)
    {
        var bay = SpacesOfKind(CareSpaceKind.EndoscopyRecovery).FirstOrDefault(IsVacant);
        if (bay is not null)
        {
            bay.OccupantId = patient.Id;
            WalkTo(patient, bay.PatientSpot);
            _recovering[patient.Id] = new RecoveryState(patient.Id, Now + SedationRecovery, bay.Id);
            patient.Log(Now, $"Taken to {bay.Name} to come round from sedation.");
            return;
        }

        _recovering[patient.Id] = new RecoveryState(patient.Id, Now + SedationRecovery, endoscopyRoomId);
        patient.Log(Now, "Coming round from sedation on the endoscopy trolley: there is no free recovery bay, so the room can't be used until they are ready.");
    }

    private void UpdateRecovery()
    {
        if (_recovering.Count == 0)
        {
            return;
        }

        foreach (var state in _recovering.Values.ToList())
        {
            var present = _patients.TryGetValue(state.PatientId, out var patient) && patient.IsInDepartment && patient.IsAlive;
            if (present && (Now < state.Until || patient!.IsMoving))
            {
                continue;
            }

            _recovering.Remove(state.PatientId);
            if (FindSpace(state.RoomId) is { Kind: CareSpaceKind.EndoscopyRecovery } bay && bay.OccupantId == state.PatientId)
            {
                bay.OccupantId = null;
            }

            if (present)
            {
                patient!.Log(Now, "Awake and recovered from sedation.");
                ReturnFromServiceRoom(patient);
            }
        }
    }

    // ---------------------------------------------------------------- physiotherapy

    /// <summary>Ward patients who need physiotherapy get a session about once a day while a physiotherapist works here.</summary>
    private void SchedulePhysio()
    {
        if (Now.Second != 0 || !SpacesOfKind(CareSpaceKind.PhysioGym).Any(IsStaffed))
        {
            return;
        }

        foreach (var patient in _patients.Values.Where(p => p.Stage == PatientStage.OnWard && p.IsAlive && p.PhysioSessions < MaxPhysioSessions))
        {
            if (patient.WorkingDiagnosis is not { } diagnosis || !Database.Conditions.TryGetValue(diagnosis.ConditionId, out var condition) || !condition.NeedsPhysio)
            {
                continue;
            }

            var since = patient.LastPhysioAt ?? patient.AdmittedAt;
            var due = patient.LastPhysioAt is null ? TimeSpan.FromHours(6) : TimeSpan.FromHours(20);
            if (since is null || Now - since < due || patient.DischargeDueAt is not { } discharge || discharge - Now < TimeSpan.FromHours(6) || HasTask(patient, TaskKind.Physio))
            {
                continue;
            }

            AddTask(TaskKind.Physio, StaffRole.Physiotherapist, patient, 30, "Physiotherapy");
        }
    }

    private void CompletePhysio(StaffMember physio, Patient patient)
    {
        patient.PhysioSessions++;
        patient.LastPhysioAt = Now;
        var sooner = TimeSpan.FromHours(10 + (6 * physio.Skill));
        if (patient.DischargeDueAt is { } due)
        {
            var earliest = Now.AddHours(2);
            patient.DischargeDueAt = due - sooner < earliest ? earliest : due - sooner;
        }

        var progress = patient.PhysioSessions switch
        {
            1 => "sitting out of bed and standing with help",
            2 => "walking a few steps with a frame",
            3 => "walking on the ward with a frame",
            _ => "managing stairs and ready for home",
        };
        patient.Log(Now, $"Physiotherapy with {physio.Name}: {progress}. Should be home sooner.");
    }

    // ---------------------------------------------------------------- same-day emergency care

    /// <summary>Whether a ward is an ambulatory care (same-day) unit.</summary>
    public bool IsSameDayUnit(CareSpace ward) =>
        Database.RoomTemplates.TryGetValue(ward.TemplateId, out var template) && template.SameDay;

    /// <summary>
    /// A patient who would be admitted but is well enough is treated in the ambulatory care unit and goes
    /// home the same day, if it has a free bed. False if not (they are admitted as usual).
    /// </summary>
    private bool StartSameDayCare(Patient patient, ConditionDefinition diagnosed)
    {
        if (patient.Severity > SameDayFitSeverity)
        {
            return false;
        }

        patient.SameDay = true;
        if (FreeWardBed(patient) is not { } free)
        {
            patient.SameDay = false;
            return false;
        }

        patient.Stage = PatientStage.AwaitingBed;
        patient.BedRequestedAt = Now;
        free.Bed.OccupantId = patient.Id;
        patient.ReservedWardId = free.Ward.Id;
        patient.ReservedBedIndex = free.Index;
        patient.Log(Now, $"Well enough for same-day emergency care instead of admission: {diagnosed.AmbulatoryHours![0]:0} to {diagnosed.AmbulatoryHours[1]:0} hours on {free.Ward.Name}, then home.", important: true);
        AddTask(TaskKind.EscortToWard, StaffRole.Nurse, patient, 5, $"Take to {free.Ward.Name}");
        return true;
    }
}
