using System;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Automated launch check, run with: godot --headless --path game -- --smoke-test
/// Starts a new game, lets the simulation run, saves, reloads and compares, then exits
/// with code 0 on success or 1 on failure. Used to verify each build actually runs.
/// </summary>
public partial class SmokeTest : Node
{
    private const string SlotName = "smoke_test";
    private int _frame;
    private Stage _stage = Stage.WaitForMenu;
    private DateTime _savedTime;
    private CameraState? _savedCamera;
    private string? _screenshotDirectory;

    private enum Stage
    {
        WaitForMenu,
        WaitForHospital,
        RunSimulation,
        WaitForLoad,
        Done,
    }

    public static void Begin(Node menu)
    {
        var manager = GameManager.Instance;
        if (manager.GetNodeOrNull("SmokeTest") is not null)
        {
            return;
        }

        var test = new SmokeTest { Name = "SmokeTest", ProcessMode = ProcessModeEnum.Always };
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshots=", StringComparison.Ordinal))
            {
                test._screenshotDirectory = arg["--screenshots=".Length..];
            }
        }

        manager.AddChild(test);
        GD.Print("[smoke] Main menu loaded.");
    }

    public override void _Process(double delta)
    {
        _frame++;
        try
        {
            Step();
        }
        catch (Exception ex)
        {
            Fail(ex.ToString());
        }

        if (_frame > 2000)
        {
            Fail($"Timed out in stage {_stage}.");
        }
    }

    private void Step()
    {
        var manager = GameManager.Instance;
        var scene = GetTree().CurrentScene;

        switch (_stage)
        {
            case Stage.WaitForMenu when _frame >= 10:
                Screenshot("01_main_menu");
                foreach (var panel in new UI.ModalPanel[] { new UI.SettingsPanel(), new UI.LoadGamePanel() })
                {
                    scene.AddChild(panel);
                    panel.Close();
                }

                GD.Print("[smoke] Settings and Load Game panels open and close.");
                Check(manager.Database is not null, "Game data failed to load: " + manager.DataLoadError);
                manager.StartNewGame("Smoke Test General");
                _stage = Stage.WaitForHospital;
                break;

            case Stage.WaitForHospital when scene is HospitalScene hospital && hospital.IsNodeReady():
                Check(hospital.Session.Site.Buildings.Count == 1, "Starter building missing.");
                Check(hospital.Camera.IsCurrent(), "Camera is not active.");
                hospital.Session.Clock.SetSpeed(SimulationSpeed.VeryFast);
                GD.Print("[smoke] Hospital scene loaded.");
                _frame = 0;
                _stage = Stage.RunSimulation;
                break;

            case Stage.RunSimulation when scene is HospitalScene hospital && _frame >= 30:
                var clock = hospital.Session.Clock;
                Check(clock.TickCount > 0, "Simulation clock did not advance.");
                Screenshot("02_hospital");
                GD.Print($"[smoke] Clock advanced {clock.TickCount} ticks to {clock.Now:yyyy-MM-dd HH:mm:ss}.");
                hospital.Session.Clock.SetSpeed(SimulationSpeed.Paused);
                _savedTime = clock.Now;
                _savedCamera = hospital.CaptureCamera();
                manager.SaveGame(SlotName, _savedCamera);
                GD.Print("[smoke] Game saved.");
                manager.LoadGame(SlotName);
                _stage = Stage.WaitForLoad;
                break;

            case Stage.WaitForLoad when scene is HospitalScene hospital && hospital.IsNodeReady() && hospital.Session.Clock.Now == _savedTime:
                Check(hospital.Session.HospitalName == "Smoke Test General", "Hospital name not restored.");
                Check(hospital.CaptureCamera() == _savedCamera, "Camera not restored.");
                SaveManager.Delete(SlotName);
                GD.Print("[smoke] Save reloaded and verified.");
                GD.Print("[smoke] PASSED");
                _stage = Stage.Done;
                GetTree().Quit(0);
                break;
        }
    }

    /// <summary>Saves the current frame when run with "--screenshots=DIR" (needs a real display, not --headless).</summary>
    private void Screenshot(string name)
    {
        if (_screenshotDirectory is null || DisplayServer.GetName() == "headless")
        {
            return;
        }

        DirAccess.MakeDirRecursiveAbsolute(_screenshotDirectory);
        var image = GetViewport().GetTexture().GetImage();
        var path = $"{_screenshotDirectory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[smoke] Screenshot saved to {path}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void Fail(string message)
    {
        if (_stage == Stage.Done)
        {
            return;
        }

        _stage = Stage.Done;
        GD.PrintErr("[smoke] FAILED: " + message);
        GetTree().Quit(1);
    }
}
