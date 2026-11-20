using System;
using System.Collections.Generic;
using BlueprintHospital.Hospital;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>
/// The pen every top-down drawing uses. Sizes and positions are given in tiles (a tile is half a
/// metre) and land on the canvas at <see cref="IsoDraw.PlanTile"/> pixels per tile, so the art
/// lines up with the cursor, routes and build outlines. Shapes are rounded rectangles, ellipses
/// and lines with a tinted outline and a soft shadow that always falls down and to the right of
/// the screen, whichever way the view is turned.
/// </summary>
public sealed class Pen
{
    /// <summary>Canvas pixels per tile.</summary>
    public const float T = IsoDraw.PlanTile;

    /// <summary>Outlines are the fill colour mixed this far towards ink.</summary>
    private const float OutlineTint = 0.42f;

    private static readonly Color Ink = new("#101820");
    private static readonly Color ShadowInk = new("#0B1620");

    private readonly CanvasItem _c;
    private readonly Rid _rid;
    private readonly Stack<Transform2D> _stack = new();
    private Transform2D _m = Transform2D.Identity;
    private readonly int _quarterTurns;
    private bool _inShadow;

    /// <summary>The default outline width, in tiles.</summary>
    public const double Hair = 0.035;

    public Pen(CanvasItem canvas, bool shadows = true, bool turned = true)
    {
        _c = canvas;
        _rid = canvas.GetCanvasItem();
        _quarterTurns = turned ? IsoDraw.Rotation : 0;
        Shadows = shadows;
    }

    /// <summary>
    /// A pen for a canvas that is not one of the view's layers (the build tool's previews): it carries
    /// the turn of the view and the floor in view itself, so the art still goes down in grid tiles.
    /// </summary>
    public static Pen OnMap(CanvasItem canvas)
    {
        var pen = new Pen(canvas, shadows: false, turned: false);
        pen._m = GridFrame(IsoDraw.ToScreen);
        canvas.DrawSetTransformMatrix(pen._m);
        return pen;
    }

    /// <summary>Puts the canvas back to drawing in its own coordinates, after a pen made with <see cref="OnMap"/>.</summary>
    public void Done() => _c.DrawSetTransformMatrix(Transform2D.Identity);

    /// <summary>The transform that puts art drawn in grid tiles (at <see cref="T"/> pixels a tile) where <paramref name="project"/> says the grid is.</summary>
    public static Transform2D GridFrame(Func<double, double, Vector2> project)
    {
        var origin = project(0, 0);
        return new Transform2D((project(1, 0) - origin) / T, (project(0, 1) - origin) / T, origin);
    }

    /// <summary>False when shadows are switched off in the settings: nothing casts one.</summary>
    public bool Shadows { get; }

    /// <summary>How strong shadows are (0 to 1).</summary>
    public float ShadowStrength { get; set; } = 0.2f;

    /// <summary>Everything drawn is multiplied by this alpha (ghosts in build previews).</summary>
    public float Alpha { get; set; } = 1f;

    public CanvasItem Canvas => _c;

    // ------------------------------------------------------------------ colours

    public static Color Dark(Color c, float t) => new(c.Lerp(Ink, t), c.A);

    public static Color Light(Color c, float t) => new(c.Lerp(Colors.White, t), c.A);

    public static Color A(Color c, float alpha) => new(c, alpha);

    public static Color A(string hex, float alpha) => new(new Color(hex), alpha);

    /// <summary>The outline that goes with a fill.</summary>
    public static Color Outline(Color fill) => Dark(fill, OutlineTint);

    private Color Tone(Color c) => Alpha >= 1f ? c : new Color(c, c.A * Alpha);

    // ------------------------------------------------------------------ frames

    /// <summary>Draws in a frame whose origin is (cx, cy), turned by <paramref name="angle"/> radians, until <see cref="Pop"/>.</summary>
    public void Push(double cx, double cy, double angle = 0, double scale = 1)
    {
        _stack.Push(_m);
        var local = new Transform2D((float)angle, new Vector2((float)scale, (float)scale), 0, new Vector2((float)cx * T, (float)cy * T));
        _m *= local;
        _c.DrawSetTransformMatrix(_m);
    }

    public void Pop()
    {
        _m = _stack.Pop();
        _c.DrawSetTransformMatrix(_m);
    }

    /// <summary>Runs <paramref name="draw"/> in a frame at (cx, cy) turned by <paramref name="angle"/>.</summary>
    public void In(double cx, double cy, double angle, Action draw, double scale = 1)
    {
        Push(cx, cy, angle, scale);
        draw();
        Pop();
    }

    /// <summary>The angle that turns a frame so its -y axis points along (dx, dy): the way people and vehicles face.</summary>
    public static double Facing(double dx, double dy) => Math.Atan2(dx, -dy);

    /// <summary>A step on screen (in tiles), as a step in the current frame.</summary>
    private Vector2 ScreenStep(double dx, double dy)
    {
        var v = new Vector2((float)dx, (float)dy);
        v = _quarterTurns switch
        {
            1 => new Vector2(v.Y, -v.X),
            2 => -v,
            3 => new Vector2(-v.Y, v.X),
            _ => v,
        };
        return _m.BasisXformInv(v) / Math.Max(0.0001f, _m.X.LengthSquared());
    }

    /// <summary>The angle that keeps something upright on screen however the view and the frame are turned.</summary>
    private float UprightAngle() => (-_quarterTurns * Mathf.Pi / 2) - _m.Rotation;

    // ------------------------------------------------------------------ shadows

    /// <summary>
    /// Draws a shadow: <paramref name="shape"/> is called with the shadow colour, moved a little down
    /// and to the right on screen. Two passes, the nearer one fainter, soften its edge.
    /// </summary>
    public void Shadow(Action<Color> shape, double dx = 0.1, double dy = 0.14, float strength = 1f)
    {
        if (!Shadows || _inShadow)
        {
            return;
        }

        _inShadow = true;
        var alpha = ShadowStrength * strength;
        var step = ScreenStep(dx, dy);
        foreach (var (k, a) in new[] { (1.25f, 0.4f), (0.8f, 0.6f) })
        {
            _stack.Push(_m);
            _m *= new Transform2D(0, step * k * T);
            _c.DrawSetTransformMatrix(_m);
            shape(new Color(ShadowInk, alpha * a));
            _m = _stack.Pop();
        }

        _c.DrawSetTransformMatrix(_m);
        _inShadow = false;
    }

    // ------------------------------------------------------------------ shapes

    private void Fill(Vector2[] points, Color colour)
    {
        if (points.Length < 3 || colour.A <= 0.003f)
        {
            return;
        }

        var indices = new int[(points.Length - 2) * 3];
        for (var i = 0; i < points.Length - 2; i++)
        {
            indices[i * 3] = 0;
            indices[(i * 3) + 1] = i + 1;
            indices[(i * 3) + 2] = i + 2;
        }

        RenderingServer.CanvasItemAddTriangleArray(_rid, indices, points, new[] { Tone(colour) });
    }

    private void Stroke(Vector2[] points, Color colour, double width, bool closed)
    {
        if (points.Length < 2 || colour.A <= 0.003f || _inShadow)
        {
            return;
        }

        if (closed)
        {
            var loop = new Vector2[points.Length + 2];
            points.CopyTo(loop, 0);
            loop[^2] = points[0];
            loop[^1] = points[1];
            points = loop;
        }

        _c.DrawPolyline(points, Tone(colour), (float)width * T);
    }

    private static Vector2[] RoundedPoints(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        if (r < 0.6f)
        {
            return new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) };
        }

        var steps = r > 9 ? 6 : r > 3.5f ? 4 : 3;
        var points = new Vector2[(steps + 1) * 4];
        var n = 0;
        Span<(float Cx, float Cy, float From)> corners = stackalloc (float, float, float)[]
        {
            (x + w - r, y + r, -Mathf.Pi / 2), (x + w - r, y + h - r, 0), (x + r, y + h - r, Mathf.Pi / 2), (x + r, y + r, Mathf.Pi),
        };
        foreach (var (cx, cy, from) in corners)
        {
            for (var i = 0; i <= steps; i++)
            {
                var a = from + (i * Mathf.Pi / 2 / steps);
                points[n++] = new Vector2(cx + (Mathf.Cos(a) * r), cy + (Mathf.Sin(a) * r));
            }
        }

        return points;
    }

    /// <summary>A plain rectangle.</summary>
    public void Rect(double x, double y, double w, double h, Color fill)
    {
        if (w <= 0.001 || h <= 0.001)
        {
            return;
        }

        Fill(RoundedPoints((float)x * T, (float)y * T, (float)w * T, (float)h * T, 0), fill);
    }

    /// <summary>A rounded rectangle, filled, outlined or both.</summary>
    public void Round(double x, double y, double w, double h, double r, Color? fill, Color? stroke = null, double lineWidth = Hair)
    {
        if (w <= 0.001 || h <= 0.001)
        {
            return;
        }

        var points = RoundedPoints((float)x * T, (float)y * T, (float)w * T, (float)h * T, (float)r * T);
        if (fill is { } f)
        {
            Fill(points, f);
        }

        if (stroke is { } s)
        {
            Stroke(points, s, lineWidth, true);
        }
    }

    /// <summary>A rounded rectangle in <paramref name="fill"/> with its matching outline.</summary>
    public void Solid(double x, double y, double w, double h, double r, Color fill, double lineWidth = Hair) =>
        Round(x, y, w, h, r, fill, Outline(fill), lineWidth);

    /// <summary>A piece of furniture: a rounded rectangle with its outline and a shadow on the floor.</summary>
    public void Box(double x, double y, double w, double h, double r, Color fill, bool shadow = true, double lineWidth = Hair)
    {
        if (shadow)
        {
            Shadow(k => Round(x, y, w, h, r, k));
        }

        Solid(x, y, w, h, r, fill, lineWidth);
    }

    private static Vector2[] EllipsePoints(float x, float y, float rx, float ry, float rot)
    {
        var big = Math.Max(rx, ry);
        var n = big > 40 ? 32 : big > 12 ? 22 : big > 4 ? 14 : 10;
        var points = new Vector2[n];
        var (sin, cos) = rot == 0 ? (0f, 1f) : (Mathf.Sin(rot), Mathf.Cos(rot));
        for (var i = 0; i < n; i++)
        {
            var a = i * Mathf.Tau / n;
            var (px, py) = (Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            points[i] = new Vector2(x + (px * cos) - (py * sin), y + (px * sin) + (py * cos));
        }

        return points;
    }

    public void Ellipse(double x, double y, double rx, double ry, Color? fill, Color? stroke = null, double lineWidth = Hair, double rot = 0)
    {
        if (rx <= 0.001 || ry <= 0.001)
        {
            return;
        }

        var points = EllipsePoints((float)x * T, (float)y * T, (float)rx * T, (float)ry * T, (float)rot);
        if (fill is { } f)
        {
            Fill(points, f);
        }

        if (stroke is { } s)
        {
            Stroke(points, s, lineWidth, true);
        }
    }

    public void Circle(double x, double y, double r, Color? fill, Color? stroke = null, double lineWidth = Hair) =>
        Ellipse(x, y, r, r, fill, stroke, lineWidth);

    /// <summary>A disc in <paramref name="fill"/> with its matching outline.</summary>
    public void Dot(double x, double y, double r, Color fill, double lineWidth = Hair) => Circle(x, y, r, fill, Outline(fill), lineWidth);

    public void Line(double x1, double y1, double x2, double y2, Color colour, double width)
    {
        if (colour.A <= 0.003f)
        {
            return;
        }

        _c.DrawLine(new Vector2((float)x1 * T, (float)y1 * T), new Vector2((float)x2 * T, (float)y2 * T), Tone(colour), (float)width * T);
    }

    /// <summary>A line of dashes.</summary>
    public void Dashes(double x1, double y1, double x2, double y2, Color colour, double width, double dash, double gap)
    {
        var (dx, dy) = (x2 - x1, y2 - y1);
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
        {
            return;
        }

        (dx, dy) = (dx / length, dy / length);
        for (var at = 0.0; at < length; at += dash + gap)
        {
            var end = Math.Min(at + dash, length);
            Line(x1 + (dx * at), y1 + (dy * at), x1 + (dx * end), y1 + (dy * end), colour, width);
        }
    }

    /// <summary>A convex polygon (points in tiles).</summary>
    public void Poly(Color? fill, Color? stroke, double lineWidth, params (double X, double Y)[] points)
    {
        var p = Array.ConvertAll(points, q => new Vector2((float)q.X * T, (float)q.Y * T));
        if (fill is { } f)
        {
            Fill(p, f);
        }

        if (stroke is { } s)
        {
            Stroke(p, s, lineWidth, true);
        }
    }

    /// <summary>An open line through several points.</summary>
    public void Path(Color colour, double width, params (double X, double Y)[] points) =>
        Stroke(Array.ConvertAll(points, q => new Vector2((float)q.X * T, (float)q.Y * T)), colour, width, false);

    /// <summary>Part of a circle's edge, from angle <paramref name="from"/> to <paramref name="to"/> (radians, clockwise on screen from +x).</summary>
    public void Arc(double x, double y, double r, double from, double to, Color colour, double width, bool dashed = false)
    {
        var n = Math.Max(4, (int)(Math.Abs(to - from) * r * T / 5));
        if (!dashed)
        {
            var points = new Vector2[n + 1];
            for (var i = 0; i <= n; i++)
            {
                var a = from + ((to - from) * i / n);
                points[i] = new Vector2((float)(x + (Math.Cos(a) * r)) * T, (float)(y + (Math.Sin(a) * r)) * T);
            }

            Stroke(points, colour, width, false);
            return;
        }

        for (var i = 0; i < n; i += 2)
        {
            var (a, b) = (from + ((to - from) * i / n), from + ((to - from) * (i + 1) / n));
            Line(x + (Math.Cos(a) * r), y + (Math.Sin(a) * r), x + (Math.Cos(b) * r), y + (Math.Sin(b) * r), colour, width);
        }
    }

    // ------------------------------------------------------------------ text

    public static float TextWidth(string text, int size, bool bold = true) =>
        (bold ? Flat.Bold : ThemeDB.FallbackFont).GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    /// <summary>
    /// Text centred on (x, y). <paramref name="size"/> is in canvas pixels. Upright text stays the
    /// right way up when the view turns; otherwise it lies on the ground and turns with it.
    /// </summary>
    public void Text(string text, double x, double y, int size, Color colour, bool bold = true, double rot = 0, bool upright = false, float spacing = 0)
    {
        if (_inShadow || string.IsNullOrEmpty(text))
        {
            return;
        }

        var font = bold ? Flat.Bold : ThemeDB.FallbackFont;
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X + (spacing * Math.Max(0, text.Length - 1));
        var angle = (float)rot + (upright ? UprightAngle() : 0);
        if (!upright)
        {
            // Lettering on the ground or on a sign turns with the view, but never ends up upside down.
            var onScreen = Mathf.Wrap(angle + _m.Rotation + (_quarterTurns * Mathf.Pi / 2), -Mathf.Pi, Mathf.Pi);
            if (Math.Abs(onScreen) > (Mathf.Pi / 2) + 0.01f)
            {
                angle += Mathf.Pi;
            }
        }

        _c.DrawSetTransformMatrix(_m * new Transform2D(angle, new Vector2((float)x * T, (float)y * T)));
        if (spacing <= 0)
        {
            _c.DrawString(font, new Vector2(-width / 2, size * 0.36f), text, HorizontalAlignment.Left, -1, size, Tone(colour));
        }
        else
        {
            var at = -width / 2;
            foreach (var ch in text)
            {
                var s = ch.ToString();
                _c.DrawString(font, new Vector2(at, size * 0.36f), s, HorizontalAlignment.Left, -1, size, Tone(colour));
                at += font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X + spacing;
            }
        }

        _c.DrawSetTransformMatrix(_m);
    }

    /// <summary>A rounded label with text in it, centred on (x, y), upright on screen. Sizes in canvas pixels.</summary>
    public void Pill(string text, double x, double y, int size, Color fill, Color border, Color ink, float padX = 7, float padY = 4)
    {
        var w = (TextWidth(text, size) + (padX * 2)) / T;
        var h = (size + (padY * 2)) / T;
        Push(x, y, UprightAngle());
        Round(-w / 2, -h / 2, w, h, h / 2, fill, border, 1.5 / T);
        Text(text, 0, 0.5 / T, size, ink);
        Pop();
    }

    /// <summary>Runs <paramref name="draw"/> in a frame at (x, y) that stays upright on screen.</summary>
    public void Upright(double x, double y, Action draw)
    {
        Push(x, y, UprightAngle());
        draw();
        Pop();
    }
}
