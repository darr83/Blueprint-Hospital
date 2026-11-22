using System;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the weather, run with: godot --path game -- --weather-shots=DIR [--3d] (needs a display).
/// Starts a game with seasons, lets people arrive, then sets each kind of weather with the cheat and
/// photographs the hospital and its grounds.
/// </summary>
public partial class WeatherShots : Node
{
    private static readonly string[] Kinds = { "clear", "rain", "heavy_rain", "storm", "fog", "snow", "ice", "heatwave" };

    private string _directory = "";
    private int _frame;
    private int _shot = -1;
    private HospitalScene? _hospital;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--weather-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new WeatherShots { Name = "WeatherShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--weather-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Weather Review General", seasons: new SeasonChoice(true, 1))).CallDeferred();
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
                h.Session.Clock.SetSpeed(SimulationSpeed.Fast);
                var b = BlueprintHospital.Simulation.World.StarterSite.MainBuildingFootprint;
                h.Camera.SnapTo(IsoDraw.ToScreen(b.X + (b.Width / 2.0), b.Bottom + 2), 0.5f);
                _frame = 0;
            }

            return;
        }

        // A couple of game hours first, so there are people about.
        if (_shot < 0)
        {
            if (_frame < 240)
            {
                return;
            }

            _hospital.Session.Clock.SetSpeed(SimulationSpeed.Paused);
            _shot = 0;
            _frame = 0;
        }

        if (_shot >= Kinds.Length * 2)
        {
            GD.Print("[weather] Done.");
            GetTree().Quit(0);
            return;
        }

        if (_frame < 20)
        {
            return;
        }

        var kind = Kinds[_shot / 2];
        if (_shot % 2 == 0)
        {
            var result = _hospital.Session.Simulation.RunCheat($"weather {kind}");
            GD.Print($"[weather] {kind}: {result.Message}");
            // Let the rain and snow fall for a moment while paused frames draw.
            _hospital.Session.Clock.SetSpeed(SimulationSpeed.Normal);
        }
        else
        {
            _hospital.Session.Clock.SetSpeed(SimulationSpeed.Paused);
            var image = GetViewport().GetTexture().GetImage();
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            var path = $"{_directory}/{kind}.png";
            image.SavePng(path);
            GD.Print($"[weather] Saved {path} (overlay: {_hospital.Weather.Showing})");
        }

        _shot++;
        _frame = 0;
    }
}
