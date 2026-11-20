using BlueprintHospital.Simulation.Hospital;

namespace BlueprintHospital.Simulation.Data.Models;

public enum InvestigationCategory
{
    /// <summary>Done at the bedside and read straight away (ECG, urine test).</summary>
    Bedside,

    /// <summary>A sample is taken at the bedside and sent to the laboratory.</summary>
    Blood,

    /// <summary>
    /// Done in its own room by that room's specialist (X-ray, CT, echo, EEG, ultrasound, endoscopy);
    /// the patient is taken there and the report follows after the turnaround time.
    /// </summary>
    Imaging,
}

public enum SexRestriction
{
    Any,
    Female,
    Male,
}

/// <summary>
/// One line of a lab or bedside result sheet (a blood value such as haemoglobin, or a finding such as "Nitrites").
/// Numeric analytes have a reference range; text analytes have a normal wording.
/// </summary>
public sealed class AnalyteDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Unit { get; init; }

    /// <summary>Lower end of the reference range (none for "less than" ranges).</summary>
    public double? Low { get; init; }

    /// <summary>Upper end of the reference range (none for "more than" ranges).</summary>
    public double? High { get; init; }

    /// <summary>Female reference range, where it differs (haemoglobin, creatinine).</summary>
    public double? LowFemale { get; init; }

    public double? HighFemale { get; init; }

    /// <summary>[low, high] a healthy patient's value is drawn from. Required for numeric analytes.</summary>
    public double[]? Normal { get; init; }

    /// <summary>For text analytes: what a normal result says (e.g. "Negative").</summary>
    public string? NormalText { get; init; }

    /// <summary>Decimal places to show. Left out, the value decides (0 at 20 and above, else 1).</summary>
    public int? Decimals { get; init; }

    /// <summary>
    /// Words that name this analyte in a summary, so a number written there (e.g. "Potassium 5.6") is shown
    /// on the sheet. The name is always tried too.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    public bool IsText => Normal is null && NormalText is not null;

    public (double? Low, double? High) RangeFor(bool female) =>
        female && (LowFemale is not null || HighFemale is not null) ? (LowFemale ?? Low, HighFemale ?? High) : (Low, High);
}

/// <summary>A possible result: the text shown to the player and an optional numeric value range.</summary>
public class ResultTemplate
{
    /// <summary>[low, high] for numeric tests. The value is drawn from this range.</summary>
    public double[]? ValueRange { get; init; }

    /// <summary>Report wording. One is chosen at random; "{value}" is replaced with the value.</summary>
    public IReadOnlyList<string> Summaries { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Imaging, ECG and bedside pictures, one per summary (or one for them all). Ids come from the game's
    /// picture list (see ResultPictures).
    /// </summary>
    public IReadOnlyList<string> Pictures { get; init; } = Array.Empty<string>();

    /// <summary>The longer "Findings" text of an imaging report, one per summary (or one for them all).</summary>
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Values of the sheet's analytes for this result, as [low, high] to draw from (equal for an exact value).
    /// Analytes not listed stay normal, unless the summary names a value for them.
    /// </summary>
    public IReadOnlyDictionary<string, double[]> Analytes { get; init; } = new Dictionary<string, double[]>();

    /// <summary>Wording of text analytes for this result (e.g. "nitrites": "Positive").</summary>
    public IReadOnlyDictionary<string, string> AnalyteText { get; init; } = new Dictionary<string, string>();
}

/// <summary>What the test shows when the patient has a particular condition.</summary>
public sealed class AbnormalResult : ResultTemplate
{
    public string Condition { get; init; } = string.Empty;

    /// <summary>Chance the test is abnormal in a patient with this condition (sensitivity).</summary>
    public double Probability { get; init; }

    /// <summary>
    /// The picture shown when a patient with this condition gets a normal result (e.g. a normal wrist X-ray for a
    /// suspected wrist fracture). Left out, the test's normal picture is used.
    /// </summary>
    public string? NormalPicture { get; init; }

    public ResultTemplate AsTemplate() => this;
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

    /// <summary>The lines of the result sheet (blood values, dipstick pads and so on). Empty for imaging and ECG.</summary>
    public IReadOnlyList<AnalyteDefinition> Analytes { get; init; } = Array.Empty<AnalyteDefinition>();

    public ResultTemplate Normal { get; init; } = new();

    /// <summary>
    /// Which picture a normal result shows, by the patient's illness or symptom, so a normal X-ray is of the part
    /// that hurts (a hip for a fall, a knee for a hot swollen joint). Keys are condition or symptom ids.
    /// </summary>
    public IReadOnlyDictionary<string, string> NormalPictures { get; init; } = new Dictionary<string, string>();

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

        if ((Category == InvestigationCategory.Imaging) != (Room is { } room && CareSpace.IsTestRoom(room)))
        {
            context.Error("room", "imaging tests, and only imaging tests, name their room: x_ray, ct, echo, eeg, ultrasound or endoscopy.");
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
            if (Abnormal[i].NormalPicture is { } np && !ResultPictureIds.IsKnown(np))
            {
                context.Error($"abnormal[{i}].normal_picture", $"'{np}' is not a known picture.");
            }
        }

        foreach (var (key, picture) in NormalPictures.Where(p => !ResultPictureIds.IsKnown(p.Value)))
        {
            context.Error("normal_pictures", $"'{picture}' for '{key}' is not a known picture.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Analytes.Count; i++)
        {
            var a = Analytes[i];
            context.RequireId($"analytes[{i}].id", a.Id);
            context.RequireText($"analytes[{i}].name", a.Name);
            if (!seen.Add(a.Id))
            {
                context.Error($"analytes[{i}].id", $"'{a.Id}' is listed twice.");
            }

            if (a.Normal is null && a.NormalText is null)
            {
                context.Error($"analytes[{i}]", "needs normal (a [low, high] range) or normal_text.");
            }

            if (a.Normal is { } n && (n.Length != 2 || n[0] > n[1]))
            {
                context.Error($"analytes[{i}].normal", "must be [low, high].");
            }
        }
    }

    private void CheckTemplate(ValidationContext context, string field, ResultTemplate template)
    {
        foreach (var p in template.Pictures.Where(p => !ResultPictureIds.IsKnown(p)))
        {
            context.Error(field + ".pictures", $"'{p}' is not a known picture.");
        }

        if (template.Pictures.Count > 1 && template.Pictures.Count != template.Summaries.Count)
        {
            context.Error(field + ".pictures", "must have one picture, or one per summary.");
        }

        if (template.Findings.Count > 1 && template.Findings.Count != template.Summaries.Count)
        {
            context.Error(field + ".findings", "must have one entry, or one per summary.");
        }

        foreach (var (id, values) in template.Analytes)
        {
            if (Analytes.FirstOrDefault(a => a.Id == id) is not { IsText: false })
            {
                context.Error(field + ".analytes", $"'{id}' is not a numeric analyte of this test.");
            }

            if (values.Length != 2 || values[0] > values[1])
            {
                context.Error(field + ".analytes", $"'{id}' must be [low, high].");
            }
        }

        foreach (var id in template.AnalyteText.Keys.Where(id => Analytes.FirstOrDefault(a => a.Id == id) is not { IsText: true }))
        {
            context.Error(field + ".analyte_text", $"'{id}' is not a text analyte of this test.");
        }

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
