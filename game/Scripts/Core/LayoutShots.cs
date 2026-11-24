using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of room layouts and doors, run with: godot --path game -- --layout-shots=DIR [--3d] (needs a display).
/// In an empty building: a big ward with its door in a side wall, a deep ward, a room built with no corridor to its
/// door (the red "Not in use" sign), the same room once a corridor reaches it, and a door being placed by hand.
/// </summary>
public partial class LayoutShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;
    private readonly List<(string Name, Action<HospitalScene> Set, double X, double Y, float Zoom)> _shots = new();

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--layout-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new LayoutShots { Name = "LayoutShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--layout-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Layout Review General", StartingHospital.EmptyBuilding, wardEmergencies: WardEmergencyFrequency.Off)).CallDeferred();
                return true;
            }
        }

        return false;
    }

    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static GridRect At(int x, int y, int w, int l) => new(B.X + x, B.Y + y, w, l);

    public override void _Process(double delta)
    {
        _frame++;
        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                h.Hud.Visible = false;
                if (h.GetNodeOrNull<CanvasItem>("TileCursor") is { } cursor)
                {
                    cursor.Visible = false;
                }

                h.Session.Clock.SetSpeed(SimulationSpeed.Paused);
                Plan(h.Session.Simulation);
                _frame = 0;
            }

            return;
        }

        if (_frame < 10 || _frame % 8 != 0)
        {
            return;
        }

        var index = _step / 2;
        if (index >= _shots.Count)
        {
            GD.Print("[layout] Done.");
            GetTree().Quit(0);
            return;
        }

        var (name, set, x, y, zoom) = _shots[index];
        if (_step % 2 == 0)
        {
            set(_hospital);
            _hospital.Camera.SnapTo(IsoDraw.ToScreen(x, y), zoom);
        }
        else
        {
            var image = GetViewport().GetTexture().GetImage();
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            var path = $"{_directory}/{name}.png";
            image.SavePng(path);
            GD.Print($"[layout] Saved {path}");
        }

        _step++;
    }

    private void Plan(HospitalSimulation sim)
    {
        sim.AdjustBalance(20_000_000);

        // A corridor up from the main entrance, through the middle of the building, and one along the bottom.
        Report(sim, "corridor", sim.BuildRoom(new RoomPlan("corridor", At(20, 0, 4, 40), WallSide.South)));
        Report(sim, "corridor", sim.BuildRoom(new RoomPlan("corridor", At(24, 34, 36, 4), WallSide.South)));

        // A ward 18 m long and 8 m deep with its door in its east wall, onto the middle corridor.
        Report(sim, "side ward", sim.BuildRoom(sim.AutoPlan("medical_ward", At(4, 2, 16, 36), doorSide: WallSide.East)));

        // A ward 18 m by 16 m, door onto the bottom corridor.
        Report(sim, "deep ward", sim.BuildRoom(sim.AutoPlan("emergency_ward", At(24, 2, 36, 32), doorSide: WallSide.South)));

        var centre = (B.X + 30.0, B.Y + 20.0);
        _shots.Add(("1_big_wards", _ => { }, centre.Item1, centre.Item2, 0.62f));
        _shots.Add(("2_side_ward_close", _ => { }, B.X + 12.0, B.Y + 20.0, 1.05f));
        _shots.Add(("3_deep_ward_close", _ => { }, B.X + 42.0, B.Y + 18.0, 1.0f));
        if (OS.GetCmdlineUserArgs().Contains("--before"))
        {
            return;
        }

        // A room with no corridor to its door: built, but not in use.
        _shots.Add(("4_not_in_use", h =>
        {
            var s = h.Session.Simulation;
            var side = s.Spaces.First(r => r.TemplateId == "medical_ward");
            Report(s, "demolish", s.DemolishRoom(side.Id));
            Report(s, "loose ward", s.BuildRoom(s.AutoPlan("medical_ward", At(2, 4, 14, 22), doorSide: WallSide.East, strict: true)));
        }, B.X + 10.0, B.Y + 15.0, 1.05f));

        // Lay a corridor to its door and it comes into use.
        _shots.Add(("5_joined_up", h =>
        {
            var s = h.Session.Simulation;
            Report(s, "link corridor", s.BuildRoom(new RoomPlan("corridor", At(16, 4, 4, 22), WallSide.South)));
        }, B.X + 10.0, B.Y + 15.0, 1.05f));

        // Placing the deep ward's door by hand, near the west end of its wall: the preview shows the new layout.
        _shots.Add(("6_place_door_preview", h =>
        {
            var s = h.Session.Simulation;
            var ward = s.Spaces.First(r => r.TemplateId == "emergency_ward");
            h.BuildTool.StartRoomDoor(ward.Id);
            h.BuildTool.PreviewAt(new GridCoord(ward.Rect.X + 5, ward.Rect.Bottom));
        }, B.X + 42.0, B.Y + 18.0, 1.0f));

        _shots.Add(("7_door_placed", h =>
        {
            var s = h.Session.Simulation;
            var ward = s.Spaces.First(r => r.TemplateId == "emergency_ward");
            h.BuildTool.Cancel();
            Report(s, "door", s.MoveDoorTo(ward.Id, new GridCoord(ward.Rect.X + 5, ward.Rect.Bottom)));
        }, B.X + 42.0, B.Y + 18.0, 1.0f));
    }

    private static void Report(HospitalSimulation sim, string what, BuildCheck check) =>
        GD.Print($"[layout] {what}: {(check.CanBuild ? $"ok £{check.Cost:N0}" : string.Join("; ", check.Problems))} · beds: {string.Join(", ", sim.Spaces.Where(s => s.IsWard).Select(s => $"{s.Name} {s.Beds.Count}"))}");
}
