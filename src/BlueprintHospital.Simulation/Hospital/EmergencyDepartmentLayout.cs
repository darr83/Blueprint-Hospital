using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Hospital;

/// <summary>
/// The A&amp;E every new game starts with, built from the same room templates the player uses.
/// Coordinates are relative to the building's corner; one tile is 0.5 m.
/// </summary>
public static class EmergencyDepartmentLayout
{
    public const string Department = "emergency";

    /// <summary>One room of the starter layout: template, id, name, corner, size in tiles along the door wall and deep, and door side.</summary>
    private sealed record Placement(string Template, string Id, string Name, int X, int Y, int Width, int Depth, WallSide Door);

    private static readonly Placement[] Starter =
    {
        new("corridor", "corridor_main", "A&E corridor", 4, 9, 56, 8, WallSide.South),
        new("corridor", "corridor_link", "Waiting room corridor", 22, 17, 8, 9, WallSide.South),
        new("minors_cubicle", "minors_1", "Minors 1", 4, 0, 8, 9, WallSide.South),
        new("minors_cubicle", "minors_2", "Minors 2", 12, 0, 8, 9, WallSide.South),
        new("staff_room", "staff_room", "Staff room", 20, 0, 8, 9, WallSide.South),
        new("majors_cubicle", "majors_1", "Majors 1", 28, 0, 8, 9, WallSide.South),
        new("majors_cubicle", "majors_2", "Majors 2", 36, 0, 8, 9, WallSide.South),
        new("resus_bay", "resus_1", "Resus", 44, 0, 12, 9, WallSide.South),
        new("doctors_office", "doctors_office", "Doctors' office", 4, 17, 10, 9, WallSide.North),
        new("triage_room", "triage_1", "Triage", 30, 17, 9, 8, WallSide.West),
        new("nurses_station", "nurses_station", "Nurses' station", 38, 17, 10, 9, WallSide.North),
        new("waiting_room", "waiting_room", "Waiting room", 15, 26, 22, 14, WallSide.North),
    };

    public static List<CareSpace> Create(Building building, GameDatabase database)
    {
        var o = new GridCoord(building.Footprint.X, building.Footprint.Y);
        return Starter.Select(p =>
        {
            var template = database.RoomTemplates[p.Template];
            var rect = RoomLayoutEngine.RectFor(new GridCoord(o.X + p.X, o.Y + p.Y), p.Width, p.Depth, p.Door);
            return RoomLayoutEngine.Build(template, database, p.Id, p.Name, rect, p.Door);
        }).ToList();
    }

    /// <summary>Builds the walkability grid: paths and floors walkable, walls blocking except at doors and entrances.</summary>
    public static NavGrid BuildNavGrid(SiteMap site, IEnumerable<CareSpace> spaces)
    {
        var grid = new NavGrid(site.Width, site.Length);
        foreach (var tile in site.Bounds.Tiles())
        {
            grid.SetCost(tile, site[tile] switch
            {
                TileKind.BuildingFloor => 1,
                TileKind.Paving => 1,
                TileKind.AmbulanceBay => 1,
                TileKind.Parking => 2,
                TileKind.Road => 3,
                TileKind.Grass => 4,
                _ => 1,
            });
        }

        foreach (var building in site.Buildings)
        {
            foreach (var (inside, outside, side) in building.OuterEdges())
            {
                if (!building.IsOpening(inside, side))
                {
                    grid.BlockEdge(inside, outside);
                }
            }
        }

        foreach (var space in spaces.Where(s => s.HasWalls))
        {
            BlockPerimeter(grid, space.Rect, space.IsDoor);
        }

        return grid;
    }

    private static void BlockPerimeter(NavGrid grid, GridRect rect, Func<GridCoord, WallSide, bool> isOpening)
    {
        for (var x = rect.X; x < rect.Right; x++)
        {
            var north = new GridCoord(x, rect.Y);
            if (!isOpening(north, WallSide.North))
            {
                grid.BlockEdge(north, new GridCoord(x, rect.Y - 1));
            }

            var south = new GridCoord(x, rect.Bottom - 1);
            if (!isOpening(south, WallSide.South))
            {
                grid.BlockEdge(south, new GridCoord(x, rect.Bottom));
            }
        }

        for (var y = rect.Y; y < rect.Bottom; y++)
        {
            var west = new GridCoord(rect.X, y);
            if (!isOpening(west, WallSide.West))
            {
                grid.BlockEdge(west, new GridCoord(rect.X - 1, y));
            }

            var east = new GridCoord(rect.Right - 1, y);
            if (!isOpening(east, WallSide.East))
            {
                grid.BlockEdge(east, new GridCoord(rect.Right, y));
            }
        }
    }
}
