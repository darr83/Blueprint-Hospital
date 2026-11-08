using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// In-game heads-up display in the "Clinical light" style. Top bar: hospital, clock and speed, balance
/// and menu. Left: navigation (board, build, patients, staff, finances, routes, grid). Along the top of
/// the map: the headline A&amp;E figures. Left column: the A&amp;E board and notifications. Right: a docked
/// card for the selected patient, member of staff or room, or the open list. Bottom right: zoom and rotate.
/// </summary>
public partial class MainHUD : CanvasLayer
{
    public const float TopBarHeight = 60;
    public const float NavWidth = 84;
    private const float Gap = 16;

    private readonly Label _dateLabel = UiStyle.Caption(string.Empty, 12);
    private readonly Label _timeLabel = new();
    private readonly Label _hoverLabel = new();
    private readonly Label _tagLabel = new();
    private readonly PanelContainer _tag = new() { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
    private readonly Button _balanceButton = new() { FocusMode = Control.FocusModeEnum.None };
    private readonly Dictionary<SimulationSpeed, Button> _speedButtons = new();
    private readonly Dictionary<string, Button> _nav = new();
    private readonly VBoxContainer _left = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly VBoxContainer _mapControls = new();
    private GameSession _session = null!;
    private IDisposable? _speedSubscription;
    private bool _routesShown;
    private bool _gridShown;
    private bool _rosterShowsStaff;
    private double _statsRefresh;

    private (PanelContainer Card, Label Value) _inDept;
    private (PanelContainer Card, Label Value) _waiting;
    private (PanelContainer Card, Label Value) _longest;
    private (PanelContainer Card, Label Value) _fourHour;
    private (PanelContainer Card, Label Value) _beds;
    private (PanelContainer Card, Label Value) _staffOn;

    /// <summary>Raised when the player clicks the grid toggle.</summary>
    public event Action? GridToggled;

    /// <summary>Raised when the player clicks the routes toggle.</summary>
    public event Action? RoutesToggled;

    /// <summary>Raised when the player opens the menu from the top bar.</summary>
    public event Action? MenuRequested;

    /// <summary>Raised by the zoom buttons: above 1 zooms in, below 1 zooms out.</summary>
    public event Action<float>? ZoomRequested;

    /// <summary>Raised by the rotate buttons: -1 anticlockwise (Q), +1 clockwise (E).</summary>
    public event Action<int>? RotateRequested;

    /// <summary>Where the selected person is in the world and their name, for the tag above their head.</summary>
    public Func<(Vector2 World, string Name)?>? SelectionAnchor { get; set; }

    public DepartmentDashboard Dashboard { get; } = new() { Name = "Dashboard" };

    public NotificationFeed Feed { get; } = new() { Name = "Notifications" };

    public PatientRecordPanel Record { get; } = new() { Name = "PatientRecord", Visible = false };

    public StaffPanel StaffInfo { get; } = new() { Name = "StaffInfo", Visible = false };

    public RosterPanel Roster { get; } = new() { Name = "Roster", Visible = false };

    public BuildPanel Build { get; } = new() { Name = "Build", Visible = false };

    public RoomPanel RoomInfo { get; } = new() { Name = "RoomInfo", Visible = false };

    public FinancePanel Finances { get; } = new() { Name = "Finances", Visible = false };

    public RecruitmentPanel Recruitment { get; } = new() { Name = "Recruitment", Visible = false };

    /// <summary>True while the A&amp;E board and notifications column is showing.</summary>
    public bool BoardShown => _left.Visible;

    private IEnumerable<Control> RightPanels => new Control[] { Record, StaffInfo, Roster, Build, RoomInfo, Finances, Recruitment };

    /// <summary>Highlights the Routes button while everyone's routes are shown.</summary>
    public void SetRoutesShown(bool shown) => _routesShown = shown;

    /// <summary>Highlights the Grid button while the grid is shown.</summary>
    public void SetGridShown(bool shown) => _gridShown = shown;

    /// <summary>Closes every right-hand panel.</summary>
    public void HideRightPanels()
    {
        Record.Visible = StaffInfo.Visible = Roster.Visible = Build.Visible = Finances.Visible = Recruitment.Visible = false;
        RoomInfo.Close();
    }

    /// <summary>Opens the finances, or closes them.</summary>
    public void ToggleFinances()
    {
        var show = !Finances.Visible;
        HideRightPanels();
        Finances.Visible = show;
    }

    /// <summary>Opens recruitment, or closes it.</summary>
    public void ToggleRecruitment()
    {
        var show = !Recruitment.Visible;
        HideRightPanels();
        Recruitment.Visible = show;
        if (show)
        {
            Recruitment.Refresh();
        }
    }

    /// <summary>Opens the build menu, or closes it.</summary>
    public void ToggleBuild()
    {
        var show = !Build.Visible;
        HideRightPanels();
        Build.Visible = show;
    }

    /// <summary>Shows one room's details, closing the other right-hand panels.</summary>
    public void ShowRoom(BlueprintHospital.Simulation.Hospital.CareSpace room)
    {
        HideRightPanels();
        RoomInfo.Show(room);
    }

    /// <summary>Shows a member of staff's panel, closing the other right-hand panels.</summary>
    public void ShowStaff(StaffMember member)
    {
        HideRightPanels();
        StaffInfo.Show(member);
    }

    /// <summary>Opens the patient or staff list, or closes it if that tab is already showing.</summary>
    public void ToggleRoster(bool staff)
    {
        var show = !(Roster.Visible && _rosterShowsStaff == staff);
        HideRightPanels();
        Roster.Visible = show;
        _rosterShowsStaff = staff;
        Roster.ShowTab(staff);
    }

    /// <summary>Shows or hides the A&amp;E board and notifications.</summary>
    public void ToggleBoard() => _left.Visible = !_left.Visible;

    public void Initialise(GameSession session, BlueprintHospital.Hospital.BuildTool tool)
    {
        _session = session;
        Layer = 10;
        ProcessMode = ProcessModeEnum.Always;

        var root = new Control { Theme = UiStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(BuildTag());
        root.AddChild(BuildTopBar());
        root.AddChild(BuildNav());
        root.AddChild(BuildStatCards());
        root.AddChild(BuildHoverPanel());
        root.AddChild(BuildMapControls());

        var sim = session.Simulation;
        _left.OffsetLeft = NavWidth + Gap;
        _left.OffsetTop = TopBarHeight + Gap + 82;
        _left.AnchorBottom = 1;
        _left.OffsetBottom = -Gap;
        _left.AddThemeConstantOverride("separation", 10);
        Dashboard.Initialise(sim);
        Feed.Initialise(sim, session.Events);
        Dashboard.CustomMinimumSize = new Vector2(330, 0);
        Feed.CustomMinimumSize = new Vector2(330, 120);
        Feed.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _left.AddChild(Dashboard);
        _left.AddChild(Feed);
        root.AddChild(_left);

        Record.Initialise(sim);
        DockRight(Record, 420);
        root.AddChild(Record);
        StaffInfo.Initialise(sim);
        DockRight(StaffInfo, 400);
        root.AddChild(StaffInfo);
        Roster.Initialise(sim);
        DockRight(Roster, 360);
        root.AddChild(Roster);
        Build.Initialise(sim, tool);
        DockRight(Build, 452);
        root.AddChild(Build);
        RoomInfo.Initialise(sim);
        DockRight(RoomInfo, 380);
        root.AddChild(RoomInfo);
        Finances.Initialise(sim);
        DockRight(Finances, 452);
        root.AddChild(Finances);
        Recruitment.Initialise(sim);
        DockRight(Recruitment, 440);
        root.AddChild(Recruitment);

        _speedSubscription = session.Events.Subscribe<SimulationSpeedChanged>(_ => RefreshSpeedButtons());
        RefreshSpeedButtons();
        RefreshClock();
        RefreshStats();
    }

    public override void _ExitTree() => _speedSubscription?.Dispose();

    public override void _Process(double delta)
    {
        RefreshClock();
        RefreshNav();
        RefreshTag();

        // Keep the zoom buttons clear of whichever card is docked on the right.
        var open = RightPanels.FirstOrDefault(p => p.Visible);
        _mapControls.OffsetRight = open is null ? -Gap : open.OffsetLeft - Gap;
        _mapControls.OffsetLeft = _mapControls.OffsetRight - 44;

        _statsRefresh -= delta;
        if (_statsRefresh <= 0)
        {
            _statsRefresh = 0.5;
            RefreshStats();
        }
    }

    public void ShowHoveredTile(int x, int y, bool onSite, SiteMap site)
    {
        if (!onSite)
        {
            _hoverLabel.Text = "Off site";
            return;
        }

        var tile = new GridCoord(x, y);
        var building = site.BuildingAt(tile);
        var where = building is null ? Describe(site[tile]) : building.Name;
        foreach (var space in _session.Simulation.Spaces)
        {
            if (space.Rect.Contains(tile) && space.Kind != BlueprintHospital.Simulation.Hospital.CareSpaceKind.Corridor)
            {
                where = $"{space.Name} ({space.AreaSquareMetres:0} m²)";
            }
        }

        _hoverLabel.Text = $"{where}   ·   {Units.TilesToMetres(x):0.0} m, {Units.TilesToMetres(y):0.0} m";
    }

    // ---------------------------------------------------------------- top bar

    private Control BuildTopBar()
    {
        var bar = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        bar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        bar.CustomMinimumSize = new Vector2(0, TopBarHeight);
        var style = UiStyle.Box(UiStyle.Surface, 0);
        style.BorderColor = UiStyle.Border;
        style.BorderWidthBottom = 1;
        style.ContentMarginLeft = 20;
        style.ContentMarginRight = 20;
        bar.AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        bar.AddChild(row);

        // Logo and name.
        var brand = new HBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
        brand.AddThemeConstantOverride("separation", 10);
        var logo = new PanelContainer { CustomMinimumSize = new Vector2(32, 32), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        logo.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Blue, 8));
        var cross = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        cross.AddThemeColorOverride("font_color", Colors.White);
        cross.AddThemeFontSizeOverride("font_size", 26);
        logo.AddChild(cross);
        brand.AddChild(logo);
        var names = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        names.AddThemeConstantOverride("separation", -2);
        var name = new Label { Text = _session.HospitalName };
        name.AddThemeFontSizeOverride("font_size", 16);
        names.AddChild(name);
        names.AddChild(_dateLabel);
        brand.AddChild(names);
        row.AddChild(brand);

        // Clock and speed, in one pill.
        var clock = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var clockStyle = UiStyle.Box(UiStyle.Ground, 10, UiStyle.Border);
        clockStyle.ContentMarginLeft = 14;
        clockStyle.ContentMarginRight = 4;
        clockStyle.ContentMarginTop = clockStyle.ContentMarginBottom = 4;
        clock.AddThemeStyleboxOverride("panel", clockStyle);
        var clockRow = new HBoxContainer();
        clockRow.AddThemeConstantOverride("separation", 10);
        _timeLabel.AddThemeFontSizeOverride("font_size", 18);
        _timeLabel.CustomMinimumSize = new Vector2(52, 0);
        _timeLabel.VerticalAlignment = VerticalAlignment.Center;
        clockRow.AddChild(_timeLabel);
        var speeds = new PanelContainer();
        var speedsStyle = UiStyle.Box(UiStyle.Surface, 8, UiStyle.Border);
        speedsStyle.ContentMarginLeft = speedsStyle.ContentMarginRight = speedsStyle.ContentMarginTop = speedsStyle.ContentMarginBottom = 2;
        speeds.AddThemeStyleboxOverride("panel", speedsStyle);
        var speedRow = new HBoxContainer();
        speedRow.AddThemeConstantOverride("separation", 2);
        speeds.AddChild(speedRow);
        AddSpeedButton(speedRow, "❚❚", SimulationSpeed.Paused, "Pause (Space)");
        AddSpeedButton(speedRow, "1x", SimulationSpeed.Normal, "Normal speed (1)");
        AddSpeedButton(speedRow, "2x", SimulationSpeed.Fast, "Fast (2)");
        AddSpeedButton(speedRow, "4x", SimulationSpeed.VeryFast, "Very fast (3)");
        clockRow.AddChild(speeds);
        clock.AddChild(clockRow);
        row.AddChild(clock);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _balanceButton.ThemeTypeVariation = UiStyle.Secondary;
        _balanceButton.CustomMinimumSize = new Vector2(0, 40);
        _balanceButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _balanceButton.AddThemeFontSizeOverride("font_size", 16);
        _balanceButton.TooltipText = "Money in the bank. Click for finances (F)";
        _balanceButton.Pressed += ToggleFinances;
        row.AddChild(_balanceButton);

        var menu = UiStyle.SecondaryButton("Menu", () => MenuRequested?.Invoke());
        menu.TooltipText = "Save, load, settings and quit (Esc)";
        menu.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        menu.FocusMode = Control.FocusModeEnum.None;
        row.AddChild(menu);

        return bar;
    }

    private void AddSpeedButton(HBoxContainer parent, string text, SimulationSpeed speed, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(42, 32),
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeStyleboxOverride("normal", UiStyle.Box(new Color(0, 0, 0, 0), 6));
        button.AddThemeStyleboxOverride("hover", UiStyle.Box(UiStyle.Ground, 6));
        button.AddThemeStyleboxOverride("pressed", UiStyle.Box(UiStyle.Blue, 6));
        button.AddThemeStyleboxOverride("hover_pressed", UiStyle.Box(UiStyle.BlueHover, 6));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_focus_color" })
        {
            button.AddThemeColorOverride(state, UiStyle.TextDark);
        }

        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += () => _session.Clock.SetSpeed(speed);
        _speedButtons[speed] = button;
        parent.AddChild(button);
    }

    // ---------------------------------------------------------------- navigation

    private Control BuildNav()
    {
        var nav = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        nav.AnchorBottom = 1;
        nav.OffsetTop = TopBarHeight;
        nav.OffsetRight = NavWidth;
        var style = UiStyle.Box(UiStyle.Surface, 0);
        style.BorderColor = UiStyle.Border;
        style.BorderWidthRight = 1;
        style.ContentMarginLeft = style.ContentMarginRight = 8;
        style.ContentMarginTop = style.ContentMarginBottom = 12;
        nav.AddThemeStyleboxOverride("panel", style);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        nav.AddChild(column);
        column.AddChild(NavButton("Hospital", NavIcon.Kind.Hospital, ToggleBoard, "Show or hide the A&E board and notifications"));
        column.AddChild(NavButton("Build", NavIcon.Kind.Build, ToggleBuild, "Build rooms and corridors, and extend the building (B)"));
        column.AddChild(NavButton("Patients", NavIcon.Kind.Patients, () => ToggleRoster(false), "Everyone in A&E, sickest first (P)"));
        column.AddChild(NavButton("Staff", NavIcon.Kind.Staff, () => ToggleRoster(true), "Who is on shift and what they are doing (T)"));
        column.AddChild(NavButton("Hire", NavIcon.Kind.Hire, ToggleRecruitment, "Recruitment: hire and let go of staff (H)"));
        column.AddChild(NavButton("Finances", NavIcon.Kind.Finances, ToggleFinances, "Money in and out (F)"));
        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        column.AddChild(NavButton("Routes", NavIcon.Kind.Routes, () => RoutesToggled?.Invoke(), "Show where patients and staff are walking (R)"));
        column.AddChild(NavButton("Grid", NavIcon.Kind.Grid, () => GridToggled?.Invoke(), "Show or hide the building grid (G)"));
        return nav;
    }

    private Button NavButton(string text, NavIcon.Kind kind, Action onPressed, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            ThemeTypeVariation = UiStyle.Nav,
            CustomMinimumSize = new Vector2(0, 62),
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
        };

        // Push the label down so the icon sits above it.
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
        {
            var box = (StyleBoxFlat)UiStyle.Theme.GetStylebox(state, UiStyle.Nav).Duplicate();
            box.ContentMarginTop = 32;
            box.ContentMarginBottom = 6;
            button.AddThemeStyleboxOverride(state, box);
        }

        var icon = new NavIcon(kind, button);
        icon.AnchorLeft = icon.AnchorRight = 0.5f;
        icon.OffsetLeft = -12;
        icon.OffsetRight = 12;
        icon.OffsetTop = 9;
        icon.OffsetBottom = 33;
        button.AddChild(icon);
        button.Pressed += onPressed;
        _nav[text] = button;
        return button;
    }

    private void RefreshNav()
    {
        void Set(string name, bool on)
        {
            var button = _nav[name];
            if (button.ButtonPressed != on)
            {
                button.SetPressedNoSignal(on);
                button.GetChild<NavIcon>(0).QueueRedraw();
            }
        }

        Set("Hospital", _left.Visible);
        Set("Build", Build.Visible);
        Set("Patients", Roster.Visible && !_rosterShowsStaff);
        Set("Staff", Roster.Visible && _rosterShowsStaff);
        Set("Hire", Recruitment.Visible);
        Set("Finances", Finances.Visible);
        Set("Routes", _routesShown);
        Set("Grid", _gridShown);
    }

    // ---------------------------------------------------------------- headline figures

    private Control BuildStatCards()
    {
        var row = new HBoxContainer { Position = new Vector2(NavWidth + Gap, TopBarHeight + Gap), MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        _inDept = StatCard(row, "In department", "Patients in A&E now");
        _waiting = StatCard(row, "Waiting", "Waiting to be triaged or for a cubicle");
        _longest = StatCard(row, "Longest wait", "The longest anyone has been waiting for a cubicle");
        _fourHour = StatCard(row, "Seen in 4 hours", "Patients who left A&E within four hours (the standard is 95%)");
        _beds = StatCard(row, "Ward beds free", "Empty beds on your wards");
        _staffOn = StatCard(row, "Staff on shift", "Everyone working now");
        return row;
    }

    private static (PanelContainer, Label) StatCard(HBoxContainer row, string caption, string tooltip)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(112, 0), TooltipText = tooltip, MouseFilter = Control.MouseFilterEnum.Stop };
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        var label = UiStyle.Caption(caption, 12);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        column.AddChild(label);
        var value = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        value.AddThemeFontSizeOverride("font_size", 22);
        column.AddChild(value);
        card.AddChild(column);
        row.AddChild(card);
        SetCardTone((card, value), Tone.Normal);
        return (card, value);
    }

    private enum Tone
    {
        Normal,
        Warning,
        Danger,
    }

    private static void SetCardTone((PanelContainer Card, Label Value) card, Tone tone)
    {
        var (background, border, text) = tone switch
        {
            Tone.Warning => (UiStyle.AmberTint, UiStyle.AmberBorder, UiStyle.AmberText),
            Tone.Danger => (new Color("#FDECEC"), new Color("#EBA5A8"), UiStyle.Red),
            _ => (UiStyle.Surface, UiStyle.Border, UiStyle.TextDark),
        };
        var box = UiStyle.Box(background, 12, border);
        box.ContentMarginLeft = box.ContentMarginRight = 14;
        box.ContentMarginTop = box.ContentMarginBottom = 8;
        card.Card.AddThemeStyleboxOverride("panel", box);
        card.Value.AddThemeColorOverride("font_color", text);
        var caption = card.Card.GetChild(0).GetChild<Label>(0);
        caption.AddThemeColorOverride("font_color", tone == Tone.Normal ? UiStyle.TextMuted : text);
    }

    private void RefreshStats()
    {
        var sim = _session.Simulation;
        var s = sim.Stats;
        _inDept.Value.Text = s.PatientsInDepartment.ToString();
        _waiting.Value.Text = (s.AwaitingTriage + s.WaitingForSpace.Values.Sum()).ToString();

        var longest = s.LongestWaitMinutes.Values.DefaultIfEmpty(0).Max();
        _longest.Value.Text = longest < 1 ? "None" : ClinicalText.Duration(TimeSpan.FromMinutes(longest));
        SetCardTone(_longest, longest >= 240 ? Tone.Danger : longest >= 120 ? Tone.Warning : Tone.Normal);

        var four = s.FourHourPerformance;
        _fourHour.Value.Text = $"{four:P0}";
        SetCardTone(_fourHour, four >= 0.95 ? Tone.Normal : four >= 0.76 ? Tone.Warning : Tone.Danger);

        var (used, total) = s.WardBeds;
        _beds.Value.Text = total == 0 ? "No wards" : $"{total - used} of {total}";
        SetCardTone(_beds, total > 0 && used >= total ? Tone.Warning : Tone.Normal);

        _staffOn.Value.Text = sim.Staff.Count().ToString();
    }

    // ---------------------------------------------------------------- map furniture

    private Control BuildMapControls()
    {
        _mapControls.AnchorLeft = _mapControls.AnchorRight = 1;
        _mapControls.AnchorTop = _mapControls.AnchorBottom = 1;
        _mapControls.GrowVertical = Control.GrowDirection.Begin;
        _mapControls.OffsetBottom = -Gap;
        _mapControls.OffsetTop = -Gap;
        _mapControls.AddThemeConstantOverride("separation", 6);
        _mapControls.AddChild(MapButton("+", "Zoom in (mouse wheel)", () => ZoomRequested?.Invoke(1.25f)));
        _mapControls.AddChild(MapButton("−", "Zoom out (mouse wheel)", () => ZoomRequested?.Invoke(0.8f)));
        _mapControls.AddChild(MapButton("⟲", "Turn the view anticlockwise (Q)", () => RotateRequested?.Invoke(-1)));
        _mapControls.AddChild(MapButton("⟳", "Turn the view clockwise (E)", () => RotateRequested?.Invoke(1)));
        return _mapControls;
    }

    private static Button MapButton(string text, string tooltip, Action onPressed)
    {
        var button = UiStyle.SecondaryButton(text, onPressed);
        button.CustomMinimumSize = new Vector2(44, 44);
        button.TooltipText = tooltip;
        button.FocusMode = Control.FocusModeEnum.None;
        button.AddThemeFontSizeOverride("font_size", 20);
        foreach (var state in new[] { "normal", "hover", "pressed" })
        {
            var box = (StyleBoxFlat)UiStyle.Theme.GetStylebox(state, UiStyle.Secondary).Duplicate();
            box.ContentMarginLeft = box.ContentMarginRight = 0;
            box.ContentMarginTop = box.ContentMarginBottom = 0;
            box.ShadowColor = new Color(0.09f, 0.14f, 0.18f, 0.12f);
            box.ShadowSize = 6;
            box.ShadowOffset = new Vector2(0, 2);
            button.AddThemeStyleboxOverride(state, box);
        }

        return button;
    }

    private Control BuildTag()
    {
        var box = UiStyle.Box(UiStyle.Blue, 8);
        box.ContentMarginLeft = box.ContentMarginRight = 10;
        box.ContentMarginTop = box.ContentMarginBottom = 4;
        box.ShadowColor = new Color(0.09f, 0.14f, 0.18f, 0.25f);
        box.ShadowSize = 6;
        _tag.AddThemeStyleboxOverride("panel", box);
        _tagLabel.AddThemeColorOverride("font_color", Colors.White);
        _tagLabel.AddThemeFontSizeOverride("font_size", 13);
        _tag.AddChild(_tagLabel);
        return _tag;
    }

    /// <summary>Keeps the name tag floating above the selected person's head.</summary>
    private void RefreshTag()
    {
        if (SelectionAnchor?.Invoke() is not { } anchor)
        {
            _tag.Visible = false;
            return;
        }

        var canvas = GetViewport().GetCanvasTransform();
        var zoom = canvas.Scale.Y;
        var foot = canvas * anchor.World;
        _tagLabel.Text = anchor.Name;
        _tag.Size = _tag.GetCombinedMinimumSize();
        _tag.Position = new Vector2(foot.X - (_tag.Size.X / 2), foot.Y - (78 * zoom) - _tag.Size.Y);
        _tag.Visible = true;
    }

    private Control BuildHoverPanel()
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        panel.GrowVertical = Control.GrowDirection.Begin;
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.OffsetBottom = -Gap;
        panel.OffsetTop = -Gap;
        var box = UiStyle.Box(new Color(1, 1, 1, 0.94f), 18, UiStyle.Border);
        box.ContentMarginLeft = box.ContentMarginRight = 14;
        box.ContentMarginTop = box.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", box);
        _hoverLabel.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        _hoverLabel.AddThemeFontSizeOverride("font_size", 13);
        _hoverLabel.Text = "Click a person or room to inspect · B: build · Middle mouse: move · Wheel: zoom · Q/E: turn · WASD: pan";
        panel.AddChild(_hoverLabel);
        return panel;
    }

    private static void DockRight(Control panel, float width)
    {
        panel.AnchorLeft = 1;
        panel.AnchorRight = 1;
        panel.AnchorTop = 0;
        panel.AnchorBottom = 1;
        panel.OffsetLeft = -width - Gap;
        panel.OffsetRight = -Gap;
        panel.OffsetTop = TopBarHeight + Gap;
        panel.OffsetBottom = -Gap;
    }

    private void RefreshSpeedButtons()
    {
        foreach (var (speed, button) in _speedButtons)
        {
            button.SetPressedNoSignal(speed == _session.Clock.Speed);
        }
    }

    private void RefreshClock()
    {
        var balance = _session.Simulation.Balance;
        _balanceButton.Text = $"£{balance:N0}";
        var colour = balance < 0 ? UiStyle.Red : UiStyle.Green;
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
        {
            _balanceButton.AddThemeColorOverride(state, colour);
        }

        var now = _session.Clock.Now;
        _dateLabel.Text = now.ToString("dddd d MMMM yyyy");
        _timeLabel.Text = now.ToString("HH:mm");
    }

    private static string Describe(TileKind kind) => kind switch
    {
        TileKind.Grass => "Grounds",
        TileKind.Paving => "Footpath",
        TileKind.Road => "Road",
        TileKind.AmbulanceBay => "Ambulance bay",
        TileKind.Parking => "Car park",
        TileKind.BuildingFloor => "Building",
        _ => kind.ToString(),
    };
}
