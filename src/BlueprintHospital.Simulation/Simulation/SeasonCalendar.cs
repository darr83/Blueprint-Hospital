using BlueprintHospital.Simulation.Data.Models;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>How far a yearly outbreak such as flu has got, in words the player sees.</summary>
public enum WaveStage
{
    None,
    Low,
    Moderate,
    High,
}

/// <summary>
/// Works out, for any day, how the time of year changes who comes to A&amp;E: the month's busyness,
/// how common each illness is, and where this year's flu and RSV waves are. Each year's waves start
/// on a different day and are a different strength, chosen from <see cref="Seed"/>, so the same game
/// always has the same winters and no two games are alike. Pure: no state but the seed.
/// </summary>
public sealed class SeasonCalendar(SeasonProfile profile, int seed)
{
    public SeasonProfile Profile { get; } = profile;

    public int Seed { get; } = seed;

    public static Season SeasonOf(DateTime date) => date.Month switch
    {
        12 or 1 or 2 => Season.Winter,
        3 or 4 or 5 => Season.Spring,
        6 or 7 or 8 => Season.Summer,
        _ => Season.Autumn,
    };

    public static string Name(Season season) => season switch
    {
        Season.Winter => "Winter",
        Season.Spring => "Spring",
        Season.Summer => "Summer",
        _ => "Autumn",
    };

    /// <summary>How many people come to A&amp;E on this day compared with an average day (1).</summary>
    public double Volume(DateTime date)
    {
        var volume = Monthly(date, m => Profile.Months[m].Volume);
        foreach (var wave in Profile.Waves)
        {
            volume *= 1 + (wave.ExtraVolume * WaveLevel(wave, date));
        }

        return volume;
    }

    /// <summary>How much commoner an illness is on this day than usual (1 = as usual).</summary>
    public double ConditionWeight(string conditionId, DateTime date)
    {
        var weight = Profile.ConditionMonths.TryGetValue(conditionId, out var months) ? Monthly(date, m => months[m]) : 1;
        foreach (var wave in Profile.Waves)
        {
            if (wave.Conditions.TryGetValue(conditionId, out var peak))
            {
                weight *= 1 + ((peak - 1) * WaveLevel(wave, date));
            }
        }

        return weight;
    }

    /// <summary>How strong a wave is on this day: 0 before it starts and after it ends, about 1 at the peak of a typical year.</summary>
    public double WaveLevel(string waveId, DateTime date) =>
        Profile.Waves.FirstOrDefault(w => w.Id == waveId) is { } wave ? WaveLevel(wave, date) : 0;

    public double WaveLevel(SeasonWave wave, DateTime date)
    {
        // A wave that started last winter may still be going.
        var level = 0.0;
        foreach (var year in new[] { date.Year - 1, date.Year })
        {
            var (start, strength) = WaveYear(wave, year);
            var days = (date - start).TotalDays;
            level = Math.Max(level, strength * Shape(days, wave.RiseDays, wave.FallDays));
        }

        return level;
    }

    /// <summary>Where a wave is, in words.</summary>
    public WaveStage Stage(string waveId, DateTime date) => WaveLevel(waveId, date) switch
    {
        < 0.1 => WaveStage.None,
        < 0.4 => WaveStage.Low,
        < 0.8 => WaveStage.Moderate,
        _ => WaveStage.High,
    };

    /// <summary>Whether a wave is building (true), dying away (false), or not on (null).</summary>
    public bool? Rising(string waveId, DateTime date)
    {
        if (WaveLevel(waveId, date) < 0.05)
        {
            return null;
        }

        return WaveLevel(waveId, date.AddDays(3)) >= WaveLevel(waveId, date);
    }

    /// <summary>The day this year's wave starts and how strong it is (1 = a typical year).</summary>
    public (DateTime Start, double Strength) WaveYear(SeasonWave wave, int year)
    {
        SeasonWave.TryParseDay(wave.EarliestStart, out var m1, out var d1);
        SeasonWave.TryParseDay(wave.LatestStart, out var m2, out var d2);
        var earliest = new DateTime(year, m1, d1);
        var latest = new DateTime(year, m2, d2);
        if (latest < earliest)
        {
            latest = latest.AddYears(1);
        }

        var random = new Core.GameRandom(Hash(wave.Id, year));
        var start = earliest.AddDays(Math.Round(random.NextDouble() * (latest - earliest).TotalDays));
        var strength = wave.Strength[0] + (random.NextDouble() * (wave.Strength[1] - wave.Strength[0]));
        return (start, strength);
    }

    private int Hash(string id, int year)
    {
        unchecked
        {
            var h = (Seed * 397) ^ (year * 7919);
            foreach (var c in id)
            {
                h = (h * 31) + c;
            }

            return h;
        }
    }

    /// <summary>Rises smoothly from 0 to 1 over <paramref name="rise"/> days, then falls smoothly back to 0 over <paramref name="fall"/>.</summary>
    private static double Shape(double days, int rise, int fall)
    {
        if (days <= 0 || days >= rise + fall)
        {
            return 0;
        }

        return days < rise ? Smooth(days / rise) : 1 - Smooth((days - rise) / fall);
    }

    private static double Smooth(double t) => t * t * (3 - (2 * t));

    /// <summary>A monthly figure for any day, blended between the middles of the months so it never jumps at midnight on the 1st.</summary>
    private static double Monthly(DateTime date, Func<int, double> value)
    {
        var month = date.Month - 1;
        var middle = new DateTime(date.Year, date.Month, 15);
        var otherMonth = date < middle ? (month + 11) % 12 : (month + 1) % 12;
        var otherMiddle = date < middle ? middle.AddMonths(-1) : middle.AddMonths(1);
        var t = Math.Abs((date - middle).TotalDays) / Math.Abs((otherMiddle - middle).TotalDays);
        return (value(month) * (1 - t)) + (value(otherMonth) * t);
    }
}
