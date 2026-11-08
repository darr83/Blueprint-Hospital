namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>How many patients arrive at a department, by hour and weekday. Loaded from Data/Arrivals.</summary>
public sealed class ArrivalProfile : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Department { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Average arrivals per hour for each hour of the day, starting at midnight.</summary>
    public IReadOnlyList<double> HourlyArrivals { get; init; } = Array.Empty<double>();

    /// <summary>Multiplier per day of the week, keyed by lowercase English day name.</summary>
    public IReadOnlyDictionary<string, double> WeekdayMultipliers { get; init; } = new Dictionary<string, double>();

    public double RateAt(DateTime time)
    {
        var day = time.DayOfWeek.ToString().ToLowerInvariant();
        var multiplier = WeekdayMultipliers.TryGetValue(day, out var m) ? m : 1.0;
        return HourlyArrivals[time.Hour] * multiplier;
    }

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireId("department", Department);
        if (HourlyArrivals.Count != 24)
        {
            context.Error("hourly_arrivals", $"must have 24 values, one per hour (has {HourlyArrivals.Count}).");
        }

        for (var i = 0; i < HourlyArrivals.Count; i++)
        {
            context.RequireRange($"hourly_arrivals[{i}]", HourlyArrivals[i], 0, 200);
        }

        foreach (var (day, value) in WeekdayMultipliers)
        {
            if (!Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out _))
            {
                context.Error($"weekday_multipliers.{day}", "is not a day of the week.");
            }

            context.RequireRange($"weekday_multipliers.{day}", value, 0, 10);
        }
    }
}
