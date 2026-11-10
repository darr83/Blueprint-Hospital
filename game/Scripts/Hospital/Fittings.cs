using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// The fitted furniture of each kind of room, drawn to suit where it stands: desks with drawer
/// pedestals, a two-level reception desk and nurses' station, the pharmacy hatch, clinical base
/// units with gloves and a sharps bin, a staff kitchenette, bedside lockers, the plaster trolley
/// and sink, medicine shelves, the drug fridge and a clinical wash basin with its dispensers.
/// Each has a front (the side people use) that faces into the room.
/// </summary>
public static partial class FurnitureArt
{
    private static readonly Color Steel = new("#C3CCD3");
    private static readonly Color Plinth = new("#55616B");
    private static readonly Color White = new("#F4F7F9");

    /// <summary>
    /// The side of an item people use: for desks and counters the side away from the staff chair
    /// (where visitors come), otherwise the side away from the nearest wall.
    /// </summary>
    public static Sides FrontOf(CareSpace room, PlacedFurniture item)
    {
        var (x, y, w, l) = (item.X, item.Y, item.Width, item.Length);
        if (item.ItemId is "office_desk" or "triage_desk" or "reception_desk" or "station_counter" or "pharmacy_counter")
        {
            var best = 2.2;
            var staffSide = Sides.None;
            foreach (var chair in room.Furniture.Where(f => f.ItemId == "staff_chair"))
            {
                var (cx, cy) = (chair.X + (chair.Width / 2), chair.Y + (chair.Length / 2));
                var gx = Math.Max(0, Math.Max(x - cx, cx - (x + w)));
                var gy = Math.Max(0, Math.Max(y - cy, cy - (y + l)));
                if (gx + gy < best)
                {
                    best = gx + gy;
                    staffSide = gx > gy ? (cx < x ? Sides.West : Sides.East) : (cy < y ? Sides.North : Sides.South);
                }
            }

            if (staffSide != Sides.None)
            {
                return IsoFace.Opposite(staffSide);
            }
        }

        var r = room.Rect;
        var gaps = new[] { (Sides.South, y - r.Y), (Sides.North, r.Bottom - (y + l)), (Sides.East, x - r.X), (Sides.West, r.Right - (x + w)) };

        // Long items stand with their length along the wall, so only the long sides count.
        var candidates = Math.Abs(w - l) < 0.2 ? gaps : w > l ? gaps.Take(2).ToArray() : gaps.Skip(2).ToArray();
        return candidates.MinBy(g => g.Item2).Item1;
    }

    /// <summary>How high things standing on an item sit: the staff worktop of a two-level counter is lower than its ledge.</summary>
    public static float TopHeight(string id, float height) => id switch
    {
        "reception_desk" => height - 9,
        "station_counter" => height - 5,
        _ => height,
    };

    /// <summary>A strip of a slice along one of its sides: <paramref name="from"/> tiles in from that side, <paramref name="depth"/> tiles deep.</summary>
    private static (double X, double Y, double W, double L) Strip(double sx, double sy, double sw, double sl, Sides side, double from, double depth) => side switch
    {
        Sides.South => (sx, sy + sl - from - depth, sw, depth),
        Sides.North => (sx, sy + from, sw, depth),
        Sides.East => (sx + sw - from - depth, sy, depth, sl),
        _ => (sx + from, sy, depth, sl),
    };

    private static double DepthAlong(double sw, double sl, Sides side) => side is Sides.North or Sides.South ? sl : sw;

    private static IEnumerable<IsoFace> FrontFaces(double x, double y, double w, double l, float height, Sides front, Sides joined, float lift = 0) =>
        IsoFace.Of(x, y, w, l, height, lift).Where(f => f.Side == front && (joined & front) == 0);

    private static IEnumerable<IsoFace> OpenFaces(double x, double y, double w, double l, float height, Sides joined, float lift = 0) =>
        IsoFace.Of(x, y, w, l, height, lift).Where(f => (joined & f.Side) == 0);

    private static int Seed(double sx, double sy) => (int)Math.Abs((Math.Floor(sx * 3) * 7) + (Math.Floor(sy * 3) * 13));

    private static int SliceIndex(double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole) =>
        whole.W >= whole.L ? (int)Math.Round((sx - whole.X) / sw) : (int)Math.Round((sy - whole.Y) / sl);

    private static int SliceCount(double sw, double sl, (double X, double Y, double W, double L) whole) =>
        whole.W >= whole.L ? (int)Math.Round(whole.W / sw) : (int)Math.Round(whole.L / sl);

    private static Vector2 Up(double gx, double gy, float lift) => IsoDraw.ToScreen(gx, gy) + new Vector2(0, -lift);

    private static void TopQuad(CanvasItem c, double x, double y, double w, double l, float lift, Color colour) =>
        c.DrawColoredPolygon(new[] { Up(x, y, lift), Up(x + w, y, lift), Up(x + w, y + l, lift), Up(x, y + l, lift) }, colour);

    /// <summary>Draws the pieces of one slice back to front.</summary>
    private static void InOrder(params (double X, double Y, Action Draw)[] parts)
    {
        foreach (var part in parts.OrderBy(p => IsoDraw.ToScreen(p.X, p.Y).Y))
        {
            part.Draw();
        }
    }

    private static (double X, double Y) Mid((double X, double Y, double W, double L) r) => (r.X + (r.W / 2), r.Y + (r.L / 2));

    /// <summary>Draws the room-specific fitted furniture. Returns false for ids it doesn't know.</summary>
    private static bool Fitted(CanvasItem c, string id, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined, Sides front)
    {
        if (front == Sides.None)
        {
            front = whole.W >= whole.L ? Sides.South : Sides.East;
        }

        switch (id)
        {
            case "office_desk":
            case "triage_desk":
                Desk(c, sx, sy, sw, sl, whole, height, colour, joined, front);
                return true;
            case "staff_table":
                StaffTable(c, sx, sy, sw, sl, whole, height, colour, joined);
                return true;
            case "reception_desk":
                TwoLevelCounter(c, sx, sy, sw, sl, whole, height, colour, joined, front, 9, 0.5, new Color("#005EB8"), "Reception", Pictogram.Info);
                return true;
            case "station_counter":
                TwoLevelCounter(c, sx, sy, sw, sl, whole, height, colour, joined, front, 5, 0.32, new Color("#00897B"), "Nurses' station", Pictogram.Cross);
                return true;
            case "pharmacy_counter":
                PharmacyCounter(c, sx, sy, sw, sl, whole, height, colour, joined, front);
                return true;
            case "clinical_cupboard":
                ClinicalCupboard(c, sx, sy, sw, sl, whole, height, colour, joined, front);
                return true;
            case "kitchenette":
                Kitchenette(c, sx, sy, sw, sl, whole, height, colour, joined, front);
                return true;
            case "bedside_locker":
                Locker(c, sx, sy, sw, sl, height, colour, front);
                return true;
            case "plaster_trolley":
                PlasterTrolley(c, sx, sy, sw, sl, whole, height, colour, joined, front);
                return true;
            case "plaster_sink":
                PlasterSink(c, sx, sy, sw, sl, whole, height, colour, joined, front);
                return true;
            case "medicine_shelves":
                MedicineShelves(c, sx, sy, sw, sl, whole, height, colour, joined, front);
                return true;
            case "drug_fridge":
                if (IsFrontSlice(sx, sy, sw, sl, whole))
                {
                    DrugFridge(c, whole.X, whole.Y, whole.W, whole.L, height, colour, front);
                }

                return true;
            case "hand_basin":
                WashStation(c, sx, sy, sw, sl, whole, height, joined, front);
                return true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ desks and tables

    /// <summary>
    /// An office desk: a top on panel ends, a modesty panel on the visitors' side and a pedestal of
    /// three drawers under the first end on the side where the member of staff sits.
    /// </summary>
    private static void Desk(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined, Sides front)
    {
        const float slab = 3f;
        var alongX = whole.W >= whole.L;
        var (first, last) = alongX ? ((joined & Sides.West) == 0, (joined & Sides.East) == 0) : ((joined & Sides.North) == 0, (joined & Sides.South) == 0);
        var panel = new Color("#C9D1D8");
        var parts = new List<(double, double, Action)>();

        // If the front is an end of the desk (a chair at its head), put the modesty panel along a long side.
        var frontIsLong = alongX ? front is Sides.North or Sides.South : front is Sides.East or Sides.West;
        var visitorSide = frontIsLong ? front : alongX ? Sides.North : Sides.West;
        var modesty = Strip(sx, sy, sw, sl, visitorSide, 0.18, 0.07);
        parts.Add((Mid(modesty).X, Mid(modesty).Y, () => IsoDraw.Prism(c, modesty.X, modesty.Y, modesty.W, modesty.L, (height - slab) * 0.62f, colour.Darkened(0.12f), colour.Darkened(0.24f), joined, (height - slab) * 0.38f)));

        void End(bool atStart)
        {
            var e = alongX ? (X: atStart ? sx + 0.05 : sx + sw - 0.15, Y: sy + 0.08, W: 0.1, L: sl - 0.16) : (X: sx + 0.08, Y: atStart ? sy + 0.05 : sy + sl - 0.15, W: sw - 0.16, L: 0.1);
            parts.Add((Mid(e).X, Mid(e).Y, () => IsoDraw.Prism(c, e.X, e.Y, e.W, e.L, height - slab, panel, panel.Darkened(0.14f))));
        }

        if (first)
        {
            End(true);

            // The drawer pedestal, tucked under the staff side.
            var staffSide = IsoFace.Opposite(visitorSide);
            var depth = DepthAlong(sw, sl, staffSide);
            var slot = alongX ? (sx + 0.2, sy, Math.Min(0.75, sw - 0.25), sl) : (sx, sy + 0.2, sw, Math.Min(0.75, sl - 0.25));
            var ped = Strip(slot.Item1, slot.Item2, slot.Item3, slot.Item4, staffSide, 0.1, depth * 0.5);
            parts.Add((Mid(ped).X, Mid(ped).Y, () =>
            {
                var h = height - slab - 1.5f;
                IsoDraw.Prism(c, ped.X, ped.Y, ped.W, ped.L, h, new Color("#E3E8EC"), new Color("#D2D9DF"), skirting: Plinth, skirtingHeight: 1.5f);
                foreach (var face in IsoFace.Of(ped.X, ped.Y, ped.W, ped.L, h))
                {
                    if (face.Side == staffSide)
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            var v = 2.5f + (i * (h - 3) / 3);
                            face.Line(c, 0.08f, v, 0.92f, v, new Color("#9AA6B0"), 0.8f);
                            face.Line(c, 0.36f, v + ((h - 3) / 6), 0.64f, v + ((h - 3) / 6), new Color("#6E7A84"), 1.2f);
                        }
                    }
                }
            }));
        }

        if (last)
        {
            End(false);
        }

        InOrder(parts.ToArray());
        IsoDraw.Prism(c, sx, sy, sw, sl, slab, colour.Lightened(0.06f), colour.Darkened(0.22f), joined, height - slab, rim: colour.Lightened(0.32f));

        // A cable port in the top of each slice.
        var port = Up(sx + (sw / 2) + (alongX ? 0 : 0.0), sy + (sl / 2), height + 0.2f);
        var off = visitorSide switch { Sides.North => new Vector2(9, -4.5f), Sides.South => new Vector2(-9, 4.5f), Sides.West => new Vector2(-9, -4.5f), _ => new Vector2(9, 4.5f) };
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(port + (off * (float)(DepthAlong(sw, sl, visitorSide) * 0.42)), 1.6f, 8), colour.Darkened(0.45f));
    }

    /// <summary>A staff room table: tubular steel legs, a pale top with a coloured edge, and somebody's mug or lunch.</summary>
    private static void StaffTable(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined)
    {
        foreach (var (lx, ly) in Corners(whole, 0.16))
        {
            if (Inside(lx, ly, sx, sy, sw, sl))
            {
                var p = IsoDraw.ToScreen(lx, ly);
                c.DrawLine(p, p + new Vector2(0, -height + 3), new Color("#8C979F"), 2.2f);
                c.DrawLine(p + new Vector2(0.7f, 0), p + new Vector2(0.7f, -height + 3), new Color("#C3CCD3"), 0.7f);
                c.DrawCircle(p, 1.3f, new Color("#3A434B"));
            }
        }

        IsoDraw.Prism(c, sx, sy, sw, sl, 3, colour.Lightened(0.28f), colour.Darkened(0.05f), joined, height - 3, rim: colour.Lightened(0.5f));
        var seed = Seed(sx, sy);
        var mid = Up(sx + (sw / 2), sy + (sl / 2), height);
        switch (seed % 4)
        {
            case 0:
                Mug(c, mid + new Vector2(-4, 1), new Color("#E53935"));
                Mug(c, mid + new Vector2(6, -3), new Color("#1E88E5"));
                break;
            case 1:
                // A lunch box and an apple.
                Figures.RoundedRect(c, new Rect2(mid + new Vector2(-6, -4), new Vector2(9, 5)), 1.2f, new Color("#4DB6AC"));
                c.DrawRect(new Rect2(mid + new Vector2(-6, -4), new Vector2(9, 1.4f)), new Color("#80CBC4"));
                c.DrawCircle(mid + new Vector2(7, -1.5f), 2f, new Color("#D32F2F"));
                break;
            case 2:
                // A newspaper and a mug.
                c.DrawColoredPolygon(new[] { mid + new Vector2(-9, 0), mid + new Vector2(-1, -4), mid + new Vector2(5, -1), mid + new Vector2(-3, 3) }, new Color("#F1F1EC"));
                c.DrawLine(mid + new Vector2(-6, 0), mid + new Vector2(0, -3), new Color("#6E7A84"), 0.7f, true);
                c.DrawLine(mid + new Vector2(-4, 1.2f), mid + new Vector2(2, -1.8f), new Color("#9AA6B0"), 0.7f, true);
                Mug(c, mid + new Vector2(8, 1), new Color("#F9A825"));
                break;
        }
    }

    private static void Mug(CanvasItem c, Vector2 at, Color colour)
    {
        c.DrawArc(at + new Vector2(2.2f, -2.2f), 1.5f, -1.5f, 1.5f, 6, colour, 0.9f, true);
        c.DrawRect(new Rect2(at + new Vector2(-2, -4.4f), new Vector2(4, 4.4f)), colour);
        c.DrawRect(new Rect2(at + new Vector2(-1.5f, -4.4f), new Vector2(3, 1f)), new Color("#4E342E"));
    }

    // ------------------------------------------------------------------ counters

    /// <summary>
    /// A reception desk or nurses' station: a low worktop on the staff side and a raised ledge on the
    /// public side, a panelled front with a coloured band, and a name plate in the middle.
    /// </summary>
    private static void TwoLevelCounter(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined, Sides front, float drop, double ledgeDepth, Color band, string name, Pictogram icon)
    {
        var low = height - drop;
        var depth = DepthAlong(sw, sl, front);
        var staff = Strip(sx, sy, sw, sl, IsoFace.Opposite(front), 0, depth - ledgeDepth);
        var ledge = Strip(sx, sy, sw, sl, front, 0, ledgeDepth);
        var cream = new Color("#F3EEE4");
        var index = SliceIndex(sx, sy, sw, sl, whole);
        var count = SliceCount(sw, sl, whole);
        InOrder(
            (Mid(staff).X, Mid(staff).Y, () =>
            {
                // The staff side: an open knee space under a pale worktop, closed at the ends.
                IsoDraw.Prism(c, staff.X, staff.Y, staff.W, staff.L, low - 2.5f, colour.Darkened(0.1f), colour.Darkened(0.2f), joined | front, skirting: Plinth, skirtingHeight: 2);
                IsoDraw.Prism(c, staff.X, staff.Y, staff.W, staff.L, 2.5f, cream, cream.Darkened(0.14f), joined | front, low - 2.5f, rim: Colors.White);
            }),
            (Mid(ledge).X, Mid(ledge).Y, () =>
            {
                IsoDraw.Prism(c, ledge.X, ledge.Y, ledge.W, ledge.L, height - 2.5f, colour, colour.Darkened(0.1f), joined, skirting: Plinth, skirtingHeight: 2.5f);
                foreach (var face in FrontFaces(ledge.X, ledge.Y, ledge.W, ledge.L, height - 2.5f, front, joined))
                {
                    // Panelled front: a paler inset panel, the coloured band and a shadow line under the ledge.
                    face.Quad(c, 0, 4.5f, 1, height - 9.5f, colour.Lightened(0.14f));
                    face.Quad(c, 0, height - 9.5f, 1, height - 6.5f, band);
                    face.Line(c, 0, height - 3.2f, 1, height - 3.2f, colour.Darkened(0.35f), 1f);
                    face.Line(c, 1, 4.5f, 1, height - 9.5f, colour.Darkened(0.12f), 0.8f);
                    // The name plate sits in the middle of the whole desk; every slice it crosses draws it.
                    var centre = front switch
                    {
                        Sides.South => IsoDraw.ToScreen(whole.X + (whole.W / 2), ledge.Y + ledge.L),
                        Sides.North => IsoDraw.ToScreen(whole.X + (whole.W / 2), ledge.Y),
                        Sides.East => IsoDraw.ToScreen(ledge.X + ledge.W, whole.Y + (whole.L / 2)),
                        _ => IsoDraw.ToScreen(ledge.X, whole.Y + (whole.L / 2)),
                    };
                    var mid = (centre - face.A).Dot((face.B - face.A).Normalized());
                    var plate = Flat.Width(name, 7) + 18;
                    if (count >= 2 && mid + (plate / 2) > 0 && mid - (plate / 2) < face.Length)
                    {
                        face.Draw(c, (length, h) =>
                        {
                            Flat.Round(c, mid - (plate / 2), h - 20.5f, plate, 11f, 1.8f, face.Lit(band));
                            Signs.Draw(c, icon, mid - (plate / 2) + 6.5f, h - 15f, 7.5f, Colors.White, face.Lit(band));
                            Flat.Text(c, name, mid + 5.5f, h - 12.3f, 7, Colors.White);
                        }, shade: false);
                    }
                }

                var over = 0.05;
                var top = Strip(sx, sy, sw, sl, front, -over, ledgeDepth + over);
                IsoDraw.Prism(c, top.X, top.Y, top.W, top.L, 2.5f, Colors.White, new Color("#DDE3E8"), joined, height - 2.5f, rim: Colors.White);

                // Now and then something left on the ledge: a pen pot or a leaflet stand.
                var (mx, my) = Mid(ledge);
                var at = Up(mx, my, height);
                switch (Seed(sx, sy) % 5)
                {
                    case 1:
                        c.DrawRect(new Rect2(at + new Vector2(-1.6f, -4), new Vector2(3.2f, 4)), new Color("#37474F"));
                        c.DrawLine(at + new Vector2(-0.6f, -4), at + new Vector2(-1.4f, -7), new Color("#1E88E5"), 0.8f);
                        c.DrawLine(at + new Vector2(0.6f, -4), at + new Vector2(1.2f, -6.6f), new Color("#E53935"), 0.8f);
                        break;
                    case 3:
                        c.DrawRect(new Rect2(at + new Vector2(-3, -6), new Vector2(6, 6)), new Color(0.85f, 0.93f, 0.97f, 0.7f));
                        c.DrawRect(new Rect2(at + new Vector2(-2.2f, -5.4f), new Vector2(4.4f, 4f)), band.Lightened(0.2f));
                        c.DrawRect(new Rect2(at + new Vector2(-2.2f, -5.4f), new Vector2(4.4f, 1.2f)), Colors.White);
                        break;
                }
            }));
    }

    /// <summary>The pharmacy counter: a white front with a green band and cross, and a glass screen with a serving hatch.</summary>
    private static void PharmacyCounter(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined, Sides front)
    {
        var green = new Color("#1E8E3E");
        var index = SliceIndex(sx, sy, sw, sl, whole);
        var count = SliceCount(sw, sl, whole);
        var middle = index == (count - 1) / 2;
        IsoDraw.Prism(c, sx, sy, sw, sl, height - 2.5f, White, new Color("#E4EAEE"), joined, skirting: Plinth, skirtingHeight: 2.5f);
        foreach (var face in OpenFaces(sx, sy, sw, sl, height - 2.5f, joined))
        {
            face.Quad(c, 0, height - 10, 1, height - 6.5f, green);
            face.Quad(c, 0, 2.5f, 1, 5f, colour);
            if (middle && face.Side == front)
            {
                face.Draw(c, (length, h) =>
                {
                    Flat.Round(c, (length / 2) - 5, h - 15.5f, 10, 10, 1.5f, green);
                    Signs.Draw(c, Pictogram.Cross, length / 2, h - 10.5f, 7.5f, Colors.White, green);
                }, shade: false);
            }
        }

        IsoDraw.Prism(c, sx, sy, sw, sl, 2.5f, new Color("#F6F8F9"), green.Lightened(0.25f), joined, height - 2.5f, rim: Colors.White);

        // The screen stands along the public edge; the middle pane has the hatch and a speech grille.
        var glass = Strip(sx, sy, sw, sl, front, 0.08, 0.06);
        var hatch = middle ? 5f : 0f;
        IsoDraw.Prism(c, glass.X, glass.Y, glass.W, glass.L, 17 - hatch, new Color(0.85f, 0.95f, 1f, 0.5f), new Color(0.62f, 0.82f, 0.93f, 0.42f), joined, height + hatch);
        IsoDraw.Prism(c, glass.X, glass.Y, glass.W, glass.L, 1.5f, Steel.Lightened(0.1f), Steel.Darkened(0.1f), joined, height + 17, rim: Colors.White);
        var alongX = whole.W >= whole.L;
        foreach (var t in new[] { 0.0, 1.0 })
        {
            var (px, py) = alongX ? (glass.X + (glass.W * t), glass.Y + (glass.L / 2)) : (glass.X + (glass.W / 2), glass.Y + (glass.L * t));
            c.DrawLine(Up(px, py, height), Up(px, py, height + 17), Steel.Darkened(0.05f), 1.2f);
        }

        if (middle)
        {
            var (gx, gy) = Mid(glass);
            c.DrawCircle(Up(gx, gy, height + 12), 2f, new Color(1, 1, 1, 0.55f));
            c.DrawCircle(Up(gx, gy, height + 12), 0.8f, Steel.Darkened(0.2f));
        }
        else if (Seed(sx, sy) % 2 == 0)
        {
            // A paper bag of medicines waiting to be collected.
            var bag = Up(sx + (sw / 2), sy + (sl / 2), height);
            c.DrawColoredPolygon(new[] { bag + new Vector2(-2.6f, 0), bag + new Vector2(2.6f, 0), bag + new Vector2(2.2f, -6), bag + new Vector2(-2.2f, -6) }, new Color("#FBFCFD"));
            c.DrawRect(new Rect2(bag + new Vector2(-1.4f, -4.2f), new Vector2(2.8f, 2.2f)), green);
        }
    }

    // ------------------------------------------------------------------ cupboards

    /// <summary>A flat door on a face: the panel outline, a bar handle and (optionally) a little label.</summary>
    private static void Door(CanvasItem c, IsoFace face, float u0, float u1, float v0, float v1, Color line, Color handle, bool handleLeft, Color? label = null)
    {
        c.DrawPolyline(new[] { face.P(u0, v0), face.P(u1, v0), face.P(u1, v1), face.P(u0, v1), face.P(u0, v0) }, face.Lit(line), 0.8f, true);
        var hu = handleLeft ? u0 + ((u1 - u0) * 0.16f) : u1 - ((u1 - u0) * 0.16f);
        face.Line(c, hu, v0 + ((v1 - v0) * 0.52f), hu, v0 + ((v1 - v0) * 0.84f), handle, 1.5f);
        if (label is { } l)
        {
            var lu = handleLeft ? u1 - ((u1 - u0) * 0.52f) : u0 + ((u1 - u0) * 0.14f);
            face.Quad(c, lu, v1 - 5.5f, lu + ((u1 - u0) * 0.38f), v1 - 2.5f, Colors.White);
            face.Quad(c, lu, v1 - 5.5f, lu + ((u1 - u0) * 0.1f), v1 - 2.5f, l);
        }
    }

    private static readonly Color[] LabelColours = { new("#1E88E5"), new("#E53935"), new("#43A047"), new("#F9A825"), new("#8E24AA") };

    /// <summary>A clinical base unit: labelled doors, a worktop with an upstand, boxes of gloves and a sharps bin.</summary>
    private static void ClinicalCupboard(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined, Sides front)
    {
        var body = height - 2.5f;
        var handle = new Color("#5E7F9C");
        var seed = Seed(sx, sy);
        IsoDraw.Prism(c, sx, sy, sw, sl, body, colour, colour.Darkened(0.07f), joined, skirting: Plinth, skirtingHeight: 2.5f);
        foreach (var face in OpenFaces(sx, sy, sw, sl, body, joined))
        {
            if (face.Side == front)
            {
                Door(c, face, 0.05f, 0.95f, 4f, body - 1.5f, colour.Darkened(0.28f), handle, seed % 2 == 0, LabelColours[seed % LabelColours.Length]);
            }
            else
            {
                c.DrawPolyline(new[] { face.P(0.08f, 4f), face.P(0.92f, 4f), face.P(0.92f, body - 1.5f), face.P(0.08f, body - 1.5f), face.P(0.08f, 4f) }, face.Lit(colour.Darkened(0.16f)), 0.7f, true);
            }
        }

        var worktop = new Color("#C5D2DC");
        IsoDraw.Prism(c, sx, sy, sw, sl, 2.5f, worktop, worktop.Darkened(0.16f), joined, body, rim: worktop.Lightened(0.4f));
        var back = Strip(sx, sy, sw, sl, IsoFace.Opposite(front), 0, 0.1);
        IsoDraw.Prism(c, back.X, back.Y, back.W, back.L, 3.5f, worktop, worktop.Darkened(0.12f), joined, height);

        var index = SliceIndex(sx, sy, sw, sl, whole);
        var (mx, my) = (sx + (sw / 2), sy + (sl / 2));
        if (index % 2 == 0)
        {
            // Boxes of gloves, one colour for each size.
            var alongX = whole.W >= whole.L;
            for (var i = 0; i < 2; i++)
            {
                var (bx, by) = alongX ? (sx + 0.1 + (i * 0.42), my - 0.2) : (mx - 0.2, sy + 0.1 + (i * 0.42));
                var box = new[] { new Color("#7E57C2"), new Color("#1E88E5"), new Color("#43A047") }[(seed + i) % 3];
                IsoDraw.Prism(c, bx, by, alongX ? 0.36 : 0.4, alongX ? 0.4 : 0.36, 4.5f, box.Lightened(0.15f), box, lift: height);
                c.DrawColoredPolygon(IsoDraw.FloorEllipse(Up(bx + (alongX ? 0.18 : 0.2), by + (alongX ? 0.2 : 0.18), height + 4.5f), 2.6f, 8), new Color("#F1F4F6"));
            }
        }
        else
        {
            // A sharps bin and a bottle of hand gel.
            var bin = Up(mx - 0.12, my, height);
            c.DrawColoredPolygon(new[] { bin + new Vector2(-3.4f, 0), bin + new Vector2(3.4f, 0), bin + new Vector2(2.8f, -7), bin + new Vector2(-2.8f, -7) }, new Color("#F7D53E"));
            c.DrawRect(new Rect2(bin + new Vector2(-3.4f, -9), new Vector2(6.8f, 2.4f)), new Color("#E65100"));
            c.DrawRect(new Rect2(bin + new Vector2(-1.5f, -5.2f), new Vector2(3f, 2.4f)), Colors.White);
            var gel = Up(mx + 0.3, my + 0.05, height);
            c.DrawRect(new Rect2(gel + new Vector2(-1.4f, -5.5f), new Vector2(2.8f, 5.5f)), new Color("#B2DFDB"));
            c.DrawRect(new Rect2(gel + new Vector2(-0.6f, -7.6f), new Vector2(1.2f, 2.2f)), Colors.White);
            c.DrawLine(gel + new Vector2(-0.6f, -7.4f), gel + new Vector2(-2.4f, -7.4f), Colors.White, 0.9f);
        }
    }

    /// <summary>The staff room kitchenette: a sink, a fridge under the worktop and a microwave, with pale doors.</summary>
    private static void Kitchenette(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined, Sides front)
    {
        var body = height - 2.5f;
        var index = SliceIndex(sx, sy, sw, sl, whole);
        var count = SliceCount(sw, sl, whole);
        var door = new Color("#C7D9E2");
        IsoDraw.Prism(c, sx, sy, sw, sl, body, door, door.Darkened(0.08f), joined, skirting: Plinth, skirtingHeight: 2.5f);
        foreach (var face in FrontFaces(sx, sy, sw, sl, body, front, joined))
        {
            if (index == 1 && count > 2)
            {
                // The fridge: a white door with a long handle and a little badge.
                face.Quad(c, 0.04f, 3.5f, 0.96f, body - 1, White);
                face.Line(c, 0.1f, body - 4.5f, 0.9f, body - 4.5f, Steel.Darkened(0.2f), 1.4f);
                face.Quad(c, 0.72f, 6f, 0.88f, 8f, new Color("#64B5F6"));
            }
            else
            {
                Door(c, face, 0.05f, 0.95f, 4f, body - 5.5f, door.Darkened(0.3f), Steel.Darkened(0.25f), index % 2 == 0);
                face.Line(c, 0.05f, body - 4f, 0.95f, body - 4f, door.Darkened(0.3f), 0.8f);
                face.Line(c, 0.38f, body - 2.6f, 0.62f, body - 2.6f, Steel.Darkened(0.25f), 1.3f);
            }
        }

        var wood = new Color("#C9A77C");
        IsoDraw.Prism(c, sx, sy, sw, sl, 2.5f, wood, wood.Darkened(0.2f), joined, body, rim: wood.Lightened(0.3f));
        var back = Strip(sx, sy, sw, sl, IsoFace.Opposite(front), 0, 0.1);
        IsoDraw.Prism(c, back.X, back.Y, back.W, back.L, 4f, White, new Color("#DDE3E8"), joined, height);
        var (mx, my) = (sx + (sw / 2), sy + (sl / 2));
        if (index == 0)
        {
            // A steel sink with its drainer and a mixer tap.
            TopQuad(c, sx + (sw * 0.14), sy + (sl * 0.18), sw * 0.72, sl * 0.6, height + 0.3f, Steel);
            TopQuad(c, sx + (sw * 0.24), sy + (sl * 0.28), sw * 0.52, sl * 0.4, height + 0.3f, new Color("#7D8A95"));
            var (tx, ty) = Mid(Strip(sx, sy, sw, sl, IsoFace.Opposite(front), 0.16, 0.05));
            var tap = Up(tx, ty, height);
            c.DrawLine(tap, tap + new Vector2(0, -7), Steel.Darkened(0.2f), 1.8f);
            var spout = (Up(mx, my, height + 7) - (tap + new Vector2(0, -7))) * 0.55f;
            c.DrawLine(tap + new Vector2(0, -7), tap + new Vector2(0, -7) + spout, Steel.Darkened(0.2f), 1.6f, true);
        }
        else if (index == count - 1)
        {
            // The microwave.
            var (wx, wy, ww, wl) = (sx + (sw * 0.1), sy + (sl * 0.2), sw * 0.8, sl * 0.6);
            IsoDraw.Prism(c, wx, wy, ww, wl, 9, White, new Color("#DDE3E8"), lift: height, rim: Colors.White);
            foreach (var face in FrontFaces(wx, wy, ww, wl, 9, front, Sides.None, height))
            {
                face.Quad(c, 0.06f, 1.5f, 0.7f, 7.5f, new Color("#1B2228"));
                face.Quad(c, 0.1f, 2.2f, 0.66f, 6.8f, new Color("#3A4A58"));
                face.Quad(c, 0.76f, 5.5f, 0.94f, 7.5f, new Color("#0D1B14"));
                face.Line(c, 0.78f, 6.5f, 0.9f, 6.5f, new Color("#5CF08A"), 0.8f);
                face.Quad(c, 0.78f, 1.8f, 0.92f, 4.2f, Steel);
            }
        }
        else if (count <= 2 || index != 1)
        {
            Mug(c, Up(mx, my, height) + new Vector2(-3, 1), new Color("#FBFCFD"));
            Mug(c, Up(mx, my, height) + new Vector2(4, -1), new Color("#1E88E5"));
        }
    }

    /// <summary>A bedside locker: a drawer over a door, on castors, with a water jug and a beaker on top.</summary>
    private static void Locker(CanvasItem c, double sx, double sy, double sw, double sl, float height, Color colour, Sides front)
    {
        foreach (var (lx, ly) in Corners((sx, sy, sw, sl), 0.12))
        {
            c.DrawCircle(IsoDraw.ToScreen(lx, ly) + new Vector2(0, -1), 1.4f, new Color("#2B3036"));
        }

        IsoDraw.Prism(c, sx + 0.04, sy + 0.04, sw - 0.08, sl - 0.08, height - 4.5f, colour, colour.Darkened(0.1f), lift: 2);
        foreach (var face in IsoFace.Of(sx + 0.04, sy + 0.04, sw - 0.08, sl - 0.08, height - 4.5f, 2))
        {
            var h = height - 4.5f;
            if (face.Side == front)
            {
                face.Line(c, 0.06f, h * 0.7f, 0.94f, h * 0.7f, colour.Darkened(0.35f), 0.8f);
                face.Line(c, 0.36f, h * 0.84f, 0.64f, h * 0.84f, new Color("#6E7A84"), 1.4f);
                Door(c, face, 0.07f, 0.93f, 1.5f, (h * 0.7f) - 1.2f, colour.Darkened(0.3f), new Color("#6E7A84"), true);
            }
            else
            {
                // The towel rail on the side.
                face.Line(c, 0.2f, h * 0.6f, 0.8f, h * 0.6f, Steel, 1.2f);
            }
        }

        IsoDraw.Prism(c, sx, sy, sw, sl, 2.5f, colour.Lightened(0.2f), colour.Darkened(0.05f), lift: height - 2.5f, rim: colour.Lightened(0.4f));
        var top = Up(sx + (sw / 2), sy + (sl / 2), height);
        var jug = top + new Vector2(-3.5f, 0.5f);
        c.DrawColoredPolygon(new[] { jug + new Vector2(-2.6f, 0), jug + new Vector2(2.6f, 0), jug + new Vector2(2.2f, -7), jug + new Vector2(-2.2f, -7) }, new Color(0.78f, 0.9f, 0.97f, 0.9f));
        c.DrawRect(new Rect2(jug + new Vector2(-2.4f, -4.4f), new Vector2(4.8f, 4.4f)), new Color(0.5f, 0.76f, 0.94f, 0.9f));
        c.DrawRect(new Rect2(jug + new Vector2(-2.6f, -8.2f), new Vector2(5.2f, 1.4f)), new Color("#1E88E5"));
        var beaker = top + new Vector2(4.5f, -0.5f);
        c.DrawRect(new Rect2(beaker + new Vector2(-1.5f, -3.6f), new Vector2(3, 3.6f)), new Color(0.86f, 0.94f, 0.98f, 0.95f));
        if (Seed(sx, sy) % 2 == 0)
        {
            // A get well card.
            var card = top + new Vector2(1, -5);
            c.DrawColoredPolygon(new[] { card + new Vector2(-2.5f, 2), card + new Vector2(2.5f, 0), card + new Vector2(2.5f, -5), card + new Vector2(-2.5f, -3) }, new Color("#FFF8E1"));
            c.DrawCircle(card + new Vector2(0, -1.6f), 1.3f, new Color("#EC407A"));
        }
    }

    // ------------------------------------------------------------------ plaster room

    /// <summary>The plaster trolley: steel drawers on castors, the top set out with rolls of plaster, a bowl and shears.</summary>
    private static void PlasterTrolley(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined, Sides front)
    {
        foreach (var (lx, ly) in Corners(whole, 0.12))
        {
            if (Inside(lx, ly, sx, sy, sw, sl))
            {
                c.DrawCircle(IsoDraw.ToScreen(lx, ly) + new Vector2(0, -1.4f), 1.8f, new Color("#2B3036"));
            }
        }

        var body = height - 5;
        IsoDraw.Prism(c, sx, sy, sw, sl, body, colour, colour.Darkened(0.1f), joined, 3, rim: colour.Lightened(0.3f));
        foreach (var face in FrontFaces(sx, sy, sw, sl, body, front, joined, 3))
        {
            for (var i = 0; i < 3; i++)
            {
                var v = 1.5f + (i * (body - 2) / 3);
                c.DrawPolyline(new[] { face.P(0.06f, v), face.P(0.94f, v), face.P(0.94f, v + ((body - 2) / 3) - 1.2f), face.P(0.06f, v + ((body - 2) / 3) - 1.2f), face.P(0.06f, v) }, face.Lit(colour.Darkened(0.3f)), 0.7f, true);
                face.Line(c, 0.36f, v + ((body - 2) / 6), 0.64f, v + ((body - 2) / 6), colour.Darkened(0.45f), 1.3f);
                face.Quad(c, 0.1f, v + ((body - 2) / 3) - 4f, 0.24f, v + ((body - 2) / 3) - 2.2f, LabelColours[i]);
            }
        }

        IsoDraw.Prism(c, sx, sy, sw, sl, 2, Steel.Lightened(0.1f), Steel.Darkened(0.12f), joined, height - 2, rim: Colors.White);
        var index = SliceIndex(sx, sy, sw, sl, whole);
        var (mx, my) = (sx + (sw / 2), sy + (sl / 2));
        if (index % 2 == 0)
        {
            // Rolls of plaster bandage and padding.
            foreach (var (ox, oy, roll) in new[] { (-0.22, -0.2, "#FBFCFD"), (0.14, -0.22, "#FBFCFD"), (-0.05, 0.18, "#B3E5FC"), (0.26, 0.16, "#F8BBD0") })
            {
                var p = Up(mx + ox, my + oy, height);
                c.DrawRect(new Rect2(p + new Vector2(-2.6f, -3.6f), new Vector2(5.2f, 3.6f)), new Color(roll).Darkened(0.12f));
                c.DrawColoredPolygon(IsoDraw.FloorEllipse(p + new Vector2(0, -3.6f), 2.6f, 10), new Color(roll));
                c.DrawCircle(p + new Vector2(0, -3.6f), 0.7f, new Color("#9AA6B0"));
            }
        }
        else
        {
            // A bowl of water and the plaster shears.
            var bowl = Up(mx - 0.1, my, height);
            c.DrawColoredPolygon(IsoDraw.FloorEllipse(bowl + new Vector2(0, -2), 6f, 14), Steel.Darkened(0.1f));
            c.DrawColoredPolygon(IsoDraw.FloorEllipse(bowl + new Vector2(0, -2.4f), 4.8f, 14), new Color("#9FD3EE"));
            var shears = Up(mx + 0.3, my + 0.25, height);
            c.DrawLine(shears + new Vector2(-4, -1), shears + new Vector2(4, -3), new Color("#6E7A84"), 1.1f, true);
            c.DrawLine(shears + new Vector2(-4, -3), shears + new Vector2(4, -1), new Color("#6E7A84"), 1.1f, true);
            c.DrawCircle(shears + new Vector2(-4.4f, -1), 1.3f, new Color("#E65100"));
            c.DrawCircle(shears + new Vector2(-4.4f, -3), 1.3f, new Color("#E65100"));
        }
    }

    /// <summary>The plaster sink: a deep steel bowl with a drainer, a tall tap and the plaster trap underneath.</summary>
    private static void PlasterSink(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined, Sides front)
    {
        var body = height - 2.5f;
        IsoDraw.Prism(c, sx, sy, sw, sl, body, colour, colour.Darkened(0.12f), joined, skirting: Plinth, skirtingHeight: 2.5f);
        var index = SliceIndex(sx, sy, sw, sl, whole);
        foreach (var face in FrontFaces(sx, sy, sw, sl, body, front, joined))
        {
            Door(c, face, 0.06f, 0.94f, 4f, body - 1.5f, colour.Darkened(0.35f), colour.Darkened(0.5f), index % 2 == 0, index == 0 ? new Color("#F9A825") : null);
        }

        IsoDraw.Prism(c, sx, sy, sw, sl, 2.5f, Steel.Lightened(0.12f), Steel.Darkened(0.12f), joined, body, rim: Colors.White);
        var back = Strip(sx, sy, sw, sl, IsoFace.Opposite(front), 0, 0.1);
        IsoDraw.Prism(c, back.X, back.Y, back.W, back.L, 5f, Steel.Lightened(0.1f), Steel.Darkened(0.1f), joined, height);
        var (mx, my) = (sx + (sw / 2), sy + (sl / 2));
        if (index == 0)
        {
            TopQuad(c, sx + (sw * 0.12), sy + (sl * 0.2), sw * 0.76, sl * 0.6, height + 0.3f, new Color("#6E7A84"));
            TopQuad(c, sx + (sw * 0.2), sy + (sl * 0.3), sw * 0.6, sl * 0.42, height + 0.3f, new Color("#4F5A64"));
            var (tx, ty) = Mid(Strip(sx, sy, sw, sl, IsoFace.Opposite(front), 0.16, 0.05));
            var tap = Up(tx, ty, height);
            c.DrawLine(tap, tap + new Vector2(0, -10), Steel.Darkened(0.25f), 1.8f);
            var spout = (Up(mx, my, height + 10) - (tap + new Vector2(0, -10))) * 0.6f;
            c.DrawLine(tap + new Vector2(0, -10), tap + new Vector2(0, -10) + spout, Steel.Darkened(0.25f), 1.6f, true);
            c.DrawLine(tap + new Vector2(0, -10) + spout, tap + new Vector2(0, -7.5f) + spout, Steel.Darkened(0.25f), 1.4f);
        }
        else
        {
            // The grooved drainer, with a bucket of plaster water on it.
            var alongX = whole.W >= whole.L;
            for (var i = 1; i < 5; i++)
            {
                var t = i / 5.0;
                c.DrawLine(alongX ? Up(sx + (sw * t), sy + (sl * 0.22), height + 0.3f) : Up(sx + (sw * 0.22), sy + (sl * t), height + 0.3f),
                    alongX ? Up(sx + (sw * t), sy + (sl * 0.8), height + 0.3f) : Up(sx + (sw * 0.8), sy + (sl * t), height + 0.3f), Steel.Darkened(0.2f), 0.8f, true);
            }

            var jug = Up(mx, my, height);
            c.DrawColoredPolygon(new[] { jug + new Vector2(-3, 0), jug + new Vector2(3, 0), jug + new Vector2(3.6f, -6.5f), jug + new Vector2(-3.6f, -6.5f) }, new Color("#E9EEF2"));
            c.DrawColoredPolygon(IsoDraw.FloorEllipse(jug + new Vector2(0, -6.5f), 3.6f, 10), new Color("#CFD8DC"));
        }
    }

    // ------------------------------------------------------------------ pharmacy

    /// <summary>Dispensary shelving: boxes, bottles and tubs of medicines on five shelves over a locked drugs cupboard.</summary>
    private static void MedicineShelves(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Color colour, Sides joined, Sides front)
    {
        IsoDraw.Prism(c, sx, sy, sw, sl, height, colour.Lightened(0.1f), colour, joined, skirting: Plinth, skirtingHeight: 2.5f, rim: colour.Lightened(0.3f));
        var index = SliceIndex(sx, sy, sw, sl, whole);
        var count = SliceCount(sw, sl, whole);
        var seed = Seed(sx, sy);
        foreach (var face in FrontFaces(sx, sy, sw, sl, height, front, joined))
        {
            face.Draw(c, (length, h) =>
            {
                var left = (joined & (whole.W >= whole.L ? Sides.West : Sides.North)) == 0 ? 2f : 0.6f;
                var cupboard = 14f;
                Flat.Rect(c, left, 2, length - left - 0.6f, h - 4 - cupboard, colour.Darkened(0.22f));
                const int shelves = 5;
                var sh = (h - 4 - cupboard) / shelves;
                for (var s = 0; s < shelves; s++)
                {
                    var foot = 2 + ((s + 1) * sh) - 1;
                    var x = left + 1f;
                    var k = 0;
                    while (x < length - 4)
                    {
                        var pick = (seed + (s * 7) + (k * 3) + (index * 5)) % 7;
                        var box = BoxColours[pick];
                        var bw = 3f + (((seed + s + k) % 3) * 1.1f);
                        var bh = sh * (0.5f + (((seed + (k * 2) + s) % 4) * 0.1f));
                        if (s == 2)
                        {
                            // Brown medicine bottles with white caps.
                            Flat.Round(c, x, foot - (sh * 0.62f), 3.2f, sh * 0.62f, 0.8f, new Color("#7A4A2A"));
                            Flat.Rect(c, x + 0.6f, foot - (sh * 0.76f), 2f, sh * 0.16f, Colors.White);
                            Flat.Rect(c, x, foot - (sh * 0.42f), 3.2f, sh * 0.2f, new Color("#FFF8E1"));
                            bw = 3.2f;
                        }
                        else
                        {
                            Flat.Rect(c, x, foot - bh, bw, bh, box);
                            Flat.Rect(c, x, foot - (bh * 0.72f), bw, bh * 0.2f, box.Lightened(0.55f));
                        }

                        x += bw + 0.9f;
                        k++;
                    }

                    Flat.Rect(c, left, foot, length - left - 0.6f, 1.1f, colour.Lightened(0.15f));
                }

                // The cupboard underneath; the middle one is the locked controlled drugs cupboard.
                var top = h - 2.5f - cupboard + 1.5f;
                Flat.Rect(c, left, top, length - left - 0.6f, cupboard - 2.5f, colour.Lightened(0.05f));
                Flat.Rect(c, left, top, length - left - 0.6f, 0.7f, colour.Darkened(0.3f));
                Flat.Rect(c, (length / 2) - 0.4f, top, 0.8f, cupboard - 2.5f, colour.Darkened(0.3f));
                Flat.Rect(c, (length / 2) - 3.2f, top + 3f, 1.4f, 4f, new Color("#6E7A84"));
                Flat.Rect(c, (length / 2) + 1.8f, top + 3f, 1.4f, 4f, new Color("#6E7A84"));
                if (index == count / 2)
                {
                    Flat.Round(c, length * 0.58f, top + 1.6f, 8.5f, 4.6f, 1f, new Color("#D5281B"));
                    Flat.Text(c, "CD", (length * 0.58f) + 4.25f, top + 5.4f, 4, Colors.White);
                    c.DrawCircle(new Vector2(length / 2, top + 8.5f), 1.1f, new Color("#F9C22E"));
                }
            });
        }
    }

    /// <summary>The drugs fridge: a glass door on shelves of vials, a temperature display, a lock and a blue badge.</summary>
    private static void DrugFridge(CanvasItem c, double sx, double sy, double sw, double sl, float height, Color colour, Sides front)
    {
        (sx, sy, sw, sl) = (sx + 0.08, sy + 0.08, sw - 0.16, sl - 0.16);
        c.DrawColoredPolygon(new[] { IsoDraw.ToScreen(sx, sy) + new Vector2(2, 2), IsoDraw.ToScreen(sx + sw + 0.1, sy) + new Vector2(2, 2), IsoDraw.ToScreen(sx + sw + 0.1, sy + sl + 0.1) + new Vector2(2, 2), IsoDraw.ToScreen(sx, sy + sl + 0.1) + new Vector2(2, 2) }, new Color(0, 0, 0, 0.12f));
        IsoDraw.Prism(c, sx, sy, sw, sl, height, colour, colour.Darkened(0.08f), skirting: Plinth, skirtingHeight: 2.5f, rim: Colors.White);
        var seed = Seed(sx, sy);
        foreach (var face in FrontFaces(sx, sy, sw, sl, height, front, Sides.None))
        {
            face.Draw(c, (length, h) =>
            {
                // The control strip, then the glass door.
                Flat.Round(c, 2, 2, length - 4, 7, 1.2f, new Color("#27323B"));
                Flat.Rect(c, 4, 3.4f, 11, 4.2f, new Color("#0D1B14"));
                Flat.TextLeft(c, "4.0", 4.8f, 7f, 4, new Color("#5CF08A"));
                c.DrawCircle(new Vector2(length - 5.5f, 5.5f), 1.3f, new Color("#F9C22E"));
                var (gx, gy, gw, gh) = (3f, 11f, length - 6, h - 16);
                Flat.Rect(c, gx - 1, gy - 1, gw + 2, gh + 2, new Color("#B9C6CF"));
                Flat.Rect(c, gx, gy, gw, gh, new Color("#CDE7F3"));
                Flat.Rect(c, gx, gy, gw, 1.4f, Colors.White);
                const int rows = 4;
                var rh = gh / rows;
                for (var r = 0; r < rows; r++)
                {
                    var foot = gy + ((r + 1) * rh) - 0.9f;
                    for (var x = gx + 1.4f; x < gx + gw - 3; x += 3.3f)
                    {
                        var kind = (seed + r + (int)x) % 4;
                        var vial = new[] { new Color("#FFFFFF"), new Color("#FFE082"), new Color("#90CAF9"), new Color("#F8BBD0") }[kind];
                        if (r % 2 == 0)
                        {
                            Flat.Rect(c, x, foot - (rh * 0.55f), 2.4f, rh * 0.55f, vial);
                            Flat.Rect(c, x + 0.4f, foot - (rh * 0.7f), 1.6f, rh * 0.15f, new Color("#607D8B"));
                        }
                        else
                        {
                            Flat.Rect(c, x, foot - (rh * 0.45f), 2.8f, rh * 0.45f, vial);
                            Flat.Rect(c, x, foot - (rh * 0.3f), 2.8f, rh * 0.12f, new Color("#1E88E5"));
                        }
                    }

                    Flat.Rect(c, gx, foot, gw, 0.9f, new Color("#F4FAFD"));
                }

                c.DrawColoredPolygon(new[] { new Vector2(gx + (gw * 0.3f), gy), new Vector2(gx + (gw * 0.5f), gy), new Vector2(gx + (gw * 0.25f), gy + gh), new Vector2(gx + (gw * 0.05f), gy + gh) }, new Color(1, 1, 1, 0.16f));
                Flat.Rect(c, gx + gw - 2.6f, gy + (gh * 0.3f), 1.4f, gh * 0.4f, new Color("#8C99A5"));
            });
        }
    }

    // ------------------------------------------------------------------ wash basin

    /// <summary>
    /// A clinical wash station: a white panel up the wall with soap and paper towel dispensers, a
    /// ceramic basin with a lever tap, the boxed-in pipework below and a pedal bin beside it.
    /// </summary>
    private static void WashStation(CanvasItem c, double sx, double sy, double sw, double sl, (double X, double Y, double W, double L) whole, float height, Sides joined, Sides front)
    {
        var back = IsoFace.Opposite(front);
        var depth = DepthAlong(sw, sl, front);
        var panel = Strip(sx, sy, sw, sl, back, 0, 0.14);
        var unit = Strip(sx, sy, sw, sl, back, 0.14, depth - 0.3);
        var tall = height * 1.85f;
        var alongX = whole.W >= whole.L;
        var first = SliceIndex(sx, sy, sw, sl, whole) == 0;
        InOrder(
            (Mid(panel).X, Mid(panel).Y, () =>
            {
                IsoDraw.Prism(c, panel.X, panel.Y, panel.W, panel.L, tall, White, new Color("#E7ECF0"), joined, skirting: Plinth, skirtingHeight: 2.5f, rim: Colors.White);
                foreach (var face in FrontFaces(panel.X, panel.Y, panel.W, panel.L, tall, front, joined))
                {
                    if (first)
                    {
                        // Soap (teal) and hand gel dispensers.
                        face.Quad(c, 0.52f, tall - 12, 0.86f, tall - 4.5f, Colors.White);
                        face.Quad(c, 0.52f, tall - 12, 0.86f, tall - 9.5f, new Color("#2BB3A3"));
                        face.Quad(c, 0.64f, tall - 13.5f, 0.74f, tall - 12, Steel.Darkened(0.2f));
                    }
                    else
                    {
                        // The paper towel dispenser, a towel poking out.
                        face.Quad(c, 0.12f, tall - 13, 0.88f, tall - 3.5f, new Color("#CFD8DC"));
                        face.Quad(c, 0.12f, tall - 13, 0.88f, tall - 11.5f, new Color("#9AA6B0"));
                        face.Quad(c, 0.3f, tall - 15.5f, 0.7f, tall - 13, Colors.White);
                    }
                }
            }),
            (Mid(unit).X, Mid(unit).Y, () =>
            {
                IsoDraw.Prism(c, unit.X, unit.Y, unit.W, unit.L, height - 5, White, new Color("#E1E7EC"), joined, skirting: Plinth, skirtingHeight: 2.5f);
                foreach (var face in FrontFaces(unit.X, unit.Y, unit.W, unit.L, height - 5, front, joined))
                {
                    c.DrawPolyline(new[] { face.P(0.08f, 4), face.P(0.92f, 4), face.P(0.92f, height - 7), face.P(0.08f, height - 7), face.P(0.08f, 4) }, face.Lit(new Color("#B9C6CF")), 0.7f, true);
                }

                // The basin: white ceramic with a grey bowl, sitting proud of the unit.
                var bowl = Strip(sx, sy, sw, sl, back, 0.14, depth - 0.2);
                var (bx, by, bw, bl) = alongX ? (bowl.X + ((joined & Sides.West) == 0 ? 0.08 : 0), bowl.Y, bowl.W - ((joined & Sides.West) == 0 ? 0.08 : 0) - ((joined & Sides.East) == 0 ? 0.08 : 0), bowl.L)
                    : (bowl.X, bowl.Y + ((joined & Sides.North) == 0 ? 0.08 : 0), bowl.W, bowl.L - ((joined & Sides.North) == 0 ? 0.08 : 0) - ((joined & Sides.South) == 0 ? 0.08 : 0));
                IsoDraw.Prism(c, bx, by, bw, bl, 5, Colors.White, new Color("#E9EEF2"), joined, height - 5, rim: Colors.White);
                if (first)
                {
                    var (cx, cy) = alongX ? (whole.X + (whole.W / 2), by + (bl / 2)) : (bx + (bw / 2), whole.Y + (whole.L / 2));
                    var (rw, rl) = alongX ? (Math.Min(whole.W * 0.32, 0.42), bl * 0.3) : (bw * 0.3, Math.Min(whole.L * 0.32, 0.42));
                    TopQuad(c, cx - rw, cy - rl, 2 * rw, 2 * rl, height + 0.3f, new Color("#CBD6DE"));
                    TopQuad(c, cx - (rw * 0.6), cy - (rl * 0.6), 1.2 * rw, 1.2 * rl, height + 0.3f, new Color("#AEBBC5"));
                    var (tx, ty) = back switch { Sides.North => (cx, by + 0.08), Sides.South => (cx, by + bl - 0.08), Sides.West => (bx + 0.08, cy), _ => (bx + bw - 0.08, cy) };
                    var tap = Up(tx, ty, height);
                    c.DrawLine(tap, tap + new Vector2(0, -6), Steel.Darkened(0.2f), 1.8f);
                    var spout = (Up(cx, cy, height + 6) - (tap + new Vector2(0, -6))) * 0.7f;
                    c.DrawLine(tap + new Vector2(0, -6), tap + new Vector2(0, -6) + spout, Steel.Darkened(0.2f), 1.6f, true);
                    c.DrawLine(tap + new Vector2(0, -6), tap + new Vector2(3, -8.5f), Steel.Darkened(0.35f), 1.2f, true);
                }
            }));
    }
}
