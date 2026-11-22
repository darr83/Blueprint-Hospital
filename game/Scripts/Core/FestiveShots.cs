using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the seasonal decorations and a police officer, run with:
/// godot --path game -- --festive-shots=DIR [--3d] (needs a display). Starts in December (the tree is up),
/// brings in someone with the police, then photographs the waiting room with each kind of decorations.
/// </summary>
public partial class FestiveShots : Node
{
    private static readonly string[] Kinds = { "christmas", "halloween", "easter" };

    private string _directory = "";
    private int _frame;
    private int _shot = -1;
    private HospitalScene? _hospital;
    private Patient? _escorted;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--festive-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new FestiveShots { Name = "FestiveShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--festive-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Festive Review General", seasons: new SeasonChoice(true, 12))).CallDeferred();
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
                var sim = h.Session.Simulation;
                if (!sim.SpacesOfKind(CareSpaceKind.Waiting).Any())
                {
                    sim.BuildStarterWaitingRoom();
                }

                _escorted = sim.SpawnPatient(ArrivalMethod.WalkIn, "nasal_fracture", age: 26);
                _escorted.PoliceEscort = true;
                h.Session.Clock.SetSpeed(SimulationSpeed.Fast);
                _frame = 0;
            }

            return;
        }

        var waiting = _hospital.Session.Simulation.SpacesOfKind(CareSpaceKind.Waiting).First().Rect;
        if (_shot < 0)
        {
            if (_frame < 200)
            {
                return;
            }

            _hospital.Session.Clock.SetSpeed(SimulationSpeed.Paused);
            _shot = 0;
            _frame = 0;
        }

        // Three decorations in the waiting room, then the police officer up close.
        if (_shot >= (Kinds.Length + 1) * 2)
        {
            SeasonalDecor.Override = null;
            GD.Print("[festive] Done.");
            GetTree().Quit(0);
            return;
        }

        if (_frame < 15)
        {
            return;
        }

        var index = _shot / 2;
        var name = index < Kinds.Length ? Kinds[index] : "police";
        if (_shot % 2 == 0)
        {
            if (index < Kinds.Length)
            {
                SeasonalDecor.Override = Kinds[index];
                if (index == 0 && GameManager.Instance.Settings.View3D)
                {
                    // The tree goes in the front corner here, behind the outside wall in the usual view: look from the other side.
                    _hospital.RotateView(2);
                }

                var piece = SeasonalDecor.Plan(_hospital.Session.Simulation, Kinds[index]).Pieces.FirstOrDefault(d => d.X >= waiting.X && d.X < waiting.Right && d.Y >= waiting.Y && d.Y < waiting.Bottom);
                var (fx, fy) = piece is null ? (waiting.X + (waiting.Width / 2.0), waiting.Y + (waiting.Length / 2.0)) : (piece.X, piece.Y);
                GD.Print($"[festive] {Kinds[index]}: {(piece is null ? "no piece" : $"{piece.Kind} at {piece.X},{piece.Y}")}");
                _hospital.Camera.SnapTo(IsoDraw.ToScreen(fx, fy), 2.2f);
            }
            else if (_escorted is { } p)
            {
                SeasonalDecor.Override = null;
                if (GameManager.Instance.Settings.View3D)
                {
                    _hospital.RotateView(2);
                }

                _hospital.Camera.SnapTo(IsoDraw.ToScreen(p.X, p.Y), 3.2f);
                GD.Print($"[festive] Officer: {_hospital.Session.Simulation.ParentOf(p)?.FullName ?? "none"} with {p.FullName} ({p.Stage}).");
            }
        }
        else
        {
            var image = GetViewport().GetTexture().GetImage();
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            var path = $"{_directory}/{name}.png";
            image.SavePng(path);
            GD.Print($"[festive] Saved {path}");
        }

        _shot++;
        _frame = 0;
    }
}
