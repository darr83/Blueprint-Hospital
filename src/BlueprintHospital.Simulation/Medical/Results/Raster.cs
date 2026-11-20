namespace BlueprintHospital.Simulation.Medical.Results;

/// <summary>A signed distance: negative inside a shape, positive outside, in pixels.</summary>
public delegate float Sdf(float x, float y);

/// <summary>How a shape's colour combines with what is under it.</summary>
public enum Blend
{
    /// <summary>Paint over (like opaque ink at the given alpha).</summary>
    Over,

    /// <summary>Add light: overlapping bones on an X-ray get brighter.</summary>
    Add,

    /// <summary>Darken by multiplying (shadows, tinting).</summary>
    Multiply,
}

/// <summary>An RGB colour with 0-1 channels.</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    public static Rgb Grey(float v) => new(v, v, v);

    public static Rgb Hex(uint hex) => new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);

    public static Rgb Lerp(Rgb a, Rgb b, float t) => new(a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t));
}

/// <summary>
/// A small software painter for the result pictures (X-rays, scans, endoscopy). Shapes are signed distance
/// functions, so edges can be crisp or soft (blurred like real film) by changing one number, and bones can be
/// cut and shifted to show a fracture. Engine-free, so the pictures can be tested without Godot.
/// </summary>
public sealed class Raster
{
    private readonly float[] _r;
    private readonly float[] _g;
    private readonly float[] _b;

    public Raster(int width, int height)
    {
        Width = width;
        Height = height;
        _r = new float[width * height];
        _g = new float[width * height];
        _b = new float[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public void Fill(Rgb c)
    {
        Array.Fill(_r, c.R);
        Array.Fill(_g, c.G);
        Array.Fill(_b, c.B);
    }

    public Rgb Get(int x, int y)
    {
        var i = (y * Width) + x;
        return new Rgb(_r[i], _g[i], _b[i]);
    }

    /// <summary>Paints the inside of <paramref name="sdf"/>, with an edge <paramref name="soft"/> pixels wide.</summary>
    public void Paint(Sdf sdf, Rgb colour, float alpha = 1, float soft = 1, Blend blend = Blend.Over, Box? bounds = null) =>
        Apply(sdf, d => Coverage(d, soft), colour, alpha, blend, bounds, soft);

    /// <summary>Paints a band just inside the edge of a shape (the bright cortex of a bone, the wall of a cyst).</summary>
    public void PaintBand(Sdf sdf, float width, Rgb colour, float alpha = 1, float soft = 1, Blend blend = Blend.Over, Box? bounds = null) =>
        Apply(sdf, d => Coverage(d, soft) * (1 - Coverage(d + width, soft)), colour, alpha, blend, bounds, soft);

    /// <summary>Paints a line of <paramref name="width"/> along the edge of a shape.</summary>
    public void Stroke(Sdf sdf, float width, Rgb colour, float alpha = 1, float soft = 1, Blend blend = Blend.Over, Box? bounds = null) =>
        Apply(sdf, d => Coverage(MathF.Abs(d) - (width / 2), soft), colour, alpha, blend, bounds, soft + width);

    private void Apply(Sdf sdf, Func<float, float> coverage, Rgb c, float alpha, Blend blend, Box? bounds, float margin)
    {
        var box = bounds ?? new Box(0, 0, Width, Height);
        var x0 = Math.Max(0, (int)(box.X0 - margin - 2));
        var y0 = Math.Max(0, (int)(box.Y0 - margin - 2));
        var x1 = Math.Min(Width - 1, (int)(box.X1 + margin + 2));
        var y1 = Math.Min(Height - 1, (int)(box.Y1 + margin + 2));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var cov = coverage(sdf(x + 0.5f, y + 0.5f)) * alpha;
                if (cov <= 0.001f)
                {
                    continue;
                }

                var i = (y * Width) + x;
                switch (blend)
                {
                    case Blend.Add:
                        _r[i] += c.R * cov;
                        _g[i] += c.G * cov;
                        _b[i] += c.B * cov;
                        break;
                    case Blend.Multiply:
                        _r[i] *= 1 - (cov * (1 - c.R));
                        _g[i] *= 1 - (cov * (1 - c.G));
                        _b[i] *= 1 - (cov * (1 - c.B));
                        break;
                    default:
                        _r[i] += (c.R - _r[i]) * cov;
                        _g[i] += (c.G - _g[i]) * cov;
                        _b[i] += (c.B - _b[i]) * cov;
                        break;
                }
            }
        }
    }

    private static float Coverage(float d, float soft)
    {
        var s = Math.Max(0.5f, soft);
        var t = Math.Clamp(0.5f - (d / (2 * s)), 0, 1);
        return t * t * (3 - (2 * t));
    }

    /// <summary>Blurs the whole picture (three box passes, close to a Gaussian).</summary>
    public void Blur(int radius)
    {
        if (radius <= 0)
        {
            return;
        }

        foreach (var ch in new[] { _r, _g, _b })
        {
            for (var pass = 0; pass < 3; pass++)
            {
                BoxPass(ch, radius, true);
                BoxPass(ch, radius, false);
            }
        }
    }

    private void BoxPass(float[] ch, int r, bool horizontal)
    {
        var n = horizontal ? Width : Height;
        var lines = horizontal ? Height : Width;
        var tmp = new float[n];
        for (var line = 0; line < lines; line++)
        {
            float Read(int k) => horizontal ? ch[(line * Width) + Math.Clamp(k, 0, n - 1)] : ch[(Math.Clamp(k, 0, n - 1) * Width) + line];
            var sum = 0f;
            for (var k = -r; k <= r; k++)
            {
                sum += Read(k);
            }

            for (var k = 0; k < n; k++)
            {
                tmp[k] = sum / ((2 * r) + 1);
                sum += Read(k + r + 1) - Read(k - r);
            }

            for (var k = 0; k < n; k++)
            {
                if (horizontal)
                {
                    ch[(line * Width) + k] = tmp[k];
                }
                else
                {
                    ch[(k * Width) + line] = tmp[k];
                }
            }
        }
    }

    /// <summary>Film grain: a little random brightness on every pixel.</summary>
    public void Grain(Random random, float amount)
    {
        for (var i = 0; i < _r.Length; i++)
        {
            var n = ((float)random.NextDouble() - 0.5f) * amount;
            _r[i] += n;
            _g[i] += n;
            _b[i] += n;
        }
    }

    /// <summary>Ultrasound speckle: multiplies every pixel by grainy random brightness.</summary>
    public void Speckle(Random random, float strength)
    {
        for (var i = 0; i < _r.Length; i++)
        {
            var u = 1 - (random.NextDouble() * 0.999);
            var m = (float)(-Math.Log(u)) * strength;
            var k = 1 - strength + m;
            _r[i] *= k;
            _g[i] *= k;
            _b[i] *= k;
        }
    }

    /// <summary>Darkens everything outside <paramref name="inside"/> to <paramref name="colour"/> (a fan or a round scope view).</summary>
    public void Mask(Sdf inside, Rgb colour, float soft = 1) =>
        Apply((x, y) => -inside(x, y), d => Coverage(d, soft), colour, 1, Blend.Over, null, soft);

    /// <summary>Mirrors the picture left to right (a left limb drawn as a right one).</summary>
    public void FlipHorizontal()
    {
        foreach (var ch in new[] { _r, _g, _b })
        {
            for (var y = 0; y < Height; y++)
            {
                Array.Reverse(ch, y * Width, Width);
            }
        }
    }

    /// <summary>The picture as 8-bit RGBA bytes.</summary>
    public byte[] ToRgba()
    {
        var bytes = new byte[Width * Height * 4];
        for (var i = 0; i < _r.Length; i++)
        {
            bytes[i * 4] = ToByte(_r[i]);
            bytes[(i * 4) + 1] = ToByte(_g[i]);
            bytes[(i * 4) + 2] = ToByte(_b[i]);
            bytes[(i * 4) + 3] = 255;
        }

        return bytes;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
}

/// <summary>An axis-aligned box, used to limit painting to where a shape is.</summary>
public readonly record struct Box(float X0, float Y0, float X1, float Y1)
{
    public static Box Around(float cx, float cy, float rx, float ry) => new(cx - rx, cy - ry, cx + rx, cy + ry);

    public static Box Of(IEnumerable<(float X, float Y)> points)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var (x, y) in points)
        {
            x0 = Math.Min(x0, x);
            y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x);
            y1 = Math.Max(y1, y);
        }

        return new Box(x0, y0, x1, y1);
    }

    public Box Grow(float by) => new(X0 - by, Y0 - by, X1 + by, Y1 + by);
}

/// <summary>Signed distance shapes and ways to combine them.</summary>
public static class Shapes
{
    public static Sdf Circle(float cx, float cy, float r) => (x, y) => MathF.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy))) - r;

    /// <summary>An ellipse, optionally rotated by <paramref name="angle"/> radians (approximate distance).</summary>
    public static Sdf Ellipse(float cx, float cy, float rx, float ry, float angle = 0)
    {
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        var m = Math.Min(rx, ry);
        return (x, y) =>
        {
            var dx = x - cx;
            var dy = y - cy;
            var u = ((dx * c) + (dy * s)) / rx;
            var v = ((-dx * s) + (dy * c)) / ry;
            return (MathF.Sqrt((u * u) + (v * v)) - 1) * m;
        };
    }

    /// <summary>A rounded bar from A to B whose radius changes from <paramref name="ra"/> to <paramref name="rb"/>.</summary>
    public static Sdf Capsule(float ax, float ay, float bx, float by, float ra, float rb)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var len2 = Math.Max(1e-4f, (dx * dx) + (dy * dy));
        return (x, y) =>
        {
            var t = Math.Clamp((((x - ax) * dx) + ((y - ay) * dy)) / len2, 0, 1);
            var px = x - (ax + (t * dx));
            var py = y - (ay + (t * dy));
            return MathF.Sqrt((px * px) + (py * py)) - (ra + ((rb - ra) * t));
        };
    }

    public static Sdf Capsule(float ax, float ay, float bx, float by, float r) => Capsule(ax, ay, bx, by, r, r);

    /// <summary>A rectangle centred on (cx, cy) with half sizes hx, hy, rotated by angle radians, with rounded corners.</summary>
    public static Sdf Rect(float cx, float cy, float hx, float hy, float angle = 0, float round = 0)
    {
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        return (x, y) =>
        {
            var dx = x - cx;
            var dy = y - cy;
            var u = MathF.Abs((dx * c) + (dy * s)) - hx + round;
            var v = MathF.Abs((-dx * s) + (dy * c)) - hy + round;
            var ox = Math.Max(u, 0);
            var oy = Math.Max(v, 0);
            return MathF.Sqrt((ox * ox) + (oy * oy)) + Math.Min(Math.Max(u, v), 0) - round;
        };
    }

    /// <summary>A filled polygon.</summary>
    public static Sdf Polygon(IReadOnlyList<(float X, float Y)> pts)
    {
        var p = pts.ToArray();
        return (x, y) =>
        {
            var d = float.MaxValue;
            var sign = 1f;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            {
                var ex = p[j].X - p[i].X;
                var ey = p[j].Y - p[i].Y;
                var wx = x - p[i].X;
                var wy = y - p[i].Y;
                var t = Math.Clamp(((wx * ex) + (wy * ey)) / Math.Max(1e-6f, (ex * ex) + (ey * ey)), 0, 1);
                var bx = wx - (ex * t);
                var by = wy - (ey * t);
                d = Math.Min(d, (bx * bx) + (by * by));
                var c1 = y >= p[i].Y;
                var c2 = y < p[j].Y;
                var c3 = (ex * wy) > (ey * wx);
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
                {
                    sign = -sign;
                }
            }

            return sign * MathF.Sqrt(d);
        };
    }

    /// <summary>A line through points, <paramref name="r"/> wide each side (tapering from r0 to r1 if given).</summary>
    public static Sdf Polyline(IReadOnlyList<(float X, float Y)> pts, float r0, float? r1 = null)
    {
        var parts = new List<Sdf>();
        var end = r1 ?? r0;
        for (var i = 0; i + 1 < pts.Count; i++)
        {
            var ta = r0 + ((end - r0) * i / Math.Max(1, pts.Count - 1));
            var tb = r0 + ((end - r0) * (i + 1) / Math.Max(1, pts.Count - 1));
            parts.Add(Capsule(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, ta, tb));
        }

        return Union(parts.ToArray());
    }

    /// <summary>Points along a cubic Bézier curve.</summary>
    public static List<(float X, float Y)> Bezier((float X, float Y) a, (float X, float Y) b, (float X, float Y) c, (float X, float Y) d, int steps = 16)
    {
        var list = new List<(float, float)>();
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var u = 1 - t;
            list.Add((
                (u * u * u * a.X) + (3 * u * u * t * b.X) + (3 * u * t * t * c.X) + (t * t * t * d.X),
                (u * u * u * a.Y) + (3 * u * u * t * b.Y) + (3 * u * t * t * c.Y) + (t * t * t * d.Y)));
        }

        return list;
    }

    /// <summary>
    /// Builds a polygon from SVG-like path text: "M x,y L x,y C x1,y1 x2,y2 x,y ... Z", so outlines can be drawn
    /// in a vector editor and pasted in.
    /// </summary>
    public static List<(float X, float Y)> Path(string path, float scale = 1, float dx = 0, float dy = 0)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(path, "([MLCZmlcz])", " $1 ").Replace(",", " ", StringComparison.Ordinal);
        var tokens = spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var pts = new List<(float X, float Y)>();
        var cur = (X: 0f, Y: 0f);
        var i = 0;
        var cmd = 'M';
        float Num() => float.Parse(tokens[i++], System.Globalization.CultureInfo.InvariantCulture);
        (float X, float Y) Pt() => ((Num() * scale) + dx, (Num() * scale) + dy);
        while (i < tokens.Length)
        {
            if (char.IsLetter(tokens[i][0]))
            {
                cmd = tokens[i][0];
                i++;
                if (cmd is 'Z' or 'z')
                {
                    continue;
                }
            }

            switch (cmd)
            {
                case 'M':
                case 'L':
                    cur = Pt();
                    pts.Add(cur);
                    break;
                case 'C':
                    var c1 = Pt();
                    var c2 = Pt();
                    var e = Pt();
                    pts.AddRange(Bezier(cur, c1, c2, e, 10).Skip(1));
                    cur = e;
                    break;
                default:
                    throw new FormatException($"Unsupported path command '{cmd}'.");
            }
        }

        return pts;
    }

    public static Sdf Union(params Sdf[] shapes) => (x, y) =>
    {
        var d = float.MaxValue;
        foreach (var s in shapes)
        {
            d = Math.Min(d, s(x, y));
        }

        return d;
    };

    /// <summary>A union that blends shapes together over <paramref name="k"/> pixels, like bone flaring into a joint.</summary>
    public static Sdf SmoothUnion(float k, params Sdf[] shapes) => (x, y) =>
    {
        var d = shapes[0](x, y);
        for (var i = 1; i < shapes.Length; i++)
        {
            var b = shapes[i](x, y);
            var h = Math.Clamp(0.5f + (0.5f * (b - d) / k), 0, 1);
            d = (b * (1 - h)) + (d * h) - (k * h * (1 - h));
        }

        return d;
    };

    public static Sdf Subtract(Sdf a, Sdf b) => (x, y) => Math.Max(a(x, y), -b(x, y));

    public static Sdf Intersect(Sdf a, Sdf b) => (x, y) => Math.Max(a(x, y), b(x, y));

    public static Sdf Move(Sdf a, float dx, float dy) => (x, y) => a(x - dx, y - dy);

    /// <summary>Rotates a shape about (cx, cy).</summary>
    public static Sdf Rotate(Sdf a, float cx, float cy, float angle)
    {
        var c = MathF.Cos(-angle);
        var s = MathF.Sin(-angle);
        return (x, y) =>
        {
            var dx = x - cx;
            var dy = y - cy;
            return a(cx + (dx * c) - (dy * s), cy + (dx * s) + (dy * c));
        };
    }

    /// <summary>The side of the line A→B: negative on the left, positive on the right.</summary>
    public static Sdf HalfPlane(float ax, float ay, float bx, float by)
    {
        var nx = by - ay;
        var ny = -(bx - ax);
        var len = MathF.Sqrt((nx * nx) + (ny * ny));
        nx /= len;
        ny /= len;
        return (x, y) => ((x - ax) * nx) + ((y - ay) * ny);
    }

    /// <summary>
    /// Breaks a shape along the line A→B: the part on the right is moved by (dx, dy) and turned by
    /// <paramref name="turn"/> radians about the break, leaving a gap of <paramref name="gap"/> pixels.
    /// </summary>
    public static Sdf Fracture(Sdf bone, float ax, float ay, float bx, float by, float gap, float dx = 0, float dy = 0, float turn = 0)
    {
        var line = HalfPlane(ax, ay, bx, by);
        var keep = Intersect(bone, (x, y) => -line(x, y) + (gap / 2));
        var moved = Intersect(bone, (x, y) => line(x, y) + (gap / 2));
        var mx = (ax + bx) / 2;
        var my = (ay + by) / 2;
        var shifted = Move(Rotate(moved, mx, my, turn), dx, dy);
        return Union(keep, shifted);
    }
}
