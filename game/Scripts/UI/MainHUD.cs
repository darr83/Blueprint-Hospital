using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// In-game heads-up display in the "Clinical light" style. Top bar: hospital, clock and speed, balance
/// the latest notification (hover for the last five) and menu. Left: navigation (board, build, patients,
/// staff, finances, routes, grid). Along the top of the map: the headline A&amp;E figures. Left column: the
/// A&amp;E board. Right: a docked
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
    private readonly PanelContainer _pickTag = new() { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
    private readonly Label _pickName = new();
    private readonly Label _pickDetail = new();
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

    /// <summary>The walls button of the 3D game: walls cut down low so you can see into every room (X).</summary>
    public event Action? WallsToggled;

    /// <summary>Raised by the floor buttons: +1 up a floor (Page Up), -1 down (Page Down).</summary>
    public event Action<int>? FloorStepRequested;

    /// <summary>Raised by the "add floor" button: opens the Building tab of the build menu.</summary>
    public event Action? AddFloorRequested;

    private readonly Label _floorLabel = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private Button? _floorUp;
    private Button? _floorDown;

    /// <summary>Shows which floor is in view on the floor switcher, and greys out the buttons at the top and bottom.</summary>
    public void SetFloorShown(int floor, int floors)
    {
        _floorLabel.Text = SiteMap.FloorLabel(floor);
        _floorLabel.TooltipText = $"{SiteMap.FloorName(floor)} of {floors}";
        if (_floorUp is not null && _floorDown is not null)
        {
            _floorUp.Disabled = floor >= floors - 1;
            _floorDown.Disabled = floor <= 0;
        }
    }

    /// <summary>Where the selected person is in the world and their name, for the tag above their head.</summary>
    public Func<(Vector2 World, string Name)?>? SelectionAnchor { get; set; }

    public DepartmentDashboard Dashboard { get; } = new() { Name = "Dashboard" };

    public NotificationFeed Feed { get; } = new() { Name = "Notifications" };

    public PatientRecordPanel Record { get; } = new() { Name = "PatientRecord", Visible = false };

    public StaffPanel StaffInfo { get; } = new() { Name = "StaffInfo", Visible = false };

    public RosterPanel Roster { get; } = new() { Name = "Roster", Visible = false };

    /// <summary>The staff rota: day and night teams, breaks and moving staff between departments.</summary>
    public StaffRotaPanel Rota { get; } = new() { Name = "Rota", Visible = false };

    /// <summary>The Stats screen: every figure for the last 24 hours, 7 days, 28 days and all time.</summary>
    public StatsPanel StatsScreen { get; } = new() { Name = "Stats", Visible = false };

    /// <summary>The illness journal (J): every illness and how many patients were treated for each.</summary>
    public JournalPanel Journal { get; } = new() { Name = "Journal", Visible = false };

    /// <summary>The Surgery panel: the theatre board and the waiting list for operations.</summary>
    public SurgeryPanel Surgery { get; } = new() { Name = "Surgery", Visible = false };

    /// <summary>The build bar along the bottom, shown in Build mode.</summary>
    public BuildBar Build { get; } = new() { Name = "BuildBar", Visible = false };

    public RoomPanel RoomInfo { get; } = new() { Name = "RoomInfo", Visible = false };

    /// <summary>A clinic patient's card.</summary>
    public OutpatientPanel OutpatientInfo { get; } = new() { Name = "OutpatientInfo", Visible = false };

    /// <summary>The Decorate panel: a room's floor and wall finish.</summary>
    public DecoratePanel Decorate { get; } = new() { Name = "Decorate", Visible = false };

    public FinancePanel Finances { get; } = new() { Name = "Finances", Visible = false };

    /// <summary>The Calendar (K): the season, what is going round, the week ahead and the year at a glance.</summary>
    public CalendarPanel Calendar { get; } = new() { Name = "Calendar", Visible = false };

    /// <summary>The top bar's season pill (season, and flu when it is about); opens the Calendar.</summary>
    public Button SeasonPill { get; } = new() { Name = "SeasonPill", FocusMode = Control.FocusModeEnum.None };

    /// <summary>The top bar's hospital pressure badge (1 steady to 4 critical); opens the Calendar.</summary>
    public Button PressureBadge { get; } = new() { Name = "PressureBadge", FocusMode = Control.FocusModeEnum.None };

    public RecruitmentPanel Recruitment { get; } = new() { Name = "Recruitment", Visible = false };

    /// <summary>Be A Doctor: the doctors on shift, to pick one to play.</summary>
    public DoctorChooserPanel Doctors { get; } = new() { Name = "Doctors", Visible = false };

    /// <summary>The Career level badge in the top bar and its goals card (Career games only).</summary>
    public LevelBadge Level { get; } = new();

    /// <summary>The outpatient clinics' calendars.</summary>
    public ClinicsPanel Clinics { get; } = new() { Name = "Clinics", Visible = false };

    /// <summary>A bed (play mode) or a piece of furniture or equipment (build mode).</summary>
    public ItemPanel ItemInfo { get; } = new() { Name = "ItemInfo", Visible = false };

    /// <summary>The crash call banner and crash team panel, under the top bar while a cardiac arrest is going on.</summary>
    public CrashPanel Crash { get; } = new() { Name = "CrashPanel" };

    /// <summary>The major incident banner and card (standby, get ready, casualties, debrief).</summary>
    public MajorIncidentPanel Incident { get; } = new() { Name = "MajorIncidentPanel" };

    /// <summary>The big bedside monitor, opened by clicking a monitor in play mode.</summary>
    public MonitorPopup Monitor { get; } = new() { Name = "MonitorPopup" };

    /// <summary>The cheat command line, opened with Ctrl+C.</summary>
    public CheatConsole Cheats { get; } = new() { Name = "CheatConsole", Visible = false };

    /// <summary>Raised when the player picks Play or Build in the top bar, or presses the Build button.</summary>
    public event Action<BlueprintHospital.Hospital.GameMode>? ModeRequested;

    /// <summary>The mode the scene is in, shown on the Play / Build switch.</summary>
    public BlueprintHospital.Hospital.GameMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Build.Visible = value == BlueprintHospital.Hospital.GameMode.Build;
            if (value != BlueprintHospital.Hospital.GameMode.Build && Decorate.Visible)
            {
                Decorate.Close();
            }
        }
    }

    private BlueprintHospital.Hospital.GameMode _mode;

    private PanelContainer? _hoverPanel;

    /// <summary>True while the A&amp;E board column is showing.</summary>
    public bool BoardShown => _left.Visible;

    private IEnumerable<Control> RightPanels => new Control[] { Record, OutpatientInfo, StaffInfo, Roster, RoomInfo, Decorate, ItemInfo, Finances, Recruitment, Clinics, Doctors, Calendar };

    /// <summary>Highlights the Routes button while everyone's routes are shown.</summary>
    public void SetRoutesShown(bool shown) => _routesShown = shown;

    /// <summary>Highlights the Grid button while the grid is shown.</summary>
    public void SetGridShown(bool shown) => _gridShown = shown;

    /// <summary>Closes every right-hand panel.</summary>
    public void HideRightPanels()
    {
        Record.Visible = StaffInfo.Visible = Roster.Visible = Doctors.Visible = Finances.Visible = Calendar.Visible = Recruitment.Visible = Clinics.Visible = Rota.Visible = StatsScreen.Visible = Surgery.Visible = Journal.Visible = false;
        RoomInfo.Close();
        OutpatientInfo.Close();
        Decorate.Close();
        ItemInfo.Close();
    }

    /// <summary>Opens the finances, or closes them.</summary>
    public void ToggleFinances()
    {
        var show = !Finances.Visible;
        HideRightPanels();
        Finances.Visible = show;
    }

    /// <summary>Opens the Calendar, or closes it.</summary>
    public void ToggleCalendar()
    {
        var show = !Calendar.Visible;
        HideRightPanels();
        Calendar.Visible = show;
        if (show)
        {
            Calendar.Refresh();
        }
    }

    /// <summary>Opens the clinic calendars, or closes them.</summary>
    public void ToggleClinics()
    {
        var show = !Clinics.Visible;
        HideRightPanels();
        if (show)
        {
            Clinics.ShowToday();
        }
    }

    /// <summary>Opens the list of doctors to play, or closes it.</summary>
    public void ToggleDoctors()
    {
        var show = !Doctors.Visible;
        HideRightPanels();
        if (show)
        {
            Doctors.Open();
        }
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

    /// <summary>Shows one room's details, closing the other right-hand panels.</summary>
    public void ShowRoom(BlueprintHospital.Simulation.Hospital.CareSpace room, bool people = false)
    {
        HideRightPanels();
        RoomInfo.Show(room, people);
    }

    /// <summary>Opens the Decorate panel on a room, closing the other right-hand panels.</summary>
    public void ShowDecorate(BlueprintHospital.Simulation.Hospital.CareSpace room)
    {
        HideRightPanels();
        Decorate.Show(room);
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

    /// <summary>Opens the staff rota, or closes it.</summary>
    public void ToggleRota()
    {
        var show = !Rota.Visible;
        HideRightPanels();
        Rota.Visible = show;
        if (show)
        {
            Rota.Rebuild();
        }
    }

    /// <summary>Opens the Surgery panel on the theatre board, or closes it.</summary>
    public void ToggleSurgery()
    {
        var show = !Surgery.Visible;
        HideRightPanels();
        Surgery.Visible = show;
        if (show)
        {
            Surgery.ShowTab(waitingList: false);
        }
    }

    /// <summary>Opens the illness journal, or closes it.</summary>
    public void ToggleJournal()
    {
        var show = !Journal.Visible;
        HideRightPanels();
        if (show)
        {
            Journal.Open();
        }
    }

    /// <summary>Opens the Stats screen, or closes it.</summary>
    public void ToggleStats()
    {
        var show = !StatsScreen.Visible;
        HideRightPanels();
        StatsScreen.Visible = show;
    }

    /// <summary>Shows or hides the A&amp;E board.</summary>
    public void ToggleBoard() => _left.Visible = !_left.Visible;

    /// <summary>The A&amp;E board's area on screen, when it is showing (the tutorial card sits beside it).</summary>
    public Rect2? BoardRect => _left.Visible ? _left.GetGlobalRect() : null;

    /// <summary>A button on the left-hand navigation, by its label ("Build", "Hire"...), for the tutorial to point at.</summary>
    public Control? NavButtonFor(string label) => _nav.GetValueOrDefault(label);

    /// <summary>The Play or Build switch button in the top bar.</summary>
    public Control? ModeButtonFor(BlueprintHospital.Hospital.GameMode mode) => _modeButtons.GetValueOrDefault(mode);

    /// <summary>A clock speed button in the top bar.</summary>
    public Control? SpeedButtonFor(SimulationSpeed speed) => _speedButtons.GetValueOrDefault(speed);

    public void Initialise(GameSession session, BlueprintHospital.Hospital.BuildTool tool)
    {
        _session = session;
        Layer = 10;
        ProcessMode = ProcessModeEnum.Always;

        var root = new Control { Theme = UiStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        Feed.Initialise(session.Simulation, session.Events);
        var buildMenu = BuildMenu.Load(new BlueprintHospital.Core.GodotDataSource(), session.Simulation.Database);
        Level.Initialise(session.Simulation, session.Events, buildMenu);
        Level.ShowMeRequested += ShowBuildIcon;
        Level.PopupRequested += popup => root.AddChild(popup);
        root.AddChild(BuildTag());
        root.AddChild(BuildPickTag());
        root.AddChild(BuildTopBar());
        root.AddChild(BuildNav());
        root.AddChild(BuildHoverPanel());
        root.AddChild(BuildMapControls());

        var sim = session.Simulation;
        _left.OffsetLeft = NavWidth + Gap;
        _left.OffsetTop = TopBarHeight + Gap;
        _left.AnchorBottom = 1;
        _left.OffsetBottom = -Gap;
        _left.AddThemeConstantOverride("separation", 10);
        Dashboard.Initialise(sim);
        Dashboard.CustomMinimumSize = new Vector2(330, 0);
        _left.AddChild(Dashboard);
        root.AddChild(_left);

        Record.Initialise(sim);
        DockRight(Record, 420);
        root.AddChild(Record);
        Record.ResultRequested += (patient, order) => BlueprintHospital.UI.Results.ResultViewer.Open(this, sim.Database, patient, order, session.HospitalName);
        StaffInfo.Initialise(sim);
        DockRight(StaffInfo, 400);
        root.AddChild(StaffInfo);
        Roster.Initialise(sim);
        DockRight(Roster, 360);
        root.AddChild(Roster);
        RoomInfo.Initialise(sim);
        DockRight(RoomInfo, 420);
        root.AddChild(RoomInfo);
        OutpatientInfo.Initialise(sim);
        DockRight(OutpatientInfo, 420);
        root.AddChild(OutpatientInfo);
        Decorate.Initialise(sim);
        DockRight(Decorate, 410);
        root.AddChild(Decorate);
        Finances.Initialise(sim);
        DockRight(Finances, 452);
        root.AddChild(Finances);
        Calendar.Initialise(sim);
        DockRight(Calendar, 520);
        root.AddChild(Calendar);
        Recruitment.Initialise(sim);
        DockRight(Recruitment, 440);
        root.AddChild(Recruitment);
        Doctors.Initialise(sim);
        DockRight(Doctors, 440);
        Doctors.CloseRequested += HideRightPanels;
        root.AddChild(Doctors);
        Clinics.Initialise(sim);
        DockRight(Clinics, 480);
        root.AddChild(Clinics);
        ItemInfo.Initialise(sim);
        DockRight(ItemInfo, 360);
        ItemInfo.AnchorBottom = 0;
        ItemInfo.OffsetBottom = TopBarHeight + Gap + 10;
        root.AddChild(ItemInfo);

        // The rota is big: it covers the map between the left-hand buttons and the right edge.
        Rota.Initialise(sim);
        Rota.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Rota.OffsetLeft = NavWidth + Gap;
        Rota.OffsetTop = TopBarHeight + Gap;
        Rota.OffsetRight = -Gap;
        Rota.OffsetBottom = -Gap;
        root.AddChild(Rota);
        Rota.HireRequested += _ => ToggleRecruitment();

        // The Stats screen is as big as the rota.
        StatsScreen.Initialise(sim);
        StatsScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        StatsScreen.OffsetLeft = NavWidth + Gap;
        StatsScreen.OffsetTop = TopBarHeight + Gap;
        StatsScreen.OffsetRight = -Gap;
        StatsScreen.OffsetBottom = -Gap;
        root.AddChild(StatsScreen);

        // And the illness journal.
        Journal.Initialise(sim);
        Journal.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Journal.OffsetLeft = NavWidth + Gap;
        Journal.OffsetTop = TopBarHeight + Gap;
        Journal.OffsetRight = -Gap;
        Journal.OffsetBottom = -Gap;
        root.AddChild(Journal);

        // So is the Surgery panel.
        Surgery.Initialise(sim);
        Surgery.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Surgery.OffsetLeft = NavWidth + Gap;
        Surgery.OffsetTop = TopBarHeight + Gap;
        Surgery.OffsetRight = -Gap;
        Surgery.OffsetBottom = -Gap;
        root.AddChild(Surgery);

        // The build bar and its pop-ups sit above the panels.
        Build.Initialise(sim, tool, buildMenu);
        root.AddChild(Build);

        // The goals card drops down from the level badge, over the map.
        if (sim.IsCareer)
        {
            root.AddChild(Level.Card);
        }

        // The crash call banner hangs under the middle of the top bar.
        Crash.Initialise(sim);
        Crash.AnchorLeft = Crash.AnchorRight = 0.5f;
        Crash.OffsetLeft = -280;
        Crash.OffsetRight = 280;
        Crash.OffsetTop = TopBarHeight + Gap;
        root.AddChild(Crash);

        // The major incident banner hangs there too; a crash call drops below it.
        Incident.Initialise(sim, session.Events);
        Incident.AnchorLeft = Incident.AnchorRight = 0.5f;
        Incident.OffsetLeft = -470;
        Incident.OffsetRight = 470;
        Incident.OffsetTop = TopBarHeight + Gap;
        root.AddChild(Incident);

        // The cheat console sits on top of everything, centred under the top bar.
        Cheats.Initialise(sim);
        Cheats.AnchorLeft = Cheats.AnchorRight = 0.5f;
        Cheats.OffsetLeft = -300;
        Cheats.OffsetRight = 300;
        Cheats.OffsetTop = TopBarHeight + Gap;
        root.AddChild(Cheats);

        // The big monitor opens over the map and panels.
        Monitor.Initialise(sim);
        root.AddChild(Monitor);

        // The notifications drop-down opens over everything else.
        root.AddChild(Feed.Dropdown);

        _speedSubscription = session.Events.Subscribe<SimulationSpeedChanged>(_ => RefreshSpeedButtons());
        RefreshSpeedButtons();
        RefreshClock();
    }

    public override void _ExitTree() => _speedSubscription?.Dispose();

    public override void _Process(double delta)
    {
        RefreshClock();
        RefreshNav();
        RefreshTag();
        Crash.OffsetTop = TopBarHeight + Gap + (Incident.Visible ? Incident.Size.Y + 6 : 0);

        // In Build mode everything docked to the bottom of the screen moves up above the build bar.
        var reserve = Build.Visible ? BuildBar.Reserve : 0;
        _left.OffsetBottom = -Gap - reserve;
        foreach (var panel in RightPanels.Append(Rota).Append(StatsScreen).Append(Surgery).Append(Journal).Where(p => p != ItemInfo))
        {
            panel.OffsetBottom = -Gap - reserve;
        }

        _mapControls.OffsetBottom = _mapControls.OffsetTop = -Gap - reserve;
        if (_hoverPanel is not null)
        {
            _hoverPanel.OffsetBottom = _hoverPanel.OffsetTop = -Gap - reserve;
        }

        // Keep the zoom buttons clear of whichever card is docked on the right.
        var open = RightPanels.FirstOrDefault(p => p.Visible);
        _mapControls.OffsetRight = open is null ? -Gap : open.OffsetLeft - Gap;
        _mapControls.OffsetLeft = _mapControls.OffsetRight - 44;
    }

    public void ShowHoveredTile(int x, int y, bool onSite, SiteMap site)
    {
        if (!onSite)
        {
            _hoverLabel.Text = "Off site";
            return;
        }

        var tile = new GridCoord(x, y);
        var local = site.Local(tile);
        var floor = site.FloorOf(tile);
        var building = site.BuildingAt(local);
        var where = building is null ? (floor == 0 ? Describe(site[local]) : "Open air") : floor == 0 ? building.Name : $"{building.Name}, {SiteMap.FloorName(floor).ToLowerInvariant()}";
        foreach (var space in _session.Simulation.Spaces)
        {
            if (space.Rect.Contains(tile) && space.Kind != BlueprintHospital.Simulation.Hospital.CareSpaceKind.Corridor)
            {
                where = $"{space.Name} ({space.AreaSquareMetres:0} m²)";
            }
        }

        _hoverLabel.Text = $"{where}   ·   {Units.TilesToMetres(local.X):0.0} m, {Units.TilesToMetres(local.Y):0.0} m";
    }

    // ---------------------------------------------------------------- top bar

    /// <summary>Switches to Build mode and opens one build bar icon ("Show me" on a goal or a new level).</summary>
    public void ShowBuildIcon(string iconId)
    {
        ModeRequested?.Invoke(BlueprintHospital.Hospital.GameMode.Build);
        Build.Open(iconId);
    }

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
        AddSpeedButton(speedRow, "8x", SimulationSpeed.Fastest, "Fastest (4)");
        clockRow.AddChild(speeds);
        clock.AddChild(clockRow);
        row.AddChild(clock);
        row.AddChild(BuildModeSwitch());
        if (_session.Simulation.IsCareer)
        {
            row.AddChild(Level);
        }

        // The season and the pressure badge: both open the Calendar (K).
        foreach (var pill in new[] { SeasonPill, PressureBadge })
        {
            pill.ThemeTypeVariation = UiStyle.Secondary;
            pill.CustomMinimumSize = new Vector2(0, 40);
            pill.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            pill.AddThemeFontSizeOverride("font_size", 14);
            pill.Pressed += ToggleCalendar;
            row.AddChild(pill);
        }

        // The latest notification fills the gap; hovering drops down the last five.
        Feed.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(Feed);

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

    private readonly Dictionary<BlueprintHospital.Hospital.GameMode, Button> _modeButtons = new();

    /// <summary>The Play / Build switch: what clicking on the map picks.</summary>
    private Control BuildModeSwitch()
    {
        var pill = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var style = UiStyle.Box(UiStyle.Ground, 10, UiStyle.Border);
        style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = style.ContentMarginBottom = 4;
        pill.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        pill.AddChild(row);
        AddModeButton(row, "Play", BlueprintHospital.Hospital.GameMode.Play, "Play mode: click patients, staff and beds");
        AddModeButton(row, "Build", BlueprintHospital.Hospital.GameMode.Build, "Build mode: build, and click rooms and items (B)");
        return pill;
    }

    private void AddModeButton(HBoxContainer parent, string text, BlueprintHospital.Hospital.GameMode mode, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(64, 32),
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeStyleboxOverride("normal", UiStyle.Box(new Color(0, 0, 0, 0), 6));
        button.AddThemeStyleboxOverride("hover", UiStyle.Box(UiStyle.Surface, 6));
        button.AddThemeStyleboxOverride("pressed", UiStyle.Box(UiStyle.Blue, 6));
        button.AddThemeStyleboxOverride("hover_pressed", UiStyle.Box(UiStyle.BlueHover, 6));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_focus_color" })
        {
            button.AddThemeColorOverride(state, UiStyle.TextDark);
        }

        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
        button.AddThemeFontSizeOverride("font_size", 14);
        button.Pressed += () =>
        {
            button.SetPressedNoSignal(mode == Mode);
            ModeRequested?.Invoke(mode);
        };
        _modeButtons[mode] = button;
        parent.AddChild(button);
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
        column.AddChild(NavButton("Hospital", NavIcon.Kind.Hospital, ToggleBoard, "Show or hide the A&E board"));
        column.AddChild(NavButton("Build", NavIcon.Kind.Build, () => ModeRequested?.Invoke(Mode == BlueprintHospital.Hospital.GameMode.Build ? BlueprintHospital.Hospital.GameMode.Play : BlueprintHospital.Hospital.GameMode.Build), "Build mode: build rooms and corridors, and pick rooms and items (B)"));
        column.AddChild(NavButton("Patients", NavIcon.Kind.Patients, () => ToggleRoster(false), "Everyone in A&E, sickest first (P)"));
        column.AddChild(NavButton("Staff", NavIcon.Kind.Staff, ToggleRota, "Staff rota: day and night teams, breaks and who works where (T)"));
        column.AddChild(NavButton("Doctor", NavIcon.Kind.Doctor, ToggleDoctors, BlueprintHospital.Core.GameView.ThreeD ? "Be a doctor: play one of your doctors in first person (O)" : "Be a doctor: walk one of your doctors round the hospital yourself and do their work (O)"));
        column.AddChild(NavButton("Surgery", NavIcon.Kind.Surgery, ToggleSurgery, "Surgery: the theatre board and who is waiting for an operation (U)"));
        column.AddChild(NavButton("Clinics", NavIcon.Kind.Clinics, ToggleClinics, "Outpatient clinics: today's and tomorrow's booked patients (C)"));
        column.AddChild(NavButton("Hire", NavIcon.Kind.Hire, ToggleRecruitment, "Recruitment: hire and let go of staff (H)"));
        column.AddChild(NavButton("Finances", NavIcon.Kind.Finances, ToggleFinances, "Money in and out (F)"));
        column.AddChild(NavButton("Stats", NavIcon.Kind.Stats, ToggleStats, "Hospital stats: the last 24 hours, 7 days, 28 days and all time (I)"));
        column.AddChild(NavButton("Journal", NavIcon.Kind.Journal, ToggleJournal, "Illness journal: every illness and how many patients you have treated for each (J)"));
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
        Set("Build", Mode == BlueprintHospital.Hospital.GameMode.Build);
        foreach (var (mode, button) in _modeButtons)
        {
            if (button.ButtonPressed != (mode == Mode))
            {
                button.SetPressedNoSignal(mode == Mode);
            }
        }
        Set("Patients", Roster.Visible && !_rosterShowsStaff);
        Set("Staff", Rota.Visible || (Roster.Visible && _rosterShowsStaff));
        Set("Doctor", Doctors.Visible);
        Set("Surgery", Surgery.Visible);
        Set("Clinics", Clinics.Visible);
        Set("Hire", Recruitment.Visible);
        Set("Finances", Finances.Visible);
        Set("Stats", StatsScreen.Visible);
        Set("Journal", Journal.Visible);
        Set("Routes", _routesShown);
        Set("Grid", _gridShown);
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
        // Floor switcher: up, the floor in view, down, and a button to build another floor.
        var addFloor = MapButton("+▲", "Build another floor (opens the Building icon in the build bar)", () => AddFloorRequested?.Invoke());
        addFloor.AddThemeFontSizeOverride("font_size", 15);
        _mapControls.AddChild(addFloor);
        _floorUp = MapButton("▲", "Up a floor (Page Up)", () => FloorStepRequested?.Invoke(1));
        _floorUp.AddThemeFontSizeOverride("font_size", 15);
        _mapControls.AddChild(_floorUp);
        var floorCard = new PanelContainer { CustomMinimumSize = new Vector2(44, 34), MouseFilter = Control.MouseFilterEnum.Pass };
        var floorBox = UiStyle.Box(UiStyle.Blue, 8);
        floorCard.AddThemeStyleboxOverride("panel", floorBox);
        _floorLabel.AddThemeColorOverride("font_color", Colors.White);
        _floorLabel.AddThemeFontSizeOverride("font_size", 18);
        _floorLabel.MouseFilter = Control.MouseFilterEnum.Stop;
        floorCard.AddChild(_floorLabel);
        _mapControls.AddChild(floorCard);
        _floorDown = MapButton("▼", "Down a floor (Page Down)", () => FloorStepRequested?.Invoke(-1));
        _floorDown.AddThemeFontSizeOverride("font_size", 15);
        _mapControls.AddChild(_floorDown);
        SetFloorShown(0, 1);

        _mapControls.AddChild(MapButton("+", "Zoom in (mouse wheel)", () => ZoomRequested?.Invoke(1.25f)));
        _mapControls.AddChild(MapButton("−", "Zoom out (mouse wheel)", () => ZoomRequested?.Invoke(0.8f)));
        _mapControls.AddChild(MapButton("⟲", "Turn the view anticlockwise (Q)", () => RotateRequested?.Invoke(-1)));
        _mapControls.AddChild(MapButton("⟳", "Turn the view clockwise (E)", () => RotateRequested?.Invoke(1)));
        if (BlueprintHospital.Core.GameView.ThreeD)
        {
            _mapControls.AddChild(MapButton("⌂", "Walls up or cut down low, to see into every room (X)", () => WallsToggled?.Invoke()));
        }

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

    private Control BuildPickTag()
    {
        var box = UiStyle.Box(new Color(1, 1, 1, 0.96f), 8, UiStyle.Blue);
        box.BorderWidthLeft = 4;
        box.ContentMarginLeft = 10;
        box.ContentMarginRight = 10;
        box.ContentMarginTop = box.ContentMarginBottom = 4;
        box.ShadowColor = new Color(0.09f, 0.14f, 0.18f, 0.25f);
        box.ShadowSize = 6;
        _pickTag.AddThemeStyleboxOverride("panel", box);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        _pickName.AddThemeColorOverride("font_color", UiStyle.TextDark);
        _pickName.AddThemeFontSizeOverride("font_size", 14);
        _pickDetail.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        _pickDetail.AddThemeFontSizeOverride("font_size", 12);
        column.AddChild(_pickName);
        column.AddChild(_pickDetail);
        _pickTag.AddChild(column);
        return _pickTag;
    }

    /// <summary>
    /// Play mode: a small card beside the mouse naming who (or whose bed) a click would pick. Null hides it.
    /// </summary>
    public void ShowPickTag((string Name, string Detail)? tag, Vector2 mouse)
    {
        if (tag is not { } t)
        {
            _pickTag.Visible = false;
            return;
        }

        if (_pickName.Text != t.Name || _pickDetail.Text != t.Detail)
        {
            _pickName.Text = t.Name;
            _pickDetail.Text = t.Detail;
            _pickTag.Size = Vector2.Zero;
        }

        _pickTag.Visible = true;
        MovePickTag(mouse);
    }

    /// <summary>Keeps the play mode name card just below and right of the mouse, inside the screen.</summary>
    public void MovePickTag(Vector2 mouse)
    {
        if (!_pickTag.Visible)
        {
            return;
        }

        var size = _pickTag.GetCombinedMinimumSize();
        var screen = GetViewport().GetVisibleRect().Size;
        var at = mouse + new Vector2(20, 22);
        at.X = Mathf.Min(at.X, screen.X - size.X - 8);
        at.Y = Mathf.Min(at.Y, screen.Y - size.Y - 8);
        _pickTag.Position = at;
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
        _tag.Position = new Vector2(foot.X - (_tag.Size.X / 2), foot.Y - ((BlueprintHospital.Hospital.IsoDraw.Plan ? 22 : 78) * zoom) - _tag.Size.Y);
        _tag.Visible = true;
    }

    private Control BuildHoverPanel()
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hoverPanel = panel;
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
        _balanceButton.Text = balance < 0 ? $"-£{-balance:N0}" : $"£{balance:N0}";
        _balanceButton.TooltipText = _session.Simulation.UnlimitedMoney
            ? "Money in the bank. Unlimited money is on, so you can keep spending in the red. Click for finances (F)"
            : "Money in the bank. Click for finances (F)";
        var colour = balance < 0 ? UiStyle.Red : UiStyle.Green;
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
        {
            _balanceButton.AddThemeColorOverride(state, colour);
        }

        var now = _session.Clock.Now;
        _dateLabel.Text = now.ToString("dddd d MMMM yyyy");
        _timeLabel.Text = now.ToString("HH:mm");
        RefreshSeason();
    }

    private void RefreshSeason()
    {
        var sim = _session.Simulation;
        var now = sim.Now;
        var text = SeasonCalendar.Name(SeasonCalendar.SeasonOf(now));
        var tip = "The season. Click for the Calendar (K)";
        if (sim.SeasonsActive)
        {
            if (sim.Weather is { } weather)
            {
                text += $" · {weather.Brief()}";
            }

            if (sim.EventsNow.FirstOrDefault(d => d.Event.Show) is { } happening)
            {
                text += $" · {happening.Event.Name}";
            }

            var flu = sim.Seasons!.Stage("flu", now);
            if (flu != WaveStage.None)
            {
                text += $" · Flu {flu.ToString().ToLowerInvariant()}";
            }

            tip = (sim.Weather is { } today ? $"Today: {today.Describe()}, low {today.Low:0}°C\n" : string.Empty)
                  + "The season, the weather and what's going round. Click for the week ahead and the year (K)";
        }

        SeasonPill.Text = text;
        SeasonPill.TooltipText = tip;

        var pressure = sim.Pressure;
        PressureBadge.Text = $"Pressure {pressure.Level}";
        PressureBadge.TooltipText = $"Hospital pressure {pressure.Level} of 4: {pressure.Name}."
            + (pressure.Reasons.Count == 0 ? " Beds free and A&E flowing." : "\n" + string.Join("\n", pressure.Reasons))
            + "\nClick for the Calendar (K)";
        var colour = new Color(CalendarPanel.PressureHex(pressure.Level));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
        {
            PressureBadge.AddThemeColorOverride(state, colour);
        }
    }

    private static string Describe(TileKind kind) => kind switch
    {
        TileKind.Grass => "Grounds",
        TileKind.Paving => "Footpath",
        TileKind.Road => "Road",
        TileKind.AmbulanceBay => "Ambulance bay",
        TileKind.Parking => "Car park",
        TileKind.TaxiRank => "Taxi rank",
        TileKind.BuildingFloor => "Building",
        _ => kind.ToString(),
    };
}
