using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>
/// Big rooms filling up with beds whichever way they face, rooms built with no corridor to their door
/// ("Not in use" until one is laid), and doors placed by hand anywhere along a wall.
/// </summary>
public class RoomLayoutAndDoorTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static CareSpace Layout(string template, int w, int d, WallSide side, int? doorStart = null, int? doorRun = null)
    {
        var h = new SimulationHarness();
        var t = h.Database.RoomTemplates[template];
        return RoomLayoutEngine.Build(t, h.Database, "w", "W", RoomLayoutEngine.RectFor(new GridCoord(0, 0), w, d, side), side, doorStart, doorRun);
    }

    [Theory]
    [InlineData(WallSide.South)]
    [InlineData(WallSide.North)]
    [InlineData(WallSide.East)]
    [InlineData(WallSide.West)]
    public void A_big_ward_is_filled_with_beds_whichever_way_its_door_faces(WallSide side)
    {
        // 23 m along the door wall and 8 m deep: the "18 beds" size. It used to get only 4 beds when its door was in a side wall.
        var ward = Layout("medical_ward", 46, 16, side);
        Assert.True(ward.Beds.Count >= 12, $"{side}: only {ward.Beds.Count} beds");

        // The biggest ward there is (30 x 16 m) holds plenty either way round.
        var biggest = Layout("medical_ward", 60, 32, side);
        Assert.True(biggest.Beds.Count >= 30, $"{side}: only {biggest.Beds.Count} beds in the biggest ward");
        AssertTidy(biggest, side);
    }

    [Fact]
    public void A_deep_ward_gets_more_than_two_rows_and_a_walkway_from_the_door()
    {
        var ward = Layout("medical_ward", 30, 32, WallSide.South);
        var rows = ward.Beds.Select(b => b.PatientSpot.Y).Distinct().Count();
        Assert.True(rows >= 4, $"only {rows} rows");

        // A clear walkway runs straight in from the door to the back wall.
        var door = ward.Doors[0];
        var x = ward.Rect.X + door.Offset + (door.Width / 2);
        var beds = ward.Furniture.Where(f => f.ItemId == "hospital_bed").ToList();
        for (var y = ward.Rect.Y + 1; y < ward.Rect.Bottom - 1; y++)
        {
            Assert.DoesNotContain(beds, b => b.Overlaps(x, y, 1, 1));
        }

        AssertTidy(ward, WallSide.South);
    }

    [Fact]
    public void Small_wards_keep_their_old_layout()
    {
        // One row in a shallow ward, two facing rows from 7.5 m deep.
        Assert.All(Layout("medical_ward", 20, 10, WallSide.South).Beds, b => Assert.False(b.HeadSouth));
        var deep = Layout("medical_ward", 20, 16, WallSide.South);
        Assert.Equal(2, deep.Beds.Select(b => b.PatientSpot.Y).Distinct().Count());
    }

    [Fact]
    public void A_big_neonatal_unit_fills_with_incubators_too()
    {
        var unit = Layout("neonatal_unit", 40, 24, WallSide.East);
        Assert.True(unit.Beds.Count >= 12, $"only {unit.Beds.Count} incubators");
    }

    private static void AssertTidy(CareSpace ward, WallSide side)
    {
        var beds = ward.Furniture.Where(f => f.ItemId == "hospital_bed").ToList();
        Assert.Equal(ward.Beds.Count, beds.Count);
        foreach (var bed in beds)
        {
            Assert.False(beds.Any(o => o != bed && o.Overlaps(bed.X, bed.Y, bed.Width, bed.Length)), $"{side}: beds overlap");
            Assert.True(bed.X >= ward.Rect.X + 1 && bed.X + bed.Width <= ward.Rect.Right - 1 && bed.Y >= ward.Rect.Y + 1 && bed.Y + bed.Length <= ward.Rect.Bottom - 1, $"{side}: bed outside the room");
        }

        foreach (var spot in ward.Beds.SelectMany(b => new[] { b.PatientSpot, b.StaffSpot }))
        {
            Assert.True(ward.Rect.Contains(spot) && !ward.Rect.IsOnEdge(spot), $"{side}: {spot} is not inside the ward");
        }

        // Each bed's staff spot is on clear floor, not under another bed.
        foreach (var b in ward.Beds)
        {
            Assert.DoesNotContain(beds, f => f.Overlaps(b.StaffSpot.X, b.StaffSpot.Y, 1, 1));
        }
    }

    /// <summary>A west wing with an east-west corridor along its top; below it, open floor.</summary>
    private static SimulationHarness Wing()
    {
        var h = new SimulationHarness(5);
        h.Sim.AdjustBalance(3_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        return h;
    }

    [Fact]
    public void A_room_with_no_corridor_to_its_door_can_be_built_but_is_not_in_use_until_one_is_laid()
    {
        var h = Wing();

        // Well clear of the corridor, with open floor between.
        var rect = new GridRect(B.X - 24, B.Y + 21, 20, 10);
        var check = h.Sim.CheckRoomAt("medical_ward", rect);
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        Assert.Contains(HospitalSimulation.NotInUseNote, check.Notes);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", rect)).CanBuild);

        var ward = h.Sim.Spaces.Single(s => s.TemplateId == "medical_ward");
        Assert.False(ward.InUse);
        Assert.DoesNotContain(ward, h.Sim.Wards);
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.Contains("not in use"));

        // Lay a corridor up to its door: it comes into use.
        var door = ward.Doors[0];
        Assert.Equal(WallSide.South, door.Side);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 17, 4, 14), WallSide.South)).CanBuild);
        var link = h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 31, 30, 3), WallSide.South));
        Assert.True(link.CanBuild, string.Join("; ", link.Problems));
        ward = h.Sim.FindSpace(ward.Id)!;
        Assert.True(ward.InUse, "the ward should be in use once a corridor reaches its door");
        Assert.Contains(ward, h.Sim.Wards);
    }

    [Fact]
    public void A_corridor_serving_a_room_in_use_cannot_be_taken_away()
    {
        var h = Wing();
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("majors_cubicle", new GridRect(B.X - 24, B.Y + 17, 8, 8))).CanBuild);
        var cubicle = h.Sim.Spaces.Last(s => s.TemplateId == "majors_cubicle");
        Assert.True(cubicle.InUse);
        var corridor = h.Sim.Spaces.First(s => s.TemplateId == "corridor" && s.Rect.X == B.X - 30);
        Assert.Contains(h.Sim.CheckDemolish(corridor.Id).Problems, p => p.Contains("cut off"));
    }

    [Fact]
    public void A_door_can_be_placed_by_hand_along_any_wall_and_the_room_is_laid_out_again_around_it()
    {
        var h = Wing();
        var rect = new GridRect(B.X - 26, B.Y + 17, 22, 12);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", rect)).CanBuild);
        var ward = h.Sim.Spaces.Single(s => s.TemplateId == "medical_ward");
        Assert.Equal(WallSide.North, ward.DoorSide);

        // Point at the corridor just outside the north wall, near its west end.
        var target = new GridCoord(rect.X + 4, rect.Y - 1);
        var check = h.Sim.CheckDoorAt(ward.Id, target);
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        Assert.Empty(check.Notes);
        Assert.True(h.Sim.MoveDoorTo(ward.Id, target).CanBuild);
        var moved = h.Sim.FindSpace(ward.Id)!;
        var door = moved.Doors[0];
        Assert.Equal(WallSide.North, door.Side);
        Assert.InRange(rect.X + door.Offset, target.X - door.Width, target.X);
        Assert.True(moved.InUse);

        // Nothing stands just inside the new door.
        Assert.DoesNotContain(moved.Furniture, f => f.Overlaps(target.X, rect.Y + 1, 1, 1));
        Assert.Contains(h.Sim.CheckDoorAt(ward.Id, target).Problems, p => p.Contains("already"));

        // A hand-placed door survives save and load.
        var save = SaveSerializer.Deserialize(SaveSerializer.Serialize(new SaveGame { Layout = h.Sim.CaptureLayout() }));
        var to = new SimulationHarness(5);
        to.Sim.RestoreLayout(save.Layout!);
        Assert.Equal(door, to.Sim.FindSpace(ward.Id)!.Doors[0]);

        // On the south wall, with only open floor outside: allowed, but the ward goes out of use.
        var south = new GridCoord(rect.X + 10, rect.Bottom);
        var away = h.Sim.CheckDoorAt(ward.Id, south);
        Assert.True(away.CanBuild, string.Join("; ", away.Problems));
        Assert.Contains(HospitalSimulation.NotInUseNote, away.Notes);
        Assert.True(h.Sim.MoveDoorTo(ward.Id, south).CanBuild);
        Assert.False(h.Sim.FindSpace(ward.Id)!.InUse);
    }

    [Fact]
    public void Turning_the_door_with_Z_steps_through_every_wall()
    {
        var h = Wing();
        var rect = new GridRect(B.X - 24, B.Y + 17, 12, 10);
        Assert.Equal(WallSide.East, h.Sim.NextDoorSide("resus_bay", rect, WallSide.South, anyWall: true));
        var pinned = h.Sim.AutoPlan("resus_bay", rect, doorSide: WallSide.South, strict: true);
        Assert.Equal(WallSide.South, pinned.DoorSide);
        Assert.Contains(HospitalSimulation.NotInUseNote, h.Sim.CheckRoom(pinned).Notes);
    }
}
