using System;
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
    private WorldEnvironment _environment = null!;

    /// <summary>The grounds: turned with the view, never shifted (they belong to the ground floor).</summary>
    private Node3D _groundRoot = null!;

    /// <summary>Everything inside: turned with the view and shifted so the floor in view sits on the grounds.</summary>
    private Node3D _root = null!;

    private Ground3D _ground = null!;
    private FloorPainter _floors = null!;
    private Walls3D _walls = null!;
    private Agents3D _agents = null!;

    private int _rotation = -1;
    private int _floor = -1;
    private bool _plan;
    private int _builtVersion = -1;

    /// <summary>Where 3D parts that belong to the floor in view go (in raw grid coordinates).</summary>
    public Node3D Root => _root;

    public WorldLayer World => _world;

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

        _groundRoot = new Node3D { Name = "Grounds" };
        AddChild(_groundRoot);
        _root = new Node3D { Name = "Inside" };
        AddChild(_root);

        _ground = new Ground3D { Name = "Ground" };
        _groundRoot.AddChild(_ground);
        _ground.Initialise(_sim);

        _floors = new FloorPainter { Name = "Floors" };
        _root.AddChild(_floors);
        _floors.Initialise(_sim);

        _walls = new Walls3D { Name = "Walls" };
        _root.AddChild(_walls);
        _walls.Initialise(this);

        _agents = new Agents3D { Name = "People" };
        _root.AddChild(_agents);
        _agents.Initialise(this);
    }

    public Agents3D People => _agents;

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
            _floors.Repaint();
            _walls.Rebuild();
        }

        _walls.Animate();
        _agents.Sync(delta);
        PlaceCamera();
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
    }
}
