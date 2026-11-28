using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using BlueprintHospital.UI;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the tutorial, run with: godot --path game -- --tutorial-shots=DIR [--3d] (needs a display).
/// Starts the tutorial from the main menu and plays it through the way a player would (build tool, build bar,
/// Recruitment), photographing the guide card, its pointer and the outline on the map at each step.
/// </summary>
public partial class TutorialShots : Node
{
    private readonly List<(int Wait, Action Do, Func<bool>? Until)> _script = new();
    private string _directory = "";
    private int _frame;
    private int _at;
    private HospitalScene? _hospital;
    private MainMenu? _menu;

    public static bool Begin(MainMenu menu)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--tutorial-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new TutorialShots { Name = "TutorialShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--tutorial-shots=".Length..], _menu = menu };
                GameManager.Instance.AddChild(shots);
                DirAccess.MakeDirRecursiveAbsolute(shots._directory);
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
            if (_frame == 20 && _menu is not null && IsInstanceValid(_menu))
            {
                _menu.TutorialButton.GrabFocus();
                Shot("00_main_menu");
                GameManager.Instance.StartTutorial();
            }

            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                Script(h);
                _frame = 0;
            }

            return;
        }

        if (_at >= _script.Count)
        {
            GetTree().Quit();
            return;
        }

        if (_script[_at].Until is { } until)
        {
            // Wait for something to happen in the game, up to a limit.
            if (until() || _frame >= _script[_at].Wait)
            {
                _script[_at].Do();
                _at++;
                _frame = 0;
            }

            return;
        }

        if (_frame >= _script[_at].Wait)
        {
            _script[_at].Do();
            _at++;
            _frame = 0;
        }
    }

    private void Then(int wait, Action action) => _script.Add((wait, action, null));

    private void Script(HospitalScene h)
    {
        var sim = h.Session.Simulation;
        var hud = h.Hud;
        var tool = h.BuildTool;
        var guide = h.Tutorial!.Guide;
        var mode = GameView.ThreeD ? "3d" : "2d";
        var shot = 0;
        void Snap(string name) => Shot($"{mode}_{++shot:00}_{name}");
        void Expect(string id)
        {
            if (guide.Current.Id != id)
            {
                GD.PrintErr($"[tutorial] Expected step {id}, on {guide.Current.Id}");
            }
        }

        void LookAt(double x, double y, float zoom) => h.Camera.SnapTo(IsoDraw.ToScreenGround(x, y), zoom);
        var b = StarterSite.MainBuildingFootprint;
        var zoomScale = GameView.ThreeD ? 1f : 1.6f;

        Then(30, () => Snap("welcome"));
        Then(1, () => guide.Next());
        Then(20, () => Snap("camera"));
        Then(1, () =>
        {
            h.Camera.SnapTo(h.Camera.Position + new Vector2(260, 120), h.Camera.Zoom.X * 1.3f);
            h.RotateView(1);
        });
        Then(10, () => h.RotateView(-1));
        Then(90, () => { Expect("build_mode"); h.Tutorial!.FrameBuilding(); });
        Then(20, () => Snap("build_mode"));
        Then(1, () => h.SetMode(GameMode.Build));
        Then(80, () => { Expect("waiting_room"); Snap("waiting_room_icon"); });
        Then(1, () => hud.Build.Open("emergency"));
        Then(20, () => Snap("waiting_room_tile"));
        Then(1, () => hud.Build.Choose("waiting_room"));
        Then(20, () => Snap("waiting_room_outline"));
        Then(1, () => tool.PreviewDrag(Tl(TutorialGuide.Spots.WaitingRoom.X, TutorialGuide.Spots.WaitingRoom.Y), Tl(TutorialGuide.Spots.WaitingRoom.Right - 1, TutorialGuide.Spots.WaitingRoom.Bottom - 1)));
        Then(10, () => Snap("waiting_room_drag"));
        Then(1, () => { tool.Confirm(); tool.Cancel(); });
        Then(90, () => { Expect("corridor"); Snap("corridor"); });
        Then(1, () => { hud.Build.Open("building"); hud.Build.Choose("corridor"); });
        Then(10, () => tool.PreviewDrag(Tl(TutorialGuide.Spots.Corridor.X, TutorialGuide.Spots.Corridor.Y), Tl(TutorialGuide.Spots.Corridor.Right - 1, TutorialGuide.Spots.Corridor.Bottom - 1)));
        Then(10, () => Snap("corridor_drag"));
        Then(1, () => { tool.Confirm(); tool.Cancel(); });
        Then(90, () => { Expect("desk"); hud.Build.Open("emergency"); hud.Build.Choose("waiting_desk"); });
        Then(10, () => tool.PreviewAt(TutorialGuide.Spots.Desk));
        Then(10, () => Snap("desk"));
        Then(1, () => { tool.Confirm(); tool.Cancel(); });
        Then(90, () => { Expect("doors"); hud.Build.Open("emergency"); hud.Build.Choose("waiting_doors"); });
        Then(10, () => { tool.PreviewAt(TutorialGuide.Spots.EntranceDoor); tool.Confirm(); });
        Then(10, () => tool.PreviewAt(TutorialGuide.Spots.InnerDoor));
        Then(10, () => Snap("doors_one_done"));
        Then(1, () => tool.Confirm());
        Then(12, () => Snap("doors_both_ticked"));
        Then(1, () => tool.Cancel());
        Then(90, () => { Expect("chairs"); hud.Build.Open("emergency"); hud.Build.Choose("waiting_chairs"); });
        Then(10, () =>
        {
            tool.FaceChairs(WallSide.East);
            tool.PreviewDrag(Tl(TutorialGuide.Spots.Chairs.X, TutorialGuide.Spots.Chairs.Y), Tl(TutorialGuide.Spots.Chairs.X, TutorialGuide.Spots.Chairs.Bottom - 1));
            tool.Confirm();
            tool.PreviewDrag(Tl(TutorialGuide.Spots.Chairs.X + 3, TutorialGuide.Spots.Chairs.Y), Tl(TutorialGuide.Spots.Chairs.X + 3, TutorialGuide.Spots.Chairs.Bottom - 1));
            tool.Confirm();
            tool.Cancel();
        });
        Then(90, () => { Expect("triage"); Snap("triage"); });
        Build("emergency", "triage_room", TutorialGuide.Spots.Triage, "triage_drag");
        Then(90, () => Expect("minors"));
        Build("emergency", "minors_cubicle", TutorialGuide.Spots.Minors1, "minors_drag");
        Then(40, () => { });
        Build("emergency", "minors_cubicle", TutorialGuide.Spots.Minors2, null);
        Then(90, () => Expect("majors"));
        Build("emergency", "majors_cubicle", TutorialGuide.Spots.Majors, "majors_drag");
        Then(90, () => { Expect("recruitment"); hud.Build.ClosePopup(); });
        Then(10, () => Snap("recruitment"));
        Then(1, () => hud.ToggleRecruitment());
        Then(80, () => { Expect("hire"); Snap("hire"); });
        Then(1, () => sim.HireForShortages());
        Then(80, () => { Expect("open"); Snap("open_doors"); });
        Then(1, () => { hud.HideRightPanels(); h.SetMode(GameMode.Play); });
        Then(10, () => Snap("open_play_mode"));
        Then(1, () => h.Session.Clock.SetSpeed(SimulationSpeed.Normal));
        Then(20, () => { Expect("arrive"); Snap("arrive_point_at_2x"); });
        Then(1, () => h.Session.Clock.SetSpeed(SimulationSpeed.Fast));
        Then(10, () => Snap("arrive_at_2x_no_hint"));
        Then(1, () => { h.Session.Clock.SetSpeed(SimulationSpeed.Paused); hud.ToggleRoster(staff: true); });
        Then(80, () => Snap("arrive_staff_tab"));
        Then(1, () => hud.Roster.ShowTab(false));
        Then(80, () => Snap("arrive_patients_tab"));
        Then(1, () => { hud.HideRightPanels(); h.Session.Clock.SetSpeed(SimulationSpeed.Fastest); });
        Then(60, () => { });
        AwaitStep("record", 30000);
        Then(1, () =>
        {
            if (guide.FirstPatient is { } p)
            {
                h.Camera.SnapTo(h.World.FootPosition(p), 0.9f * zoomScale);
            }

            h.Session.Clock.SetSpeed(SimulationSpeed.Paused);
        });
        Then(20, () => Snap("click_patient"));
        Then(1, () =>
        {
            if (guide.FirstPatient is { } p)
            {
                h.SelectPatient(p, centre: true);
            }
        });
        Then(80, () => { Expect("treat"); Snap("record_open"); });
        Then(1, () => { hud.HideRightPanels(); h.Session.Clock.SetSpeed(SimulationSpeed.Fastest); });
        AwaitStep("done", 60000);
        Then(1, () => { h.Session.Clock.SetSpeed(SimulationSpeed.Normal); h.Tutorial!.FrameBuilding(); });
        Then(40, () => Snap("done"));
        Then(1, () => h.Tutorial!.SetMinimised(true));
        Then(10, () => Snap("folded"));

        void Build(string icon, string item, GridRect rect, string? name)
        {
            Then(1, () => { hud.Build.Open(icon); hud.Build.Choose(item); });
            Then(10, () => tool.PreviewDrag(Tl(rect.X, rect.Y), Tl(rect.Right - 1, rect.Bottom - 1)));
            if (name is not null)
            {
                Then(10, () => Snap(name));
            }

            Then(1, () => { tool.Confirm(); tool.Cancel(); });
        }

        void AwaitStep(string id, int frames) =>
            _script.Add((frames, () => GD.Print($"[tutorial] Waited for {id}: on {guide.Current.Id} at {sim.Now:HH:mm}"), () => guide.Current.Id == id));
    }

    private static GridCoord Tl(int x, int y) => new(x, y);

    private void Shot(string name)
    {
        var path = $"{_directory}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[tutorial] Saved {path}");
    }
}
