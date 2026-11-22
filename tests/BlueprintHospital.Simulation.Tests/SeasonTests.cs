using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Seasons: the time of year changes how many come in and what with, and flu comes at a different time each winter.</summary>
public class SeasonTests
{
    private static SeasonCalendar Calendar(int seed)
    {
        var h = new SimulationHarness(seed, seasons: true);
        return h.Sim.Seasons!;
    }

    [Fact]
    public void The_seasons_data_loads_with_twelve_months_and_its_waves()
    {
        var h = new SimulationHarness(seasons: true);
        var profile = h.Database.Seasons.Values.Single();
        Assert.Equal(12, profile.Months.Count);
        Assert.Contains(profile.Waves, w => w.Id == "flu");
        Assert.Contains(profile.Waves, w => w.Id == "rsv");
    }

    [Fact]
    public void Flu_comes_every_winter_and_never_in_summer()
    {
        var calendar = Calendar(1234);
        for (var year = 2027; year <= 2032; year++)
        {
            var peak = Enumerable.Range(0, 150).Max(d => calendar.WaveLevel("flu", new DateTime(year, 11, 1).AddDays(d)));
            Assert.True(peak > 0.35, $"winter {year} had no flu to speak of ({peak:0.00})");
            Assert.Equal(0, calendar.WaveLevel("flu", new DateTime(year, 7, 15)));
        }
    }

    [Fact]
    public void Each_game_has_its_own_winters_but_the_same_game_always_has_the_same_ones()
    {
        var flu = Calendar(1).Profile.Waves.Single(w => w.Id == "flu");
        var starts = Enumerable.Range(1, 12).Select(seed => Calendar(seed).WaveYear(flu, 2027).Start).ToList();
        Assert.True(starts.Distinct().Count() >= 6, "flu should start on different days in different games");
        Assert.Equal(Calendar(7).WaveYear(flu, 2028), Calendar(7).WaveYear(flu, 2028));
        Assert.All(starts, s => Assert.InRange(s, new DateTime(2027, 11, 10), new DateTime(2027, 12, 28)));
    }

    [Fact]
    public void Summer_is_busiest_at_the_front_door_but_winter_patients_need_more_beds()
    {
        var h = new SimulationHarness(seasons: true);
        var year = h.Sim.YearOutlook(2027);
        var summer = year.Where(m => m.Season == Season.Summer).ToList();
        var winter = year.Where(m => m.Season == Season.Winter).ToList();
        Assert.True(summer.Average(m => m.Volume) > winter.Average(m => m.Volume) * 1.02, "summer should be busier");
        Assert.True(winter.Average(m => m.BedShare) > summer.Average(m => m.BedShare) * 1.15,
            $"winter should send more to a bed ({winter.Average(m => m.BedShare):P1} vs {summer.Average(m => m.BedShare):P1})");

        // So although fewer come in, more are admitted: winter is the bed crunch.
        double Admitted(IEnumerable<MonthOutlook> months) => months.Average(m => m.Volume * m.BedShare);
        Assert.True(Admitted(winter) > Admitted(summer) * 1.1, $"winter admissions {Admitted(winter):0.000} vs summer {Admitted(summer):0.000}");
    }

    [Fact]
    public void Sunburn_and_heat_come_in_summer_and_hypothermia_in_winter()
    {
        var h = new SimulationHarness(seasons: true);
        int Count(DateTime when, params string[] ids)
        {
            var generator = new PatientGenerator(h.Database, new GameRandom(5)) { Seasonal = h.Sim.SeasonalWeight };
            return Enumerable.Range(0, 3000).Count(_ => ids.Contains(generator.Generate(when).Condition.Id));
        }

        var july = new DateTime(2027, 7, 20, 14, 0, 0);
        var january = new DateTime(2027, 1, 20, 14, 0, 0);
        Assert.True(Count(july, "sunburn", "heat_exhaustion", "heatstroke") > 5 * Math.Max(1, Count(january, "sunburn", "heat_exhaustion", "heatstroke")));
        Assert.True(Count(january, "hypothermia") > 5 * Math.Max(1, Count(july, "hypothermia")));
    }

    [Fact]
    public void Without_seasons_every_day_is_the_same()
    {
        var h = new SimulationHarness();
        Assert.Null(h.Sim.Seasons);
        Assert.Equal(1, h.Sim.SeasonalVolume(new DateTime(2027, 7, 20)));
        Assert.Equal(1, h.Sim.SeasonalWeight(h.Database.Conditions["influenza"], new DateTime(2028, 1, 10)));
    }

    [Fact]
    public void A_career_walk_in_centre_has_no_seasons_until_level_two()
    {
        var h = new SimulationHarness(type: GameType.Career, seasons: true);
        Assert.False(h.Sim.SeasonsActive);
        h.Sim.SetLevel(2);
        Assert.True(h.Sim.SeasonsActive);
    }

    [Fact]
    public void The_week_ahead_counts_mondays_as_busier()
    {
        var h = new SimulationHarness(seasons: true);
        var week = h.Sim.WeekOutlook();
        Assert.Equal(7, week.Count);
        Assert.Equal(DayOfWeek.Monday, week[0].Date.DayOfWeek);
        Assert.True(week[0].Busyness > week[1].Busyness);
    }

    [Fact]
    public void Seasons_and_their_seed_survive_save_and_load()
    {
        var from = new SimulationHarness(seed: 4242, seasons: true);
        var json = SaveSerializer.Serialize(new SaveGame { Clock = from.Clock.CaptureState(), Layout = from.Sim.CaptureLayout(), People = from.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        Assert.True(to.Sim.SeasonsEnabled);
        Assert.Equal(4242, to.Sim.SeasonSeed);
        var flu = to.Sim.Seasons!.Profile.Waves.Single(w => w.Id == "flu");
        Assert.Equal(from.Sim.Seasons!.WaveYear(flu, 2027), to.Sim.Seasons.WaveYear(flu, 2027));
    }

    [Fact]
    public void A_quiet_hospital_is_steady()
    {
        var h = new SimulationHarness(seasons: true);
        h.RunFor(TimeSpan.FromMinutes(20));
        Assert.Equal(1, h.Sim.Pressure.Level);
        Assert.Equal("Steady", h.Sim.Pressure.Name);
    }

    [Fact]
    public void British_weather_snow_some_winters_heatwaves_some_summers_and_rain_all_year()
    {
        var h = new SimulationHarness(seed: 11, seasons: true);
        var days = Enumerable.Range(0, 365 * 6).Select(d => h.Sim.WeatherOn(new DateTime(2027, 1, 1).AddDays(d))!).ToList();
        Assert.Contains(days, d => d.Sky == Sky.Snow);
        Assert.Contains(days, d => d.Icy);
        Assert.Contains(days, d => d.Heatwave);
        Assert.DoesNotContain(days, d => d.Sky == Sky.Snow && d.Date.Month is >= 5 and <= 9);
        Assert.DoesNotContain(days, d => d.Heatwave && d.Date.Month is <= 3 or >= 11);
        var wet = days.Count(d => d.Wet || d.Sky == Sky.Snow) / (double)days.Count;
        Assert.InRange(wet, 0.25, 0.5);
        var julyHighs = days.Where(d => d.Date.Month == 7).Average(d => d.High);
        var januaryHighs = days.Where(d => d.Date.Month == 1).Average(d => d.High);
        Assert.InRange(julyHighs, 19, 25);
        Assert.InRange(januaryHighs, 4, 10);
    }

    [Fact]
    public void Ice_brings_in_broken_hips_and_a_heatwave_brings_heatstroke()
    {
        var h = new SimulationHarness(seed: 11, seasons: true);
        var all = Enumerable.Range(0, 365 * 6).Select(d => h.Sim.WeatherOn(new DateTime(2027, 1, 1, 12, 0, 0).AddDays(d))!).ToList();
        var icy = all.First(d => d.Icy).Date.AddHours(12);
        var heat = all.First(d => d.Heatwave).Date.AddHours(12);
        var hip = h.Database.Conditions["fractured_neck_of_femur"];
        var heatstroke = h.Database.Conditions["heatstroke"];
        Assert.True(h.Sim.SeasonalWeight(hip, icy) > 1.8 * h.Sim.Seasons!.ConditionWeight(hip.Id, icy));
        Assert.True(h.Sim.SeasonalWeight(heatstroke, heat) > 3 * h.Sim.Seasons.ConditionWeight(heatstroke.Id, heat));
    }

    [Fact]
    public void The_same_game_has_the_same_weather()
    {
        var a = new SimulationHarness(seed: 5, seasons: true).Sim;
        var b = new SimulationHarness(seed: 5, seasons: true).Sim;
        var c = new SimulationHarness(seed: 6, seasons: true).Sim;
        var dates = Enumerable.Range(0, 60).Select(d => new DateTime(2027, 6, 1).AddDays(d)).ToList();
        Assert.Equal(dates.Select(d => a.WeatherOn(d)), dates.Select(d => b.WeatherOn(d)));
        Assert.NotEqual(dates.Select(d => a.WeatherOn(d)!.Sky), dates.Select(d => c.WeatherOn(d)!.Sky));
    }

    [Fact]
    public void Short_days_in_winter_and_long_light_evenings_in_summer()
    {
        var (winterRise, winterSet) = WeatherCalendar.Daylight(new DateTime(2027, 12, 21));
        var (summerRise, summerSet) = WeatherCalendar.Daylight(new DateTime(2027, 6, 21));
        Assert.InRange(winterRise, 7.7, 8.3);
        Assert.InRange(winterSet, 15.7, 16.3);
        Assert.InRange(summerRise, 4.5, 5.2);
        Assert.InRange(summerSet, 21.0, 21.7);
    }

    [Fact]
    public void The_weather_cheat_sets_today_and_off_goes_back_to_the_forecast()
    {
        var h = new SimulationHarness(seed: 3, seasons: true);
        var forecast = h.Sim.Weather!;
        var snow = h.Sim.RunCheat("weather snow");
        Assert.True(snow.Succeeded, snow.Message);
        Assert.Equal(Sky.Snow, h.Sim.Weather!.Sky);
        Assert.True(h.Sim.Weather.SnowLying);
        Assert.Equal("Snow 1°C", h.Sim.Weather.Brief());
        Assert.False(h.Sim.RunCheat("weather sideways").Succeeded);
        Assert.True(h.Sim.RunCheat("weather off").Succeeded);
        Assert.Equal(forecast, h.Sim.Weather);
        Assert.False(new SimulationHarness(seed: 3).Sim.RunCheat("weather rain").Succeeded);
    }
}
