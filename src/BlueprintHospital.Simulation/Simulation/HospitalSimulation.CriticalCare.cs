using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Critical care (ICU and HDU), anaesthetists, critical care outreach, and moving patients between
/// wards.
///
/// Every patient has a level of care: 0 (a ward), 2 (high dependency) or 3 (intensive care). Very sick
/// patients need ICU when they are admitted, patients go to HDU after a big operation, and ward patients
/// who get worse are found by the outreach nurse (or the ward round) and moved up. As they get better
/// the ICU and HDU ward rounds move them down again: ICU, then HDU, then a ward, then home. An ICU only
/// takes patients while it has an anaesthetist posted to it; without one, ICU patients are transferred.
///
/// Every operation needs an anaesthetist as well as a surgeon: a theatre is only open with both, and the
/// anaesthetist stays in theatre for the whole operation.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Patients this sick need intensive care.</summary>
    public const double IntensiveCareSeverity = 0.7;

    /// <summary>Patients this sick (and not sick enough for ICU) need high dependency care.</summary>
    public const double HighDependencySeverity = 0.55;

    /// <summary>On a ward round, ICU patients stay on ICU until they are better than this.</summary>
    private const double StepDownFromIntensiveCare = 0.6;

    /// <summary>On a ward round, HDU patients stay on HDU until they are better than this.</summary>
    private const double StepDownFromHighDependency = 0.45;

    /// <summary>The outreach nurse is called to ward patients this sick.</summary>
    public const double OutreachTriggerSeverity = 0.6;

    /// <summary>Paid for each day on ICU, on top of the admission.</summary>
    public const int IntensiveCareDayTariff = 1_800;

    /// <summary>Paid for each day on HDU, on top of the admission.</summary>
    public const int HighDependencyDayTariff = 900;

    /// <summary>Patients one ICU nurse looks after.</summary>
    public const int PatientsPerIntensiveCareNurse = 1;

    /// <summary>Patients one HDU nurse looks after.</summary>
    public const int PatientsPerHighDependencyNurse = 2;

    // ---------------------------------------------------------------- units

    /// <summary>What kind of unit a ward is (assessment, day surgery, HDU, ICU or an ordinary ward).</summary>
    public WardUnit UnitOf(CareSpace ward) =>
        ward.IsWard && Database.RoomTemplates.TryGetValue(ward.TemplateId, out var template) ? template.Unit : WardUnit.None;

    public bool IsCriticalCareUnit(CareSpace ward) => UnitOf(ward) is WardUnit.HighDependency or WardUnit.IntensiveCare;

    /// <summary>An ICU takes patients only while an anaesthetist is posted to it.</summary>
    public bool IsIntensiveCareOpen(CareSpace ward) =>
        UnitOf(ward) == WardUnit.IntensiveCare && _staff.Any(s => s.Role == StaffRole.Anaesthetist && s.BaseRoomId == ward.Id);

    private IEnumerable<CareSpace> OpenIntensiveCareUnits => Wards.Where(IsIntensiveCareOpen);

    private IEnumerable<CareSpace> HighDependencyUnits => Wards.Where(w => UnitOf(w) == WardUnit.HighDependency);

    /// <summary>Whether there is anywhere for high dependency patients: an HDU, or an open ICU.</summary>
    private bool HasHighDependencyBeds => HighDependencyUnits.Any() || OpenIntensiveCareUnits.Any();

    /// <summary>The critical care unit the patient is on, if any.</summary>
    private WardUnit? CriticalUnitOfPatient(Patient patient) =>
        patient.Stage == PatientStage.OnWard && patient.CareSpaceId is { } id && FindSpace(id) is { } ward && IsCriticalCareUnit(ward) ? UnitOf(ward) : null;

    /// <summary>The level of care a patient's illness needs now: 3 (ICU), 2 (HDU) or 0 (a ward).</summary>
    private static int LevelFor(double severity, double icu = IntensiveCareSeverity, double hdu = HighDependencySeverity) =>
        severity >= icu ? 3 : severity >= hdu ? 2 : 0;

    private static string LevelName(int level) => level >= 3 ? "intensive care" : level == 2 ? "high dependency care" : "a ward bed";

    // ---------------------------------------------------------------- moving between wards

    /// <summary>
    /// A patient already in a ward bed needs a different ward: up to ICU or HDU, down from them, or out
    /// of an assessment unit after their operation. They keep their bed until one comes free.
    /// </summary>
    private void RequestWardMove(Patient patient, string note)
    {
        patient.WardMoveRequestedAt = Now;
        patient.Log(Now, note, important: true);
        if (patient.DischargeDueAt is null && patient.WorkingDiagnosis is { } d && Database.Conditions.TryGetValue(d.ConditionId, out var condition))
        {
            patient.DischargeDueAt = Now.AddHours(Random.Range(condition.LengthOfStayHours[0], condition.LengthOfStayHours[1]));
        }
    }

    /// <summary>Finds beds for patients waiting to move from one ward to another.</summary>
    private void UpdateWardMoves()
    {
        var moving = _patients.Values
            .Where(p => p.Stage == PatientStage.OnWard && p.IsAlive && p.WardMoveRequestedAt is not null && p.ReservedWardId is null && p.BusyWithTaskId is null)
            .OrderByDescending(p => p.CareLevel)
            .ThenBy(p => p.WardMoveRequestedAt)
            .ToList();

        foreach (var patient in moving)
        {
            var wards = WardsFor(patient);
            var current = patient.CareSpaceId is { } id ? FindSpace(id) : null;
            var here = current is null ? -1 : wards.IndexOf(current);
            if (wards.Count == 0)
            {
                if (patient.CareLevel >= 3)
                {
                    patient.WardMoveRequestedAt = null;
                    ArrangeTransfer(patient, Database.Conditions[patient.WorkingDiagnosis!.ConditionId],
                        "Needs intensive care, but there is no open intensive care unit here, so they are being transferred to a neighbouring hospital's ICU.");
                }
                else
                {
                    patient.WardMoveRequestedAt = null;
                }

                continue;
            }

            // Already on the best ward they could be on: nothing to do.
            if (here == 0)
            {
                patient.WardMoveRequestedAt = null;
                continue;
            }

            var better = here < 0 ? wards : wards.Take(here).ToList();
            var free = better.Select(w => (Ward: w, Index: FreeBedIndex(w))).FirstOrDefault(f => f.Index >= 0);
            if (free.Ward is null)
            {
                if (patient.CareLevel >= 3 && here < 0 && Now - patient.WardMoveRequestedAt > MaxWaitForBed)
                {
                    patient.WardMoveRequestedAt = null;
                    ArrangeTransfer(patient, Database.Conditions[patient.WorkingDiagnosis!.ConditionId],
                        $"No intensive care bed after {MaxWaitForBed.TotalHours:0} hours, so they are being transferred to a neighbouring hospital's ICU.");
                }

                continue;
            }

            free.Ward.Beds[free.Index].OccupantId = patient.Id;
            patient.ReservedWardId = free.Ward.Id;
            patient.ReservedBedIndex = free.Index;
            patient.Log(Now, $"Bed {free.Index + 1} on {free.Ward.Name} allocated.");
            AddTask(TaskKind.EscortToWard, StaffRole.Nurse, patient, 5, $"Take to {free.Ward.Name}");
        }
    }

    private static int FreeBedIndex(CareSpace ward)
    {
        for (var i = 0; i < ward.Beds.Count; i++)
        {
            if (ward.Beds[i].OccupantId is null)
            {
                return i;
            }
        }

        return -1;
    }

    // ---------------------------------------------------------------- critical care ward rounds

    /// <summary>
    /// The ICU or HDU ward round: patients who are better move down a level (HDU, then a ward). A
    /// patient with no ward to go to can go home from the unit once well enough.
    /// </summary>
    private void CriticalCareRound(StaffMember doctor, Patient patient, WardUnit unit)
    {
        var level = LevelFor(patient.Severity, StepDownFromIntensiveCare, StepDownFromHighDependency);
        var unitLevel = unit == WardUnit.IntensiveCare ? 3 : 2;
        patient.DischargeDueAt = Now.AddHours(Random.Range(6.0, 10.0));
        if (level >= unitLevel || patient.WardMoveRequestedAt is not null)
        {
            patient.CareLevel = Math.Max(patient.CareLevel, Math.Min(level, unitLevel));
            patient.Log(Now, patient.WardMoveRequestedAt is not null
                ? $"{(unit == WardUnit.IntensiveCare ? "ICU" : "HDU")} round with {doctor.Name}: still waiting for a bed to move to."
                : $"{(unit == WardUnit.IntensiveCare ? "ICU" : "HDU")} round with {doctor.Name}: still needs {LevelName(unitLevel)}. Review in a few hours.");
            return;
        }

        patient.CareLevel = level;
        if (level == 0 && patient.Severity <= FitForDischargeSeverity && WardsFor(patient).Count == 0)
        {
            patient.Log(Now, $"Round with {doctor.Name}: well enough to go home straight from {(unit == WardUnit.IntensiveCare ? "ICU" : "HDU")}.");
            ArrangeMedicinesToTakeHome(patient);
            return;
        }

        RequestWardMove(patient, $"Round with {doctor.Name}: better, so ready to step down to {LevelName(level)}.");
        patient.DischargeDueAt = Now.AddHours(Random.Range(10.0, 14.0));
    }

    /// <summary>On an ordinary ward round, a patient who has got much worse is referred up to critical care.</summary>
    private bool EscalateOnWardRound(StaffMember doctor, Patient patient)
    {
        var level = LevelFor(patient.Severity);
        if (level <= patient.CareLevel || CriticalUnitOfPatient(patient) is not null || !HasHighDependencyBeds)
        {
            return false;
        }

        patient.CareLevel = level;
        patient.DischargeDueAt = Now.AddHours(12);
        RequestWardMove(patient, $"Ward round with {doctor.Name}: much sicker, referred for {LevelName(level)}.");
        return true;
    }

    // ---------------------------------------------------------------- outreach

    /// <summary>While an outreach nurse works here, ward patients who are getting worse are reviewed.</summary>
    private void ScheduleOutreach()
    {
        if (Now.Second != 0)
        {
            return;
        }

        if (!SpacesOfKind(CareSpaceKind.OutreachOffice).Any(IsStaffed))
        {
            foreach (var task in _tasks.Values.Where(t => t.Kind == TaskKind.OutreachReview && t.AssignedStaffId is null).ToList())
            {
                RemoveTask(task);
            }

            return;
        }

        foreach (var patient in _patients.Values.Where(p => p.Stage == PatientStage.OnWard && p.IsAlive && p.WardMoveRequestedAt is null && p.ReservedWardId is null))
        {
            if (patient.Severity < OutreachTriggerSeverity || CriticalUnitOfPatient(patient) is not null || LevelFor(patient.Severity) <= patient.CareLevel
                || HasTask(patient, TaskKind.OutreachReview))
            {
                continue;
            }

            AddTask(TaskKind.OutreachReview, StaffRole.OutreachNurse, patient, 20, "Outreach review");
            Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName} is getting worse on the ward: the outreach team have been called.", patient.Id, patient.CareSpaceId);
        }
    }

    /// <summary>
    /// The outreach nurse reviews a ward patient who is getting worse. They are moved up to HDU or ICU,
    /// and an experienced nurse may spot that the diagnosis was wrong.
    /// </summary>
    private void CompleteOutreachReview(StaffMember nurse, Patient patient)
    {
        var wrong = patient.WorkingDiagnosis?.ConditionId != patient.Condition.Id;
        if (wrong && Random.Chance(nurse.Skill * 0.5))
        {
            patient.WorkingDiagnosis = new Diagnosis(patient.Condition.Id, 0.9, Now, nurse.Id);
            patient.EffectiveTreatmentGiven = true;
            Stats.WardDiagnosesCorrected++;
            patient.Log(Now, $"Outreach review by {nurse.Name}: this looks like {patient.Condition.Name.ToLowerInvariant()}. The right treatment has been started.", important: true);
        }

        var level = Math.Max(2, LevelFor(patient.Severity));
        if (!HasHighDependencyBeds)
        {
            patient.Log(Now, $"Outreach review by {nurse.Name}: needs {LevelName(level)}, but there is no critical care unit here. Closer observations on the ward.", important: true);
            if (level >= 3)
            {
                patient.CareLevel = 3;
                RequestWardMove(patient, "Referred to a neighbouring hospital's intensive care unit.");
            }

            return;
        }

        patient.CareLevel = level;
        RequestWardMove(patient, $"Outreach review by {nurse.Name}: needs {LevelName(level)}.");
    }

    // ---------------------------------------------------------------- anaesthetists in theatre

    /// <summary>An anaesthetist in this theatre who is free to give an anaesthetic now.</summary>
    private StaffMember? FreeAnaesthetist(CareSpace theatre) =>
        _staff.FirstOrDefault(s => s.Role == StaffRole.Anaesthetist && s.BaseRoomId == theatre.Id && s.IsOnDuty && !s.IsOnBreak
                                   && s.CurrentTaskId is null && s.AssistingTaskId is null && s.PagedUntil is null);

    /// <summary>The surgeon has started an operation: an anaesthetist joins them for all of it.</summary>
    private void JoinAnaesthetist(ClinicalTask task, CareSpace theatre, Patient patient)
    {
        if (FreeAnaesthetist(theatre) is not { } anaesthetist)
        {
            return;
        }

        anaesthetist.AssistingTaskId = task.Id;
        anaesthetist.Activity = StaffActivity.Working;
        anaesthetist.StatusText = $"Anaesthetic: {patient.FullName}";
        WalkTo(anaesthetist, AnaesthetistSpot(theatre));
        patient.Log(Now, $"Anaesthetic given by {anaesthetist.Name}.");
    }

    /// <summary>At the head of the operating table, beside the surgeon.</summary>
    private GridCoord AnaesthetistSpot(CareSpace theatre)
    {
        foreach (var (dx, dy) in new[] { (0, -1), (-1, 0), (1, 0), (0, 1) })
        {
            var t = new GridCoord(theatre.PatientSpot.X + dx, theatre.PatientSpot.Y + dy);
            if (t != theatre.StaffSpot && theatre.Rect.Contains(t) && Nav.IsWalkable(t))
            {
                return t;
            }
        }

        return theatre.StaffSpot;
    }

    /// <summary>
    /// An anaesthetist giving an anaesthetic stays in theatre until the operation is over (even at the
    /// end of their shift). True while they are busy with it.
    /// </summary>
    private bool UpdateAssisting(StaffMember member)
    {
        if (member.AssistingTaskId is not { } taskId)
        {
            return false;
        }

        if (_tasks.ContainsKey(taskId))
        {
            member.Activity = StaffActivity.Working;
            return true;
        }

        member.AssistingTaskId = null;
        member.Activity = StaffActivity.Idle;
        member.StatusText = "Available";
        member.TasksCompleted++;
        return false;
    }

    /// <summary>An anaesthetist posted to an ICU does the doctors' work there.</summary>
    private bool AnaesthetistCanCover(ClinicalTask task, StaffMember member) =>
        member.Role == StaffRole.Anaesthetist && task.Role == StaffRole.Doctor && member.BaseRoomId is { } id && TeamFor(task) == id;

    // ---------------------------------------------------------------- care on the units

    /// <summary>Organ support slows a patient's decline: ICU most, HDU somewhat. Time on a unit is paid for.</summary>
    private double CriticalCareFactor(Patient patient, double hours)
    {
        switch (CriticalUnitOfPatient(patient))
        {
            case WardUnit.IntensiveCare:
                patient.IntensiveCareHours += hours;
                return 0.3;
            case WardUnit.HighDependency:
                patient.HighDependencyHours += hours;
                return 0.6;
            default:
                return 1.0;
        }
    }

    /// <summary>The extra paid for days on ICU and HDU.</summary>
    private static long CriticalCarePayment(Patient patient) =>
        (long)Math.Round((patient.IntensiveCareHours / 24.0 * IntensiveCareDayTariff) + (patient.HighDependencyHours / 24.0 * HighDependencyDayTariff));
}
