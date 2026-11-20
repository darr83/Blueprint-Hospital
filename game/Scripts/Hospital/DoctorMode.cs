using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;
using BlueprintHospital.UI;
using BlueprintHospital.World3D;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Be A Doctor: the player is one of the doctors.
/// In the 2D game they are seen from above like everyone else: the arrow keys (or W A S D) walk them
/// up, down, left and right on the screen, the view follows them from floor to floor and a line on
/// the floor leads to where they are heading.
/// In the 3D game the player sees through the doctor's eyes: the keys and mouse walk and look, and
/// arrows on the floor lead the way.
/// Everything else is handed to the <see cref="DoctorHud"/>.
/// </summary>
public partial class DoctorMode : Node
{
    /// <summary>Walking and hurrying, in tiles per real second (a tile is half a metre).</summary>
    public const double WalkTilesPerSecond = 5;
    public const double HurryTilesPerSecond = 8;

    /// <summary>How close the view comes when the player becomes a doctor; the wheel changes it.</summary>
    public const float StartZoom = 1.5f;
    private const float NearestZoom = 3f;
    private const float FurthestZoom = 0.7f;

    private const float MouseDegreesPerPixel = 0.16f;
    private const float KeyTurnDegreesPerSecond = 110f;

    private HospitalScene _scene = null!;
    private GameSession _session = null!;
    private HospitalSimulation _sim = null!;
    private DoctorHud _hud = null!;
    private DoctorRoute? _route;
    private bool _firstPerson;
    private float _yaw;
    private float _pitch;
    private bool _pointer;
    private bool _pointerWanted;
    private bool _dragLook;
    private float _zoom = StartZoom;
    private (Vector2 Position, float Zoom) _cameraBefore;
    private double _routeRefresh;
    private GridCoord? _routeTarget;
    private GridCoord _routeFrom;

    /// <summary>True while the player is a doctor.</summary>
    public bool Active { get; private set; }

    /// <summary>Which way the doctor faces, in degrees (0 faces towards smaller y).</summary>
    public float Yaw => _yaw;

    /// <summary>The 3D game: true when the mouse is a pointer for the panel instead of turning the head.</summary>
    public bool PointerShown => _pointer;

    /// <summary>The 2D game: the tiles of the line on the floor to where the doctor is heading, on the floor in view.</summary>
    public IReadOnlyList<GridCoord> Route => _route?.Tiles ?? Array.Empty<GridCoord>();

    /// <summary>Raised after the player becomes a doctor, and after they stop.</summary>
    public event Action<bool>? Changed;

    public void Initialise(HospitalScene scene, GameSession session, DoctorHud hud)
    {
        _scene = scene;
        _session = session;
        _sim = session.Simulation;
        _hud = hud;
        ProcessMode = ProcessModeEnum.Always;
        hud.LeaveRequested += Exit;
        hud.WalkRequested += WalkThere;
        _firstPerson = scene.ThreeD;
        if (_firstPerson)
        {
            hud.PointerNeeded += needed =>
            {
                if (Active)
                {
                    SetPointer(needed || _pointerWanted);
                }
            };
        }
        else
        {
            _route = new DoctorRoute { Name = "DoctorRoute" };
            scene.View.FloorSlot.AddChild(_route);
        }
    }

    /// <summary>The player becomes <paramref name="member"/>.</summary>
    public PlayerResult Enter(StaffMember member)
    {
        var result = _sim.TakeControl(member.Id);
        if (!result.Ok)
        {
            return result;
        }

        Active = true;
        _yaw = YawOf(member.Facing);
        _pitch = -4;
        _pointerWanted = false;
        _routeTarget = null;
        _routeRefresh = 0;
        _zoom = StartZoom;
        var camera = _scene.Camera.CaptureState();
        _cameraBefore = (new Vector2((float)camera.X, (float)camera.Y), (float)camera.Zoom);
        if (_session.Clock.IsPaused)
        {
            _session.Clock.SetSpeed(SimulationSpeed.Normal);
        }

        _hud.Open();
        _hud.Toast(result.Message);
        if (_firstPerson)
        {
            SetPointer(false);
            Changed?.Invoke(true);
            PlaceEye(member);
        }
        else
        {
            _scene.View.Player = member;
            Changed?.Invoke(true);
            Follow(member, 1);
        }

        return result;
    }

    /// <summary>Back to running the hospital.</summary>
    public void Exit()
    {
        if (!Active)
        {
            return;
        }

        Active = false;
        var member = _sim.PlayerDoctor;
        _sim.ReleaseControl();
        _hud.Close();
        if (_firstPerson)
        {
            _scene.View3D.FirstPersonParts.ShowRoute(Array.Empty<GridCoord>());
            _scene.View3D.FirstPerson = null;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else
        {
            _route!.Show(Array.Empty<GridCoord>());
            _scene.View.Player = null;
            _scene.View.Actors.PlayerWalking = false;
            _scene.Camera.SnapTo(_scene.Camera.Position, _cameraBefore.Zoom);
        }
        Changed?.Invoke(false);
        if (member is not null)
        {
            _scene.SelectStaff(member, centre: true);
        }
    }

    private static float YawOf((double X, double Y) facing) => Mathf.RadToDeg((float)Math.Atan2(-facing.X, -facing.Y));

    private void SetPointer(bool shown)
    {
        _pointer = shown;
        _dragLook = false;
        Input.MouseMode = shown ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    /// <summary>G: the doctor walks themselves to where the line leads.</summary>
    public void WalkThere()
    {
        if (_hud.Target is not { } target)
        {
            _hud.Toast("Pick a job or a patient first.");
            return;
        }

        if (_session.Clock.IsPaused)
        {
            _session.Clock.SetSpeed(SimulationSpeed.Normal);
        }

        if (!_sim.PlayerWalkTo(target))
        {
            _hud.Toast("There is no way there from here.");
        }
    }

    public override void _Process(double delta)
    {
        if (!Active)
        {
            return;
        }

        // Shift over, let go, or the game took the doctor back: return to running the hospital.
        if (_sim.PlayerDoctor is not { } member)
        {
            Exit();
            return;
        }

        var walked = false;
        if (!_hud.Typing && !_hud.HintOpen)
        {
            walked = Walk(member, delta);
        }

        if (_firstPerson)
        {
            if (member.IsMoving)
            {
                // Walking by themselves: look where they are going.
                var want = YawOf(member.Facing);
                _yaw = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(_yaw), Mathf.DegToRad(want), (float)Math.Min(1, delta * 6)));
                _pitch = Mathf.Lerp(_pitch, -4, (float)Math.Min(1, delta * 3));
            }

            PlaceEye(member);
        }
        else
        {
            _scene.View.Actors.PlayerWalking = walked;
            _yaw = YawOf(member.Facing);
            Follow(member, (float)Math.Min(1, delta * 9));
        }

        UpdateRoute(member, delta);
        UpdateTag();
    }

    private bool Walk(StaffMember member, double delta)
    {
        if (_firstPerson)
        {
            var turn = (Input.IsKeyPressed(Key.Left) ? 1 : 0) - (Input.IsKeyPressed(Key.Right) ? 1 : 0);
            if (turn != 0)
            {
                _yaw += turn * KeyTurnDegreesPerSecond * (float)delta;
            }

            var forward = (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) ? 1 : 0) - (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) ? 1 : 0);
            var strafe = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
            if ((forward == 0 && strafe == 0) || _session.Clock.IsPaused)
            {
                return false;
            }

            var (sx, sy) = Step(_yaw, forward, strafe, (Input.IsKeyPressed(Key.Shift) ? HurryTilesPerSecond : WalkTilesPerSecond) * delta);
            _sim.PlayerMove(sx, sy);
            return true;
        }

        var right = (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A) ? 1 : 0);
        var down = (Input.IsKeyPressed(Key.Down) || Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.W) ? 1 : 0);
        if ((right == 0 && down == 0) || _session.Clock.IsPaused)
        {
            return false;
        }

        var (dx, dy) = Step(right, down, (Input.IsKeyPressed(Key.Shift) ? HurryTilesPerSecond : WalkTilesPerSecond) * delta);
        _sim.PlayerMove(dx, dy);
        return true;
    }

    /// <summary>
    /// How far a step moves on the grid: <paramref name="right"/> and <paramref name="down"/> are
    /// directions on the screen, so the arrow keys always move the doctor the way they point however
    /// the view is turned.
    /// </summary>
    public static (double Dx, double Dy) Step(double right, double down, double distance)
    {
        var length = Math.Sqrt((right * right) + (down * down));
        if (length < 1e-9)
        {
            return (0, 0);
        }

        var (ox, oy) = IsoDraw.ScreenToGrid(Vector2.Zero);
        var (gx, gy) = IsoDraw.ScreenToGrid(new Vector2((float)(right / length), (float)(down / length)) * IsoDraw.PlanTile);
        return ((gx - ox) * distance, (gy - oy) * distance);
    }

    /// <summary>The 3D game: how far a step forward and sideways moves on the grid when looking along <paramref name="yaw"/>.</summary>
    public static (double Dx, double Dy) Step(float yaw, double forward, double strafe, double distance)
    {
        var a = Mathf.DegToRad(yaw);
        var (fx, fy) = (-Math.Sin(a), -Math.Cos(a));
        var (rx, ry) = (Math.Cos(a), -Math.Sin(a));
        var (x, y) = ((fx * forward) + (rx * strafe), (fy * forward) + (ry * strafe));
        var length = Math.Sqrt((x * x) + (y * y));
        return length < 1e-9 ? (0, 0) : (x / length * distance, y / length * distance);
    }

    /// <summary>For the smoke test: walks the way the doctor was last turned for a moment.</summary>
    public void StepForwardForTest(double tiles)
    {
        var a = Mathf.DegToRad(_yaw);
        _sim.PlayerMove(-Math.Sin(a) * tiles, -Math.Cos(a) * tiles);
    }

    /// <summary>For the smoke test: turns the doctor (0 faces towards smaller y).</summary>
    public void LookForTest(float yaw) => _yaw = yaw;

    /// <summary>The 3D game, for screenshots and the smoke test: turns the player's head.</summary>
    public void LookForTest(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
        if (_sim.PlayerDoctor is { } member)
        {
            PlaceEye(member);
        }
    }

    /// <summary>The 3D game: puts the camera at the doctor's eye, on whichever floor they are on.</summary>
    private void PlaceEye(StaffMember member)
    {
        // Walking by themselves they glide between ticks; under the player's keys they are exactly where the keys put them.
        var (x, y) = member.IsMoving ? _scene.World.FootGrid(member) : (member.X, member.Y);
        var floor = _sim.FloorOf(member);
        if (floor != _scene.ViewFloor)
        {
            _scene.ShowFloor(floor);
        }

        _scene.View3D.FirstPerson = new View3D.Eye(x, y, _yaw, _pitch, member);
    }

    /// <summary>Keeps the doctor in the middle of the part of the screen the panel leaves free, on whichever floor they are on.</summary>
    private void Follow(StaffMember member, float weight)
    {
        var floor = _sim.FloorOf(member);
        if (floor != _scene.ViewFloor)
        {
            _scene.ShowFloor(floor);
            weight = 1;
        }

        // Walking by themselves they glide between ticks; under the player's keys they are exactly where the keys put them.
        var camera = _scene.Camera;
        var zoom = Mathf.Lerp(camera.Zoom.X, _zoom, weight);
        camera.SnapTo(camera.Position.Lerp(CameraSpot(member, zoom), weight), zoom);
    }

    /// <summary>Where the middle of the screen should be to keep <paramref name="member"/> in the middle of the part the panel leaves free.</summary>
    public Vector2 CameraSpot(StaffMember member, float? zoom = null)
    {
        var (x, y) = member.IsMoving ? _scene.World.FootGrid(member) : (member.X, member.Y);
        return IsoDraw.ToScreen(x, y) + new Vector2(DoctorHud.PanelWidth / 2f / (zoom ?? _zoom), 0);
    }

    private void UpdateRoute(StaffMember member, double delta)
    {
        _routeRefresh -= delta;
        var target = _hud.Target;
        if (_routeRefresh > 0 && target == _routeTarget && member.Tile == _routeFrom)
        {
            return;
        }

        _routeRefresh = 0.5;
        _routeTarget = target;
        _routeFrom = member.Tile;
        IReadOnlyList<GridCoord> route = target is { } to && to != member.Tile ? _sim.PlayerRouteTo(to) : Array.Empty<GridCoord>();

        // Only the stretch on this floor: the line leads to the lift, and carries on upstairs.
        var floor = _sim.FloorOf(member);
        var here = route.TakeWhile(t => _sim.Site.FloorOf(t) == floor).ToList();
        if (_firstPerson)
        {
            _scene.View3D.FirstPersonParts.ShowRoute(here);
        }
        else
        {
            _route!.Show(here);
        }
    }

    private void UpdateTag()
    {
        if (_hud.TargetPatient is not { IsInDepartment: true } patient || _sim.FloorOf(patient) != _scene.ViewFloor)
        {
            _hud.PlaceTag(null, string.Empty, string.Empty);
            return;
        }

        var (x, y) = _scene.World.FootGrid(patient);
        Vector2 at;
        if (_firstPerson)
        {
            var camera = _scene.View3D.Camera;
            var point = _scene.View3D.WorldPoint(x, y, Kit3D.M(_scene.World.IsLying(patient) ? 1.25 : 1.95));
            if (camera.IsPositionBehind(point))
            {
                _hud.PlaceTag(null, string.Empty, string.Empty);
                return;
            }

            at = camera.UnprojectPosition(point);
        }
        else
        {
            var lift = _scene.World.IsLying(patient) ? 2.4f : 1.2f;
            at = GetViewport().GetCanvasTransform() * (IsoDraw.ToScreen(x, y) - new Vector2(0, lift * IsoDraw.PlanTile));
        }

        var screen = GetViewport().GetVisibleRect().Size;
        if (at.X < 60 || at.X > screen.X - DoctorHud.PanelWidth - 60 || at.Y < 110 || at.Y > screen.Y - 60)
        {
            // Out of sight (or behind the panel): no tag stuck to the edge of the screen.
            _hud.PlaceTag(null, string.Empty, string.Empty);
            return;
        }

        var where = _sim.Spaces.Where(s => s.Rect.Contains(patient.Tile) && s.Kind != BlueprintHospital.Simulation.Hospital.CareSpaceKind.Corridor).Select(s => s.Name).FirstOrDefault() ?? "In the corridor";
        _hud.PlaceTag(at, patient.FullName, where);
    }

    /// <summary>Keys and mouse while the player is a doctor. Everything is swallowed so the map underneath is not clicked.</summary>
    public void HandleInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion when _firstPerson && (!_pointer || _dragLook):
                _yaw -= motion.Relative.X * MouseDegreesPerPixel;
                _pitch = Mathf.Clamp(_pitch - (motion.Relative.Y * MouseDegreesPerPixel), -70, 70);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right when _firstPerson && _pointer:
                _dragLook = right.Pressed;
                break;
            case InputEventMouseButton when _firstPerson:
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _zoom = Math.Min(NearestZoom, _zoom * 1.15f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _zoom = Math.Max(FurthestZoom, _zoom / 1.15f);
                break;
            case InputEventKey { Pressed: true, Echo: false } key when !_hud.Typing:
                OnKey(key.Keycode);
                break;
        }
    }

    private void OnKey(Key key)
    {
        switch (key)
        {
            case Key.Escape when _hud.HintOpen:
                _hud.CloseHint();
                break;
            case Key.Escape:
                Exit();
                break;
            case Key.Tab when _firstPerson:
                _pointerWanted = !_pointer;
                SetPointer(_pointerWanted || _hud.HintOpen);
                break;
            case Key.H:
                _hud.ToggleHint();
                break;
            case Key.E when !_hud.HintOpen:
                if (!_hud.StartJob())
                {
                    if (_sim.PlayerFloorLinks() is { Count: > 0 } links)
                    {
                        _sim.PlayerTakeLink(links[0]);
                    }
                    else if (_hud.Target is not null)
                    {
                        _hud.Toast("Get closer first. G walks you there.");
                    }
                }

                break;
            case Key.G when !_hud.HintOpen:
                WalkThere();
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
        }
    }

    /// <summary>The line on the floor from the doctor to where they are heading: chevrons that run along it, and a ring at the end.</summary>
    private sealed partial class DoctorRoute : Node2D
    {
        private static readonly Color Line = new("#2BB3A3");
        private IReadOnlyList<GridCoord> _tiles = Array.Empty<GridCoord>();
        private double _time;

        public IReadOnlyList<GridCoord> Tiles => _tiles;

        public void Show(IReadOnlyList<GridCoord> tiles)
        {
            _tiles = tiles;
            QueueRedraw();
        }

        public override void _Process(double delta)
        {
            if (_tiles.Count > 0)
            {
                _time += delta;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            if (_tiles.Count == 0)
            {
                return;
            }

            var points = _tiles.Select(t => IsoDraw.ToScreen(t.X + 0.5, t.Y + 0.5)).ToArray();
            if (points.Length > 1)
            {
                DrawPolyline(points, new Color(Line, 0.28f), 9f, true);
            }

            // Chevrons every tile and a half, sliding towards the end.
            const float spacing = IsoDraw.PlanTile * 1.5f;
            var at = (float)(_time * 40 % spacing);
            for (var i = 1; i < points.Length; i++)
            {
                var (a, b) = (points[i - 1], points[i]);
                var length = a.DistanceTo(b);
                var dir = (b - a) / Math.Max(length, 0.001f);
                var side = new Vector2(-dir.Y, dir.X);
                while (at < length)
                {
                    var tip = a + (dir * at);
                    DrawPolyline(new[] { tip - (dir * 6) + (side * 6), tip, tip - (dir * 6) - (side * 6) }, Line, 3f, true);
                    at += spacing;
                }

                at -= length;
            }

            var end = points[^1];
            DrawCircle(end, 15, new Color(Line, 0.22f));
            DrawArc(end, 15, 0, Mathf.Tau, 28, Line, 3f, true);
        }
    }
}
