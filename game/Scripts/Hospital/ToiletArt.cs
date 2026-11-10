using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Drawings for the toilets: cubicles with laminate partitions and a door whose latch shows red when
/// someone is inside, white toilets with a cistern and seat, an accessible toilet with grab rails and a
/// red emergency pull cord, wash basins with a mirror above, and a hand dryer on the wall. Each piece is
/// drawn with its back to the nearest wall, so it works at every size, door side and view angle.
/// </summary>
public static class ToiletArt
{
    private const float Ppm = IsoDraw.PixelsPerMetre;
    private static readonly Color Porcelain = new("#F7F9FA");
    private static readonly Color Chrome = new("#B0BEC5");
    private static readonly Color Engaged = new("#E53935");
    private static readonly Color Vacant = new("#43A047");

    public static bool Draws(string itemId) => itemId is "toilet_cubicle" or "toilet_pan" or "accessible_wc" or "wash_basin" or "hand_dryer";

    /// <summary>
    /// Adds the props for one item. <paramref name="addProp"/> places a drawing sorted by its anchor; its last
    /// argument asks for the drawing to be redrawn every frame (the cubicle's engaged sign).
    /// </summary>
    public static void Add(string itemId, CareSpace room, PlacedFurniture item, Color colour, HospitalSimulation sim, Action<Vector2, Action<CanvasItem>, bool> addProp)
    {
        var (x, y, w, l) = (item.X, item.Y, item.Width, item.Length);
        var anchor = IsoDraw.ToScreen(x + (w / 2), y + (l / 2));
        switch (itemId)
        {
            case "toilet_cubicle":
                // The cubicles stand on the back wall and open towards the door.
                var front = Towards(room.DoorSide);
                var spot = room.ToiletSpots.FirstOrDefault(t => item.Overlaps(t.X, t.Y, 1, 1));
                bool InUse() => sim.ToiletTrips.Any(t => t.Stage == ToiletStage.Using && t.RoomId == room.Id && t.Target == spot);
                addProp(anchor, c => Cubicle(c, x, y, w, l, colour, front, InUse()), true);
                break;

            case "toilet_pan":
                addProp(anchor, c => Toilet(c, x, y, w, l, BackOf(room, x, y, w, l), accessible: false), false);
                break;

            case "accessible_wc":
                addProp(anchor, c => Toilet(c, x, y, w, l, BackOf(room, x, y, w, l), accessible: true), false);
                break;

            case "wash_basin":
                addProp(anchor, c => Basin(c, x, y, w, l, BackOf(room, x, y, w, l)), false);
                break;

            case "hand_dryer":
                addProp(anchor, c => Dryer(c, x, y, w, l, colour, BackOf(room, x, y, w, l)), false);
                break;
        }
    }

    /// <summary>The unit step from the room into its door wall.</summary>
    private static (int X, int Y) Towards(WallSide side) => side switch
    {
        WallSide.North => (0, -1),
        WallSide.South => (0, 1),
        WallSide.East => (1, 0),
        _ => (-1, 0),
    };

    /// <summary>Which way the nearest wall is from an item: its back goes against it.</summary>
    private static (int X, int Y) BackOf(CareSpace room, double x, double y, double w, double l)
    {
        var r = room.Rect;
        var gaps = new[]
        {
            (Gap: y - r.Y, Dir: (0, -1)),
            (Gap: r.Bottom - (y + l), Dir: (0, 1)),
            (Gap: x - r.X, Dir: (-1, 0)),
            (Gap: r.Right - (x + w), Dir: (1, 0)),
        };
        return gaps.OrderBy(g => g.Gap).First().Dir;
    }

    /// <summary>
    /// A strip of the footprint: <paramref name="from"/>-<paramref name="to"/> of the way from the back
    /// (0) to the front (1), and <paramref name="a0"/>-<paramref name="a1"/> of the way across.
    /// </summary>
    private static (double X, double Y, double W, double L) Part(double x, double y, double w, double l, (int X, int Y) back, double from, double to, double a0 = 0, double a1 = 1)
    {
        switch (back)
        {
            case (0, -1):
                return (x + (w * a0), y + (l * from), w * (a1 - a0), l * (to - from));
            case (0, 1):
                return (x + (w * a0), y + (l * (1 - to)), w * (a1 - a0), l * (to - from));
            case (-1, 0):
                return (x + (w * from), y + (l * a0), w * (to - from), l * (a1 - a0));
            default:
                return (x + (w * (1 - to)), y + (l * a0), w * (to - from), l * (a1 - a0));
        }
    }

    private static void Box(CanvasItem c, (double X, double Y, double W, double L) r, float height, Color colour, float lift = 0, bool shadow = false) =>
        IsoDraw.Box(c, r.X, r.Y, r.W, r.L, height, colour, lift: lift, shadow: shadow);

    /// <summary>An ellipse lying flat, <paramref name="lift"/> pixels off the floor.</summary>
    private static void FlatEllipse(CanvasItem c, double cx, double cy, double rx, double ry, float lift, Color colour)
    {
        var points = new Vector2[16];
        for (var i = 0; i < points.Length; i++)
        {
            var a = i * Math.PI * 2 / points.Length;
            points[i] = IsoDraw.ToScreen(cx + (Math.Cos(a) * rx), cy + (Math.Sin(a) * ry)) + new Vector2(0, -lift);
        }

        c.DrawColoredPolygon(points, colour);
    }

    private static double Depth((double X, double Y, double W, double L) r) => IsoDraw.ToScreen(r.X + (r.W / 2), r.Y + (r.L / 2)).Y;

    private static readonly Color Metal = new("#B4BEC6");
    private static readonly Color RailBlue = new("#1F5FA8");

    /// <summary>For the art gallery: one piece drawn on its own. <paramref name="dir"/> is the cubicle's front, or the back of anything else.</summary>
    public static void Preview(CanvasItem c, string itemId, double x, double y, double w, double l, (int X, int Y) dir, Color colour, bool inUse = false)
    {
        switch (itemId)
        {
            case "toilet_cubicle": Cubicle(c, x, y, w, l, colour, dir, inUse); break;
            case "toilet_pan": Toilet(c, x, y, w, l, dir, accessible: false); break;
            case "accessible_wc": Toilet(c, x, y, w, l, dir, accessible: true); break;
            case "wash_basin": Basin(c, x, y, w, l, dir); break;
            case "hand_dryer": Dryer(c, x, y, w, l, colour, dir); break;
        }
    }

    private static Vector2 Up(double gx, double gy, float lift) => IsoDraw.ToScreen(gx, gy) + new Vector2(0, -lift);

    /// <summary>A point of the footprint: how far from the back (0) to the front (1), and how far across.</summary>
    private static Vector2 Point(double x, double y, double w, double l, (int X, int Y) back, double from, double across, float lift)
    {
        var r = Part(x, y, w, l, back, from, from, across, across);
        return Up(r.X, r.Y, lift);
    }

    /// <summary>True when the wall an item backs onto is on the far side of it, so things hung on that wall can be seen.</summary>
    private static bool WallBehind((int X, int Y) back) => (IsoDraw.ToScreen(back.X, back.Y) - IsoDraw.ToScreen(0, 0)).Y < 0;

    /// <summary>
    /// A cubicle of laminate panels on metal legs under a head rail: two partitions, and a front of two
    /// fixed posts with the door between them. Outside, the door has an indicator bolt that turns red
    /// when someone is in; inside, a slide latch and a coat hook.
    /// </summary>
    private static void Cubicle(CanvasItem c, double x, double y, double w, double l, Color colour, (int X, int Y) front, bool inUse)
    {
        var back = (-front.X, -front.Y);
        var panel = 1.75f * Ppm;
        var gap = 0.15f * Ppm;
        const double thick = 0.07;
        var depthTiles = front.X == 0 ? l : w;
        var acrossTiles = front.X == 0 ? w : l;
        var t = thick / depthTiles;
        var s = thick / acrossTiles;
        var doorColour = colour.Lightened(0.12f);

        // Pieces sorted back to front for the current view, so whatever is nearer covers what is behind.
        var pieces = new List<((double X, double Y, double W, double L) Rect, Action Draw)>();
        var pan = Part(x, y, w, l, back, 0.04, 0.5, 0.25, 0.75);
        pieces.Add((pan, () => Toilet(c, pan.X, pan.Y, pan.W, pan.L, back, accessible: false)));

        void Partition(double a0, double a1)
        {
            var side = Part(x, y, w, l, back, 0, 1, a0, a1);
            pieces.Add((side, () =>
            {
                Box(c, Part(x, y, w, l, back, 0.03, 0.03 + t, a0, a1), gap, Metal);
                Box(c, Part(x, y, w, l, back, 0.55, 0.55 + t, a0, a1), gap, Metal);
                Box(c, side, panel, colour, gap);
                Box(c, side, 1.5f, Metal, panel + gap);
            }));
        }

        Partition(0, s);
        Partition(1 - s, 1);

        var door = Part(x, y, w, l, back, 1 - t, 1, 0, 1);
        pieces.Add((door, () =>
        {
            Box(c, Part(x, y, w, l, back, 1 - t, 1, 0.17, 0.17 + s), gap, Metal);
            Box(c, Part(x, y, w, l, back, 1 - t, 1, 0.83 - s, 0.83), gap, Metal);
            Box(c, door, panel, doorColour, gap);
            foreach (var face in IsoFace.Of(door.X, door.Y, door.W, door.L, panel, gap))
            {
                var outside = face.Side == IsoFace.SideOf(front);
                if (!outside && face.Side != IsoFace.SideOf(back))
                {
                    continue;
                }

                face.Draw(c, (length, height) =>
                {
                    var (p0, p1) = (length * 0.2f, length * 0.8f);
                    Flat.Rect(c, 0, 0, length, height, colour);
                    Flat.Rect(c, p0, 0, p1 - p0, height, doorColour);
                    var line = colour.Darkened(0.35f);
                    c.DrawLine(new Vector2(p0, 0), new Vector2(p0, height), line, 0.8f);
                    c.DrawLine(new Vector2(p1, 0), new Vector2(p1, height), line, 0.8f);
                    Flat.Rect(c, p0 - 1.5f, height * 0.16f, 3, 3.5f, Metal);
                    Flat.Rect(c, p0 - 1.5f, height * 0.78f, 3, 3.5f, Metal);

                    // The lock, at hand height on the opening edge of the door.
                    var lockY = height - ((0.95f * Ppm) - gap);
                    if (outside)
                    {
                        var bolt = new Vector2(p1 - 5.5f, lockY);
                        c.DrawCircle(bolt, 3.6f, Metal.Darkened(0.15f));
                        c.DrawCircle(bolt, 3f, Colors.White);
                        c.DrawCircle(bolt, 2.3f, inUse ? Engaged : Vacant);
                        Flat.Rect(c, bolt.X - 1.6f, bolt.Y - 0.45f, 3.2f, 0.9f, Colors.White);
                        Flat.Round(c, bolt.X - 1.2f, lockY + 5, 2.4f, 5.5f, 1f, Metal);
                    }
                    else
                    {
                        Flat.Rect(c, p1 - 8.5f, lockY - 1.3f, 9.5f, 2.6f, Metal);
                        c.DrawCircle(new Vector2(p1 - 6.5f, lockY), 1.5f, Metal.Darkened(0.3f));
                        c.DrawCircle(new Vector2((p0 + p1) / 2, 7), 1.5f, Metal);
                        c.DrawLine(new Vector2((p0 + p1) / 2, 7), new Vector2((p0 + p1) / 2, 9.5f), Metal.Darkened(0.2f), 1f);
                    }
                });
            }
        }));

        foreach (var piece in pieces.OrderBy(p => Depth(p.Rect)))
        {
            piece.Draw();
        }

        // The head rail ties the posts and partitions together.
        var rail = Part(x, y, w, l, back, 1 - t, 1, 0, 1);
        Box(c, rail, 2.5f, Metal, panel + gap);
    }

    /// <summary>
    /// A close-coupled toilet: a rounded bowl narrowing to its foot, a seat with the lid up against the
    /// cistern, and a chrome flush button. The accessible one has blue grab rails (one of them the
    /// drop-down kind) and a red emergency pull cord.
    /// </summary>
    private static void Toilet(CanvasItem c, double x, double y, double w, double l, (int X, int Y) back, bool accessible)
    {
        var seat = 0.43f * Ppm;
        var tank = Part(x, y, w, l, back, 0, 0.27, 0.06, 0.94);
        var neck = Part(x, y, w, l, back, 0.06, 0.4, 0.28, 0.72);
        var bowl = Part(x, y, w, l, back, 0.32, 1, 0.2, 0.8);
        var (bx, by) = (bowl.X + (bowl.W / 2), bowl.Y + (bowl.L / 2));
        var (rx, ry) = (bowl.W / 2, bowl.L / 2);
        var pieces = new List<(double Depth, Action Draw)>();

        pieces.Add((Depth(tank), () =>
        {
            Box(c, neck, seat, Porcelain.Darkened(0.1f), 0, shadow: true);
            Box(c, tank, 0.38f * Ppm, Porcelain.Darkened(0.03f), seat - 1);
            var lid = (tank.X - 0.02, tank.Y - 0.02, tank.W + 0.04, tank.L + 0.04);
            Box(c, lid, 1.6f, Porcelain, seat - 1 + (0.38f * Ppm));
            var top = seat + (0.38f * Ppm) + 0.8f;
            FlatEllipse(c, tank.X + (tank.W / 2), tank.Y + (tank.L / 2), 0.1, 0.1, top, Chrome);
            FlatEllipse(c, tank.X + (tank.W / 2), tank.Y + (tank.L / 2), 0.05, 0.05, top, Chrome.Darkened(0.25f));
        }));

        pieces.Add((Depth(Part(x, y, w, l, back, 0.27, 0.31, 0.1, 0.9)), () =>
        {
            // The lid, standing up against the cistern.
            var points = new Vector2[13];
            for (var i = 0; i < points.Length; i++)
            {
                var a = Math.PI * i / (points.Length - 1);
                points[i] = Point(x, y, w, l, back, 0.3, 0.5 + (0.27 * Math.Cos(a)), seat + (float)(0.42 * Ppm * Math.Sin(a)));
            }

            c.DrawColoredPolygon(points, Porcelain);
            c.DrawPolyline(points, Porcelain.Darkened(0.16f), 0.8f, true);
        }));

        pieces.Add((Depth(bowl), () =>
        {
            FlatEllipse(c, bx, by, rx * 0.8, ry * 0.8, 0, new Color(0, 0, 0, 0.12f));
            const int steps = 9;
            for (var i = 0; i < steps; i++)
            {
                var f = i / (float)(steps - 1);
                var grow = 0.62 + (0.38 * Math.Min(1, f * 1.6));
                FlatEllipse(c, bx, by, rx * grow, ry * grow, f * seat, Porcelain.Darkened(0.16f - (0.1f * f)));
            }

            FlatEllipse(c, bx, by, rx * 1.03, ry * 1.03, seat + 1, Colors.White);
            FlatEllipse(c, bx, by, rx * 0.62, ry * 0.64, seat + 1, new Color("#C9D5DC"));
            FlatEllipse(c, bx, by, rx * 0.34, ry * 0.36, seat + 0.5f, new Color("#A9CFE0"));
        }));

        if (accessible)
        {
            void Rail(double across, bool dropDown)
            {
                pieces.Add((IsoDraw.ToScreen(0, 0).Y + (Point(x, y, w, l, back, 0.55, across, 0) - IsoDraw.ToScreen(0, 0)).Y, () =>
                {
                    var high = 0.72f * Ppm;
                    var low = high - 4.5f;
                    Vector2 At(double from, float lift) => Point(x, y, w, l, back, from, across, lift);
                    if (dropDown)
                    {
                        c.DrawLine(At(0.02, low - 9), At(0.02, high + 3), Metal, 3f);
                        c.DrawPolyline(new[] { At(0.02, high), At(1.05, high), At(1.05, low), At(0.02, low) }, RailBlue, 2f, true);
                    }
                    else
                    {
                        c.DrawLine(At(0.15, high), At(1.0, high), RailBlue, 2.2f, true);
                        c.DrawCircle(At(0.15, high), 1.6f, RailBlue.Darkened(0.2f));
                        c.DrawCircle(At(1.0, high), 1.6f, RailBlue.Darkened(0.2f));
                    }
                }));
            }

            Rail(-0.22, false);
            Rail(1.2, true);

            // The red emergency pull cord, hanging from the ceiling beside the toilet, with its two triangles.
            pieces.Add((Point(x, y, w, l, back, 0.95, -0.42, 0).Y, () =>
            {
                var foot = Point(x, y, w, l, back, 0.95, -0.42, 0);
                c.DrawLine(foot + new Vector2(0, -2.3f * Ppm), foot + new Vector2(0, -0.2f * Ppm), Engaged, 1.2f);
                foreach (var at in new[] { 0.12f * Ppm, 0.9f * Ppm })
                {
                    c.DrawColoredPolygon(new[] { foot + new Vector2(-3, -at), foot + new Vector2(3, -at), foot + new Vector2(0, -at - 5) }, Engaged);
                }
            }));
        }

        foreach (var piece in pieces.OrderBy(p => p.Depth))
        {
            piece.Draw();
        }
    }

    /// <summary>A wall-hung basin with a rounded bowl and a chrome mixer tap, under a framed mirror with a soap dispenser beside it.</summary>
    private static void Basin(CanvasItem c, double x, double y, double w, double l, (int X, int Y) back)
    {
        var top = 0.85f * Ppm;
        var wallBehind = WallBehind(back);
        Vector2 At(double from, double across, float lift) => Point(x, y, w, l, back, from, across, lift);

        if (wallBehind)
        {
            // The mirror: a metal frame, the glass, and two streaks of light across it.
            Vector2 M(double u, float v) => At(-0.02, 0.06 + (0.88 * u), (1.05f * Ppm) + (v * 0.62f * Ppm));
            c.DrawColoredPolygon(new[] { M(0, 0), M(1, 0), M(1, 1), M(0, 1) }, Metal.Darkened(0.1f));
            Vector2 G(double u, float v) => M(0.05 + (0.9 * u), 0.05f + (0.9f * v));
            c.DrawColoredPolygon(new[] { G(0, 0), G(1, 0), G(1, 1), G(0, 1) }, new Color("#BFDDEC"));
            c.DrawColoredPolygon(new[] { G(0.15, 0), G(0.32, 0), G(0.62, 1), G(0.45, 1) }, new Color("#DDEEF6"));
            c.DrawColoredPolygon(new[] { G(0.42, 0), G(0.48, 0), G(0.78, 1), G(0.72, 1) }, new Color("#DDEEF6"));

            // The soap dispenser on the wall beside it.
            var soap = Part(x, y, w, l, back, -0.02, 0.1, 0.84, 0.98);
            Box(c, soap, 0.17f * Ppm, new Color("#F4F7F9"), top + (0.12f * Ppm));
            var drop = At(0.1, 0.91, top + (0.19f * Ppm));
            c.DrawLine(drop, drop + new Vector2(0, 2.2f), new Color("#26A69A"), 1.6f);
        }

        // The bottle trap and waste pipe under the bowl.
        var trap = At(0.3, 0.5, 0);
        c.DrawLine(trap + new Vector2(0, -top + 4), trap + new Vector2(0, -top + 11), Chrome, 1.6f);
        var pedestal = Part(x, y, w, l, back, 0.12, 0.5, 0.36, 0.64);
        Box(c, pedestal, top - (0.16f * Ppm), Porcelain.Darkened(0.08f), 0, shadow: true);

        var bowl = Part(x, y, w, l, back, 0.02, 1, 0.02, 0.98);
        var (bx, by) = (bowl.X + (bowl.W / 2), bowl.Y + (bowl.L / 2));
        const int steps = 6;
        for (var i = 0; i < steps; i++)
        {
            var f = i / (float)(steps - 1);
            var grow = 0.62 + (0.38 * f);
            FlatEllipse(c, bx, by, bowl.W / 2 * grow, bowl.L / 2 * grow, top - (0.17f * Ppm * (1 - f)), Porcelain.Darkened(0.14f - (0.1f * f)));
        }

        FlatEllipse(c, bx, by, bowl.W / 2, bowl.L / 2, top + 0.6f, Colors.White);
        var inner = Part(x, y, w, l, back, 0.3, 0.9, 0.16, 0.84);
        FlatEllipse(c, inner.X + (inner.W / 2), inner.Y + (inner.L / 2), inner.W / 2, inner.L / 2, top + 0.6f, new Color("#CBD8DF"));
        FlatEllipse(c, inner.X + (inner.W / 2), inner.Y + (inner.L / 2), 0.05, 0.05, top + 0.6f, new Color("#7B8B95"));

        // The mixer tap: a post and a spout reaching over the bowl.
        var post = At(0.14, 0.5, top);
        c.DrawLine(post, post + new Vector2(0, -5.5f), Chrome.Darkened(0.1f), 2f);
        c.DrawLine(post + new Vector2(0, -5.5f), At(0.42, 0.5, top + 4.2f), Chrome, 1.8f, true);
        c.DrawCircle(post + new Vector2(0, -6f), 1.3f, Chrome.Lightened(0.3f));
    }

    /// <summary>A hand dryer on the wall: a white case with a dark air slot underneath and a small green light.</summary>
    private static void Dryer(CanvasItem c, double x, double y, double w, double l, Color colour, (int X, int Y) back)
    {
        var body = Part(x, y, w, l, back, 0, 0.8, 0, 1);
        var (height, lift) = (0.3f * Ppm, 1.0f * Ppm);
        Box(c, body, height, colour.Lightened(0.35f), lift);
        foreach (var face in IsoFace.Of(body.X, body.Y, body.W, body.L, height, lift))
        {
            if (face.Side != IsoFace.SideOf((-back.X, -back.Y)))
            {
                continue;
            }

            face.Draw(c, (length, h) =>
            {
                Flat.Rect(c, 0, 0, length, h, colour.Lightened(0.3f));
                Flat.Rect(c, length * 0.12f, h - 2.2f, length * 0.76f, 2.2f, new Color("#37474F"));
                Flat.Rect(c, length * 0.2f, 1.4f, length * 0.6f, 0.8f, colour.Darkened(0.1f));
                c.DrawCircle(new Vector2(length * 0.8f, 3.6f), 0.9f, Vacant);
            });
        }
    }
}
