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

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--paediatrics-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new PaediatricsShots { Name = "PaediatricsShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--paediatrics-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Children's Review General")).CallDeferred();
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
        var ward = sim.Spaces.First(s => s.TemplateId == "childrens_ward");
        var hdu = sim.Spaces.First(s => s.TemplateId == "childrens_hdu");
        switch (_step)
        {
            case 0:
                h2.Hud.Visible = false;
                Run(h2, () => ward.Beds.Count(b => b.OccupantId is not null) >= 3, TimeSpan.FromHours(16));
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
                GD.Print("[paediatrics] Done.");
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
        Report("corridor", sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 9, 44, 8), WallSide.South)));
        Report("children's ward", sim.BuildRoom(sim.AutoPlan("childrens_ward", new GridRect(B.X - 40, B.Y + 17, 24, 15))));
        Report("children's HDU", sim.BuildRoom(sim.AutoPlan("childrens_hdu", new GridRect(B.X - 16, B.Y + 17, 14, 15))));
        foreach (var need in sim.RecommendedStaffing().ToList())
        {
            for (var i = 0; i < need.Short; i++)
            {
                sim.HireStaff(need.Role, need.RoomId, 0.8, need.Shift);
            }
        }

        foreach (var (condition, age) in new[] { ("diabetic_ketoacidosis", 13), ("diabetic_ketoacidosis", 9), ("diabetic_ketoacidosis", 6), ("diabetic_ketoacidosis", 11) })
        {
            sim.SpawnPatient(ArrivalMethod.Ambulance, condition, age);
        }
    }
}
