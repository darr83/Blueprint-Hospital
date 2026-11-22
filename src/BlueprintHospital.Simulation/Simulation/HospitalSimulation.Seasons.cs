using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>One month on the Calendar's "year at a glance": how busy A&amp;E is and how many need a bed.</summary>
public sealed record MonthOutlook(int Month, Season Season, double Volume, double BedShare);

/// <summary>One day on the Calendar's week ahead.</summary>
public sealed record DayOutlook(DateTime Date, Season Season, double Busyness, double BedShare, IReadOnlyList<string> Notes)
{
    /// <summary>The day's weather, if the game has seasons.</summary>
    public DayWeather? Weather { get; init; }
}

/// <summary>The hospital pressure badge, based on the NHS's OPEL levels (1 steady to 4 critical).</summary>
public sealed record PressureReading(int Level, IReadOnlyList<string> Reasons)
{
    public string Name => Level switch
    {
        1 => "Steady",
        2 => "Busy",
        3 => "Severe",
        _ => "Critical",
    };
}

/// <summary>
/// Seasons: the time of year changes how many people come to A&amp;E and what they come in with (flu
/// and chest infections in winter, sunburn and heat illness in summer), and the flu and RSV waves rise
/// and fall at a different time each year. Also the hospital pressure badge.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How often the pressure badge is worked out again, so it doesn't flicker.</summary>
    private static readonly TimeSpan PressureInterval = TimeSpan.FromMinutes(15);

    private SeasonCalendar? _seasonCalendar;
    private WeatherCalendar? _weather;
    private DateTime _nextPressureReading;

    /// <summary>Whether this game has seasons switched on (the New Game setting).</summary>
    public bool SeasonsEnabled { get; private set; }

    /// <summary>The seed that sets when each year's flu and RSV come, and how bad they are.</summary>
    public int SeasonSeed { get; private set; }

    /// <summary>The calendar, if this game has seasons. Its effects only apply once <see cref="SeasonsActive"/>.</summary>
    public SeasonCalendar? Seasons => _seasonCalendar;

    /// <summary>Seasons are changing who comes in (switched on, and unlocked in a Career game).</summary>
    public bool SeasonsActive => _seasonCalendar is not null && IsFeatureOn(LevelFeatures.Seasons);

    public PressureReading Pressure { get; private set; } = new(1, Array.Empty<string>());

    private void SetUpSeasons(bool enabled, int seed)
    {
        SeasonsEnabled = enabled;
        SeasonSeed = seed;
        var profile = Database.Seasons.Values.OrderBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        _seasonCalendar = enabled && profile is not null ? new SeasonCalendar(profile, seed) : null;
        _weather = _seasonCalendar is null ? null : new WeatherCalendar(_seasonCalendar.Profile, seed);
    }

    /// <summary>How much commoner an illness is today than its usual prevalence.</summary>
    public double SeasonalWeight(ConditionDefinition condition, DateTime date)
    {
        if (!SeasonsActive)
        {
            return 1;
        }

        var weight = _seasonCalendar!.ConditionWeight(condition.Id, date);
        foreach (var effect in WeatherEffectsOn(date))
        {
            weight *= effect.Conditions.GetValueOrDefault(condition.Id, 1);
        }

        return weight;
    }

    /// <summary>The weather on a day, if this game has seasons (and so weather).</summary>
    public DayWeather? WeatherOn(DateTime date) =>
        _weatherOverride is { } forced && forced.Date == date.Date ? forced : _weather?.On(date);

    /// <summary>Today's weather as set by the "weather" cheat, until the day is over.</summary>
    private DayWeather? _weatherOverride;

    /// <summary>The "weather" cheat: sets today's weather (for trying the look and the effects), or "off" to go back to the forecast.</summary>
    private CheatResult WeatherCheat(string kind)
    {
        if (_weather is null)
        {
            return new CheatResult(false, "This game has no seasons, so no weather.");
        }

        var today = _weather.On(Now);
        DayWeather? forced = kind switch
        {
            "clear" => today with { Sky = Sky.Clear, High = 14, Low = 6, Icy = false, SnowLying = false, Heatwave = false },
            "sunny" => today with { Sky = Sky.Clear, High = 24, Low = 13, Icy = false, SnowLying = false, Heatwave = false },
            "cloudy" => today with { Sky = Sky.Cloudy, High = 12, Low = 6, Icy = false, SnowLying = false, Heatwave = false },
            "rain" => today with { Sky = Sky.Rain, High = 11, Low = 6, Icy = false, SnowLying = false, Heatwave = false },
            "heavy_rain" or "heavy" => today with { Sky = Sky.HeavyRain, High = 10, Low = 6, Icy = false, SnowLying = false, Heatwave = false },
            "fog" => today with { Sky = Sky.Fog, High = 7, Low = 2, Icy = false, SnowLying = false, Heatwave = false },
            "storm" or "thunderstorm" => today with { Sky = Sky.Thunderstorm, High = 24, Low = 15, Icy = false, SnowLying = false, Heatwave = false },
            "snow" => today with { Sky = Sky.Snow, High = 1, Low = -3, Icy = false, SnowLying = true, Heatwave = false },
            "ice" or "icy" => today with { Sky = Sky.Clear, High = 2, Low = -4, Icy = true, SnowLying = false, Heatwave = false },
            "heatwave" or "heat" => today with { Sky = Sky.Clear, High = 31, Low = 18, Icy = false, SnowLying = false, Heatwave = true },
            "off" => null,
            _ => today,
        };
        if (kind is not "off" && ReferenceEquals(forced, today))
        {
            return new CheatResult(false, "Usage: weather <clear, sunny, cloudy, rain, heavy_rain, fog, storm, snow, ice, heatwave or off>");
        }

        _weatherOverride = forced;
        return new CheatResult(true, forced is null ? $"Back to the forecast: {today.Describe()}." : $"Today is now: {forced.Describe()}.");
    }

    /// <summary>Today's weather, if this game has seasons.</summary>
    public DayWeather? Weather => WeatherOn(Now);

    /// <summary>Whether the weather is changing who comes in (it comes with seasons).</summary>
    public bool WeatherActive => SeasonsActive && _weather is not null;

    private IEnumerable<WeatherEffect> WeatherEffectsOn(DateTime date)
    {
        if (_weather is null)
        {
            yield break;
        }

        var effects = _seasonCalendar!.Profile.WeatherEffects;
        foreach (var key in _weather.On(date).EffectKeys())
        {
            if (effects.TryGetValue(key, out var effect))
            {
                yield return effect;
            }
        }
    }

    /// <summary>How many people come to A&amp;E today compared with an average day (1 without seasons).</summary>
    public double SeasonalVolume(DateTime date) =>
        SeasonsActive ? WeatherEffectsOn(date).Aggregate(_seasonCalendar!.Volume(date), (v, e) => v * e.Volume) : 1;

    /// <summary>The share of A&amp;E patients who need a bed (admitted or operated on) on this day, from the season's mix of illnesses.</summary>
    public double ExpectedBedShare(DateTime date)
    {
        double total = 0, beds = 0;
        foreach (var condition in Database.Conditions.Values.Where(c => !c.IsMaternity && !c.IsPlanned && CanArriveAtThisLevel(c)))
        {
            var weight = condition.Prevalence * SeasonalWeight(condition, date);
            total += weight;
            if (condition.Disposition is Disposition.Admit or Disposition.Surgery)
            {
                beds += weight;
            }
        }

        return total <= 0 ? 0 : beds / total;
    }

    /// <summary>The year at a glance: each month's busyness and share needing a bed, measured on the 15th.</summary>
    public IReadOnlyList<MonthOutlook> YearOutlook(int year) =>
        Enumerable.Range(1, 12).Select(m =>
        {
            var day = new DateTime(year, m, 15, 12, 0, 0);
            return new MonthOutlook(m, SeasonCalendar.SeasonOf(day), SeasonalVolume(day), ExpectedBedShare(day));
        }).ToList();

    /// <summary>The days ahead, today first: busyness (1 = an average day, weekdays included) and what is going round.</summary>
    public IReadOnlyList<DayOutlook> WeekOutlook(int days = 7)
    {
        var weekdayAverage = _arrivalProfile is { WeekdayMultipliers.Count: > 0 } p ? p.WeekdayMultipliers.Values.Average() : 1;
        var list = new List<DayOutlook>();
        for (var i = 0; i < days; i++)
        {
            var date = Now.Date.AddDays(i).AddHours(12);
            var weekday = _arrivalProfile?.WeekdayMultipliers.GetValueOrDefault(date.DayOfWeek.ToString().ToLowerInvariant(), 1) ?? 1;
            var notes = new List<string>();
            if (SeasonsActive && WeatherOn(date) is { } weather)
            {
                notes.AddRange(WeatherNotes(weather));
            }

            if (SeasonsActive)
            {
                foreach (var wave in _seasonCalendar!.Profile.Waves)
                {
                    var stage = _seasonCalendar.Stage(wave.Id, date);
                    if (stage != WaveStage.None)
                    {
                        notes.Add($"{wave.Name} {stage.ToString().ToLowerInvariant()}");
                    }
                }
            }

            list.Add(new DayOutlook(date.Date, SeasonCalendar.SeasonOf(date), SeasonalVolume(date) * weekday / weekdayAverage, ExpectedBedShare(date), notes) { Weather = WeatherOn(date) });
        }

        return list;
    }

    /// <summary>What a day's weather will do to A&amp;E, in a few words.</summary>
    public static IEnumerable<string> WeatherNotes(DayWeather weather)
    {
        if (weather.Icy)
        {
            yield return "ice: falls and fractures";
        }
        else if (weather.SnowLying || weather.Sky == Sky.Snow)
        {
            yield return "snow: fewer walk in, more falls";
        }

        if (weather.Heatwave)
        {
            yield return "heatwave: heat illness";
        }
        else if (weather.High >= 24)
        {
            yield return "hot: sunburn and heat";
        }

        if (weather.High <= 3 && !weather.Icy)
        {
            yield return "cold: hypothermia";
        }

        if (weather.Sky is Sky.HeavyRain or Sky.Fog)
        {
            yield return "road crashes up";
        }

        if (weather.Sky == Sky.Thunderstorm)
        {
            yield return "thunderstorm asthma";
        }
    }

    /// <summary>
    /// The pressure badge, from 1 (steady) to 4 (critical), like the NHS's OPEL levels. It looks at ward beds,
    /// how full A&amp;E is, ambulances waiting to hand over, patients in the corridor and the longest wait.
    /// It only tells the player how things are: it has no penalties.
    /// </summary>
    private void UpdatePressure()
    {
        if (Now < _nextPressureReading)
        {
            return;
        }

        _nextPressureReading = Now + PressureInterval;
        Pressure = ReadPressure();
    }

    public PressureReading ReadPressure()
    {
        var points = 0;
        var reasons = new List<string>();
        void Add(int p, string reason)
        {
            if (p > 0)
            {
                points += p;
                reasons.Add(reason);
            }
        }

        var (used, total) = Stats.WardBeds;
        if (total > 0)
        {
            var occupancy = (double)used / total;
            Add(occupancy >= 0.98 ? 3 : occupancy >= 0.92 ? 2 : occupancy >= 0.85 ? 1 : 0, $"Ward beds {occupancy:P0} full");
        }

        var spaces = _spaces.Values.Count(s => s.Kind is CareSpaceKind.Resus or CareSpaceKind.Majors or CareSpaceKind.Minors);
        if (spaces > 0)
        {
            var crowding = (double)Stats.PatientsInDepartment / spaces;
            Add(crowding >= 3 ? 3 : crowding >= 2 ? 2 : crowding >= 1.4 ? 1 : 0, $"{Stats.PatientsInDepartment} people in A&E for {spaces} cubicles");
        }

        Add(Stats.AmbulancesWaiting >= 3 ? 2 : Stats.AmbulancesWaiting >= 1 ? 1 : 0, $"{Stats.AmbulancesWaiting} ambulance{(Stats.AmbulancesWaiting == 1 ? "" : "s")} waiting to hand over");
        Add(Stats.InCorridor >= 4 ? 3 : Stats.InCorridor >= 1 ? 2 : 0, $"{Stats.InCorridor} patient{(Stats.InCorridor == 1 ? "" : "s")} in the corridor");
        Add(Stats.AwaitingBed >= 4 ? 2 : Stats.AwaitingBed >= 2 ? 1 : 0, $"{Stats.AwaitingBed} waiting in A&E for a ward bed");
        var longest = Stats.LongestWaitMinutes.Values.DefaultIfEmpty(0).Max();
        Add(longest >= 12 * 60 ? 2 : longest >= 4 * 60 ? 1 : 0, $"Longest wait {longest / 60:0.#} hours");

        var level = points switch
        {
            <= 1 => 1,
            <= 3 => 2,
            <= 5 => 3,
            _ => 4,
        };
        return new PressureReading(level, reasons);
    }
}
