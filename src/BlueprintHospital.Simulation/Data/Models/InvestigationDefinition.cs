using BlueprintHospital.Simulation.Hospital;

namespace BlueprintHospital.Simulation.Data.Models;

public enum InvestigationCategory
{
    /// <summary>Done at the bedside and read straight away (ECG, urine test).</summary>
    Bedside,

    /// <summary>A sample is taken at the bedside and sent to the laboratory.</summary>
    Blood,

    /// <summary>Done by a radiographer in an X-ray or CT room; a radiologist reports it after the turnaround time.</summary>
    Imaging,
}

public enum SexRestriction
{
    Any,
    Female,
    Male,
}

/// <summary>A possible result: the text shown to the player and an optional numeric value range.</summary>
public sealed class ResultTemplate
{
    /// <summary>[low, high] for numeric tests. The value is drawn from this range.</summary>
    public double[]? ValueRange { get; init; }

    /// <summary>Report wording. One is chosen at random; "{value}" is replaced with the value.</summary>
    public IReadOnlyList<string> Summaries { get; init; } = Array.Empty<string>();
}

/// <summary>What the test shows when the patient has a particular condition.</summary>
public sealed class AbnormalResult
{
    public string Condition { get; init; } = string.Empty;

    /// <summary>Chance the test is abnormal in a patient with this condition (sensitivity).</summary>
    public double Probability { get; init; }

    public double[]? ValueRange { get; init; }

    public IReadOnlyList<string> Summaries { get; init; } = Array.Empty<string>();

    public ResultTemplate AsTemplate() => new() { ValueRange = ValueRange, Summaries = Summaries };
}

/// <summary>A diagnostic test. Loaded from Data/Investigations.</summary>
public sealed class InvestigationDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public InvestigationCategory Category { get; init; }

    public ClinicalRole PerformedBy { get; init; } = ClinicalRole.Nurse;

    /// <summary>Time at the bedside to do the test or take the sample.</summary>
    public int ProcedureMinutes { get; init; }

    /// <summary>Time from sample to result (0 for bedside tests).</summary>
    public int TurnaroundMinutes { get; init; }

    public int Cost { get; init; }

    /// <summary>For imaging: the room it needs (x_ray or ct). Without one on site the test can't be done.</summary>
    public CareSpaceKind? Room { get; init; }

    public SexRestriction AppliesToSex { get; init; } = SexRestriction.Any;

    public string? Unit { get; init; }

    public ResultTemplate Normal { get; init; } = new();

    /// <summary>Chance the test is abnormal in a patient without any listed condition.</summary>
    public double FalsePositiveRate { get; init; }

    public ResultTemplate? FalsePositive { get; init; }

    public IReadOnlyList<AbnormalResult> Abnormal { get; init; } = Array.Empty<AbnormalResult>();

    /// <summary>If true, an abnormal result immediately makes the patient Red (e.g. ST elevation on an ECG).</summary>
    public bool EscalateToRedIfAbnormal { get; init; }

    /// <summary>Probability of an abnormal result given the patient has <paramref name="conditionId"/>.</summary>
    public double ProbabilityAbnormal(string conditionId) =>
        Abnormal.FirstOrDefault(a => a.Condition == conditionId)?.Probability ?? FalsePositiveRate;

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        context.RequireRange("procedure_minutes", ProcedureMinutes, 1, 240);
        context.RequireRange("turnaround_minutes", TurnaroundMinutes, 0, 24 * 60);
        context.RequireRange("cost", Cost, 0, 100_000);
        context.RequireRange("false_positive_rate", FalsePositiveRate, 0, 1);
        CheckTemplate(context, "normal", Normal);

        if ((Category == InvestigationCategory.Imaging) != Room is CareSpaceKind.XRay or CareSpaceKind.CT)
        {
            context.Error("room", "imaging tests, and only imaging tests, name their room: x_ray or ct.");
        }

        if (FalsePositiveRate > 0)
        {
            if (FalsePositive is null)
            {
                context.Error("false_positive", "is required when false_positive_rate is above 0.");
            }
            else
            {
                CheckTemplate(context, "false_positive", FalsePositive);
            }
        }

        for (var i = 0; i < Abnormal.Count; i++)
        {
            context.RequireId($"abnormal[{i}].condition", Abnormal[i].Condition);
            context.RequireRange($"abnormal[{i}].probability", Abnormal[i].Probability, 0, 1);
            CheckTemplate(context, $"abnormal[{i}]", Abnormal[i].AsTemplate());
        }
    }

    private static void CheckTemplate(ValidationContext context, string field, ResultTemplate template)
    {
        if (template.Summaries.Count == 0 || template.Summaries.Any(string.IsNullOrWhiteSpace))
        {
            context.Error(field + ".summaries", "must contain at least one non-empty report.");
        }

        if (template.ValueRange is { } range && (range.Length != 2 || range[0] > range[1]))
        {
            context.Error(field + ".value_range", "must be [low, high].");
        }

        if (template.ValueRange is null && template.Summaries.Any(s => s.Contains("{value}", StringComparison.Ordinal)))
        {
            context.Error(field + ".summaries", "uses {value} but no value_range is given.");
        }
    }
}
