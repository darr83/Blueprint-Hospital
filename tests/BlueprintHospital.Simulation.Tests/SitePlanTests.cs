using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;
using Xunit;
using Xunit.Abstractions;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The player choosing where the main entrance and the ambulance bays go when a game starts.</summary>
public class SitePlanTests(ITestOutputHelper output)
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    public static IEnumerable<object[]> AnyWall() => new[]
    {
        new object[] { WallSide.West, 16, WallSide.North, 36 },
        new object[] { WallSide.East, 8, WallSide.South, 14 },
        new object[] { WallSide.North, 10, WallSide.West, 20 },
        new object[] { WallSide.South, 44, WallSide.South, 8 },
        new object[] { WallSide.North, 40, WallSide.East, 26 },
    };

    [Fact]
    public void The_usual_plan_is_the_old_layout()
    {
        var site = StarterSite.Create();
        Assert.Equal(SitePlan.Standard, site.Layout!.Plan);
        Assert.Equal(new GridCoord(B.X + 22, B.Bottom - 1), site.Buildings[0].Entrances.Single(e => !e.IsAmbulanceEntrance).Tile);
        Assert.Equal(new GridCoord(B.Right - 1, B.Y + 9), site.Buildings[0].Entrances.Single(e => e.IsAmbulanceEntrance).Tile);
        Assert.Equal(new GridRect(B.Right + 3, B.Y - 1, 13, 7), site.Layout.AmbulanceBays[0]);
        Assert.Equal(new GridCoord(B.X + 26, B.Bottom + 16), site.Layout.WalkInSpawn);
        Assert.Equal(TileKind.Parking, site[new GridCoord(B.X, B.Bottom + 6)]);
        Assert.Equal(TileKind.Grass, site[new GridCoord(B.X, B.Y - 10)]);
    }

    [Theory]
    [MemberData(nameof(AnyWall))]
    public void Either_entrance_can_go_on_any_wall(WallSide front, int frontAt, WallSide bays, int baysAt)
    {
        var plan = new SitePlan(front, frontAt, bays, baysAt);
        Assert.Empty(StarterSite.Problems(plan));
        var site = StarterSite.Create(plan);
        var layout = site.Layout!;

        // Four bays against the chosen wall, with a road along their open ends.
        Assert.Equal(4, layout.AmbulanceBays.Count);
        foreach (var bay in layout.AmbulanceBays)
        {
            Assert.All(bay.Tiles(), t => Assert.Equal(TileKind.AmbulanceBay, site[t]));
            Assert.False(bay.Intersects(B));
            var mouth = bays switch
            {
                WallSide.East => new GridCoord(bay.Right, bay.Y + 3),
                WallSide.West => new GridCoord(bay.X - 1, bay.Y + 3),
                WallSide.North => new GridCoord(bay.X + 3, bay.Y - 1),
                _ => new GridCoord(bay.X + 3, bay.Bottom),
            };
            Assert.Equal(TileKind.Road, site[mouth]);
            Assert.Equal(TileKind.Road, site[layout.Lane.Beside(mouth)]);
        }

        // People arrive on the road at the end of the front path.
        Assert.Equal(TileKind.Road, site[layout.WalkInSpawn]);
        Assert.True(site.Buildings[0].IsOpening(StarterSite.DoorTile(B, front, frontAt), front));
        Assert.True(site.Buildings[0].IsOpening(StarterSite.DoorTile(B, bays, baysAt), bays));
    }

    [Fact]
    public void Plans_that_do_not_fit_are_refused()
    {
        Assert.NotEmpty(StarterSite.Problems(new SitePlan(WallSide.South, 0, WallSide.East, 9)));
        Assert.NotEmpty(StarterSite.Problems(new SitePlan(WallSide.South, 22, WallSide.East, 40)));
        Assert.NotEmpty(StarterSite.Problems(new SitePlan(WallSide.South, 22, WallSide.South, 20)));
        Assert.Throws<ArgumentException>(() => StarterSite.Create(new SitePlan(WallSide.East, 9, WallSide.East, 9)));
    }

    [Theory]
    [MemberData(nameof(AnyWall))]
    public void Ambulances_reverse_into_bays_on_any_wall_and_patients_get_in(WallSide front, int frontAt, WallSide bays, int baysAt)
    {
        var h = new SimulationHarness(seed: 3, startWith: StartingHospital.EmptyBuilding, plan: new SitePlan(front, frontAt, bays, baysAt));
        var sim = h.Sim;
        for (var i = 0; i < 5; i++)
        {
            sim.SpawnPatient(ArrivalMethod.Ambulance);
        }

        h.RunUntil(() => sim.Ambulances.Count(a => a.State == AmbulanceState.AtBay) == 4 && sim.Ambulances.All(a => !a.IsMoving), TimeSpan.FromHours(1), "four parked ambulances");
        foreach (var a in sim.Ambulances.Where(a => a.State == AmbulanceState.AtBay))
        {
            output.WriteLine($"{a.CallSign} bay {a.BaySlot} at {a.X:0.0},{a.Y:0.0} heading {a.Heading}");
            var bay = sim.AmbulanceBays[a.BaySlot!.Value];
            Assert.True(bay.Contains(a.Tile));

            // Nose out to the road: away from the building.
            var expected = bays switch
            {
                WallSide.East => (1.0, 0.0),
                WallSide.West => (-1.0, 0.0),
                WallSide.North => (0.0, -1.0),
                _ => (0.0, 1.0),
            };
            Assert.Equal(expected, a.Heading);
        }

        var queued = sim.Ambulances.Single(a => a.State == AmbulanceState.WaitingForBay);
        Assert.Equal(TileKind.Road, sim.Site[queued.Tile]);

        // The stretcher spots can be reached from the street.
        var reached = sim.Nav.Reachable(sim.WalkInSpawn);
        Assert.All(sim.AmbulanceBayPatientTiles, t => Assert.Contains(t, reached));
    }

    [Theory]
    [InlineData(WallSide.South, 22, WallSide.East)]
    [InlineData(WallSide.North, 34, WallSide.West)]
    [InlineData(WallSide.South, 4, WallSide.West)]
    [InlineData(WallSide.North, 50, WallSide.East)]
    [InlineData(WallSide.South, 45, WallSide.East)]
    public void Career_starts_lay_their_rooms_out_around_the_entrances(WallSide front, int frontAt, WallSide bays)
    {
        var plan = new SitePlan(front, frontAt, bays, EmergencyDepartmentLayout.StarterAmbulanceAt(front));
        Assert.True(EmergencyDepartmentLayout.FitsStarter(plan));
        var h = new SimulationHarness(seed: 5, type: GameType.Career, plan: plan);
        var sim = h.Sim;
        var rooms = sim.Spaces.Where(s => s.HasWalls).ToList();
        foreach (var a in rooms)
        {
            Assert.True(sim.MainBuilding.Footprint.Contains(new GridCoord(a.Rect.X, a.Rect.Y)) && a.Rect.Right <= B.Right && a.Rect.Bottom <= B.Bottom, $"{a.Name} is outside the building.");
            Assert.All(rooms.Where(b => b != a), b => Assert.False(a.Rect.Intersects(b.Rect), $"{a.Name} overlaps {b.Name}."));
        }

        // The ready-made waiting room is behind the main entrance and open for business.
        Assert.True(sim.IsEmergencyOpen, string.Join(", ", sim.ClosedReasons));
        Assert.True(sim.HasEntranceDoor(sim.MainWaitingRoom!));

        // Walk-in and ambulance patients both get seen.
        var walkIn = sim.SpawnPatient(ArrivalMethod.WalkIn);
        var ambulance = sim.SpawnPatient(ArrivalMethod.Ambulance);
        h.RunUntil(() => walkIn.Triage.HasValue && ambulance.Triage.HasValue, TimeSpan.FromHours(3), "both patients triaged");
    }

    [Fact]
    public void A_corridor_can_join_the_main_entrance_and_lead_to_a_waiting_room()
    {
        // Front doors on the west wall; a corridor straight in from them, narrower than the doorway and not
        // covering its first tile, and a waiting room off the corridor.
        var plan = new SitePlan(WallSide.West, 16, WallSide.East, 9);
        var h = new SimulationHarness(seed: 9, startWith: StartingHospital.EmptyBuilding, plan: plan);
        var sim = h.Sim;
        sim.AdjustBalance(5_000_000);
        var corridor = sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X, B.Y + 18, 50, 5), WallSide.South));
        Assert.True(corridor.CanBuild, string.Join(" ", corridor.Problems));

        var waiting = sim.BuildRoom(sim.AutoPlan("waiting_room", new GridRect(B.X + 20, B.Y + 23, 20, 14)));
        Assert.True(waiting.CanBuild, string.Join(" ", waiting.Problems));
        var room = sim.SpacesOfKind(CareSpaceKind.Waiting).Single();
        Assert.True(sim.PlaceReceptionDesk(room.Id, new GridCoord(room.Rect.X + 10, room.Rect.Y + 8), WallSide.North).CanBuild);
        room = sim.SpacesOfKind(CareSpaceKind.Waiting).Single();
        Assert.Contains(sim.WaitingRoomProblems(room), p => p.Contains("corridor"));
        var door = sim.PlaceWaitingDoor(room.Id, new GridCoord(room.Rect.X + 4, room.Rect.Y));
        Assert.True(door.CanBuild, string.Join(" ", door.Problems));
        room = sim.SpacesOfKind(CareSpaceKind.Waiting).Single();
        Assert.Empty(sim.WaitingRoomProblems(room));
        Assert.Equal(room, sim.MainWaitingRoom);

        // The rest of a small A&E off the same corridor, and staff.
        Assert.True(sim.BuildRoom(sim.AutoPlan("triage_room", new GridRect(B.X + 10, B.Y + 9, 9, 9))).CanBuild);
        Assert.True(sim.BuildRoom(sim.AutoPlan("minors_cubicle", new GridRect(B.X + 20, B.Y + 9, 8, 9))).CanBuild);
        sim.HireStaff(StaffRole.Receptionist);
        sim.HireStaff(StaffRole.Nurse);
        sim.HireStaff(StaffRole.Doctor);
        Assert.True(sim.IsEmergencyOpen, string.Join(", ", sim.ClosedReasons));

        // A walk-in comes in the west doors, along the corridor and books in at the desk.
        var patient = sim.SpawnPatient(ArrivalMethod.WalkIn);
        h.RunUntil(() => patient.Triage.HasValue, TimeSpan.FromHours(3), "the walk-in to be triaged");
    }

    [Fact]
    public void The_site_plan_is_saved_and_loaded()
    {
        var plan = new SitePlan(WallSide.North, 12, WallSide.West, 20);
        var save = new SaveGame { SitePlan = plan };
        var loaded = SaveSerializer.Deserialize(SaveSerializer.Serialize(save));
        Assert.Equal(plan, loaded.SitePlan);
        Assert.Null(SaveSerializer.Deserialize(SaveSerializer.Serialize(new SaveGame())).SitePlan);
    }
}
