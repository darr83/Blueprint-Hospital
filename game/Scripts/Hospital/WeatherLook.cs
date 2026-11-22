using System;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Simulation;
using Godot;
using Sky = BlueprintHospital.Simulation.Data.Models.Sky;

namespace BlueprintHospital.Hospital;

/// <summary>How warmly people are dressed for the day.</summary>
public enum Climate
{
    Cold,
    Mild,
    Warm,
    Hot,
}

/// <summary>
/// What today's weather looks like, shared by the 2D and 3D views: the grounds under snow, frost or a
/// summer's parched grass, wet roads, rain or snow falling in spells, morning fog, a grey sky dimming
/// the light, and when the sun comes up and goes down. Worked out from the simulation's weather (none
/// when the game has no seasons, and then everything looks as it always did).
/// </summary>
public static class WeatherLook
{
    private static readonly Color SnowWhite = new("#EEF2F6");
    private static readonly Color Straw = new("#B9B27A");
    private static readonly Color FrostWhite = new("#DCE6EA");

    private static DateTime _hour = DateTime.MinValue;

    /// <summary>Today's weather, or null without seasons.</summary>
    public static DayWeather? Today { get; private set; }

    /// <summary>Tomorrow's weather, for the change at midnight.</summary>
    public static DayWeather? Tomorrow { get; private set; }

    /// <summary>0 to 1: snow lying on the grass (1 is a white blanket).</summary>
    public static float Snow { get; private set; }

    /// <summary>0 to 1: grass dried out by a hot spell.</summary>
    public static float Parched { get; private set; }

    /// <summary>0 to 1: frost on the grass first thing on a cold morning.</summary>
    public static float Frost { get; private set; }

    /// <summary>Whether the roads and paving shine wet.</summary>
    public static bool WetGround { get; private set; }

    /// <summary>0 to 1, how hard it is raining now (0 when dry or snowing).</summary>
    public static float Rain { get; private set; }

    /// <summary>0 to 1, how hard it is snowing now.</summary>
    public static float Snowfall { get; private set; }

    /// <summary>0 to 1, how thick the fog is now.</summary>
    public static float Fog { get; private set; }

    /// <summary>0 to 1, how grey the sky is (dims the sunlight).</summary>
    public static float Cloud { get; private set; }

    /// <summary>0 to 1, how much the heat warms the light.</summary>
    public static float Heat { get; private set; }

    public static Climate Climate { get; private set; } = Climate.Mild;

    /// <summary>Changes whenever the look of the ground changes (snow, frost, parched grass or wet roads), so the views know to repaint it.</summary>
    public static int GroundVersion { get; private set; }

    /// <summary>Follows the simulation's clock: call every frame (cheap: it only works anything out once an hour of game time).</summary>
    public static void Update(HospitalSimulation sim)
    {
        var now = sim.Now;
        var weather = sim.SeasonsActive ? sim.Weather : null;
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        if (weather != Today || hour != _hour)
        {
            _hour = hour;
            Today = weather;
            Tomorrow = weather is null ? null : sim.WeatherOn(now.Date.AddDays(1));
            var ground = (Snow, Parched, Frost, WetGround);
            Work(weather, now.Hour);
            if (ground != (Snow, Parched, Frost, WetGround))
            {
                GroundVersion++;
            }
        }

        // Rain and snow come and go in spells, eased across the hour.
        if (weather is not null)
        {
            var t = (float)(now.Minute / 60.0);
            var (rain, snow) = Falling(weather, now.Hour);
            var (rainNext, snowNext) = now.Hour == 23 ? (Tomorrow is null ? (0f, 0f) : Falling(Tomorrow, 0)) : Falling(weather, now.Hour + 1);
            Rain = Mathf.Lerp(rain, rainNext, t);
            Snowfall = Mathf.Lerp(snow, snowNext, t);
            Fog = Mathf.Lerp(FogAt(weather, now.Hour), FogAt(weather, now.Hour + 1), t);
        }
        else
        {
            Rain = Snowfall = Fog = 0;
        }
    }

    /// <summary>Clears the weather (a new game, or a game without seasons).</summary>
    public static void Reset()
    {
        Today = Tomorrow = null;
        _hour = DateTime.MinValue;
        Work(null, 12);
        Rain = Snowfall = Fog = 0;
        GroundVersion++;
    }

    private static void Work(DayWeather? weather, int hour)
    {
        if (weather is null)
        {
            Snow = Parched = Frost = Cloud = Heat = 0;
            WetGround = false;
            Climate = Climate.Mild;
            return;
        }

        Snow = weather.SnowLying ? (weather.Sky == Sky.Snow || weather.High <= 1 ? 1f : 0.7f) : 0f;
        Parched = weather.Heatwave ? 1f : weather.High >= 25 ? 0.5f : 0f;
        Frost = !weather.SnowLying && weather.Frost && hour < 11 ? Math.Clamp((float)(-weather.Low + 1) / 4f, 0.35f, 1f) * (hour < 9 ? 1f : 0.5f) : 0f;
        WetGround = weather.Wet || (weather.Icy && hour < 12);
        Cloud = weather.Sky switch
        {
            Sky.Clear => 0f,
            Sky.Cloudy => 0.35f,
            Sky.Rain => 0.5f,
            Sky.HeavyRain => 0.65f,
            Sky.Thunderstorm => 0.7f,
            Sky.Fog => 0.4f,
            _ => 0.45f,
        };
        Heat = weather.Heatwave ? 1f : weather.High >= 24 ? 0.5f : 0f;
        Climate = weather.High <= 9 ? Climate.Cold : weather.High >= 24 ? Climate.Hot : weather.High >= 18 ? Climate.Warm : Climate.Mild;
    }

    /// <summary>How hard it rains and snows in a given hour of the day: in spells, and thunderstorms come in the afternoon.</summary>
    private static (float Rain, float Snow) Falling(DayWeather weather, int hour)
    {
        var roll = (float)Hash(weather.Date, hour);
        return weather.Sky switch
        {
            Sky.Rain => (roll < 0.55 ? 0.45f + (roll * 0.6f) : 0f, 0f),
            Sky.HeavyRain => (roll < 0.75 ? 0.8f + (roll * 0.25f) : 0.25f, 0f),
            Sky.Thunderstorm => (hour is >= 13 and <= 20 ? (roll < 0.7 ? 1f : 0.4f) : roll < 0.45 ? 0.55f : 0f, 0f),
            Sky.Snow => (0f, roll < 0.6 ? 0.5f + (roll * 0.8f) : 0.1f),
            _ => (0f, 0f),
        };
    }

    /// <summary>Fog is thickest at dawn and lifts towards midday.</summary>
    private static float FogAt(DayWeather weather, int hour)
    {
        if (weather.Sky != Sky.Fog)
        {
            return 0;
        }

        return hour <= 9 || hour >= 21 ? 1f : hour >= 13 && hour < 18 ? 0.35f : hour < 13 ? 1f - ((hour - 9) * 0.16f) : 0.35f + ((hour - 18) * 0.2f);
    }

    private static double Hash(DateTime date, int hour)
    {
        unchecked
        {
            var h = (uint)((date.DayOfYear * 73856093) ^ (date.Year * 19349663) ^ (hour * 83492791));
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535.0;
        }
    }

    /// <summary>
    /// How much daylight there is outside at this time: 0 at night, 1 by day, easing over the hour either
    /// side of sunrise and sunset. With seasons that follows the time of year (8am to 4pm at Christmas,
    /// 5am to 9pm at midsummer); without, dawn is 6 to 8 and dusk 7 to 9 in the evening as it always was.
    /// </summary>
    public static float Daylight(DateTime now)
    {
        var hour = (float)now.TimeOfDay.TotalHours;
        if (Today is null)
        {
            return Mathf.SmoothStep(6f, 8f, hour) * (1f - Mathf.SmoothStep(19f, 21f, hour));
        }

        var (rise, set) = WeatherCalendar.Daylight(now);
        return Mathf.SmoothStep((float)rise - 0.75f, (float)rise + 0.75f, hour) * (1f - Mathf.SmoothStep((float)set - 0.75f, (float)set + 0.75f, hour));
    }

    /// <summary>Grass as the weather leaves it: under snow, white with frost, or dried to straw.</summary>
    public static Color Grass(Color grass)
    {
        var c = grass;
        if (Parched > 0)
        {
            c = c.Lerp(Straw, Parched * 0.55f);
        }

        if (Frost > 0)
        {
            c = c.Lerp(FrostWhite, Frost * 0.55f);
        }

        if (Snow > 0)
        {
            c = c.Lerp(SnowWhite, Snow * 0.92f);
        }

        return c;
    }

    /// <summary>A road, car park or path as the weather leaves it: darker when wet, edged with slush when snow is lying.</summary>
    public static Color Hard(Color surface, bool road)
    {
        var c = surface;
        if (WetGround)
        {
            c = c.Darkened(road ? 0.2f : 0.14f);
        }

        if (Snow > 0)
        {
            // Gritted roads stay dark; paving keeps a thin layer.
            c = c.Lerp(SnowWhite, Snow * (road ? 0.12f : 0.55f));
        }

        return c;
    }

    /// <summary>Text for the top bar, e.g. "Heavy rain, 9°C".</summary>
    public static string? Describe() => Today?.Describe();
}
