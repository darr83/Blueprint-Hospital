namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>The four seasons, as the calendar counts them (winter is December to February).</summary>
public enum Season
{
    Winter,
    Spring,
    Summer,
    Autumn,
}

/// <summary>
/// How the year changes who comes to A&amp;E: how busy each month is, which illnesses are commoner in
/// which months, and the winter waves (flu, RSV) that rise and fall at a different time each year.
/// Loaded from Data/Seasons.
/// </summary>
public sealed class SeasonProfile : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>January to December.</summary>
    public IReadOnlyList<SeasonMonth> Months { get; init; } = Array.Empty<SeasonMonth>();

    /// <summary>
    /// How much commoner each illness is in each month (12 values, January first; 1 = as common as usual).
    /// Illnesses not listed are equally common all year.
    /// </summary>
    public IReadOnlyDictionary<string, double[]> ConditionMonths { get; init; } = new Dictionary<string, double[]>();

    /// <summary>Outbreaks that come once a year, at a different time and strength each year.</summary>
    public IReadOnlyList<SeasonWave> Waves { get; init; } = Array.Empty<SeasonWave>();

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        if (Months.Count != 12)
        {
            context.Error("months", $"must have 12 months, January first (has {Months.Count}).");
        }

        for (var i = 0; i < Months.Count; i++)
        {
            context.RequireRange($"months[{i}].volume", Months[i].Volume, 0.2, 3);
        }

        foreach (var (condition, values) in ConditionMonths)
        {
            if (values.Length != 12)
            {
                context.Error($"condition_months.{condition}", $"must have 12 values, January first (has {values.Length}).");
            }

            for (var i = 0; i < values.Length; i++)
            {
                context.RequireRange($"condition_months.{condition}[{i}]", values[i], 0, 50);
            }
        }

        for (var i = 0; i < Waves.Count; i++)
        {
            Waves[i].Validate(context, $"waves[{i}]");
        }
    }
}

/// <summary>One month of the year.</summary>
public sealed class SeasonMonth
{
    public Season Season { get; init; }

    /// <summary>How many people come to A&amp;E compared with an average month (1).</summary>
    public double Volume { get; init; } = 1;
}

/// <summary>
/// A yearly outbreak such as flu: it starts on a random day between <see cref="EarliestStart"/> and
/// <see cref="LatestStart"/>, builds for <see cref="RiseDays"/> to its peak and dies away over <see cref="FallDays"/>.
/// </summary>
public sealed class SeasonWave
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Earliest start, as month and day ("11-01").</summary>
    public string EarliestStart { get; init; } = "11-01";

    /// <summary>Latest start, as month and day ("12-20").</summary>
    public string LatestStart { get; init; } = "12-20";

    public int RiseDays { get; init; } = 30;

    public int FallDays { get; init; } = 60;

    /// <summary>How strong a year's wave can be, lowest to highest (1 = a typical year).</summary>
    public double[] Strength { get; init; } = { 0.5, 1.5 };

    /// <summary>At the peak of a typical year, how much commoner each illness is (6 = six times as common).</summary>
    public IReadOnlyDictionary<string, double> Conditions { get; init; } = new Dictionary<string, double>();

    /// <summary>At the peak of a typical year, how many more people come to A&amp;E (0.08 = 8% more).</summary>
    public double ExtraVolume { get; init; }

    internal void Validate(ValidationContext context, string field)
    {
        context.RequireId(field + ".id", Id);
        context.RequireText(field + ".name", Name);
        if (!TryParseDay(EarliestStart, out _, out _))
        {
            context.Error(field + ".earliest_start", "must be a month and day like \"11-01\".");
        }

        if (!TryParseDay(LatestStart, out _, out _))
        {
            context.Error(field + ".latest_start", "must be a month and day like \"12-20\".");
        }

        context.RequireRange(field + ".rise_days", RiseDays, 1, 200);
        context.RequireRange(field + ".fall_days", FallDays, 1, 300);
        if (Strength.Length != 2 || Strength[0] > Strength[1])
        {
            context.Error(field + ".strength", "must be [lowest, highest].");
        }

        foreach (var (condition, value) in Conditions)
        {
            context.RequireRange($"{field}.conditions.{condition}", value, 0, 50);
        }

        context.RequireRange(field + ".extra_volume", ExtraVolume, 0, 2);
    }

    /// <summary>Reads "MM-DD".</summary>
    public static bool TryParseDay(string text, out int month, out int day)
    {
        month = day = 0;
        var parts = text.Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out month) && int.TryParse(parts[1], out day)
               && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(2001, month);
    }
}
