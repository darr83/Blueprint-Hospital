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

/// <summary>
/// A hospital building: its original footprint plus any extensions, enclosed by outer walls
/// with entrances.
/// </summary>
public sealed class Building(string id, string name, GridRect footprint)
{
    private readonly List<Entrance> _entrances = new();
    private readonly List<GridRect> _extensions = new();

    public string Id { get; } = id;

    public string Name { get; } = name;

    public GridRect Footprint { get; } = footprint;

    public IReadOnlyList<Entrance> Entrances => _entrances;

    /// <summary>Areas added to the original footprint by building extensions.</summary>
    public IReadOnlyList<GridRect> Extensions => _extensions;

    /// <summary>The original footprint and every extension.</summary>
    public IEnumerable<GridRect> Areas => _extensions.Prepend(Footprint);

    /// <summary>Smallest rectangle around the whole building.</summary>
    public GridRect Bounds
    {
        get
        {
            var areas = Areas.ToList();
            var x = areas.Min(a => a.X);
            var y = areas.Min(a => a.Y);
            return new GridRect(x, y, areas.Max(a => a.Right) - x, areas.Max(a => a.Bottom) - y);
        }
    }

    public bool Contains(GridCoord c) => Footprint.Contains(c) || _extensions.Any(e => e.Contains(c));

    public double AreaSquareMetres => Areas.SelectMany(a => a.Tiles()).Distinct().Count() * Units.MetresPerTile * Units.MetresPerTile;

    internal void AddExtension(GridRect area) => _extensions.Add(area);

    internal void ClearExtensions() => _extensions.Clear();

    /// <summary>Every edge between a floor tile of the building and a tile outside it: where the outer walls stand.</summary>
    public IEnumerable<(GridCoord Inside, GridCoord Outside, WallSide Side)> OuterEdges()
    {
        var seen = new HashSet<GridCoord>();
        foreach (var tile in Areas.SelectMany(a => a.Tiles()))
        {
            if (!seen.Add(tile))
            {
                continue;
            }

            foreach (var (dx, dy, side) in new[] { (0, -1, WallSide.North), (1, 0, WallSide.East), (0, 1, WallSide.South), (-1, 0, WallSide.West) })
            {
                var n = new GridCoord(tile.X + dx, tile.Y + dy);
                if (!Contains(n))
                {
                    yield return (tile, n, side);
                }
            }
        }
    }

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

    public Building? BuildingAt(GridCoord c) => _buildings.FirstOrDefault(b => b.Contains(c));

    /// <summary>Adds an extension to a building and lays its floor. Checks are made by the caller.</summary>
    public void ExtendBuilding(Building building, GridRect area)
    {
        building.AddExtension(area);
        Fill(area, TileKind.BuildingFloor);
    }
}
