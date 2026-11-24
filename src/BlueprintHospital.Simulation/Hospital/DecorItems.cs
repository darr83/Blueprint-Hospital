using System.Text.Json.Serialization;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Hospital;

/// <summary>
/// A seat built into a decoration (a bench, sofa, armchair or cafe table), in the item's own frame: tiles across
/// from its middle, tiles towards its front, and which way someone sitting there looks (0 the item's front,
/// +1 towards +across, -1 towards -across).
/// </summary>
public sealed record DecorSeatSpec(double Across, double Front, int Turn = 0);

/// <summary>
/// Something the player can place to dress a corridor, open area or waiting room. Floor items take up
/// <see cref="Width"/> tiles across by <see cref="Depth"/> deep; wall items hang along <see cref="Width"/> tiles
/// of wall. <see cref="Kind"/> is the drawing used in both views.
/// </summary>
public sealed record DecorItem(
    string Id,
    string Name,
    string Kind,
    int Width,
    int Depth,
    int Cost,
    bool OnWall = false,
    bool OpenAreaOnly = false,
    bool Tintable = false,
    bool Comfort = false,
    bool Flat = false,
    string DefaultTint = "#2A9187",
    IReadOnlyList<DecorSeatSpec>? Seats = null)
{
    public IReadOnlyList<DecorSeatSpec> SeatSpecs => Seats ?? Array.Empty<DecorSeatSpec>();
}

/// <summary>A decoration the player has placed. <see cref="Tile"/> is the tile they clicked: the front middle of the item.</summary>
public sealed record PlacedDecoration(int Id, string ItemId, GridCoord Tile, WallSide Facing, string? Colour = null)
{
    [JsonIgnore]
    public DecorItem? Item => DecorItems.Find(ItemId);
}

/// <summary>A seat someone can wait on: a placed chair or a seat on a bench, sofa or cafe table, and the way it faces.</summary>
public sealed record WaitSeat(GridCoord Tile, WallSide Facing, double ShiftX = 0, double ShiftY = 0);

/// <summary>Every decoration the player can place.</summary>
public static class DecorItems
{
    /// <summary>The template id of the open area: a wall-less space, like a corridor, to fill with seating and decorations.</summary>
    public const string OpenAreaTemplate = "open_area";

    /// <summary>The build bar item that opens the decorations row.</summary>
    public const string BuildMenuItem = "decorations";

    /// <summary>Build bar tiles for decorations are named this followed by the item's id.</summary>
    public const string TilePrefix = "decor:";

    /// <summary>How much less likely a patient waiting near something nice to look at is to walk out.</summary>
    public const double ComfortWalkOutFactor = 0.75;

    /// <summary>How near (in tiles, either way) a comfort item has to be to cheer someone up.</summary>
    public const int ComfortRange = 4;

    public static readonly IReadOnlyList<DecorItem> All = new DecorItem[]
    {
        new("plant", "Potted plant", "Plant", 1, 1, 60, Comfort: true),
        new("palm", "Palm", "Palm", 2, 2, 180, Comfort: true),
        new("bench", "Bench", "Bench", 3, 1, 350, Seats: new DecorSeatSpec[] { new(-1, 0), new(0, 0), new(1, 0) }),
        new("water_cooler", "Water cooler", "WaterCooler", 1, 1, 150),
        new("bins", "Recycling bins", "Bins", 2, 1, 120),
        new("leaflet_rack", "Leaflet rack", "LeafletRack", 1, 1, 80),
        new("gel_station", "Hand gel station", "GelStation", 1, 1, 90),
        new("fish_tank", "Fish tank", "FishTank", 3, 1, 900, Comfort: true),
        new("sculpture", "Sculpture", "Sculpture", 1, 1, 600, Tintable: true, Comfort: true, DefaultTint: "#1F5FBF"),
        new("notice_stand", "Notice board stand", "NoticeStand", 2, 1, 140),
        new("planter", "Planter trough", "Planter", 3, 1, 250, Comfort: true),
        new("kiosk", "Information kiosk", "Kiosk", 1, 1, 1_200),
        new("floor_lamp", "Floor lamp", "FloorLamp", 1, 1, 110),

        new("sofa", "Sofa", "Sofa", 4, 2, 900, OpenAreaOnly: true, Tintable: true,
            Seats: new DecorSeatSpec[] { new(-1.15, 0.1), new(0, 0.1), new(1.15, 0.1) }),
        new("armchair", "Armchair", "Armchair", 2, 2, 400, OpenAreaOnly: true, Tintable: true, DefaultTint: "#E07A32",
            Seats: new DecorSeatSpec[] { new(0, 0.1) }),
        new("coffee_table", "Coffee table", "CoffeeTable", 3, 2, 250, OpenAreaOnly: true),
        new("cafe_table", "Cafe table for two", "CafeTable", 3, 1, 300, OpenAreaOnly: true, Tintable: true, DefaultTint: "#D6A72C",
            Seats: new DecorSeatSpec[] { new(-1.15, 0, 1), new(1.15, 0, -1) }),
        new("rug", "Rug", "Rug", 6, 4, 300, OpenAreaOnly: true, Tintable: true, Flat: true, DefaultTint: "#3F6E8C"),
        new("bookcase", "Bookcase", "Bookcase", 2, 1, 300, OpenAreaOnly: true, Comfort: true),

        new("picture", "Framed picture", "PastelPrint", 2, 1, 120, OnWall: true, Comfort: true),
        new("notice_board", "Notice board", "Noticeboard", 3, 1, 90, OnWall: true),
        new("clock", "Clock", "Clock", 1, 1, 60, OnWall: true),
        new("tv", "TV screen", "Tv", 2, 1, 500, OnWall: true, Comfort: true),
    };

    /// <summary>The colours on offer for items with fabric or paint (the chair colours).</summary>
    public static IReadOnlyList<(string Name, string Hex)> Colours => ChairColours.All;

    public static DecorItem? Find(string? id) => All.FirstOrDefault(i => i.Id == id);

    /// <summary>The colour an item is drawn in: the chosen one for items that take a colour, else its usual one.</summary>
    public static string TintOf(DecorItem item, string? colour) =>
        item.Tintable && Colours.Any(c => string.Equals(c.Hex, colour, StringComparison.OrdinalIgnoreCase)) ? colour! : item.DefaultTint;

    /// <summary>The way an item's front faces as a step (dx, dy).</summary>
    public static (int Dx, int Dy) Front(WallSide facing) => facing switch
    {
        WallSide.North => (0, -1),
        WallSide.South => (0, 1),
        WallSide.West => (-1, 0),
        _ => (1, 0),
    };

    /// <summary>The way "across" runs for an item facing <paramref name="facing"/>.</summary>
    public static (int Dx, int Dy) Across(WallSide facing)
    {
        var (fx, fy) = Front(facing);
        return (-fy, fx);
    }

    private static int FirstAcross(int width) => -((width - 1) / 2);

    /// <summary>
    /// The tiles a floor item covers: <see cref="DecorItem.Width"/> across, centred on <paramref name="tile"/>, and
    /// <see cref="DecorItem.Depth"/> deep going back from it. For a wall item, the tiles along the wall in front of it.
    /// </summary>
    public static IEnumerable<GridCoord> Footprint(DecorItem item, GridCoord tile, WallSide facing)
    {
        var (fx, fy) = Front(facing);
        var (ax, ay) = Across(facing);
        var depth = item.OnWall ? 1 : item.Depth;
        for (var d = 0; d < depth; d++)
        {
            for (var a = FirstAcross(item.Width); a < FirstAcross(item.Width) + item.Width; a++)
            {
                yield return new GridCoord(tile.X + (a * ax) - (d * fx), tile.Y + (a * ay) - (d * fy));
            }
        }
    }

    /// <summary>The middle of a floor item, in tiles (where it is drawn).</summary>
    public static (double X, double Y) Centre(DecorItem item, GridCoord tile, WallSide facing)
    {
        var (fx, fy) = Front(facing);
        var (ax, ay) = Across(facing);
        var across = FirstAcross(item.Width) + ((item.Width - 1) / 2.0);
        var back = item.OnWall ? 0 : (item.Depth - 1) / 2.0;
        return (tile.X + 0.5 + (across * ax) - (back * fx), tile.Y + 0.5 + (across * ay) - (back * fy));
    }

    /// <summary>The seats on a placed item: the tile each is on, the way it faces, and how far the seat is from the tile's middle.</summary>
    public static IEnumerable<WaitSeat> SeatsOf(PlacedDecoration placed)
    {
        if (placed.Item is not { } item)
        {
            yield break;
        }

        var (cx, cy) = Centre(item, placed.Tile, placed.Facing);
        var (fx, fy) = Front(placed.Facing);
        var (ax, ay) = Across(placed.Facing);
        foreach (var seat in item.SeatSpecs)
        {
            var (x, y) = (cx + (seat.Across * ax) + (seat.Front * fx), cy + (seat.Across * ay) + (seat.Front * fy));
            var tile = new GridCoord((int)Math.Floor(x), (int)Math.Floor(y));
            var facing = seat.Turn == 0 ? placed.Facing : SideOf(seat.Turn * ax, seat.Turn * ay);
            yield return new WaitSeat(tile, facing, x - (tile.X + 0.5), y - (tile.Y + 0.5));
        }
    }

    private static WallSide SideOf(int dx, int dy) => (dx, dy) switch
    {
        (0, -1) => WallSide.North,
        (0, 1) => WallSide.South,
        (-1, 0) => WallSide.West,
        _ => WallSide.East,
    };
}
