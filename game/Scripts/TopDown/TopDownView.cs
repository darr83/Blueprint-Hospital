using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Core;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>
/// The hospital seen from directly above, in 2D. Layers from the ground up: the grounds, the room
/// floors, a slot for the selection outline, furniture, people and vehicles, door leaves, and the
/// walls with their signs and room names on top. Each layer draws in plain grid coordinates; its
/// transform carries the quarter turns of the view and the floor being looked at. The layers that
/// stay put are redrawn only when the layout, the view or the display settings change.
/// </summary>
public partial class TopDownView : Node2D
{
    private static readonly Color NightTint = new("#5F6FA3");
    private static readonly Color Overcast = new("#A9B1BA");
    private static readonly Color HeatTint = new("#FFE2B0");

    private HospitalSimulation _sim = null!;
    private WorldLayer _world = null!;
    private GroundLayer _ground = null!;
    private DepartmentRenderer _floors = null!;
    private BandsLayer _bands = null!;
    private RoomsLayer _rooms = null!;
    private ActorsLayer _actors = null!;
    private DoorsLayer _doors = null!;
    private WallsLayer _walls = null!;
    private (int Version, int Rotation, int Floor, bool Shadows, bool Detailed, bool Grid)? _built;
    private int _decorBuilt = -1;
    private (int Rotation, int Floor, bool Shadows, bool Detailed, string Site)? _groundBuilt;
    private bool _showGrid;
    private int _groundWeather = -1;

    /// <summary>Where the scene puts what lies on the floor under the furniture: the outline of the selected room.</summary>
    public Node2D FloorSlot { get; private set; } = null!;

    public GroundLayer Ground => _ground;

    public RoomsLayer Rooms => _rooms;

    public ActorsLayer Actors => _actors;

    public WallsLayer Walls => _walls;

    public DoorsLayer Doors => _doors;

    /// <summary>The fine half-metre grid over the floors (G).</summary>
    public bool ShowGrid
    {
        get => _showGrid;
        set => _showGrid = value;
    }

    /// <summary>Whether shadows are being drawn (the lighting setting).</summary>
    public bool Shadows => _built?.Shadows ?? true;

    /// <summary>Whether the grounds are drawn with their fine detail (the surface detail setting).</summary>
    public bool Detailed => _built?.Detailed ?? true;

    /// <summary>How dark it is outside, 0 in daylight to 1 at night.</summary>
    public float Night { get; private set; }

    /// <summary>How many stretches of wall are drawn on the floor in view.</summary>
    public int WallCount => _walls.Plan.Walls.Count;

    /// <summary>Every word on a sign or painted on the ground outside.</summary>
    public IEnumerable<string> SignTexts => _walls.SignTexts.Concat(_ground.Lettering);

    public void Initialise(HospitalSimulation simulation, WorldLayer world)
    {
        _sim = simulation;
        _world = world;

        _ground = new GroundLayer { Name = "Ground" };
        AddChild(_ground);
        _ground.Initialise(simulation);

        _floors = new DepartmentRenderer { Name = "Floors" };
        AddChild(_floors);
        _floors.Initialise(simulation);

        FloorSlot = new Node2D { Name = "OnTheFloor" };
        AddChild(FloorSlot);

        _bands = new BandsLayer { Name = "WallBands" };
        AddChild(_bands);
        _bands.Initialise(simulation, world);

        _rooms = new RoomsLayer { Name = "Rooms" };
        AddChild(_rooms);
        _rooms.Initialise(simulation, world);

        _actors = new ActorsLayer { Name = "Actors" };
        AddChild(_actors);
        _actors.Initialise(simulation, world, _rooms);

        _doors = new DoorsLayer { Name = "Doors" };
        AddChild(_doors);
        _doors.Initialise(world);

        _walls = new WallsLayer { Name = "Walls" };
        AddChild(_walls);
        _walls.Initialise(simulation, world);

        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_sim is null)
        {
            return;
        }

        Refresh();

        // Evening falls over the grounds (earlier in winter, later in summer); inside, the lights stay on.
        // A grey sky dims them a little by day and a heatwave warms them.
        var day = WeatherLook.Daylight(_world.Clock.Now);
        Night = Shadows ? 1f - day : 0f;
        var cloud = System.Math.Max(WeatherLook.Cloud, WeatherLook.Fog * 0.8f);
        _ground.Modulate = Colors.White.Lerp(NightTint, Night * 0.5f).Lerp(Overcast, cloud * 0.25f * day).Lerp(HeatTint, WeatherLook.Heat * 0.12f * day);
    }

    /// <summary>
    /// Redraws the layers that stay put if the layout, the view or the display settings have changed.
    /// A change of floor or wall finish alone repaints only the floors and the bands round the walls.
    /// </summary>
    public void Refresh()
    {
        var settings = GameManager.Instance?.Settings;
        var key = (_world.StaticVersion, IsoDraw.Rotation, IsoDraw.ViewFloor, settings?.EnhancedLighting ?? true, settings?.SurfaceTextures ?? true, _showGrid);
        if (_groundBuilt is not null && _groundWeather != WeatherLook.GroundVersion)
        {
            // Snow, frost, parched grass or wet roads: only the grounds are drawn again.
            _groundWeather = WeatherLook.GroundVersion;
            _ground.QueueRedraw();
        }

        if (key == _built)
        {
            if (_decorBuilt != _world.DecorVersion)
            {
                _decorBuilt = _world.DecorVersion;
                _floors.Repaint();
                _bands.QueueRedraw();
            }

            return;
        }

        _built = key;
        _decorBuilt = _world.DecorVersion;
        var (_, _, _, shadows, detailed, grid) = key;

        _ground.Transform = Pen.GridFrame(IsoDraw.ToScreenGround);
        var floor = Pen.GridFrame(IsoDraw.ToScreen);
        foreach (var layer in new Node2D[] { _bands, _rooms, _actors, _doors, _walls })
        {
            layer.Transform = floor;
        }

        var plan = WallPlan.Build(_world, _sim);
        // The grounds take the longest to draw and only change when the site does, not for each room or chair.
        var groundKey = (IsoDraw.Rotation, IsoDraw.ViewFloor, shadows, detailed, _world.SiteSignature());
        if (groundKey != _groundBuilt)
        {
            _groundBuilt = groundKey;
            _groundWeather = WeatherLook.GroundVersion;
            _ground.Shadows = shadows;
            _ground.Detailed = detailed;
            _ground.QueueRedraw();
        }

        _floors.Repaint();
        _bands.Shadows = shadows;
        _bands.QueueRedraw();
        _rooms.Shadows = shadows;
        _rooms.Detailed = detailed;
        _rooms.ShowGrid = grid;
        _rooms.Plan = plan;
        _rooms.Rebuild();
        _actors.Shadows = shadows;
        _doors.Plan = plan;
        _walls.Shadows = shadows;
        _walls.Plan = plan;
        _walls.QueueRedraw();
    }

    /// <summary>The patient, visitor or member of staff under a point in the world, if any.</summary>
    public Agent? HitTest(Vector2 world)
    {
        var (gx, gy) = IsoDraw.ScreenToGrid(world);
        return _actors.HitTest(gx, gy);
    }

    /// <summary>The doctor the player is walking about as, marked with a ring.</summary>
    public StaffMember? Player
    {
        get => _actors.Player;
        set => _actors.Player = value;
    }
}
