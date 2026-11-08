using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.World;
using BlueprintHospital.UI;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// The main play scene. Wires the current <see cref="GameSession"/> to the site view,
/// camera and HUD, and drives the simulation clock every frame.
/// </summary>
public partial class HospitalScene : Node2D
{
    private readonly IsoProjection _projection = new();
    private GameSession _session = null!;
    private SiteRenderer _site = null!;
    private TileCursor _cursor = null!;
    private IsoCamera _camera = null!;
    private MainHUD _hud = null!;

    public GameSession Session => _session;

    public IsoCamera Camera => _camera;

    public SiteRenderer SiteView => _site;

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
            var footprint = StarterSite.MainBuildingFootprint;
            _camera.SnapTo(_site.ToScreen(footprint.X + (footprint.Width / 2.0), footprint.Y + (footprint.Length / 2.0)), 0.45f);
        }

        _hud = new MainHUD { Name = "HUD" };
        AddChild(_hud);
        _hud.Initialise(_session);
        _hud.GridToggled += ToggleGrid;
        _hud.MenuRequested += OpenPauseMenu;
        _cursor.HoveredTileChanged += (x, y, onSite) => _hud.ShowHoveredTile(x, y, onSite, _session.Site);
    }

    public override void _Process(double delta)
    {
        _session.Clock.Advance(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Escape:
                OpenPauseMenu();
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
