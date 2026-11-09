using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class BuildingTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    [Fact]
    public void Every_template_lays_out_at_every_preset_size_and_direction()
    {
        var h = new SimulationHarness();
        foreach (var template in h.Database.RoomTemplates.Values.Where(t => !t.IsCorridor))
        {
            foreach (var size in template.Sizes)
            {
                foreach (var side in Enum.GetValues<WallSide>())
                {
                    var rect = RoomLayoutEngine.RectFor(new GridCoord(10, 10), Units.MetresToTiles(size.WidthM), Units.MetresToTiles(size.LengthM), side);
                    var room = RoomLayoutEngine.Build(template, h.Database, "r", "R", rect, side);
                    var what = $"{template.Id} {size.Name} door {side}";

                    Assert.True(rect.Contains(room.StaffSpot), $"{what}: staff spot outside the room");
                    Assert.True(rect.Contains(room.PatientSpot), $"{what}: patient spot outside the room");
                    Assert.All(room.Furniture, f => Assert.True(
                        f.X >= rect.X - 0.01 && f.Y >= rect.Y - 0.01 && f.X + f.Width <= rect.Right + 0.01 && f.Y + f.Length <= rect.Bottom + 0.01,
                        $"{what}: {f.ItemId} sticks out of the room"));
                    Assert.All(template.Furniture.Intersect(RoomLayoutEngine.RequiredFurniture(template.Layout)), id => Assert.Contains(room.Furniture, f => f.ItemId == id));
                    Assert.Equal(template.DoorBothEnds ? 2 : 1, room.Doors.Count);
                    Assert.All(room.Doors, d => Assert.Equal(d == room.Doors[0] ? side : d.Side, d.Side));
                    Assert.True(room.BuildCost > template.BaseCost, $"{what}: cost should include floor and furniture");
                }
            }
        }
    }

    [Fact]
    public void Bigger_waiting_room_has_more_seats()
    {
        var h = new SimulationHarness();
        var t = h.Database.RoomTemplates["waiting_room"];
        CareSpace Make(double w, double l) => RoomLayoutEngine.Build(t, h.Database, "w", "W", RoomLayoutEngine.RectFor(new GridCoord(0, 0), Units.MetresToTiles(w), Units.MetresToTiles(l), WallSide.South), WallSide.South);
        Assert.True(Make(14, 9).Seats.Count > Make(8, 6).Seats.Count);
        Assert.NotEmpty(Make(8, 6).QueueTiles);
    }

    [Fact]
    public void Starter_layout_passes_the_building_rules()
    {
        var h = new SimulationHarness();
        foreach (var space in h.Sim.Spaces.ToList())
        {
            var check = h.Sim.CheckRoom(new RoomPlan(space.TemplateId, space.Rect, space.DoorSide), space.Id);
            Assert.True(check.CanBuild, $"{space.Name}: {string.Join("; ", check.Problems)}");
        }
    }

    [Fact]
    public void Rooms_must_be_inside_the_building_and_not_overlap()
    {
        var h = new SimulationHarness();
        var outside = h.Sim.CheckRoom(new RoomPlan("minors_cubicle", new GridRect(B.X - 12, B.Y, 8, 9), WallSide.South));
        Assert.Contains(outside.Problems, p => p.Contains("inside the building"));

        var overlap = h.Sim.CheckRoom(new RoomPlan("minors_cubicle", new GridRect(B.X + 6, B.Y, 8, 9), WallSide.South));
        Assert.Contains(overlap.Problems, p => p.Contains("Overlaps"));

        var tooSmall = h.Sim.CheckRoom(new RoomPlan("resus_bay", new GridRect(B.X, B.Y + 30, 4, 4), WallSide.South));
        Assert.Contains(tooSmall.Problems, p => p.Contains("Too small"));
    }

    [Fact]
    public void Door_must_open_onto_a_corridor()
    {
        var h = new SimulationHarness();

        // The strip at the west end of the building is empty floor; a room there facing west opens onto the outer wall.
        var check = h.Sim.CheckRoom(new RoomPlan("staff_room", new GridRect(B.X, B.Y + 26, 4 * 2, 3 * 2), WallSide.West));
        Assert.Contains(check.Problems, p => p.Contains("corridor"));
    }

    [Fact]
    public void Extend_the_building_then_add_a_corridor_and_a_cubicle_that_patients_use()
    {
        var h = new SimulationHarness();
        var start = h.Sim.Balance;

        var extension = h.Sim.ExtendBuilding(new GridRect(B.X - 10, B.Y, 10, 20));
        Assert.True(extension.CanBuild, string.Join("; ", extension.Problems));

        var corridor = h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 10, B.Y + 9, 14, 8), WallSide.South));
        Assert.True(corridor.CanBuild, string.Join("; ", corridor.Problems));

        var cubicle = h.Sim.BuildRoom(new RoomPlan("minors_cubicle", new GridRect(B.X - 10, B.Y, 8, 9), WallSide.South));
        Assert.True(cubicle.CanBuild, string.Join("; ", cubicle.Problems));
        Assert.Equal(start - extension.Cost - corridor.Cost - cubicle.Cost, h.Sim.Balance);
        Assert.Equal(3, h.Sim.SpacesOfKind(CareSpaceKind.Minors).Count());

        // Fill the original cubicles and the new one gets used.
        var patients = Enumerable.Range(0, 4).Select(_ => h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration")).ToList();
        var newRoom = h.Sim.SpacesOfKind(CareSpaceKind.Minors).Single(s => s.Rect.X < B.X);
        h.RunUntil(() => newRoom.OccupantId is not null, TimeSpan.FromHours(4), "the new cubicle to be used");
    }

    [Fact]
    public void The_waiting_room_can_be_demolished_but_not_an_occupied_room()
    {
        var h = new SimulationHarness();
        var waiting = h.Sim.SpacesOfKind(CareSpaceKind.Waiting).Single();
        Assert.True(h.Sim.CheckDemolish(waiting.Id).CanBuild, string.Join("; ", h.Sim.CheckDemolish(waiting.Id).Problems));

        // The last triage room can go: A&E then closes to new patients until there is another.
        var triage = h.Sim.SpacesOfKind(CareSpaceKind.Triage).Single();
        Assert.True(h.Sim.DemolishRoom(triage.Id).CanBuild);
        Assert.False(h.Sim.IsEmergencyOpen);
        Assert.Contains("a triage room", h.Sim.ClosedReasons);

        var cubicle = h.Sim.Space("minors_1");
        cubicle.OccupantId = 999;
        Assert.False(h.Sim.CheckDemolish("minors_1").CanBuild);
        cubicle.OccupantId = null;

        var before = h.Sim.Balance;
        var result = h.Sim.DemolishRoom("minors_1");
        Assert.True(result.CanBuild, string.Join("; ", result.Problems));
        Assert.True(h.Sim.Balance > before);
        Assert.Null(h.Sim.FindSpace("minors_1"));
    }

    [Fact]
    public void Extensions_cannot_cover_the_ambulance_bay_or_an_entrance_approach()
    {
        var h = new SimulationHarness();
        Assert.False(h.Sim.CheckExtension(new GridRect(B.Right, B.Y + 6, 8, 14)).CanBuild);
        Assert.Contains(h.Sim.CheckExtension(new GridRect(B.X + 20, B.Bottom, 12, 3)).Problems, p => p.Contains("main entrance"));
        Assert.Contains(h.Sim.CheckExtension(new GridRect(B.X, B.Y - 30, 6, 6)).Problems, p => p.Contains("join on"));
    }

    [Fact]
    public void Layout_survives_save_and_load()
    {
        var h = new SimulationHarness();
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 10, B.Y, 10, 20)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 10, B.Y + 9, 14, 8), WallSide.South)).CanBuild);
        h.Sim.RenameRoom("resus_1", "Resus A");

        var json = SaveSerializer.Serialize(new SaveGame { Layout = h.Sim.CaptureLayout() });
        var loaded = SaveSerializer.Deserialize(json);

        var fresh = new SimulationHarness();
        fresh.Sim.RestoreLayout(loaded.Layout!);
        Assert.Equal(h.Sim.Balance, fresh.Sim.Balance);
        Assert.Equal(h.Sim.Spaces.Count(), fresh.Sim.Spaces.Count());
        Assert.Equal("Resus A", fresh.Sim.Space("resus_1").Name);
        Assert.True(fresh.Sim.MainBuilding.Contains(new GridCoord(B.X - 5, B.Y + 5)));
    }

    [Fact]
    public void A_second_triage_room_lets_two_nurses_triage_at_once()
    {
        var h = new SimulationHarness();
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 10, B.Y, 10, 20)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 10, B.Y + 9, 14, 8), WallSide.South)).CanBuild);
        var built = h.Sim.BuildRoom(new RoomPlan("triage_room", new GridRect(B.X - 10, B.Y, 8, 9), WallSide.South));
        Assert.True(built.CanBuild, string.Join("; ", built.Problems));

        for (var i = 0; i < 4; i++)
        {
            h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        }

        h.RunUntil(() => h.Sim.SpacesOfKind(CareSpaceKind.Triage).All(t => t.ReservedByStaffId is not null), TimeSpan.FromHours(2), "both triage rooms in use");
    }
}

public class DragToBuildTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static SimulationHarness WithWestWing()
    {
        var h = new SimulationHarness();
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 16, B.Y, 16, 26)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 16, B.Y + 9, 20, 8), WallSide.South)).CanBuild);
        return h;
    }

    [Fact]
    public void Door_goes_on_the_wall_that_touches_a_corridor()
    {
        var h = WithWestWing();

        // North of the new corridor: the door must face south onto it.
        var north = h.Sim.AutoPlan("minors_cubicle", new GridRect(B.X - 16, B.Y, 8, 9));
        Assert.Equal(WallSide.South, north.DoorSide);

        // South of it: the door faces north.
        var south = h.Sim.AutoPlan("staff_room", new GridRect(B.X - 16, B.Y + 17, 8, 8));
        Assert.Equal(WallSide.North, south.DoorSide);
        Assert.True(h.Sim.CheckRoomAt("staff_room", new GridRect(B.X - 16, B.Y + 17, 8, 8)).CanBuild);
    }

    [Fact]
    public void Room_away_from_any_corridor_is_refused_with_a_reason()
    {
        var h = WithWestWing();
        var check = h.Sim.CheckRoomAt("minors_cubicle", new GridRect(B.X - 16, B.Y + 18, 8, 7));
        Assert.True(check.CanBuild || check.Problems.Any());
        var isolated = h.Sim.CheckRoomAt("minors_cubicle", new GridRect(B.X - 15, B.Y + 19, 7, 7));
        Assert.Contains(isolated.Problems, p => p.Contains("corridor"));
    }

    [Fact]
    public void Either_side_can_be_the_long_one()
    {
        var h = WithWestWing();
        // 7 m along the corridor but only 3.5 m deep is too shallow for a resus bay (minimum 5 x 4 m).
        Assert.Contains(h.Sim.CheckRoomAt("resus_bay", new GridRect(B.X - 16, B.Y + 2, 14, 7)).Problems, p => p.Contains("Too small"));
        // 5 m along the corridor and 4.5 m deep is fine.
        var deep = h.Sim.CheckRoomAt("resus_bay", new GridRect(B.X - 16, B.Y, 10, 9));
        Assert.True(deep.CanBuild, string.Join("; ", deep.Problems));
    }

    [Fact]
    public void Bigger_rooms_get_more_furniture()
    {
        var h = WithWestWing();
        var small = h.Sim.CheckRoomAt("staff_room", new GridRect(B.X - 16, B.Y + 17, 10, 8));
        var large = h.Sim.CheckRoomAt("staff_room", new GridRect(B.X - 16, B.Y + 17, 16, 9));
        Assert.True(small.CanBuild, string.Join("; ", small.Problems));
        Assert.True(large.CanBuild, string.Join("; ", large.Problems));
        Assert.True(large.Preview!.Seats.Count > small.Preview!.Seats.Count);
    }

    [Fact]
    public void Dragged_room_survives_save_and_load_with_its_door()
    {
        var h = WithWestWing();
        var plan = h.Sim.AutoPlan("doctors_office", new GridRect(B.X - 16, B.Y + 17, 12, 8));
        Assert.True(h.Sim.BuildRoom(plan).CanBuild);
        var built = h.Sim.Spaces.Single(s => s.TemplateId == "doctors_office" && s.Rect.X < B.X);

        var fresh = new SimulationHarness();
        fresh.Sim.RestoreLayout(SaveSerializer.Deserialize(SaveSerializer.Serialize(new SaveGame { Layout = h.Sim.CaptureLayout() })).Layout!);
        var loaded = fresh.Sim.Space(built.Id);
        Assert.Equal(built.Doors, loaded.Doors);
        Assert.Equal(built.Seats, loaded.Seats);
    }

    [Fact]
    public void Corridors_can_overlap_and_join_and_only_new_floor_is_paid_for()
    {
        var h = new SimulationHarness();
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 20, B.Y, 20, 20)).CanBuild);
        var first = h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 20, B.Y + 9, 24, 5), WallSide.South));
        Assert.True(first.CanBuild, string.Join("; ", first.Problems));

        // An L-shaped branch dragged across the first corridor: only its new tiles are charged.
        var branch = new GridRect(B.X - 12, B.Y + 2, 5, 15);
        var check = h.Sim.CheckRoom(new RoomPlan("corridor", branch, WallSide.South));
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        var template = h.Database.RoomTemplates["corridor"];
        var newTiles = (5 * 15) - (5 * 5);
        Assert.Equal((int)Math.Round(template.CostPerSquareMetre * newTiles * 0.25), check.Cost);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", branch, WallSide.South)).CanBuild);

        // Dragging over floor that is already corridor is refused, and a room still can't overlap one.
        var again = h.Sim.CheckRoom(new RoomPlan("corridor", new GridRect(B.X - 12, B.Y + 9, 5, 5), WallSide.South));
        Assert.Contains(again.Problems, p => p.Contains("already"));
        var room = h.Sim.CheckRoom(new RoomPlan("minors_cubicle", new GridRect(B.X - 14, B.Y + 2, 8, 9), WallSide.South));
        Assert.Contains(room.Problems, p => p.Contains("Overlaps"));

        // People can walk from the branch to the department.
        var path = h.Sim.Nav.FindPath(new GridCoord(B.X - 10, B.Y + 3), new GridCoord(B.X + 2, B.Y + 11));
        Assert.NotEmpty(path);
    }
}
