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

    private bool _overPick;

    /// <summary>
    /// True while the mouse is over a person or bed that a click would pick: they are ringed or framed
    /// instead, so the square steps aside.
    /// </summary>
    public bool OverPick
    {
        get => _overPick;
        set
        {
            if (value != _overPick)
            {
                _overPick = value;
                QueueRedraw();
            }
        }
    }

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
        var tile = IsoDraw.ScreenToTile(mouse);
        var local = _site.Local(tile);
        GridCoord? next = _site.InBounds(local) && _site.FloorOf(tile) == IsoDraw.ViewFloor ? tile : null;
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
        if (_hovered is not { } t || _overPick)
        {
            return;
        }

        // A blue square with a white edge round it, so it shows on light floors and dark ones alike.
        var corners = new[]
        {
            Point(t.X, t.Y), Point(t.X + 1, t.Y), Point(t.X + 1, t.Y + 1), Point(t.X, t.Y + 1),
        };
        var loop = new[] { corners[0], corners[1], corners[2], corners[3], corners[0] };
        DrawColoredPolygon(corners, new Color(Blue, 0.28f));
        DrawPolyline(loop, new Color(1, 1, 1, 0.9f), 5f, true);
        DrawPolyline(loop, Blue, 2.5f, true);
    }

    private static readonly Color Blue = new("#1F5FBF");

    private Vector2 Point(double x, double y)
    {
        return IsoDraw.ToScreen(x, y);
    }
}
