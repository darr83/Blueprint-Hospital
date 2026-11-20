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
            // A frame round the item, drawn over it so it shows even on a bed.
            const double pad = 0.15;
            var (x, y, w, l) = (i.X - pad, i.Y - pad, i.W + (2 * pad), i.L + (2 * pad));
            IsoDraw.FloorQuad(this, x, y, w, l, new Color(UiStyle.Blue, 0.16f));
            IsoDraw.FloorOutline(this, x, y, w, l, line, 3f);
        }
    }
}
