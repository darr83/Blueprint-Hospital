using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the play mode cursor, run with: godot --path game -- --cursor-shots=DIR [--cursor-3d]
/// (needs a display). Lets the hospital run for a while, then puts the mouse over empty floor, a member of
/// staff, a patient and a bed and photographs each.
/// </summary>
public partial class CursorShots : Node
{
    private string _directory = "";
    private bool _threeD;
    private int _frame;
    private int _shot;
    private HospitalScene? _hospital;
    private (string Name, Vector2 World)[] _targets = Array.Empty<(string, Vector2)>();

    public static bool Begin()
    {
        var args = OS.GetCmdlineUserArgs();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--cursor-shots=", StringComparison.Ordinal))
            {
                var shots = new CursorShots
                {
                    Name = "CursorShots",
                    ProcessMode = ProcessModeEnum.Always,
                    _directory = arg["--cursor-shots=".Length..],
                    _threeD = args.Contains("--cursor-3d"),
                };
                GameManager.Instance.Settings.View3D = shots._threeD;
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Cursor Review General")).CallDeferred();
                return true;
            }
        }

        return false;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                SmokeTest.FitOutWaitingRoom(h);
                for (var i = 0; i < 3; i++)
                {
                    h.Session.Simulation.SpawnPatient(i == 0 ? ArrivalMethod.Ambulance : ArrivalMethod.WalkIn);
                }

                h.Session.Clock.SetSpeed(SimulationSpeed.Fastest);
                _frame = 0;
            }

            return;
        }

        // Let patients arrive and settle into cubicles first.
        if (_targets.Length == 0)
        {
            if (_frame < 240)
            {
                return;
            }

            _hospital.Session.Clock.SetSpeed(SimulationSpeed.Paused);
            var sim = _hospital.Session.Simulation;
            var world = _hospital.World;
            var list = new System.Collections.Generic.List<(string, Vector2)>();
            var bedSpace = sim.Spaces.FirstOrDefault(s => s.OccupantId is { } id && sim.FindPatient(id) is { } p && world.IsOnMap(p) && world.OnViewFloor(p)
                && s.Furniture.Any(f => sim.Database.Furniture.TryGetValue(f.ItemId, out var d) && d.Style == Simulation.Data.Models.FurnitureStyle.Bed));
            if (bedSpace is not null)
            {
                var patient = sim.FindPatient(bedSpace.OccupantId!.Value)!;
                list.Add(("patient_in_bed", world.FootPosition(patient) + new Vector2(0, _threeD ? -14 : 0)));
                var bed = bedSpace.Furniture.First(f => sim.Database.Furniture.TryGetValue(f.ItemId, out var d) && d.Style == Simulation.Data.Models.FurnitureStyle.Bed);
                var (x, y, w, l) = _hospital.Footprint(bedSpace, bed);
                list.Add(("bed", IsoDraw.ToScreen(x + (w / 2), y + (l * 0.85))));
            }

            if (sim.Staff.FirstOrDefault(m => m.Activity == StaffActivity.Working && world.OnViewFloor(m) && sim.Patients.All(p => !ReferenceEquals(p, m))) is { } staff)
            {
                list.Add(("staff", world.FootPosition(staff) + new Vector2(0, _threeD ? -30 : 0)));
            }

            if (sim.Patients.FirstOrDefault(p => sim.BedOf(p) is null && world.IsOnMap(p) && world.OnViewFloor(p) && !world.IsLying(p)) is { } walker)
            {
                list.Add(("patient", world.FootPosition(walker) + new Vector2(0, _threeD ? -30 : 0)));
            }

            list.Add(("floor", list.Count > 0 ? list[0].Item2 + new Vector2(_threeD ? 260 : 0, _threeD ? 30 : 250) : _hospital.Camera.Position));
            _targets = list.ToArray();
            _frame = 0;
            return;
        }

        if (_frame % 8 != 0)
        {
            return;
        }

        var index = _shot / 3;
        if (index >= _targets.Length)
        {
            GD.Print("[cursor] Done.");
            GetTree().Quit(0);
            return;
        }

        var (name, target) = _targets[index];
        switch (_shot % 3)
        {
            case 0:
                _hospital.Camera.SnapTo(name == "floor" ? target - new Vector2(_threeD ? 260 : 0, _threeD ? 30 : 250) : target, _threeD ? 1.8f : 1.6f);
                break;
            case 1:
                var screen = GetViewport().GetCanvasTransform() * target;
                Input.WarpMouse(screen);
                GetViewport().PushInput(new InputEventMouseMotion { Position = screen, GlobalPosition = screen });
                break;
            default:
                var image = GetViewport().GetTexture().GetImage();
                DirAccess.MakeDirRecursiveAbsolute(_directory);
                var path = $"{_directory}/cursor_{(_threeD ? "3d" : "2d")}_{index}_{name}.png";
                image.SavePng(path);
                var hover = _hospital.PlayHover;
                GD.Print($"[cursor] Saved {path} (hover: {hover.Agent?.GetType().Name ?? (hover.Bed is not null ? "bed" : "nothing")})");
                break;
        }

        _shot++;
    }
}
