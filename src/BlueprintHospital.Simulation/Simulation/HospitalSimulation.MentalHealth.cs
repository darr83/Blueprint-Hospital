using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Mental Health, stage 1: the liaison team in A&amp;E. A&amp;E treats the body first; once a patient
/// whose problem is their mental health is medically fit, the treatment plan's "Mental health
/// assessment" step sends them to the mental health assessment room, where a mental health nurse
/// (or a psychiatrist) sees them. The target is to be seen within an hour of being referred.
///
/// With no staffed assessment room an A&amp;E doctor does a basic assessment instead. Until the hospital
/// has a mental health ward, patients who need admitting are transferred to a mental health hospital.
/// </summary>
public sealed partial class HospitalSimulation
{
    public const string MentalHealthDepartmentId = "mental_health";

    /// <summary>The liaison team aim to see a referred patient within this long.</summary>
    public static readonly TimeSpan LiaisonTarget = TimeSpan.FromHours(1);

    /// <summary>Staff who belong to the mental health team.</summary>
    public static bool IsMentalHealthRole(StaffRole role) => role is StaffRole.MentalHealthNurse or StaffRole.Psychiatrist;

    /// <summary>A psychiatrist can do any mental health nurse's assessment.</summary>
    private static bool PsychiatristCanCover(ClinicalTask task, StaffMember member) =>
        member.Role == StaffRole.Psychiatrist && task.Role == StaffRole.MentalHealthNurse;

    /// <summary>Whether a patient is under the mental health team (their working diagnosis is a mental health condition).</summary>
    public bool IsMentalHealthPatient(Patient patient) =>
        patient.WorkingDiagnosis is { } diagnosis && Database.Conditions.TryGetValue(diagnosis.ConditionId, out var condition)
        && condition.Department == MentalHealthDepartmentId;

    /// <summary>Patients the liaison team saw in an assessment room.</summary>
    public int LiaisonAssessments => (int)HistoryStore.Total(StatsPeriod.AllTime, Now, StatKeys.LiaisonSeen);

    /// <summary>Of those, the ones seen within an hour of being referred.</summary>
    public int LiaisonWithinTarget => (int)HistoryStore.Total(StatsPeriod.AllTime, Now, StatKeys.LiaisonWithinHour);

    /// <summary>Counts a liaison assessment done in the assessment room, and whether it met the one hour target.</summary>
    private void RecordLiaisonAssessment(Patient patient, ClinicalTask task)
    {
        var waited = Now - task.CreatedAt;
        Tally(StatKeys.LiaisonSeen);
        if (waited <= LiaisonTarget)
        {
            Tally(StatKeys.LiaisonWithinHour);
        }

        Tally(StatKeys.LiaisonMinutes, Math.Max(0, waited.TotalMinutes));
        patient.Log(Now, $"Seen by the mental health liaison team {(waited <= LiaisonTarget ? "within the hour" : $"after {(int)waited.TotalMinutes} minutes")}.");
    }

    /// <summary>Why a mental health patient who needs admitting can't be.</summary>
    private string NoWardReasonForMentalHealth() =>
        IsUnlocked(MentalHealthDepartmentId)
            ? "There is no mental health ward here yet, so they are being transferred to a mental health hospital."
            : "This hospital has no mental health department, so they are being transferred to a mental health hospital.";
}
