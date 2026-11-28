using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the Mental Health Act (mental health stage 3), run with:
/// godot --path game -- --mental-health-act-shots=DIR [--3d] (needs a display).
/// Loads the example hospital, has the police bring people in under Section 136, and photographs the officer in A&amp;E,
/// the Section 136 suite, the AMHP's assessment, the patient record, the out-of-area bed button and the Stats card.
/// </summary>
public partial class MentalHealthActShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;
    private readonly List<Patient> _people = new();
    private Patient? _assessed;
    private Patient? _waiting;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--mental-health-act-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new MentalHealthActShots { Name = "MentalHealthActShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--mental-health-act-shots=".Length..] };
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
        var suite = sim.Section136Suites.First();
        switch (_step)
        {
            case 0:
                h2.Hud.Visible = false;
                foreach (var (condition, age) in new[] { ("mania", 27), ("first_episode_psychosis", 34), ("mania", 41) })
                {
                    _people.Add(sim.SpawnSection136Patient(condition, age));
                }

                // The officer with them in A&E, once they have been triaged.
                Run(h2, () => _people.Any(p => p.TriagedAt is not null && sim.Parents.Any(o => o.ChildId == p.Id && o.Police) && !p.IsMoving), TimeSpan.FromHours(3));
                var first = _people.First(p => sim.Parents.Any(o => o.ChildId == p.Id && o.Police));
                h2.ShowFloor(sim.FloorOf(first));
                Focus(h2, first.X + 0.5, first.Y + 0.5, view3D ? 2.6f : 2.2f);
                break;
            case 1:
                Save("1_police_in_ae");
                Run(h2, () => _people.Any(p => p.InSection136Suite && !p.IsMoving), TimeSpan.FromHours(12));
                h2.ShowFloor(sim.FloorOf(suite));
                Focus(h2, suite.Rect.X + (suite.Rect.Width / 2.0), suite.Rect.Y + (suite.Rect.Length / 2.0), view3D ? 2.6f : 2.2f);
                break;
            case 2:
                Save("2_section_136_suite");
                Run(h2, () => _people.Any(p => p.MentalHealthAct == MentalHealthActStatus.Assessing && sim.AmhpFor(p) is { IsMoving: false }), TimeSpan.FromHours(30));
                _assessed = _people.FirstOrDefault(p => p.MentalHealthAct == MentalHealthActStatus.Assessing) ?? _people[0];
                h2.ShowFloor(sim.FloorOf(_assessed));
                Focus(h2, _assessed.X + 0.5, _assessed.Y + 0.5, view3D ? 2.8f : 2.4f);
                break;
            case 3:
                Save("3_amhp_assessment");
                h2.Hud.Visible = true;
                h2.SelectPatient(_assessed!, centre: true);
                break;
            case 4:
                Save("4_patient_record");
                h2.ClearSelectionForTest();

                // Willow ward full: the next admission waits in A&E, and the record offers an out-of-area bed.
                foreach (var ward in sim.MentalHealthWards)
                {
                    foreach (var bed in ward.Beds.Where(b => b.OccupantId is null))
                    {
                        bed.OccupantId = 999_999;
                    }
                }

                _waiting = sim.SpawnPatient(ArrivalMethod.WalkIn, "severe_depression", 46);
                Run(h2, () => sim.CanBuyOutOfAreaBed(_waiting) && sim.Now - _waiting.BedRequestedAt > TimeSpan.FromHours(3), TimeSpan.FromHours(30));
                h2.ShowFloor(sim.FloorOf(_waiting));
                h2.SelectPatient(_waiting, centre: true);
                break;
            case 5:
                Save("5_out_of_area_bed");
                h2.ClearSelectionForTest();
                h2.Hud.ToggleStats();
                break;
            case 6:
                Save("6_stats");
                GD.Print($"[mental-health-act] S136 {sim.Section136Arrivals}, in suite {sim.Section136HandedOverInSuite}, police hours {sim.PoliceHoursInEmergency:0.0}, MHA {sim.MentalHealthActAssessments}, detained {sim.Detentions}, out of area {sim.OutOfAreaPlacements}.");
                GD.Print("[mental-health-act] Done.");
                GetTree().Quit(0);
                return;
        }

        _step++;
        _frame = 0;
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

        GD.Print($"[mental-health-act] {sim.Now:ddd HH:mm}: {(until() ? "reached" : "gave up")}");
    }

    private void Save(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        var path = $"{_directory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[mental-health-act] Saved {path}");
    }
}
