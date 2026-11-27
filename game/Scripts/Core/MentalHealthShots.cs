using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the mental health department, run with: godot --path game -- --mental-health-shots=DIR [--3d] (needs a display).
/// Loads the example hospital, sends in patients in mental health crisis, and photographs the assessment room in use,
/// the mental health team lined up in the corridor, and the room panel.
/// </summary>
public partial class MentalHealthShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--mental-health-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new MentalHealthShots { Name = "MentalHealthShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--mental-health-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.LoadExampleHospital()).CallDeferred();
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
                h.Session.Clock.SetSpeed(SimulationSpeed.Paused);
                _frame = 0;
            }

            return;
        }

        if (_frame < 12)
        {
            return;
        }

        var h2 = _hospital;
        var sim = h2.Session.Simulation;
        var view3D = GameManager.Instance.Settings.View3D;
        var room = sim.Spaces.First(s => s.Kind == CareSpaceKind.MentalHealthAssessment);
        var r = room.Rect;
        switch (_step)
        {
            case 0:
                h2.Hud.Visible = false;
                h2.ShowFloor(0);
                if (view3D)
                {
                    // Look from the outside wall's side, so the seats aren't behind the cut-away wall.
                    h2.RotateView(2);
                }

                foreach (var condition in new[] { "low_mood_suicidal_thoughts", "self_harm", "first_episode_psychosis", "mania" })
                {
                    sim.SpawnPatient(ArrivalMethod.WalkIn, condition, 34);
                }

                Run(h2, () => sim.Patients.Any(p => p.Tile == room.PatientSpot && !p.IsMoving)
                              && sim.Staff.Any(s => s.Tile == room.StaffSpot && !s.IsMoving), TimeSpan.FromHours(8));
                Focus(h2, r.X + (r.Width / 2.0), r.Y + (r.Length / 2.0), view3D ? 2.6f : 1.9f);
                break;
            case 1:
                Save("1_assessment_room");
                Focus(h2, r.X + (r.Width / 2.0) - 2, r.Y + (r.Length / 2.0) + 2, view3D ? 1.5f : 1.0f);
                break;
            case 2:
                Save("2_room_in_the_hospital");
                LineUp(sim, r);
                Focus(h2, r.X - 4, r.Bottom + 2.5, view3D ? 3.2f : 2.6f);
                break;
            case 3:
                Save("3_team");
                h2.Hud.Visible = true;
                h2.SetMode(GameMode.Build);
                h2.Hud.ShowRoom(room);
                Focus(h2, r.X + (r.Width / 2.0), r.Y + (r.Length / 2.0), view3D ? 2.0f : 1.5f);
                break;
            case 4:
                Save("4_room_panel");
                GD.Print($"[mental-health] Liaison seen {sim.LiaisonAssessments}, within the hour {sim.LiaisonWithinTarget}.");
                GD.Print("[mental-health] Done.");
                GetTree().Quit(0);
                return;
        }

        _step++;
        _frame = 0;
    }

    /// <summary>Every mental health grade standing in a row in the corridor outside the room.</summary>
    private static void LineUp(HospitalSimulation sim, GridRect r)
    {
        var x = r.X - 9;
        foreach (var (role, skill) in new[]
                 {
                     (StaffRole.MentalHealthNurse, 0.5), (StaffRole.MentalHealthNurse, 0.65), (StaffRole.MentalHealthNurse, 0.78), (StaffRole.MentalHealthNurse, 0.9),
                     (StaffRole.Psychiatrist, 0.5), (StaffRole.Psychiatrist, 0.65), (StaffRole.Psychiatrist, 0.78), (StaffRole.Psychiatrist, 0.9),
                 })
        {
            var member = sim.HireStaff(role, null, skill);
            member.PlaceAt(new GridCoord(x, r.Bottom + 2));
            x += 1;
        }
    }

    private static void Focus(HospitalScene hospital, double x, double y, float zoom) => hospital.Camera.SnapTo(IsoDraw.ToScreen(x, y), zoom);

    private static void Run(HospitalScene hospital, Func<bool> until, TimeSpan limit)
    {
        var sim = hospital.Session.Simulation;
        var end = sim.Now + limit;
        var clock = hospital.Session.Clock;
        while (sim.Now < end && !until())
        {
            clock.Step();
        }

        GD.Print($"[mental-health] {sim.Now:HH:mm}: {(until() ? "reached" : "gave up")}");
    }

    private void Save(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        var path = $"{_directory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[mental-health] Saved {path}");
    }
}
