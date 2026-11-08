using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class FloorTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A lift beside the nurses' station, its door onto the main A&amp;E corridor.</summary>
    private static readonly GridRect LiftRect = new(B.X + 48, B.Y + 17, 5, 6);

    /// <summary>Stairs next to it.</summary>
    private static readonly GridRect StairsRect = new(B.X + 54, B.Y + 17, 5, 8);

    private static SimulationHarness Rich(int seed = 1234)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(5_000_000);
        return h;
    }

    private static GridRect Up(SimulationHarness h, GridRect local, int floor = 1) => h.Sim.Site.OnFloor(local, floor);

    private static void Build(SimulationHarness h, string template, GridRect rect)
    {
        var check = h.Sim.BuildRoom(h.Sim.AutoPlan(template, rect));
        Assert.True(check.CanBuild, $"{template}: {string.Join("; ", check.Problems)}");
    }

    /// <summary>Floor 1: a corridor along the same line as the A&amp;E corridor below, starting from the lift (or stairs) door.</summary>
    private static GridRect UpstairsCorridor(SimulationHarness h) => Up(h, new GridRect(B.X + 4, B.Y + 9, 56, 8));

    [Fact]
    public void Adding_a_floor_costs_money_and_copies_the_outline()
    {
        var h = new SimulationHarness();
        var before = h.Sim.Balance;
        var cost = h.Sim.CheckAddFloor().Cost;
        Assert.Equal((int)Math.Round(h.Sim.MainBuilding.AreaSquareMetres * HospitalSimulation.FloorCostPerSquareMetre), cost);

        var check = h.Sim.AddFloor();
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        Assert.Equal(2, h.Sim.FloorCount);
        Assert.Equal(before - cost, h.Sim.Balance);

        // Upstairs is floor inside the outline and nothing outside it.
        var inside = h.Sim.Site.OnFloor(new GridCoord(B.X + 10, B.Y + 10), 1);
        var outside = h.Sim.Site.OnFloor(new GridCoord(B.X - 3, B.Y + 10), 1);
        Assert.Equal(1, h.Sim.FloorOf(inside));
        Assert.True(h.Sim.Site.IsIndoors(inside));
        Assert.False(h.Sim.Site.IsIndoors(outside));
        Assert.True(h.Sim.Nav.IsWalkable(inside));
        Assert.False(h.Sim.Nav.IsWalkable(outside));

        // The next floor up costs more per square metre.
        Assert.True(h.Sim.CheckAddFloor().Cost > cost);
    }

    [Fact]
    public void The_building_has_at_most_five_floors()
    {
        var h = Rich();
        for (var i = 0; i < SiteMap.MaxUpperFloors; i++)
        {
            Assert.True(h.Sim.AddFloor().CanBuild);
        }

        Assert.Contains(h.Sim.CheckAddFloor().Problems, p => p.Contains("at most"));
    }

    [Fact]
    public void Rooms_upstairs_need_a_lift_or_stairs_to_reach_them()
    {
        var h = Rich();
        h.Sim.AddFloor();

        // No way up yet, so a corridor can't start anywhere upstairs.
        var corridor = h.Sim.CheckRoomAt("corridor", UpstairsCorridor(h));
        Assert.Contains(corridor.Problems, p => p.Contains("Must join"));

        Build(h, "lift", LiftRect);
        Assert.Equal(2, h.Sim.Spaces.Count(s => s.Kind == CareSpaceKind.Lift));
        Build(h, "corridor", UpstairsCorridor(h));

        // A ward upstairs, reached through the lift.
        Build(h, "emergency_ward", Up(h, new GridRect(B.X + 20, B.Y + 17, 14, 10)));
        Assert.Contains(h.Sim.Spaces, s => s.IsWard && h.Sim.FloorOf(s) == 1);
    }

    [Fact]
    public void Wards_upstairs_need_a_lift_but_a_staff_room_is_fine_with_stairs()
    {
        var h = Rich();
        h.Sim.AddFloor();
        Build(h, "stairs", StairsRect);
        Build(h, "corridor", UpstairsCorridor(h));

        var ward = h.Sim.CheckRoomAt("emergency_ward", Up(h, new GridRect(B.X + 20, B.Y + 17, 14, 10)));
        Assert.Contains(ward.Problems, p => p.Contains("needs a lift"));

        Build(h, "staff_room", Up(h, new GridRect(B.X + 4, B.Y + 17, 8, 8)));
    }

    [Fact]
    public void Lifts_and_stairs_go_through_floors_added_later_and_cost_each_floor()
    {
        var h = Rich();
        var single = h.Sim.CheckRoomAt("lift", LiftRect).Cost;
        h.Sim.AddFloor();
        var twoFloors = h.Sim.CheckRoomAt("lift", LiftRect);
        Assert.Equal(single * 2, twoFloors.Cost);
        Build(h, "lift", LiftRect);

        var floorWithoutLift = (int)Math.Round(h.Sim.MainBuilding.AreaSquareMetres * (HospitalSimulation.FloorCostPerSquareMetre + HospitalSimulation.FloorCostRisePerSquareMetre));
        Assert.Equal(floorWithoutLift + single, h.Sim.CheckAddFloor().Cost);
        h.Sim.AddFloor();

        var shaft = Assert.Single(h.Sim.Shafts);
        Assert.Equal(new[] { 0, 1, 2 }, shaft.Select(h.Sim.FloorOf));
        Assert.All(shaft, s => Assert.Equal(h.Sim.Site.Local(shaft[0].Rect), h.Sim.Site.Local(s.Rect)));
        Assert.All(shaft, s => Assert.Equal(shaft[0].Name, s.Name));
        Assert.All(shaft, s => Assert.Equal(shaft[0].Doors, s.Doors));

        // Demolishing takes it out of every floor.
        var demolish = h.Sim.DemolishRoom(shaft[1].Id);
        Assert.True(demolish.CanBuild, string.Join("; ", demolish.Problems));
        Assert.DoesNotContain(h.Sim.Spaces, s => s.IsShaft);
    }

    [Fact]
    public void A_lift_needs_the_same_space_clear_on_every_floor()
    {
        var h = Rich();
        h.Sim.AddFloor();
        Build(h, "stairs", StairsRect);
        Build(h, "corridor", UpstairsCorridor(h));
        Build(h, "staff_room", Up(h, new GridRect(B.X + 44, B.Y + 17, 8, 8)));

        // Downstairs is clear, but upstairs the staff room is in the way.
        var check = h.Sim.CheckRoomAt("lift", new GridRect(B.X + 48, B.Y + 17, 5, 6));
        Assert.Contains(check.Problems, p => p.Contains("Overlaps") && p.Contains("every floor"));
    }

    [Fact]
    public void Walkers_take_the_stairs_for_one_floor_and_trolleys_always_take_the_lift()
    {
        var h = Rich();
        h.Sim.AddFloor();
        Build(h, "lift", LiftRect);
        Build(h, "stairs", StairsRect);
        Build(h, "corridor", UpstairsCorridor(h));

        var stairs = h.Sim.Spaces.First(s => s.Kind == CareSpaceKind.Stairs && h.Sim.FloorOf(s) == 0);
        var lift = h.Sim.Spaces.First(s => s.Kind == CareSpaceKind.Lift && h.Sim.FloorOf(s) == 0);
        var from = new GridCoord(B.X + 50, B.Y + 12);
        var to = h.Sim.Site.OnFloor(new GridCoord(B.X + 52, B.Y + 12), 1);

        var walk = (NavPath)h.Sim.Nav.FindPath(from, to);
        var rideAt = Enumerable.Range(1, walk.Count - 1).Single(i => walk.HopAt(i) > 0);
        Assert.Equal(stairs.PatientSpot, walk[rideAt - 1]);

        var wheeled = (NavPath)h.Sim.Nav.FindPath(from, to, stepFreeOnly: true);
        var liftAt = Enumerable.Range(1, wheeled.Count - 1).Single(i => wheeled.HopAt(i) > 0);
        Assert.Equal(lift.PatientSpot, wheeled[liftAt - 1]);
        Assert.Equal(VerticalTransport.LiftWait + VerticalTransport.LiftPerFloor, wheeled.HopAt(liftAt));
    }

    [Fact]
    public void Someone_walking_upstairs_waits_for_the_ride_then_steps_out_on_the_next_floor()
    {
        var h = Rich();
        h.Sim.AddFloor();
        Build(h, "stairs", StairsRect);
        Build(h, "corridor", UpstairsCorridor(h));

        var walker = new TestWalker();
        var start = new GridCoord(B.X + 50, B.Y + 12);
        var goal = h.Sim.Site.OnFloor(new GridCoord(B.X + 30, B.Y + 12), 1);
        walker.PlaceAt(start);
        walker.FollowPath(h.Sim.Nav.FindPath(start, goal));

        var floorsSeen = new List<int>();
        for (var i = 0; i < 2000 && walker.IsMoving; i++)
        {
            walker.Move(0.25);
            floorsSeen.Add(h.Sim.Site.FloorOf(walker.Y));

            // Never drawn halfway between floors.
            Assert.Equal(h.Sim.Site.FloorOf(walker.Y), h.Sim.Site.FloorOf(walker.PreviousY));
        }

        Assert.Equal(goal, walker.Tile);
        Assert.Equal(new[] { 0, 1 }, floorsSeen.Distinct());
    }

    [Fact]
    public void Floors_lifts_and_stairs_survive_save_and_load()
    {
        var h = Rich();
        h.Sim.AddFloor();
        Build(h, "lift", LiftRect);
        Build(h, "corridor", UpstairsCorridor(h));
        Build(h, "emergency_ward", Up(h, new GridRect(B.X + 20, B.Y + 17, 14, 10)));
        var layout = h.Sim.CaptureLayout();
        Assert.Equal(2, layout.Floors);

        var loaded = new SimulationHarness();
        loaded.Sim.RestoreLayout(layout);
        Assert.Equal(2, loaded.Sim.FloorCount);
        Assert.Equal(2, loaded.Sim.Spaces.Count(s => s.Kind == CareSpaceKind.Lift));
        Assert.Contains(loaded.Sim.Spaces, s => s.IsWard && loaded.Sim.FloorOf(s) == 1);
        Assert.Contains(loaded.Sim.Nav.Links, l => l.Link.StepFree);

        // A save from before floors existed has no floor count, and loads as one floor.
        var old = new LayoutState { Balance = 1000 };
        var fresh = new SimulationHarness();
        fresh.Sim.RestoreLayout(old);
        Assert.Equal(1, fresh.Sim.FloorCount);
    }

    [Fact]
    public void Extending_a_tall_building_extends_every_floor_and_costs_each_floor()
    {
        var h = Rich();
        var area = new GridRect(B.X - 10, B.Y, 10, 10);
        var single = h.Sim.CheckExtension(area).Cost;
        h.Sim.AddFloor();
        Assert.Equal(single * 2, h.Sim.CheckExtension(area).Cost);
        Assert.True(h.Sim.ExtendBuilding(area).CanBuild);
        Assert.True(h.Sim.Site.IsIndoors(h.Sim.Site.OnFloor(new GridCoord(B.X - 5, B.Y + 5), 1)));
    }

    [Fact]
    public void A_porter_takes_a_poorly_patient_up_in_the_lift_to_xray()
    {
        // Seed 1: a pneumonia whose doctor wants a chest X-ray. With some seeds the history and exam
        // alone make the doctor sure enough, no X-ray is ordered and there is no lift trip to test.
        var h = Rich(seed: 1);
        h.Sim.AddFloor();
        Build(h, "lift", LiftRect);
        Build(h, "stairs", StairsRect);
        Build(h, "corridor", UpstairsCorridor(h));
        Build(h, "xray_room", Up(h, new GridRect(B.X + 20, B.Y + 17, 9, 9)));
        h.StaffToRecommended();

        var lifts = h.Sim.Spaces.Where(s => s.Kind == CareSpaceKind.Lift).Select(s => s.PatientSpot).ToHashSet();
        var stairs = h.Sim.Spaces.Where(s => s.Kind == CareSpaceKind.Stairs).Select(s => s.PatientSpot).ToHashSet();
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        var usedLift = false;
        var usedStairs = false;
        var pushedUpstairs = false;
        var maxGap = 0.0;
        h.RunUntil(() =>
        {
            usedLift |= lifts.Contains(patient.Tile) && patient.PushedByStaffId is not null;
            usedStairs |= stairs.Contains(patient.Tile) && patient.PushedByStaffId is not null;
            if (patient.PushedByStaffId is { } id && h.Sim.FindStaff(id) is { } porter && patient.IsMoving && h.Sim.FloorOf(patient) == 1)
            {
                pushedUpstairs = true;
                Assert.Equal(1, h.Sim.FloorOf(porter));
                maxGap = Math.Max(maxGap, Math.Sqrt(Math.Pow(porter.X - patient.X, 2) + Math.Pow(porter.Y - patient.Y, 2)));
            }

            return patient.Investigations.Any(o => o.InvestigationId == "chest_xray" && o.Status != InvestigationStatus.Requested) || patient.Stage == PatientStage.Gone;
        }, TimeSpan.FromHours(8), "the chest X-ray upstairs");

        var journey = h.Journey(patient);
        Assert.Contains("Chest X-ray done by", journey);
        Assert.True(usedLift, journey);
        Assert.False(usedStairs, journey);
        Assert.True(pushedUpstairs, journey);
        Assert.InRange(maxGap, 0.0, 3.5);
    }

    private sealed class TestWalker : Agent;
}
