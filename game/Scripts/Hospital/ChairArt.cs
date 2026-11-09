using System;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Hospital seating: an upholstered seat and back on a grey metal frame, with arms. A chair faces one of
/// the four grid directions (the way someone sitting on it looks) and is drawn correctly at every view
/// angle. It is drawn in two parts so a person sitting on it sorts between them: the frame and seat go
/// behind the sitter, and the back goes in front of them when the chair faces away from the viewer.
/// </summary>
public static class ChairArt
{
    private static readonly Color Frame = new("#5E6770");
    private static readonly Color FrameLight = new("#8A949D");

    /// <summary>Seat height off the floor, in pixels (about 0.45 m).</summary>
    public const float SeatTop = 14f;

    /// <summary>Half the seat's width across and depth front to back, in tiles.</summary>
    private const double HalfWidth = 0.42;
    private const double HalfDepth = 0.36;

    /// <summary>
    /// How far a chair stands forward of the middle of its tile, in tiles, so a chair with its back to a wall
    /// stands just clear of the wall instead of sinking into it.
    /// </summary>
    public const double Inset = 0.1;

    /// <summary>The middle of the chair's seat, in grid units.</summary>
    public static (double X, double Y) Centre(GridCoord tile, WallSide facing)
    {
        var (fx, fy) = Direction(facing);
        return (tile.X + 0.5 + (fx * Inset), tile.Y + 0.5 + (fy * Inset));
    }

    /// <summary>The way someone sitting on a chair facing <paramref name="facing"/> looks, as a grid step.</summary>
    public static (double X, double Y) Direction(WallSide facing) => facing switch
    {
        WallSide.North => (0, -1),
        WallSide.East => (1, 0),
        WallSide.West => (-1, 0),
        _ => (0, 1),
    };

    /// <summary>The facing as a step on screen in the current view.</summary>
    public static Vector2 ScreenDirection(WallSide facing)
    {
        var (fx, fy) = Direction(facing);
        return IsoDraw.ToScreen(fx, fy) - IsoDraw.ToScreen(0, 0);
    }

    /// <summary>True when someone sitting on the chair has their back to the viewer.</summary>
    public static bool FacesAway(WallSide facing) => ScreenDirection(facing).Y < 0;

    /// <summary>-1 when the sitter looks to the left of the screen, +1 to the right.</summary>
    public static float Lean(WallSide facing) => Math.Sign(ScreenDirection(facing).X);

    /// <summary>Where the chair's frame and seat sort: just behind the person sitting on it.</summary>
    public static Vector2 SeatAnchor(GridCoord tile, WallSide facing)
    {
        var (cx, cy) = Centre(tile, facing);
        return IsoDraw.ToScreen(cx, cy) - new Vector2(0, 1);
    }

    /// <summary>Where the chair's back sorts: the middle of its back edge.</summary>
    public static Vector2 BackAnchor(GridCoord tile, WallSide facing)
    {
        var (fx, fy) = Direction(facing);
        var (cx, cy) = Centre(tile, facing);
        return IsoDraw.ToScreen(cx - (fx * HalfDepth), cy - (fy * HalfDepth));
    }

    /// <summary>A rectangle in the chair's own frame (u across, v from back to front) as a grid footprint.</summary>
    private static (double X, double Y, double W, double L) Local(double cx, double cy, WallSide facing, double u0, double v0, double u1, double v1)
    {
        var (fx, fy) = Direction(facing);
        var (ax, ay) = (-fy, fx);
        var x0 = cx + (u0 * ax) + (v0 * fx);
        var y0 = cy + (u0 * ay) + (v0 * fy);
        var x1 = cx + (u1 * ax) + (v1 * fx);
        var y1 = cy + (u1 * ay) + (v1 * fy);
        return (Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));
    }

    private static void Part(CanvasItem c, (double X, double Y, double W, double L) r, float height, Color colour, float lift, bool shadow = false) =>
        IsoDraw.Box(c, r.X, r.Y, r.W, r.L, height, colour, lift, shadow: shadow);

    /// <summary>The legs, seat and arms: everything a sitter sits in front of.</summary>
    public static void Seat(CanvasItem c, GridCoord tile, WallSide facing, Color colour)
    {
        var (cx, cy) = Centre(tile, facing);
        var (hw, hd) = (HalfWidth, HalfDepth);

        // Soft shadow on the floor.
        var shadow = Local(cx, cy, facing, -hw, -hd, hw, hd);
        IsoDraw.FloorQuad(c, shadow.X + 0.06, shadow.Y + 0.06, shadow.W, shadow.L, new Color(0, 0, 0, 0.13f));

        // Four legs and the rail between them, back ones first so the front ones stand over them.
        var legs = new[] { (-hw + 0.06, -hd + 0.06), (hw - 0.06, -hd + 0.06), (-hw + 0.06, hd - 0.06), (hw - 0.06, hd - 0.06) };
        Array.Sort(legs, (a, b) => Depth(cx, cy, facing, a).CompareTo(Depth(cx, cy, facing, b)));
        foreach (var (u, v) in legs)
        {
            Part(c, Local(cx, cy, facing, u - 0.035, v - 0.035, u + 0.035, v + 0.035), SeatTop - 3, Frame, 0);
        }

        // The seat: a frame rail under a cushion with a lighter top and a piped front edge.
        Part(c, Local(cx, cy, facing, -hw, -hd, hw, hd), 1.5f, Frame, SeatTop - 3.5f);
        Part(c, Local(cx, cy, facing, -hw + 0.03, -hd + 0.02, hw - 0.03, hd), 3f, colour, SeatTop - 2f);

        // Arms: a post at the front and a padded rest on top, on both sides.
        foreach (var u in new[] { -hw, hw - 0.07 })
        {
            Part(c, Local(cx, cy, facing, u, hd - 0.14, u + 0.07, hd - 0.07), 6f, FrameLight, SeatTop + 1);
            Part(c, Local(cx, cy, facing, u - 0.01, -hd + 0.02, u + 0.08, hd - 0.04), 1.8f, Frame.Darkened(0.1f), SeatTop + 6.5f);
        }
    }

    /// <summary>The back of the chair: two posts and an upholstered pad.</summary>
    public static void Back(CanvasItem c, GridCoord tile, WallSide facing, Color colour)
    {
        var (cx, cy) = Centre(tile, facing);
        var (hw, hd) = (HalfWidth, HalfDepth);
        foreach (var u in new[] { -hw + 0.08, hw - 0.14 })
        {
            Part(c, Local(cx, cy, facing, u, -hd, u + 0.06, -hd + 0.06), 6f, Frame, SeatTop);
        }

        Part(c, Local(cx, cy, facing, -hw + 0.02, -hd, hw - 0.02, -hd + 0.11), 11f, colour.Darkened(0.06f), SeatTop + 3);

        // A grey cap along the top of the back.
        Part(c, Local(cx, cy, facing, -hw + 0.02, -hd - 0.005, hw - 0.02, -hd + 0.115), 1.2f, FrameLight, SeatTop + 14);
    }

    /// <summary>The whole chair, back to front, for previews and the art gallery.</summary>
    public static void Whole(CanvasItem c, GridCoord tile, WallSide facing, Color colour)
    {
        if (FacesAway(facing))
        {
            Seat(c, tile, facing, colour);
            Back(c, tile, facing, colour);
        }
        else
        {
            Back(c, tile, facing, colour);
            Seat(c, tile, facing, colour);
        }
    }

    private static float Depth(double cx, double cy, WallSide facing, (double U, double V) p)
    {
        var r = Local(cx, cy, facing, p.U, p.V, p.U, p.V);
        return IsoDraw.ToScreen(r.X, r.Y).Y;
    }
}
