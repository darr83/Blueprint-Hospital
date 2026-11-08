namespace BlueprintHospital.Simulation.World;

/// <summary>What a tile on the hospital site is made of.</summary>
public enum TileKind : byte
{
    Grass,
    Paving,
    Road,
    AmbulanceBay,
    Parking,
    BuildingFloor,
}

/// <summary>An entrance in a building's outer wall. <see cref="Tile"/> is the floor tile just inside.</summary>
public sealed record Entrance(string Id, string Name, GridCoord Tile, WallSide Side, int WidthTiles, bool IsAmbulanceEntrance);

/// <summary>Which outer wall of a building something sits on.</summary>
public enum WallSide
{
    North,  // y == rect.Y      (top-right edge on screen)
    East,   // x == rect.Right-1 (bottom-right edge on screen)
    South,  // y == rect.Bottom-1 (bottom-left edge on screen)
    West,   // x == rect.X      (top-left edge on screen)
}

/// <summary>A hospital building: a floor footprint enclosed by outer walls with entrances.</summary>
public sealed class Building(string id, string name, GridRect footprint)
{
    private readonly List<Entrance> _entrances = new();

    public string Id { get; } = id;

    public string Name { get; } = name;

    public GridRect Footprint { get; } = footprint;

    public IReadOnlyList<Entrance> Entrances => _entrances;

    public void AddEntrance(Entrance entrance)
    {
        if (!Footprint.IsOnEdge(entrance.Tile))
        {
            throw new ArgumentException($"Entrance '{entrance.Id}' at {entrance.Tile} is not on the edge of building '{Id}'.", nameof(entrance));
        }

        _entrances.Add(entrance);
    }

    /// <summary>True if the given outer wall segment of <paramref name="tile"/> is an opening.</summary>
    public bool IsOpening(GridCoord tile, WallSide side) =>
        _entrances.Any(e => e.Side == side && Covers(e, tile));

    private static bool Covers(Entrance e, GridCoord tile) => e.Side switch
    {
        WallSide.North or WallSide.South => tile.Y == e.Tile.Y && tile.X >= e.Tile.X && tile.X < e.Tile.X + e.WidthTiles,
        _ => tile.X == e.Tile.X && tile.Y >= e.Tile.Y && tile.Y < e.Tile.Y + e.WidthTiles,
    };
}

/// <summary>The whole hospital site: ground tiles plus the buildings standing on it.</summary>
public sealed class SiteMap
{
    private readonly TileKind[] _tiles;
    private readonly List<Building> _buildings = new();

    public SiteMap(int width, int length, TileKind fill = TileKind.Grass)
    {
        if (width <= 0 || length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Site must be at least one tile in each direction.");
        }

        Width = width;
        Length = length;
        _tiles = new TileKind[width * length];
        Array.Fill(_tiles, fill);
    }

    public int Width { get; }

    public int Length { get; }

    public GridRect Bounds => new(0, 0, Width, Length);

    public IReadOnlyList<Building> Buildings => _buildings;

    public bool InBounds(GridCoord c) => Bounds.Contains(c);

    public TileKind this[GridCoord c]
    {
        get => InBounds(c) ? _tiles[(c.Y * Width) + c.X] : throw new ArgumentOutOfRangeException(nameof(c), c, "Outside the site.");
        set
        {
            if (!InBounds(c))
            {
                throw new ArgumentOutOfRangeException(nameof(c), c, "Outside the site.");
            }

            _tiles[(c.Y * Width) + c.X] = value;
        }
    }

    public void Fill(GridRect rect, TileKind kind)
    {
        foreach (var tile in rect.Tiles())
        {
            this[tile] = kind;
        }
    }

    public void AddBuilding(Building building)
    {
        if (!Bounds.Contains(new GridCoord(building.Footprint.X, building.Footprint.Y))
            || !Bounds.Contains(new GridCoord(building.Footprint.Right - 1, building.Footprint.Bottom - 1)))
        {
            throw new ArgumentException($"Building '{building.Id}' does not fit on the site.", nameof(building));
        }

        if (_buildings.Any(b => b.Footprint.Intersects(building.Footprint)))
        {
            throw new ArgumentException($"Building '{building.Id}' overlaps another building.", nameof(building));
        }

        _buildings.Add(building);
        Fill(building.Footprint, TileKind.BuildingFloor);
    }

    public Building? BuildingAt(GridCoord c) => _buildings.FirstOrDefault(b => b.Footprint.Contains(c));
}
