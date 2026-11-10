using System;
using System.Linq;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Drawing "flat" on an upright isometric face: inside <see cref="On"/> ordinary 2D drawing
/// (rectangles, circles, text) is laid onto the face, x running along it and y straight down, so
/// signs, screens and machine fronts can be designed like a flat picture and still sit in the world.
/// </summary>
public static class Flat
{
    /// <summary>Set by the art gallery, which draws scaled cells: the transform to return to.</summary>
    public static Transform2D? Override { get; set; }

    private static Font? _bold;

    public static Font Bold => _bold ??= new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.6f };

    private static Transform2D Base(CanvasItem c) => Override ?? (c is Node2D node ? new Transform2D(0, -node.Position) : Transform2D.Identity);

    /// <summary>Runs <paramref name="draw"/> with (0, 0) at <paramref name="origin"/>, x along <paramref name="along"/> and y down the screen.</summary>
    public static void On(CanvasItem c, Vector2 origin, Vector2 along, Action draw)
    {
        var b = Base(c);
        c.DrawSetTransformMatrix(b * new Transform2D(along.Normalized(), Vector2.Down, origin));
        draw();
        c.DrawSetTransformMatrix(b);
    }

    public static void Rect(CanvasItem c, float x, float y, float w, float h, Color colour)
    {
        if (w > 0.01f && h > 0.01f)
        {
            c.DrawRect(new Rect2(x, y, w, h), colour);
        }
    }

    public static void Round(CanvasItem c, float x, float y, float w, float h, float r, Color colour)
    {
        if (w <= 0.01f || h <= 0.01f)
        {
            return;
        }

        r = Math.Min(r, (Math.Min(w, h) / 2) - 0.05f);
        if (r < 0.5f)
        {
            c.DrawRect(new Rect2(x, y, w, h), colour);
        }
        else
        {
            Figures.RoundedRect(c, new Rect2(x, y, w, h), r, colour);
        }
    }

    /// <summary>Text centred on <paramref name="cx"/> with its baseline at <paramref name="baseline"/>.</summary>
    public static void Text(CanvasItem c, string text, float cx, float baseline, int size, Color colour, bool bold = true)
    {
        var font = bold ? Bold : ThemeDB.FallbackFont;
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        c.DrawString(font, new Vector2(cx - (width / 2), baseline), text, HorizontalAlignment.Left, -1, size, colour);
    }

    public static void TextLeft(CanvasItem c, string text, float x, float baseline, int size, Color colour, bool bold = true) =>
        c.DrawString(bold ? Bold : ThemeDB.FallbackFont, new Vector2(x, baseline), text, HorizontalAlignment.Left, -1, size, colour);

    public static float Width(string text, int size, bool bold = true) =>
        (bold ? Bold : ThemeDB.FallbackFont).GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    /// <summary>An arrow in a square of side <paramref name="s"/> centred on (x, y), pointing along <paramref name="angle"/> (0 = right, quarter turns clockwise).</summary>
    public static void Arrow(CanvasItem c, float x, float y, float s, int quarterTurns, Color colour)
    {
        var a = quarterTurns * Mathf.Pi / 2;
        Vector2 P(float px, float py) => new Vector2(x, y) + new Vector2(px, py).Rotated(a) * s;
        c.DrawColoredPolygon(new[] { P(0.5f, 0), P(0.02f, -0.46f), P(0.02f, -0.16f), P(-0.5f, -0.16f), P(-0.5f, 0.16f), P(0.02f, 0.16f), P(0.02f, 0.46f) }, colour);
    }
}

/// <summary>One visible upright face of a box: its bottom edge left to right on screen, and its height.</summary>
public readonly record struct IsoFace(Vector2 A, Vector2 B, float Height, Sides Side, bool Left, float Lift = 0)
{
    public float Length => (B - A).Length();

    /// <summary>A point u (0 to 1) along the face and v pixels up from its foot.</summary>
    public Vector2 P(float u, float v) => A.Lerp(B, u) + new Vector2(0, -v - Lift);

    /// <summary>Light comes from the upper left: faces turned to the right are a little darker.</summary>
    public Color Lit(Color colour) => Left ? colour : colour.Darkened(0.1f);

    public void Quad(CanvasItem c, float u0, float v0, float u1, float v1, Color colour) =>
        c.DrawColoredPolygon(new[] { P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1) }, Lit(colour));

    public void Line(CanvasItem c, float u0, float v0, float u1, float v1, Color colour, float width = 1f) =>
        c.DrawLine(P(u0, v0), P(u1, v1), Lit(colour), width, true);

    /// <summary>
    /// Draws flat on the face: <paramref name="draw"/> gets its length and height in pixels, with
    /// (0, 0) at the top left. Faces turned away from the light are dimmed afterwards.
    /// </summary>
    public void Draw(CanvasItem c, Action<float, float> draw, bool shade = true)
    {
        var (length, height, left) = (Length, Height, Left);
        Flat.On(c, A + new Vector2(0, -Height - Lift), B - A, () =>
        {
            draw(length, height);
            if (!left && shade)
            {
                c.DrawRect(new Rect2(0, 0, length, height), new Color(0, 0, 0, 0.1f));
            }
        });
    }

    /// <summary>The faces of a box that are turned to the viewer.</summary>
    public static IsoFace[] Of(double x, double y, double w, double l, float height, float lift = 0) =>
        IsoDraw.VisibleFaces(x, y, w, l).Select(f => new IsoFace(f.A, f.B, height, f.Side, f.Left, lift)).ToArray();

    /// <summary>The side of a footprint that looks along a grid direction.</summary>
    public static Sides SideOf((int X, int Y) direction) => direction switch
    {
        (0, 1) => Sides.South,
        (0, -1) => Sides.North,
        (1, 0) => Sides.East,
        _ => Sides.West,
    };

    public static Sides Opposite(Sides side) => side switch
    {
        Sides.North => Sides.South,
        Sides.South => Sides.North,
        Sides.East => Sides.West,
        Sides.West => Sides.East,
        _ => Sides.None,
    };
}
