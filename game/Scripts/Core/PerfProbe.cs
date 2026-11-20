using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// A performance check, run with: godot --path game -- --perf [--perf-3d]. Starts a new game, adds a
/// wing of clinics and a waiting room full of patients, then times what the player feels: a quiet
/// frame, scrolling, a frame after placing chairs or building a room, the checks run while the
/// mouse moves over the floor in Build mode, and trying out a finish in Decorate. Prints the
/// results (in milliseconds) and quits.
/// </summary>
public partial class PerfProbe : Node
{
    private HospitalScene? _hospital;
    private int _frame;
    private int _stage;
    private ulong _last;
    private readonly List<double> _frames = new();
    private string _measuring = "";
    private int _left;
    private Action? _after;
    private int _layer;
    private int _shotTaken;
    private static readonly string[] LayerNames = { "None", "Ground", "Floors", "WallBands", "Rooms", "Actors", "Doors", "Walls" };

    public static bool Begin()
    {
        var args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--perf") < 0)
        {
            return false;
        }

        GameManager.Instance.Settings.View3D = Array.IndexOf(args, "--perf-3d") >= 0;
        GameManager.Instance.AddChild(new PerfProbe { Name = "PerfProbe", ProcessMode = ProcessModeEnum.Always });
        Callable.From(() => GameManager.Instance.StartNewGame("Perf General", unlimitedMoney: true)).CallDeferred();
        return true;
    }

    private static void Say(string text) => GD.Print($"[perf] {text}");

    private static double Ms(Action action, int times = 1)
    {
        action();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < times; i++)
        {
            action();
        }

        return watch.Elapsed.TotalMilliseconds / times;
    }

    /// <summary>Times the next <paramref name="count"/> frames (wall clock between frames), calling <paramref name="each"/> before each.</summary>
    private void Measure(string what, int count, Action? each = null)
    {
        _measuring = what;
        _left = count;
        _frames.Clear();
        _after = each;
        _last = 0;
    }

    public override void _Process(double delta)
    {
        _frame++;
        var now = Time.GetTicksUsec();
        if (_measuring.Length > 0)
        {
            if (_last != 0)
            {
                _frames.Add((now - _last) / 1000.0);
            }

            _last = now;
            if (_frames.Count >= _left)
            {
                var sorted = _frames.OrderBy(f => f).ToList();
                Say($"{_measuring}: mean {sorted.Average():0.0} ms, median {sorted[sorted.Count / 2]:0.0}, worst {sorted[^1]:0.0} over {sorted.Count} frames");

                _measuring = "";
                _stage++;
                return;
            }

            _after?.Invoke();
            return;
        }

        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                Setup(h);
                _frame = 0;
            }

            return;
        }

        if (_frame < 10)
        {
            return;
        }

        var sim = _hospital.Session.Simulation;
        var world = _hospital.World;
        switch (_stage)
        {
            case 0:
                Say($"View: {(GameView.ThreeD ? "3D" : "2D")}; rooms {sim.Spaces.Count()}, chairs {sim.Chairs.Count}, people {sim.Patients.Count() + sim.Staff.Count()}");
                Measure("quiet frame", 60);
                break;
            case 1 when OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--perf-shot=", StringComparison.Ordinal)) is { } shot && _shotTaken < 2:
            {
                // A picture of the floors close up, to check they look the same after a change.
                if (_shotTaken == 0)
                {
                    var room = sim.Spaces.First(s => s.Kind == Simulation.Hospital.CareSpaceKind.StaffRoom);
                    _hospital.Camera.SnapTo(IsoDraw.ToScreen(room.Rect.X + 2, room.Rect.Y + 2), 2.2f);
                    _hospital.Hud.Visible = false;
                }
                else
                {
                    GetViewport().GetTexture().GetImage().SavePng(shot["--perf-shot=".Length..]);
                    _hospital.Hud.Visible = true;
                }

                _shotTaken++;
                break;
            }

            case 1:
            {
                var start = _hospital.Camera.Position;
                var t = 0;
                Measure("scrolling", 60, () => _hospital.Camera.SnapTo(start + new Vector2(++t * 12, t * 4), _hospital.Camera.Zoom.X));
                break;
            }

            case 2:
                Say($"WorldLayer.RebuildStatic: {Ms(world.RebuildStatic, 3):0.0} ms");
                _stage++;
                break;
            case 3:
            {
                // Hovering in Build mode: the chair check for a row of five, and a room check.
                var row = Enumerable.Range(0, 5).Select(i => new GridCoord(50 + i, 14)).ToList();
                Say($"CheckChair x5 (one hover): {Ms(() => row.ForEach(t => sim.CheckChair(t, WallSide.South, null!)), 5):0.00} ms");
                var at = new GridRect(44, 34, 8, 8);
                Say($"CheckRoomAt 8x8 (one hover): {Ms(() => sim.CheckRoomAt("cardiology_clinic", at, null, null), 5):0.00} ms ({string.Join("; ", sim.CheckRoomAt("cardiology_clinic", at, null, null).Problems)})");
                Say($"Check corridor 4x16 (one hover): {Ms(() => sim.CheckRoom(new RoomPlan("corridor", new GridRect(44, 30, 4, 16), WallSide.South)), 5):0.00} ms");
                Say($"AutoPlan 8x8: {Ms(() => sim.AutoPlan("cardiology_clinic", at, null, null), 5):0.00} ms");
                _stage++;
                break;
            }

            case 4:
            {
                // Place one chair a frame, as when clicking chairs down one after another.
                var tiles = new Queue<GridCoord>(Enumerable.Range(46, 50).Select(x => new GridCoord(x, 14)));

                Measure("placing a chair every frame", 20, () =>
                {
                    while (tiles.Count > 0 && !sim.PlaceChair(tiles.Dequeue(), WallSide.South, null!).CanBuild)
                    {
                    }
                });
                break;
            }

            case 5:
            {
                var room = sim.Spaces.First(s => s.Kind == Simulation.Hospital.CareSpaceKind.StaffRoom);
                _hospital.Hud.Build.Open(Simulation.Data.BuildMenu.Decorate);
                _hospital.Hud.ShowDecorate(room);
                var finishes = Enum.GetValues<Simulation.Hospital.FloorFinish>();
                var i = 0;
                Measure("trying a different finish every frame", 20, () => _hospital.Hud.Decorate.PickFloor(finishes[++i % finishes.Length]));
                break;
            }

            case 6 when Array.IndexOf(OS.GetCmdlineUserArgs(), "--perf-layers") >= 0 && _layer < LayerNames.Length:
            {
                _hospital.Hud.Decorate.Close();
                _hospital.Hud.Build.ClosePopup();
                var name = LayerNames[_layer];
                foreach (var n in LayerNames)
                {
                    if (_hospital.View.GetNodeOrNull<CanvasItem>(n) is { } layer)
                    {
                        layer.Visible = n != name;
                    }
                }

                _layer++;
                _stage--;
                Measure($"without {name}", 15);
                break;
            }

            case 6:
                _hospital.Hud.Decorate.Close();
                _hospital.Hud.Build.ClosePopup();
                Say("done");
                GetTree().Quit();
                _stage++;
                break;
        }
    }

    private static void Setup(HospitalScene h)
    {
        var sim = h.Session.Simulation;
        foreach (var dept in sim.Database.Departments.Keys)
        {
            sim.UnlockDepartment(dept);
        }

        sim.ExtendBuilding(new GridRect(40, 2, 60, 28));
        sim.ExtendBuilding(new GridRect(40, 30, 60, 20));
        sim.BuildRoom(new RoomPlan("corridor", new GridRect(40, 13, 4, 34), WallSide.South));
        sim.BuildRoom(new RoomPlan("corridor", new GridRect(44, 13, 56, 5), WallSide.South));
        var north = new[] { "fracture_clinic", "cardiology_clinic", "neurology_clinic", "gastro_clinic", "gynaecology_clinic" };
        var south = new[] { "outpatient_reception", "xray_room", "plaster_room", "physio_gym", "endoscopy_room" };
        for (var i = 0; i < 5; i++)
        {
            sim.BuildRoom(sim.AutoPlan(north[i], new GridRect(44 + (i * 11), 2, 11, 11)));
            sim.BuildRoom(sim.AutoPlan(south[i], new GridRect(44 + (i * 11), 18, 11, 12)));
        }

        for (var i = 0; i < 12; i++)
        {
            sim.SpawnPatient(i % 3 == 0 ? ArrivalMethod.Ambulance : ArrivalMethod.WalkIn);
        }

        for (var i = 0; i < 600; i++)
        {
            h.Session.Clock.Step();
        }
    }
}
