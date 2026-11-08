using System;
using BlueprintHospital.Simulation.Hospital;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Drawings for the cafeteria and vending point furniture: vending machines with a lit glass front full
/// of snacks, a water machine with its blue bottle, a food counter with a glass display and a till, and
/// cafe tables. Machines face into the room, away from the wall they stand against, and their glass front
/// is only drawn when it faces the viewer.
/// </summary>
public static class CafeArt
{
    private static readonly Color Glass = new("#1E2A33");
    private static readonly Color[] Snacks =
    {
        new("#E53935"), new("#FDD835"), new("#43A047"), new("#1E88E5"), new("#FB8C00"), new("#8E24AA"), new("#F5F5F5"), new("#6D4C41"),
    };

    public static bool Draws(string itemId) => itemId is "vending_machine" or "water_cooler" or "food_counter" or "cafe_table";

    /// <summary>Adds the props for one item; <paramref name="addProp"/> places a drawing sorted by its anchor.</summary>
    public static void Add(string itemId, CareSpace room, PlacedFurniture item, float height, Color colour, Action<Vector2, Action<CanvasItem>> addProp)
    {
        var (x, y, w, l) = (item.X, item.Y, item.Width, item.Length);
        var seed = (int)Math.Abs((Math.Floor(x * 7) * 31) + (Math.Floor(y * 7) * 17));
        switch (itemId)
        {
            case "vending_machine":
                var front = FrontOf(room, x, y, w, l);
                addProp(IsoDraw.ToScreen(x + (w / 2), y + (l / 2)), c => VendingMachine(c, x, y, w, l, height, colour, front, seed));
                break;

            case "water_cooler":
                addProp(IsoDraw.ToScreen(x + (w / 2), y + (l / 2)), c => WaterCooler(c, x, y, w, l, height, colour, FrontOf(room, x, y, w, l)));
                break;

            case "food_counter":
                // One-tile slices so each sorts on its own, like other long furniture.
                var alongX = w >= l;
                var length = alongX ? w : l;
                var slices = Math.Max(1, (int)Math.Ceiling(length));
                var step = length / slices;
                for (var i = 0; i < slices; i++)
                {
                    var (sx, sy, sw, sl) = alongX ? (x + (i * step), y, step, l) : (x, y + (i * step), w, step);
                    var index = i;
                    addProp(IsoDraw.ToScreen(sx + (sw / 2), sy + (sl / 2)), c => CounterSlice(c, sx, sy, sw, sl, height, colour, index, slices, seed));
                }

                break;

            case "cafe_table":
                addProp(IsoDraw.ToScreen(x + (w / 2), y + (l / 2)), c => Table(c, x, y, w, l, height, colour, seed));
                break;
        }
    }

    /// <summary>Which way an item against a wall faces: away from the wall along its long side.</summary>
    private static (int X, int Y) FrontOf(CareSpace room, double x, double y, double w, double l)
    {
        var r = room.Rect;
        if (w >= l)
        {
            return y - r.Y < r.Bottom - (y + l) ? (0, 1) : (0, -1);
        }

        return x - r.X < r.Right - (x + w) ? (1, 0) : (-1, 0);
    }

    /// <summary>The bottom corners of a footprint's face on one side, in screen space, left to right as stored.</summary>
    private static (Vector2 A, Vector2 B, bool Visible) Face(double x, double y, double w, double l, (int X, int Y) side)
    {
        var (a, b) = side switch
        {
            (0, 1) => (IsoDraw.ToScreen(x, y + l), IsoDraw.ToScreen(x + w, y + l)),
            (0, -1) => (IsoDraw.ToScreen(x + w, y), IsoDraw.ToScreen(x, y)),
            (1, 0) => (IsoDraw.ToScreen(x + w, y + l), IsoDraw.ToScreen(x + w, y)),
            _ => (IsoDraw.ToScreen(x, y), IsoDraw.ToScreen(x, y + l)),
        };
        var centre = IsoDraw.ToScreen(x + (w / 2), y + (l / 2));
        return (a, b, (a + b).Y / 2 > centre.Y);
    }

    /// <summary>A patch on a face: <paramref name="s0"/>-<paramref name="s1"/> along it, <paramref name="h0"/>-<paramref name="h1"/> pixels up.</summary>
    private static void Patch(CanvasItem c, Vector2 a, Vector2 b, double s0, double s1, float h0, float h1, Color colour)
    {
        Vector2 P(double s, float h) => a + ((b - a) * (float)s) + new Vector2(0, -h);
        c.DrawColoredPolygon(new[] { P(s0, h0), P(s1, h0), P(s1, h1), P(s0, h1) }, colour);
    }

    private static void VendingMachine(CanvasItem c, double x, double y, double w, double l, float height, Color colour, (int X, int Y) front, int seed)
    {
        IsoDraw.Box(c, x, y, w, l, height, colour.Darkened(0.1f));

        // The cabinet's other sides carry the brand: a pale band with a white badge, so the machine
        // reads as a vending machine from behind too.
        foreach (var side in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
        {
            if (side == front)
            {
                continue;
            }

            var (sa, sb, shows) = Face(x, y, w, l, side);
            if (shows)
            {
                var dim = (sa.X + sb.X) / 2 < IsoDraw.ToScreen(x + (w / 2), y + (l / 2)).X ? 0.1f : 0.22f;
                Patch(c, sa, sb, 0.0, 1.0, height * 0.55f, height * 0.78f, colour.Lightened(0.25f).Darkened(dim));
                Patch(c, sa, sb, 0.3, 0.7, height * 0.6f, height * 0.73f, new Color("#FAFAFA").Darkened(dim));
                Patch(c, sa, sb, 0.36, 0.64, height * 0.645f, height * 0.685f, colour.Darkened(dim));
            }
        }

        var (a, b, visible) = Face(x, y, w, l, front);
        if (!visible)
        {
            return;
        }

        var shade = (a.X + b.X) / 2 < IsoDraw.ToScreen(x + (w / 2), y + (l / 2)).X ? 0f : 0.12f;

        // Lit sign along the top, then the glass front with shelves of snacks, a keypad and the tray.
        Patch(c, a, b, 0.06, 0.94, height * 0.88f, height * 0.97f, colour.Lightened(0.35f).Darkened(shade));
        Patch(c, a, b, 0.07, 0.66, height * 0.2f, height * 0.85f, Glass.Darkened(shade));
        var rows = 5;
        for (var row = 0; row < rows; row++)
        {
            var h0 = height * (0.24f + (row * 0.12f));
            Patch(c, a, b, 0.08, 0.65, h0 - 1.5f, h0 - 0.5f, new Color(1, 1, 1, 0.25f));
            for (var col = 0; col < 4; col++)
            {
                var snack = Snacks[(seed + (row * 5) + (col * 3)) % Snacks.Length].Darkened(shade);
                var s0 = 0.1 + (col * 0.14);
                Patch(c, a, b, s0, s0 + 0.09, h0, h0 + (height * 0.08f), snack);
            }
        }

        Patch(c, a, b, 0.07, 0.66, height * 0.62f, height * 0.85f, new Color(1, 1, 1, 0.08f));
        Patch(c, a, b, 0.72, 0.92, height * 0.52f, height * 0.74f, new Color("#CFD8DC").Darkened(shade));
        for (var k = 0; k < 3; k++)
        {
            Patch(c, a, b, 0.75 + (k * 0.055), 0.79 + (k * 0.055), height * 0.56f, height * 0.6f, new Color("#455A64"));
            Patch(c, a, b, 0.75 + (k * 0.055), 0.79 + (k * 0.055), height * 0.63f, height * 0.67f, new Color("#455A64"));
        }

        Patch(c, a, b, 0.74, 0.9, height * 0.42f, height * 0.46f, new Color("#212121"));
        Patch(c, a, b, 0.1, 0.62, height * 0.06f, height * 0.15f, new Color("#101418"));
    }

    private static void WaterCooler(CanvasItem c, double x, double y, double w, double l, float height, Color colour, (int X, int Y) front)
    {
        var body = height * 0.72f;
        IsoDraw.Box(c, x, y, w, l, body, colour);
        var (a, b, visible) = Face(x, y, w, l, front);
        if (visible)
        {
            // Two taps (cold blue, hot red) over a drip tray.
            Patch(c, a, b, 0.2, 0.8, body * 0.5f, body * 0.75f, new Color("#B0BEC5"));
            Patch(c, a, b, 0.28, 0.42, body * 0.62f, body * 0.7f, new Color("#1E88E5"));
            Patch(c, a, b, 0.58, 0.72, body * 0.62f, body * 0.7f, new Color("#E53935"));
            Patch(c, a, b, 0.22, 0.78, body * 0.46f, body * 0.5f, new Color("#607D8B"));
        }

        // The upturned blue bottle on top.
        var inset = Math.Min(w, l) * 0.18;
        IsoDraw.Box(c, x + inset, y + inset, w - (2 * inset), l - (2 * inset), height - body, new Color(0.45f, 0.72f, 0.95f, 0.85f), lift: body, shadow: false);
        IsoDraw.Box(c, x + (inset * 1.8), y + (inset * 1.8), w - (3.6 * inset), l - (3.6 * inset), 2, new Color("#1565C0"), lift: height, shadow: false);
    }

    private static void CounterSlice(CanvasItem c, double x, double y, double w, double l, float height, Color colour, int index, int count, int seed)
    {
        // Wooden counter with a pale worktop.
        IsoDraw.Box(c, x, y, w, l, height - 3, colour, shadow: false);
        IsoDraw.Box(c, x, y, w, l, 3, new Color("#EEE6DA"), lift: height - 3, shadow: false);

        if (index == count / 2)
        {
            // The till.
            IsoDraw.Box(c, x + (w * 0.3), y + (l * 0.3), w * 0.4, l * 0.35, 7, new Color("#37474F"), lift: height, shadow: false);
            IsoDraw.Box(c, x + (w * 0.35), y + (l * 0.3), w * 0.3, l * 0.08, 6, new Color("#80CBC4"), lift: height + 7, shadow: false);
            return;
        }

        // A glass display case of sandwiches, cakes and fruit.
        var (ix, iy, iw, il) = (x + (w * 0.08), y + (l * 0.15), w * 0.84, l * 0.7);
        for (var i = 0; i < 3; i++)
        {
            var food = Snacks[(seed + index + (i * 3)) % Snacks.Length];
            var t = 0.1 + (i * 0.28);
            var (fx, fy, fw, fl) = w >= l ? (ix + (iw * t), iy + (il * 0.25), iw * 0.22, il * 0.5) : (ix + (iw * 0.25), iy + (il * t), iw * 0.5, il * 0.22);
            IsoDraw.Box(c, fx, fy, fw, fl, 4, food.Lightened(0.15f), lift: height, shadow: false);
        }

        IsoDraw.Box(c, ix, iy, iw, il, 12, new Color(0.78f, 0.9f, 0.96f, 0.32f), lift: height, sideColour: new Color(0.7f, 0.85f, 0.95f, 0.3f), shadow: false);
    }

    private static void Table(CanvasItem c, double x, double y, double w, double l, float height, Color colour, int seed)
    {
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        IsoDraw.Box(c, cx - 0.12, cy - 0.12, 0.24, 0.24, height - 3, new Color("#5D4037"));
        IsoDraw.Box(c, x, y, w, l, 3, colour, lift: height - 3);

        // Now and then a cup and a plate left on the table.
        if (seed % 3 == 0)
        {
            IsoDraw.Box(c, cx - 0.35, cy - 0.3, 0.32, 0.32, 1, new Color("#FAFAFA"), lift: height, shadow: false);
            IsoDraw.Box(c, cx + 0.1, cy + 0.05, 0.16, 0.16, 5, new Color("#FFFFFF"), lift: height, shadow: false);
        }
    }
}
