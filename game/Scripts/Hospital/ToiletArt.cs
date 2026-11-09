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

    private static void Cubicle(CanvasItem c, double x, double y, double w, double l, Color colour, (int X, int Y) front, bool inUse)
    {
        var back = (-front.X, -front.Y);
        var panel = 1.8f * Ppm;
        var gap = 0.15f * Ppm;
        const double thick = 0.06;
        var depthTiles = front.X == 0 ? l : w;
        var acrossTiles = front.X == 0 ? w : l;
        var t = thick / depthTiles;
        var s = thick / acrossTiles;

        // Pieces sorted back to front for the current view, so whatever is nearer covers what is behind.
        var pieces = new List<((double X, double Y, double W, double L) Rect, Action Draw)>();
        var pan = Part(x, y, w, l, back, 0.04, 0.5, 0.25, 0.75);
        pieces.Add((pan, () => Toilet(c, pan.X, pan.Y, pan.W, pan.L, back, accessible: false)));
        var left = Part(x, y, w, l, back, 0, 1, 0, s);
        var right = Part(x, y, w, l, back, 0, 1, 1 - s, 1);
        pieces.Add((left, () => Box(c, left, panel, colour, gap)));
        pieces.Add((right, () => Box(c, right, panel, colour, gap)));

        // The front panel and door, with the latch sign at handle height on both faces.
        var door = Part(x, y, w, l, back, 1 - t, 1, s, 1 - s);
        pieces.Add((door, () =>
        {
            Box(c, door, panel, colour.Lightened(0.08f), gap);
            var latch = Part(door.X, door.Y, door.W, door.L, back, 0, 1, 0.78, 0.9);
            IsoDraw.Box(c, latch.X - 0.01, latch.Y - 0.01, latch.W + 0.02, latch.L + 0.02, 0.08f * Ppm, inUse ? Engaged : Vacant, lift: 0.95f * Ppm, shadow: false);
        }));

        foreach (var piece in pieces.OrderBy(p => Depth(p.Rect)))
        {
            piece.Draw();
        }

        // A thin top rail ties the partitions together.
        var rail = Part(x, y, w, l, back, 0.97, 1, 0, 1);
        Box(c, rail, 2, colour.Darkened(0.2f), panel + gap);
    }

    private static void Toilet(CanvasItem c, double x, double y, double w, double l, (int X, int Y) back, bool accessible)
    {
        var bowlHeight = 0.42f * Ppm;
        var cistern = Part(x, y, w, l, back, 0, 0.28, 0.08, 0.92);
        var bowl = Part(x, y, w, l, back, 0.22, 1, 0.15, 0.85);
        var pedestal = Part(x, y, w, l, back, 0.3, 0.85, 0.3, 0.7);

        if (accessible)
        {
            // Grab rails: a fixed rail along the wall beside it and a drop-down rail on the open side.
            var side = Part(x, y, w, l, back, 0.1, 1.05, -0.2, -0.12);
            var open = Part(x, y, w, l, back, 0.1, 1.05, 1.12, 1.2);
            Box(c, side, 3, Chrome, 0.75f * Ppm);
            Box(c, open, 3, Chrome, 0.75f * Ppm);
        }

        Box(c, pedestal, bowlHeight * 0.7f, Porcelain.Darkened(0.05f), 0, shadow: true);
        Box(c, bowl, 0.08f * Ppm, Porcelain, bowlHeight - (0.08f * Ppm));
        var centre = Part(x, y, w, l, back, 0.35, 0.95, 0.25, 0.75);
        FlatEllipse(c, centre.X + (centre.W / 2), centre.Y + (centre.L / 2), centre.W / 2, centre.L / 2, bowlHeight + 1, new Color("#ECEFF1"));
        FlatEllipse(c, centre.X + (centre.W / 2), centre.Y + (centre.L / 2), centre.W * 0.32, centre.L * 0.32, bowlHeight + 1.5f, new Color("#90A4AE"));
        Box(c, cistern, 0.82f * Ppm, Porcelain, 0, shadow: true);

        // The flush button on top.
        var flush = Part(x, y, w, l, back, 0.08, 0.2, 0.4, 0.6);
        Box(c, flush, 1, Chrome, 0.82f * Ppm);

        if (accessible)
        {
            // The red emergency pull cord, hanging from the ceiling beside the toilet, with its triangle near the floor.
            var cord = Part(x, y, w, l, back, 0.9, 1.0, -0.35, -0.28);
            var foot = IsoDraw.ToScreen(cord.X + (cord.W / 2), cord.Y + (cord.L / 2));
            c.DrawLine(foot + new Vector2(0, -2.3f * Ppm), foot + new Vector2(0, -0.25f * Ppm), Engaged, 1.2f);
            c.DrawColoredPolygon(new[] { foot + new Vector2(-3, -0.12f * Ppm), foot + new Vector2(3, -0.12f * Ppm), foot + new Vector2(0, -0.28f * Ppm) }, Engaged);
        }
    }

    private static void Basin(CanvasItem c, double x, double y, double w, double l, (int X, int Y) back)
    {
        var top = 0.85f * Ppm;

        // A mirror on the wall above it.
        var mirror = Part(x, y, w, l, back, -0.12, -0.04, 0.05, 0.95);
        Box(c, mirror, 0.6f * Ppm, new Color("#B3D4E6"), 1.05f * Ppm);

        var pedestal = Part(x, y, w, l, back, 0.05, 0.45, 0.35, 0.65);
        Box(c, pedestal, top - (0.18f * Ppm), Porcelain.Darkened(0.06f), 0, shadow: true);
        var bowl = Part(x, y, w, l, back, 0, 1, 0, 1);
        Box(c, bowl, 0.18f * Ppm, Porcelain, top - (0.18f * Ppm));
        var sink = Part(x, y, w, l, back, 0.3, 0.88, 0.18, 0.82);
        FlatEllipse(c, sink.X + (sink.W / 2), sink.Y + (sink.L / 2), sink.W / 2, sink.L / 2, top + 0.5f, new Color("#CFD8DC"));
        var tap = Part(x, y, w, l, back, 0.05, 0.3, 0.44, 0.56);
        Box(c, tap, 0.1f * Ppm, Chrome, top);

        // A soap dispenser on the wall beside the mirror.
        var soap = Part(x, y, w, l, back, -0.02, 0.1, 0.82, 0.98);
        Box(c, soap, 0.16f * Ppm, new Color("#E0F2F1"), top + (0.15f * Ppm));
    }

    private static void Dryer(CanvasItem c, double x, double y, double w, double l, Color colour, (int X, int Y) back)
    {
        var body = Part(x, y, w, l, back, 0, 0.8, 0, 1);
        Box(c, body, 0.28f * Ppm, colour, 1.05f * Ppm);
        var nozzle = Part(x, y, w, l, back, 0.5, 0.75, 0.35, 0.65);
        Box(c, nozzle, 0.05f * Ppm, new Color("#37474F"), 1.0f * Ppm);
    }
}
