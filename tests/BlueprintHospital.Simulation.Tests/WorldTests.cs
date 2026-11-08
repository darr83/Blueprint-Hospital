using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class WorldTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 3)]
    [InlineData(-4, 7)]
    [InlineData(159, 119)]
    public void Screen_to_tile_inverts_tile_centre(int x, int y)
    {
        var projection = new IsoProjection();
        var (sx, sy) = projection.TileCentre(new GridCoord(x, y));
        Assert.Equal(new GridCoord(x, y), projection.ScreenToTile(sx, sy));
    }

    [Fact]
    public void Projection_is_two_to_one()
    {
        var projection = new IsoProjection(64, 32);
        Assert.Equal((32.0, 16.0), projection.GridToScreen(1, 0));
        Assert.Equal((-32.0, 16.0), projection.GridToScreen(0, 1));
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 1)]
    [InlineData(3.5, 7)]
    [InlineData(3.6, 8)]
    [InlineData(4.0, 8)]
    public void Metres_round_up_to_whole_tiles(double metres, int tiles)
    {
        Assert.Equal(tiles, Units.MetresToTiles(metres));
    }

    [Fact]
    public void Area_is_in_square_metres()
    {
        // A 4.0 m x 4.5 m minor treatment room.
        Assert.Equal(18.0, Units.AreaSquareMetres(8, 9));
    }

    [Fact]
    public void Rect_intersection_and_edges()
    {
        var a = new GridRect(0, 0, 4, 4);
        Assert.True(a.Intersects(new GridRect(3, 3, 2, 2)));
        Assert.False(a.Intersects(new GridRect(4, 0, 2, 2)));
        Assert.True(a.IsOnEdge(new GridCoord(0, 2)));
        Assert.False(a.IsOnEdge(new GridCoord(1, 1)));
        Assert.Equal(16, a.Tiles().Count());
    }

    [Fact]
    public void Starter_site_has_building_with_both_entrances()
    {
        var site = StarterSite.Create();

        var building = Assert.Single(site.Buildings);
        Assert.Equal(TileKind.BuildingFloor, site[new GridCoord(building.Footprint.X + 5, building.Footprint.Y + 5)]);
        Assert.Contains(building.Entrances, e => e.IsAmbulanceEntrance);
        Assert.Contains(building.Entrances, e => !e.IsAmbulanceEntrance);
        Assert.Contains(site.Bounds.Tiles(), t => site[t] == TileKind.AmbulanceBay);
        Assert.Equal(30.0, Units.TilesToMetres(building.Footprint.Width));
    }

    [Fact]
    public void Entrances_open_the_wall()
    {
        var site = StarterSite.Create();
        var building = site.Buildings[0];
        var main = building.Entrances.Single(e => e.Id == "main_entrance");

        Assert.True(building.IsOpening(main.Tile, WallSide.South));
        Assert.False(building.IsOpening(main.Tile, WallSide.East));
        Assert.False(building.IsOpening(new GridCoord(building.Footprint.X, building.Footprint.Bottom - 1), WallSide.South));
    }

    [Fact]
    public void Overlapping_buildings_are_rejected()
    {
        var site = StarterSite.Create();
        var overlap = new Building("annex", "Annex", new GridRect(StarterSite.MainBuildingFootprint.X + 1, StarterSite.MainBuildingFootprint.Y + 1, 4, 4));
        Assert.Throws<ArgumentException>(() => site.AddBuilding(overlap));
    }
}
