namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>
/// A day (or night, or few days) in the year that changes A&amp;E: New Year's Eve, Christmas, Bonfire
/// Night, bank holidays, big matches, a music festival nearby. Loaded from Data/Events.
/// It comes on a fixed date, a date worked out each year (Easter, the last Monday in May), on certain
/// weekdays in some months (big matches on Saturdays), or on a random date in a window (a festival).
/// </summary>
public sealed class CalendarEventDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>What it does, for the Calendar.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// When: "12-25" (a fixed date), "easter-2" (days from Easter Sunday), "last-mon-05" or "first-wed-08"
    /// (a weekday in a month). Empty when it comes on <see cref="Weekdays"/> or in <see cref="RandomIn"/>.
    /// </summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>Weekdays it can come on ("sat"), in <see cref="Months"/>, each with <see cref="Chance"/>.</summary>
    public IReadOnlyList<string> Weekdays { get; init; } = Array.Empty<string>();

    /// <summary>The months a weekday event can come in (1 to 12; all if empty).</summary>
    public IReadOnlyList<int> Months { get; init; } = Array.Empty<int>();

    /// <summary>For a weekday event, the chance each of those days has it (the same game always has the same days).</summary>
    public double Chance { get; init; } = 1;

    /// <summary>Once a year on a random day between these two dates ("06-15", "08-20").</summary>
    public IReadOnlyList<string> RandomIn { get; init; } = Array.Empty<string>();

    /// <summary>How many days it lasts.</summary>
    public int Days { get; init; } = 1;

    /// <summary>The hours of each day it is on, from 0 to 24; <see cref="ToHour"/> past 24 runs on into the next morning.</summary>
    public double FromHour { get; init; }

    public double ToHour { get; init; } = 24;

    /// <summary>How many come to A&amp;E while it is on, compared with usual.</summary>
    public double Volume { get; init; } = 1;

    /// <summary>How much commoner each illness or injury is while it is on.</summary>
    public IReadOnlyDictionary<string, double> Conditions { get; init; } = new Dictionary<string, double>();

    /// <summary>The chance that someone coming in with each of these is brought in by the police, who stay with them in A&amp;E.</summary>
    public IReadOnlyDictionary<string, double> Police { get; init; } = new Dictionary<string, double>();

    /// <summary>The share of staff on annual leave each shift while it is on (0.1 = one in ten).</summary>
    public double Leave { get; init; }

    /// <summary>Decorations put up while it is on: "christmas", "halloween", "easter" or "" for none.</summary>
    public string Decorations { get; init; } = string.Empty;

    /// <summary>A message for the player when it starts (none if empty).</summary>
    public string Notice { get; init; } = string.Empty;

    /// <summary>Whether it is listed on the Calendar (Friday and Saturday nights are not).</summary>
    public bool Show { get; init; } = true;

    public static readonly IReadOnlySet<string> DecorationKinds = new HashSet<string>(StringComparer.Ordinal) { "", "christmas", "halloween", "easter" };

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        var ways = (Date.Length > 0 ? 1 : 0) + (Weekdays.Count > 0 ? 1 : 0) + (RandomIn.Count > 0 ? 1 : 0);
        if (ways != 1)
        {
            context.Error("date", "needs exactly one of date, weekdays or random_in.");
        }

        if (Date.Length > 0 && !EventDates.IsValidRule(Date))
        {
            context.Error("date", $"'{Date}' is not a date like \"12-25\", \"easter-2\" or \"last-mon-05\".");
        }

        foreach (var day in Weekdays)
        {
            if (!EventDates.TryParseWeekday(day, out _))
            {
                context.Error("weekdays", $"'{day}' is not a weekday (mon, tue, wed, thu, fri, sat, sun).");
            }
        }

        foreach (var month in Months)
        {
            context.RequireRange("months", month, 1, 12);
        }

        if (RandomIn.Count > 0 && (RandomIn.Count != 2 || !SeasonWave.TryParseDay(RandomIn[0], out _, out _) || !SeasonWave.TryParseDay(RandomIn[1], out _, out _)))
        {
            context.Error("random_in", "must be two dates like [\"06-15\", \"08-20\"].");
        }

        context.RequireRange("chance", Chance, 0, 1);
        context.RequireRange("days", Days, 1, 60);
        context.RequireRange("from_hour", FromHour, 0, 24);
        context.RequireRange("to_hour", ToHour, FromHour, 48);
        context.RequireRange("volume", Volume, 0.3, 3);
        context.RequireRange("leave", Leave, 0, 0.5);
        foreach (var (condition, value) in Conditions)
        {
            context.RequireRange($"conditions.{condition}", value, 0, 50);
        }

        foreach (var (condition, value) in Police)
        {
            context.RequireRange($"police.{condition}", value, 0, 1);
        }

        if (!DecorationKinds.Contains(Decorations))
        {
            context.Error("decorations", "must be christmas, halloween, easter or empty.");
        }
    }
}

/// <summary>Works out the dates in the event data: Easter, "the last Monday in May" and the like.</summary>
public static class EventDates
{
    private static readonly string[] DayNames = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

    public static bool TryParseWeekday(string text, out DayOfWeek day)
    {
        var i = Array.IndexOf(DayNames, text.ToLowerInvariant());
        day = i < 0 ? DayOfWeek.Sunday : (DayOfWeek)i;
        return i >= 0;
    }

    public static bool IsValidRule(string rule) => TryResolve(rule, 2027, out _);

    /// <summary>The date a rule falls on in a year.</summary>
    public static bool TryResolve(string rule, int year, out DateTime date)
    {
        date = default;
        if (SeasonWave.TryParseDay(rule, out var month, out var day))
        {
            if (month == 2 && day == 29 && !DateTime.IsLeapYear(year))
            {
                day = 28;
            }

            date = new DateTime(year, month, day);
            return true;
        }

        if (rule.StartsWith("easter", StringComparison.Ordinal))
        {
            var offset = rule.Length == 6 ? 0 : int.TryParse(rule[6..], out var o) ? o : int.MinValue;
            if (offset == int.MinValue || Math.Abs(offset) > 60)
            {
                return false;
            }

            date = Easter(year).AddDays(offset);
            return true;
        }

        // "last-mon-05", "first-wed-08", "second-sat-06".
        var parts = rule.Split('-');
        if (parts.Length != 3 || !TryParseWeekday(parts[1], out var weekday) || !int.TryParse(parts[2], out month) || month is < 1 or > 12)
        {
            return false;
        }

        var nth = parts[0] switch
        {
            "first" => 1,
            "second" => 2,
            "third" => 3,
            "fourth" => 4,
            "last" => -1,
            _ => 0,
        };
        if (nth == 0)
        {
            return false;
        }

        if (nth > 0)
        {
            var d = new DateTime(year, month, 1);
            while (d.DayOfWeek != weekday)
            {
                d = d.AddDays(1);
            }

            date = d.AddDays(7 * (nth - 1));
        }
        else
        {
            var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            while (d.DayOfWeek != weekday)
            {
                d = d.AddDays(-1);
            }

            date = d;
        }

        return true;
    }

    /// <summary>Easter Sunday (the Western church's date), by the anonymous Gregorian algorithm.</summary>
    public static DateTime Easter(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateTime(year, month, day);
    }
}
