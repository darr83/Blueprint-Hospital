using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The player choosing which wall a room's door goes on (the Z key while building, or the room panel).</summary>
public class DoorSideTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with an east-west corridor and a north-south one meeting it, so a room in the corner has corridor on two walls.</summary>
    private static (SimulationHarness H, GridRect Corner) Wing()
    {
        var h = new SimulationHarness(5);
        h.Sim.AdjustBalance(2_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 17, 5, 17), WallSide.South)).CanBuild);

        // 12 tiles wide along the top corridor, 10 deep along the side one: the top wall has the longer stretch.
        return (h, new GridRect(B.X - 25, B.Y + 17, 12, 10));
    }

    [Fact]
    public void Lists_only_walls_with_a_corridor_beside_them()
    {
        var (h, corner) = Wing();
        Assert.Equal(new[] { WallSide.North, WallSide.West }, h.Sim.DoorSides("resus_bay", corner));
        Assert.Equal(WallSide.West, h.Sim.NextDoorSide("resus_bay", corner, WallSide.North));
        Assert.Equal(WallSide.North, h.Sim.NextDoorSide("resus_bay", corner, WallSide.West));
    }

    [Fact]
    public void The_chosen_wall_gets_the_door_and_the_layout_turns_with_it()
    {
        var (h, corner) = Wing();
        Assert.Equal(WallSide.North, h.Sim.AutoPlan("resus_bay", corner).DoorSide);

        var west = h.Sim.AutoPlan("resus_bay", corner, doorSide: WallSide.West);
        Assert.Equal(WallSide.West, west.DoorSide);
        var check = h.Sim.BuildRoom(west);
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        var room = h.Sim.Spaces.Last(s => s.TemplateId == "resus_bay");
        Assert.Equal(WallSide.West, room.DoorSide);
        Assert.All(room.Doors, d => Assert.Equal(WallSide.West, d.Side));

        // The bed stands against the wall opposite the door, so it is in the east half of the room.
        var bed = room.Furniture.First(f => f.ItemId == "hospital_bed");
        Assert.True(bed.X + (bed.Width / 2) > corner.X + (corner.Width / 2.0), "bed should be at the far (east) end");
    }

    [Fact]
    public void A_wall_with_no_corridor_falls_back_to_the_automatic_door()
    {
        var (h, corner) = Wing();
        Assert.Equal(WallSide.North, h.Sim.AutoPlan("resus_bay", corner, doorSide: WallSide.South).DoorSide);
    }

    [Fact]
    public void An_existing_room_can_have_its_door_moved_for_the_move_fee()
    {
        var (h, corner) = Wing();
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("resus_bay", corner)).CanBuild);
        var room = h.Sim.Spaces.Last(s => s.TemplateId == "resus_bay");
        Assert.Equal(WallSide.North, room.DoorSide);

        Assert.Contains(h.Sim.CheckDoorSide(room.Id, WallSide.South).Problems, p => p.Contains("no corridor"));
        Assert.Contains(h.Sim.CheckDoorSide(room.Id, WallSide.North).Problems, p => p.Contains("already"));

        var check = h.Sim.CheckDoorSide(room.Id, WallSide.West);
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        Assert.Equal((int)Math.Round(room.BuildCost * HospitalSimulation.MoveFee), check.Cost);

        var before = h.Sim.Balance;
        Assert.True(h.Sim.MoveDoor(room.Id, WallSide.West).CanBuild);
        var moved = h.Sim.FindSpace(room.Id)!;
        Assert.Equal(WallSide.West, moved.DoorSide);
        Assert.Equal(room.Name, moved.Name);
        Assert.Equal(room.Rect, moved.Rect);
        Assert.Equal(before - check.Cost, h.Sim.Balance);
    }

    [Fact]
    public void Waiting_rooms_lifts_and_corridors_keep_their_own_doors()
    {
        var (h, _) = Wing();
        var corridor = h.Sim.Spaces.First(s => s.TemplateId == "corridor");
        Assert.False(h.Sim.CheckDoorSide(corridor.Id, WallSide.North).CanBuild);
    }

    [Fact]
    public void A_moved_door_survives_save_and_load()
    {
        var (h, corner) = Wing();
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("resus_bay", corner, doorSide: WallSide.West)).CanBuild);
        var id = h.Sim.Spaces.Last(s => s.TemplateId == "resus_bay").Id;
        var save = SaveSerializer.Deserialize(SaveSerializer.Serialize(new SaveGame { Layout = h.Sim.CaptureLayout() }));
        var to = new SimulationHarness(5);
        to.Sim.RestoreLayout(save.Layout!);
        Assert.Equal(WallSide.West, to.Sim.FindSpace(id)!.DoorSide);
    }
}
