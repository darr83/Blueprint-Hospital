using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of a major incident, run with: godot --path game -- --incident-shots=DIR [--3d] [--incident=KIND]
/// (needs a display). Declares a road collision, takes the standby card, gets ready (trauma team, call in staff),
/// takes the card again as the casualties arrive, then stands it down for the debrief.
/// </summary>
public partial class IncidentShots : Node
{
    private string _directory = "";
    private string _kind = "road_collision";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;

    public static bool Begin()
    {
        var args = OS.GetCmdlineUserArgs();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--incident-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(args, "--3d") >= 0;
                var shots = new IncidentShots { Name = "IncidentShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--incident-shots=".Length..] };
                if (args.FirstOrDefault(a => a.StartsWith("--incident=", StringComparison.Ordinal)) is { } kind)
                {
                    shots._kind = kind["--incident=".Length..];
                }

                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Incident Review General", seasons: new SeasonChoice(true, 1, MajorIncidentFrequency.Off))).CallDeferred();
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
                if (!h.Session.Simulation.SpacesOfKind(CareSpaceKind.Waiting).Any())
                {
                    h.Session.Simulation.BuildStarterWaitingRoom();
                }

                h.Session.Clock.SetSpeed(SimulationSpeed.Fast);
                _frame = 0;
            }

            return;
        }

        var session = _hospital.Session;
        var sim = session.Simulation;
        var panel = _hospital.Hud.Incident;
        switch (_step)
        {
            case 0 when _frame > 120:
                GD.Print($"[incident] {sim.RunCheat($"incident {_kind}").Message}");
                session.Clock.SetSpeed(SimulationSpeed.Paused);
                _step = 1;
                _frame = 0;
                break;
            case 1 when _frame > 20:
                panel.Open();
                Shot("1_standby");
                break;
            case 2 when _frame > 20:
                foreach (var action in HospitalSimulation.IncidentActions)
                {
                    GD.Print($"[incident] {action}: {sim.DoIncidentAction(action)}");
                }

                session.Clock.SetSpeed(SimulationSpeed.Fastest);
                Next();
                break;
            case 3 when sim.ActiveIncident is { } on && on.Casualties.Count(c => c.PatientId is not null) >= Math.Min(3, on.Casualties.Count):
                session.Clock.SetSpeed(SimulationSpeed.Paused);
                panel.Open();
                Next();
                break;
            case 4 when _frame > 20:
                Shot("2_arriving");
                break;
            case 5 when _frame > 5:
                // A look at the department: the casualties coming in.
                panel.Expanded = false;
                if (sim.ActiveIncident?.Casualties.FirstOrDefault(c => c.PatientId is not null)?.PatientId is { } id && sim.FindPatient(id) is { } patient)
                {
                    _hospital.Camera.SnapTo(IsoDraw.ToScreen(patient.X, patient.Y), 1.4f);
                }

                Next();
                break;
            case 6 when _frame > 20:
                Shot("3_department");
                break;
            case 7 when _frame > 5:
                sim.RunCheat("incident end");
                panel.Open();
                Next();
                break;
            case 8 when _frame > 20:
                Shot("4_debrief");
                break;
            case 9:
                GD.Print("[incident] Done.");
                GetTree().Quit(0);
                break;
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
        GD.Print($"[incident] Saved {path}");
        Next();
    }
}
