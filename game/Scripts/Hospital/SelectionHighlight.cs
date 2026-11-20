using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.World;
using BlueprintHospital.UI;
using System.Linq;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>Outlines the selected room, bed or item on the floor so the player can see what they picked.</summary>
public partial class SelectionHighlight : Node2D
{
    /// <summary>The selected room, outlined along its walls.</summary>
    public GridRect? Room { get; set; }

    /// <summary>The room under the mouse while decorating: a lighter outline.</summary>
    public GridRect? Hover { get; set; }

    /// <summary>The selected item or bed: its footprint on the floor (X, Y, width, length in tiles).</summary>
    public (double X, double Y, double W, double L)? Item { get; set; }

    /// <summary>How tall the selected item stands, in pixels (the 3D game only).</summary>
    public float ItemHeight { get; set; }

    /// <summary>In play mode: the bed under the mouse that a click would open, framed more lightly than a selected one.</summary>
    public (double X, double Y, double W, double L)? HoverItem { get; set; }

    /// <summary>How tall the bed under the mouse stands, in pixels (the 3D game only).</summary>
    public float HoverItemHeight { get; set; }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var line = UiStyle.Blue;
        if (Hover is { } h && h != Room)
        {
            IsoDraw.FloorQuad(this, h.X, h.Y, h.Width, h.Length, new Color(UiStyle.Blue, 0.1f));
            IsoDraw.FloorOutline(this, h.X + 0.3, h.Y + 0.3, h.Width - 0.6, h.Length - 0.6, new Color(UiStyle.Blue, 0.6f), 2f);
        }
        if (Room is { } r)
        {
            // Just inside the walls, so the walls don't hide the outline.
            const double inset = 0.3;
            IsoDraw.FloorQuad(this, r.X, r.Y, r.Width, r.Length, new Color(UiStyle.Blue, 0.16f));
            IsoDraw.FloorOutline(this, r.X + inset, r.Y + inset, r.Width - (2 * inset), r.Length - (2 * inset), line, 3f);
        }

        if (HoverItem is { } hover && hover != Item)
        {
            Frame(hover, HoverItemHeight, new Color(UiStyle.BlueHover, 0.8f), 0.08f, 0.7f);
        }

        if (Item is { } i)
        {
            Frame(i, ItemHeight, line, 0.16f, 1f);
        }
    }

    /// <summary>A frame round an item, drawn over it so it shows even on a bed: flat in 2D, a wire box in 3D.</summary>
    private void Frame((double X, double Y, double W, double L) i, float height, Color line, float fill, float weight)
    {
        const double pad = 0.15;
        var (x, y, w, l) = (i.X - pad, i.Y - pad, i.W + (2 * pad), i.L + (2 * pad));
        if (!Core.GameView.ThreeD)
        {
            IsoDraw.FloorQuad(this, x, y, w, l, new Color(UiStyle.Blue, fill));
            IsoDraw.FloorOutline(this, x, y, w, l, line, 3f * weight);
            return;
        }

        var floor = new[] { IsoDraw.ToScreen(x, y), IsoDraw.ToScreen(x + w, y), IsoDraw.ToScreen(x + w, y + l), IsoDraw.ToScreen(x, y + l) };
        var lift = new Vector2(0, -height - 4);
        var top = floor.Select(p => p + lift).ToArray();
        DrawColoredPolygon(top, new Color(UiStyle.Blue, fill + 0.02f));
        DrawPolyline(floor.Append(floor[0]).ToArray(), line, 2f * weight, true);
        DrawPolyline(top.Append(top[0]).ToArray(), line, 3f * weight, true);
        for (var k = 0; k < 4; k++)
        {
            DrawLine(floor[k], top[k], line, 2f * weight, true);
        }
    }
}
