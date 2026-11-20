using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Parents: a mum or dad comes in with every child, stays beside them, and sleeps on the fold-out bed on the ward.</summary>
public class ParentsTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static SimulationHarness ChildrensWard(int seed)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(5_000_000);
        Assert.True(h.Sim.UnlockDepartment(HospitalSimulation.PaediatricsDepartmentId).CanBuild);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 9, 44, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("childrens_ward", new GridRect(B.X - 40, B.Y + 17, 22, 15))).CanBuild);
        h.StaffToRecommended();
        return h;
    }

    [Fact]
    public void A_child_comes_in_with_a_parent_and_an_adult_does_not()
    {
        var h = new SimulationHarness(11);
        var child = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "diabetic_ketoacidosis", age: 7);
        var adult = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "diabetic_ketoacidosis", age: 30);
        h.RunFor(TimeSpan.FromMinutes(2));

        var parent = Assert.Single(h.Sim.Parents);
        Assert.Same(parent, h.Sim.ParentOf(child));
        Assert.Null(h.Sim.ParentOf(adult));
        Assert.Equal(child.Surname, parent.Surname);
        Assert.True(parent.Age >= 23);
        Assert.Contains(child.Timeline, e => e.Text.StartsWith("Came in with", StringComparison.Ordinal));
        Assert.True(Math.Max(Math.Abs(parent.Tile.X - child.Tile.X), Math.Abs(parent.Tile.Y - child.Tile.Y)) <= 3, $"parent at {parent.Tile}, child at {child.Tile}");
    }

    [Fact]
    public void A_parent_stays_on_the_fold_out_bed_and_sleeps_there_at_night_then_goes_home_with_their_child()
    {
        var h = ChildrensWard(12);
        var child = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "diabetic_ketoacidosis", age: 10);
        h.RunUntil(() => child.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "the child to reach the ward");
        Assert.True(child.Stage == PatientStage.OnWard, h.Journey(child));

        var parent = h.Sim.ParentOf(child)!;
        h.RunUntil(() => parent.FoldOutBed is not null, TimeSpan.FromHours(1), "the parent to settle on the fold-out bed");
        var ward = h.Sim.FindSpace(child.CareSpaceId!)!;
        Assert.Same(HospitalSimulation.FoldOutBedBeside(ward, h.Sim.BedOf(child)!), parent.FoldOutBed);
        Assert.Contains(child.Timeline, e => e.Text.Contains("fold-out bed"));

        h.RunUntil(() => parent.Activity == ParentActivity.Sleeping || child.Stage == PatientStage.Gone, TimeSpan.FromDays(1), "night time");
        if (child.Stage != PatientStage.Gone)
        {
            Assert.True(h.Sim.IsParentBedtime);
        }

        h.RunUntil(() => child.Stage == PatientStage.Gone, TimeSpan.FromDays(6), "the child to go home");
        h.RunFor(TimeSpan.FromSeconds(2));
        Assert.Null(h.Sim.ParentOf(child));
        Assert.Empty(h.Sim.Parents);
    }
}
