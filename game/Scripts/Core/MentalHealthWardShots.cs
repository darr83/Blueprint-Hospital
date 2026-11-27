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
/// Review shots of the mental health ward (stage 2), run with: godot --path game -- --mental-health-ward-shots=DIR [--3d] (needs a display).
/// Loads the example hospital, admits a few patients to Willow ward on the top floor, and photographs the bedrooms,
/// someone on close observations, the roof garden and lounge in use, the support worker and the patient panel.
/// </summary>
public partial class MentalHealthWardShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;
    private Patient? _watched;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--mental-health-ward-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new MentalHealthWardShots { Name = "MentalHealthWardShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--mental-health-ward-shots=".Length..] };
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
        var ward = sim.Wards.First(sim.IsMentalHealthWard);
        var floor = sim.FloorOf(ward);
        var w = ward.Rect;
        var garden = sim.Spaces.First(s => s.Kind == CareSpaceKind.MentalHealthGarden);
        var lounge = sim.Spaces.First(s => s.Kind == CareSpaceKind.MentalHealthLounge);
        switch (_step)
        {
            case 0:
                h2.Hud.Visible = false;
                h2.ShowFloor(floor);
                foreach (var condition in new[] { "severe_depression", "first_episode_psychosis", "mania", "severe_depression", "mania", "first_episode_psychosis" })
                {
                    sim.SpawnPatient(ArrivalMethod.WalkIn, condition, 30 + (_frame % 20));
                }

                // Admitted, someone with each patient who needs it, and the day under way.
                Run(h2, () => sim.Patients.Count(p => sim.IsOnMentalHealthWard(p)) >= 4 && sim.Now.Hour is >= 11 and < 17
                              && sim.Patients.Any(p => sim.DayRoomOf(p) is not null), TimeSpan.FromHours(40));
                _watched = sim.Patients.Where(p => sim.IsOnMentalHealthWard(p) && BedTile(sim, p)).OrderBy(p => p.Id).FirstOrDefault();
                if (_watched is not null)
                {
                    // One patient on close observations, so the 1:1 shows.
                    _watched.Wellbeing = 0.15;
                    _watched.ObservationLevel = ObservationLevel.ArmsLength;
                    Run(h2, () => sim.CarerAtBedside(_watched) is not null, TimeSpan.FromHours(3));
                    GD.Print($"[mental-health-ward] Close obs: {sim.Patients.Count(sim.NeedsCloseObservation)}");
                }

                Focus(h2, w.X + (w.Width / 2.0), w.Y + (w.Length / 2.0), view3D ? 1.25f : 0.95f);
                break;
            case 1:
                Save("1_ward");
                if (_watched is not null && BedTile(sim, _watched))
                {
                    Focus(h2, _watched.Tile.X + 1.5, _watched.Tile.Y + 1, view3D ? 3.0f : 2.4f);
                }
                else
                {
                    Focus(h2, w.X + 8, w.Y + 4, view3D ? 3.0f : 2.4f);
                }

                break;
            case 2:
                Save("2_bedroom_close_observation");
                Focus(h2, garden.Rect.X + (garden.Rect.Width / 2.0), garden.Rect.Y + (garden.Rect.Length / 2.0), view3D ? 1.9f : 1.6f);
                break;
            case 3:
                Save("3_roof_garden");
                Focus(h2, lounge.Rect.X + (lounge.Rect.Width / 2.0), lounge.Rect.Y + (lounge.Rect.Length / 2.0), view3D ? 2.4f : 2.0f);
                break;
            case 4:
                Save("4_lounge");
                LineUp(sim, w);
                Focus(h2, w.X + 6, w.Bottom + 3, view3D ? 3.2f : 2.6f);
                break;
            case 5:
                Save("5_team");
                h2.Hud.Visible = true;
                if (_watched is not null)
                {
                    h2.SelectPatient(_watched, centre: true);
                }

                break;
            case 6:
                Save("6_patient_panel");
                GD.Print($"[mental-health-ward] {ward.Name}: {ward.Beds.Count} bedrooms, {ward.Beds.Count(b => b.OccupantId is not null)} in use; in the lounge or garden {sim.DayRoomVisits.Count}.");
                GD.Print("[mental-health-ward] Done.");
                GetTree().Quit(0);
                return;
        }

        _step++;
        _frame = 0;
    }

    private static bool BedTile(HospitalSimulation sim, Patient p) =>
        p.CareSpaceId is { } id && sim.FindSpace(id) is { } ward && p.BedIndex is { } i && i < ward.Beds.Count && ward.Beds[i].PatientSpot == p.Tile;

    /// <summary>The ward team standing in a row in the corridor below the ward: nurses, support workers and psychiatrists.</summary>
    private static void LineUp(HospitalSimulation sim, GridRect r)
    {
        var x = r.X + 1;
        foreach (var (role, skill) in new[]
                 {
                     (StaffRole.MentalHealthNurse, 0.5), (StaffRole.MentalHealthNurse, 0.78),
                     (StaffRole.SupportWorker, 0.45), (StaffRole.SupportWorker, 0.7),
                     (StaffRole.Psychiatrist, 0.65), (StaffRole.Psychiatrist, 0.9),
                 })
        {
            var member = sim.HireStaff(role, null, skill);
            member.PlaceAt(new GridCoord(x, r.Bottom + 2));
            x += 2;
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

        GD.Print($"[mental-health-ward] {sim.Now:ddd HH:mm}: {(until() ? "reached" : "gave up")}");
    }

    private void Save(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        var path = $"{_directory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[mental-health-ward] Saved {path}");
    }
}
