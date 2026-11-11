using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Close-up review shots of doorways, run with: godot --path game -- --door-shots=DIR (needs a display).
/// Builds stairs and a lift beside A&amp;E, then photographs them and the waiting room's doors from all four sides.
/// </summary>
public partial class DoorShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _shot;
    private HospitalScene? _hospital;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--door-shots=", StringComparison.Ordinal))
            {
                var shots = new DoorShots { Name = "DoorShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--door-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Door Review General")).CallDeferred();
                return true;
            }
        }

        return false;
    }

    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>Where each shot looks: the lift and stairs side by side, and the waiting room's inner and entrance doors.</summary>
    private static readonly (string Name, double X, double Y, float Zoom)[] Targets =
    {
        ("lift_stairs", B.X + 53.5, B.Y + 18, 2.6f),
        ("waiting_inner", B.X + 26.5, B.Y + 26.5, 2.6f),
        ("waiting_entrance", B.X + 26.5, B.Bottom - 1, 2.6f),
    };

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
                Build(h.Session.Simulation);
                _frame = 0;
            }

            return;
        }

        if (_frame < 10 || _frame % 6 != 0)
        {
            return;
        }

        var index = _shot / 2;
        if (index >= Targets.Length * 4)
        {
            GD.Print("[doors] Done.");
            GetTree().Quit(0);
            return;
        }

        var (name, x, y, zoom) = Targets[index / 4];
        var view = index % 4;
        if (_shot % 2 == 0)
        {
            _hospital.RotateView(view - IsoDraw.Rotation);
            _hospital.Camera.SnapTo(IsoDraw.ToScreen(x, y), zoom);
        }
        else
        {
            var image = GetViewport().GetTexture().GetImage();
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            var path = $"{_directory}/{name}_view{view}.png";
            image.SavePng(path);
            GD.Print($"[doors] Saved {path}");
        }

        _shot++;
    }

    private static void Build(HospitalSimulation sim)
    {
        sim.AdjustBalance(5_000_000);
        if (!sim.SpacesOfKind(CareSpaceKind.Waiting).Any())
        {
            sim.BuildStarterWaitingRoom();
        }

        sim.AddFloor();
        foreach (var (template, rect) in new[] { ("lift", new GridRect(B.X + 48, B.Y + 17, 5, 6)), ("stairs", new GridRect(B.X + 54, B.Y + 17, 5, 8)) })
        {
            var check = sim.BuildRoom(sim.AutoPlan(template, rect));
            GD.Print($"[doors] {template}: {(check.CanBuild ? "built" : string.Join("; ", check.Problems))}");
        }
    }
}
