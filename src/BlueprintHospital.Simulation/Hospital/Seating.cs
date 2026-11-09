using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Hospital;

/// <summary>
/// A waiting chair the player has placed on a corridor or in a waiting room. <see cref="Facing"/> is the way
/// someone sitting on it looks; <see cref="Colour"/> is its upholstery, as a hex colour.
/// </summary>
public sealed record PlacedChair(GridCoord Tile, WallSide Facing, string Colour);

/// <summary>The upholstery colours the player can choose for waiting chairs.</summary>
public static class ChairColours
{
    public static readonly IReadOnlyList<(string Name, string Hex)> All = new[]
    {
        ("Hospital blue", "#4F7FB0"),
        ("Teal", "#2A9187"),
        ("Leaf green", "#5E9E4A"),
        ("Plum", "#7E6BB5"),
        ("Burgundy", "#A23B4C"),
        ("Orange", "#E07A32"),
        ("Mustard", "#D6A72C"),
        ("Charcoal", "#4A5058"),
    };

    public static string Default => All[0].Hex;

    /// <summary>The colour's name, or the hex code if it isn't one of the choices.</summary>
    public static string NameOf(string hex) =>
        All.FirstOrDefault(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase)).Name ?? hex;

    /// <summary>A known colour (saves can hold anything), falling back to the default.</summary>
    public static string Normalise(string? hex) =>
        All.FirstOrDefault(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase)).Hex ?? Default;
}

/// <summary>
/// A reception desk the player has placed. <see cref="Tile"/> is the middle of the desk and <see cref="Facing"/>
/// the side patients stand on; the receptionist sits on the other side.
/// </summary>
public sealed record ReceptionDesk(GridCoord Tile, WallSide Facing);

/// <summary>What the player has fitted in a waiting room: its reception desk and doors (chairs are kept separately).</summary>
public sealed record WaitingFittings(ReceptionDesk? Desk, IReadOnlyList<Doorway> Doors)
{
    public static WaitingFittings Empty { get; } = new(null, Array.Empty<Doorway>());
}

/// <summary>A reception desk laid out in a room: the desk itself, where the receptionist sits, and the queue in front.</summary>
public sealed record DeskLayout(PlacedFurniture Furniture, GridCoord StaffSpot, GridCoord PatientSpot, IReadOnlyList<GridCoord> DeskTiles, IReadOnlyList<GridCoord> Queue)
{
    /// <summary>Every tile the desk, receptionist and queue use, which chairs must keep clear of.</summary>
    public IEnumerable<GridCoord> AllTiles => DeskTiles.Append(StaffSpot).Concat(Queue);
}

/// <summary>Geometry shared by waiting rooms and Outpatients reception: reception desks, queues and doorways.</summary>
public static class WaitingRoomLayout
{
    /// <summary>A reception desk is 2.5 m long: 5 tiles.</summary>
    public const int DeskTiles = 5;

    /// <summary>The longest reception queue, in tiles.</summary>
    public const int MaxQueue = 8;

    /// <summary>A placed door in a waiting room is 2 m wide.</summary>
    public const int DoorWidth = 4;

    public static (int X, int Y) Step(WallSide side) => side switch
    {
        WallSide.North => (0, -1),
        WallSide.South => (0, 1),
        WallSide.East => (1, 0),
        _ => (-1, 0),
    };

    public static WallSide Opposite(WallSide side) => side switch
    {
        WallSide.North => WallSide.South,
        WallSide.South => WallSide.North,
        WallSide.East => WallSide.West,
        _ => WallSide.East,
    };

    /// <summary>The next way round, clockwise on the grid: north, east, south, west.</summary>
    public static WallSide Clockwise(WallSide side) => side switch
    {
        WallSide.North => WallSide.East,
        WallSide.East => WallSide.South,
        WallSide.South => WallSide.West,
        _ => WallSide.North,
    };

    /// <summary>Lays out a desk in <paramref name="room"/>; the queue stops at the wall or anything in <paramref name="keepClear"/>.</summary>
    public static DeskLayout Desk(GridRect room, ReceptionDesk desk, ISet<GridCoord> keepClear)
    {
        var (fx, fy) = Step(desk.Facing);
        var (ax, ay) = (-fy, fx);
        var half = DeskTiles / 2;
        var tiles = Enumerable.Range(-half, DeskTiles).Select(i => new GridCoord(desk.Tile.X + (i * ax), desk.Tile.Y + (i * ay))).ToList();

        // The desk top runs across the middle of its row of tiles, 0.8 of a tile deep.
        var (cx, cy) = (desk.Tile.X + 0.5, desk.Tile.Y + 0.5);
        var (x0, y0) = (cx - ((half + 0.5) * ax) - (0.4 * fx), cy - ((half + 0.5) * ay) - (0.4 * fy));
        var (x1, y1) = (cx + ((half + 0.5) * ax) + (0.4 * fx), cy + ((half + 0.5) * ay) + (0.4 * fy));
        var furniture = new PlacedFurniture("reception_desk", Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));

        var staff = new GridCoord(desk.Tile.X - fx, desk.Tile.Y - fy);
        var patient = new GridCoord(desk.Tile.X + fx, desk.Tile.Y + fy);
        var queue = new List<GridCoord>();
        for (var t = patient; queue.Count < MaxQueue && room.Contains(t) && !keepClear.Contains(t); t = new GridCoord(t.X + fx, t.Y + fy))
        {
            queue.Add(t);
        }

        if (queue.Count == 0)
        {
            queue.Add(patient);
        }

        return new DeskLayout(furniture, staff, patient, tiles, queue);
    }

    /// <summary>The floor tiles just inside each doorway of a room.</summary>
    public static HashSet<GridCoord> DoorTiles(GridRect r, IEnumerable<Doorway> doors)
    {
        var tiles = new HashSet<GridCoord>();
        foreach (var door in doors)
        {
            for (var i = door.Offset; i < door.Offset + door.Width; i++)
            {
                tiles.Add(door.Side switch
                {
                    WallSide.North => new GridCoord(r.X + i, r.Y),
                    WallSide.South => new GridCoord(r.X + i, r.Bottom - 1),
                    WallSide.West => new GridCoord(r.X, r.Y + i),
                    _ => new GridCoord(r.Right - 1, r.Y + i),
                });
            }
        }

        return tiles;
    }
}
