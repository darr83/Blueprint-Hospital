using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Why someone is not at work this shift.</summary>
public enum StaffAbsence
{
    None,
    Sick,
    Leave,
}

/// <summary>
/// Calendar events (New Year's Eve, Christmas, bank holidays, big matches, a festival up the road):
/// while one is on it changes how many come in and with what, and some patients come in with the
/// police. Also staff off sick (more when flu and norovirus are going round) and on annual leave
/// (more over Christmas and in the summer holidays). These come with seasons.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Everyone has a small chance of being off sick on any shift (about the NHS's usual sickness rate).</summary>
    public const double BaseSicknessChance = 0.035;

    private EventCalendar? _eventCalendar;
    private DateTime _eventsCheckedTo;
    private readonly Dictionary<DateTime, Dictionary<int, StaffAbsence>> _absences = new();

    /// <summary>The year's events, if this game has seasons.</summary>
    public EventCalendar? CalendarEvents => _eventCalendar;

    /// <summary>Whether calendar events are changing who comes in (they come with seasons).</summary>
    public bool CalendarEventsActive => SeasonsActive && _eventCalendar is not null;

    /// <summary>The events on now.</summary>
    public IReadOnlyList<EventDay> EventsNow => CalendarEventsActive ? _eventCalendar!.ActiveAt(Now).ToList() : Array.Empty<EventDay>();

    /// <summary>The decorations up now ("christmas", "halloween", "easter"), or "" for none.</summary>
    public string DecorationsNow => CalendarEventsActive
        ? _eventCalendar!.ActiveAt(Now).Select(d => d.Event.Decorations).FirstOrDefault(d => d.Length > 0) ?? string.Empty
        : string.Empty;

    private void SetUpEvents(int seed)
    {
        var events = Database.CalendarEvents.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        _eventCalendar = _seasonCalendar is null || events.Count == 0 ? null : new EventCalendar(events, seed);
        _eventsCheckedTo = Now;
        _absences.Clear();
    }

    private double EventVolumeAt(DateTime time) =>
        CalendarEventsActive ? _eventCalendar!.ActiveAt(time).Aggregate(1.0, (v, d) => v * d.Event.Volume) : 1;

    /// <summary>A day's events averaged over the day, for the week ahead (New Year's Eve counts for its evening).</summary>
    private double EventDayVolume(DateTime date)
    {
        if (!CalendarEventsActive)
        {
            return 1;
        }

        double total = 0;
        for (var h = 0; h < 24; h++)
        {
            total += EventVolumeAt(date.Date.AddHours(h + 0.5));
        }

        return total / 24;
    }

    private double EventConditionWeight(string conditionId, DateTime time) =>
        CalendarEventsActive ? _eventCalendar!.ActiveAt(time).Aggregate(1.0, (w, d) => w * d.Event.Conditions.GetValueOrDefault(conditionId, 1)) : 1;

    /// <summary>The chance someone coming in now with this is brought in by the police.</summary>
    public double PoliceChance(string conditionId, DateTime time)
    {
        if (!CalendarEventsActive)
        {
            return 0;
        }

        var none = _eventCalendar!.ActiveAt(time).Aggregate(1.0, (p, d) => p * (1 - d.Event.Police.GetValueOrDefault(conditionId, 0)));
        return 1 - none;
    }

    /// <summary>Some adults come in with the police on a Saturday night or New Year's Eve.</summary>
    private void MaybePoliceEscort(Patient patient)
    {
        if (patient.Age < AdultAge || patient.Maternity)
        {
            return;
        }

        var chance = PoliceChance(patient.Condition.Id, Now);
        if (chance > 0 && Random.NextDouble() < chance)
        {
            patient.PoliceEscort = true;
        }
    }

    /// <summary>Announces events as they start (once each), and who is off sick or on leave as each shift starts.</summary>
    private void UpdateEvents()
    {
        if (!CalendarEventsActive)
        {
            _eventsCheckedTo = Now;
            return;
        }

        if (Now < _eventsCheckedTo || Now - _eventsCheckedTo > TimeSpan.FromDays(2))
        {
            _eventsCheckedTo = Now;
        }

        if (Now == _eventsCheckedTo)
        {
            return;
        }

        foreach (var day in _eventCalendar!.Upcoming(_eventsCheckedTo.AddDays(-1), 3).Where(d => d.First && d.Start > _eventsCheckedTo && d.Start <= Now && d.Event.Notice.Length > 0))
        {
            Notifications.Add(Now, NotificationLevel.Info, day.Event.Notice);
        }

        // Each shift: who has phoned in sick, and who is on leave.
        var shiftStart = CurrentShiftStart;
        if (shiftStart > _eventsCheckedTo && shiftStart <= Now)
        {
            var away = _staff.Where(s => s.Shift == CurrentShift).Select(s => (s, Absence: AbsenceOf(s, shiftStart))).Where(x => x.Absence != StaffAbsence.None).ToList();
            var sick = away.Count(x => x.Absence == StaffAbsence.Sick);
            var leave = away.Count(x => x.Absence == StaffAbsence.Leave);
            if (sick + leave > 0)
            {
                var parts = new List<string>();
                if (sick > 0)
                {
                    parts.Add($"{sick} off sick{(SicknessReason(shiftStart) is { } why ? $" ({why})" : string.Empty)}");
                }

                if (leave > 0)
                {
                    parts.Add($"{leave} on annual leave");
                }

                Notifications.Add(Now, sick >= 3 ? NotificationLevel.Warning : NotificationLevel.Info, $"{char.ToUpperInvariant(ShiftName(CurrentShift)[0])}{ShiftName(CurrentShift)[1..]} shift: {string.Join(" and ", parts)}. Staff rota (T) shows the gaps.");
            }
        }

        _eventsCheckedTo = Now;
    }

    /// <summary>What is behind the sickness this shift, if something is going round.</summary>
    private string? SicknessReason(DateTime shiftStart)
    {
        if (_seasonCalendar is null)
        {
            return null;
        }

        var flu = _seasonCalendar.WaveLevel("flu", shiftStart);
        var noro = _seasonCalendar.WaveLevel("norovirus", shiftStart);
        return flu >= 0.3 && flu >= noro ? "flu is going round" : noro >= 0.3 ? "norovirus is going round" : null;
    }

    /// <summary>The chance each member of staff is off sick on a shift: higher with flu and norovirus about.</summary>
    public double SicknessChance(DateTime shiftStart)
    {
        if (!CalendarEventsActive || _seasonCalendar is null)
        {
            return 0;
        }

        return BaseSicknessChance + (0.05 * _seasonCalendar.WaveLevel("flu", shiftStart)) + (0.03 * _seasonCalendar.WaveLevel("norovirus", shiftStart));
    }

    /// <summary>The share of staff on annual leave on a shift.</summary>
    public double LeaveShare(DateTime shiftStart) =>
        CalendarEventsActive ? _eventCalendar!.ActiveAt(shiftStart.AddHours(1)).Sum(d => d.Event.Leave) : 0;

    /// <summary>
    /// Whether someone is off this shift, and why. Worked out the same way every time from the game's seed,
    /// so it needs no saving; never more than half of a team (the same job in the same place on the same
    /// shift) is away, and someone working on their own never is.
    /// </summary>
    public StaffAbsence AbsenceOf(StaffMember member, DateTime shiftStart)
    {
        if (!CalendarEventsActive || member.Shift != ShiftAt(shiftStart.AddHours(1)))
        {
            return StaffAbsence.None;
        }

        if (!_absences.TryGetValue(shiftStart, out var shift))
        {
            shift = _absences[shiftStart] = WorkOutAbsences(shiftStart);
            foreach (var old in _absences.Keys.Where(k => k < shiftStart.AddDays(-2)).ToList())
            {
                _absences.Remove(old);
            }
        }

        return shift.GetValueOrDefault(member.Id, StaffAbsence.None);
    }

    private Dictionary<int, StaffAbsence> WorkOutAbsences(DateTime shiftStart)
    {
        var result = new Dictionary<int, StaffAbsence>();
        var sick = SicknessChance(shiftStart);
        var leave = LeaveShare(shiftStart);
        var shift = ShiftAt(shiftStart.AddHours(1));
        foreach (var team in _staff.Where(s => s.Shift == shift).GroupBy(s => (s.Role, s.BaseRoomId)))
        {
            var members = team.OrderBy(s => s.Id).ToList();
            var cap = members.Count / 2;
            foreach (var member in members)
            {
                if (result.Count(kv => members.Any(m => m.Id == kv.Key)) >= cap)
                {
                    break;
                }

                var roll = Roll(member.Id, shiftStart);
                if (roll < sick)
                {
                    result[member.Id] = StaffAbsence.Sick;
                }
                else if (roll < sick + leave)
                {
                    result[member.Id] = StaffAbsence.Leave;
                }
            }
        }

        return result;
    }

    private double Roll(int staffId, DateTime shiftStart)
    {
        unchecked
        {
            var h = (uint)((SeasonSeed * 73856093) ^ (staffId * 19349663) ^ ((int)(shiftStart.Ticks / TimeSpan.TicksPerHour) * 83492791));
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>Text for staff at home this shift.</summary>
    private static string AbsenceText(StaffAbsence absence) => absence == StaffAbsence.Sick ? "Off sick this shift" : "On annual leave this shift";
}
