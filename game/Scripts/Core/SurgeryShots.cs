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
/// Review shots of a theatre suite, run with: godot --path game -- --surgery-shots=DIR [--3d] (needs a display).
/// Builds a theatre with its anaesthetic room and a surgical ward, fills them with surgical patients, then
/// photographs a patient being put to sleep, the operation, and the Surgery panel's two tabs.
/// </summary>
public partial class SurgeryShots : Node
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--surgery-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new SurgeryShots { Name = "SurgeryShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--surgery-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Surgery Review General")).CallDeferred();
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
                Build(h.Session.Simulation);
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
        var anaesthetic = sim.SpacesOfKind(CareSpaceKind.AnaestheticRoom).First();
        var theatre = sim.SpacesOfKind(CareSpaceKind.Theatre).First();
        switch (_step)
        {
            case 0:
                h2.Hud.Visible = false;
                Run(h2, () => sim.Tasks.Any(t => t.Kind == TaskKind.Surgery && t.Phase == SurgeryPhase.Anaesthetic && t.AnaestheticSecondsLeft is < 400), TimeSpan.FromHours(30));
                Focus(h2, (anaesthetic.Rect.X + theatre.Rect.Right) / 2.0, anaesthetic.Rect.Y + 5);
                break;
            case 1:
                Save("1_put_to_sleep");
                Run(h2, () => sim.Tasks.Any(t => t.Kind == TaskKind.Surgery && t.Started && t.SpaceId == theatre.Id), TimeSpan.FromHours(2));
                break;
            case 2:
                Save("2_operating");
                h2.Hud.Visible = true;
                h2.Hud.ToggleSurgery();
                break;
            case 3:
                Save("3_panel_board");
                h2.Hud.Surgery.ShowTab(waitingList: true);
                break;
            case 4:
                Save("4_panel_waiting_list");
                h2.Hud.ToggleSurgery();
                h2.SetMode(GameMode.Build);
                h2.Hud.ShowRoom(anaesthetic);
                break;
            case 5:
                Save("5_room_panel");
                GD.Print("[surgery] Done.");
                GetTree().Quit(0);
                return;
        }

        _step++;
        _frame = 0;
    }

    private static void Focus(HospitalScene hospital, double x, double y) => hospital.Camera.SnapTo(IsoDraw.ToScreen(x, y), 2.0f);

    private static void Run(HospitalScene hospital, Func<bool> until, TimeSpan limit)
    {
        var sim = hospital.Session.Simulation;
        var end = sim.Now + limit;
        var clock = hospital.Session.Clock;
        while (sim.Now < end && !until())
        {
            clock.Step();
        }

        GD.Print($"[surgery] {sim.Now:HH:mm}: {(until() ? "reached" : "gave up")}");
    }

    private void Save(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        var path = $"{_directory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[surgery] Saved {path}");
    }

    private static void Build(HospitalSimulation sim)
    {
        sim.AdjustBalance(5_000_000);
        foreach (var department in new[] { "general_surgery", "gynaecology" })
        {
            sim.UnlockDepartment(department);
        }

        void Report(string what, BuildCheck check) => GD.Print($"[surgery] {what}: {(check.CanBuild ? "built" : string.Join("; ", check.Problems))}");
        Report("extension", sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)));
        Report("corridor", sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 12, 44, 6), WallSide.South)));
        Report("theatre", sim.BuildRoom(sim.AutoPlan("operating_theatre", new GridRect(B.X - 40, B.Y + 18, 12, 12))));
        Report("anaesthetic room", sim.BuildRoom(sim.AutoPlan("anaesthetic_room", new GridRect(B.X - 28, B.Y + 18, 7, 10))));
        Report("surgical ward", sim.BuildRoom(sim.AutoPlan("surgical_ward", new GridRect(B.X - 21, B.Y + 18, 16, 12))));
        var theatre = sim.SpacesOfKind(CareSpaceKind.Theatre).Single();
        foreach (var department in sim.SurgicalDepartments)
        {
            sim.SetTheatreAccess(theatre.Id, department.Id, true);
        }

        foreach (var need in sim.RecommendedStaffing().ToList())
        {
            for (var i = 0; i < need.Short; i++)
            {
                sim.HireStaff(need.Role, need.RoomId, 0.8, need.Shift);
            }
        }

        foreach (var condition in new[] { "acute_appendicitis", "perforated_peptic_ulcer", "perianal_abscess", "ectopic_pregnancy", "acute_appendicitis", "strangulated_inguinal_hernia" })
        {
            sim.SpawnPatient(ArrivalMethod.Ambulance, condition);
        }

        sim.SpawnPlannedPatient("planned_hernia_repair");
    }
}
