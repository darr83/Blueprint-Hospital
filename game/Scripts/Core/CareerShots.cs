using System;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.UI;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of Career mode, run with: godot --path game -- --career-shots=DIR (needs a display).
/// The New Game panel, a level 1 walk-in centre with its badge, the goals card, the build bar's
/// locked rooms, the level-up pop-up and Recruitment's "Hire minimum team".
/// </summary>
public partial class CareerShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;
    private MainMenu? _menu;

    public static bool Begin(MainMenu menu)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--career-shots=", StringComparison.Ordinal))
            {
                var shots = new CareerShots { Name = "CareerShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--career-shots=".Length..], _menu = menu };
                GameManager.Instance.AddChild(shots);
                DirAccess.MakeDirRecursiveAbsolute(shots._directory);
                Callable.From(menu.ShowNewGame).CallDeferred();
                return true;
            }
        }

        return false;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_step == 0)
        {
            if (_frame == 20)
            {
                Shot("01_new_game");
                GameManager.Instance.StartNewGame("Riverside Walk-in Centre", StartingHospital.SmallEmergency, false, WardEmergencyFrequency.Realistic, GameType.Career);
                _step = 1;
                _frame = 0;
            }

            return;
        }

        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                h.Session.Clock.SetSpeed(SimulationSpeed.Fastest);
                _frame = 0;
            }

            return;
        }

        var hud = _hospital.Hud;
        var sim = _hospital.Session.Simulation;
        switch (_step, _frame)
        {
            case (1, 400):
                // A few game hours in, so the goals have some progress.
                _hospital.Session.Clock.SetSpeed(SimulationSpeed.Normal);
                Shot("02_level_1");
                hud.Level.ToggleCard();
                Next();
                break;
            case (2, 20):
                Shot("03_goals_card");
                hud.Level.ToggleCard();
                hud.ShowBuildIcon("emergency");
                Next();
                break;
            case (3, 20):
                Shot("04_build_bar_locked");
                hud.Build.ClosePopup();
                sim.RunCheat("level 4");
                Next();
                break;
            case (4, 30):
                Shot("05_level_up");
                foreach (var child in hud.GetChildren())
                {
                    CloseModals(child);
                }

                hud.ShowBuildIcon("general_medicine");
                Next();
                break;
            case (5, 20):
                Shot("06_level_4_build_bar");
                hud.Build.ClosePopup();
                hud.ToggleRecruitment();
                Next();
                break;
            case (6, 20):
                Shot("07_recruitment");
                GD.Print("[career] Done.");
                GetTree().Quit(0);
                break;
        }
    }

    private void Next()
    {
        _step++;
        _frame = 0;
    }

    private static void CloseModals(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is ModalPanel modal)
            {
                modal.Close();
            }
            else
            {
                CloseModals(child);
            }
        }
    }

    private void Shot(string name)
    {
        var path = $"{_directory}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[career] Saved {path}");
    }
}
