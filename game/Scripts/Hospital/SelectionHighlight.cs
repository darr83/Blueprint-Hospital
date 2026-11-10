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

    /// <summary>How tall the selected item stands, in pixels.</summary>
    public float ItemHeight { get; set; }

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

        if (Item is { } i)
        {
            // A wire box round the item, drawn over it so it shows even on a bed.
            const double pad = 0.15;
            var (x, y, w, l) = (i.X - pad, i.Y - pad, i.W + (2 * pad), i.L + (2 * pad));
            var floor = new[] { IsoDraw.ToScreen(x, y), IsoDraw.ToScreen(x + w, y), IsoDraw.ToScreen(x + w, y + l), IsoDraw.ToScreen(x, y + l) };
            var lift = new Vector2(0, -ItemHeight - 4);
            var top = floor.Select(p => p + lift).ToArray();
            DrawColoredPolygon(top, new Color(UiStyle.Blue, 0.18f));
            DrawPolyline(floor.Append(floor[0]).ToArray(), line, 2f, true);
            DrawPolyline(top.Append(top[0]).ToArray(), line, 3f, true);
            for (var k = 0; k < 4; k++)
            {
                DrawLine(floor[k], top[k], line, 2f, true);
            }
        }
    }
}
