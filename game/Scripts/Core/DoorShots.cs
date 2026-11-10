using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>Mock-up shots for choosing a room's door wall, run with: godot --path game -- --door-shots=DIR (needs a display).</summary>
public partial class DoorShots : Node
{
    private string _directory = string.Empty;
    private int _frame;
    private int _step;
    private HospitalScene? _h;
    private string _resus3 = string.Empty;

    public static bool Begin()
    {
        var arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--door-shots=", StringComparison.Ordinal));
        if (arg is null)
        {
            return false;
        }

        GameManager.Instance.AddChild(new DoorShots { Name = "DoorShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--door-shots=".Length..] });
        Callable.From(() => GameManager.Instance.StartNewGame("Door Review General")).CallDeferred();
        return true;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_h is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _h = h;
                Setup(h);
                _frame = 0;
            }

            return;
        }

        if (_frame % 8 != 0)
        {
            return;
        }

        var sim = _h.Session.Simulation;
        var view = (IsoDraw.ToScreen(61, 22), 1.0f);
        var panelView = (IsoDraw.ToScreen(61, 22) + new Vector2(170, 20), 0.95f);
        switch (_step++)
        {
            case 0:
                _h.Hud.Visible = false;
                _h.Camera.SnapTo(view.Item1, view.Item2);
                break;
            case 1:
                Save("1_before");
                break;
            case 2:
            {
                // Move or resize Resus 3, then press Z until the door is on the corridor wall like the others.
                _h.SetMode(GameMode.Build);
                _h.Hud.HideRightPanels();
                var room = sim.FindSpace(_resus3)!;
                _h.BuildTool.StartRoom(sim.Database.RoomTemplates[room.TemplateId], _resus3);
                _h.BuildTool.PreviewDrag(new GridCoord(room.Rect.X, room.Rect.Y), new GridCoord(room.Rect.Right - 1, room.Rect.Bottom - 1));
                for (var i = 0; i < 4 && _h.BuildTool.PlannedDoorSide != WallSide.North; i++)
                {
                    _h.BuildTool.Turn();
                }

                GD.Print($"[door] planned {_h.BuildTool.PlannedDoorSide}, check {string.Join("; ", _h.BuildTool.Check?.Problems ?? Array.Empty<string>())}");
                break;
            }

            case 3:
                Save("2_z_key_preview");
                break;
            case 4:
            {
                _h.BuildTool.Cancel();
                _h.BuildTool.FollowMouse = false;
                var room = sim.FindSpace(_resus3)!;
                _h.Hud.Visible = true;
                if (_h.Hud.BoardShown)
                {
                    _h.Hud.ToggleBoard();
                }

                _h.Hud.ShowRoom(room);
                _h.Camera.SnapTo(panelView.Item1, panelView.Item2);
                break;
            }

            case 5:
                Save("3_room_panel_before");
                break;
            case 6:
                GD.Print($"[door] move: {string.Join("; ", sim.MoveDoor(_resus3, WallSide.North).Problems)}");
                _h.Hud.ShowRoom(sim.FindSpace(_resus3)!);
                break;
            case 7:
                Save("4_room_panel_after");
                break;
            case 8:
                _h.Hud.Visible = false;
                _h.SetMode(GameMode.Play);
                _h.Camera.SnapTo(view.Item1, view.Item2);
                break;
            case 9:
                Save("5_after");
                break;
            default:
                GetTree().Quit(0);
                break;
        }
    }

    private void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        GetViewport().GetTexture().GetImage().SavePng($"{_directory}/{name}.png");
        GD.Print($"[door] saved {name} cam {_h!.Camera.Position} {_h.Camera.Zoom}");
    }

    private void Setup(HospitalScene h)
    {
        var sim = h.Session.Simulation;
        sim.AdjustBalance(10_000_000);
        void Report(string what, BuildCheck c) => GD.Print(c.CanBuild ? $"[door] built {what}" : $"[door] {what} refused: {string.Join("; ", c.Problems)}");
        Report("extension", sim.ExtendBuilding(new GridRect(40, 2, 60, 28)));
        Report("link", sim.BuildRoom(new RoomPlan("corridor", new GridRect(40, 13, 4, 34), WallSide.South)));
        Report("corridor", sim.BuildRoom(new RoomPlan("corridor", new GridRect(44, 13, 56, 5), WallSide.South)));
        Report("side corridor", sim.BuildRoom(new RoomPlan("corridor", new GridRect(73, 18, 5, 12), WallSide.South)));
        var rects = new[] { new GridRect(44, 18, 10, 9), new GridRect(54, 18, 10, 9), new GridRect(64, 18, 9, 11) };
        for (var i = 0; i < rects.Length; i++)
        {
            Report($"resus {i + 1}", sim.BuildRoom(sim.AutoPlan("resus_bay", rects[i])));
            var room = sim.Spaces.Last(s => s.TemplateId == "resus_bay");
            sim.RenameRoom(room.Id, $"Resus {i + 1}");
            GD.Print($"[door] Resus {i + 1} door {room.DoorSide}");
            _resus3 = room.Id;
        }

        h.Session.Clock.SetSpeed(BlueprintHospital.Simulation.Core.SimulationSpeed.Paused);
    }
}
