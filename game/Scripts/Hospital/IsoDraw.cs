using System;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Drawing helpers for simple isometric geometry: floor quads and extruded boxes.
/// Grid positions are in tiles; heights are in screen pixels (about 28 px per metre).
/// </summary>
public static class IsoDraw
{
    /// <summary>Screen pixels per metre of height, matching the 3 m outer walls.</summary>
    public const float PixelsPerMetre = 28f;

    public static Vector2 ToScreen(double x, double y) => new((float)((x - y) * 32), (float)((x + y) * 16));

    public static void FloorQuad(CanvasItem canvas, double x, double y, double w, double l, Color colour) =>
        canvas.DrawColoredPolygon(new[] { ToScreen(x, y), ToScreen(x + w, y), ToScreen(x + w, y + l), ToScreen(x, y + l) }, colour);

    public static void FloorOutline(CanvasItem canvas, double x, double y, double w, double l, Color colour, float width = 1.5f)
    {
        var p = new[] { ToScreen(x, y), ToScreen(x + w, y), ToScreen(x + w, y + l), ToScreen(x, y + l), ToScreen(x, y) };
        canvas.DrawPolyline(p, colour, width, true);
    }

    /// <summary>An axis-aligned box sitting on the floor.</summary>
    public static void Box(CanvasItem canvas, double x, double y, double w, double l, float height, Color top, float lift = 0, Color? sideColour = null, bool shadow = true)
    {
        var corners = new[] { ToScreen(x, y), ToScreen(x + w, y), ToScreen(x + w, y + l), ToScreen(x, y + l) };
        BoxFromCorners(canvas, corners, height, top, lift, sideColour, shadow);
    }

    /// <summary>
    /// A box whose floor footprint is centred on (cx, cy), <paramref name="length"/> tiles along
    /// <paramref name="dir"/> and <paramref name="width"/> tiles across it. Used for vehicles.
    /// </summary>
    public static Vector2[] OrientedBox(CanvasItem canvas, double cx, double cy, (double X, double Y) dir, double length, double width, float height, Color top, float lift = 0)
    {
        var len = Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y));
        var (dx, dy) = len < 1e-6 ? (1.0, 0.0) : (dir.X / len, dir.Y / len);
        var (nx, ny) = (-dy, dx);
        var hl = length / 2;
        var hw = width / 2;
        var corners = new[]
        {
            ToScreen(cx + (dx * hl) + (nx * hw), cy + (dy * hl) + (ny * hw)),
            ToScreen(cx + (dx * hl) - (nx * hw), cy + (dy * hl) - (ny * hw)),
            ToScreen(cx - (dx * hl) - (nx * hw), cy - (dy * hl) - (ny * hw)),
            ToScreen(cx - (dx * hl) + (nx * hw), cy - (dy * hl) + (ny * hw)),
        };
        BoxFromCorners(canvas, corners, height, top, lift, null, true);
        return corners;
    }

    private static void BoxFromCorners(CanvasItem canvas, Vector2[] corners, float height, Color top, float lift, Color? sideColour, bool shadow)
    {
        var up = new Vector2(0, -height - lift);
        var down = new Vector2(0, -lift);
        var centre = (corners[0] + corners[1] + corners[2] + corners[3]) / 4;

        // Shadow on the floor.
        if (lift <= 0 && shadow && height > 0)
        {
            canvas.DrawColoredPolygon(Array.ConvertAll(corners, c => c + ((c - centre) * 0.08f) + new Vector2(2, 2)), new Color(0, 0, 0, 0.12f));
        }

        // Side faces that face the viewer (whose bottom edge is lower on screen than the box centre).
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            var mid = (a + b) / 2;
            if (mid.Y <= centre.Y)
            {
                continue;
            }

            if (height <= 0)
            {
                continue;
            }

            // Light comes from the upper left: the left-facing side is lighter than the right.
            var side = sideColour ?? top;
            var shade = sideColour is null ? (mid.X < centre.X ? 0.18f : 0.32f) : (mid.X < centre.X ? 0f : 0.1f);
            canvas.DrawColoredPolygon(new[] { a + down, b + down, b + up, a + up }, side.Darkened(shade));
        }

        canvas.DrawColoredPolygon(Array.ConvertAll(corners, c => c + up), top);
    }

    /// <summary>An ellipse lying on the floor (2:1), for shadows and selection rings.</summary>
    public static Vector2[] FloorEllipse(Vector2 centre, float radius, int segments = 20)
    {
        var points = new Vector2[segments];
        for (var i = 0; i < segments; i++)
        {
            var a = i * Mathf.Tau / segments;
            points[i] = centre + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * 0.5f);
        }

        return points;
    }
}
