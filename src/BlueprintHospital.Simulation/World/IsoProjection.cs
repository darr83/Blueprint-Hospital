namespace BlueprintHospital.Simulation.World;

/// <summary>
/// 2:1 dimetric ("isometric") projection between grid space and screen pixels.
/// The tile origin (0,0) is drawn with its top corner at screen (0,0).
/// </summary>
public sealed class IsoProjection(double tileWidth = 64, double tileHeight = 32)
{
    public double TileWidth { get; } = tileWidth > 0 ? tileWidth : throw new ArgumentOutOfRangeException(nameof(tileWidth));

    public double TileHeight { get; } = tileHeight > 0 ? tileHeight : throw new ArgumentOutOfRangeException(nameof(tileHeight));

    public double HalfWidth => TileWidth / 2;

    public double HalfHeight => TileHeight / 2;

    /// <summary>Screen position of a (possibly fractional) grid point.</summary>
    public (double X, double Y) GridToScreen(double gridX, double gridY) =>
        ((gridX - gridY) * HalfWidth, (gridX + gridY) * HalfHeight);

    /// <summary>Grid position (fractional) under a screen point.</summary>
    public (double X, double Y) ScreenToGrid(double screenX, double screenY)
    {
        var a = screenX / HalfWidth;
        var b = screenY / HalfHeight;
        return ((a + b) / 2, (b - a) / 2);
    }

    /// <summary>The tile under a screen point.</summary>
    public GridCoord ScreenToTile(double screenX, double screenY)
    {
        var (x, y) = ScreenToGrid(screenX, screenY);
        return new GridCoord((int)Math.Floor(x), (int)Math.Floor(y));
    }

    /// <summary>Screen centre of a tile.</summary>
    public (double X, double Y) TileCentre(GridCoord tile) => GridToScreen(tile.X + 0.5, tile.Y + 0.5);
}
