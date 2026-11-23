using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Medical.Results;
using Godot;

namespace BlueprintHospital.UI.Results;

/// <summary>Shared drawing helpers and the picture texture cache for the result viewer.</summary>
internal static class ResultArt
{
    private static readonly Dictionary<(string, int), (ImageTexture Texture, RenderedPicture Picture)> Cache = new();
    private static readonly Queue<(string, int)> Order = new();

    public static readonly Color Mark = new("#FFB300");
    public static readonly Color MarkInk = new("#1B1B1B");

    /// <summary>The drawn picture and its texture, made once and kept for the next time it is opened.</summary>
    public static (ImageTexture Texture, RenderedPicture Picture) Picture(string id, int seed)
    {
        var key = (id, seed);
        if (Cache.TryGetValue(key, out var hit))
        {
            return hit;
        }

        var picture = ResultPictures.Render(id, seed);
        var image = Image.CreateFromData(picture.Width, picture.Height, false, Image.Format.Rgba8, picture.Rgba);
        var made = (ImageTexture.CreateFromImage(image), picture);
        Cache[key] = made;
        Order.Enqueue(key);
        while (Order.Count > 24)
        {
            Cache.Remove(Order.Dequeue());
        }

        return made;
    }

    public static Font Font => ThemeDB.FallbackFont;

    /// <summary>A finding marker: a dashed circle and a label on an amber tab.</summary>
    public static void DrawMarker(CanvasItem c, Vector2 centre, float radius, string label, Rect2 bounds)
    {
        const int dashes = 28;
        for (var i = 0; i < dashes; i++)
        {
            var a0 = i / (float)dashes * Mathf.Tau;
            var a1 = (i + 0.6f) / dashes * Mathf.Tau;
            c.DrawArc(centre, radius, a0, a1, 6, Mark, 3, true);
        }

        DrawTab(c, centre, radius, label, bounds);
    }

    public static void DrawTab(CanvasItem c, Vector2 centre, float radius, string label, Rect2 bounds)
    {
        const int size = 15;
        var width = Font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X + 18;
        var above = centre.Y - radius - 40 > bounds.Position.Y;
        var tab = new Rect2(centre.X - (width / 2), above ? centre.Y - radius - 36 : centre.Y + radius + 10, width, 26);
        tab.Position = new Vector2(Mathf.Clamp(tab.Position.X, bounds.Position.X + 4, bounds.End.X - width - 4), Mathf.Clamp(tab.Position.Y, bounds.Position.Y + 4, bounds.End.Y - 30));
        var anchor = new Vector2(Mathf.Clamp(centre.X, tab.Position.X + 8, tab.End.X - 8), above ? tab.End.Y : tab.Position.Y);
        var dir = (anchor - centre).Normalized();
        c.DrawLine(centre + (dir * radius), anchor, Mark, 2.5f, true);
        c.DrawRect(tab, Mark);
        c.DrawString(Font, tab.Position + new Vector2(9, 18), label, HorizontalAlignment.Left, -1, size, MarkInk);
    }
}

/// <summary>Draws a result picture to fit, with zoom, brightness, corner labels and finding markers.</summary>
public partial class PictureView : Control
{
    private ImageTexture? _texture;
    private RenderedPicture? _picture;

    public float Zoom { get; set; } = 1;

    public float Brightness { get; set; } = 1;

    public bool ShowMarkers { get; set; } = true;

    public Color Backdrop { get; set; } = new("#050607");

    public void SetPicture(string id, int seed)
    {
        (_texture, _picture) = ResultArt.Picture(id, seed);
        QueueRedraw();
    }

    /// <summary>The picture's natural aspect, so a layout can size it.</summary>
    public float Aspect => _picture is null ? 1 : _picture.Width / (float)_picture.Height;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Backdrop);
        if (_texture is null || _picture is null)
        {
            return;
        }

        var fit = Mathf.Min(Size.X / _picture.Width, Size.Y / _picture.Height) * Zoom;
        var size = new Vector2(_picture.Width, _picture.Height) * fit;
        var origin = (Size - size) / 2;
        var area = new Rect2(origin, size);
        var clip = new Rect2(Vector2.Zero, Size);
        DrawTextureRect(_texture, area, false, new Color(Brightness, Brightness, Brightness));
        var font = ResultArt.Font;
        foreach (var l in _picture.Labels)
        {
            var size2 = Mathf.RoundToInt(l.Size * Mathf.Clamp(fit, 0.8f, 1.6f));
            var pos = origin + (new Vector2(l.X, l.Y) * fit);
            var w = font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, size2).X;
            var x = l.Align < 0 ? pos.X : l.Align == 0 ? pos.X - (w / 2) : pos.X - w;
            var colour = new Color(0.9f, 0.9f, 0.9f);
            if (l.Boxed)
            {
                DrawRect(new Rect2(x - 6, pos.Y - 2, w + 12, size2 + 8), colour, false, 2);
            }

            DrawString(font, new Vector2(x, pos.Y + size2), l.Text, HorizontalAlignment.Left, -1, size2, l.Boxed || _picture.Title.StartsWith("Urine", StringComparison.Ordinal) ? (l.Boxed ? colour : UiStyle.TextMuted) : colour);
        }

        if (ShowMarkers)
        {
            foreach (var m in _picture.Markers)
            {
                ResultArt.DrawMarker(this, origin + (new Vector2(m.X, m.Y) * fit), m.Radius * fit, m.Label, clip);
            }
        }
    }
}

/// <summary>Draws an ECG on pink paper (3 x 4 leads and a rhythm strip) or an EEG as stacked channels.</summary>
public partial class TraceView : Control
{
    private TraceRecording? _trace;
    private bool _eeg;

    public bool ShowMarkers { get; set; } = true;

    public void SetTrace(string id, int seed)
    {
        _trace = Waveforms.Build(id, seed);
        _eeg = id.StartsWith("eeg_", StringComparison.Ordinal);
        CustomMinimumSize = new Vector2(0, _eeg ? 420 : 300);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_trace is null)
        {
            return;
        }

        if (_eeg)
        {
            DrawEeg(_trace);
        }
        else
        {
            DrawEcg(_trace);
        }
    }

    private void DrawEcg(TraceRecording t)
    {
        var w = Size.X;
        var mm = w / 250f;
        var h = mm * 110;
        DrawRect(new Rect2(0, 0, w, h), new Color("#FFF7F7"));
        for (var i = 0; i * mm <= w; i++)
        {
            DrawLine(new Vector2(i * mm, 0), new Vector2(i * mm, h), i % 5 == 0 ? new Color("#EC9BA1") : new Color("#F7D3D6"), i % 5 == 0 ? 1 : 0.5f);
        }

        for (var i = 0; i * mm <= h; i++)
        {
            DrawLine(new Vector2(0, i * mm), new Vector2(w, i * mm), i % 5 == 0 ? new Color("#EC9BA1") : new Color("#F7D3D6"), i % 5 == 0 ? 1 : 0.5f);
        }

        string[,] layout = { { "I", "aVR", "V1", "V4" }, { "II", "aVL", "V2", "V5" }, { "III", "aVF", "V3", "V6" } };
        var font = ResultArt.Font;
        var ink = new Color("#1B1B1B");
        var colW = w / 4;
        var marks = new List<(Rect2 Box, bool Main)>();
        for (var row = 0; row < 3; row++)
        {
            var baseY = mm * (17 + (row * 25));
            for (var col = 0; col < 4; col++)
            {
                var name = layout[row, col];
                var ch = t.Channel(name);
                var start = (int)(col * 2.5 * t.SampleRate);
                var count = (int)(2.5 * t.SampleRate);
                var pts = new Vector2[count];
                for (var i = 0; i < count; i++)
                {
                    pts[i] = new Vector2((col * colW) + (i * colW / count), baseY - (ch.Samples[start + i] * 10 * mm));
                }

                DrawPolyline(pts, ink, 1.3f, true);
                DrawString(font, new Vector2((col * colW) + 4, baseY - (mm * 8)), name, HorizontalAlignment.Left, -1, 14, ink);
                if (col > 0)
                {
                    DrawLine(new Vector2(col * colW, baseY - (mm * 3)), new Vector2(col * colW, baseY + (mm * 3)), ink, 1.3f);
                }

                var box = new Rect2((col * colW) + 2, baseY - (mm * 11), colW - 4, mm * 18);
                if (t.Marked.Contains(name))
                {
                    marks.Add((box, true));
                }
                else if (t.Reciprocal.Contains(name))
                {
                    marks.Add((box, false));
                }
            }
        }

        var rhythm = t.Channel("II");
        var rY = mm * 95;
        var rp = new Vector2[rhythm.Samples.Length / 2];
        for (var i = 0; i < rp.Length; i++)
        {
            rp[i] = new Vector2(i * w / rp.Length, rY - (rhythm.Samples[i * 2] * 10 * mm));
        }

        DrawPolyline(rp, ink, 1.3f, true);
        DrawString(font, new Vector2(4, rY - (mm * 8)), "II", HorizontalAlignment.Left, -1, 14, ink);
        DrawString(font, new Vector2(w - 230, h - 4), "25 mm/s   10 mm/mV   0.05-150 Hz", HorizontalAlignment.Left, -1, 11, new Color("#8A5A5E"));
        if (ShowMarkers)
        {
            foreach (var (box, main) in marks)
            {
                var colour = main ? new Color("#E69A00") : UiStyle.Blue;
                DrawRect(box, new Color(colour, 0.1f));
                DrawRect(box, colour, false, 2);
            }

            DrawLabelUnder(marks.Where(m => m.Main).Select(m => m.Box).ToList(), t.MarkLabel, new Color("#E69A00"), ink);
            DrawLabelUnder(marks.Where(m => !m.Main).Select(m => m.Box).ToList(), t.ReciprocalLabel, UiStyle.Blue, Colors.White);
        }

        CustomMinimumSize = new Vector2(CustomMinimumSize.X, h);
    }

    private void DrawLabelUnder(List<Rect2> boxes, string text, Color bg, Color fg)
    {
        if (boxes.Count == 0 || text.Length == 0)
        {
            return;
        }

        var last = boxes.OrderBy(b => b.End.Y).ThenBy(b => b.Position.X).Last();
        var width = ResultArt.Font.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X + 16;
        var tab = new Rect2(Mathf.Min(last.Position.X + 4, Size.X - width - 4), last.End.Y + 2, width, 22);
        DrawRect(tab, bg);
        DrawString(ResultArt.Font, tab.Position + new Vector2(8, 16), text, HorizontalAlignment.Left, -1, 13, fg);
    }

    private void DrawEeg(TraceRecording t)
    {
        var w = Size.X;
        var rows = t.Channels.Count;
        var h = Mathf.Max(Size.Y, rows * 34f);
        DrawRect(new Rect2(0, 0, w, h), new Color("#FBFBF7"));
        var left = 70f;
        for (var s = 0; s <= 10; s++)
        {
            var x = left + ((w - left - 8) * s / 10f);
            DrawLine(new Vector2(x, 0), new Vector2(x, h), new Color("#DCDCD2"), 1);
        }

        var ink = new Color("#1E2A36");
        var spacing = h / rows;
        for (var r = 0; r < rows; r++)
        {
            var ch = t.Channels[r];
            var y = (r + 0.5f) * spacing;
            var marked = ShowMarkers && t.Marked.Contains(ch.Name);
            if (marked)
            {
                DrawRect(new Rect2(left, y - (spacing / 2) + 1, w - left - 8, spacing - 2), new Color(1f, 0.7f, 0f, 0.08f));
            }

            DrawString(ResultArt.Font, new Vector2(6, y + 5), ch.Name, HorizontalAlignment.Left, -1, 12, ink);
            var n = ch.Samples.Length / 2;
            var pts = new Vector2[n];
            for (var i = 0; i < n; i++)
            {
                pts[i] = new Vector2(left + ((w - left - 8) * i / n), y - (ch.Samples[i * 2] * spacing / 220f));
            }

            DrawPolyline(pts, ink, 1, true);
        }

        if (ShowMarkers && t.MarkLabel.Length > 0)
        {
            var width = ResultArt.Font.GetStringSize(t.MarkLabel, HorizontalAlignment.Left, -1, 14).X + 18;
            var tab = new Rect2(w - width - 12, 8, width, 24);
            DrawRect(tab, ResultArt.Mark);
            DrawString(ResultArt.Font, tab.Position + new Vector2(9, 17), t.MarkLabel, HorizontalAlignment.Left, -1, 14, ResultArt.MarkInk);
        }
    }

    public string Statement => _trace?.Statement ?? string.Empty;

    public IReadOnlyList<string> Measurements => _trace?.Measurements ?? Array.Empty<string>();
}

/// <summary>A small bar showing where a value sits against its reference range.</summary>
public partial class RangeBar : Control
{
    private readonly double? _value;
    private readonly double? _low;
    private readonly double? _high;
    private readonly string _flag;
    private readonly bool _dark;

    public RangeBar(double? value, double? low, double? high, string flag, bool dark = false)
    {
        _value = value;
        _low = low;
        _high = high;
        _flag = flag;
        _dark = dark;
        CustomMinimumSize = new Vector2(120, 18);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        if (_value is not { } v || (_low is null && _high is null))
        {
            return;
        }

        var lo = _low ?? 0;
        var hi = _high ?? (lo * 2);
        var span = Math.Max(1e-6, hi - lo);
        var min = Math.Max(0, lo - (span * 0.7));
        var max = hi + (span * 0.7);
        float X(double a) => 6 + (float)((Math.Clamp(a, min, max) - min) / (max - min) * (Size.X - 12));
        var mid = Size.Y / 2;
        DrawRect(new Rect2(6, mid - 2, Size.X - 12, 4), _dark ? new Color("#2B3946") : UiStyle.Hairline);
        DrawRect(new Rect2(X(lo), mid - 4, X(hi) - X(lo), 8), _dark ? new Color("#3E5A47") : new Color("#CDE8D8"));
        var colour = _flag == "H" ? UiStyle.Red : _flag == "L" ? UiStyle.Blue : UiStyle.Green;
        if (v > max)
        {
            DrawColoredPolygon(new[] { new Vector2(Size.X - 10, mid - 6), new Vector2(Size.X - 1, mid), new Vector2(Size.X - 10, mid + 6) }, colour);
        }
        else if (v < min)
        {
            DrawColoredPolygon(new[] { new Vector2(10, mid - 6), new Vector2(1, mid), new Vector2(10, mid + 6) }, colour);
        }
        else
        {
            DrawCircle(new Vector2(X(v), mid), 6, Colors.White);
            DrawCircle(new Vector2(X(v), mid), 4.5f, colour);
        }
    }
}
