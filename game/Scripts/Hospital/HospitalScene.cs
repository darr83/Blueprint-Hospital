using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Agents;
using System.Linq;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;
using BlueprintHospital.UI;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// What clicking on the map picks. In play mode: patients, staff and beds. In build mode: rooms,
/// furniture and equipment, and the build menu is open.
/// </summary>
public enum GameMode
{
    Play,
    Build,
}

/// <summary>
/// The main play scene. Wires the current <see cref="GameSession"/> to the site and department
/// views, the people in them, the camera and HUD, and drives the simulation clock every frame.
/// In play mode clicking a patient, member of staff or bed opens their panel; in build mode
/// clicking a room or a piece of furniture does.
/// </summary>
public partial class HospitalScene : Node2D
{
    private readonly IsoProjection _projection = new();
    private GameSession _session = null!;
    private SiteRenderer _site = null!;
    private DepartmentRenderer _department = null!;
    private WorldLayer _world = null!;
    private OverlayLayer _overlay = null!;
    private RouteLayer _routes = null!;
    private TileCursor _cursor = null!;
    private BuildTool _build = null!;
    private IsoCamera _camera = null!;
    private MainHUD _hud = null!;
    private SelectionHighlight _highlight = null!;
    private SelectionHighlight _itemHighlight = null!;

    /// <summary>Play (people and beds) or Build (rooms and items).</summary>
    public GameMode Mode { get; private set; } = GameMode.Play;

    public GameSession Session => _session;

    public IsoCamera Camera => _camera;

    public SiteRenderer SiteView => _site;

    public WorldLayer World => _world;

    public MainHUD Hud => _hud;

    public override void _Ready()
    {
        var manager = GameManager.Instance;

        // Allows running this scene directly from the editor (F6) for quick testing.
        if (manager.Session is null && manager.Database is not null)
        {
            manager.StartNewGameInPlace();
        }

        _session = manager.Session!;
        var settings = manager.Settings;

        IsoDraw.SetSite(_session.Site.Width, _session.Site.Length, _session.Site.FloorStride);
        IsoDraw.SetRotation(0);
        IsoDraw.SetViewFloor(0);

        _site = new SiteRenderer { Name = "Site" };
        AddChild(_site);
        _site.Initialise(_session.Site, _projection);
        _site.ShowGrid = settings.ShowGridByDefault;

        _department = new DepartmentRenderer { Name = "Department" };
        AddChild(_department);
        _department.Initialise(_session.Simulation);

        // Outline of the selected room or item, on the floor under everything standing on it.
        _highlight = new SelectionHighlight { Name = "SelectionHighlight" };
        AddChild(_highlight);

        // Wet patches where someone didn't make it to a toilet, on the floor under everything else.
        var accidents = new AccidentLayer { Name = "Accidents" };
        AddChild(accidents);
        accidents.Initialise(_session.Simulation);

        // Walls, furniture, people and vehicles, depth-sorted together; labels on top.
        _world = new WorldLayer { Name = "World" };
        AddChild(_world);
        _world.Initialise(_session.Simulation, _session.Clock);

        _itemHighlight = new SelectionHighlight { Name = "ItemHighlight" };
        AddChild(_itemHighlight);

        _routes = new RouteLayer { Name = "Routes" };
        AddChild(_routes);
        _routes.Initialise(_world);

        _overlay = new OverlayLayer { Name = "Labels" };
        AddChild(_overlay);
        _overlay.Initialise(_world);

        _cursor = new TileCursor { Name = "TileCursor" };
        AddChild(_cursor);
        _cursor.Initialise(_session.Site, _projection);

        _build = new BuildTool { Name = "BuildTool" };
        AddChild(_build);
        _build.Initialise(_session.Simulation, _projection);
        _layoutSubscription = _session.Events.Subscribe<LayoutChanged>(_ => _layoutDirty = true);

        _camera = new IsoCamera
        {
            Name = "Camera",
            PanSpeedMultiplier = settings.CameraPanSpeed,
            EdgeScrolling = settings.EdgeScrolling,
            Bounds = SiteBounds(),
        };
        AddChild(_camera);

        if (_session.InitialCamera is { } saved)
        {
            _camera.SnapTo(new Vector2((float)saved.X, (float)saved.Y), (float)saved.Zoom);
        }
        else
        {
            // Start looking at the emergency department.
            var footprint = StarterSite.MainBuildingFootprint;
            _camera.SnapTo(_site.ToScreen(footprint.X + 22, footprint.Y + 16), 0.56f);
        }

        _hud = new MainHUD { Name = "HUD" };
        AddChild(_hud);
        _hud.Initialise(_session, _build);

        // Sound: ambience, monitors, machines, chatter, sirens. Only reads the game.
        var audio = new BlueprintHospital.Audio.HospitalAudio { Name = "Audio" };
        AddChild(audio);
        audio.Initialise(_session, _world);
        _hud.RoomInfo.MoveRequested += room =>
        {
            _hud.RoomInfo.Close();
            SetMode(GameMode.Build);
            _build.StartRoom(_session.Database.RoomTemplates[room.TemplateId], room.Id);
        };
        _hud.ModeRequested += SetMode;
        _hud.ItemInfo.RoomRequested += room => _hud.ShowRoom(room);
        _hud.ItemInfo.PatientRequested += p => SelectPatient(p, centre: true);
        _hud.GridToggled += ToggleGrid;
        _hud.RoutesToggled += ToggleRoutes;
        _hud.MenuRequested += OpenPauseMenu;
        _cursor.HoveredTileChanged += (x, y, onSite) => _hud.ShowHoveredTile(x, y, onSite, _session.Site);
        _hud.Record.CloseRequested += ClearSelection;
        _hud.Record.FocusRequested += p => FocusOn(p);
        _hud.Feed.PatientClicked += id =>
        {
            if (_session.Simulation.FindPatient(id) is { } patient)
            {
                SelectPatient(patient, centre: true);
            }
        };
        _hud.Roster.PatientClicked += p => SelectPatient(p, centre: true);
        _hud.Crash.FollowRequested += id =>
        {
            if (_session.Simulation.FindPatient(id) is { } patient)
            {
                SelectPatient(patient, centre: true);
                _hud.Crash.Expanded = true;
            }
        };
        _hud.Roster.StaffClicked += m => SelectStaff(m, centre: true);
        _hud.Rota.ShowOnMapRequested += m => SelectStaff(m, centre: true);
        _hud.Clinics.OutpatientClicked += o => SelectOutpatient(o, centre: true);
        _hud.StaffInfo.CloseRequested += ClearSelection;
        _hud.StaffInfo.FocusRequested += m => FocusOn(m);
        _hud.ZoomRequested += factor => _camera.ZoomBy(factor);
        _hud.RotateRequested += RotateView;
        _hud.FloorStepRequested += step => ShowFloor(ViewFloor + step);
        _hud.AddFloorRequested += () =>
        {
            SetMode(GameMode.Build);
            _hud.HideRightPanels();
            _hud.Build.Open("building");
        };
        _hud.SetFloorShown(ViewFloor, _session.Simulation.FloorCount);
        _hud.SetGridShown(_site.ShowGrid);
        _hud.SelectionAnchor = () => _world.Selected switch
        {
            Agent a when !_world.OnViewFloor(a) => null,
            Patient p when _world.IsOnMap(p) => (_world.FootPosition(p), p.FullName),
            StaffMember m => (_world.FootPosition(m), m.Name),
            Outpatient o when _session.Simulation.FindAppointment(o.AppointmentId) is { } a => (_world.FootPosition(o), a.FullName),
            _ => null,
        };
    }

    /// <summary>
    /// Switches between play and build mode. Build mode opens the build menu and lets go of whoever
    /// was selected; play mode closes the build menu and any room or item panel.
    /// </summary>
    public void SetMode(GameMode mode)
    {
        if (mode == Mode)
        {
            return;
        }

        Mode = mode;
        _hud.Mode = mode;
        _build.Cancel();
        ClearSelection();
        _hud.HideRightPanels();
    }

    /// <summary>Selects a patient and opens their record (switching to play mode).</summary>
    public void SelectPatient(Patient patient, bool centre = false)
    {
        SetMode(GameMode.Play);
        _world.Selected = patient;
        _hud.HideRightPanels();
        _hud.Record.Show(patient);
        if (centre && patient.IsInDepartment)
        {
            FocusOn(patient);
        }
    }

    /// <summary>Selects a member of staff and opens their panel.</summary>
    public void SelectStaff(StaffMember member, bool centre = false)
    {
        SetMode(GameMode.Play);
        _world.Selected = member;
        _hud.ShowStaff(member);
        if (centre)
        {
            FocusOn(member);
        }
    }

    /// <summary>Selects a clinic patient and opens the clinic calendars with them picked out.</summary>
    public void SelectOutpatient(Outpatient outpatient, bool centre = false)
    {
        SetMode(GameMode.Play);
        _world.Selected = outpatient;
        _hud.HideRightPanels();
        _hud.Clinics.SelectedOutpatientId = outpatient.Id;
        _hud.Clinics.ShowToday();
        if (centre)
        {
            FocusOn(outpatient);
        }
    }

    /// <summary>For the smoke test: deselects whoever is selected.</summary>
    public void ClearSelectionForTest() => ClearSelection();

    private void ClearSelection()
    {
        _world.Selected = null;
        _hud.Clinics.SelectedOutpatientId = null;
        _hud.Record.Visible = false;
        _hud.StaffInfo.Visible = false;
    }

    private System.IDisposable? _layoutSubscription;
    private bool _layoutDirty;

    public BuildTool BuildTool => _build;

    public override void _ExitTree() => _layoutSubscription?.Dispose();

    public override void _Process(double delta)
    {
        _session.Clock.Advance(delta);
        _highlight.Room = _hud.RoomInfo.Visible && _hud.RoomInfo.RoomId is { } roomId ? _session.Simulation.FindSpace(roomId)?.Rect
            : _hud.Decorate.Visible && _hud.Decorate.RoomId is { } decorId ? _session.Simulation.FindSpace(decorId)?.Rect
            : null;
        _highlight.Hover = _hud.Build.Decorating && GetViewport().GuiGetHoveredControl() is null ? DecorTarget(GetGlobalMousePosition())?.Rect : null;
        var shown = _hud.ItemInfo.Visible && _hud.ItemInfo.Room is { } itemRoom && _hud.ItemInfo.Item is { } item ? (itemRoom, item) : default;
        _itemHighlight.Item = shown.item is null ? null : Footprint(shown.itemRoom, shown.item);
        _itemHighlight.ItemHeight = shown.item is not null && _session.Database.Furniture.TryGetValue(shown.item.ItemId, out var shownDef)
            ? (float)System.Math.Min(System.Math.Max(shownDef.HeightM, 0.3), 1.2) * IsoDraw.PixelsPerMetre
            : 0;
        if (_layoutDirty)
        {
            _layoutDirty = false;
            if (ViewFloor >= _session.Simulation.FloorCount)
            {
                ShowFloor(_session.Simulation.FloorCount - 1);
            }

            _hud.SetFloorShown(ViewFloor, _session.Simulation.FloorCount);
            _site.Rebuild();
            _department.QueueRedraw();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_build.HandleInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            ClickAt(GetGlobalMousePosition());
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Escape when _hud.Record.Visible || _hud.StaffInfo.Visible || _hud.Roster.Visible || _hud.RoomInfo.Visible || _hud.Decorate.Visible || _hud.ItemInfo.Visible || _hud.Finances.Visible || _hud.Recruitment.Visible || _hud.Clinics.Visible || _hud.Rota.Visible || _hud.StatsScreen.Visible:
                ClearSelection();
                _hud.HideRightPanels();
                break;
            case Key.Escape when _hud.Build.PopupOpen:
                _hud.Build.ClosePopup();
                break;
            case Key.Escape when Mode == GameMode.Build:
                SetMode(GameMode.Play);
                break;
            case Key.B:
                SetMode(Mode == GameMode.Build ? GameMode.Play : GameMode.Build);
                break;
            case Key.Escape:
                OpenPauseMenu();
                break;
            case Key.P:
                _hud.ToggleRoster(staff: false);
                break;
            case Key.T:
                _hud.ToggleRota();
                break;
            case Key.F:
                _hud.ToggleFinances();
                break;
            case Key.C when !key.CtrlPressed:
                _hud.ToggleClinics();
                break;
            case Key.H:
                _hud.ToggleRecruitment();
                break;
            case Key.I:
                _hud.ToggleStats();
                break;
            case Key.Space:
                _session.Clock.SetSpeed(_session.Clock.IsPaused ? SimulationSpeed.Normal : SimulationSpeed.Paused);
                break;
            case Key.Key1:
                _session.Clock.SetSpeed(SimulationSpeed.Normal);
                break;
            case Key.Key2:
                _session.Clock.SetSpeed(SimulationSpeed.Fast);
                break;
            case Key.Key3:
                _session.Clock.SetSpeed(SimulationSpeed.VeryFast);
                break;
            case Key.Key4:
                _session.Clock.SetSpeed(SimulationSpeed.Fastest);
                break;
            case Key.G:
                ToggleGrid();
                break;
            case Key.R:
                ToggleRoutes();
                break;
            case Key.Q:
                RotateView(-1);
                break;
            case Key.E:
                RotateView(1);
                break;
            case Key.Pageup:
                ShowFloor(ViewFloor + 1);
                break;
            case Key.Pagedown:
                ShowFloor(ViewFloor - 1);
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// A left click on the map. Play mode picks a person, or failing that a bed; build mode picks a
    /// piece of furniture, or failing that the room. Clicking empty floor closes the panel.
    /// </summary>
    public void ClickAt(Vector2 world)
    {
        if (Mode == GameMode.Play)
        {
            switch (_world.HitTest(world))
            {
                case Patient patient:
                    SelectPatient(patient);
                    return;
                case StaffMember member:
                    SelectStaff(member);
                    return;
                case Outpatient outpatient:
                    SelectOutpatient(outpatient);
                    return;
            }

            ClearSelection();
            if (ItemAt(world, bedsOnly: true) is { } bed)
            {
                _hud.HideRightPanels();
                _hud.ItemInfo.ShowBed(bed.Room, bed.Item);
            }
            else
            {
                _hud.ItemInfo.Close();
            }

            return;
        }

        if (_hud.Build.Decorating)
        {
            if (DecorTarget(world) is { } decorRoom)
            {
                _hud.ShowDecorate(decorRoom);
            }
            else
            {
                _hud.HideRightPanels();
            }

            return;
        }

        if (ItemAt(world, bedsOnly: false) is { } hit)
        {
            _hud.HideRightPanels();
            _hud.ItemInfo.ShowItem(hit.Room, hit.Item);
            return;
        }

        var tile = IsoDraw.ScreenToTile(world);
        var room = _session.Simulation.Spaces
            .Where(s => s.Rect.Contains(tile))
            .OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 1 : 0)
            .FirstOrDefault();
        if (room is not null)
        {
            _hud.ShowRoom(room);
        }
        else
        {
            _hud.HideRightPanels();
        }
    }

    /// <summary>The room under a point on screen that the Decorate tool can work on: rooms before corridors, never lifts or stairs.</summary>
    public CareSpace? DecorTarget(Vector2 world)
    {
        var tile = IsoDraw.ScreenToTile(world);
        return _session.Simulation.Spaces
            .Where(s => s.Rect.Contains(tile) && IsoDraw.OnViewFloor(s.Rect.Y) && DecorCatalogue.CanDecorate(s))
            .OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 1 : 0)
            .FirstOrDefault();
    }

    /// <summary>
    /// The furniture under a point on screen, counting its height so clicking the top of a cupboard
    /// works. Beds are the patient beds on wards and in cubicles (not X-ray tables).
    /// </summary>
    public (CareSpace Room, PlacedFurniture Item)? ItemAt(Vector2 world, bool bedsOnly)
    {
        (CareSpace, PlacedFurniture)? best = null;
        var bestDepth = float.MinValue;
        foreach (var space in _session.Simulation.Spaces.Where(s => IsoDraw.OnViewFloor(s.Rect.Y)))
        {
            foreach (var item in space.Furniture)
            {
                if (!_session.Database.Furniture.TryGetValue(item.ItemId, out var def))
                {
                    continue;
                }

                var isBed = def.Style == Simulation.Data.Models.FurnitureStyle.Bed && !space.IsServiceRoom;
                if (bedsOnly && !isBed)
                {
                    continue;
                }

                const double pad = 0.1;
                var (x, y, w, l) = Footprint(space, item);
                var lift = new Vector2(0, -(float)System.Math.Max(def.HeightM, 0.3) * IsoDraw.PixelsPerMetre);
                var floor = new[]
                {
                    IsoDraw.ToScreen(x - pad, y - pad), IsoDraw.ToScreen(x + w + pad, y - pad),
                    IsoDraw.ToScreen(x + w + pad, y + l + pad), IsoDraw.ToScreen(x - pad, y + l + pad),
                };
                var hull = Geometry2D.ConvexHull(floor.Concat(floor.Select(p => p + lift)).ToArray());
                if (!Geometry2D.IsPointInPolygon(world, hull))
                {
                    continue;
                }

                // Nearest the viewer wins when two overlap on screen.
                var depth = IsoDraw.ToScreen(x + (w / 2), y + (l / 2)).Y;
                if (depth > bestDepth)
                {
                    best = (space, item);
                    bestDepth = depth;
                }
            }
        }

        // Waiting chairs the player has placed, in waiting rooms and along corridors.
        if (!bedsOnly)
        {
            var sim = _session.Simulation;
            foreach (var placed in sim.Chairs.Where(c => IsoDraw.OnViewFloor(c.Tile.Y)))
            {
                var home = sim.Spaces.Where(s => s.Rect.Contains(placed.Tile)).OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 1 : 0).FirstOrDefault();
                if (home is null)
                {
                    continue;
                }

                const double pad = 0.1;
                var chair = new PlacedFurniture(HospitalSimulation.ChairItem, placed.Tile.X + 0.08, placed.Tile.Y + 0.08, 0.84, 0.84);
                var lift = new Vector2(0, -0.9f * IsoDraw.PixelsPerMetre);
                var floor = new[]
                {
                    IsoDraw.ToScreen(chair.X - pad, chair.Y - pad), IsoDraw.ToScreen(chair.X + chair.Width + pad, chair.Y - pad),
                    IsoDraw.ToScreen(chair.X + chair.Width + pad, chair.Y + chair.Length + pad), IsoDraw.ToScreen(chair.X - pad, chair.Y + chair.Length + pad),
                };
                var hull = Geometry2D.ConvexHull(floor.Concat(floor.Select(p => p + lift)).ToArray());
                var depth = IsoDraw.ToScreen(chair.CentreX, chair.CentreY).Y;
                if (Geometry2D.IsPointInPolygon(world, hull) && depth > bestDepth)
                {
                    best = (home, chair);
                    bestDepth = depth;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Where an item is drawn on the floor (X, Y, width, length in tiles). Patient beds are drawn
    /// centred on the bed's patient spot rather than at the layout's footprint.
    /// </summary>
    public (double X, double Y, double W, double L) Footprint(CareSpace space, PlacedFurniture item)
    {
        var isBed = _session.Database.Furniture.TryGetValue(item.ItemId, out var def)
            && def.Style == Simulation.Data.Models.FurnitureStyle.Bed && !space.IsServiceRoom;
        if (!isBed)
        {
            return (item.X, item.Y, item.Width, item.Length);
        }

        var spot = space.IsWard ? item.Tile : space.PatientSpot;
        return (spot.X + 0.5 - 0.8, spot.Y + 0.5 - 2.1, 1.6, 4.2);
    }

    /// <summary>The camera, saved as if the view were not rotated (saves always load facing the default way).</summary>
    public CameraState CaptureCamera()
    {
        var state = _camera.CaptureState();
        if (IsoDraw.Rotation == 0)
        {
            return state;
        }

        var (gx, gy) = IsoDraw.ScreenToGroundGrid(new Vector2((float)state.X, (float)state.Y));
        return state with { X = (gx - gy) * 32, Y = (gx + gy) * 16 };
    }

    /// <summary>Turns the view a quarter turn (Q anticlockwise, E clockwise), keeping the same spot in the middle of the screen.</summary>
    public void RotateView(int quarterTurns)
    {
        var state = _camera.CaptureState();
        var (gx, gy) = IsoDraw.ScreenToGrid(new Vector2((float)state.X, (float)state.Y));
        IsoDraw.SetRotation(IsoDraw.Rotation + quarterTurns);
        _site.Rebuild();
        _department.QueueRedraw();
        _world.RebuildStatic();
        _camera.Bounds = SiteBounds();
        _camera.SnapTo(IsoDraw.ToScreen(gx, gy), (float)state.Zoom);
    }

    /// <summary>The floor in view (0 = ground).</summary>
    public int ViewFloor => IsoDraw.ViewFloor;

    /// <summary>Looks at another floor: only its rooms and people are drawn, over the grounds faded below.</summary>
    public void ShowFloor(int floor)
    {
        floor = System.Math.Clamp(floor, 0, _session.Simulation.FloorCount - 1);
        if (floor == ViewFloor)
        {
            _hud.SetFloorShown(ViewFloor, _session.Simulation.FloorCount);
            return;
        }

        IsoDraw.SetViewFloor(floor);
        _site.QueueRedraw();
        _department.QueueRedraw();
        _world.RebuildStatic();
        _hud.SetFloorShown(floor, _session.Simulation.FloorCount);
    }

    /// <summary>Moves the view to someone, changing floor first if they are upstairs or down.</summary>
    public void FocusOn(Agent agent)
    {
        ShowFloor(_session.Simulation.FloorOf(agent));
        _camera.GlideTo(_world.FootPosition(agent));
    }

    private void ToggleGrid()
    {
        _site.ShowGrid = !_site.ShowGrid;
        _hud.SetGridShown(_site.ShowGrid);
    }

    /// <summary>Shows or hides everyone's walking route (the selected person's always shows).</summary>
    public void ToggleRoutes()
    {
        _routes.ShowAll = !_routes.ShowAll;
        _hud.SetRoutesShown(_routes.ShowAll);
    }

    public bool RoutesShown => _routes.ShowAll;

    private void OpenPauseMenu()
    {
        if (GetTree().Paused)
        {
            return;
        }

        var menu = PauseMenu.Create(CaptureCamera, _session.HospitalName);
        var layer = new CanvasLayer { Layer = 20, ProcessMode = ProcessModeEnum.Always };
        layer.AddChild(menu);
        menu.Closed += () => layer.QueueFree();
        AddChild(layer);
    }

    private Rect2 SiteBounds()
    {
        var site = _session.Site;
        var corners = new[]
        {
            _site.ToScreen(0, 0), _site.ToScreen(site.Width, 0), _site.ToScreen(site.Width, site.Length), _site.ToScreen(0, site.Length),
        };
        var rect = new Rect2(corners[0], Vector2.Zero);
        foreach (var c in corners)
        {
            rect = rect.Expand(c);
        }

        return rect;
    }
}
