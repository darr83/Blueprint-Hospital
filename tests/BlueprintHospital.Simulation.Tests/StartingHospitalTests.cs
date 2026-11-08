using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The two ways to start: a small staffed A&amp;E, or an empty building.</summary>
public class StartingHospitalTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    [Fact]
    public void The_small_emergency_department_is_open_from_the_start()
    {
        var h = new SimulationHarness();
        Assert.True(h.Sim.IsEmergencyOpen);
        Assert.NotEmpty(h.Sim.Staff);
    }

    [Fact]
    public void An_empty_building_has_only_a_waiting_room_no_staff_and_more_money()
    {
        var small = new SimulationHarness();
        var empty = new SimulationHarness(startWith: StartingHospital.EmptyBuilding);

        Assert.Equal(CareSpaceKind.Waiting, Assert.Single(empty.Sim.Spaces).Kind);
        Assert.Empty(empty.Sim.Staff);
        Assert.True(empty.Sim.Balance > small.Sim.Balance);
        Assert.NotEmpty(empty.Sim.Candidates);
        Assert.False(empty.Sim.IsEmergencyOpen);
        Assert.Equal(4, empty.Sim.ClosedReasons.Count);
    }

    [Fact]
    public void No_patients_come_until_the_emergency_department_can_see_them()
    {
        var h = new SimulationHarness(arrivals: true, arrivalMultiplier: 3, startWith: StartingHospital.EmptyBuilding);
        h.RunFor(TimeSpan.FromHours(3));
        Assert.Equal(0, h.Sim.Stats.ArrivalsTotal);

        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X + 4, B.Y + 9, 56, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("triage_room", new GridRect(B.X + 4, B.Y + 17, 9, 8))).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("majors_cubicle", new GridRect(B.X + 28, B.Y, 8, 9))).CanBuild);
        h.Sim.HireStaff(StaffRole.Nurse, skill: 0.7);
        Assert.False(h.Sim.IsEmergencyOpen);
        h.Sim.HireStaff(StaffRole.Doctor, skill: 0.7);
        Assert.True(h.Sim.IsEmergencyOpen, string.Join(", ", h.Sim.ClosedReasons));

        h.RunFor(TimeSpan.FromHours(3));
        Assert.True(h.Sim.Stats.ArrivalsTotal > 0);
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.Contains("A&E is open"));
    }
}
