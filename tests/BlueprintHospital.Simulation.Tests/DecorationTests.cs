using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class DecorationTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A corridor along the extension's west (outside) wall, and an open area off its south side.</summary>
    private static (SimulationHarness H, CareSpace Open) WithOpenArea()
    {
        var h = new SimulationHarness();
        h.Sim.AdjustBalance(5_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        var built = h.Sim.BuildRoom(new RoomPlan(DecorItems.OpenAreaTemplate, new GridRect(B.X - 20, B.Y + 17, 12, 10), WallSide.South));
        Assert.True(built.CanBuild, string.Join(" ", built.Problems));
        return (h, h.Sim.Spaces.Single(s => s.TemplateId == DecorItems.OpenAreaTemplate));
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

    /// <summary>A corridor tile against the building's outside (west) wall.</summary>
    private static GridCoord ByWall(int dy) => new(B.X - 30, B.Y + 9 + dy);

    [Fact]
    public void An_open_area_is_wall_free_floor_with_a_wood_floor()
    {
        var (h, open) = WithOpenArea();
        Assert.Equal(CareSpaceKind.Corridor, open.Kind);
        Assert.False(open.HasWalls);
        Assert.Equal(FloorFinish.WoodPlanks, h.Sim.DecorOf(open).Floor);
    }

    [Fact]
    public void A_bench_against_a_corridor_wall_is_three_seats_and_people_walk_round_it()
    {
        var (h, _) = WithOpenArea();
        var balance = h.Sim.Balance;
        var placed = h.Sim.PlaceDecoration("bench", ByWall(3), WallSide.East);
        Assert.True(placed.CanBuild, string.Join(" ", placed.Problems));
        Assert.Equal(balance - placed.Cost, h.Sim.Balance);

        var bench = Assert.Single(h.Sim.PlacedDecorations);
        var tiles = h.Sim.FootprintOf(bench).ToList();
        Assert.Equal(3, tiles.Count);
        Assert.All(tiles, t => Assert.Equal(B.X - 30, t.X));
        Assert.Equal(3, h.Sim.DecorSeats.Count);
        Assert.All(h.Sim.DecorSeats, s => Assert.Equal(WallSide.East, s.Facing));
        Assert.All(tiles, t => Assert.True(h.Sim.Nav.CostAt(t) > 1));
        Assert.Contains(ByWall(3), h.Sim.OutpatientSeats);
        Assert.Contains(ByWall(3), h.Sim.SeatTiles);
    }

    [Fact]
    public void On_a_plain_corridor_items_stand_against_a_wall_and_leave_room_to_pass()
    {
        var (h, _) = WithOpenArea();
        var middle = new GridCoord(B.X - 25, B.Y + 12);
        Assert.Contains(h.Sim.CheckDecoration("bench", middle, WallSide.South).Problems, p => p.Contains("wall"));
        Assert.False(h.Sim.CheckDecoration("sofa", ByWall(3), WallSide.East).CanBuild);
        Assert.True(h.Sim.CheckDecoration("plant", ByWall(1), WallSide.East).CanBuild);
    }

    [Fact]
    public void Open_area_furniture_goes_anywhere_in_an_open_area_but_not_on_top_of_anything()
    {
        var (h, open) = WithOpenArea();
        var middle = new GridCoord(open.Rect.X + 6, open.Rect.Y + 5);
        Assert.True(h.Sim.PlaceDecoration("sofa", middle, WallSide.South, "#A23B4C").CanBuild);
        Assert.Equal("#A23B4C", h.Sim.PlacedDecorations[0].Colour);
        Assert.Equal(3, h.Sim.DecorSeats.Count);
        Assert.Contains(h.Sim.CheckDecoration("palm", middle, WallSide.South).Problems, p => p.Contains("already"));

        // A rug lies under the furniture; chairs go on it, but not on the sofa.
        Assert.True(h.Sim.PlaceDecoration("rug", new GridCoord(middle.X, middle.Y + 2), WallSide.South).CanBuild);
        Assert.False(h.Sim.CheckChair(middle, WallSide.South, ChairColours.Default).CanBuild);
        Assert.True(h.Sim.CheckChair(new GridCoord(middle.X - 2, middle.Y + 2), WallSide.South, ChairColours.Default).CanBuild);
    }

    [Fact]
    public void Wall_items_hang_on_a_solid_wall_only()
    {
        var (h, open) = WithOpenArea();
        Assert.True(h.Sim.PlaceDecoration("picture", ByWall(5), WallSide.East).CanBuild);
        Assert.Contains(h.Sim.CheckDecoration("clock", ByWall(5), WallSide.East).Problems, p => p.Contains("already"));
        Assert.False(h.Sim.CheckDecoration("clock", new GridCoord(open.Rect.X + 4, open.Rect.Y + 4), WallSide.South).CanBuild);

        // A wall item doesn't take up floor: a plant can stand under the picture.
        Assert.True(h.Sim.CheckDecoration("plant", ByWall(5), WallSide.East).CanBuild);
    }

    [Fact]
    public void Decorations_turn_recolour_and_sell_for_half_and_survive_save_and_load()
    {
        var (h, open) = WithOpenArea();
        var tile = new GridCoord(open.Rect.X + 4, open.Rect.Y + 4);
        Assert.True(h.Sim.PlaceDecoration("armchair", tile, WallSide.South).CanBuild);
        var chair = h.Sim.PlacedDecorations[0];
        Assert.Equal("#E07A32", chair.Colour);
        Assert.True(h.Sim.RecolourDecoration(chair.Id, "#2A9187"));
        var turn = h.Sim.NextDecorationFacing(chair.Id);
        Assert.NotNull(turn);
        Assert.True(h.Sim.MoveDecoration(chair.Id, tile, turn!.Value).CanBuild);

        var loaded = Reload(h);
        var back = Assert.Single(loaded.Sim.PlacedDecorations);
        Assert.Equal(("armchair", tile, turn.Value, "#2A9187"), (back.ItemId, back.Tile, back.Facing, back.Colour));
        Assert.Single(loaded.Sim.DecorSeats);

        var balance = loaded.Sim.Balance;
        Assert.True(loaded.Sim.RemoveDecoration(back.Id));
        Assert.Equal(balance + (DecorItems.Find("armchair")!.Cost / 2), loaded.Sim.Balance);
        Assert.Empty(loaded.Sim.DecorSeats);
    }

    [Fact]
    public void Selling_the_open_area_takes_its_decorations_away_and_refunds_them()
    {
        var (h, open) = WithOpenArea();
        Assert.True(h.Sim.PlaceDecoration("coffee_table", new GridCoord(open.Rect.X + 6, open.Rect.Y + 5), WallSide.South).CanBuild);
        Assert.True(h.Sim.DemolishRoom(open.Id).CanBuild);
        Assert.Empty(h.Sim.PlacedDecorations);
    }

    [Fact]
    public void Plants_and_pictures_make_the_wait_nicer()
    {
        var (h, open) = WithOpenArea();
        var tile = new GridCoord(open.Rect.X + 4, open.Rect.Y + 4);
        Assert.False(h.Sim.NearComfort(tile));
        Assert.True(h.Sim.PlaceDecoration("plant", new GridCoord(tile.X + 2, tile.Y), WallSide.South).CanBuild);
        Assert.True(h.Sim.NearComfort(tile));
        Assert.False(h.Sim.NearComfort(new GridCoord(tile.X + 10, tile.Y)));
    }
}
