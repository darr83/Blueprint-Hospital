using System.Globalization;
using System.Text.RegularExpressions;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Patients;

namespace BlueprintHospital.Simulation.Medical.Results;

/// <summary>How a result is shown: a white report sheet, or the dark imaging viewer.</summary>
public enum ResultLayout
{
    /// <summary>Lab and bedside results, ECGs: a hospital report sheet.</summary>
    Sheet,

    /// <summary>X-ray, CT, ultrasound, echo, EEG, endoscopy: the dark imaging viewer.</summary>
    Imaging,
}

/// <summary>One line of a result sheet.</summary>
/// <param name="Flag">"H" above the range, "L" below it, "*" for an abnormal text result, otherwise empty.</param>
public sealed record AnalyteRow(
    string Id,
    string Name,
    string Value,
    string? Unit,
    string RangeText,
    double? Numeric,
    double? Low,
    double? High,
    string Flag)
{
    public bool IsAbnormal => Flag.Length > 0;
}

/// <summary>
/// A readable result document built from a finished investigation: the values against their reference ranges
/// for lab tests, or the picture and written report for imaging. It follows the result the patient actually got
/// (normal, a false positive, or the finding for their condition), so it always agrees with the one-line summary.
/// Rebuilt the same way every time from the order, so nothing extra needs saving.
/// </summary>
public sealed class ResultDocument
{
    public required InvestigationDefinition Test { get; init; }

    public required InvestigationOrder Order { get; init; }

    public required ResultLayout Layout { get; init; }

    /// <summary>Sheet lines; empty for imaging and ECG.</summary>
    public required IReadOnlyList<AnalyteRow> Rows { get; init; }

    /// <summary>The picture to draw (X-ray, scan, ECG trace, test cassette), or null for a table-only sheet.</summary>
    public required string? PictureId { get; init; }

    /// <summary>The "Findings" paragraph of an imaging report.</summary>
    public required string? Findings { get; init; }

    /// <summary>The conclusion: the one-line summary shown in the patient record.</summary>
    public string Conclusion => Order.Summary;

    /// <summary>Seed for the picture's small variations (side, displacement, noise).</summary>
    public required int Seed { get; init; }

    /// <summary>Who produced the report, for the footer.</summary>
    public required string Department { get; init; }

    public bool Abnormal => Order.Abnormal;

    /// <summary>Builds the document for a resulted <paramref name="order"/>; null if it has no result yet.</summary>
    public static ResultDocument? Build(InvestigationDefinition test, Patient patient, InvestigationOrder order)
    {
        if (order.Status != InvestigationStatus.Resulted)
        {
            return null;
        }

        var seed = StableSeed(patient.Id, test.Id, order.RequestedAt);
        var random = new Random(seed);
        var (template, specific) = TemplateFor(test, patient, order);
        var index = SummaryIndex(template, order);

        string? picture = null;
        if (template.Pictures.Count > 0)
        {
            picture = template.Pictures[Math.Min(index, template.Pictures.Count - 1)];
        }

        if (!order.Abnormal)
        {
            picture = specific?.NormalPicture ?? NormalPictureFor(test, patient) ?? picture;
        }

        string? findings = template.Findings.Count > 0 ? template.Findings[Math.Min(index, template.Findings.Count - 1)] : null;

        var rows = test.Analytes.Count == 0
            ? (IReadOnlyList<AnalyteRow>)Array.Empty<AnalyteRow>()
            : BuildRows(test, template, order.Summary, patient.Sex == Sex.Female, random);

        return new ResultDocument
        {
            Test = test,
            Order = order,
            Layout = test.Category == InvestigationCategory.Imaging ? ResultLayout.Imaging : ResultLayout.Sheet,
            Rows = rows,
            PictureId = picture,
            Findings = findings ?? (test.Category == InvestigationCategory.Imaging ? order.Summary : null),
            Seed = seed,
            Department = DepartmentFor(test),
        };
    }

    /// <summary>
    /// The normal picture for this patient's illness, else for their presenting complaint, else for their other
    /// symptoms in order; null when the test does not vary its normal picture.
    /// </summary>
    public static string? NormalPictureFor(InvestigationDefinition test, Patient patient)
    {
        if (test.NormalPictures.Count == 0)
        {
            return null;
        }

        foreach (var key in patient.Symptoms.Prepend(patient.PresentingComplaint).Prepend(patient.Condition.Id))
        {
            if (test.NormalPictures.TryGetValue(key, out var picture))
            {
                return picture;
            }
        }

        return null;
    }

    /// <summary>
    /// Which result template produced the order's result: the patient's condition when abnormal and listed,
    /// the false positive when abnormal otherwise, else normal. Also returns the condition's entry, if any.
    /// </summary>
    public static (ResultTemplate Template, AbnormalResult? Specific) TemplateFor(InvestigationDefinition test, Patient patient, InvestigationOrder order)
    {
        var specific = test.Abnormal.FirstOrDefault(a => a.Condition == patient.Condition.Id);
        if (order.Abnormal)
        {
            if (specific is not null && MatchesAny(specific, order))
            {
                return (specific, specific);
            }

            if (test.FalsePositive is not null && MatchesAny(test.FalsePositive, order))
            {
                return (test.FalsePositive, specific);
            }

            return ((ResultTemplate?)specific ?? test.FalsePositive ?? test.Normal, specific);
        }

        return (test.Normal, specific);
    }

    private static bool MatchesAny(ResultTemplate template, InvestigationOrder order) => SummaryIndexOrMinus(template, order) >= 0;

    private static int SummaryIndex(ResultTemplate template, InvestigationOrder order) => Math.Max(0, SummaryIndexOrMinus(template, order));

    private static int SummaryIndexOrMinus(ResultTemplate template, InvestigationOrder order)
    {
        for (var i = 0; i < template.Summaries.Count; i++)
        {
            var text = template.Summaries[i];
            if (order.Value is { } v)
            {
                text = text.Replace("{value}", InvestigationSystem.FormatValue(v), StringComparison.Ordinal);
            }

            if (string.Equals(text, order.Summary, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<AnalyteRow> BuildRows(InvestigationDefinition test, ResultTemplate template, string summary, bool female, Random random)
    {
        var rows = new List<AnalyteRow>();
        foreach (var a in test.Analytes)
        {
            var (low, high) = a.RangeFor(female);
            if (a.IsText)
            {
                var text = template.AnalyteText.TryGetValue(a.Id, out var t) ? t : FindTextInSummary(a, summary) ?? a.NormalText!;
                var flag = string.Equals(text, a.NormalText, StringComparison.OrdinalIgnoreCase) ? string.Empty : "*";
                rows.Add(new AnalyteRow(a.Id, a.Name, text, a.Unit, a.NormalText!, null, null, null, flag));
                continue;
            }

            double value;
            if (FindInSummary(a, summary) is { } stated)
            {
                value = stated;
            }
            else if (template.Analytes.TryGetValue(a.Id, out var range))
            {
                value = range[0] + (random.NextDouble() * (range[1] - range[0]));
            }
            else
            {
                // A healthy value, kept a little inside the reference range so it never reads as borderline.
                var n = a.Normal!;
                var lo = n[0];
                var hi = n[1];
                if (low is { } l)
                {
                    lo = Math.Max(lo, l);
                }

                if (high is { } h)
                {
                    hi = Math.Min(hi, h);
                }

                value = lo + (random.NextDouble() * Math.Max(0, hi - lo));
            }

            var shown = Format(value, a.Decimals);
            var rounded = double.Parse(shown, CultureInfo.InvariantCulture);
            var f = low is { } lw && rounded < lw ? "L" : high is { } hg && rounded > hg ? "H" : string.Empty;
            rows.Add(new AnalyteRow(a.Id, a.Name, shown, a.Unit, RangeText(low, high, a.Decimals), rounded, low, high, f));
        }

        return rows;
    }

    /// <summary>
    /// Finds a number given for <paramref name="analyte"/> in the summary, e.g. "Potassium 5.6", "ALT raised at 180 U/L",
    /// "white cells 2,400". Lets the sheet agree with whatever the summary says, even after editing.
    /// </summary>
    public static double? FindInSummary(AnalyteDefinition analyte, string summary)
    {
        foreach (var name in analyte.Aliases.Append(analyte.Name))
        {
            var pattern = @"(?<![A-Za-z])" + Regex.Escape(name) +
                @"(?![A-Za-z])(?:\s+(?:is|was|mildly|slightly|markedly|raised|low|high|at|of|level|count))*\s*:?\s*(\d[\d,]*(?:\.\d+)?)";
            var match = Regex.Match(summary, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success && double.TryParse(match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>Finds a written result for a text analyte in the summary, e.g. "Influenza B detected".</summary>
    public static string? FindTextInSummary(AnalyteDefinition analyte, string summary)
    {
        foreach (var name in analyte.Aliases.Append(analyte.Name))
        {
            var match = Regex.Match(summary, @"(?<![A-Za-z])" + Regex.Escape(name) + @"\s+(not detected|detected|positive|negative)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var word = match.Groups[1].Value.ToLowerInvariant();
                return char.ToUpperInvariant(word[0]) + word[1..];
            }
        }

        return null;
    }

    public static string Format(double value, int? decimals)
    {
        var d = decimals ?? (Math.Abs(value) >= 20 ? 0 : 1);
        return value.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    public static string RangeText(double? low, double? high, int? decimals)
    {
        string F(double v) => decimals is { } d ? v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : v.ToString("0.###", CultureInfo.InvariantCulture);
        return (low, high) switch
        {
            ({ } l, { } h) => $"{F(l)} – {F(h)}",
            (null, { } h) => $"< {F(h)}",
            ({ } l, null) => $"> {F(l)}",
            _ => string.Empty,
        };
    }

    private static string DepartmentFor(InvestigationDefinition test) => test.Category switch
    {
        InvestigationCategory.Blood => "Pathology · Blood Sciences",
        InvestigationCategory.Imaging => test.Room switch
        {
            Hospital.CareSpaceKind.Endoscopy => "Endoscopy Unit",
            Hospital.CareSpaceKind.Echo => "Cardiac Physiology",
            Hospital.CareSpaceKind.Eeg => "Neurophysiology",
            _ => "Department of Radiology",
        },
        _ => "Emergency Department",
    };

    /// <summary>A seed that is the same every time for the same patient, test and request time.</summary>
    public static int StableSeed(int patientId, string testId, DateTime requestedAt)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var c in testId)
            {
                h = (h ^ c) * 16777619;
            }

            h = (h ^ patientId) * 16777619;
            h = (h ^ (int)(requestedAt.Ticks / TimeSpan.TicksPerMinute)) * 16777619;
            return h & 0x7FFFFFFF;
        }
    }
}
