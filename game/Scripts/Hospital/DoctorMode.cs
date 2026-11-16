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
/// Be A Doctor: the player is one of the doctors and sees the hospital through their eyes. This turns
/// the keys and mouse into walking and looking, keeps the camera at the doctor's eye, draws the arrows
/// to where they are heading and hands everything else to the <see cref="DoctorHud"/>.
/// </summary>
public partial class DoctorMode : Node
{
    /// <summary>Walking and hurrying, in tiles per real second (a tile is half a metre).</summary>
    public const double WalkTilesPerSecond = 5;
    public const double HurryTilesPerSecond = 8;

    private const float MouseDegreesPerPixel = 0.16f;
    private const float KeyTurnDegreesPerSecond = 110f;

    private HospitalScene _scene = null!;
    private GameSession _session = null!;
    private HospitalSimulation _sim = null!;
    private DoctorHud _hud = null!;
    private float _yaw;
    private float _pitch;
    private bool _pointer;
    private bool _dragLook;
    private double _routeRefresh;
    private GridCoord? _routeTarget;
    private GridCoord _routeFrom;

    /// <summary>True while the player is a doctor.</summary>
    public bool Active { get; private set; }

    /// <summary>Which way the player is looking, in degrees (0 looks towards smaller y).</summary>
    public float Yaw => _yaw;

    /// <summary>True when the mouse is a pointer for the panel instead of turning the head.</summary>
    public bool PointerShown => _pointer;

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
        hud.PointerNeeded += needed =>
        {
            if (Active)
            {
                SetPointer(needed || _pointerWanted);
            }
        };
    }

    private bool _pointerWanted;

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
        _routeTarget = null;
        _routeRefresh = 0;
        _pointerWanted = false;
        if (_session.Clock.IsPaused)
        {
            _session.Clock.SetSpeed(SimulationSpeed.Normal);
        }

        _hud.Open();
        _hud.Toast(result.Message);
        SetPointer(false);
        Changed?.Invoke(true);
        PlaceEye(member);
        return result;
    }

    /// <summary>Back to running the hospital from above.</summary>
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
        _scene.View3D.FirstPersonParts.ShowRoute(Array.Empty<GridCoord>());
        _scene.View3D.FirstPerson = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
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

    /// <summary>G: the doctor walks themselves to where the arrows lead.</summary>
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

        // Shift over, let go, or the game took the doctor back: return to the view from above.
        if (_sim.PlayerDoctor is not { } member)
        {
            Exit();
            return;
        }

        if (!_hud.Typing && !_hud.HintOpen)
        {
            Walk(member, delta);
        }

        if (member.IsMoving)
        {
            // Walking by themselves: look where they are going.
            var want = YawOf(member.Facing);
            _yaw = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(_yaw), Mathf.DegToRad(want), (float)Math.Min(1, delta * 6)));
            _pitch = Mathf.Lerp(_pitch, -4, (float)Math.Min(1, delta * 3));
        }

        PlaceEye(member);
        UpdateRoute(member, delta);
        UpdateTag();
    }

    private void Walk(StaffMember member, double delta)
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
            return;
        }

        var (dx, dy) = Step(_yaw, forward, strafe, (Input.IsKeyPressed(Key.Shift) ? HurryTilesPerSecond : WalkTilesPerSecond) * delta);
        _sim.PlayerMove(dx, dy);
    }

    /// <summary>How far a step forward and sideways moves on the grid when looking along <paramref name="yaw"/>.</summary>
    public static (double Dx, double Dy) Step(float yaw, double forward, double strafe, double distance)
    {
        var a = Mathf.DegToRad(yaw);
        var (fx, fy) = (-Math.Sin(a), -Math.Cos(a));
        var (rx, ry) = (Math.Cos(a), -Math.Sin(a));
        var (x, y) = ((fx * forward) + (rx * strafe), (fy * forward) + (ry * strafe));
        var length = Math.Sqrt((x * x) + (y * y));
        return length < 1e-9 ? (0, 0) : (x / length * distance, y / length * distance);
    }

    /// <summary>For the smoke test: walks the way the player is looking for a moment.</summary>
    public void StepForwardForTest(double tiles)
    {
        var (dx, dy) = Step(_yaw, 1, 0, tiles);
        _sim.PlayerMove(dx, dy);
    }

    /// <summary>For screenshots and the smoke test: turns the player's head.</summary>
    public void LookForTest(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
        if (_sim.PlayerDoctor is { } member)
        {
            PlaceEye(member);
        }
    }

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

        // Only the stretch on this floor: the arrows lead to the lift, and carry on upstairs.
        var floor = _sim.FloorOf(member);
        _scene.View3D.FirstPersonParts.ShowRoute(route.TakeWhile(t => _sim.Site.FloorOf(t) == floor).ToList());
    }

    private void UpdateTag()
    {
        if (_hud.TargetPatient is not { IsInDepartment: true } patient || _sim.FloorOf(patient) != _scene.ViewFloor)
        {
            _hud.PlaceTag(null, string.Empty, string.Empty);
            return;
        }

        var (x, y) = _scene.World.FootGrid(patient);
        var camera = _scene.View3D.Camera;
        var point = _scene.View3D.WorldPoint(x, y, Kit3D.M(_scene.World.IsLying(patient) ? 1.25 : 1.95));
        if (camera.IsPositionBehind(point))
        {
            _hud.PlaceTag(null, string.Empty, string.Empty);
            return;
        }

        var at = camera.UnprojectPosition(point);
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

    /// <summary>Keys and mouse while the player is a doctor. Everything is swallowed so the view from above does not move.</summary>
    public void HandleInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion when !_pointer || _dragLook:
                _yaw -= motion.Relative.X * MouseDegreesPerPixel;
                _pitch = Mathf.Clamp(_pitch - (motion.Relative.Y * MouseDegreesPerPixel), -70, 70);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right when _pointer:
                _dragLook = right.Pressed;
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
            case Key.Tab:
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
}
