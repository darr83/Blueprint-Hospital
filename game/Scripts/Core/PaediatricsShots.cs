using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the children's ward, run with: godot --path game -- --paediatrics-shots=DIR [--3d] (needs a display).
/// Builds a children's ward and children's HDU with their team, admits some children, then photographs the ward,
/// the HDU and the build bar's Children tiles.
/// </summary>
public partial class PaediatricsShots : Node
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;
    private bool _example;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--paediatrics-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var example = Array.IndexOf(OS.GetCmdlineUserArgs(), "--example") >= 0;
                var shots = new PaediatricsShots { Name = "PaediatricsShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--paediatrics-shots=".Length..], _example = example };
                GameManager.Instance.AddChild(shots);
                Callable.From(() =>
                {
                    if (example)
                    {
                        GameManager.Instance.LoadExampleHospital();
                    }
                    else
                    {
                        GameManager.Instance.StartNewGame("Children's Review General");
                    }
                }).CallDeferred();
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
                if (!_example)
                {
                    Build(h.Session.Simulation);
                }

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
        if (_example)
        {
            ExampleStep(h2, sim);
            return;
        }

        var ward = sim.Spaces.First(s => s.TemplateId == "childrens_ward");
        var hdu = sim.Spaces.First(s => s.TemplateId == "childrens_hdu");
        switch (_step)
        {
            case 0:
                h2.Hud.Visible = false;
                Run(h2, () => sim.Parents.Count(p => p.FoldOutBed is not null) >= 3, TimeSpan.FromHours(16));
                Focus(h2, ward.Rect.X + (ward.Rect.Width / 2.0), ward.Rect.Y + (ward.Rect.Length / 2.0), GameManager.Instance.Settings.View3D ? 1.8f : 1.1f);
                break;
            case 1:
                Save("1_childrens_ward");
                Focus(h2, (ward.Rect.X + hdu.Rect.Right) / 2.0, ward.Rect.Y + (ward.Rect.Length / 2.0), GameManager.Instance.Settings.View3D ? 1.3f : 0.75f);
                break;
            case 2:
                Save("2_ward_and_hdu");
                h2.Hud.Visible = true;
                h2.SetMode(GameMode.Build);
                h2.Hud.ShowRoom(ward);
                break;
            case 3:
                Save("3_room_panel");
                h2.Hud.Visible = false;
                h2.SetMode(GameMode.Play);
                h2.Hud.HideRightPanels();
                Run(h2, () => sim.Parents.Count(p => p.Activity == ParentActivity.Sleeping) >= 2, TimeSpan.FromHours(20));
                Focus(h2, ward.Rect.X + (ward.Rect.Width / 2.0), ward.Rect.Y + (ward.Rect.Length / 2.0), GameManager.Instance.Settings.View3D ? 1.8f : 1.1f);
                break;
            case 4:
                Save("4_night_parents_asleep");
                WalkIns(sim);
                Run(h2, () => sim.Patients.Any(p => p.CareSpaceId == Room(sim, "childrens_cubicle").Id) && sim.Patients.Count(p => Room(sim, "childrens_waiting_room").Rect.Contains(p.Tile)) >= 2, TimeSpan.FromHours(3));
                Focus(h2, Room(sim, "childrens_waiting_room").Rect.X + 2, Room(sim, "childrens_waiting_room").Rect.Y + 8, GameManager.Instance.Settings.View3D ? 1.8f : 1.1f);
                break;
            case 5:
                Save("5_childrens_cubicle_and_waiting_room");
                Run(h2, () => sim.Patients.Any(p => p.CareSpaceId == Room(sim, "picu").Id), TimeSpan.FromHours(12));
                Focus(h2, Room(sim, "picu").Rect.X + (Room(sim, "picu").Rect.Width / 2.0), Room(sim, "picu").Rect.Y + 6, GameManager.Instance.Settings.View3D ? 1.8f : 1.1f);
                break;
            case 6:
                Save("6_picu");
                GD.Print("[paediatrics] Done.");
                GetTree().Quit(0);
                return;
        }

        _step++;
        _frame = 0;
    }

    /// <summary>Stage 4 shots in the example hospital: the play and teenage rooms, the parents' room and the children's clinic.</summary>
    private void ExampleStep(HospitalScene h, HospitalSimulation sim)
    {
        var view3D = GameManager.Instance.Settings.View3D;
        void Look(string template, int floor, double zoom3D, double zoom2D, double dx = 0)
        {
            h.ShowFloor(floor);
            var r = Room(sim, template).Rect;
            Focus(h, r.X + (r.Width / 2.0) + dx, r.Y + (r.Length / 2.0), (float)(view3D ? zoom3D : zoom2D));
        }

        switch (_step)
        {
            case 0:
                h.Hud.Visible = false;
                // Enough children to fill the cubicles, so some are left waiting and go to play.
                foreach (var age in new[] { 4, 5, 7, 9, 13, 14, 3, 6, 8, 10, 12, 15, 4, 6, 9, 11, 13, 14, 5, 7 })
                {
                    sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration", age);
                }

                Run(h, () => sim.PlayVisits.Count(v => v.Playing) >= 2, TimeSpan.FromHours(6));
                Look("play_room", 0, 1.9, 1.3, 3.5);
                break;
            case 1:
                Save("7_play_and_teen_rooms");
                Look("play_room", 0, 2.6, 1.9);
                break;
            case 2:
                Save("8_play_room_close");
                Run(h, () => sim.Parents.Any(p => p.Activity == ParentActivity.InParentsRoom && !p.IsMoving), TimeSpan.FromHours(12));
                Look("parents_room", 2, 2.2, 1.6);
                break;
            case 3:
                Save("9_parents_room");
                Look("childrens_clinic", 0, 2.2, 1.6);
                break;
            case 4:
                Save("10_childrens_clinic");
                GD.Print("[paediatrics] Done.");
                GetTree().Quit(0);
                return;
        }

        _step++;
        _frame = 0;
    }

    private static CareSpace Room(HospitalSimulation sim, string template) => sim.Spaces.First(s => s.TemplateId == template);

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

        GD.Print($"[paediatrics] {sim.Now:HH:mm}: {(until() ? "reached" : "gave up")}");
    }

    private void Save(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        var path = $"{_directory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[paediatrics] Saved {path}");
    }

    private static void Build(HospitalSimulation sim)
    {
        sim.AdjustBalance(5_000_000);
        sim.UnlockDepartment(HospitalSimulation.PaediatricsDepartmentId);
        void Report(string what, BuildCheck check) => GD.Print($"[paediatrics] {what}: {(check.CanBuild ? "built" : string.Join("; ", check.Problems))}");
        Report("extension", sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)));
        Report("corridor", sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 11, 44, 6), WallSide.South)));
        Report("children's cubicle", sim.BuildRoom(sim.AutoPlan("childrens_cubicle", new GridRect(B.X - 40, B.Y, 7, 11), doorSide: WallSide.South)));
        Report("children's waiting room", sim.BuildRoom(new RoomPlan("childrens_waiting_room", new GridRect(B.X - 33, B.Y, 10, 11), WallSide.South), "Children's waiting room"));
        var waiting = sim.Spaces.First(s => s.TemplateId == "childrens_waiting_room");
        Report("waiting room door", sim.PlaceWaitingDoor(waiting.Id, new GridCoord(B.X - 29, B.Y + 10)));
        foreach (var y in new[] { B.Y + 2, B.Y + 5 })
        {
            for (var x = B.X - 32; x <= B.X - 25; x++)
            {
                sim.PlaceChair(new GridCoord(x, y), WallSide.South, "#D6A72C");
            }
        }

        Report("PICU", sim.BuildRoom(sim.AutoPlan("picu", new GridRect(B.X - 23, B.Y, 23, 11), doorSide: WallSide.South)));
        Report("children's ward", sim.BuildRoom(sim.AutoPlan("childrens_ward", new GridRect(B.X - 40, B.Y + 17, 24, 15))));
        Report("children's HDU", sim.BuildRoom(sim.AutoPlan("childrens_hdu", new GridRect(B.X - 16, B.Y + 17, 14, 15))));
        foreach (var need in sim.RecommendedStaffing().ToList())
        {
            for (var i = 0; i < need.Short; i++)
            {
                sim.HireStaff(need.Role, need.RoomId, 0.8, need.Shift);
            }
        }

        foreach (var (condition, age) in new[] { ("diabetic_ketoacidosis", 13), ("diabetic_ketoacidosis", 9), ("diabetic_ketoacidosis", 6), ("diabetic_ketoacidosis", 11),
                     ("paediatric_sepsis", 4), ("paediatric_sepsis", 7), ("paediatric_sepsis", 2) })
        {
            sim.SpawnPatient(ArrivalMethod.Ambulance, condition, age);
        }
    }

    /// <summary>Walk-in children (and one adult, who must not use the children's rooms) for the children's A&amp;E shot.</summary>
    private static void WalkIns(HospitalSimulation sim)
    {
        foreach (var (condition, age) in new[] { ("pulled_elbow", 3), ("viral_wheeze", 2), ("dehydration_gastroenteritis", 5), ("acute_asthma_child", 9), ("pulled_elbow", 4), ("viral_wheeze", 1) })
        {
            sim.SpawnPatient(ArrivalMethod.WalkIn, condition, age);
        }
    }
}
