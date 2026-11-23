using System;
using System.Collections.Generic;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>Small line icons for the patient record (tabs, rename), drawn in code so they stay crisp.</summary>
public partial class PanelIcon : Control
{
    public enum Kind
    {
        Person,
        Heart,
        Flask,
        Pill,
        Clock,
        Pencil,
    }

    private Color _colour = UiStyle.TextDark;

    public PanelIcon(Kind kind, float size = 18)
    {
        Icon = kind;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public Kind Icon { get; }

    public Color Colour
    {
        get => _colour;
        set
        {
            _colour = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // Drawn on a 24 x 24 grid, scaled to the control.
        var s = Mathf.Min(Size.X, Size.Y) / 24f;
        var o = new Vector2((Size.X - 24 * s) / 2, (Size.Y - 24 * s) / 2);
        var w = Mathf.Max(1.4f, 2f * s);
        Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
        void Line(params Vector2[] points) => DrawPolyline(points, _colour, w, true);
        void Ring(float x, float y, float r, float from = 0, float to = Mathf.Tau) => DrawArc(P(x, y), r * s, from, to, 28, _colour, w, true);

        switch (Icon)
        {
            case Kind.Person:
                Ring(12, 8, 4);
                Ring(12, 21, 8, Mathf.Pi, Mathf.Tau);
                break;
            case Kind.Heart:
                Line(P(12, 20.5f), P(4.2f, 13), P(3, 9.5f), P(4, 6), P(7.5f, 4), P(10.5f, 5), P(12, 7), P(13.5f, 5), P(16.5f, 4), P(20, 6), P(21, 9.5f), P(19.8f, 13), P(12, 20.5f));
                break;
            case Kind.Flask:
                Line(P(9, 3), P(15, 3));
                Line(P(10, 3), P(10, 9), P(5, 18), P(6, 21), P(18, 21), P(19, 18), P(14, 9), P(14, 3));
                Line(P(7.2f, 15), P(16.8f, 15));
                break;
            case Kind.Pill:
                {
                    // A capsule on a slant with a line across its middle.
                    var a = P(6.5f, 17.5f);
                    var b = P(17.5f, 6.5f);
                    var r = 4.2f * s;
                    var dir = (b - a).Normalized();
                    var n = new Vector2(-dir.Y, dir.X) * r;
                    DrawLine(a + n, b + n, _colour, w, true);
                    DrawLine(a - n, b - n, _colour, w, true);
                    var angle = dir.Angle();
                    DrawArc(a, r, angle + Mathf.Pi / 2, angle + Mathf.Pi * 1.5f, 14, _colour, w, true);
                    DrawArc(b, r, angle - Mathf.Pi / 2, angle + Mathf.Pi / 2, 14, _colour, w, true);
                    var mid = (a + b) / 2;
                    DrawLine(mid + n, mid - n, _colour, w, true);
                    break;
                }

            case Kind.Clock:
                Ring(12, 12, 9);
                Line(P(12, 7), P(12, 12), P(15.5f, 14));
                break;
            case Kind.Pencil:
                Line(P(4, 20), P(5, 15.5f), P(16, 4.5f), P(19.5f, 8), P(8.5f, 19), P(4, 20));
                Line(P(13.5f, 7), P(17, 10.5f));
                break;
        }
    }
}

/// <summary>
/// The patient's journey through the department as a row of steps: green ticks for what has
/// happened, a blue ring for what is happening now, grey for what is still to come.
/// </summary>
public partial class JourneyBar : Control
{
    public enum Step
    {
        Done,
        Current,
        Todo,
        Bad,
    }

    private IReadOnlyList<(string Label, Step State)> _steps = Array.Empty<(string, Step)>();

    public JourneyBar()
    {
        CustomMinimumSize = new Vector2(0, 46);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The steps as last drawn, for tests.</summary>
    public IReadOnlyList<(string Label, Step State)> Steps => _steps;

    public void Set(IReadOnlyList<(string Label, Step State)> steps)
    {
        _steps = steps;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_steps.Count == 0)
        {
            return;
        }

        var green = new Color(ClinicalText.GreenHex);
        var grey = new Color("#C3CCD4");
        var font = GetThemeDefaultFont();
        var slot = Size.X / _steps.Count;
        const float y = 10f;
        const float r = 8.5f;
        for (var i = 1; i < _steps.Count; i++)
        {
            var reached = _steps[i].State is Step.Done or Step.Current or Step.Bad && _steps[i - 1].State == Step.Done;
            DrawLine(new Vector2(slot * (i - 0.5f) + r, y), new Vector2(slot * (i + 0.5f) - r, y), reached ? green : grey, 2f, true);
        }

        for (var i = 0; i < _steps.Count; i++)
        {
            var (label, state) = _steps[i];
            var c = new Vector2(slot * (i + 0.5f), y);
            var text = UiStyle.TextMuted;
            switch (state)
            {
                case Step.Done:
                    DrawCircle(c, r, green);
                    DrawPolyline(new[] { c + new Vector2(-3.6f, 0.2f), c + new Vector2(-1f, 2.8f), c + new Vector2(3.8f, -2.4f) }, Colors.White, 1.8f, true);
                    text = UiStyle.TextDark;
                    break;
                case Step.Bad:
                    DrawCircle(c, r, new Color(ClinicalText.RedHex));
                    DrawLine(c + new Vector2(-3, -3), c + new Vector2(3, 3), Colors.White, 1.8f, true);
                    DrawLine(c + new Vector2(3, -3), c + new Vector2(-3, 3), Colors.White, 1.8f, true);
                    text = UiStyle.Red;
                    break;
                case Step.Current:
                    DrawCircle(c, r + 3, UiStyle.BlueTint);
                    DrawCircle(c, r, Colors.White);
                    DrawArc(c, r - 1, 0, Mathf.Tau, 28, UiStyle.Blue, 2f, true);
                    DrawCircle(c, 3.6f, UiStyle.Blue);
                    text = UiStyle.Blue;
                    break;
                default:
                    DrawCircle(c, r, Colors.White);
                    DrawArc(c, r - 1, 0, Mathf.Tau, 28, grey, 2f, true);
                    break;
            }

            // A label too wide for its slot ("Ward / home") goes on two lines.
            var lines = font.GetStringSize(label, HorizontalAlignment.Left, -1, 11).X > slot - 2 && label.Contains(" / ")
                ? label.Replace(" / ", " /\n").Split('\n')
                : new[] { label };
            for (var l = 0; l < lines.Length; l++)
            {
                DrawString(font, new Vector2(slot * i, y + r + 15 + l * 13), lines[l], HorizontalAlignment.Center, slot, 11, text);
            }
        }
    }
}

/// <summary>NEWS2 for each set of observations as coloured bars, oldest on the left.</summary>
public partial class News2Trend : Control
{
    private IReadOnlyList<(string Time, int Score)> _points = Array.Empty<(string, int)>();

    public News2Trend()
    {
        CustomMinimumSize = new Vector2(0, 78);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(IReadOnlyList<(string Time, int Score)> points)
    {
        _points = points;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_points.Count == 0)
        {
            return;
        }

        var font = GetThemeDefaultFont();
        const float gap = 6f;
        const float top = 14f;
        var bottom = Size.Y - 16f;
        var slot = (Size.X - gap * (_points.Count - 1)) / _points.Count;
        for (var i = 0; i < _points.Count; i++)
        {
            var (time, score) = _points[i];
            var x = i * (slot + gap);
            var h = 4 + (bottom - top - 4) * Math.Min(score, 10) / 10f;
            DrawRect(new Rect2(x, bottom - h, slot, h), new Color(PatientRecordPanel.News2Band(score)));
            DrawString(font, new Vector2(x, bottom - h - 3), score.ToString(), HorizontalAlignment.Center, slot, 11, UiStyle.TextMuted);
            DrawString(font, new Vector2(x, Size.Y - 2), time, HorizontalAlignment.Center, slot, 10, UiStyle.TextMuted);
        }
    }
}
