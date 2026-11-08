using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>Highlights the tile under the mouse and reports it to the HUD.</summary>
public partial class TileCursor : Node2D
{
    private SiteMap _site = null!;
    private IsoProjection _projection = null!;
    private GridCoord? _hovered;

    [Signal]
    public delegate void HoveredTileChangedEventHandler(int x, int y, bool onSite);

    public GridCoord? HoveredTile => _hovered;

    public void Initialise(SiteMap site, IsoProjection projection)
    {
        _site = site;
        _projection = projection;
    }

    public override void _Process(double delta)
    {
        if (_projection is null)
        {
            return;
        }

        var mouse = GetGlobalMousePosition();
        var tile = _projection.ScreenToTile(mouse.X, mouse.Y);
        GridCoord? next = _site.InBounds(tile) ? tile : null;
        if (next == _hovered)
        {
            return;
        }

        _hovered = next;
        QueueRedraw();
        EmitSignal(SignalName.HoveredTileChanged, tile.X, tile.Y, next.HasValue);
    }

    public override void _Draw()
    {
        if (_hovered is not { } t)
        {
            return;
        }

        var corners = new[]
        {
            Point(t.X, t.Y), Point(t.X + 1, t.Y), Point(t.X + 1, t.Y + 1), Point(t.X, t.Y + 1),
        };
        DrawColoredPolygon(corners, new Color(1, 1, 1, 0.25f));
        DrawPolyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[0] }, new Color("#005EB8"), 2f);
    }

    private Vector2 Point(double x, double y)
    {
        var (sx, sy) = _projection.GridToScreen(x, y);
        return new Vector2((float)sx, (float)sy);
    }
}
