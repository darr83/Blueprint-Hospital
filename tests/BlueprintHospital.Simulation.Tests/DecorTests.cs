using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class DecorTests
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

    [Fact]
    public void New_rooms_start_with_the_look_for_their_kind()
    {
        var (h, room) = WithStaffRoom();
        Assert.Equal(DecorCatalogue.StartingLook(CareSpaceKind.StaffRoom), h.Sim.DecorOf(room));
        Assert.Equal(FloorFinish.WoodPlanks, h.Sim.DecorOf(room).Floor);
    }

    [Fact]
    public void Corridors_and_lifts_stay_standard()
    {
        var (h, _) = WithStaffRoom();
        foreach (var space in h.Sim.Spaces.Where(s => s.Kind is CareSpaceKind.Corridor or CareSpaceKind.Lift or CareSpaceKind.Stairs))
        {
            Assert.True(h.Sim.DecorOf(space).IsStandard);
        }
    }

    [Fact]
    public void Redecorating_charges_by_floor_area_for_the_parts_that_change()
    {
        var (h, room) = WithStaffRoom();
        var start = h.Sim.Balance;
        var look = h.Sim.DecorOf(room) with { Floor = FloorFinish.CarpetTiles, FloorColour = "#8FA7B8" };

        var expected = (int)Math.Round(room.AreaSquareMetres * DecorCatalogue.PricePerSquareMetre(FloorFinish.CarpetTiles));
        Assert.Equal(expected, h.Sim.DecorateCost(room, look));
        var result = h.Sim.Decorate(room.Id, look);

        Assert.True(result.CanBuild);
        Assert.Equal(expected, result.Cost);
        Assert.Equal(start - expected, h.Sim.Balance);
        Assert.Equal(look, h.Sim.DecorOf(room));

        // The same look again costs nothing.
        Assert.Equal(0, h.Sim.DecorateCost(room, look));
    }

    [Fact]
    public void Redecorating_needs_the_money()
    {
        var (h, room) = WithStaffRoom();
        h.Sim.AdjustBalance(-h.Sim.Balance);
        var before = h.Sim.DecorOf(room);

        var result = h.Sim.Decorate(room.Id, new RoomDecor(FloorFinish.Terrazzo, null, WallFinish.Tiles));

        Assert.False(result.CanBuild);
        Assert.Equal(before, h.Sim.DecorOf(room));
    }

    [Fact]
    public void A_chosen_look_is_used_for_new_rooms_of_that_type()
    {
        var (h, room) = WithStaffRoom();
        var look = new RoomDecor(FloorFinish.CarpetTiles, "#B9A3C9", WallFinish.Paint, "#E9E1F2");
        Assert.True(h.Sim.Decorate(room.Id, look, forNewRooms: true).CanBuild);

        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("staff_room", new GridRect(B.X - 21, B.Y, 9, 9))).CanBuild);
        var second = h.Sim.Spaces.Last(s => s.TemplateId == "staff_room");

        Assert.NotEqual(room.Id, second.Id);
        Assert.Equal(look, h.Sim.DecorOf(second));
    }

    [Fact]
    public void Moving_a_room_keeps_its_finish()
    {
        var (h, room) = WithStaffRoom();
        var look = new RoomDecor(FloorFinish.Chequer, null, WallFinish.Stripe, "#DCEBF5");
        Assert.True(h.Sim.Decorate(room.Id, look).CanBuild);

        Assert.True(h.Sim.MoveRoom(room.Id, h.Sim.AutoPlan("staff_room", new GridRect(B.X - 21, B.Y, 9, 9))).CanBuild);

        Assert.Equal(look, h.Sim.DecorOf(h.Sim.FindSpace(room.Id)!));
    }

    [Fact]
    public void Finishes_survive_save_and_load()
    {
        var (h, room) = WithStaffRoom();
        var look = new RoomDecor(FloorFinish.Terrazzo, "#E6E1D8", WallFinish.TwoTone, "#DCEBF5");
        Assert.True(h.Sim.Decorate(room.Id, look, forNewRooms: true).CanBuild);

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

        Assert.Equal(look, loaded.Sim.DecorOf(loaded.Sim.FindSpace(room.Id)!));
        Assert.True(loaded.Sim.HasDecorDefault("staff_room"));
        Assert.Equal(look, loaded.Sim.NewRoomDecor("staff_room"));
    }

    [Fact]
    public void Older_saves_without_finishes_load_with_the_standard_look()
    {
        var (h, room) = WithStaffRoom();
        var people = h.Sim.CapturePeople();
        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = people })
            .Replace("\"decor\":", "\"old_decor\":", StringComparison.Ordinal)
            .Replace("\"decor_defaults\":", "\"old_decor_defaults\":", StringComparison.Ordinal);
        var save = SaveSerializer.Deserialize(json);
        var loaded = new SimulationHarness();
        loaded.Sim.RestoreLayout(save.Layout!);
        loaded.Sim.RestorePeople(save.People!);

        Assert.All(loaded.Sim.Spaces, s => Assert.True(loaded.Sim.DecorOf(s).IsStandard));
        Assert.False(loaded.Sim.HasDecorDefault("staff_room"));
        _ = room;
    }
}
