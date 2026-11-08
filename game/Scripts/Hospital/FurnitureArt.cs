using System;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Detailed drawings for the plain "box" furniture: desks on legs, cupboards with doors and
/// handles, drawers on trolleys, shelves of medicines, a drip stand, an oxygen cylinder and so on.
/// Long items are drawn as one-tile slices (so each slice sorts on its own); the slices share
/// faces with their neighbours, which are left out so the item reads as one piece.
/// </summary>
public static class FurnitureArt
{
    private enum Kind
    {
        Plain,
        Table,
        Counter,
        Cupboard,
        Drawers,
        Shelves,
        Fridge,
        Basin,
        DripStand,
        Cylinder,
    }

    private static Kind KindOf(string id) => id switch
    {
        "office_desk" or "staff_table" or "triage_desk" or "instrument_trolley" => Kind.Table,
        "reception_desk" or "station_counter" or "pharmacy_counter" => Kind.Counter,
        "clinical_cupboard" or "kitchenette" or "bedside_locker" => Kind.Cupboard,
        "resus_trolley" or "plaster_trolley" => Kind.Drawers,
        "medicine_shelves" => Kind.Shelves,
        "drug_fridge" => Kind.Fridge,
        "hand_basin" or "plaster_sink" => Kind.Basin,
        "drip_stand" => Kind.DripStand,
        "oxygen_cylinder" => Kind.Cylinder,
        _ => Kind.Plain,
    };

    /// <summary>
    /// Draws one slice (sx, sy, sw, sl) of an item whose whole footprint is <paramref name="whole"/>.
    /// <paramref name="joined"/> lists the slice's sides that touch the next slice.
    /// </summary>
    public static void Slice(CanvasItem c, string id, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined)
    {
        var face = colour.Darkened(0.18f);
        var rim = colour.Lightened(0.3f);
        switch (KindOf(id))
        {
            case Kind.Table:
                Table(c, sx, sy, sw, sl, whole, height, colour, joined);
                return;

            case Kind.Counter:
                // A solid counter with a dark plinth and a slightly overhanging worktop.
                IsoDraw.Prism(c, sx, sy, sw, sl, height - 3, colour.Darkened(0.05f), face, joined, skirting: colour.Darkened(0.5f), skirtingHeight: 3);
                IsoDraw.Prism(c, sx - Edge(joined, Sides.West), sy - Edge(joined, Sides.North), sw + Edge(joined, Sides.West) + Edge(joined, Sides.East), sl + Edge(joined, Sides.North) + Edge(joined, Sides.South), 3, colour.Lightened(0.22f), colour.Lightened(0.05f), joined, height - 3, rim: colour.Lightened(0.4f));
                return;

            case Kind.DripStand:
                DripStand(c, sx + (sw / 2), sy + (sl / 2), height);
                return;

            case Kind.Cylinder:
                Cylinder(c, sx + (sw / 2), sy + (sl / 2), height);
                return;
        }

        var kind = KindOf(id);
        IsoDraw.Prism(c, sx, sy, sw, sl, height, colour, face, joined, skirting: kind is Kind.Cupboard or Kind.Basin or Kind.Fridge ? colour.Darkened(0.45f) : null, skirtingHeight: 2, rim: rim);
        var line = colour.Darkened(0.4f);
        foreach (var (a, b, _, _) in IsoDraw.VisibleFaces(sx, sy, sw, sl))
        {
            Vector2 At(float u, float v) => a.Lerp(b, u) + new Vector2(0, -v);
            switch (kind)
            {
                case Kind.Cupboard:
                case Kind.Basin:
                    // A pair of doors with small handles.
                    c.DrawLine(At(0.5f, 3), At(0.5f, height - 3), line, 1f, true);
                    c.DrawLine(At(0.42f, height * 0.62f), At(0.42f, height * 0.78f), line, 1.4f, true);
                    c.DrawLine(At(0.58f, height * 0.62f), At(0.58f, height * 0.78f), line, 1.4f, true);
                    break;

                case Kind.Drawers:
                    for (var i = 1; i < 4; i++)
                    {
                        var v = height * i / 4f;
                        c.DrawLine(At(0.04f, v), At(0.96f, v), line, 1f, true);
                        c.DrawLine(At(0.4f, v - (height / 8f)), At(0.6f, v - (height / 8f)), colour.Lightened(0.5f), 1.4f, true);
                    }

                    break;

                case Kind.Shelves:
                    // Shelves of coloured medicine boxes.
                    for (var i = 1; i < 5; i++)
                    {
                        var v = height * i / 5f;
                        c.DrawLine(At(0.03f, v), At(0.97f, v), line, 1f, true);
                        for (var k = 0; k < 4; k++)
                        {
                            var u = 0.08f + (k * 0.22f);
                            var box = BoxColours[(i + k + (int)(sx * 3) + (int)(sy * 5)) % BoxColours.Length];
                            c.DrawColoredPolygon(new[] { At(u, v + 1), At(u + 0.16f, v + 1), At(u + 0.16f, v + (height / 5f) - 3), At(u, v + (height / 5f) - 3) }, box);
                        }
                    }

                    break;

                case Kind.Fridge:
                    // A glass door showing the shelves inside, with a temperature display.
                    c.DrawColoredPolygon(new[] { At(0.1f, 6), At(0.9f, 6), At(0.9f, height - 6), At(0.1f, height - 6) }, new Color("#A9D3E8", 0.75f));
                    for (var i = 1; i < 4; i++)
                    {
                        c.DrawLine(At(0.12f, height * i / 4f), At(0.88f, height * i / 4f), new Color("#E9F5FB"), 1f, true);
                    }

                    c.DrawLine(At(0.7f, height - 3), At(0.85f, height - 3), new Color("#3EE07A"), 1.5f);
                    break;
            }
        }

        if (kind == Kind.Basin)
        {
            // The basin set into the top, with a mixer tap.
            var top = new Vector2(0, -height);
            var (cx, cy) = (sx + (sw / 2), sy + (sl / 2));
            c.DrawColoredPolygon(new[] { IsoDraw.ToScreen(cx - 0.3, cy - 0.3) + top, IsoDraw.ToScreen(cx + 0.3, cy - 0.3) + top, IsoDraw.ToScreen(cx + 0.3, cy + 0.3) + top, IsoDraw.ToScreen(cx - 0.3, cy + 0.3) + top }, new Color("#C9D6DF"));
            var tap = IsoDraw.ToScreen(cx, cy) + top;
            c.DrawLine(tap, tap + new Vector2(0, -6), new Color("#9AA6B0"), 2f);
            c.DrawCircle(tap + new Vector2(0, -6), 1.6f, new Color("#B9C3CB"));
        }
    }

    private static readonly Color[] BoxColours =
    {
        new("#E57373"), new("#64B5F6"), new("#FFF176"), new("#81C784"), new("#FFFFFF"), new("#BA68C8"), new("#FFB74D"),
    };

    private static double Edge(Sides joined, Sides side) => (joined & side) != 0 ? 0 : 0.06;

    private static void Table(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined)
    {
        const float slab = 4f;
        const double leg = 0.12;
        var legColour = new Color("#4A535B");

        // Legs at the corners of the whole table, drawn by whichever slice they stand in.
        foreach (var (lx, ly) in new[]
                 {
                     (whole.X + 0.08, whole.Y + 0.08), (whole.X + whole.W - 0.08 - leg, whole.Y + 0.08),
                     (whole.X + 0.08, whole.Y + whole.L - 0.08 - leg), (whole.X + whole.W - 0.08 - leg, whole.Y + whole.L - 0.08 - leg),
                 })
        {
            if (lx >= sx - 1e-6 && lx < sx + sw - 1e-6 && ly >= sy - 1e-6 && ly < sy + sl - 1e-6)
            {
                IsoDraw.Box(c, lx, ly, leg, leg, height - slab, legColour, shadow: false);
            }
        }

        IsoDraw.Prism(c, sx, sy, sw, sl, slab, colour, colour.Darkened(0.25f), joined, height - slab, rim: colour.Lightened(0.3f));
    }

    private static void DripStand(CanvasItem c, double gx, double gy, float height)
    {
        var foot = IsoDraw.ToScreen(gx, gy);
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(foot + new Vector2(1, 1), 9), new Color(0, 0, 0, 0.14f));
        for (var i = 0; i < 5; i++)
        {
            var a = (i * Mathf.Tau / 5) + 0.6f;
            var end = foot + new Vector2(Mathf.Cos(a) * 8, Mathf.Sin(a) * 4);
            c.DrawLine(foot + new Vector2(0, -2), end, new Color("#56616B"), 1.8f, true);
            c.DrawCircle(end, 1.3f, new Color("#2B3036"));
        }

        var top = foot + new Vector2(0, -height);
        c.DrawLine(foot + new Vector2(0, -2), top, new Color("#A9B4BD"), 2f);
        c.DrawLine(top + new Vector2(-6, 0), top + new Vector2(6, 0), new Color("#A9B4BD"), 1.5f);

        // A bag of fluid hanging from the hook, with its line.
        var bag = top + new Vector2(-5, 2);
        Figures.RoundedRect(c, new Rect2(bag + new Vector2(-3.5f, 0), new Vector2(7, 11)), 2, new Color("#DDEFF7", 0.95f));
        c.DrawRect(new Rect2(bag + new Vector2(-3, 4), new Vector2(6, 6.5f)), new Color("#BFE0F0"));
        c.DrawLine(bag + new Vector2(0, 11), bag + new Vector2(1, 26), new Color("#E6F2F8"), 1f, true);
    }

    private static void Cylinder(CanvasItem c, double gx, double gy, float height)
    {
        var foot = IsoDraw.ToScreen(gx, gy);
        const float r = 5f;
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(foot + new Vector2(1, 1), r + 2), new Color(0, 0, 0, 0.16f));
        var body = new Color("#F2F4F5");
        var shoulder = new Color("#2C2F33");
        var top = foot + new Vector2(0, -height);
        c.DrawColoredPolygon(new[] { foot + new Vector2(-r, 0), foot + new Vector2(r, 0), top + new Vector2(r, 6), top + new Vector2(-r, 6) }, body);
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(foot, r, 12), body.Darkened(0.1f));
        c.DrawLine(foot + new Vector2(r - 1.5f, 0), top + new Vector2(r - 1.5f, 6), body.Darkened(0.15f), 2f);

        // White shoulder for oxygen with a black valve on top.
        c.DrawColoredPolygon(new[] { top + new Vector2(-r, 6), top + new Vector2(r, 6), top + new Vector2(r - 1.5f, 1), top + new Vector2(-r + 1.5f, 1) }, Colors.White);
        c.DrawRect(new Rect2(top + new Vector2(-2, -3), new Vector2(4, 4)), shoulder);
        c.DrawLine(top + new Vector2(2, -1), top + new Vector2(5, -1), shoulder, 1.5f);
    }
}
