using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Hospital;

/// <summary>
/// The A&amp;E every new game starts with, built from the same room templates the player uses. There is no waiting
/// room: the player builds and fits that out (see <c>HospitalSimulation.BuildStarterWaitingRoom</c> for the old one).
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
    };

    /// <summary>Where the A&amp;E corridor meets the east and west walls: the ambulance doors of a ready-made start go here.</summary>
    public const int SpineAt = 9;

    /// <summary>Where the starter's waiting room link meets the front wall in the usual layout.</summary>
    private const int LinkAt = 22;

    /// <summary>
    /// True when the ready-made A&amp;E can be laid out around <paramref name="plan"/>: the main entrance on the
    /// north or south wall, and the ambulance doors at the east or west end of the A&amp;E corridor.
    /// </summary>
    public static bool FitsStarter(SitePlan plan) =>
        plan.EntranceSide is WallSide.North or WallSide.South
        && plan.AmbulanceSide is WallSide.East or WallSide.West
        && plan.AmbulanceAt == StarterAmbulanceAt(plan.EntranceSide);

    /// <summary>Where the ambulance doors go for a ready-made start (the corridor moves to the back when the front is north).</summary>
    public static int StarterAmbulanceAt(WallSide entranceSide) =>
        entranceSide == WallSide.North ? StarterSite.MainBuildingFootprint.Length - SpineAt - 8 : SpineAt;

    public static List<CareSpace> Create(Building building, GameDatabase database, SitePlan? plan = null)
    {
        var fit = plan is not null && FitsStarter(plan) ? plan : SitePlan.Standard;
        return Arrange(fit).Select(p =>
        {
            var template = database.RoomTemplates[p.Template];
            var rect = Place(building.Footprint, fit, p.X, p.Y, p.Width, p.Depth, p.Door, out var door);
            return RoomLayoutEngine.Build(template, database, p.Id, p.Name, rect, door);
        }).ToList();
    }

    /// <summary>
    /// The starter rooms for a plan, in the usual (front-south, ambulances-east) frame: the waiting room link
    /// slides along to meet the main entrance and the rooms beside it move out of its way.
    /// </summary>
    private static IEnumerable<Placement> Arrange(SitePlan plan)
    {
        var link = LocalLink(plan);
        if (link == LinkAt)
        {
            return Starter;
        }

        var width = StarterSite.MainBuildingFootprint.Width;
        var front = Starter.Where(p => p.Y == 17 && p.Template != "corridor").OrderBy(p => p.X).ToList();
        var placed = new List<(int X, int Width)> { (link, 8) };
        var moved = new List<Placement>();
        foreach (var room in front)
        {
            // Nearest spot to where it usually is, clear of the link and the rooms already placed.
            var w = room.Door is WallSide.North or WallSide.South ? room.Width : room.Depth;
            var x = Enumerable.Range(4, width - 4 - w + 1)
                .Where(x => placed.All(o => x + w <= o.X || o.X + o.Width <= x))
                .OrderBy(x => Math.Abs(x - room.X)).ThenBy(x => x).First();
            placed.Add((x, w));
            moved.Add(x == room.X ? room : room.Door is WallSide.North ? room with { X = x } : room with { X = x, Width = room.Depth, Depth = room.Width, Door = WallSide.North });
        }

        return Starter.Where(p => !front.Contains(p))
            .Select(p => p.Id == "corridor_link" ? p with { X = link } : p)
            .Concat(moved);
    }

    /// <summary>Where the waiting room link meets the front wall, counted in the usual (unmirrored) frame.</summary>
    private static int LocalLink(SitePlan plan) =>
        plan.AmbulanceSide == WallSide.West ? StarterSite.MainBuildingFootprint.Width - plan.EntranceAt - StarterSite.DoorWidth : plan.EntranceAt;

    /// <summary>Where the starter waiting room goes for a plan: centred on the main entrance's link.</summary>
    public static GridRect StarterWaitingRoom(GridRect building, SitePlan? plan)
    {
        var fit = plan is not null && FitsStarter(plan) ? plan : SitePlan.Standard;
        var x = Math.Clamp(15 + (LocalLink(fit) - LinkAt), 0, building.Width - 22);
        return Place(building, fit, x, 26, 22, 14, WallSide.North, out _);
    }

    /// <summary>
    /// A starter room's rectangle on the building, mirrored to suit the plan: top to bottom when the main
    /// entrance is on the north wall, left to right when the ambulances come to the west wall.
    /// </summary>
    private static GridRect Place(GridRect b, SitePlan plan, int x, int y, int width, int depth, WallSide door, out WallSide side)
    {
        var local = RoomLayoutEngine.RectFor(new GridCoord(x, y), width, depth, door);
        var flipY = plan.EntranceSide == WallSide.North;
        var flipX = plan.AmbulanceSide == WallSide.West;
        var lx = flipX ? b.Width - local.X - local.Width : local.X;
        var ly = flipY ? b.Length - local.Y - local.Length : local.Y;
        side = door switch
        {
            WallSide.North when flipY => WallSide.South,
            WallSide.South when flipY => WallSide.North,
            WallSide.East when flipX => WallSide.West,
            WallSide.West when flipX => WallSide.East,
            _ => door,
        };
        return new GridRect(b.X + lx, b.Y + ly, local.Width, local.Length);
    }

    public static NavGrid BuildNavGrid(SiteMap site, IEnumerable<CareSpace> spaces)
    {
        var all = spaces as IReadOnlyCollection<CareSpace> ?? spaces.ToList();
        var grid = new NavGrid(site.Width, site.GridLength, site.FloorStride);
        foreach (var tile in site.Bounds.Tiles())
        {
            grid.SetCost(tile, site[tile] switch
            {
                TileKind.BuildingFloor => 1,
                TileKind.Paving => 1,
                TileKind.AmbulanceBay => 1,
                TileKind.Parking => 2,
                TileKind.Road => 3,
                TileKind.TaxiRank => 3,
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

        // Upper floors: the building's outline, walled all round (there are no street doors upstairs).
        for (var floor = 1; floor < site.FloorCount; floor++)
        {
            foreach (var building in site.Buildings)
            {
                foreach (var tile in building.Areas.SelectMany(a => a.Tiles()))
                {
                    grid.SetCost(site.OnFloor(tile, floor), 1);
                }

                foreach (var (inside, outside, _) in building.OuterEdges())
                {
                    grid.BlockEdge(site.OnFloor(inside, floor), site.OnFloor(outside, floor));
                }
            }
        }

        foreach (var space in all.Where(s => s.HasWalls))
        {
            BlockPerimeter(grid, space.Rect, space.IsDoor);
        }

        VerticalTransport.AddLinks(grid, site, all);
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
