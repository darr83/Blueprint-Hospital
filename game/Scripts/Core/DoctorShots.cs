using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of Be A Doctor, run with: godot --path game -- --doctor-shots=DIR (needs a display).
/// Runs A&amp;E for a few hours, becomes one of the doctors and photographs each step of seeing a patient.
/// </summary>
public partial class DoctorShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;
    private Simulation.Patients.Patient? _patient;
    private ClinicalTask? _task;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--doctor-shots=", StringComparison.Ordinal))
            {
                var shots = new DoctorShots { Name = "DoctorShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--doctor-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Blueprint General Hospital")).CallDeferred();
                return true;
            }
        }

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--doctor-review=", StringComparison.Ordinal))
            {
                var shots = new DoctorShots { Name = "DoctorShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--doctor-review=".Length..], _review = true };
                foreach (var other in OS.GetCmdlineUserArgs().Where(a => a.StartsWith("--wall-extra=", StringComparison.Ordinal)))
                {
                    World3D.FirstPerson3D.ExtraHeightMetres = double.Parse(other["--wall-extra=".Length..], System.Globalization.CultureInfo.InvariantCulture);
                }

                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Blueprint General Hospital")).CallDeferred();
                return true;
            }
        }

        return false;
    }

    private bool _review;
    private System.Collections.Generic.List<(string Name, Simulation.World.GridCoord At, float Yaw, float Pitch)>? _views;

    /// <summary>The graphics review: stands the doctor in each kind of room and looks around, with no panel in the way.</summary>
    private void Review(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        if (_views is null)
        {
            var doctor = sim.PlayableDoctors.OrderBy(d => d.Id).First();
            hospital.EnterDoctorMode(doctor);
            hospital.DoctorHud.Visible = false;
            _views = new();
            foreach (var kind in new[] { Simulation.Hospital.CareSpaceKind.Corridor, Simulation.Hospital.CareSpaceKind.Waiting, Simulation.Hospital.CareSpaceKind.Triage, Simulation.Hospital.CareSpaceKind.Majors, Simulation.Hospital.CareSpaceKind.Resus, Simulation.Hospital.CareSpaceKind.StaffRoom, Simulation.Hospital.CareSpaceKind.NursesStation, Simulation.Hospital.CareSpaceKind.DoctorsOffice })
            {
                var space = sim.Spaces.Where(sp => sp.Kind == kind).OrderByDescending(sp => sp.Rect.Width * sp.Rect.Length).FirstOrDefault();
                if (space is null)
                {
                    continue;
                }

                var r = space.Rect;
                var centre = new Simulation.World.GridCoord(r.X + (r.Width / 2), r.Y + (r.Length / 2));
                var spot = Enumerable.Range(0, 6).SelectMany(d => new[] { new Simulation.World.GridCoord(centre.X + d, centre.Y), new Simulation.World.GridCoord(centre.X - d, centre.Y), new Simulation.World.GridCoord(centre.X, centre.Y + d), new Simulation.World.GridCoord(centre.X, centre.Y - d) })
                    .FirstOrDefault(c => r.Contains(c) && sim.Nav.IsWalkable(c), centre);
                var along = r.Width >= r.Length ? 90f : 0f;
                _views.Add(($"{kind}_a", spot, along, -3));
                _views.Add(($"{kind}_b", spot, along + 180, -3));
                _views.Add(($"{kind}_c", spot, along + 90, -3));
                _views.Add(($"{kind}_up", spot, along + 45, 28));
            }

            _frame = -6;
            return;
        }

        var index = _step / 2;
        if (index >= _views.Count)
        {
            GD.Print("[doctor] Review done.");
            GetTree().Quit(0);
            return;
        }

        var (name, at, yaw, pitch) = _views[index];
        if (_step % 2 == 0)
        {
            sim.PlayerDoctor?.PlaceAt(at);
            hospital.Doctor.LookForTest(yaw, pitch);
        }
        else
        {
            Save($"{index:00}_{name}");
        }

        _step++;
        _frame = -4;
    }

    private void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        var path = $"{_directory}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[doctor] Saved {path}");
    }

    private void Next(int wait = 12)
    {
        _step++;
        _frame = -wait;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                if (!h.Session.Simulation.SpacesOfKind(Simulation.Hospital.CareSpaceKind.Waiting).Any())
                {
                    h.Session.Simulation.BuildStarterWaitingRoom();
                }

                // A busy afternoon: let A&E fill up before anyone is played.
                for (var i = 0; i < 2 * 3600; i++)
                {
                    h.Session.Clock.Step();
                }

                h.Session.Clock.SetSpeed(SimulationSpeed.Normal);
                _frame = -20;
            }

            return;
        }

        if (_frame < 0)
        {
            return;
        }

        if (_review)
        {
            Review(_hospital);
            return;
        }

        var hospital = _hospital;
        var sim = hospital.Session.Simulation;
        var mode = hospital.Doctor;
        var hud = hospital.DoctorHud;
        switch (_step)
        {
            case 0:
                hospital.Hud.ToggleDoctors();
                Next();
                break;
            case 1:
                Save("1_choose_a_doctor");
                {
                    // Whoever has an assessment waiting nearest to them.
                    var doctor = sim.PlayableDoctors.OrderBy(d => d.Id).First();
                    GD.Print($"[doctor] Playing {doctor.Name}: {hospital.EnterDoctorMode(doctor)}");
                }

                Next(4);
                break;
            case 2:
                {
                    // Wait for a patient nobody has seen yet, so every step of seeing them can be shown.
                    var tasks = sim.PlayerTasks();
                    var task = tasks.FirstOrDefault(t => t.Kind == TaskKind.DoctorAssessment);
                    for (var i = 0; i < 1800 && task is null; i++)
                    {
                        hospital.Session.Clock.Step();
                        task = sim.PlayerTasks().FirstOrDefault(t => t.Kind == TaskKind.DoctorAssessment && sim.FindPatient(t.PatientId) is { IsMoving: false, Stage: Simulation.Patients.PatientStage.InTreatmentSpace });
                    }

                    if (task is null)
                    {
                        _frame = -1;
                        break;
                    }

                    GD.Print($"[doctor] {sim.Now:HH:mm} jobs: {string.Join(", ", sim.PlayerTasks().Take(6).Select(t => t.Kind + " " + t.Label))}");
                    _task = task;
                    _patient = sim.FindPatient(task.PatientId);
                    GD.Print($"[doctor] Claim: {sim.PlayerClaim(task.Id)}");
                    sim.PlayerDoctor!.PlaceAt(sim.Spaces.Where(sp => sp.Kind == Simulation.Hospital.CareSpaceKind.Triage).Select(sp => sp.StaffSpot).First());
                    hud.CloseCard();
                    Next(20);
                }

                break;
            case 3:
                Save("2_my_jobs");
                {
                    mode.WalkThere();
                }

                Next(3);
                break;
            case 4:
                Save("2b_walking_to_the_patient");
                {
                    var spot = hud.Target!.Value;
                    var member = sim.PlayerDoctor!;
                    member.PlaceAt(spot);
                    var (px, py) = hospital.World.FootGrid(_patient!);
                    mode.LookForTest(Mathf.RadToDeg((float)Math.Atan2(-(px - member.X), -(py - member.Y))), -22);
                }

                Next();
                break;
            case 5:
                Save("3_at_the_bedside");
                GD.Print($"[doctor] Start: {hud.StartJob()}");
                Next(20);
                break;
            case 6:
                Save("4_taking_the_history");
                for (var i = 0; i < 4000 && sim.PlayerWorkProgress() is not null; i++)
                {
                    hospital.Session.Clock.Step();
                }

                hud.OpenCard(_patient!, "History");
                Next(20);
                break;
            case 7:
                Save("5_history");
                hud.OpenCard(_patient!, "Tests");
                Next(20);
                break;
            case 8:
                Save("6_tests");
                {
                    var wanted = sim.TestOptionsFor(_patient!).Where(o => o.Available).OrderByDescending(o => o.Relevant).Take(4).Select(o => o.Test.Id).ToList();
                    GD.Print($"[doctor] Tests: {sim.PlayerOrderTests(_patient!.Id, wanted)}");
                    for (var i = 0; i < 2 * 3600 && _patient.Investigations.Any(x => x.Status != Simulation.Patients.InvestigationStatus.Resulted); i++)
                    {
                        hospital.Session.Clock.Step();
                    }

                    if (sim.PlayerDoctor is { } member && _patient.IsInDepartment)
                    {
                        member.PlaceAt(sim.PlayerBedsideSpot(_patient));
                        var (px, py) = hospital.World.FootGrid(_patient);
                        mode.LookForTest(Mathf.RadToDeg((float)Math.Atan2(-(px - member.X), -(py - member.Y))), -22);
                    }

                    hud.OpenCard(_patient, "Diagnosis");
                }

                Next(20);
                break;
            case 9:
                Save("7_diagnosis");
                GD.Print($"[doctor] Hint: {hud.ToggleHint()}");
                Next(20);
                break;
            case 10:
                Save("8_hint");
                hud.CloseHint();
                GD.Print($"[doctor] Decide for me: {sim.PlayerDecideForMe(_patient!.Id)}");
                hud.OpenCard(_patient, "Treatment");
                Next(20);
                break;
            case 11:
                Save("9_treatment");
                hud.CloseCard();
                Next(20);
                break;
            case 12:
                Save("10_my_jobs");
                mode.Exit();
                Next(20);
                break;
            case 13:
                Save("11_back_to_the_hospital");
                GD.Print("[doctor] Done.");
                GetTree().Quit(0);
                break;
        }
    }
}
