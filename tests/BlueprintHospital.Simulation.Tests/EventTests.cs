using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Xunit;

namespace BlueprintHospital.Simulation.Tests;

public class EventTests
{
    [Fact]
    public void Moving_dates_fall_on_the_right_days()
    {
        Assert.Equal(new DateTime(2027, 3, 28), EventDates.Easter(2027));
        Assert.Equal(new DateTime(2028, 4, 16), EventDates.Easter(2028));
        Assert.True(EventDates.TryResolve("last-mon-05", 2027, out var spring));
        Assert.Equal(new DateTime(2027, 5, 31), spring);
        Assert.True(EventDates.TryResolve("first-wed-08", 2027, out var changeover));
        Assert.Equal(new DateTime(2027, 8, 4), changeover);
        Assert.True(EventDates.TryResolve("easter-2", 2027, out var goodFriday));
        Assert.Equal(new DateTime(2027, 3, 26), goodFriday);
        Assert.False(EventDates.IsValidRule("someday"));
    }

    [Fact]
    public void New_Years_Eve_is_busy_with_drink_until_the_early_hours()
    {
        var h = new SimulationHarness(seed: 9, seasons: true);
        var sim = h.Sim;
        var drunk = h.Database.Conditions["alcohol_intoxication"];
        var evening = new DateTime(2027, 12, 31, 23, 0, 0);
        var lateNight = new DateTime(2028, 1, 1, 3, 0, 0);
        var afternoon = new DateTime(2027, 12, 31, 13, 0, 0);
        Assert.True(sim.SeasonalWeight(drunk, evening) > 6 * sim.SeasonalWeight(drunk, afternoon));
        Assert.True(sim.SeasonalWeight(drunk, lateNight) > 6 * sim.SeasonalWeight(drunk, afternoon));
        Assert.True(sim.SeasonalVolume(evening) > 1.2 * sim.SeasonalVolume(afternoon));
        Assert.True(sim.PoliceChance("nasal_fracture", evening) > 0.3);
        Assert.Equal(0, sim.PoliceChance("nasal_fracture", new DateTime(2027, 12, 28, 13, 0, 0)));
        Assert.Contains(sim.CalendarEvents!.ActiveAt(lateNight), d => d.Event.Id == "new_years_eve");

        // Without seasons none of it applies.
        var plain = new SimulationHarness(seed: 9).Sim;
        Assert.Equal(1, plain.SeasonalWeight(drunk, evening));
        Assert.Equal(0, plain.PoliceChance("nasal_fracture", evening));
    }

    [Fact]
    public void Big_matches_and_the_festival_come_on_the_same_days_for_the_same_game()
    {
        var a = new SimulationHarness(seed: 21, seasons: true).Sim.CalendarEvents!;
        var b = new SimulationHarness(seed: 21, seasons: true).Sim.CalendarEvents!;
        var start = new DateTime(2027, 1, 1);
        var matchesA = a.Upcoming(start, 365).Where(d => d.Event.Id == "big_match").Select(d => d.Day).ToList();
        var matchesB = b.Upcoming(start, 365).Where(d => d.Event.Id == "big_match").Select(d => d.Day).ToList();
        Assert.Equal(matchesA, matchesB);
        Assert.InRange(matchesA.Count, 6, 25);
        Assert.All(matchesA, d => Assert.Equal(DayOfWeek.Saturday, d.DayOfWeek));
        Assert.DoesNotContain(matchesA, d => d.Month is 6 or 7);
        var festival = a.Upcoming(start, 365).Single(d => d.Event.Id == "music_festival");
        Assert.InRange(festival.Day, new DateTime(2027, 6, 10), new DateTime(2027, 8, 20));
        Assert.Equal("christmas", new SimulationHarness(seed: 21, seasons: true, start: new DateTime(2027, 12, 20, 10, 0, 0)).Sim.DecorationsNow);
    }

    [Fact]
    public void Someone_brought_in_by_the_police_has_an_officer_with_them_in_AandE()
    {
        var h = new SimulationHarness(seed: 4, seasons: true);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "nasal_fracture", age: 28);
        patient.PoliceEscort = true;
        h.RunUntil(() => h.Sim.ParentOf(patient) is { Police: true }, TimeSpan.FromMinutes(10), "a police officer with them");
        var officer = h.Sim.ParentOf(patient)!;
        Assert.Equal("PC", officer.FirstName);
        Assert.Contains(patient.Timeline, e => e.Text.StartsWith("Brought in by the police", StringComparison.Ordinal));
        h.RunFor(TimeSpan.FromMinutes(5));
        Assert.True(Math.Max(Math.Abs(officer.Tile.X - patient.Tile.X), Math.Abs(officer.Tile.Y - patient.Tile.Y)) <= 3);
    }

    [Fact]
    public void Staff_phone_in_sick_more_when_flu_is_about_but_never_a_whole_team()
    {
        var h = new SimulationHarness(seed: 5, seasons: true);
        h.StaffToRecommended();
        var sim = h.Sim;
        var flu = sim.Seasons!.Profile.Waves.Single(w => w.Id == "flu");
        var (fluStart, _) = sim.Seasons.WaveYear(flu, 2027);
        var peak = HospitalSimulation.ShiftStart(fluStart.AddDays(flu.RiseDays).AddHours(9));
        var june = HospitalSimulation.ShiftStart(new DateTime(2027, 6, 9, 9, 0, 0));
        Assert.True(sim.SicknessChance(peak) > sim.SicknessChance(june) + 0.02);

        int Away(DateTime start, StaffShift shift) =>
            Enumerable.Range(0, 60).Sum(d => sim.Staff.Count(s => s.Shift == shift && sim.AbsenceOf(s, start.AddDays(d)) != StaffAbsence.None));
        Assert.True(Away(peak.AddDays(-30), StaffShift.Day) > Away(june, StaffShift.Day));

        for (var d = 0; d < 60; d++)
        {
            var start = peak.AddDays(d - 30);
            foreach (var team in sim.Staff.Where(s => s.Shift == StaffShift.Day).GroupBy(s => (s.Role, s.BaseRoomId)))
            {
                Assert.True(team.Count(s => sim.AbsenceOf(s, start) != StaffAbsence.None) <= team.Count() / 2);
            }
        }
    }

    [Fact]
    public void Off_sick_staff_stay_at_home_for_their_shift()
    {
        var h = new SimulationHarness(seed: 5, seasons: true, start: new DateTime(2027, 12, 22, 6, 0, 0));
        h.StaffToRecommended();
        h.RunFor(TimeSpan.FromHours(2.5));
        var sim = h.Sim;
        var away = sim.Staff.Where(s => s.Shift == StaffShift.Day && sim.AbsenceOf(s, sim.CurrentShiftStart) != StaffAbsence.None).ToList();
        Assert.NotEmpty(away);
        Assert.All(away, s => Assert.False(s.IsOnDuty));
        Assert.All(away, s => Assert.True(s.StatusText.StartsWith("Off sick", StringComparison.Ordinal) || s.StatusText.StartsWith("On annual leave", StringComparison.Ordinal), s.StatusText));
        Assert.Contains(sim.Notifications.Recent, n => n.Text.StartsWith("Day shift:", StringComparison.Ordinal));
    }

    [Fact]
    public void Events_are_announced_when_they_start()
    {
        var h = new SimulationHarness(seed: 3, seasons: true, start: new DateTime(2027, 12, 24, 22, 0, 0));
        h.RunFor(TimeSpan.FromHours(3));
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.StartsWith("Christmas Day", StringComparison.Ordinal));
        Assert.Contains(h.Sim.WeekOutlook(), d => d.Notes.Contains("Boxing Day"));
    }
}
