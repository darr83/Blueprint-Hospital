using System;
using System.Linq;
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
        Couch,
        Bars,
        Mat,
        Stairs,
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
        "examination_couch" => Kind.Couch,
        "parallel_bars" => Kind.Bars,
        "exercise_mat" => Kind.Mat,
        "practice_stairs" => Kind.Stairs,
        _ => Kind.Plain,
    };

    /// <summary>
    /// Draws one slice (sx, sy, sw, sl) of an item whose whole footprint is <paramref name="whole"/>.
    /// <paramref name="joined"/> lists the slice's sides that touch the next slice.
    /// </summary>
    public static void Slice(CanvasItem c, string id, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined)
    {
        if (EquipmentArt.Draws(id))
        {
            // Drawn whole, once, by the slice under the item's centre.
            var (cx, cy) = (whole.X + (whole.W / 2), whole.Y + (whole.L / 2));
            if (cx >= sx - 1e-6 && cx < sx + sw - 1e-6 && cy >= sy - 1e-6 && cy < sy + sl - 1e-6)
            {
                EquipmentArt.Draw(c, id, whole, height, colour);
            }

            return;
        }

        var face = colour.Darkened(0.18f);
        var rim = colour.Lightened(0.3f);
        switch (KindOf(id))
        {
            case Kind.Table:
                Table(c, id, sx, sy, sw, sl, whole, height, colour, joined);
                return;

            case Kind.Couch:
                Couch(c, sx, sy, sw, sl, whole, height, colour, joined);
                return;

            case Kind.Bars:
                ParallelBars(c, sx, sy, sw, sl, whole, height, joined);
                return;

            case Kind.Mat:
                IsoDraw.Prism(c, sx, sy, sw, sl, 2, colour, colour.Darkened(0.2f), joined, rim: colour.Darkened(0.25f));
                foreach (var (a, b) in Inset(sx, sy, sw, sl, whole, 0.12))
                {
                    c.DrawLine(a + new Vector2(0, -2), b + new Vector2(0, -2), colour.Lightened(0.3f), 1f, true);
                }

                return;

            case Kind.Stairs:
                PracticeStairs(c, sx, sy, sw, sl, whole, height, colour);
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

        if (id == "resus_trolley")
        {
            // Castors under the red drawers.
            foreach (var (px, py) in Corners(whole, 0.1))
            {
                if (Inside(px, py, sx, sy, sw, sl))
                {
                    var p = IsoDraw.ToScreen(px, py);
                    c.DrawCircle(p + new Vector2(0, -1.5f), 1.8f, new Color("#2B3036"));
                }
            }
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

        if (id == "resus_trolley" && IsFrontSlice(sx, sy, sw, sl, whole))
        {
            Defibrillator(c, whole.X + (whole.W / 2), whole.Y + (whole.L / 2), height);
        }
    }

    /// <summary>The four corners of a footprint, pulled in by <paramref name="inset"/>.</summary>
    private static (double X, double Y)[] Corners((double X, double Y, double W, double L) r, double inset) => new[]
    {
        (r.X + inset, r.Y + inset), (r.X + r.W - inset, r.Y + inset), (r.X + r.W - inset, r.Y + r.L - inset), (r.X + inset, r.Y + r.L - inset),
    };

    private static bool Inside(double px, double py, double sx, double sy, double sw, double sl) =>
        px >= sx - 1e-6 && px < sx + sw + 1e-6 && py >= sy - 1e-6 && py < sy + sl + 1e-6;

    /// <summary>
    /// True for the slice nearest the viewer, which is drawn last: anything standing on top of the
    /// whole item is drawn by it, so the other slices don't paint over it.
    /// </summary>
    private static bool IsFrontSlice(double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole)
    {
        var front = Corners(whole, 0.01).MaxBy(p => IsoDraw.ToScreen(p.X, p.Y).Y);
        return Inside(front.X, front.Y, sx, sy, sw, sl);
    }

    /// <summary>The outline of the whole item pulled in a little, cut to this slice: for borders and stitching.</summary>
    private static (Vector2 A, Vector2 B)[] Inset(double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, double inset)
    {
        var (x0, y0, x1, y1) = (whole.X + inset, whole.Y + inset, whole.X + whole.W - inset, whole.Y + whole.L - inset);
        var (a0, b0, a1, b1) = (Math.Max(x0, sx), Math.Max(y0, sy), Math.Min(x1, sx + sw), Math.Min(y1, sy + sl));
        var lines = new System.Collections.Generic.List<(Vector2, Vector2)>();
        if (a0 < a1)
        {
            if (y0 >= sy && y0 <= sy + sl) lines.Add((IsoDraw.ToScreen(a0, y0), IsoDraw.ToScreen(a1, y0)));
            if (y1 >= sy && y1 <= sy + sl) lines.Add((IsoDraw.ToScreen(a0, y1), IsoDraw.ToScreen(a1, y1)));
        }

        if (b0 < b1)
        {
            if (x0 >= sx && x0 <= sx + sw) lines.Add((IsoDraw.ToScreen(x0, b0), IsoDraw.ToScreen(x0, b1)));
            if (x1 >= sx && x1 <= sx + sw) lines.Add((IsoDraw.ToScreen(x1, b0), IsoDraw.ToScreen(x1, b1)));
        }

        return lines.ToArray();
    }

    /// <summary>A defibrillator on top of the resus trolley: grey case, yellow handle, a small screen with a trace, and the pads.</summary>
    private static void Defibrillator(CanvasItem c, double cx, double cy, float height)
    {
        IsoDraw.Prism(c, cx - 0.3, cy - 0.25, 0.6, 0.5, 6, new Color("#56616B"), new Color("#3A434B"), lift: height, rim: new Color("#7D8A95"));
        var top = IsoDraw.ToScreen(cx, cy) + new Vector2(0, -height - 6);
        c.DrawLine(top + new Vector2(-6, -3), top + new Vector2(6, -3), new Color("#F2C230"), 2.2f, true);
        c.DrawLine(top + new Vector2(-6, -3), top + new Vector2(-6, 0), new Color("#F2C230"), 2f);
        c.DrawLine(top + new Vector2(6, -3), top + new Vector2(6, 0), new Color("#F2C230"), 2f);

        // The screen, turned to face the viewer.
        var screen = new Rect2(top + new Vector2(-6, -12), new Vector2(10, 7));
        Figures.RoundedRect(c, screen.Grow(1.2f), 1.5f, new Color("#3A434B"));
        c.DrawRect(screen, new Color("#0A1218"));
        c.DrawPolyline(new[] { screen.Position + new Vector2(0.5f, 4.5f), screen.Position + new Vector2(3.5f, 4.5f), screen.Position + new Vector2(4.5f, 1), screen.Position + new Vector2(5.5f, 6), screen.Position + new Vector2(6.5f, 4.5f), screen.Position + new Vector2(9.5f, 4.5f) }, new Color("#3EE07A"), 0.8f, true);
        c.DrawCircle(top + new Vector2(7.5f, -8), 1.5f, new Color("#E53935"));

        // The pads packet and a sharps bin on the trolley top.
        var pads = IsoDraw.ToScreen(cx + 0.38, cy + 0.3) + new Vector2(0, -height);
        c.DrawColoredPolygon(new[] { pads + new Vector2(-3, 0), pads + new Vector2(3, 0), pads + new Vector2(3, -2), pads + new Vector2(-3, -2) }, new Color("#F7F7F2"));
        c.DrawLine(pads + new Vector2(-2, -1), pads + new Vector2(2, -1), new Color("#E53935"), 0.8f);
        var bin = IsoDraw.ToScreen(cx - 0.4, cy + 0.35) + new Vector2(0, -height);
        c.DrawRect(new Rect2(bin + new Vector2(-2.5f, -6), new Vector2(5, 6)), new Color("#F2C230"));
        c.DrawRect(new Rect2(bin + new Vector2(-2.5f, -7), new Vector2(5, 1.6f)), new Color("#E53935"));
    }

    private static readonly Color[] BoxColours =
    {
        new("#E57373"), new("#64B5F6"), new("#FFF176"), new("#81C784"), new("#FFFFFF"), new("#BA68C8"), new("#FFB74D"),
    };

    private static double Edge(Sides joined, Sides side) => (joined & side) != 0 ? 0 : 0.06;

    private static void Table(CanvasItem c, string id, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined)
    {
        if (id == "instrument_trolley")
        {
            InstrumentTrolley(c, sx, sy, sw, sl, whole, height, joined);
            return;
        }

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

    /// <summary>A stainless steel instrument trolley: two shelves on slim legs, the top laid with a blue drape and instruments.</summary>
    private static void InstrumentTrolley(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Sides joined)
    {
        var steel = new Color("#C9D2D9");
        foreach (var (lx, ly) in Corners(whole, 0.1))
        {
            if (Inside(lx, ly, sx, sy, sw, sl))
            {
                var p = IsoDraw.ToScreen(lx, ly);
                c.DrawLine(p + new Vector2(0, -2), p + new Vector2(0, -height), new Color("#9AA6B0"), 1.6f);
                c.DrawCircle(p + new Vector2(0, -1.3f), 1.5f, new Color("#2B3036"));
            }
        }

        IsoDraw.Prism(c, sx, sy, sw, sl, 1.5f, steel, steel.Darkened(0.2f), joined, 7, rim: steel.Lightened(0.3f));
        IsoDraw.Prism(c, sx, sy, sw, sl, 2, steel, steel.Darkened(0.2f), joined, height - 2, rim: steel.Lightened(0.3f));

        // The drape, slightly in from the edges, and the instruments laid out on it.
        var (dx0, dy0, dx1, dy1) = (Math.Max(sx, whole.X + 0.08), Math.Max(sy, whole.Y + 0.08), Math.Min(sx + sw, whole.X + whole.W - 0.08), Math.Min(sy + sl, whole.Y + whole.L - 0.08));
        var up = new Vector2(0, -height - 0.5f);
        c.DrawColoredPolygon(new[] { IsoDraw.ToScreen(dx0, dy0) + up, IsoDraw.ToScreen(dx1, dy0) + up, IsoDraw.ToScreen(dx1, dy1) + up, IsoDraw.ToScreen(dx0, dy1) + up }, new Color("#4F8FB0"));
        var tool = new Color("#E9EEF2");
        for (var i = 0; i < 3; i++)
        {
            var (tx, ty) = (dx0 + ((dx1 - dx0) * (0.25 + (i * 0.25))), dy0 + ((dy1 - dy0) * 0.5));
            var a = IsoDraw.ToScreen(tx, ty - ((dy1 - dy0) * 0.3)) + up;
            var b = IsoDraw.ToScreen(tx, ty + ((dy1 - dy0) * 0.3)) + up;
            c.DrawLine(a, b, tool, 1f, true);
            c.DrawCircle(b, 1f, tool);
        }
    }

    /// <summary>An examination couch: chrome legs, a padded blue top with a raised back at the head and a strip of couch roll.</summary>
    private static void Couch(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined)
    {
        const float pad = 5f;
        foreach (var (lx, ly) in Corners(whole, 0.12))
        {
            if (Inside(lx, ly, sx, sy, sw, sl))
            {
                IsoDraw.Box(c, lx - 0.05, ly - 0.05, 0.1, 0.1, height - pad, new Color("#AEB8C1"), shadow: false);
            }
        }

        IsoDraw.Prism(c, sx, sy, sw, sl, 2, new Color("#8C979F"), new Color("#6F7B85"), joined, height - pad - 2);
        IsoDraw.Prism(c, sx, sy, sw, sl, pad, colour.Lightened(0.08f), colour.Darkened(0.12f), joined, height - pad, rim: colour.Lightened(0.3f));

        // The head end is the north (or west) end; its back rest is raised.
        var alongX = whole.W >= whole.L;
        var (hx, hy, hw, hl) = alongX ? (whole.X, whole.Y, 0.9, whole.L) : (whole.X, whole.Y, whole.W, 0.9);
        var head = sx < hx + hw - 1e-6 && sy < hy + hl - 1e-6;

        // Paper couch roll down the middle.
        var across = alongX ? whole.L : whole.W;
        var (rx0, ry0, rx1, ry1) = alongX
            ? (Math.Max(sx, whole.X + 0.1), whole.Y + (across * 0.22), Math.Min(sx + sw, whole.X + whole.W - 0.1), whole.Y + (across * 0.78))
            : (whole.X + (across * 0.22), Math.Max(sy, whole.Y + 0.1), whole.X + (across * 0.78), Math.Min(sy + sl, whole.Y + whole.L - 0.1));
        var top = new Vector2(0, -height - 0.4f);
        c.DrawColoredPolygon(new[] { IsoDraw.ToScreen(rx0, ry0) + top, IsoDraw.ToScreen(rx1, ry0) + top, IsoDraw.ToScreen(rx1, ry1) + top, IsoDraw.ToScreen(rx0, ry1) + top }, new Color("#F4F6F2"));

        if (head)
        {
            var (bx, by, bw, bl) = (Math.Max(sx, hx), Math.Max(sy, hy), Math.Min(sx + sw, hx + hw) - Math.Max(sx, hx), Math.Min(sy + sl, hy + hl) - Math.Max(sy, hy));
            IsoDraw.Prism(c, bx, by, bw, bl, 6, colour.Lightened(0.12f), colour.Darkened(0.08f), joined & (alongX ? Sides.North | Sides.South : Sides.East | Sides.West), height, rim: colour.Lightened(0.35f));
            if (sx <= whole.X + 1e-6 && sy <= whole.Y + 1e-6)
            {
                // The roll itself, on its holder at the head end.
                var roll = alongX ? IsoDraw.ToScreen(whole.X - 0.05, whole.Y + (across / 2)) : IsoDraw.ToScreen(whole.X + (across / 2), whole.Y - 0.05);
                roll += new Vector2(0, -height - 3);
                c.DrawLine(roll + (alongX ? new Vector2(-8, 4) : new Vector2(8, 4)), roll + (alongX ? new Vector2(8, -4) : new Vector2(-8, -4)), new Color("#F4F6F2"), 4.5f, true);
                c.DrawLine(roll + (alongX ? new Vector2(-8, 4) : new Vector2(8, 4)), roll + (alongX ? new Vector2(8, -4) : new Vector2(-8, -4)), new Color("#D9DDD6"), 1.2f, true);
            }
        }
    }

    /// <summary>Physio parallel bars: a wooden platform with two steel rails on posts down its length.</summary>
    private static void ParallelBars(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Sides joined)
    {
        var wood = new Color("#C9A77C");
        IsoDraw.Prism(c, sx, sy, sw, sl, 2, wood, wood.Darkened(0.25f), joined, rim: wood.Lightened(0.25f));
        var alongX = whole.W >= whole.L;
        var length = alongX ? whole.W : whole.L;
        var across = alongX ? whole.L : whole.W;
        var rail = new Color("#B9C3CB");
        foreach (var off in new[] { 0.12, across - 0.12 })
        {
            // Posts at each end and in the middle, then the rail across the top of them.
            foreach (var t in new[] { 0.15, length / 2, length - 0.15 })
            {
                var (px, py) = alongX ? (whole.X + t, whole.Y + off) : (whole.X + off, whole.Y + t);
                if (Inside(px, py, sx, sy, sw, sl))
                {
                    var p = IsoDraw.ToScreen(px, py);
                    c.DrawLine(p + new Vector2(0, -2), p + new Vector2(0, -height), new Color("#8C979F"), 1.8f);
                }
            }

            var (rx, ry, rw, rl) = alongX ? (sx, whole.Y + off - 0.04, sw, 0.08) : (whole.X + off - 0.04, sy, 0.08, sl);
            IsoDraw.Prism(c, rx, ry, rw, rl, 2, rail.Lightened(0.2f), rail, joined, height - 1);
        }
    }

    /// <summary>Physio practice stairs: steps up one end, a landing, steps down the other, with a handrail each side.</summary>
    private static void PracticeStairs(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var alongX = whole.W >= whole.L;
        var length = alongX ? whole.W : whole.L;
        var across = alongX ? whole.L : whole.W;
        var (start, end) = alongX ? (sx - whole.X, sx - whole.X + sw) : (sy - whole.Y, sy - whole.Y + sl);

        // Three steps up, a landing, three steps down; each step a third of the height.
        // Drawn back to front, so nearer steps cover the ones behind.
        const int steps = 3;
        var stepLength = length / ((steps * 2) + 1.5);
        var blocks = new System.Collections.Generic.List<(double X, double Y, double W, double L, float H)>();
        for (var i = 0; i < (steps * 2) + 1; i++)
        {
            var (a, b) = i < steps ? (i * stepLength, (i + 1) * stepLength) : i == steps ? (steps * stepLength, length - (steps * stepLength)) : (length - ((2 * steps - i + 1) * stepLength), length - ((2 * steps - i) * stepLength));
            var (a0, b0) = (Math.Max(a, start), Math.Min(b, end));
            if (b0 - a0 < 1e-6)
            {
                continue;
            }

            var h = height * (i < steps ? i + 1 : i == steps ? steps : (2 * steps) - i + 1) / steps;
            var (bx, by, bw, bl) = alongX ? (whole.X + a0, whole.Y, b0 - a0, across) : (whole.X, whole.Y + a0, across, b0 - a0);
            blocks.Add((bx, by, bw, bl, h));
        }

        foreach (var (bx, by, bw, bl, h) in blocks.OrderBy(b => IsoDraw.ToScreen(b.X + (b.W / 2), b.Y + (b.L / 2)).Y))
        {
            IsoDraw.Prism(c, bx, by, bw, bl, h, colour.Lightened(0.15f), colour.Darkened(0.1f), rim: colour.Lightened(0.35f));
            foreach (var (p, q, _, _) in IsoDraw.VisibleFaces(bx, by, bw, bl))
            {
                c.DrawLine(p + new Vector2(0, -h + 1), q + new Vector2(0, -h + 1), new Color("#F2C230"), 1.2f);
            }
        }

        // Handrails along both sides.
        foreach (var off in new[] { 0.06, across - 0.06 })
        {
            Vector2 At(double t, float h) => (alongX ? IsoDraw.ToScreen(whole.X + t, whole.Y + off) : IsoDraw.ToScreen(whole.X + off, whole.Y + t)) + new Vector2(0, -h);
            var (a0, b0) = (Math.Max(0, start), Math.Min(length, end));
            float RailH(double t) => (float)(18 + (height * Math.Clamp(Math.Min(t, length - t) / (steps * stepLength), 0, 1)));
            c.DrawLine(At(a0, RailH(a0)), At(b0, RailH(b0)), new Color("#B9C3CB"), 1.6f, true);
            foreach (var t in new[] { 0.1, length / 2, length - 0.1 })
            {
                if (t >= start - 1e-6 && t < end + 1e-6)
                {
                    c.DrawLine(At(t, 0), At(t, RailH(t)), new Color("#8C979F"), 1.4f);
                }
            }
        }
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
        c.DrawRect(new Rect2(bag + new Vector2(-2, 1.5f), new Vector2(4, 1.5f)), new Color("#5C8FC4"));

        // A smaller bag of antibiotics on the other hook.
        var small = top + new Vector2(5, 2);
        Figures.RoundedRect(c, new Rect2(small + new Vector2(-2.5f, 0), new Vector2(5, 7)), 1.5f, new Color("#EEF6E8", 0.95f));
        c.DrawRect(new Rect2(small + new Vector2(-1.5f, 1.5f), new Vector2(3, 1.2f)), new Color("#E57373"));

        // The volumetric pump clamped to the pole, with the giving set running through it.
        var pump = foot + new Vector2(0, -height * 0.58f);
        c.DrawPolyline(new[] { bag + new Vector2(0, 11), pump + new Vector2(-3, -6), pump + new Vector2(-3, 6), pump + new Vector2(-6, 16) }, new Color("#E6F2F8"), 0.9f, true);
        c.DrawLine(small + new Vector2(0, 7), pump + new Vector2(-1, -6), new Color("#E6F2F8"), 0.8f, true);
        Figures.RoundedRect(c, new Rect2(pump + new Vector2(-6, -6), new Vector2(11, 12)), 2, new Color("#E9EDF0"));
        Figures.RoundedRect(c, new Rect2(pump + new Vector2(-6, -6), new Vector2(11, 3)), 1, new Color("#2F6FD0"));
        c.DrawRect(new Rect2(pump + new Vector2(-4.5f, -2), new Vector2(8, 4)), new Color("#0A1218"));
        c.DrawLine(pump + new Vector2(-3.5f, 0), pump + new Vector2(1.5f, 0), new Color("#3EE07A"), 1.2f);
        c.DrawCircle(pump + new Vector2(-3, 4), 0.8f, new Color("#3EE07A"));
        c.DrawCircle(pump + new Vector2(2, 4), 0.8f, new Color("#9AA6B0"));
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

        // Regulator with its gauge and flowmeter, and green oxygen tubing coiled over the valve.
        c.DrawCircle(top + new Vector2(-4, -1), 2.2f, new Color("#E9EDF0"));
        c.DrawArc(top + new Vector2(-4, -1), 2.2f, 0, Mathf.Tau, 12, new Color("#6E7A84"), 0.6f, true);
        c.DrawLine(top + new Vector2(-4, -1), top + new Vector2(-3, -2.4f), new Color("#E53935"), 0.6f);
        c.DrawPolyline(new[] { top + new Vector2(4, 0), top + new Vector2(8, 6), top + new Vector2(6, 14), top + new Vector2(2, 12), top + new Vector2(5, 6) }, new Color("#7CCB8A"), 1f, true);

        // The cylinder stands in a small two-wheeled trolley.
        c.DrawColoredPolygon(new[] { foot + new Vector2(-r - 1, -1), foot + new Vector2(r + 1, -1), foot + new Vector2(r + 1, 2), foot + new Vector2(-r - 1, 2) }, new Color("#3A4148"));
        c.DrawLine(foot + new Vector2(-r - 1, 0), top + new Vector2(-r - 1, 10), new Color("#3A4148"), 1.2f);
        c.DrawCircle(foot + new Vector2(-r - 1, 1), 2f, new Color("#2B3036"));
        c.DrawCircle(foot + new Vector2(r + 1, 1), 2f, new Color("#2B3036"));
    }
}
