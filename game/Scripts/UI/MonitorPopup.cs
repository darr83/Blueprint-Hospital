using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The big bedside monitor: opened by clicking a monitor in play mode. Its ECG, pleth and breathing
/// traces sweep across a black screen at the patient's own heart and breathing rates, in the rhythm their
/// illness gives them, with the live numbers beside them and alarms for anything out of range.
/// </summary>
public partial class MonitorPopup : Control
{
    private readonly MonitorScreen _screen = new();
    private readonly Button _silence = new() { Text = "Silence alarm", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44) };
    private HospitalSimulation _sim = null!;

    /// <summary>Raised when the player asks for the patient's record.</summary>
    public event Action<Patient>? RecordRequested;

    /// <summary>The patient on the monitor showing.</summary>
    public Patient? Patient => _screen.Patient;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        _screen.Simulation = simulation;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        var dim = new ColorRect { Color = new Color(0.06f, 0.1f, 0.14f, 0.55f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        dim.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Close();
            }
        };
        AddChild(dim);

        // The monitor's grey casing, centred.
        var bezel = new PanelContainer();
        var casing = UiStyle.Box(new Color("#30363C"), 26, new Color("#4B535A"));
        casing.BorderWidthTop = casing.BorderWidthLeft = casing.BorderWidthRight = casing.BorderWidthBottom = 3;
        casing.ContentMarginLeft = casing.ContentMarginRight = casing.ContentMarginTop = 24;
        casing.ContentMarginBottom = 18;
        casing.ShadowColor = new Color(0, 0, 0, 0.45f);
        casing.ShadowSize = 30;
        bezel.AddThemeStyleboxOverride("panel", casing);
        bezel.AnchorLeft = bezel.AnchorRight = bezel.AnchorTop = bezel.AnchorBottom = 0.5f;
        bezel.OffsetLeft = -600;
        bezel.OffsetRight = 600;
        bezel.OffsetTop = -370;
        bezel.OffsetBottom = 370;
        AddChild(bezel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        bezel.AddChild(column);
        _screen.SizeFlagsVertical = SizeFlags.ExpandFill;
        _screen.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.AddChild(_screen);

        var keys = new HBoxContainer();
        keys.AddThemeConstantOverride("separation", 10);
        column.AddChild(keys);
        _silence.Pressed += () => _screen.SilenceFor(120);
        keys.AddChild(Key(_silence, "#3A4046", "#D5DCE2"));
        var trends = new Button { Text = "Trends", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44), ToggleMode = true };
        trends.Toggled += on => _screen.ShowTrends = on;
        keys.AddChild(Key(trends, "#3A4046", "#D5DCE2"));
        var record = new Button { Text = "Open patient record", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44) };
        record.Pressed += () =>
        {
            if (_screen.Patient is { } p)
            {
                Close();
                RecordRequested?.Invoke(p);
            }
        };
        keys.AddChild(Key(record, "#1F5FBF", "#FFFFFF"));
        var close = new Button { Text = "Close  ✕", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(130, 44), TooltipText = "Close (Esc)" };
        close.Pressed += Close;
        keys.AddChild(Key(close, "#5B6B79", "#FFFFFF"));
        _trends = trends;
    }

    private Button _trends = null!;

    private static Button Key(Button button, string back, string ink)
    {
        foreach (var (state, colour) in new[] { ("normal", new Color(back)), ("hover", new Color(back).Lightened(0.12f)), ("pressed", new Color(back).Lightened(0.22f)), ("hover_pressed", new Color(back).Lightened(0.22f)), ("focus", new Color(back)) })
        {
            button.AddThemeStyleboxOverride(state, UiStyle.Box(colour, 8));
        }

        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
        {
            button.AddThemeColorOverride(state, new Color(ink));
        }

        UiStyle.Strong(button, 15);
        button.MouseDefaultCursorShape = CursorShape.PointingHand;
        return button;
    }

    /// <summary>Shows the monitor for a patient, in a room (and bed, on a ward or bay).</summary>
    public void Open(Patient patient, CareSpace room, int? bed)
    {
        _screen.Show(patient, room, bed);
        _trends.SetPressedNoSignal(false);
        _screen.ShowTrends = false;
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
        _screen.Patient = null;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        if (_screen.Patient is not { IsInDepartment: true })
        {
            Close();
            return;
        }

        _silence.Text = _screen.SilencedFor > 0 ? $"Silenced {(int)_screen.SilencedFor / 60}:{(int)_screen.SilencedFor % 60:00}" : "Silence alarm";
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Godot.Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}

/// <summary>The black screen of the big monitor: traces on the left, numbers on the right, NEWS2 along the bottom.</summary>
public partial class MonitorScreen : Control
{
    private const int SampleRate = 125;
    private const double SweepSeconds = 5.0;

    private static readonly Color Ecg = new("#39E75F");
    private static readonly Color Pleth = new("#2EC7F0");
    private static readonly Color Resp = new("#F5E15A");
    private static readonly Color Nibp = new("#FF5A7A");
    private static readonly Color Temp = new("#FF9F43");
    private static readonly Color Pain = new("#C9A7FF");
    private static readonly Color Grid = new("#16202B");
    private static readonly Color Bar = new("#0E1A2A");

    private readonly float[] _ecg = new float[(int)(SampleRate * SweepSeconds)];
    private readonly float[] _pleth = new float[(int)(SampleRate * SweepSeconds)];
    private readonly float[] _resp = new float[(int)(SampleRate * SweepSeconds)];
    private readonly Random _random = new(7);
    private int _cursor;
    private double _sampleDebt;
    private double _beatTime, _beatLength = 1, _breathTime, _breathLength = 4, _pTime;
    private double _clock;
    private double _refresh;
    private string _room = string.Empty;
    private int? _bed;
    private Observations? _obs;
    private News2Score? _news;
    private MonitorRhythm _rhythm;

    public HospitalSimulation Simulation { get; set; } = null!;

    public Patient? Patient { get; set; }

    public bool ShowTrends { get; set; }

    /// <summary>Seconds left on a silenced alarm.</summary>
    public double SilencedFor { get; private set; }

    public MonitorScreen()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(Patient patient, CareSpace room, int? bed)
    {
        Patient = patient;
        _room = room.Name;
        _bed = bed;
        Array.Clear(_ecg);
        Array.Clear(_pleth);
        Array.Clear(_resp);
        _cursor = 0;
        SilencedFor = 0;
        Read();

        // Start at this patient's own rates, not the last patient's.
        _beatTime = _breathTime = 0;
        _beatLength = NextBeatLength();
        _breathLength = _obs is null ? 4 : 60.0 / Math.Clamp(_obs.RespiratoryRate, 4, 60);

        // Fill the screen once so it opens mid-sweep, like a monitor that has been running.
        Advance(SweepSeconds * 0.98);
    }

    public void SilenceFor(double seconds) => SilencedFor = seconds;

    private void Read()
    {
        if (Patient is null)
        {
            return;
        }

        _obs = Patient.CurrentObservations();
        _news = News2.Calculate(_obs);
        _rhythm = Simulation.RhythmOf(Patient);
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || Patient is null)
        {
            return;
        }

        SilencedFor = Math.Max(0, SilencedFor - delta);
        _refresh -= delta;
        if (_refresh <= 0)
        {
            _refresh = 0.5;
            Read();
        }

        Advance(delta);
        QueueRedraw();
    }

    // ------------------------------------------------------------------ the traces

    private double HeartPeriod => _obs is null ? 1 : 60.0 / Math.Clamp(_obs.HeartRate, 20, 240);

    private void Advance(double seconds)
    {
        _sampleDebt += seconds * SampleRate;
        var dt = 1.0 / SampleRate;
        while (_sampleDebt >= 1)
        {
            _sampleDebt -= 1;
            _clock += dt;
            _beatTime += dt;
            _breathTime += dt;
            _pTime += dt;
            if (_beatTime >= _beatLength)
            {
                _beatTime -= _beatLength;
                _beatLength = NextBeatLength();
            }

            if (_breathTime >= _breathLength)
            {
                _breathTime -= _breathLength;
                _breathLength = _obs is null ? 4 : 60.0 / Math.Clamp(_obs.RespiratoryRate, 4, 60) * (0.92 + (_random.NextDouble() * 0.16));
            }

            if (_pTime >= 0.8)
            {
                _pTime -= 0.8;
            }

            _ecg[_cursor] = EcgSample();
            _pleth[_cursor] = PlethSample();
            _resp[_cursor] = (float)Math.Sin(_breathTime / _breathLength * Math.Tau - (Math.PI / 2)) * 0.8f;
            _cursor = (_cursor + 1) % _ecg.Length;
        }
    }

    private double NextBeatLength() => _rhythm switch
    {
        // Irregularly irregular: no two beats the same length.
        MonitorRhythm.AtrialFibrillation => HeartPeriod * (0.55 + (_random.NextDouble() * 0.9)),
        _ => HeartPeriod * (0.98 + (_random.NextDouble() * 0.04)),
    };

    private static double Bump(double t, double centre, double width) => Math.Exp(-Math.Pow((t - centre) / width, 2));

    /// <summary>The ECG at this moment: a P-QRS-T for each beat, squeezed when the heart is fast.</summary>
    private float EcgSample()
    {
        var noise = (_random.NextDouble() - 0.5) * 0.03;
        switch (_rhythm)
        {
            case MonitorRhythm.Asystole:
                return (float)(noise + (Math.Sin(_clock * 1.3) * 0.02));
            case MonitorRhythm.VentricularFibrillation:
                return (float)((Math.Sin(_clock * 31) * 0.35) + (Math.Sin(_clock * 19.7 + 1) * 0.25) + (Math.Sin(_clock * 7.3) * 0.15) + noise);
        }

        var scale = Math.Min(1, _beatLength / 0.75);
        var t = _beatTime / scale;
        var value = 0.0;
        if (_rhythm == MonitorRhythm.AtrialFibrillation)
        {
            // Fibrillation waves instead of P waves.
            value += Math.Sin(_clock * 37) * 0.04 + (Math.Sin(_clock * 23 + 2) * 0.03);
        }
        else if (_rhythm == MonitorRhythm.HeartBlock)
        {
            // P waves march through on their own, regardless of the slow ventricles.
            value += Bump(_pTime, 0.1, 0.035) * 0.15;
        }
        else if (_rhythm != MonitorRhythm.Svt)
        {
            value += Bump(t, 0.1, 0.03) * 0.14;
        }

        var wide = _rhythm == MonitorRhythm.HeartBlock ? 1.6 : 1.0;
        value += -Bump(t, 0.205, 0.008 * wide) * 0.12;
        value += Bump(t, 0.225, 0.011 * wide) * 1.0;
        value += -Bump(t, 0.248, 0.01 * wide) * 0.28;
        value += Bump(t, 0.42, 0.055) * 0.26;
        return (float)(value + noise);
    }

    /// <summary>The pulse oximeter's wave: a rise just after each beat, a dicrotic notch and a slow fall.</summary>
    private float PlethSample()
    {
        if (_rhythm is MonitorRhythm.Asystole or MonitorRhythm.VentricularFibrillation)
        {
            return (float)((_random.NextDouble() - 0.5) * 0.02);
        }

        var scale = Math.Min(1, _beatLength / 0.75);
        var t = _beatTime / scale;
        var rise = Bump(t, 0.38, 0.075) * 0.9;
        var notch = Bump(t, 0.56, 0.07) * 0.32;
        var weak = _obs is { OxygenSaturation: < 90 } ? 0.6 : 1.0;
        return (float)(((rise + notch) * weak) - 0.35);
    }

    // ------------------------------------------------------------------ drawing

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color("#05080C"));
        if (Patient is null || _obs is null || _news is null)
        {
            return;
        }

        var font = UiStyle.SemiBold;
        var bold = UiStyle.Bold;
        var arrest = _rhythm is MonitorRhythm.Asystole or MonitorRhythm.VentricularFibrillation;
        var flash = (int)(_clock * 2.5) % 2 == 0 && SilencedFor <= 0;

        // Top bar: who, where, alarms, time.
        const float top = 38;
        DrawRect(new Rect2(0, 0, size.X, top), Bar);
        var where = _bed is { } b ? $"Bed {b + 1} · " : string.Empty;
        var x = 14f;
        x = Text(font, $"{where}{Patient.FullName}, {Patient.Age}", new Vector2(x, 26), 18, Colors.White) + 18;
        x = Text(UiStyle.Medium, _room, new Vector2(x, 26), 15, new Color("#BFD3EA")) + 18;
        foreach (var (text, red) in Alarms(arrest))
        {
            var width = bold.GetStringSize(text, HorizontalAlignment.Left, -1, 15).X + 20;
            var box = new Rect2(x, 6, width, top - 12);
            var on = red ? flash || SilencedFor > 0 : true;
            DrawRect(box, red ? (on ? new Color("#D32F2F") : new Color("#7A1A1A")) : new Color("#F9C80E"));
            Text(bold, text, new Vector2(x + 10, 25), 15, red ? Colors.White : new Color("#111111"));
            x += width + 8;
            if (x > size.X - 220)
            {
                break;
            }
        }

        var clock = Simulation.Now.ToString("HH:mm");
        var clockText = $"{(Patient.Age < 16 ? "Paed" : "Adult")} · {clock}";
        Text(UiStyle.Medium, clockText, new Vector2(size.X - 14 - UiStyle.Medium.GetStringSize(clockText, HorizontalAlignment.Left, -1, 15).X, 25), 15, new Color("#BFD3EA"));

        const float bottom = 118;
        var wavesWidth = (float)Math.Round(size.X * 0.63);
        var bodyTop = top;
        var bodyHeight = size.Y - top - bottom;
        var row = bodyHeight / 4;

        // Grid lines.
        for (var i = 1; i <= 4; i++)
        {
            DrawLine(new Vector2(0, bodyTop + (row * i)), new Vector2(size.X, bodyTop + (row * i)), Grid, 1);
        }

        DrawLine(new Vector2(wavesWidth, bodyTop), new Vector2(wavesWidth, size.Y - bottom), Grid, 1);

        if (ShowTrends)
        {
            DrawTrends(new Rect2(0, bodyTop, wavesWidth, bodyHeight));
        }
        else
        {
            var rhythm = HospitalSimulation.RhythmName(_rhythm);
            Trace(_ecg, new Rect2(0, bodyTop, wavesWidth, row), Ecg, 0.55f, $"II  x1   {rhythm}", 2.4f);
            Trace(_ecg, new Rect2(0, bodyTop + row, wavesWidth, row), Ecg.Darkened(0.25f), 0.42f, "V5", 1.8f);
            Trace(_pleth, new Rect2(0, bodyTop + (row * 2), wavesWidth, row), Pleth, 0.5f, "Pleth", 2.4f);
            Trace(_resp, new Rect2(0, bodyTop + (row * 3), wavesWidth, row), Resp, 0.42f, "Resp  (impedance)", 2.4f);
        }

        // The big numbers.
        var n = _news;
        var numbersX = wavesWidth;
        var numbersW = size.X - wavesWidth;
        var hr = arrest ? "---" : _obs.HeartRate.ToString();
        var spo2 = arrest ? "---" : _obs.OxygenSaturation.ToString();
        var rr = arrest ? "---" : _obs.RespiratoryRate.ToString();
        Number(new Rect2(numbersX, bodyTop, numbersW, row), "HR  bpm", "120\n50", hr, Ecg, n.HeartRate, flash, 0);
        Number(new Rect2(numbersX, bodyTop + row, numbersW, row), "SpO₂  %", "100\n94", spo2, Pleth, n.OxygenSaturation, flash, 0, _obs.OnSupplementalOxygen ? $"PR {hr} · on oxygen" : $"PR {hr} · room air");
        Number(new Rect2(numbersX, bodyTop + (row * 2), numbersW, row), "RR  rpm", "24\n8", rr, Resp, n.RespiratoryRate, flash, 0);
        var last = Patient.LatestObservations;
        var bp = last?.Obs ?? _obs;
        var map = (bp.SystolicBp + (2 * bp.DiastolicBp)) / 3;
        var when = Patient.LastObservationsAt is { } at ? $"Last cuff {at:HH:mm}" : "Cuff";
        Number(new Rect2(numbersX, bodyTop + (row * 3), numbersW, row), "NIBP  mmHg", when, arrest ? "---" : $"{bp.SystolicBp}/{bp.DiastolicBp}", Nibp, n.SystolicBp, flash, 0, arrest ? null : $"({map})");

        // Bottom row: temperature, ACVPU, pain and NEWS2.
        var by = size.Y - bottom;
        var cells = new[] { 1f, 1f, 1f, 1.5f };
        var unit = size.X / cells.Sum();
        var cx = 0f;
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = new Rect2(cx, by, unit * cells[i], bottom);
            DrawLine(new Vector2(cx, by), new Vector2(cx, size.Y), Grid, 1);
            switch (i)
            {
                case 0:
                    Small(cell, "Temp  °C", (last?.Obs ?? _obs).Temperature.ToString("0.0"), Temp, n.Temperature >= 2 && flash);
                    break;
                case 1:
                    Small(cell, "ACVPU", ConsciousnessText(_obs.Consciousness), Colors.White, n.Consciousness >= 3 && flash, 40);
                    break;
                case 2:
                    Small(cell, "Pain", $"{_obs.PainScore}/10", Pain, false);
                    break;
                default:
                    News2Cell(cell, n);
                    break;
            }

            cx += cell.Size.X;
        }

        DrawLine(new Vector2(0, by), new Vector2(size.X, by), Grid, 1);
    }

    private static string ConsciousnessText(Consciousness c) => c switch
    {
        Consciousness.Alert => "Alert",
        Consciousness.NewConfusion => "Confused",
        Consciousness.Voice => "Voice",
        Consciousness.Pain => "Pain",
        _ => "Unresp.",
    };

    /// <summary>The alarm messages for the top bar: red for a NEWS2 part scoring 3, yellow for 2.</summary>
    private IEnumerable<(string Text, bool Red)> Alarms(bool arrest)
    {
        if (arrest)
        {
            yield return (_rhythm == MonitorRhythm.Asystole ? "*** ASYSTOLE" : "*** VFIB / VTACH", true);
            yield break;
        }

        var n = _news!;
        var o = _obs!;
        if (n.RespiratoryRate >= 2)
        {
            yield return ($"{(n.RespiratoryRate >= 3 ? "***" : "**")} RESP RATE {(o.RespiratoryRate > 20 ? "HIGH" : "LOW")} {o.RespiratoryRate}", n.RespiratoryRate >= 3);
        }

        if (n.OxygenSaturation >= 2)
        {
            yield return ($"{(n.OxygenSaturation >= 3 ? "***" : "**")} SpO₂ LOW {o.OxygenSaturation}", n.OxygenSaturation >= 3);
        }

        if (n.HeartRate >= 2)
        {
            yield return ($"{(n.HeartRate >= 3 ? "***" : "**")} HR {(o.HeartRate > 90 ? "HIGH" : "LOW")} {o.HeartRate}", n.HeartRate >= 3);
        }

        if (n.SystolicBp >= 2)
        {
            yield return ($"{(n.SystolicBp >= 3 ? "***" : "**")} NIBP SYS {(o.SystolicBp > 200 ? "HIGH" : "LOW")} {o.SystolicBp}", n.SystolicBp >= 3);
        }

        if (n.Temperature >= 2)
        {
            yield return ($"** TEMP {(o.Temperature > 38 ? "HIGH" : "LOW")} {o.Temperature:0.0}", false);
        }

        if (_rhythm == MonitorRhythm.AtrialFibrillation)
        {
            yield return ("** IRREGULAR HR", false);
        }
    }

    private float Text(Font font, string text, Vector2 at, int size, Color colour)
    {
        DrawString(font, at, text, HorizontalAlignment.Left, -1, size, colour);
        return at.X + font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
    }

    /// <summary>A sweeping trace: the newest samples are written at the cursor, with a gap just ahead of it.</summary>
    private void Trace(float[] samples, Rect2 area, Color colour, float gain, string label, float width)
    {
        var count = samples.Length;
        var mid = area.Position.Y + (area.Size.Y * 0.58f);
        var amplitude = area.Size.Y * gain;
        var gap = SampleRate / 8;
        var points = new List<Vector2>();
        for (var i = 0; i < count; i++)
        {
            var ahead = (i - _cursor + count) % count;
            if (ahead < gap)
            {
                if (points.Count > 1)
                {
                    DrawPolyline(points.ToArray(), colour, width, true);
                }

                points.Clear();
                continue;
            }

            points.Add(new Vector2(area.Position.X + (area.Size.X * i / (count - 1)), mid - (samples[i] * amplitude)));
        }

        if (points.Count > 1)
        {
            DrawPolyline(points.ToArray(), colour, width, true);
        }

        var font = UiStyle.SemiBold;
        var size = font.GetStringSize(label, HorizontalAlignment.Left, -1, 15);
        DrawRect(new Rect2(area.Position + new Vector2(6, 4), size + new Vector2(8, 4)), new Color("#05080C"));
        DrawString(font, area.Position + new Vector2(10, 21), label, HorizontalAlignment.Left, -1, 15, colour);
    }

    private void Number(Rect2 box, string label, string limits, string value, Color colour, int score, bool flash, int unused, string? extra = null)
    {
        if (score >= 3)
        {
            DrawRect(box.Grow(-1), flash ? new Color(0.83f, 0.18f, 0.18f, 0.42f) : new Color(0.83f, 0.18f, 0.18f, 0.22f));
        }
        else if (score == 2)
        {
            DrawRect(box.Grow(-1), new Color(0.98f, 0.78f, 0.05f, 0.14f));
        }

        var font = UiStyle.SemiBold;
        DrawString(font, box.Position + new Vector2(16, 26), label, HorizontalAlignment.Left, -1, 16, colour);
        var y = 46f;
        foreach (var line in limits.Split('\n'))
        {
            DrawString(UiStyle.Medium, box.Position + new Vector2(16, y), line, HorizontalAlignment.Left, -1, 12, new Color(colour, 0.8f));
            y += 15;
        }

        if (extra is not null)
        {
            var w = UiStyle.Medium.GetStringSize(extra, HorizontalAlignment.Left, -1, 15).X;
            DrawString(UiStyle.Medium, box.Position + new Vector2(box.Size.X - w - 18, 26), extra, HorizontalAlignment.Left, -1, 15, colour);
        }

        var big = (int)Math.Min(box.Size.Y * 0.78f, value.Length > 4 ? 74 : 104);
        var size = font.GetStringSize(value, HorizontalAlignment.Left, -1, big);
        DrawString(font, new Vector2(box.End.X - size.X - 18, box.End.Y - 12), value, HorizontalAlignment.Left, -1, big, colour);
    }

    private void Small(Rect2 cell, string label, string value, Color colour, bool alarm, int big = 52)
    {
        if (alarm)
        {
            DrawRect(cell.Grow(-1), new Color(0.83f, 0.18f, 0.18f, 0.35f));
        }

        DrawString(UiStyle.SemiBold, cell.Position + new Vector2(14, 24), label, HorizontalAlignment.Left, -1, 15, colour);
        var size = UiStyle.SemiBold.GetStringSize(value, HorizontalAlignment.Left, -1, big);
        DrawString(UiStyle.SemiBold, new Vector2(cell.End.X - size.X - 14, cell.End.Y - 16), value, HorizontalAlignment.Left, -1, big, colour);
    }

    private void News2Cell(Rect2 cell, News2Score n)
    {
        var (back, ink, risk) = n.Risk switch
        {
            News2Risk.High => (new Color("#D32F2F"), Colors.White, "high risk"),
            News2Risk.Medium => (new Color("#EF6C00"), Colors.White, "medium risk"),
            News2Risk.LowMedium => (new Color("#F9A825"), new Color("#2B1D00"), "low–medium risk"),
            _ => (new Color("#2E7D32"), Colors.White, "low risk"),
        };
        DrawRect(cell, back);
        DrawString(UiStyle.SemiBold, cell.Position + new Vector2(14, 24), $"NEWS2 · {risk}", HorizontalAlignment.Left, -1, 15, ink);
        var value = n.Total.ToString();
        var size = UiStyle.Bold.GetStringSize(value, HorizontalAlignment.Left, -1, 64);
        DrawString(UiStyle.Bold, new Vector2(cell.End.X - size.X - 16, cell.End.Y - 14), value, HorizontalAlignment.Left, -1, 64, ink);

        // A sparkline of the last dozen sets of obs.
        var history = Patient!.ObservationHistory.TakeLast(12).Select(h => h.News2.Total).ToList();
        history.Add(n.Total);
        if (history.Count > 1)
        {
            var area = new Rect2(cell.Position + new Vector2(14, 40), new Vector2(cell.Size.X - size.X - 50, cell.Size.Y - 58));
            var points = history.Select((v, i) => new Vector2(area.Position.X + (area.Size.X * i / (history.Count - 1)), area.End.Y - (area.Size.Y * Math.Min(v, 12) / 12f))).ToArray();
            DrawPolyline(points, ink, 3, true);
            DrawCircle(points[^1], 4, ink);
        }
    }

    /// <summary>Trends: each vital sign over the patient's recorded obs, in place of the live traces.</summary>
    private void DrawTrends(Rect2 area)
    {
        var history = Patient!.ObservationHistory.TakeLast(24).ToList();
        var rows = new (string Label, Color Colour, Func<Observations, double> Value, double Min, double Max)[]
        {
            ("HR", Ecg, o => o.HeartRate, 30, 180),
            ("SpO₂", Pleth, o => o.OxygenSaturation, 80, 100),
            ("RR", Resp, o => o.RespiratoryRate, 5, 40),
            ("Sys BP", Nibp, o => o.SystolicBp, 60, 200),
        };
        var rowHeight = area.Size.Y / rows.Length;
        for (var r = 0; r < rows.Length; r++)
        {
            var box = new Rect2(area.Position.X + 110, area.Position.Y + (r * rowHeight) + 14, area.Size.X - 130, rowHeight - 28);
            var (label, colour, value, min, max) = rows[r];
            DrawString(UiStyle.SemiBold, new Vector2(area.Position.X + 12, box.Position.Y + 22), label, HorizontalAlignment.Left, -1, 16, colour);
            if (history.Count < 2)
            {
                DrawString(UiStyle.Medium, new Vector2(box.Position.X, box.Position.Y + 22), "Not enough obs yet", HorizontalAlignment.Left, -1, 14, new Color(colour, 0.7f));
                continue;
            }

            var points = history.Select((h, i) => new Vector2(box.Position.X + (box.Size.X * i / (history.Count - 1)), box.End.Y - (float)(box.Size.Y * Math.Clamp((value(h.Obs) - min) / (max - min), 0, 1)))).ToArray();
            DrawPolyline(points, colour, 2.5f, true);
            foreach (var p in points)
            {
                DrawCircle(p, 3, colour);
            }

            DrawString(UiStyle.Medium, new Vector2(box.Position.X, box.End.Y + 14), history[0].Time.ToString("HH:mm"), HorizontalAlignment.Left, -1, 12, new Color("#7F8C99"));
            var end = history[^1].Time.ToString("HH:mm");
            DrawString(UiStyle.Medium, new Vector2(box.End.X - 34, box.End.Y + 14), end, HorizontalAlignment.Left, -1, 12, new Color("#7F8C99"));
        }
    }
}
