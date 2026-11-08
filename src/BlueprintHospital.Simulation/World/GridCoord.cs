namespace BlueprintHospital.Simulation.World;

/// <summary>Integer tile position on the site grid. X runs to the south-east on screen, Y to the south-west.</summary>
public readonly record struct GridCoord(int X, int Y)
{
    public static GridCoord operator +(GridCoord a, GridCoord b) => new(a.X + b.X, a.Y + b.Y);

    public static GridCoord operator -(GridCoord a, GridCoord b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>Axis-aligned rectangle of tiles. <see cref="Width"/> runs along X, <see cref="Length"/> along Y.</summary>
public readonly record struct GridRect(int X, int Y, int Width, int Length)
{
    public int Right => X + Width;

    public int Bottom => Y + Length;

    public int Area => Width * Length;

    public bool Contains(GridCoord c) => c.X >= X && c.X < Right && c.Y >= Y && c.Y < Bottom;

    public bool Intersects(GridRect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    /// <summary>True if the tile is inside the rectangle and on its outer ring.</summary>
    public bool IsOnEdge(GridCoord c) =>
        Contains(c) && (c.X == X || c.X == Right - 1 || c.Y == Y || c.Y == Bottom - 1);

    public IEnumerable<GridCoord> Tiles()
    {
        for (var y = Y; y < Bottom; y++)
        {
            for (var x = X; x < Right; x++)
            {
                yield return new GridCoord(x, y);
            }
        }
    }
}

/// <summary>Converts between tiles and real-world metres. All room dimensions in the game are stored in metres.</summary>
public static class Units
{
    /// <summary>Side length of one grid tile in metres.</summary>
    public const double MetresPerTile = 0.5;

    public static double TilesToMetres(int tiles) => tiles * MetresPerTile;

    /// <summary>Rounds a length in metres up to whole tiles, so a room is never smaller than requested.</summary>
    public static int MetresToTiles(double metres)
    {
        if (metres < 0 || double.IsNaN(metres))
        {
            throw new ArgumentOutOfRangeException(nameof(metres), metres, "Length must be zero or more.");
        }

        return (int)Math.Ceiling(Math.Round(metres / MetresPerTile, 6));
    }

    public static double AreaSquareMetres(int widthTiles, int lengthTiles) =>
        TilesToMetres(widthTiles) * TilesToMetres(lengthTiles);
}
