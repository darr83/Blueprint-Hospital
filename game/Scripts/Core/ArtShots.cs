using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Art review shots, run with: godot --path game -- --art-shots=DIR (needs a display, not --headless).
/// Starts a new game, lets patients fill the beds, then photographs the department, a bed with
/// its monitor and a room corner from all four view angles, with the interface hidden, and quits.
/// </summary>
public partial class ArtShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _shot;
    private HospitalScene? _hospital;

    public static void Begin(Node menu)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--art-shots=", StringComparison.Ordinal))
            {
                var shots = new ArtShots { Name = "ArtShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--art-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Art Review General")).CallDeferred();
                return;
            }
        }
    }

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

                // Bring in a few patients so there are people in the beds to look at.
                for (var i = 0; i < 6; i++)
                {
                    h.Session.Simulation.SpawnPatient(i % 2 == 0 ? ArrivalMethod.WalkIn : ArrivalMethod.Ambulance);
                }

                var clock = h.Session.Clock;
                clock.SetSpeed(SimulationSpeed.Paused);
                for (var i = 0; i < 2400 && h.Session.Simulation.Spaces.Count(s => s.IsTreatmentSpace && !s.IsFree) < 4; i++)
                {
                    clock.Step();
                }

                _frame = 0;
            }

            return;
        }

        // Each shot: frame the view, wait a few frames so it settles, then save it.
        const int settle = 6;
        if (_frame % settle != 0)
        {
            return;
        }

        var shots = new (string Name, Func<HospitalScene, (Vector2 At, float Zoom)> Frame)[]
        {
            ("overview", Overview),
            ("bed", BedCloseUp),
            ("corner", Corner),
        };

        var rotation = _shot / (shots.Length * 2);
        var index = (_shot / 2) % shots.Length;
        if (rotation >= 4)
        {
            GD.Print("[art] Done.");
            GetTree().Quit(0);
            return;
        }

        if (_shot % 2 == 0)
        {
            if (index == 0 && IsoDraw.Rotation != rotation)
            {
                _hospital.RotateView(rotation - IsoDraw.Rotation);
            }

            var (at, zoom) = shots[index].Frame(_hospital);
            _hospital.Camera.SnapTo(at, zoom);
        }
        else
        {
            var image = GetViewport().GetTexture().GetImage();
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            var path = $"{_directory}/{shots[index].Name}_{rotation}.png";
            image.SavePng(path);
            GD.Print($"[art] Saved {path}");
        }

        _shot++;
    }

    private static (Vector2, float) Overview(HospitalScene h)
    {
        var rooms = h.Session.Simulation.Spaces.Where(s => s.HasWalls).ToList();
        var (x0, y0) = (rooms.Min(r => r.Rect.X), rooms.Min(r => r.Rect.Y));
        var (x1, y1) = (rooms.Max(r => r.Rect.Right), rooms.Max(r => r.Rect.Bottom));
        return (IsoDraw.ToScreen((x0 + x1) / 2.0, (y0 + y1) / 2.0), 0.55f);
    }

    private static (Vector2, float) BedCloseUp(HospitalScene h)
    {
        var bay = h.Session.Simulation.Spaces.Where(s => s.IsTreatmentSpace).OrderBy(s => s.IsFree).ThenBy(s => s.Id).First();
        var spot = bay.PatientSpot;
        return (IsoDraw.ToScreen(spot.X + 0.5, spot.Y + 0.5) + new Vector2(0, -30), 2.2f);
    }

    private static (Vector2, float) Corner(HospitalScene h)
    {
        var room = h.Session.Simulation.Spaces.Where(s => s.HasWalls).OrderByDescending(s => s.Rect.Width * s.Rect.Length).First();
        var r = room.Rect;
        return (IsoDraw.ToScreen(r.Right, r.Bottom) + new Vector2(0, -20), 1.6f);
    }
}
