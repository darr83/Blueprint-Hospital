using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;
using BlueprintHospital.UI;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// The main play scene. Wires the current <see cref="GameSession"/> to the site and department
/// views, the people in them, the camera and HUD, and drives the simulation clock every frame.
/// Clicking a patient opens their record; clicking a member of staff shows the staff list.
/// </summary>
public partial class HospitalScene : Node2D
{
    private readonly IsoProjection _projection = new();
    private GameSession _session = null!;
    private SiteRenderer _site = null!;
    private DepartmentRenderer _department = null!;
    private AgentRenderer _agents = null!;
    private TileCursor _cursor = null!;
    private IsoCamera _camera = null!;
    private MainHUD _hud = null!;

    public GameSession Session => _session;

    public IsoCamera Camera => _camera;

    public SiteRenderer SiteView => _site;

    public AgentRenderer Agents => _agents;

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

        _site = new SiteRenderer { Name = "Site" };
        AddChild(_site);
        _site.Initialise(_session.Site, _projection);
        _site.ShowGrid = settings.ShowGridByDefault;

        _department = new DepartmentRenderer { Name = "Department" };
        AddChild(_department);
        _department.Initialise(_session.Simulation);

        _agents = new AgentRenderer { Name = "People" };
        AddChild(_agents);
        _agents.Initialise(_session.Simulation, _session.Clock);

        _cursor = new TileCursor { Name = "TileCursor" };
        AddChild(_cursor);
        _cursor.Initialise(_session.Site, _projection);

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
            _camera.SnapTo(_site.ToScreen(footprint.X + 30, footprint.Y + 21), 0.56f);
        }

        _hud = new MainHUD { Name = "HUD" };
        AddChild(_hud);
        _hud.Initialise(_session);
        _hud.GridToggled += ToggleGrid;
        _hud.MenuRequested += OpenPauseMenu;
        _cursor.HoveredTileChanged += (x, y, onSite) => _hud.ShowHoveredTile(x, y, onSite, _session.Site);
        _hud.Record.CloseRequested += ClearSelection;
        _hud.Record.FocusRequested += p => _camera.GlideTo(_agents.FootPosition(p));
        _hud.Feed.PatientClicked += id =>
        {
            if (_session.Simulation.FindPatient(id) is { } patient)
            {
                SelectPatient(patient, centre: true);
            }
        };
        _hud.Roster.PatientClicked += p => SelectPatient(p, centre: true);
        _hud.Roster.StaffClicked += m => SelectStaff(m, centre: true);
    }

    /// <summary>Selects a patient and opens their record.</summary>
    public void SelectPatient(Patient patient, bool centre = false)
    {
        _agents.Selected = patient;
        _hud.Roster.Visible = false;
        _hud.Record.Show(patient);
        if (centre && patient.IsInDepartment)
        {
            _camera.GlideTo(_agents.FootPosition(patient));
        }
    }

    private void SelectStaff(StaffMember member, bool centre)
    {
        _agents.Selected = member;
        if (centre)
        {
            _camera.GlideTo(_agents.FootPosition(member));
        }
    }

    private void ClearSelection()
    {
        _agents.Selected = null;
        _hud.Record.Visible = false;
    }

    public override void _Process(double delta)
    {
        _session.Clock.Advance(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            switch (_agents.HitTest(GetGlobalMousePosition()))
            {
                case Patient patient:
                    SelectPatient(patient);
                    break;
                case StaffMember member:
                    SelectStaff(member, centre: false);
                    _hud.Record.Visible = false;
                    if (!_hud.Roster.Visible)
                    {
                        _hud.ToggleRoster(staff: true);
                    }

                    break;
                default:
                    ClearSelection();
                    break;
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Escape when _hud.Record.Visible || _hud.Roster.Visible:
                ClearSelection();
                _hud.Roster.Visible = false;
                break;
            case Key.Escape:
                OpenPauseMenu();
                break;
            case Key.P:
                _hud.ToggleRoster(staff: false);
                break;
            case Key.T:
                _hud.ToggleRoster(staff: true);
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
            case Key.G:
                ToggleGrid();
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    public CameraState CaptureCamera() => _camera.CaptureState();

    private void ToggleGrid() => _site.ShowGrid = !_site.ShowGrid;

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
