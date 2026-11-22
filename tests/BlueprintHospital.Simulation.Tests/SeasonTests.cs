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
}
