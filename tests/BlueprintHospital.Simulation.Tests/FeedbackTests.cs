using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Tests;

public class FeedbackTests
{
    [Fact]
    public void Patients_rate_their_stay_as_they_leave_and_the_hospital_gets_a_star_rating()
    {
        var h = new SimulationHarness(41, arrivals: true);
        Assert.Null(h.Sim.HospitalRating);
        h.RunUntil(() => h.Sim.FeedbackFor(StatsPeriod.AllTime).Count >= 6, TimeSpan.FromHours(30), "six patients to rate their stay");

        var summary = h.Sim.FeedbackFor(StatsPeriod.AllTime);
        Assert.Equal(summary.Count, summary.ByStars.Sum());
        Assert.InRange(summary.Average, 1, 5);
        Assert.NotNull(h.Sim.HospitalRating);
        Assert.Contains(summary.Departments, d => d.Department == "A&E");
        Assert.NotEmpty(h.Sim.FeedbackComments);
        var rated = h.Sim.DepartedPatients.Where(p => p.Stars is not null).ToList();
        Assert.NotEmpty(rated);
        Assert.All(rated, p => Assert.Contains(p.Timeline, t => t.Text.StartsWith("Rated their stay", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_long_wait_in_the_corridor_with_an_accident_rates_one_star_and_a_quick_visit_rates_five()
    {
        var h = new SimulationHarness(42);
        var bad = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        bad.LeftEmergencyAt = bad.ArrivedAt.AddHours(10);
        bad.FirstSeenByDoctorAt = bad.ArrivedAt.AddHours(3);
        bad.CorridorMinutes = 180;
        bad.HadToiletAccident = true;
        var (badStars, badReasons, badFacts) = h.Sim.JudgeStay(bad, PatientOutcome.Discharged);
        Assert.Equal(1, badStars);
        Assert.Contains("wait", badReasons);
        Assert.Contains("corridor", badReasons);
        Assert.Contains("accident", badReasons);
        Assert.Contains(badFacts, f => f.Contains("in A&E (4-hour target missed)", StringComparison.Ordinal));

        var good = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        good.LeftEmergencyAt = good.ArrivedAt.AddMinutes(70);
        good.FirstSeenByDoctorAt = good.ArrivedAt.AddMinutes(15);
        good.Happiness = 0.8;
        var (goodStars, goodReasons, _) = h.Sim.JudgeStay(good, PatientOutcome.Discharged);
        Assert.Equal(5, goodStars);
        Assert.Equal("caring", goodReasons[0]);
        Assert.Contains("quick", goodReasons);
    }

    [Fact]
    public void An_investigation_costs_money_takes_a_day_and_finds_the_cause()
    {
        var h = new SimulationHarness(43);
        var complaint = h.Sim.AddTestComplaint(ComplaintSeverity.Formal)!;
        Assert.Equal(h.Sim.Now + TimeSpan.FromDays(3), complaint.DueAt);
        var before = h.Sim.Balance;
        Assert.True(h.Sim.Respond(complaint.Id, ComplaintResponse.Investigate));
        Assert.Equal(before - HospitalSimulation.InvestigationCost, h.Sim.Balance);
        Assert.Equal(ComplaintStatus.Waiting, complaint.Status);
        Assert.False(complaint.CauseFound);
        Assert.All(h.Sim.OptionsFor(complaint), o => Assert.False(o.Enabled));
        Assert.False(h.Sim.Respond(complaint.Id, ComplaintResponse.Apologise));

        h.RunFor(TimeSpan.FromHours(24.5));
        Assert.True(complaint.CauseFound);
        Assert.NotEqual(ComplaintStatus.Waiting, complaint.Status);
        Assert.False(h.Sim.OptionsFor(complaint).Single(o => o.Response == ComplaintResponse.Investigate).Enabled);
        Assert.Equal(-HospitalSimulation.InvestigationCost, h.Sim.Accounts.Sum(d => d[MoneyCategory.Complaints]));
    }

    [Fact]
    public void Unanswered_concerns_become_formal_and_unanswered_formal_complaints_go_to_the_Ombudsman()
    {
        var h = new SimulationHarness(44);
        var concern = h.Sim.AddTestComplaint(ComplaintSeverity.Concern)!;
        h.RunFor(TimeSpan.FromHours(25));
        Assert.Equal(ComplaintSeverity.Formal, concern.Severity);
        Assert.Equal(ComplaintStatus.Open, concern.Status);

        var before = h.Sim.Balance;
        h.RunFor(TimeSpan.FromDays(3.1));
        Assert.Equal(ComplaintStatus.Ombudsman, concern.Status);
        Assert.Equal(HospitalSimulation.OmbudsmanFine(ComplaintSeverity.Formal), concern.Spent);
        Assert.True(h.Sim.Balance <= before - HospitalSimulation.OmbudsmanFine(ComplaintSeverity.Formal));
        Assert.True(h.Sim.ReputationAdjustment < 0);
        Assert.Empty(h.Sim.OpenComplaints);
    }

    [Fact]
    public void Meeting_them_needs_a_consultant_or_registrar_and_takes_an_hour()
    {
        var h = new SimulationHarness(45);
        var complaint = h.Sim.AddTestComplaint(ComplaintSeverity.Concern)!;
        var meet = h.Sim.OptionsFor(complaint).Single(o => o.Response == ComplaintResponse.Meet);
        var hasSenior = h.Sim.Staff.Any(s => s.Role == StaffRole.Doctor && s.Grade is "Consultant" or "Registrar");
        Assert.Equal(hasSenior, meet.Enabled);
        if (!hasSenior)
        {
            return;
        }

        Assert.True(h.Sim.Respond(complaint.Id, ComplaintResponse.Meet));
        Assert.Equal(ComplaintStatus.Waiting, complaint.Status);
        h.RunFor(TimeSpan.FromMinutes(65));
        Assert.NotEqual(ComplaintStatus.Waiting, complaint.Status);
        Assert.Contains(ComplaintResponse.Meet, complaint.Tried);
    }

    [Fact]
    public void Complaints_ratings_and_comments_are_saved()
    {
        var h = new SimulationHarness(46, arrivals: true);
        h.RunUntil(() => h.Sim.FeedbackComments.Count >= 2, TimeSpan.FromHours(30), "two comments");
        var complaint = h.Sim.AddTestComplaint(ComplaintSeverity.Serious)!;
        Assert.True(h.Sim.Respond(complaint.Id, ComplaintResponse.Investigate));

        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);

        var loaded = to.Sim.Complaints.Single(c => c.Id == complaint.Id);
        Assert.Equal(ComplaintSeverity.Serious, loaded.Severity);
        Assert.Equal(ComplaintStatus.Waiting, loaded.Status);
        Assert.Equal(complaint.Facts, loaded.Facts);
        Assert.Equal(h.Sim.FeedbackComments.Count, to.Sim.FeedbackComments.Count);
        Assert.Equal(h.Sim.FeedbackFor(StatsPeriod.AllTime).Count, to.Sim.FeedbackFor(StatsPeriod.AllTime).Count);
    }

    [Fact]
    public void The_tutorial_has_no_ratings_or_complaints()
    {
        var h = new SimulationHarness(47, type: GameType.Tutorial);
        Assert.False(h.Sim.FeedbackOn);
    }
}
