using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the helipad and the air ambulance, run with: godot --path game -- --helipad-shots=DIR [--3d]
/// (needs a display). Builds a helipad north of the building, declares a road collision, and takes the
/// helicopter coming in to land, its paramedic wheeling the patient round, then a firefighter from a house fire.
/// </summary>
public partial class HelipadShots : Node
{
    private static readonly GridCoord PadCorner = new(50, 1);

    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;
    private bool _threeD;

    public static bool Begin()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.FirstOrDefault(a => a.StartsWith("--helipad-shots=", StringComparison.Ordinal)) is not { } arg)
        {
            return false;
        }

        var threeD = Array.IndexOf(args, "--3d") >= 0;
        GameManager.Instance.Settings.View3D = threeD;
        var shots = new HelipadShots { Name = "HelipadShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--helipad-shots=".Length..], _threeD = threeD };
        GameManager.Instance.AddChild(shots);
        Callable.From(() => GameManager.Instance.StartNewGame("Helipad Review General", seasons: new SeasonChoice(true, 6, MajorIncidentFrequency.Off))).CallDeferred();
        return true;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                var s = h.Session.Simulation;
                if (!s.SpacesOfKind(CareSpaceKind.Waiting).Any())
                {
                    s.BuildStarterWaitingRoom();
                }

                s.RunCheat("money 2000000");
                GD.Print($"[helipad] build: {string.Join(" ", s.BuildHelipad(PadCorner).Problems)}");
                if (_threeD)
                {
                    // Look from the north, so the building doesn't stand between the camera and the pad.
                    h.RotateView(2);
                }

                h.Session.Clock.SetSpeed(SimulationSpeed.Fast);
                _frame = 0;
            }

            return;
        }

        var session = _hospital.Session;
        var sim = session.Simulation;
        var pad = sim.Helipad ?? HospitalSimulation.HelipadAt(PadCorner);
        var centre = HospitalSimulation.HelipadCentre(pad);
        switch (_step)
        {
            case 0 when _frame > 60:
                GD.Print($"[helipad] {sim.RunCheat("incident road_collision").Message}");
                session.Clock.SetSpeed(SimulationSpeed.Fastest);
                _hospital.Camera.SnapTo(IsoDraw.ToScreen(centre.X, centre.Y + 4), _threeD ? 0.75f : 0.55f);
                Next();
                break;
            case 1 when sim.Helicopters.FirstOrDefault() is { } heli && heli.State == HelicopterState.Landing && heli.Altitude < 22:
                session.Clock.SetSpeed(SimulationSpeed.Paused);
                _hospital.Hud.Incident.Close();
                Next();
                break;
            case 2 when _frame > 20:
                Shot("1_landing");
                break;
            case 3:
                session.Clock.SetSpeed(SimulationSpeed.Normal);
                Next();
                break;
            case 4 when sim.Parents.FirstOrDefault(p => p.Escort == EscortKind.AirCrew) is { } crew && sim.FindPatient(crew.ChildId) is { } patient
                        && Math.Abs(patient.X - (pad.X + (pad.Width / 2.0))) + Math.Abs(patient.Y - pad.Bottom) > 6:
                session.Clock.SetSpeed(SimulationSpeed.Paused);
                _hospital.Hud.Incident.Close();
                _hospital.Camera.SnapTo(IsoDraw.ToScreen(patient.X, patient.Y), _threeD ? 2.0f : 1.0f);
                Next();
                break;
            case 5 when _frame > 20:
                Shot("2_paramedic");
                break;
            case 6:
                if (_threeD)
                {
                    _hospital.RotateView(2);
                }

                sim.RunCheat("incident end");
                GD.Print($"[helipad] {sim.RunCheat("incident house_fire").Message}");
                session.Clock.SetSpeed(SimulationSpeed.Fastest);
                Next();
                break;
            case 7 when sim.Parents.FirstOrDefault(p => p.Escort == EscortKind.FireCrew && p.Leaving) is not null:
                // Handed over: the firefighter walks back out to their engine.
                session.Clock.SetSpeed(SimulationSpeed.Normal);
                Next();
                break;
            case 8 when _frame > 240 && sim.Parents.FirstOrDefault(p => p.Escort == EscortKind.FireCrew && p.Leaving) is { } firefighter:
                session.Clock.SetSpeed(SimulationSpeed.Paused);
                _hospital.Hud.Incident.Close();
                _hospital.Camera.SnapTo(IsoDraw.ToScreen(firefighter.X, firefighter.Y), _threeD ? 2.4f : 1.4f);
                _step++;
                _frame = 0;
                break;
            case 9 when _frame > 20:
                Shot("3_firefighter");
                break;
            case 10:
                GD.Print("[helipad] Done.");
                GetTree().Quit(0);
                break;
        }

        if (_frame > 20000)
        {
            GD.PrintErr($"[helipad] Stuck at step {_step}.");
            GetTree().Quit(1);
        }
    }

    private void Next()
    {
        _step++;
        _frame = 0;
    }

    private void Shot(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        var path = $"{_directory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[helipad] Saved {path}");
        Next();
    }
}
