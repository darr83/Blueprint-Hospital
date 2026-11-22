using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>One day's weather.</summary>
public sealed record DayWeather(DateTime Date, Sky Sky, double High, double Low, bool Icy, bool SnowLying, bool Heatwave)
{
    /// <summary>Frost on the ground in the morning.</summary>
    public bool Frost => Low <= 0;

    public bool Wet => Sky is Sky.Rain or Sky.HeavyRain or Sky.Thunderstorm;

    /// <summary>In words, e.g. "Heavy rain, 9°C" or "Snow lying, icy, -1°C".</summary>
    public string Describe()
    {
        var sky = Sky switch
        {
            Sky.Clear => High >= 22 ? "Sunny" : "Clear",
            Sky.Cloudy => "Cloudy",
            Sky.Rain => "Rain",
            Sky.HeavyRain => "Heavy rain",
            Sky.Fog => "Fog",
            Sky.Thunderstorm => "Thunderstorms",
            _ => "Snow",
        };
        if (Heatwave)
        {
            sky = "Heatwave";
        }
        else if (SnowLying && Sky != Sky.Snow)
        {
            sky += ", snow lying";
        }

        if (Icy)
        {
            sky += ", icy";
        }

        return $"{sky}, {High:0}°C";
    }

    /// <summary>A few words for the top bar, e.g. "Rain 9°C", "Icy -1°C" or "Heatwave 29°C".</summary>
    public string Brief()
    {
        var word = Heatwave ? "Heatwave"
            : Icy ? "Icy"
            : Sky == Sky.Snow ? "Snow"
            : SnowLying ? "Snow lying"
            : Sky switch
            {
                Sky.Clear => High >= 22 ? "Sunny" : "Clear",
                Sky.Cloudy => "Cloudy",
                Sky.Rain => "Rain",
                Sky.HeavyRain => "Heavy rain",
                Sky.Fog => "Fog",
                _ => "Storms",
            };
        return $"{word} {High:0}°C";
    }

    /// <summary>The weather's kinds that have effects (see <see cref="WeatherEffectKeys"/>).</summary>
    public IEnumerable<string> EffectKeys()
    {
        if (Icy)
        {
            yield return WeatherEffectKeys.Icy;
        }

        if (SnowLying || Sky == Sky.Snow)
        {
            yield return WeatherEffectKeys.Snow;
        }

        if (High <= 3)
        {
            yield return WeatherEffectKeys.Cold;
        }

        if (High >= 24)
        {
            yield return WeatherEffectKeys.Hot;
        }

        if (Heatwave)
        {
            yield return WeatherEffectKeys.Heatwave;
        }

        switch (Sky)
        {
            case Sky.Rain:
                yield return WeatherEffectKeys.Rain;
                break;
            case Sky.HeavyRain:
                yield return WeatherEffectKeys.HeavyRain;
                break;
            case Sky.Fog:
                yield return WeatherEffectKeys.Fog;
                break;
            case Sky.Thunderstorm:
                yield return WeatherEffectKeys.Thunderstorm;
                break;
        }
    }
}

/// <summary>
/// The weather, day by day. Each year is worked out once from the seed: temperatures wander around the
/// month's normal (warm and cold spells last a few days), the sky tends to stay as it was, rain falls as
/// snow when it is cold enough, snow lies until it thaws, wet ground freezes into ice on frosty nights,
/// and three days of 27°C or more make a heatwave. The same game always has the same weather.
/// </summary>
public sealed class WeatherCalendar(SeasonProfile profile, int seed)
{
    /// <summary>The high that counts towards a heatwave (the Met Office's threshold for much of England).</summary>
    public const double HeatwaveHigh = 27;

    private readonly Dictionary<int, DayWeather[]> _years = new();

    public DayWeather On(DateTime date)
    {
        if (!_years.TryGetValue(date.Year, out var days))
        {
            days = _years[date.Year] = MakeYear(date.Year);
        }

        return days[date.DayOfYear - 1];
    }

    private DayWeather[] MakeYear(int year)
    {
        var random = new GameRandom(unchecked((seed * 7349) ^ (year * 104_729) ^ 0x5EA5));
        var count = DateTime.IsLeapYear(year) ? 366 : 365;
        var days = new DayWeather[count];
        double anomaly = 0;
        var sky = Sky.Cloudy;
        var snowLying = false;
        var wetYesterday = false;
        var hotDays = 0;
        for (var i = 0; i < count; i++)
        {
            var date = new DateTime(year, 1, 1).AddDays(i);
            anomaly = (0.75 * anomaly) + (random.Normal(0, 2.2));
            var (low, high) = Normal(date);
            high += anomaly;
            low += anomaly * 0.8;

            // The sky tends to stay as it was for a day or two.
            var month = profile.Months[date.Month - 1];
            if (i == 0 || random.NextDouble() > 0.5 || !Allowed(sky, high))
            {
                var choices = month.Sky
                    .Select(kv => (Ok: SeasonMonth.TryParseSky(kv.Key, out var s), Sky: s, Weight: kv.Value))
                    .Where(c => c.Ok && c.Weight > 0 && Allowed(c.Sky, high))
                    .ToList();
                sky = choices.Count == 0 ? Sky.Cloudy : random.PickWeighted(choices, c => c.Weight).Sky;
            }

            var today = sky;
            if (today is Sky.Rain or Sky.HeavyRain && high <= 2.5)
            {
                today = Sky.Snow;
            }

            snowLying = today == Sky.Snow || (snowLying && high <= 4);
            var icy = low <= -1 && (wetYesterday || snowLying);
            hotDays = high >= HeatwaveHigh ? hotDays + 1 : 0;
            var heatwave = hotDays >= 3;
            days[i] = new DayWeather(date, today, Math.Round(high, 1), Math.Round(low, 1), icy, snowLying, heatwave);
            wetYesterday = today is Sky.Rain or Sky.HeavyRain or Sky.Thunderstorm or Sky.Snow;
        }

        // A heatwave counts from its first hot day, not its third.
        for (var i = 0; i < count; i++)
        {
            if (days[i].Heatwave && i >= 2)
            {
                days[i - 1] = days[i - 1] with { Heatwave = true };
                days[i - 2] = days[i - 2] with { Heatwave = true };
            }
        }

        return days;
    }

    private static bool Allowed(Sky sky, double high) => sky switch
    {
        Sky.Thunderstorm => high >= 18,
        Sky.Fog => high <= 16,
        _ => true,
    };

    /// <summary>The usual low and high for the day, blended between the middles of the months.</summary>
    private (double Low, double High) Normal(DateTime date)
    {
        var month = date.Month - 1;
        var middle = new DateTime(date.Year, date.Month, 15);
        var other = date < middle ? (month + 11) % 12 : (month + 1) % 12;
        var otherMiddle = date < middle ? middle.AddMonths(-1) : middle.AddMonths(1);
        var t = Math.Abs((date - middle).TotalDays) / Math.Abs((otherMiddle - middle).TotalDays);
        var a = profile.Months[month].Temperature;
        var b = profile.Months[other].Temperature;
        return ((a[0] * (1 - t)) + (b[0] * t), (a[1] * (1 - t)) + (b[1] * t));
    }

    /// <summary>
    /// Sunrise and sunset in hours on the clock (British Summer Time from the last Sunday in March to the last
    /// Sunday in October), for a UK hospital: about 8am to 4pm at Christmas and 4:45am to 9:20pm at midsummer.
    /// </summary>
    public static (double Sunrise, double Sunset) Daylight(DateTime date)
    {
        var length = 12.3 + (4.35 * Math.Cos(2 * Math.PI * (date.DayOfYear - 172) / 365.25));
        var noon = IsSummerTime(date) ? 13.05 : 12.05;
        return (noon - (length / 2), noon + (length / 2));
    }

    public static bool IsSummerTime(DateTime date)
    {
        static DateTime LastSunday(int year, int month)
        {
            var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            while (d.DayOfWeek != DayOfWeek.Sunday)
            {
                d = d.AddDays(-1);
            }

            return d;
        }

        return date.Date >= LastSunday(date.Year, 3) && date.Date < LastSunday(date.Year, 10);
    }
}
