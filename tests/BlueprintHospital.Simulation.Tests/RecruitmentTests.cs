using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Grades, the daily candidates, hiring and letting staff go.</summary>
public class RecruitmentTests
{
    [Theory]
    [InlineData(StaffRole.Nurse, 0.5, "Band 5 nurse")]
    [InlineData(StaffRole.Nurse, 0.65, "Band 6 nurse")]
    [InlineData(StaffRole.Nurse, 0.75, "Band 7 nurse")]
    [InlineData(StaffRole.Nurse, 0.85, "Band 8 nurse")]
    [InlineData(StaffRole.Nurse, 0.95, "Band 9 nurse")]
    [InlineData(StaffRole.Doctor, 0.5, "Junior Doctor")]
    [InlineData(StaffRole.Doctor, 0.65, "Doctor")]
    [InlineData(StaffRole.Doctor, 0.8, "Registrar")]
    [InlineData(StaffRole.Doctor, 0.9, "Consultant")]
    public void Grades_follow_how_good_someone_is(StaffRole role, double skill, string grade)
    {
        Assert.Equal(grade, StaffGrades.Of(role, skill).Title);
    }

    [Fact]
    public void The_starting_team_uses_the_new_grades()
    {
        var h = new SimulationHarness();
        var grades = h.Sim.Staff.Where(s => s.Role is StaffRole.Nurse or StaffRole.Doctor).Select(s => s.Grade).ToList();
        Assert.All(grades, g => Assert.Contains(StaffGrades.All, x => x.Title == g));
        Assert.Contains("Registrar", grades);
        Assert.Contains("Band 7 nurse", grades);
    }

    [Fact]
    public void New_candidates_arrive_every_few_hours()
    {
        var h = new SimulationHarness(start: new DateTime(2027, 1, 4, 8, 0, 0));
        Assert.NotEmpty(h.Sim.Candidates);
        var first = h.Sim.Candidates.Select(c => c.Id).ToHashSet();

        h.RunFor(TimeSpan.FromHours(HospitalSimulation.CandidatesEveryHours - 0.5));
        Assert.Equal(first, h.Sim.Candidates.Select(c => c.Id).ToHashSet());

        h.RunFor(TimeSpan.FromHours(1));
        Assert.NotEmpty(h.Sim.Candidates);
        Assert.DoesNotContain(h.Sim.Candidates, c => first.Contains(c.Id));
    }

    [Theory]
    [InlineData(StartingHospital.SmallEmergency)]
    [InlineData(StartingHospital.EmptyBuilding)]
    public void Every_kind_of_staff_applies_each_time(StartingHospital start)
    {
        var h = new SimulationHarness(startWith: start);
        Assert.Equal(Enum.GetValues<StaffRole>().Length, HospitalSimulation.RecruitableRoles.Count);
        for (var batch = 0; batch < 3; batch++)
        {
            var roles = h.Sim.Candidates.Select(c => c.Role).ToHashSet();
            Assert.All(HospitalSimulation.RecruitableRoles, r => Assert.Contains(r, roles));
            h.RunFor(TimeSpan.FromHours(HospitalSimulation.CandidatesEveryHours));
        }
    }

    [Fact]
    public void Hiring_and_letting_go()
    {
        var h = new SimulationHarness();
        var candidate = h.Sim.Candidates.First(c => c.Role is StaffRole.Nurse or StaffRole.Doctor or StaffRole.Porter);
        var before = h.Sim.Staff.Count();

        var member = h.Sim.Hire(candidate.Id, null);
        Assert.NotNull(member);
        Assert.Equal(candidate.Name, member!.Name);
        Assert.Equal(candidate.Salary, member.Salary);
        Assert.Equal(before + 1, h.Sim.Staff.Count());
        Assert.DoesNotContain(h.Sim.Candidates, c => c.Id == candidate.Id);
        Assert.Null(h.Sim.Hire(candidate.Id, null));

        Assert.True(h.Sim.Fire(member.Id));
        Assert.Equal(before, h.Sim.Staff.Count());
        Assert.False(h.Sim.Fire(member.Id));
    }

    [Fact]
    public void Work_goes_back_on_the_board_when_someone_leaves()
    {
        var h = new SimulationHarness(seed: 3);
        var patient = h.Sim.SpawnPatient(Patients.ArrivalMethod.WalkIn, "ankle_sprain");
        h.RunUntil(() => h.Sim.Staff.Any(s => s.Role == StaffRole.Receptionist && s.CurrentTaskId is not null), TimeSpan.FromHours(1), "reception to start");
        var receptionist = h.Sim.Staff.Single(s => s.Role == StaffRole.Receptionist && s.IsOnDuty);
        var task = receptionist.CurrentTaskId!.Value;

        Assert.True(h.Sim.Fire(receptionist.Id));
        var unassigned = h.Sim.Tasks.Single(t => t.Id == task);
        Assert.Null(unassigned.AssignedStaffId);
        Assert.Null(patient.BusyWithTaskId);
    }

    [Fact]
    public void Recommended_staffing_starts_met_for_the_starting_department()
    {
        var h = new SimulationHarness();
        var needs = h.Sim.RecommendedStaffing();
        Assert.Contains(needs, n => n.Role == StaffRole.Porter);
        Assert.Contains(needs, n => n.Role == StaffRole.Nurse && n.RoomId is null);
        Assert.All(needs.Where(n => n.Role is StaffRole.Receptionist or StaffRole.Porter), n => Assert.Equal(0, n.Short));
    }
}
