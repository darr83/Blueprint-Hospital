using System;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>Sides of a footprint on the grid: north is towards y = 0, west towards x = 0.</summary>
[Flags]
public enum Sides
{
    None = 0,
    North = 1,
    East = 2,
    South = 4,
    West = 8,
}

/// <summary>
/// Drawing helpers for simple isometric geometry: floor quads and extruded boxes.
/// Grid positions are in tiles; heights are in screen pixels (about 28 px per metre).
/// </summary>
public static class IsoDraw
{
    /// <summary>Screen pixels per metre of height, matching the 3 m outer walls.</summary>
    public const float PixelsPerMetre = 28f;

    private static double _centreX;
    private static double _centreY;

    /// <summary>How many quarter turns the view is rotated (0 to 3). Q and E turn it.</summary>
    public static int Rotation { get; private set; }

    private static int _floorStride;

    /// <summary>The floor being looked at (0 = ground). Page Up and Page Down change it.</summary>
    public static int ViewFloor { get; private set; }

    /// <summary>How far the floor in view is from the ground floor's slice of the grid, in tiles.</summary>
    public static double FloorShift => ViewFloor * _floorStride;

    /// <summary>The view turns about the middle of the site. <paramref name="floorStride"/> is the grid rows from one floor to the next.</summary>
    public static void SetSite(int width, int length, int floorStride = 0)
    {
        _centreX = width / 2.0;
        _centreY = length / 2.0;
        _floorStride = floorStride;
    }

    public static void SetViewFloor(int floor) => ViewFloor = Math.Max(0, floor);

    /// <summary>Which floor a grid position is on.</summary>
    public static int FloorOf(double y) => _floorStride <= 0 || y < _floorStride ? 0 : (int)(y / _floorStride);

    /// <summary>True if a grid position is on the floor in view, so it is drawn.</summary>
    public static bool OnViewFloor(double y) => FloorOf(y) == ViewFloor;

    /// <summary>Screen position of a ground-floor (site) grid point, whichever floor is in view: the grounds and the camera.</summary>
    public static Vector2 ToScreenGround(double x, double y)
    {
        var (rx, ry) = Rotate(x, y);
        return new((float)((rx - ry) * 32), (float)((rx + ry) * 16));
    }

    /// <summary>The ground-floor (site) grid point under a screen position.</summary>
    public static (double X, double Y) ScreenToGroundGrid(Vector2 screen)
    {
        var (a, b) = (screen.X / 32.0, screen.Y / 16.0);
        return Unrotate((a + b) / 2, (b - a) / 2);
    }

    public static void SetRotation(int quarterTurns) => Rotation = ((quarterTurns % 4) + 4) % 4;

    /// <summary>A grid point turned to the current view.</summary>
    public static (double X, double Y) Rotate(double x, double y)
    {
        var (dx, dy) = (x - _centreX, y - _centreY);
        return Rotation switch
        {
            1 => (_centreX - dy, _centreY + dx),
            2 => (_centreX - dx, _centreY - dy),
            3 => (_centreX + dy, _centreY - dx),
            _ => (x, y),
        };
    }

    private static (double X, double Y) Unrotate(double x, double y)
    {
        var (dx, dy) = (x - _centreX, y - _centreY);
        return Rotation switch
        {
            1 => (_centreX + dy, _centreY - dx),
            2 => (_centreX - dx, _centreY - dy),
            3 => (_centreX - dy, _centreY + dx),
            _ => (x, y),
        };
    }

    /// <summary>Screen position of a grid point in the current view. Points on the floor in view land over the site.</summary>
    public static Vector2 ToScreen(double x, double y) => ToScreenGround(x, y - FloorShift);

    /// <summary>The grid point under a screen position in the current view, on the floor in view.</summary>
    public static (double X, double Y) ScreenToGrid(Vector2 screen)
    {
        var (x, y) = ScreenToGroundGrid(screen);
        return (x, y + FloorShift);
    }

    public static GridCoord ScreenToTile(Vector2 screen)
    {
        var (x, y) = ScreenToGrid(screen);
        return new GridCoord((int)Math.Floor(x), (int)Math.Floor(y));
    }

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

    /// <summary>
    /// A box that is one piece of something larger (a wall run, a counter). Faces listed in
    /// <paramref name="joined"/> touch a neighbouring piece, so they are not drawn: pieces then join
    /// with no seams from every view angle. Optional skirting is a darker band along the foot of
    /// each face, and the rim is a fine line along the outer top edges.
    /// </summary>
    public static void Prism(CanvasItem canvas, double x, double y, double w, double l, float height, Color top, Color face, Sides joined = Sides.None, float lift = 0, Color? skirting = null, float skirtingHeight = 4f, Color? rim = null)
    {
        var corners = new[] { ToScreen(x, y), ToScreen(x + w, y), ToScreen(x + w, y + l), ToScreen(x, y + l) };
        var up = new Vector2(0, -height - lift);
        var down = new Vector2(0, -lift);
        var centre = (corners[0] + corners[1] + corners[2] + corners[3]) / 4;
        for (var i = 0; i < 4; i++)
        {
            if ((joined & EdgeSide(i)) != 0)
            {
                continue;
            }

            var a = corners[i];
            var b = corners[(i + 1) % 4];
            var mid = (a + b) / 2;
            if (mid.Y <= centre.Y || height <= 0)
            {
                continue;
            }

            // Light from the upper left: the left-facing side is lighter than the right.
            var shade = mid.X < centre.X ? 0f : 0.1f;
            canvas.DrawColoredPolygon(new[] { a + down, b + down, b + up, a + up }, face.Darkened(shade));
            if (skirting is { } skirt)
            {
                var s = new Vector2(0, -Math.Min(skirtingHeight, height));
                canvas.DrawColoredPolygon(new[] { a + down, b + down, b + down + s, a + down + s }, skirt.Darkened(shade));
            }
        }

        canvas.DrawColoredPolygon(Array.ConvertAll(corners, c => c + up), top);
        if (rim is { } line)
        {
            for (var i = 0; i < 4; i++)
            {
                if ((joined & EdgeSide(i)) == 0)
                {
                    canvas.DrawLine(corners[i] + up, corners[(i + 1) % 4] + up, line, 1f, true);
                }
            }
        }
    }

    /// <summary>The visible side faces of a box (as floor-to-top quads), for drawing details such as drawers or doors on them.</summary>
    public static (Vector2 A, Vector2 B, Sides Side, bool Left)[] VisibleFaces(double x, double y, double w, double l)
    {
        var corners = new[] { ToScreen(x, y), ToScreen(x + w, y), ToScreen(x + w, y + l), ToScreen(x, y + l) };
        var centre = (corners[0] + corners[1] + corners[2] + corners[3]) / 4;
        var faces = new System.Collections.Generic.List<(Vector2, Vector2, Sides, bool)>(2);
        for (var i = 0; i < 4; i++)
        {
            var (a, b) = (corners[i], corners[(i + 1) % 4]);
            var mid = (a + b) / 2;
            if (mid.Y > centre.Y)
            {
                // Always left to right on screen, so details line up the same way at every angle.
                faces.Add(a.X <= b.X ? (a, b, EdgeSide(i), mid.X < centre.X) : (b, a, EdgeSide(i), mid.X < centre.X));
            }
        }

        return faces.ToArray();
    }

    private static Sides EdgeSide(int edge) => edge switch
    {
        0 => Sides.North,
        1 => Sides.East,
        2 => Sides.South,
        _ => Sides.West,
    };

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
