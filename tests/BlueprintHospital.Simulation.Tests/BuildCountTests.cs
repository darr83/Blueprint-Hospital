using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The count badge on each build bar tile.</summary>
public class BuildCountTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    [Fact]
    public void Counts_match_the_rooms_and_fittings_in_the_hospital()
    {
        var h = new SimulationHarness();
        var sim = h.Sim;
        foreach (var id in sim.Database.RoomTemplates.Keys)
        {
            Assert.Equal(sim.Spaces.Count(s => s.TemplateId == id), sim.BuiltCount(id));
        }

        Assert.True(sim.BuiltCount("waiting_room") >= 1);
        Assert.Equal(0, sim.BuiltCount("operating_theatre"));
        Assert.Equal(0, sim.BuiltCount(BuildMenu.AddFloor));
        Assert.Equal(sim.Chairs.Count, sim.BuiltCount(BuildMenu.WaitingChairs));
        Assert.Equal(sim.ResusTrolleys.Count, sim.BuiltCount(BuildMenu.ResusTrolleys));
        Assert.Equal(sim.SpacesOfKind(CareSpaceKind.Waiting).Count(r => sim.FittingsOf(r).Desk is not null), sim.BuiltCount(BuildMenu.WaitingDesk));
    }

    [Fact]
    public void A_lift_through_two_floors_counts_once()
    {
        var h = new SimulationHarness();
        h.Sim.AdjustBalance(5_000_000);
        Assert.True(h.Sim.AddFloor().CanBuild);
        Assert.Equal(1, h.Sim.BuiltCount(BuildMenu.AddFloor));
        Assert.Equal(0, h.Sim.BuiltCount("lift"));
        var check = h.Sim.BuildRoom(h.Sim.AutoPlan("lift", new GridRect(B.X + 48, B.Y + 17, 5, 6)));
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        Assert.Equal(2, h.Sim.Spaces.Count(s => s.Kind == CareSpaceKind.Lift));
        Assert.Equal(1, h.Sim.BuiltCount("lift"));
    }
}
