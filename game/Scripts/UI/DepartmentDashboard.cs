using System;
using System.Collections.Generic;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The A&amp;E board: red, amber and green tiles for who is in the department, then cubicle and
/// ward capacity bars, ambulances, the headline performance figures and totals since opening.
/// </summary>
public partial class DepartmentDashboard : PanelContainer
{
    // Tile colours: a shade deeper than the triage colours so white numbers read clearly on them.
    private static readonly Color RedTile = new("#D32F2F");
    private static readonly Color AmberTile = new("#F9A825");
    private static readonly Color GreenTile = new("#2E7D32");
    private static readonly Color AmberInk = new("#2B1D00");
    private static readonly Color Section = new("#F5F7FA");
    private static readonly Color EmptySlot = new("#DCE3E9");

    private readonly Dictionary<TriageCategory, (RichTextLabel Count, RichTextLabel Detail)> _tiles = new();
    private readonly Dictionary<string, (CapacityBar Bar, RichTextLabel Figure)> _capacity = new();
    private readonly Dictionary<string, RichTextLabel> _stats = new();
    private RichTextLabel _inDepartment = null!;
    private RichTextLabel _closed = null!;
    private RichTextLabel _triage = null!;
    private RichTextLabel _ambulances = null!;
    private RichTextLabel _handover = null!;
    private RichTextLabel _bedWait = null!;
    private RichTextLabel _footer = null!;
    private Control _wardRow = null!;
    private HospitalSimulation _sim = null!;
    private double _refresh;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 10);
        AddChild(column);

        var header = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        header.AddChild(Text("[b]A&E board[/b]", 18, expand: true));
        _inDepartment = Text(string.Empty, 13, UiStyle.TextMuted);
        _inDepartment.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        header.AddChild(_inDepartment);
        column.AddChild(header);

        _closed = Text(string.Empty, 13, UiStyle.AmberText, wrap: true);
        column.AddChild(_closed);

        var tiles = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        tiles.AddThemeConstantOverride("h_separation", 8);
        foreach (var (category, colour, ink) in new[]
        {
            (TriageCategory.Red, RedTile, Colors.White),
            (TriageCategory.Amber, AmberTile, AmberInk),
            (TriageCategory.Green, GreenTile, Colors.White),
        })
        {
            tiles.AddChild(Tile(category, colour, ink));
        }

        column.AddChild(tiles);
        _triage = Text(string.Empty, 12, UiStyle.TextMuted);
        column.AddChild(_triage);

        var cubicles = SectionBox();
        var rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 6);
        var titles = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        titles.AddChild(Text("[b]CUBICLES[/b]", 11, UiStyle.TextMuted, expand: true));
        titles.AddChild(Text("[b]IN USE[/b]", 11, UiStyle.TextMuted));
        rows.AddChild(titles);
        rows.AddChild(CapacityRow("Resus", RedTile));
        rows.AddChild(CapacityRow("Majors", AmberTile));
        rows.AddChild(CapacityRow("Minors", GreenTile));
        _wardRow = CapacityRow("Ward", UiStyle.Blue);
        rows.AddChild(_wardRow);
        _bedWait = Text(string.Empty, 12, UiStyle.AmberText, wrap: true);
        rows.AddChild(_bedWait);
        cubicles.AddChild(rows);
        column.AddChild(cubicles);

        var ambulance = SectionBox();
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _ambulances = Text(string.Empty, 14, expand: true);
        _handover = Text(string.Empty, 13, UiStyle.TextMuted);
        _handover.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        line.AddChild(_ambulances);
        line.AddChild(_handover);
        ambulance.AddChild(line);
        column.AddChild(ambulance);

        var stats = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        stats.AddThemeConstantOverride("h_separation", 8);
        stats.AddChild(StatBox("four", "4-hour target", "95% standard"));
        stats.AddChild(StatBox("doctor", "To doctor", "average"));
        stats.AddChild(StatBox("accuracy", "Diagnosis", "accurate"));
        column.AddChild(stats);

        column.AddChild(new HSeparator { MouseFilter = MouseFilterEnum.Ignore });
        _footer = Text(string.Empty, 12, UiStyle.TextMuted, wrap: true);
        column.AddChild(_footer);
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh <= 0)
        {
            _refresh = 0.5;
            Refresh();
        }
    }

    private void Refresh()
    {
        var s = _sim.Stats;
        Set(_inDepartment, $"In department [b][color=#17232E]{s.PatientsInDepartment}[/color][/b]");

        var closed = _sim.ClosedReasons;
        _closed.Visible = closed.Count > 0;
        if (closed.Count > 0)
        {
            Set(_closed, $"[b]Closed to new patients.[/b] Needs {string.Join(", ", closed)}.");
        }

        foreach (var (category, (count, detail)) in _tiles)
        {
            Set(count, $"[b]{s.InDepartment.GetValueOrDefault(category)}[/b]");
            var waiting = s.WaitingForSpace.GetValueOrDefault(category);
            var longest = s.LongestWaitMinutes.GetValueOrDefault(category);
            var text = waiting == 0 ? "none waiting" : $"{waiting} waiting · {ClinicalText.Duration(TimeSpan.FromMinutes(longest))}";
            var urgent = waiting > 0 && (category == TriageCategory.Red || longest > 120);
            Set(detail, urgent ? $"[b]{text}[/b]" : text);
        }

        _triage.Visible = s.AwaitingTriage > 0;
        Set(_triage, $"{s.AwaitingTriage} awaiting triage");

        SetCapacity("Resus", s.Resus);
        SetCapacity("Majors", s.Majors);
        SetCapacity("Minors", s.Minors);
        _wardRow.Visible = s.WardBeds.Total > 0;
        SetCapacity("Ward", s.WardBeds);
        _bedWait.Visible = s.AwaitingBed > 0;
        Set(_bedWait, $"{s.AwaitingBed} waiting in A&E for a ward bed");

        var ambulanceHex = s.AmbulancesWaiting >= 3 ? ClinicalText.RedHex : "#17232E";
        Set(_ambulances, $"[color={ambulanceHex}]Ambulances waiting [b]{s.AmbulancesWaiting}[/b][/color]");
        Set(_handover, s.AverageHandoverMinutes > 0 ? $"handover {s.AverageHandoverMinutes:0} min" : string.Empty);

        var four = s.FourHourPerformance;
        var fourHex = four >= 0.95 ? "#2E7D32" : four >= 0.76 ? ClinicalText.AmberTextHex : ClinicalText.RedHex;
        Set(_stats["four"], $"[b][color={fourHex}]{four * 100:0}%[/color][/b]");
        Set(_stats["doctor"], $"[b]{s.AverageTimeToDoctorMinutes:0} min[/b]");
        Set(_stats["accuracy"], $"[b]{s.DiagnosticAccuracy * 100:0}%[/b]");

        var footer = $"[b][color=#17232E]Since opening[/color][/b] · {s.ArrivalsTotal} arrivals ({s.AmbulanceArrivalsTotal} by ambulance)\n" +
                     $"{s.Discharged} home · {s.Admitted} admitted · {s.Transferred} transferred · {s.LeftWithoutBeingSeen} left unseen · {s.Deaths} died";
        if (s.Admitted > 0)
        {
            footer += $"\nWards: {s.WardDischarges} home · {s.WardDeaths} died";
        }

        Set(_footer, footer);
    }

    private void SetCapacity(string name, (int Used, int Total) usage)
    {
        var (bar, figure) = _capacity[name];
        bar.Set(usage.Used, usage.Total);
        var full = usage.Total > 0 && usage.Used >= usage.Total;
        Set(figure, full ? $"[b][color={ClinicalText.AmberTextHex}]{usage.Used}/{usage.Total}[/color][/b]" : $"[b]{usage.Used}/{usage.Total}[/b]");
    }

    private static void Set(RichTextLabel label, string text)
    {
        if (label.Text != text)
        {
            label.Text = text;
        }
    }

    private PanelContainer Tile(TriageCategory category, Color colour, Color ink)
    {
        var tile = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        var box = UiStyle.Box(colour, 12);
        box.ContentMarginLeft = box.ContentMarginRight = 10;
        box.ContentMarginTop = 6;
        box.ContentMarginBottom = 8;
        tile.AddThemeStyleboxOverride("panel", box);
        var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", -2);
        stack.AddChild(Text($"[b]{category.ToString().ToUpperInvariant()}[/b]", 12, ink));
        var count = Text("0", 30, ink);
        stack.AddChild(count);
        var detail = Text(string.Empty, 11, ink);
        stack.AddChild(detail);
        tile.AddChild(stack);
        _tiles[category] = (count, detail);
        return tile;
    }

    private Control CapacityRow(string name, Color fill)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        var label = Text(name, 14);
        label.CustomMinimumSize = new Vector2(58, 0);
        row.AddChild(label);
        var bar = new CapacityBar(fill, EmptySlot) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(bar);
        var figure = Text(string.Empty, 14, align: HorizontalAlignment.Right);
        figure.CustomMinimumSize = new Vector2(44, 0);
        row.AddChild(figure);
        _capacity[name] = (bar, figure);
        return row;
    }

    private Control StatBox(string key, string title, string note)
    {
        var box = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        var style = UiStyle.Box(UiStyle.Surface, 12, UiStyle.Border);
        style.ContentMarginLeft = style.ContentMarginRight = 9;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 7;
        box.AddThemeStyleboxOverride("panel", style);
        var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", 0);
        stack.AddChild(Text(title, 11, UiStyle.TextMuted));
        var value = Text(string.Empty, 19);
        stack.AddChild(value);
        stack.AddChild(Text(note, 11, UiStyle.TextMuted));
        box.AddChild(stack);
        _stats[key] = value;
        return box;
    }

    private static PanelContainer SectionBox()
    {
        var box = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = UiStyle.Box(Section, 12);
        style.ContentMarginLeft = style.ContentMarginRight = 12;
        style.ContentMarginTop = style.ContentMarginBottom = 9;
        box.AddThemeStyleboxOverride("panel", style);
        return box;
    }

    private static RichTextLabel Text(string bbcode, int size, Color? colour = null, bool expand = false, bool wrap = false, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = expand || wrap ? SizeFlags.ExpandFill : SizeFlags.ShrinkBegin,
            HorizontalAlignment = align,
            Text = bbcode,
        };
        label.AddThemeColorOverride("default_color", colour ?? UiStyle.TextDark);
        label.AddThemeFontSizeOverride("normal_font_size", size);
        label.AddThemeFontSizeOverride("bold_font_size", size);
        return label;
    }
}

/// <summary>A row of rounded slots, one per bed or cubicle, filled for each in use (a single bar when there are many).</summary>
public partial class CapacityBar : Control
{
    private const float Height = 10;
    private const float Gap = 3;
    private const int MaxSlots = 12;
    private readonly Color _fill;
    private readonly Color _empty;
    private int _used;
    private int _total;

    public CapacityBar(Color fill, Color empty)
    {
        _fill = fill;
        _empty = empty;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(40, Height);
    }

    public CapacityBar()
        : this(Colors.Gray, Colors.LightGray)
    {
    }

    public void Set(int used, int total)
    {
        if (used == _used && total == _total)
        {
            return;
        }

        _used = used;
        _total = total;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var width = Size.X;
        if (_total <= 0)
        {
            Slot(0, width, _empty);
            return;
        }

        if (_total > MaxSlots)
        {
            Slot(0, width, _empty);
            var share = Math.Clamp(_used / (float)_total, 0, 1);
            if (share > 0)
            {
                Slot(0, Math.Max(Height, width * share), _fill);
            }

            return;
        }

        var slot = (width - (Gap * (_total - 1))) / _total;
        for (var i = 0; i < _total; i++)
        {
            Slot(i * (slot + Gap), slot, i < _used ? _fill : _empty);
        }
    }

    private void Slot(float x, float w, Color colour)
    {
        var box = UiStyle.Box(colour, (int)(Height / 2));
        DrawStyleBox(box, new Rect2(x, (Size.Y - Height) / 2, w, Height));
    }
}

/// <summary>Styles for the in-game panels: white cards and clickable list rows.</summary>
public static class HudStyle
{
    /// <summary>The card every in-game panel sits on (kept under its old name).</summary>
    public static StyleBoxFlat DarkPanel() => UiStyle.Card(16);

    public static StyleBoxFlat RowBox(Color colour) => new()
    {
        BgColor = colour,
        CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
    };

    /// <summary>A flat, left-aligned button for clickable list rows.</summary>
    public static Button RowButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.AddThemeStyleboxOverride("normal", RowBox(new Color("#F7F9FB")));
        button.AddThemeStyleboxOverride("hover", RowBox(UiStyle.BlueTint));
        button.AddThemeStyleboxOverride("pressed", RowBox(new Color("#D6E2F3")));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
        {
            button.AddThemeColorOverride(state, UiStyle.TextDark);
        }

        button.AddThemeFontSizeOverride("font_size", 14);
        button.Pressed += onPressed;
        return button;
    }
}
