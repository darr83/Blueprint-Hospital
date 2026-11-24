using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class RoomColourAndChairStyleTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static (SimulationHarness H, CareSpace Room) WithStaffRoom()
    {
        var h = new SimulationHarness();
        h.Sim.AdjustBalance(5_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("staff_room", new GridRect(B.X - 30, B.Y, 9, 9))).CanBuild);
        return (h, h.Sim.Spaces.Last(s => s.TemplateId == "staff_room"));
    }

    private static SimulationHarness Reload(SimulationHarness h)
    {
        var save = SaveSerializer.Deserialize(SaveSerializer.Serialize(new SaveGame
        {
            Clock = h.Clock.CaptureState(),
            Layout = h.Sim.CaptureLayout(),
            People = h.Sim.CapturePeople(),
        }));
        var loaded = new SimulationHarness();
        loaded.Clock.RestoreState(save.Clock);
        loaded.Sim.RestoreLayout(save.Layout!);
        loaded.Sim.RestorePeople(save.People!);
        return loaded;
    }

    [Fact]
    public void A_room_can_take_a_colour_scheme_for_free_and_go_back_to_its_department_colour()
    {
        var (h, room) = WithStaffRoom();
        var balance = h.Sim.Balance;
        Assert.Null(h.Sim.RoomColourOf(room));

        Assert.True(h.Sim.SetRoomColour(room.Id, "#00897b"));
        Assert.Equal("#00897B", h.Sim.RoomColourOf(room));
        Assert.Equal(balance, h.Sim.Balance);

        // Only the scheme colours are allowed: anything else means the department's colour.
        Assert.True(h.Sim.SetRoomColour(room.Id, "#123456"));
        Assert.Null(h.Sim.RoomColourOf(room));

        Assert.True(h.Sim.SetRoomColour(room.Id, "#E53935"));
        Assert.True(h.Sim.SetRoomColour(room.Id, null));
        Assert.Null(h.Sim.RoomColourOf(room));
    }

    [Fact]
    public void Corridors_have_no_colour_scheme()
    {
        var (h, _) = WithStaffRoom();
        var corridor = h.Sim.Spaces.First(s => s.Kind == CareSpaceKind.Corridor);
        Assert.False(h.Sim.SetRoomColour(corridor.Id, "#E53935"));
        Assert.Null(h.Sim.RoomColourOf(corridor));
    }

    [Fact]
    public void Colour_schemes_survive_moving_and_save_and_load()
    {
        var (h, room) = WithStaffRoom();
        Assert.True(h.Sim.SetRoomColour(room.Id, "#7E57C2"));
        Assert.True(h.Sim.MoveRoom(room.Id, h.Sim.AutoPlan("staff_room", new GridRect(B.X - 21, B.Y, 9, 9))).CanBuild);
        Assert.Equal("#7E57C2", h.Sim.RoomColourOf(h.Sim.FindSpace(room.Id)!));

        var loaded = Reload(h);
        Assert.Equal("#7E57C2", loaded.Sim.RoomColourOf(loaded.Sim.FindSpace(room.Id)!));
    }

    [Fact]
    public void Chairs_come_in_styles_that_can_be_changed_for_free()
    {
        var (h, _) = WithStaffRoom();
        var tile = new GridCoord(B.X - 20, B.Y + 9);
        Assert.True(h.Sim.PlaceChair(tile, WallSide.South, ChairColours.Default, "beam_seat").CanBuild);
        Assert.Equal("beam_seat", h.Sim.ChairAt(tile)!.ItemId);

        // The same chair again is refused; a new style is a free change.
        Assert.False(h.Sim.CheckChair(tile, WallSide.South, ChairColours.Default, "beam_seat").CanBuild);
        var balance = h.Sim.Balance;
        Assert.True(h.Sim.PlaceChair(tile, WallSide.South, ChairColours.Default, "lounge_chair").CanBuild);
        Assert.Equal("lounge_chair", h.Sim.ChairAt(tile)!.ItemId);
        Assert.Equal(balance, h.Sim.Balance);

        // Unknown styles (and older saves with none) are the classic chair.
        Assert.True(h.Sim.PlaceChair(tile, WallSide.South, ChairColours.Default, "throne").CanBuild);
        Assert.Equal(ChairStyles.Classic, h.Sim.ChairAt(tile)!.ItemId);
        Assert.Equal(ChairStyles.Classic, new PlacedChair(tile, WallSide.South, ChairColours.Default).ItemId);
        Assert.Equal(4, ChairStyles.All.Count);
    }

    [Fact]
    public void Chair_styles_survive_save_and_load()
    {
        var (h, _) = WithStaffRoom();
        var tile = new GridCoord(B.X - 20, B.Y + 9);
        Assert.True(h.Sim.PlaceChair(tile, WallSide.North, "#A23B4C", "visitor_chair").CanBuild);

        var loaded = Reload(h);
        var chair = loaded.Sim.ChairAt(tile)!;
        Assert.Equal("visitor_chair", chair.ItemId);
        Assert.Equal("#A23B4C", chair.Colour);
        Assert.Equal(WallSide.North, chair.Facing);
    }
}
