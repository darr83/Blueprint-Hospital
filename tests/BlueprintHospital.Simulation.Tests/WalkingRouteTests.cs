using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Routes are straightened across open floor, but never through walls, round corners too tightly, or off the path.</summary>
public class WalkingRouteTests
{
    private static NavGrid Open(int size = 20)
    {
        var grid = new NavGrid(size, size);
        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                grid.SetCost(new GridCoord(x, y), 1);
            }
        }

        return grid;
    }

    [Fact]
    public void Open_floor_is_crossed_in_a_straight_line()
    {
        var grid = Open();
        var route = grid.FindRoute(new GridCoord(1, 1), new GridCoord(12, 7));
        Assert.Equal(new[] { new GridCoord(1, 1), new GridCoord(12, 7) }, route);
    }

    [Fact]
    public void A_route_through_a_door_bends_at_the_door_and_never_crosses_the_wall()
    {
        var grid = Open();

        // A wall along x = 10 with a two-tile doorway at y = 15..16.
        for (var y = 0; y < 20; y++)
        {
            if (y is not (15 or 16))
            {
                grid.BlockEdge(new GridCoord(9, y), new GridCoord(10, y));
            }
        }

        var path = grid.FindPath(new GridCoord(2, 2), new GridCoord(17, 3));
        var route = grid.FindRoute(new GridCoord(2, 2), new GridCoord(17, 3));
        Assert.True(route.Count < path.Count);
        Assert.Equal(new GridCoord(2, 2), route[0]);
        Assert.Equal(new GridCoord(17, 3), route[^1]);
        for (var i = 1; i < route.Count; i++)
        {
            Assert.True(grid.ClearLine(route[i - 1], route[i], 1), $"Leg {route[i - 1]} to {route[i]} crosses a wall.");
        }

        // It must pass through the doorway.
        Assert.Contains(route, t => t.Y is >= 13 and <= 18 && t.X is >= 8 and <= 11);
    }

    [Fact]
    public void Smoothing_keeps_off_dearer_ground()
    {
        var grid = Open();

        // Grass (cost 4) everywhere except an L-shaped path along y = 2 then x = 15.
        for (var x = 0; x < 20; x++)
        {
            for (var y = 0; y < 20; y++)
            {
                grid.SetCost(new GridCoord(x, y), (byte)((y == 2 && x <= 15) || (x == 15 && y >= 2) ? 1 : 4));
            }
        }

        var route = grid.FindRoute(new GridCoord(2, 2), new GridCoord(15, 14));
        Assert.Contains(new GridCoord(15, 2), route);
    }
}
