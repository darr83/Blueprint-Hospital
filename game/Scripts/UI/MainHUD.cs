using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// In-game heads-up display. Top bar: hospital name, date and time, simulation speed.
/// Left: the A&amp;E board and notifications. Right: the selected patient's record, or the
/// patient and staff lists. Bottom: what is under the cursor.
/// </summary>
public partial class MainHUD : CanvasLayer
{
    private readonly Label _dateLabel = new();
    private readonly Label _timeLabel = new();
    private readonly Label _hoverLabel = new();
    private readonly System.Collections.Generic.Dictionary<SimulationSpeed, Button> _speedButtons = new();
    private GameSession _session = null!;
    private System.IDisposable? _speedSubscription;

    /// <summary>Raised when the player clicks the grid toggle.</summary>
    public event System.Action? GridToggled;

    /// <summary>Raised when the player opens the menu from the top bar.</summary>
    public event System.Action? MenuRequested;

    public DepartmentDashboard Dashboard { get; } = new() { Name = "Dashboard" };

    public NotificationFeed Feed { get; } = new() { Name = "Notifications" };

    public PatientRecordPanel Record { get; } = new() { Name = "PatientRecord", Visible = false };

    public RosterPanel Roster { get; } = new() { Name = "Roster", Visible = false };

    /// <summary>Opens the patient or staff list, or closes it if that tab is already showing.</summary>
    public void ToggleRoster(bool staff)
    {
        Record.Visible = false;
        Roster.Visible = !(Roster.Visible && _rosterShowsStaff == staff);
        _rosterShowsStaff = staff;
        Roster.ShowTab(staff);
    }

    private bool _rosterShowsStaff;

    public void Initialise(GameSession session)
    {
        _session = session;
        Layer = 10;
        ProcessMode = ProcessModeEnum.Always;

        var root = new Control { Theme = UiStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(BuildTopBar());
        root.AddChild(BuildHoverPanel());

        var sim = session.Simulation;
        var left = new VBoxContainer { Position = new Vector2(12, 58), MouseFilter = Control.MouseFilterEnum.Ignore };
        left.AddThemeConstantOverride("separation", 8);
        Dashboard.Initialise(sim);
        Feed.Initialise(sim, session.Events);
        left.AddChild(Dashboard);
        left.AddChild(Feed);
        root.AddChild(left);

        Record.Initialise(sim);
        DockRight(Record, 452);
        root.AddChild(Record);
        Roster.Initialise(sim);
        DockRight(Roster, 352);
        root.AddChild(Roster);

        _speedSubscription = session.Events.Subscribe<SimulationSpeedChanged>(_ => RefreshSpeedButtons());
        RefreshSpeedButtons();
        RefreshClock();
    }

    public override void _ExitTree()
    {
        _speedSubscription?.Dispose();
    }

    public override void _Process(double delta) => RefreshClock();

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

    private Control BuildTopBar()
    {
        var bar = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        bar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = UiStyle.TopBarBackground,
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 6, ContentMarginBottom = 6,
        });

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        bar.AddChild(row);

        var name = new Label { Text = _session.HospitalName };
        name.AddThemeFontSizeOverride("font_size", 20);
        name.AddThemeColorOverride("font_color", Colors.White);
        row.AddChild(name);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        foreach (var label in new[] { _dateLabel, _timeLabel })
        {
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeFontSizeOverride("font_size", 18);
            row.AddChild(label);
        }

        _timeLabel.CustomMinimumSize = new Vector2(60, 0);

        var speeds = new HBoxContainer();
        speeds.AddThemeConstantOverride("separation", 4);
        AddSpeedButton(speeds, "❚❚", SimulationSpeed.Paused, "Pause (Space)");
        AddSpeedButton(speeds, "1x", SimulationSpeed.Normal, "Normal speed (1)");
        AddSpeedButton(speeds, "2x", SimulationSpeed.Fast, "Fast (2)");
        AddSpeedButton(speeds, "4x", SimulationSpeed.VeryFast, "Very fast (3)");
        row.AddChild(speeds);

        row.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0) });
        var patients = UiStyle.Button("Patients", () => ToggleRoster(false), 96);
        patients.TooltipText = "Everyone in A&E, sickest first (P)";
        row.AddChild(patients);
        var staff = UiStyle.Button("Staff", () => ToggleRoster(true), 76);
        staff.TooltipText = "Who is on shift and what they are doing (T)";
        row.AddChild(staff);
        var grid = UiStyle.Button("Grid", () => GridToggled?.Invoke(), 70);
        grid.TooltipText = "Show or hide the building grid (G)";
        row.AddChild(grid);
        var menu = UiStyle.Button("Menu", () => MenuRequested?.Invoke(), 80);
        menu.TooltipText = "Save, load, settings and quit (Esc)";
        row.AddChild(menu);

        return bar;
    }

    private static void DockRight(Control panel, float width)
    {
        panel.AnchorLeft = 1;
        panel.AnchorRight = 1;
        panel.AnchorTop = 0;
        panel.AnchorBottom = 1;
        panel.OffsetLeft = -width;
        panel.OffsetRight = 0;
        panel.OffsetTop = 58;
        panel.OffsetBottom = -12;
    }

    private Control BuildHoverPanel()
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        panel.GrowVertical = Control.GrowDirection.Begin;
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.OffsetBottom = -12;
        panel.OffsetTop = -12;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(UiStyle.TopBarBackground, 0.85f),
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        _hoverLabel.AddThemeColorOverride("font_color", Colors.White);
        _hoverLabel.Text = "Click a person to inspect · Middle mouse: move · Wheel: zoom · WASD: pan";
        panel.AddChild(_hoverLabel);
        return panel;
    }

    private void AddSpeedButton(HBoxContainer parent, string text, SimulationSpeed speed, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(48, 34),
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
        };
        button.Pressed += () => _session.Clock.SetSpeed(speed);
        _speedButtons[speed] = button;
        parent.AddChild(button);
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
