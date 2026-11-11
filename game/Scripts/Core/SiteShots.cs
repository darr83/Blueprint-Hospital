using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of where the entrances and ambulance bays can go, run with:
/// godot --path game -- --site-shots=DIR (needs a display). Starts a game for each sample site plan,
/// brings in a few ambulances, and photographs the whole site from above and the bays close up.
/// </summary>
public partial class SiteShots : Node
{
    private static readonly (string Name, GameType Type, StartingHospital Start, SitePlan Plan, bool Corridor)[] Scenes =
    {
        ("1_usual_career", GameType.Career, StartingHospital.SmallEmergency, SitePlan.Standard, false),
        ("2_career_north_west", GameType.Career, StartingHospital.SmallEmergency, new SitePlan(WallSide.North, 34, WallSide.West, EmergencyDepartmentLayout.StarterAmbulanceAt(WallSide.North)), false),
        ("3_career_south_east_left", GameType.Career, StartingHospital.SmallEmergency, new SitePlan(WallSide.South, 6, WallSide.East, EmergencyDepartmentLayout.SpineAt), false),
        ("4_empty_west_north", GameType.Sandbox, StartingHospital.EmptyBuilding, new SitePlan(WallSide.West, 16, WallSide.North, 36), true),
        ("5_empty_east_south", GameType.Sandbox, StartingHospital.EmptyBuilding, new SitePlan(WallSide.East, 8, WallSide.South, 14), true),
    };

    private string _directory = "";
    private bool _planner;
    private int _plannerStep;
    private int _scene = -1;
    private int _frame;
    private HospitalScene? _hospital;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--planner-shots=", StringComparison.Ordinal))
            {
                var planner = new SiteShots { Name = "SiteShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--planner-shots=".Length..], _planner = true };
                GameManager.Instance.AddChild(planner);
                DirAccess.MakeDirRecursiveAbsolute(planner._directory);
                Callable.From(() => GameManager.Instance.StartNewGame("Planner Review", OS.GetCmdlineUserArgs().Contains("--planner-empty") ? StartingHospital.EmptyBuilding : StartingHospital.SmallEmergency, false, WardEmergencyFrequency.Off, OS.GetCmdlineUserArgs().Contains("--planner-empty") ? GameType.Sandbox : GameType.Career, chooseSite: true)).CallDeferred();
                return true;
            }

            if (arg.StartsWith("--site-shots=", StringComparison.Ordinal))
            {
                var shots = new SiteShots { Name = "SiteShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--site-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                DirAccess.MakeDirRecursiveAbsolute(shots._directory);
                Callable.From(shots.NextScene).CallDeferred();
                return true;
            }
        }

        return false;
    }

    private void NextScene()
    {
        _scene++;
        if (_scene >= Scenes.Length)
        {
            GD.Print("[site] Done.");
            GetTree().Quit(0);
            return;
        }

        var s = Scenes[_scene];
        _hospital = null;
        _frame = 0;
        GameManager.Instance.StartNewGame("Site Review", s.Start, false, WardEmergencyFrequency.Off, s.Type, s.Plan);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_planner)
        {
            DrivePlanner();
            return;
        }

        if (_scene < 0 || _scene >= Scenes.Length)
        {
            return;
        }

        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady() && h.Session.Site.Layout?.Plan == Scenes[_scene].Plan)
            {
                _hospital = h;
                _frame = 0;
                Prepare(h);
            }

            return;
        }

        var name = Scenes[_scene].Name;
        switch (_frame)
        {
            case 10:
                _hospital.SetPlanView(true);
                _hospital.Camera.SnapTo(IsoDraw.ToScreen(80, 60), 0.23f);
                break;
            case 40:
                Shot($"{name}_site");
                _hospital.SetPlanView(false);
                var bays = _hospital.Session.Simulation.AmbulanceBays;
                var door = _hospital.Session.Site.Buildings[0].Entrances.First(e => !e.IsAmbulanceEntrance).Tile;
                var x = (bays.Average(b => b.X + (b.Width / 2.0)) + door.X) / 2;
                var y = (bays.Average(b => b.Y + (b.Length / 2.0)) + door.Y) / 2;
                _hospital.Camera.SnapTo(IsoDraw.ToScreen(x, y), 0.42f);
                break;
            case 70:
                Shot($"{name}_view");
                NextScene();
                break;
        }
    }

    /// <summary>Hovers and clicks through the site plan: front doors north, ambulances west, then Start.</summary>
    private void DrivePlanner()
    {
        if (GetTree().CurrentScene is not HospitalScene h || !h.IsNodeReady())
        {
            _frame = 0;
            return;
        }

        if (h != _hospital)
        {
            _hospital = h;
            _frame = 0;
        }

        var empty = OS.GetCmdlineUserArgs().Contains("--planner-empty");
        var b = StarterSite.MainBuildingFootprint;
        var targets = empty
            ? new[] { (b.X + 2.0, b.Y + 12.0), (b.X + 30.0, b.Bottom + 2.0) }
            : new[] { (b.X + 40.0, b.Y - 2.0), (b.X - 3.0, b.Y + 20.0) };
        switch (_frame)
        {
            case 30 when _plannerStep < 2:
                var (tx, ty) = targets[_plannerStep];
                var screen = h.GetViewport().GetCanvasTransform() * IsoDraw.ToScreen(tx, ty);
                Input.WarpMouse(screen);
                break;
            case 45 when _plannerStep < 2:
                Shot($"planner_{_plannerStep + 1}_hover");
                var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = h.GetViewport().GetMousePosition(), GlobalPosition = h.GetViewport().GetMousePosition() };
                _plannerStep++;
                _frame = 0;
                _hospital = null;
                Input.ParseInputEvent(press);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = press.Position, GlobalPosition = press.Position });
                break;
            case 45:
                Shot("planner_3_ready");
                break;
            case 50:
                h.GetNode<Hospital.SitePlanner>("SitePlanner").Call("Finish");
                break;
            case 90:
                Shot("planner_4_started");
                GD.Print($"[site] plan {h.Session.Site.Layout?.Plan}");
                GetTree().Quit(0);
                break;
        }
    }

    private void Prepare(HospitalScene h)
    {
        h.Hud.Visible = false;
        if (h.GetNodeOrNull<CanvasItem>("TileCursor") is { } cursor)
        {
            cursor.Visible = false;
        }

        var sim = h.Session.Simulation;
        if (Scenes[_scene].Corridor)
        {
            BuildFromEntrance(sim);
        }

        for (var i = 0; i < 3; i++)
        {
            sim.SpawnPatient(ArrivalMethod.Ambulance);
        }

        var clock = h.Session.Clock;
        clock.SetSpeed(SimulationSpeed.Paused);
        for (var i = 0; i < 4000 && sim.Ambulances.Count(a => a.State == Simulation.Transport.AmbulanceState.AtBay) < 3; i++)
        {
            clock.Step();
        }

        GD.Print($"[site] {Scenes[_scene].Name}: ambulances {string.Join(", ", sim.Ambulances.Select(a => $"{a.State} {a.X:0},{a.Y:0}"))}");
    }

    /// <summary>A corridor straight in from the main entrance, with a waiting room off it, to show a corridor joining the entrance.</summary>
    private static void BuildFromEntrance(HospitalSimulation sim)
    {
        var b = sim.MainBuilding.Footprint;
        var e = sim.MainBuilding.Entrances.First(x => !x.IsAmbulanceEntrance);
        GridRect corridor, waiting;
        switch (e.Side)
        {
            case WallSide.West:
                corridor = new GridRect(b.X, e.Tile.Y, 30, 8);
                waiting = new GridRect(b.X + 10, e.Tile.Y + 8, 20, 14);
                break;
            case WallSide.East:
                corridor = new GridRect(b.Right - 30, e.Tile.Y, 30, 8);
                waiting = new GridRect(b.Right - 30, e.Tile.Y + 8, 20, 14);
                break;
            case WallSide.North:
                corridor = new GridRect(e.Tile.X, b.Y, 8, 24);
                waiting = new GridRect(e.Tile.X + 8, b.Y + 6, 20, 14);
                break;
            default:
                corridor = new GridRect(e.Tile.X, b.Bottom - 24, 8, 24);
                waiting = new GridRect(e.Tile.X + 8, b.Bottom - 20, 20, 14);
                break;
        }

        GD.Print($"[site] corridor: {string.Join(" ", sim.BuildRoom(new RoomPlan("corridor", corridor, WallSide.South)).Problems)}");
        GD.Print($"[site] waiting: {string.Join(" ", sim.BuildRoom(sim.AutoPlan("waiting_room", waiting)).Problems)}");
    }

    private void Shot(string name)
    {
        var path = $"{_directory}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[site] Saved {path}");
    }
}
