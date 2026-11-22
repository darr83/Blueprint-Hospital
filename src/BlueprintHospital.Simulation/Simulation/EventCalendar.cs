using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>One day of a calendar event, and the hours it is on (End may run into the next morning).</summary>
public sealed record EventDay(CalendarEventDefinition Event, DateTime Day, DateTime Start, DateTime End)
{
    public bool IsOn(DateTime time) => time >= Start && time < End;

    /// <summary>The first day of a run of days (the day to announce it).</summary>
    public bool First { get; init; } = true;
}

/// <summary>
/// The year's calendar events, worked out once per year from the data and the game's seed: New Year's
/// Eve, Christmas, bank holidays (Easter and the May and August Mondays move each year), the Saturdays
/// with a big match, and the weekend of the music festival. The same game always has the same days.
/// </summary>
public sealed class EventCalendar(IReadOnlyList<CalendarEventDefinition> events, int seed)
{
    private readonly Dictionary<int, List<EventDay>> _years = new();

    public IReadOnlyList<CalendarEventDefinition> Events => events;

    /// <summary>The events on at a moment (including a night that began the evening before).</summary>
    public IEnumerable<EventDay> ActiveAt(DateTime time) =>
        Year(time.Year).Concat(time.Month == 1 ? Year(time.Year - 1) : Enumerable.Empty<EventDay>())
            .Where(d => d.IsOn(time));

    /// <summary>The events whose day is this date.</summary>
    public IEnumerable<EventDay> On(DateTime date) => Year(date.Year).Where(d => d.Day == date.Date);

    /// <summary>The events on or starting in the days from a date, in order: one entry for each run of days (its first day, or the one on now).</summary>
    public IEnumerable<EventDay> Upcoming(DateTime from, int days)
    {
        var to = from.Date.AddDays(days);
        return Year(from.Year).Concat(to.Year != from.Year ? Year(to.Year) : Enumerable.Empty<EventDay>())
            .Where(d => d.Day < to && ((d.First && d.End > from) || (d.Start <= from && d.End > from) || (d.Day == from.Date && !d.First && d.End > from)))
            .GroupBy(d => (d.Event.Id, d.First ? d.Day : from.Date))
            .Select(g => g.First())
            .OrderBy(d => d.Start);
    }

    private List<EventDay> Year(int year)
    {
        if (!_years.TryGetValue(year, out var list))
        {
            list = _years[year] = MakeYear(year);
        }

        return list;
    }

    private List<EventDay> MakeYear(int year)
    {
        var list = new List<EventDay>();
        foreach (var e in events)
        {
            foreach (var start in StartDays(e, year))
            {
                for (var i = 0; i < e.Days; i++)
                {
                    var day = start.AddDays(i);
                    list.Add(new EventDay(e, day, day.AddHours(e.FromHour), day.AddHours(e.ToHour)) { First = i == 0 });
                }
            }
        }

        return list.OrderBy(d => d.Start).ToList();
    }

    private IEnumerable<DateTime> StartDays(CalendarEventDefinition e, int year)
    {
        if (e.Date.Length > 0)
        {
            if (EventDates.TryResolve(e.Date, year, out var date))
            {
                yield return date;
            }

            yield break;
        }

        if (e.RandomIn.Count == 2 && SeasonWave.TryParseDay(e.RandomIn[0], out var m1, out var d1) && SeasonWave.TryParseDay(e.RandomIn[1], out var m2, out var d2))
        {
            var first = new DateTime(year, m1, d1);
            var last = new DateTime(year, m2, d2);
            var span = Math.Max(0, (int)(last - first).TotalDays);
            var random = new GameRandom(Hash(e.Id, year, 0));
            yield return first.AddDays(random.Range(0, span));
            yield break;
        }

        var weekdays = e.Weekdays.Select(w => EventDates.TryParseWeekday(w, out var d) ? d : (DayOfWeek?)null).Where(d => d is not null).ToHashSet();
        for (var day = new DateTime(year, 1, 1); day.Year == year; day = day.AddDays(1))
        {
            if (!weekdays.Contains(day.DayOfWeek) || (e.Months.Count > 0 && !e.Months.Contains(day.Month)))
            {
                continue;
            }

            if (e.Chance >= 1 || new GameRandom(Hash(e.Id, year, day.DayOfYear)).NextDouble() < e.Chance)
            {
                yield return day;
            }
        }
    }

    private int Hash(string id, int year, int day)
    {
        unchecked
        {
            var h = (seed * 486_187_739) ^ (year * 104_729) ^ (day * 7919);
            foreach (var c in id)
            {
                h = (h * 31) + c;
            }

            return h;
        }
    }
}
