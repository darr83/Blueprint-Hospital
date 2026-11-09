namespace BlueprintHospital.Simulation.Simulation;

/// <summary>The spans of time the Stats screen can show.</summary>
public enum StatsPeriod
{
    Last24Hours,
    Last7Days,
    Last28Days,
    AllTime,
}

/// <summary>A gauge read at regular moments (patients in A&amp;E, beds in use...): how many readings, their sum and the highest.</summary>
public sealed class GaugeReadings
{
    public double Sum { get; set; }

    public int Count { get; set; }

    public double Peak { get; set; }

    public double Average => Count == 0 ? 0 : Sum / Count;

    internal void Add(double value)
    {
        Peak = Count == 0 ? value : Math.Max(Peak, value);
        Sum += value;
        Count++;
    }

    internal void Merge(GaugeReadings other)
    {
        if (other.Count == 0)
        {
            return;
        }

        Peak = Count == 0 ? other.Peak : Math.Max(Peak, other.Peak);
        Sum += other.Sum;
        Count += other.Count;
    }
}

/// <summary>Everything counted in one hour, one day or since the hospital opened.</summary>
public sealed class StatsBucket
{
    public DateTime Start { get; set; }

    public Dictionary<string, double> Totals { get; set; } = new();

    public Dictionary<string, GaugeReadings> Gauges { get; set; } = new();

    internal void Add(string key, double amount) => Totals[key] = Totals.GetValueOrDefault(key) + amount;

    internal void Sample(string key, double value)
    {
        if (!Gauges.TryGetValue(key, out var readings))
        {
            Gauges[key] = readings = new GaugeReadings();
        }

        readings.Add(value);
    }
}

/// <summary>The stats history in a save.</summary>
public sealed class StatsHistoryState
{
    public DateTime Since { get; init; }

    public List<StatsBucket> Hours { get; init; } = new();

    public List<StatsBucket> Days { get; init; } = new();

    public StatsBucket AllTime { get; init; } = new();
}

/// <summary>
/// The hospital's figures over time: counts (arrivals, deaths, money...) and regular readings of gauges
/// (patients in A&amp;E, ward beds in use...), kept hour by hour for the last day, day by day for the last
/// 28 days, and in one running total since the hospital opened.
/// </summary>
public sealed class StatsHistory
{
    public const int HoursKept = 24;
    public const int DaysKept = 28;

    private readonly List<StatsBucket> _hours = new();
    private readonly List<StatsBucket> _days = new();
    private StatsBucket _allTime = new();

    public StatsHistory(DateTime since)
    {
        Since = since;
        _allTime.Start = since;
    }

    /// <summary>When counting started (the start of the game, or when an older save was first loaded).</summary>
    public DateTime Since { get; private set; }

    /// <summary>Adds to a count (money can be negative).</summary>
    public void Add(DateTime now, string key, double amount = 1)
    {
        if (amount == 0)
        {
            return;
        }

        Hour(now).Add(key, amount);
        Day(now).Add(key, amount);
        _allTime.Add(key, amount);
    }

    /// <summary>Takes a reading of a gauge.</summary>
    public void Sample(DateTime now, string key, double value)
    {
        Hour(now).Sample(key, value);
        Day(now).Sample(key, value);
        _allTime.Sample(key, value);
    }

    /// <summary>The buckets that make up a period, oldest first.</summary>
    public IReadOnlyList<StatsBucket> Buckets(StatsPeriod period, DateTime now)
    {
        switch (period)
        {
            case StatsPeriod.Last24Hours:
                var hourFrom = HourOf(now).AddHours(-(HoursKept - 1));
                return _hours.Where(b => b.Start >= hourFrom).OrderBy(b => b.Start).ToList();
            case StatsPeriod.AllTime:
                return new[] { _allTime };
            default:
                var dayFrom = now.Date.AddDays(-(DaysIn(period) - 1));
                return _days.Where(b => b.Start >= dayFrom).OrderBy(b => b.Start).ToList();
        }
    }

    public double Total(StatsPeriod period, DateTime now, string key) => Buckets(period, now).Sum(b => b.Totals.GetValueOrDefault(key));

    public GaugeReadings Gauge(StatsPeriod period, DateTime now, string key)
    {
        var merged = new GaugeReadings();
        foreach (var bucket in Buckets(period, now))
        {
            if (bucket.Gauges.TryGetValue(key, out var readings))
            {
                merged.Merge(readings);
            }
        }

        return merged;
    }

    /// <summary>
    /// A count hour by hour (last 24 hours) or day by day (7 or 28 days, and all time, which shows the
    /// last 28 days), oldest first, with a slot for every hour or day even if nothing happened.
    /// </summary>
    public IReadOnlyList<(DateTime Start, double Value)> Series(StatsPeriod period, DateTime now, string key)
    {
        if (period == StatsPeriod.Last24Hours)
        {
            var first = HourOf(now).AddHours(-(HoursKept - 1));
            return Enumerable.Range(0, HoursKept)
                .Select(i => first.AddHours(i))
                .Select(h => (h, _hours.FirstOrDefault(b => b.Start == h)?.Totals.GetValueOrDefault(key) ?? 0))
                .ToList();
        }

        var days = period == StatsPeriod.AllTime ? DaysKept : DaysIn(period);
        var from = now.Date.AddDays(-(days - 1));
        return Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(d => (d, _days.FirstOrDefault(b => b.Start == d)?.Totals.GetValueOrDefault(key) ?? 0))
            .ToList();
    }

    /// <summary>How much of a period the game has actually been running for.</summary>
    public TimeSpan Covered(StatsPeriod period, DateTime now)
    {
        var played = now - Since;
        if (played < TimeSpan.Zero)
        {
            played = TimeSpan.Zero;
        }

        return period switch
        {
            StatsPeriod.Last24Hours => Min(played, TimeSpan.FromHours(24)),
            StatsPeriod.AllTime => played,
            _ => Min(played, TimeSpan.FromDays(DaysIn(period))),
        };
    }

    public StatsHistoryState Capture() => new()
    {
        Since = Since,
        Hours = _hours.ToList(),
        Days = _days.ToList(),
        AllTime = _allTime,
    };

    public void Restore(StatsHistoryState state)
    {
        Since = state.Since;
        _hours.Clear();
        _hours.AddRange(state.Hours.OrderByDescending(b => b.Start).Take(HoursKept));
        _days.Clear();
        _days.AddRange(state.Days.OrderByDescending(b => b.Start).Take(DaysKept));
        _allTime = state.AllTime;
    }

    /// <summary>Starts again from <paramref name="since"/> with nothing counted.</summary>
    public void Reset(DateTime since)
    {
        Since = since;
        _hours.Clear();
        _days.Clear();
        _allTime = new StatsBucket { Start = since };
    }

    /// <summary>Sets a running total since opening without adding it to any hour or day (for older saves).</summary>
    internal void SeedAllTime(string key, double total)
    {
        if (total != 0)
        {
            _allTime.Totals[key] = total;
        }
    }

    public static int DaysIn(StatsPeriod period) => period switch
    {
        StatsPeriod.Last7Days => 7,
        StatsPeriod.Last28Days => 28,
        _ => 1,
    };

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static DateTime HourOf(DateTime time) => new(time.Year, time.Month, time.Day, time.Hour, 0, 0, time.Kind);

    private StatsBucket Hour(DateTime now) => Current(_hours, HourOf(now), HoursKept);

    private StatsBucket Day(DateTime now) => Current(_days, now.Date, DaysKept);

    private static StatsBucket Current(List<StatsBucket> list, DateTime start, int kept)
    {
        if (list.Count > 0 && list[0].Start == start)
        {
            return list[0];
        }

        var bucket = new StatsBucket { Start = start };
        list.Insert(0, bucket);
        while (list.Count > kept)
        {
            list.RemoveAt(list.Count - 1);
        }

        return bucket;
    }
}
