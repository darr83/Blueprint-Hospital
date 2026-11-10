using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>Something about a patient that makes an emergency more (or less) likely.</summary>
public enum EmergencyRisk
{
    /// <summary>Aged 65 or over ("older").</summary>
    Older,

    /// <summary>Aged 80 or over ("elderly").</summary>
    Elderly,

    /// <summary>Under 16.</summary>
    Child,

    /// <summary>Has had an operation this stay.</summary>
    HadSurgery,

    /// <summary>Has diabetes in their past history.</summary>
    Diabetes,

    /// <summary>Heart disease in their past history, or in with a heart problem.</summary>
    HeartDisease,

    /// <summary>In with a stroke, seizure or other brain problem.</summary>
    Neurology,

    /// <summary>On a maternity ward.</summary>
    Maternity,

    /// <summary>Has been given medicines this stay.</summary>
    NewMedicines,

    /// <summary>Within the first two days of their admission.</summary>
    FirstTwoDays,

    /// <summary>In intensive care or high dependency.</summary>
    CriticalCare,

    /// <summary>Has a drug allergy listed.</summary>
    KnownAllergy,
}

/// <summary>How much more likely an emergency is for a patient with this risk.</summary>
public sealed class EmergencyRiskFactor
{
    public EmergencyRisk Risk { get; init; }

    /// <summary>Multiplies the chance (2 = twice as likely, 0.5 = half as likely).</summary>
    public double Times { get; init; } = 1;
}

/// <summary>How the emergency shows on the monitor, at its worst (it fades as they settle).</summary>
public sealed class EmergencyObservations
{
    public double HeartRate { get; init; }

    public double SystolicBp { get; init; }

    public double RespiratoryRate { get; init; }

    public double OxygenSaturation { get; init; }

    public double Temperature { get; init; }

    /// <summary>Newly confused (counts towards NEWS2).</summary>
    public bool Confused { get; init; }
}

/// <summary>A piece of bedside work in an emergency: the review, then the treatment.</summary>
public sealed class EmergencyStep
{
    /// <summary>Who does it: a doctor or a nurse.</summary>
    public StaffRole Role { get; init; } = StaffRole.Doctor;

    public int Minutes { get; init; } = 10;

    /// <summary>What it says on the task board.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Lines written in the patient's record when it is done (tests, findings, what was given).</summary>
    public IReadOnlyList<string> Log { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A sudden emergency for a patient already in a hospital bed (a heart attack, a fall, a bleed after an
/// operation...). Loaded from Data/Emergencies. The ward team are called: a doctor reviews at the bedside,
/// then the treatment is given; untreated, the patient keeps getting worse and may arrest.
/// </summary>
public sealed class InpatientEmergencyDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>What the staff see first, written in the record when it starts.</summary>
    public string Signs { get; init; } = string.Empty;

    /// <summary>How often it happens, per patient per day in bed, before risks (Realistic setting).</summary>
    public double PerDay { get; init; }

    /// <summary>Patients only have it if they have all of these (hypoglycaemia needs diabetes).</summary>
    public IReadOnlyList<EmergencyRisk> Requires { get; init; } = Array.Empty<EmergencyRisk>();

    public IReadOnlyList<EmergencyRiskFactor> Risks { get; init; } = Array.Empty<EmergencyRiskFactor>();

    /// <summary>How unwell they become straight away (0 to 1; 1 is cardiac arrest).</summary>
    public double Severity { get; init; } = 0.8;

    /// <summary>How fast they get worse until the treatment is in.</summary>
    public double WorsenPerHour { get; init; } = 0.1;

    /// <summary>Left untreated, they can go on to arrest.</summary>
    public bool CanBeFatal { get; init; } = true;

    /// <summary>The chance the heart stops straight away.</summary>
    public double ArrestChance { get; init; }

    public EmergencyObservations Observations { get; init; } = new();

    /// <summary>The patient is on the floor beside their bed until they are helped up.</summary>
    public bool Fall { get; init; }

    public EmergencyStep Review { get; init; } = new();

    public EmergencyStep Treatment { get; init; } = new();

    /// <summary>How fast they settle once treated.</summary>
    public double RecoveryPerHour { get; init; } = 0.3;

    /// <summary>The emergency is over once they are back down to this.</summary>
    public double SettledSeverity { get; init; } = 0.45;

    /// <summary>After treatment they need intensive care or high dependency (level 2 or 3), if the hospital has it.</summary>
    public int CareLevel { get; init; }

    /// <summary>A room that makes the treatment better (a cath lab for a heart attack, a CT scanner for a stroke).</summary>
    public CareSpaceKind? BetterWith { get; init; }

    /// <summary>Written in the record when the hospital has that room.</summary>
    public string BetterLog { get; init; } = string.Empty;

    /// <summary>Written in the record when it doesn't.</summary>
    public string WithoutLog { get; init; } = string.Empty;

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        context.RequireText("signs", Signs);
        context.RequireRange("per_day", PerDay, 0, 1);
        context.RequireRange("severity", Severity, 0.3, 1);
        context.RequireRange("worsen_per_hour", WorsenPerHour, 0, 5);
        context.RequireRange("arrest_chance", ArrestChance, 0, 1);
        context.RequireRange("recovery_per_hour", RecoveryPerHour, 0.01, 5);
        context.RequireRange("settled_severity", SettledSeverity, 0, 0.9);
        context.RequireRange("care_level", CareLevel, 0, 3);
        foreach (var (step, field) in new[] { (Review, "review"), (Treatment, "treatment") })
        {
            context.RequireText($"{field}.label", step.Label);
            context.RequireRange($"{field}.minutes", step.Minutes, 1, 120);
            if (step.Role is not (StaffRole.Doctor or StaffRole.Nurse))
            {
                context.Error($"{field}.role", "must be doctor or nurse.");
            }
        }

        foreach (var risk in Risks)
        {
            context.RequireRange("risks.times", risk.Times, 0, 50);
        }
    }
}
