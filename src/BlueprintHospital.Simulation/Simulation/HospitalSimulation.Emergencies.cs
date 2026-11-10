using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>How often patients already in a bed have a sudden emergency (chosen at New Game).</summary>
public enum WardEmergencyFrequency
{
    /// <summary>Never.</summary>
    Off,

    /// <summary>About a third as often as real life.</summary>
    Rare,

    /// <summary>About as often as a real hospital: roughly one a day with 30 inpatients.</summary>
    Realistic,
}

/// <summary>An emergency has started for a patient in bed (for the tannoy, sounds and screen).</summary>
public sealed record WardEmergencyStarted(int PatientId, string EmergencyId, string Name) : Core.IGameEvent;

/// <summary>
/// Sudden emergencies for patients already in a hospital bed: a heart attack, a pulmonary embolism, sepsis, a
/// fall, a bleed after an operation, a stroke, a low blood sugar, an allergic reaction, delirium, or the heart
/// simply stopping. They come from Data/Emergencies. The ward team are called: a doctor (or nurse) reviews at
/// the bedside, then the treatment goes in and they settle. Until it does, they keep getting worse and can
/// go into cardiac arrest. Some rooms (a cath lab, a CT scanner, a theatre) make the treatment better.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>The Rare setting, as a share of Realistic.</summary>
    private const double RareShare = 0.3;

    /// <summary>Nobody has an emergency in their first hour in bed (they have only just been seen).</summary>
    private static readonly TimeSpan SettlingIn = TimeSpan.FromHours(1);

    /// <summary>A second emergency doesn't follow straight on from the first.</summary>
    private static readonly TimeSpan BetweenEmergencies = TimeSpan.FromHours(12);

    private int _emergencyCountdown = 60;

    public WardEmergencyFrequency WardEmergencies { get; set; }

    /// <summary>Patients with an emergency going on right now.</summary>
    public IEnumerable<Patient> PatientsWithEmergencies => _patients.Values.Where(p => p.Emergency is not null && p.IsAlive);

    /// <summary>The chance per hour of each emergency for this patient, with their risks (before the setting).</summary>
    public double EmergencyChancePerHour(Patient patient, InpatientEmergencyDefinition emergency)
    {
        if (emergency.Requires.Any(r => !HasRisk(patient, r)))
        {
            return 0;
        }

        var chance = emergency.PerDay / 24.0;
        foreach (var factor in emergency.Risks.Where(f => HasRisk(patient, f.Risk)))
        {
            chance *= factor.Times;
        }

        return chance;
    }

    private bool HasRisk(Patient patient, EmergencyRisk risk) => risk switch
    {
        EmergencyRisk.Older => patient.Age >= 65,
        EmergencyRisk.Elderly => patient.Age >= 80,
        EmergencyRisk.Child => patient.Age < 16,
        EmergencyRisk.HadSurgery => patient.HadSurgery,
        EmergencyRisk.Diabetes => patient.PastHistory.Any(h => h.Contains("diabetes", StringComparison.OrdinalIgnoreCase)),
        EmergencyRisk.HeartDisease => patient.Condition.Department == "cardiology"
            || patient.PastHistory.Any(h => h.Contains("heart", StringComparison.OrdinalIgnoreCase) || h.Contains("atrial", StringComparison.OrdinalIgnoreCase)),
        EmergencyRisk.Neurology => patient.Condition.Department == "neurology",
        EmergencyRisk.Maternity => patient.Maternity,
        EmergencyRisk.NewMedicines => patient.TreatmentPlan.Any(t => t.Done && t.Step.Medication is not null),
        EmergencyRisk.FirstTwoDays => patient.AdmittedAt is { } admitted && Now - admitted < TimeSpan.FromDays(2),
        EmergencyRisk.CriticalCare => patient.CareSpaceId is { } id && FindSpace(id) is { } ward && IsCriticalCareUnit(ward),
        EmergencyRisk.KnownAllergy => patient.Allergies.Count > 0,
        _ => false,
    };

    /// <summary>Who can have one: alive, settled in a ward bed, and not already in the middle of something.</summary>
    private bool CanHaveEmergency(Patient patient) =>
        patient.IsAlive && patient.IsInDepartment && patient.Stage == PatientStage.OnWard && !patient.InArrest
        && patient.Emergency is null && BedOf(patient) is not null && !patient.IsMoving && patient.PendingMove is null
        && patient.AdmittedAt is { } admitted && Now - admitted >= SettlingIn
        && (patient.EmergencyStartedAt is not { } last || Now - last >= BetweenEmergencies)
        && (patient.StabilisedUntil is not { } until || Now >= until);

    /// <summary>Once a game minute: rolls for new emergencies, and moves on the ones going on.</summary>
    private void UpdateWardEmergencies()
    {
        if (--_emergencyCountdown > 0)
        {
            return;
        }

        _emergencyCountdown = 60;
        foreach (var patient in PatientsWithEmergencies.ToList())
        {
            CallEmergencyTeam(patient);
        }

        if (WardEmergencies == WardEmergencyFrequency.Off || Database.Emergencies.Count == 0)
        {
            return;
        }

        var scale = WardEmergencies == WardEmergencyFrequency.Rare ? RareShare : 1.0;
        foreach (var patient in _patients.Values.Where(CanHaveEmergency).ToList())
        {
            foreach (var emergency in Database.Emergencies.Values)
            {
                if (Random.Chance(EmergencyChancePerHour(patient, emergency) * scale / 60.0))
                {
                    StartWardEmergency(patient, emergency);
                    break;
                }
            }
        }
    }

    /// <summary>Starts an emergency for a patient in bed (also used by tests and the cheat console).</summary>
    public void StartWardEmergency(Patient patient, InpatientEmergencyDefinition emergency)
    {
        patient.EmergencyStartedAt = Now;
        patient.EmergencyCount++;
        Stats.WardEmergencies++;
        var where = patient.CareSpaceId is { } id && FindSpace(id) is { } ward ? ward.Name : "the ward";
        var bed = patient.BedIndex is { } index ? $", bed {index + 1}" : string.Empty;

        // Some emergencies stop the heart straight away: the crash team take it from here.
        if (Random.Chance(emergency.ArrestChance))
        {
            patient.Log(Now, $"{emergency.Name}: {emergency.Signs}", important: true);
            Events.Publish(new WardEmergencyStarted(patient.Id, emergency.Id, emergency.Name));
            StartArrest(patient, emergency.Name.ToLowerInvariant());
            return;
        }

        patient.Emergency = emergency;
        patient.EmergencyReviewed = false;
        patient.EmergencyTreated = false;
        patient.Severity = Math.Max(patient.Severity, emergency.Severity);
        patient.EmergencyPeak = patient.Severity;
        patient.RedReviewed = false;
        patient.Log(Now, $"Emergency: {emergency.Name.ToLowerInvariant()}. {emergency.Signs} The ward team have been called.", important: true);
        Notifications.Add(Now, NotificationLevel.Warning, $"Emergency on {where}{bed}: {patient.FullName}, {emergency.Name.ToLowerInvariant()}.", patient.Id, patient.CareSpaceId, patient.Triage);
        Events.Publish(new WardEmergencyStarted(patient.Id, emergency.Id, emergency.Name));

        // Nobody goes home in the middle of an emergency.
        foreach (var task in _tasks.Values.Where(t => t.PatientId == patient.Id && t.Kind == TaskKind.WardDischarge && t.AssignedStaffId is null).ToList())
        {
            RemoveTask(task);
        }

        if (patient.DischargeDueAt is { } due && due < Now.AddDays(1))
        {
            patient.DischargeDueAt = Now.AddDays(1);
        }

        CallEmergencyTeam(patient);
    }

    /// <summary>Puts the review, then the treatment, on the task board (once each).</summary>
    private void CallEmergencyTeam(Patient patient)
    {
        if (patient.Emergency is not { } emergency || patient.InArrest || !patient.IsInDepartment)
        {
            return;
        }

        if (!patient.EmergencyReviewed)
        {
            if (!HasTask(patient, TaskKind.EmergencyReview))
            {
                AddTask(TaskKind.EmergencyReview, emergency.Review.Role, patient, emergency.Review.Minutes, emergency.Review.Label, emergency.Id);
            }
        }
        else if (!patient.EmergencyTreated && !HasTask(patient, TaskKind.EmergencyTreatment))
        {
            AddTask(TaskKind.EmergencyTreatment, emergency.Treatment.Role, patient, emergency.Treatment.Minutes, emergency.Treatment.Label, emergency.Id);
        }
    }

    private void CompleteEmergencyReview(StaffMember member, Patient patient)
    {
        if (patient.Emergency is not { } emergency || patient.EmergencyReviewed)
        {
            return;
        }

        patient.EmergencyReviewed = true;
        if (member.Role == StaffRole.Doctor && patient.MonitorIsRed && !patient.RedReviewed)
        {
            MarkRedReviewed(member, patient);
        }

        var who = member.Role == StaffRole.Doctor ? member.Name : $"{member.Name} ({member.Role.ToString().ToLowerInvariant()})";
        patient.Log(Now, $"{emergency.Review.Label} by {who}.");
        foreach (var line in emergency.Review.Log)
        {
            patient.Log(Now, line);
        }

        CallEmergencyTeam(patient);
    }

    private void CompleteEmergencyTreatment(StaffMember member, Patient patient)
    {
        if (patient.Emergency is not { } emergency || patient.EmergencyTreated)
        {
            return;
        }

        patient.EmergencyTreated = true;
        patient.EmergencyPeak = Math.Max(patient.Severity, emergency.SettledSeverity + 0.05);
        patient.Log(Now, $"{emergency.Treatment.Label}: {member.Name}.", important: true);
        foreach (var line in emergency.Treatment.Log)
        {
            patient.Log(Now, line);
        }

        if (emergency.BetterWith is { } kind)
        {
            var has = SpacesOfKind(kind).Any();
            var note = has ? emergency.BetterLog : emergency.WithoutLog;
            if (!string.IsNullOrEmpty(note))
            {
                patient.Log(Now, note);
            }
        }

        if (emergency.CareLevel > patient.CareLevel)
        {
            patient.CareLevel = emergency.CareLevel;
            var needs = emergency.CareLevel >= 3 ? "intensive care" : "high dependency care";
            var unit = CriticalUnitOfPatient(patient);
            var enough = unit == WardUnit.IntensiveCare || (unit == WardUnit.HighDependency && emergency.CareLevel <= 2);
            if (!enough && patient.Stage == PatientStage.OnWard)
            {
                patient.WardMoveRequestedAt = null;
                RequestWardMove(patient, $"After the {emergency.Name.ToLowerInvariant()}: needs {needs}.");
            }
        }

        Stats.WardEmergenciesTreated++;
        if (patient.EmergencyStartedAt is { } started)
        {
            Stats.RecordTimeToEmergencyTreatment((Now - started).TotalSeconds);
        }
    }

    /// <summary>
    /// The physiology of a patient with an emergency: worse until treated (possibly to cardiac arrest), then
    /// settling at the emergency's own pace. True when this has handled them this tick.
    /// </summary>
    private bool UpdateEmergencyPhysiology(Patient patient, double hours, bool stabilised)
    {
        if (patient.Emergency is not { } emergency)
        {
            return false;
        }

        // Better still with the room that helps (a cath lab, a CT scanner, a theatre).
        var pace = emergency.BetterWith is { } kind && SpacesOfKind(kind).Any() ? 1.5 : 1.0;
        if (!patient.EmergencyTreated)
        {
            if (!stabilised)
            {
                var ceiling = emergency.CanBeFatal ? 1.0 : 0.92;
                patient.Severity = Math.Min(ceiling, patient.Severity + (emergency.WorsenPerHour * hours));
                patient.EmergencyPeak = Math.Max(patient.EmergencyPeak, patient.Severity);
            }

            if (emergency.CanBeFatal && patient.Severity >= 1.0)
            {
                EndEmergency(patient, settled: false);
                if (CanBeResuscitated(patient))
                {
                    patient.Log(Now, $"The {emergency.Name.ToLowerInvariant()} wasn't treated in time.", important: true);
                    StartArrest(patient, emergency.Name.ToLowerInvariant());
                }
                else
                {
                    PatientDied(patient);
                }
            }

            return true;
        }

        patient.Severity = Math.Max(0, patient.Severity - (emergency.RecoveryPerHour * pace * hours));
        if (patient.Severity <= emergency.SettledSeverity)
        {
            EndEmergency(patient, settled: true);
        }

        return true;
    }

    private void EndEmergency(Patient patient, bool settled)
    {
        if (patient.Emergency is not { } emergency)
        {
            return;
        }

        if (settled)
        {
            patient.Log(Now, $"Settled after the {emergency.Name.ToLowerInvariant()}.");
            Stats.WardEmergenciesSettled++;
        }

        patient.Emergency = null;
        patient.EmergencyReviewed = false;
        patient.EmergencyTreated = false;
        foreach (var task in _tasks.Values.Where(t => t.PatientId == patient.Id && t.Kind is TaskKind.EmergencyReview or TaskKind.EmergencyTreatment).ToList())
        {
            RemoveTask(task);
        }
    }
}
