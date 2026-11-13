using System;
using BlueprintHospital.Core;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The hospital in 3D, seen through an orthographic camera.
/// The camera copies the 2D camera every frame (where it looks and how far it is zoomed) and the
/// view's quarter turn, floor and plan mode, so everything still drawn on top in 2D (labels, the
/// tile cursor, build outlines, routes) lands in exactly the same place.
/// </summary>
public partial class View3D : Node3D
{
    /// <summary>The camera tilts down 30°, which gives the classic 2:1 isometric look.</summary>
    private const float Pitch = 30f;

    /// <summary>How far back along its line of sight the camera sits from the point it looks at.</summary>
    private float CameraDistance => 30f + (_camera.Size * 1.1f);

    private HospitalScene _scene = null!;
    private HospitalSimulation _sim = null!;
    private WorldLayer _world = null!;
    private Camera3D _camera = null!;
    private DirectionalLight3D _sun = null!;
    private DirectionalLight3D _fill = null!;
    private bool? _enhanced;
    private WorldEnvironment _environment = null!;

    /// <summary>The grounds: turned with the view, never shifted (they belong to the ground floor).</summary>
    private Node3D _groundRoot = null!;

    /// <summary>Everything inside: turned with the view and shifted so the floor in view sits on the grounds.</summary>
    private Node3D _root = null!;

    private Outside3D _outside = null!;
    private Ground3D _ground = null!;
    private FloorPainter _floors = null!;
    private Walls3D _walls = null!;
    private Agents3D _agents = null!;
    private Beds3D _beds = null!;
    private Furniture3D _furniture = null!;
    private Decor3D _decor = null!;
    private Life3D _life = null!;

    private int _rotation = -1;
    private int _floor = -1;
    private bool _plan;
    private int _builtVersion = -1;

    /// <summary>Where 3D parts that belong to the floor in view go (in raw grid coordinates).</summary>
    public Node3D Root => _root;

    /// <summary>The grounds' dressing: the outside walls, signs, car park and planting.</summary>
    public Outside3D Outside => _outside;

    public WorldLayer World => _world;

    /// <summary>How bright the enhanced lighting's fill light is now (0 with enhanced lighting off).</summary>
    public float FillEnergy => _fill.LightEnergy;

    public HospitalSimulation Simulation => _sim;

    public void Initialise(HospitalScene scene, HospitalSimulation simulation, WorldLayer world)
    {
        _scene = scene;
        _sim = simulation;
        _world = world;
        ProcessPriority = 100;

        _camera = new Camera3D
        {
            Name = "Camera3D",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Near = 0.5f,
            Far = 400f,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        };
        AddChild(_camera);

        // Light from the upper left of the screen, as the 2D art was shaded: the left faces lit, the right in shade.
        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightColor = new Color("#FFF6E8"),
            LightEnergy = 0.72f,
            ShadowEnabled = true,
            ShadowBias = 0.03f,
            ShadowNormalBias = 0.6f,
            ShadowBlur = 1.0f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 200f,
        };
        AddChild(_sun);

        // A weak, shadowless, cool light from the other side, so faces turned away from the sun are
        // not all one flat shade. Only lit with enhanced lighting on.
        _fill = new DirectionalLight3D { Name = "Fill", LightColor = new Color("#CFE0FF"), LightEnergy = 0f, ShadowEnabled = false };
        AddChild(_fill);

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.16f, 0.19f, 0.17f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#DCE6F0"),
            AmbientLightEnergy = 0.5f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            SsaoEnabled = true,
            SsaoRadius = 0.8f,
            SsaoIntensity = 1.4f,
            SsaoDetail = 0.5f,
        };
        _environment = new WorldEnvironment { Name = "Environment", Environment = environment };
        AddChild(_environment);
        ApplyLighting();

        _groundRoot = new Node3D { Name = "Grounds" };
        AddChild(_groundRoot);
        _root = new Node3D { Name = "Inside" };
        AddChild(_root);

        _ground = new Ground3D { Name = "Ground" };
        _groundRoot.AddChild(_ground);
        _ground.Initialise(_sim);

        _outside = new Outside3D { Name = "Outside" };
        _groundRoot.AddChild(_outside);
        _outside.Initialise(this, _sim);

        _floors = new FloorPainter { Name = "Floors" };
        _root.AddChild(_floors);
        _floors.Initialise(_sim);

        _walls = new Walls3D { Name = "Walls" };
        _root.AddChild(_walls);
        _walls.Initialise(this);

        _furniture = new Furniture3D { Name = "Furniture" };
        _root.AddChild(_furniture);
        _furniture.Initialise(this);

        _decor = new Decor3D { Name = "Decor" };
        _root.AddChild(_decor);
        _decor.Initialise(this);

        _life = new Life3D { Name = "Life" };
        _root.AddChild(_life);
        _life.Initialise(this);

        _beds = new Beds3D { Name = "Beds" };
        _root.AddChild(_beds);
        _beds.Initialise(this);

        _agents = new Agents3D { Name = "People" };
        _root.AddChild(_agents);
        _agents.Initialise(this);
    }

    public Agents3D People => _agents;

    public Beds3D Beds => _beds;

    public Furniture3D Furniture => _furniture;

    public Decor3D Decor => _decor;

    public Life3D Life => _life;

    /// <summary>How far into the night it is: 0 by day, 1 at night, easing across dawn and dusk.</summary>
    public float Night { get; private set; }

    /// <summary>Shows or hides the 3D world (and its camera).</summary>
    public void SetShown(bool on)
    {
        Visible = on;
        _camera.Current = on;
        _environment.Environment.BackgroundMode = on ? Godot.Environment.BGMode.Color : Godot.Environment.BGMode.ClearColor;
        if (on)
        {
            _builtVersion = -1;
        }
    }

    /// <summary>Walls cut down low so you can see into every room.</summary>
    public bool Cutaway
    {
        get => _walls.Cutaway;
        set
        {
            _walls.Cutaway = value;
            _builtVersion = -1;
        }
    }

    /// <summary>The floor grid lines (G) are painted into the floor.</summary>
    public bool ShowGrid
    {
        get => _floors.ShowGrid;
        set => _floors.ShowGrid = value;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        if (_rotation != IsoDraw.Rotation || _floor != IsoDraw.ViewFloor || _plan != IsoDraw.Plan)
        {
            _rotation = IsoDraw.Rotation;
            _floor = IsoDraw.ViewFloor;
            _plan = IsoDraw.Plan;
            PlaceRoots();
            _builtVersion = -1;
        }

        // Walls, doors and floors follow the 2D world's layout: rebuilt whenever it is.
        if (_builtVersion != _world.StaticVersion)
        {
            _builtVersion = _world.StaticVersion;
            _ground.Rebuild();
            _outside.Rebuild(_walls.Cutaway);
            _floors.Repaint();
            _walls.Rebuild();
            _beds.Rebuild();
            _furniture.Rebuild();
            _decor.Rebuild(_walls.Cutaway);
            _life.Rebuild();
        }

        Daylight();

        _walls.Animate(delta);
        _beds.Sync(delta);
        _furniture.Sync();
        _decor.Sync();
        _outside.Sync();
        _agents.Sync(delta);
        _life.Sync();
        PlaceCamera();
    }

    /// <summary>
    /// Sets the lighting quality from the player's settings. Enhanced: deeper contact shading where
    /// things meet, sharper and finer sun shadows, a soft fill light, smoothed edges and a slightly
    /// richer picture. Off: the plain lighting, for slower computers.
    /// </summary>
    private void ApplyLighting()
    {
        var on = GameManager.Instance?.Settings.EnhancedLighting ?? true;
        if (_enhanced == on)
        {
            return;
        }

        _enhanced = on;
        var env = _environment.Environment;
        env.SsaoRadius = on ? 1.0f : 0.8f;
        env.SsaoIntensity = on ? 2.6f : 1.4f;
        env.SsaoPower = on ? 1.6f : 1.5f;
        env.SsaoDetail = on ? 1.0f : 0.5f;
        env.AdjustmentEnabled = on;
        env.AdjustmentSaturation = 1.12f;
        env.AdjustmentContrast = 1.1f;
        env.AdjustmentBrightness = 0.97f;
        _sun.ShadowBlur = on ? 0.6f : 1.0f;
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        var viewport = GetViewport();
        viewport.Msaa3D = on ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
        viewport.ScreenSpaceAA = on ? Viewport.ScreenSpaceAAEnum.Smaa : Viewport.ScreenSpaceAAEnum.Disabled;
        RenderingServer.DirectionalShadowAtlasSetSize(on ? 8192 : 4096, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(on ? RenderingServer.ShadowQuality.SoftHigh : RenderingServer.ShadowQuality.SoftLow);
    }

    private static readonly Color DaySun = new("#FFF6E8");
    private static readonly Color DawnSun = new("#FFC98A");
    private static readonly Color NightSun = new("#8FA6D8");
    private static readonly Color DayAmbient = new("#DCE6F0");
    private static readonly Color NightAmbient = new("#B8C4E0");
    private static readonly Color DayGround = new(0.16f, 0.19f, 0.17f);
    private static readonly Color NightGround = new(0.06f, 0.08f, 0.12f);

    /// <summary>
    /// Follows the hospital clock: warm low sun morning and evening, a cool moonlit night. Inside stays
    /// well lit (the wards never go dark), so the night only dims and cools the light a little.
    /// </summary>
    private void Daylight()
    {
        var hour = _sim.Now.TimeOfDay.TotalHours;

        // 0 at night, 1 in the day, easing across dawn (6 to 8) and dusk (19 to 21).
        var day = (float)Math.Clamp(Math.Min((hour - 6) / 2, (21 - hour) / 2), 0, 1);
        day = day * day * (3 - (2 * day));
        Night = 1 - day;
        var low = 1 - Math.Abs((day * 2) - 1);
        var sun = NightSun.Lerp(DaySun, day).Lerp(DawnSun, low * 0.6f);
        _sun.LightColor = sun;
        _sun.LightEnergy = Mathf.Lerp(0.24f, 0.72f, day);
        ApplyLighting();
        _fill.LightEnergy = _enhanced == true ? Mathf.Lerp(0.05f, 0.12f, day) : 0f;
        var env = _environment.Environment;
        env.AmbientLightColor = NightAmbient.Lerp(DayAmbient, day);
        env.AmbientLightEnergy = Mathf.Lerp(0.38f, 0.5f, day);
        env.BackgroundColor = NightGround.Lerp(DayGround, day);
    }

    /// <summary>The view's quarter turn about the middle of the site, as a 3D transform of grid space.</summary>
    private static Transform3D Turn(double shift)
    {
        var (ox, oy) = IsoDraw.Rotate(0, -shift);
        var (ax, ay) = IsoDraw.Rotate(1, -shift);
        var (bx, by) = IsoDraw.Rotate(0, 1 - shift);
        var basis = new Basis(
            new Vector3((float)(ax - ox), 0, (float)(ay - oy)),
            Vector3.Up,
            new Vector3((float)(bx - ox), 0, (float)(by - oy)));
        return new Transform3D(basis, new Vector3((float)ox, 0, (float)oy));
    }

    private void PlaceRoots()
    {
        _groundRoot.Transform = Turn(0);
        _root.Transform = Turn(IsoDraw.FloorShift);
    }

    private void PlaceCamera()
    {
        var camera = _scene.Camera;
        var centre = camera.GetScreenCenterPosition();
        var zoom = camera.Zoom.X;
        var height = GetViewport().GetVisibleRect().Size.Y;
        if (IsoDraw.Plan)
        {
            var target = new Vector3(centre.X / IsoDraw.PlanTile, 0, centre.Y / IsoDraw.PlanTile);
            _camera.Size = height / (IsoDraw.PlanTile * zoom);
            _camera.Transform = new Transform3D(Basis.Identity, target + (Vector3.Up * CameraDistance)).LookingAt(target, Vector3.Forward);
            _sun.RotationDegrees = new Vector3(-62, -30, 0);
            _fill.RotationDegrees = new Vector3(-50, 150, 0);
            return;
        }

        // The point on the ground (in turned grid space) at the middle of the screen.
        var (a, b) = (centre.X / 32.0, centre.Y / 16.0);
        var look = new Vector3((float)((a + b) / 2), 0, (float)((b - a) / 2));
        var pitch = Mathf.DegToRad(Pitch);
        var back = new Vector3(Mathf.Cos(pitch) * Mathf.Sqrt(0.5f), Mathf.Sin(pitch), Mathf.Cos(pitch) * Mathf.Sqrt(0.5f));
        _camera.Size = height / (Kit3D.PxPerUnit * zoom);
        // Shadows only need to cover what is on screen: the closer the zoom, the sharper they are.
        _sun.DirectionalShadowMaxDistance = CameraDistance * 2f;
        _camera.Transform = new Transform3D(Basis.Identity, look + (back * CameraDistance)).LookingAt(look, Vector3.Up);

        // Sun over the viewer's left shoulder: shadows fall to the right and a little towards the viewer,
        // as the 2D art's drop shadows did.
        _sun.GlobalRotationDegrees = new Vector3(-52, -72, 0);
        _fill.GlobalRotationDegrees = new Vector3(-35, 110, 0);
    }
}
