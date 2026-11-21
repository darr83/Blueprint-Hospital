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
/// Review shots of visitors and people walking about, run with: godot --path game -- --visitor-shots=DIR [--3d]
/// (needs a display). Loads the example hospital, runs it until the wards have visitors, then photographs a ward
/// at visiting time, the room panel's visiting hours, the ICU, the café and A&amp;E's waiting area.
/// </summary>
public partial class VisitorShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;
    private CareSpace? _ward;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--visitor-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new VisitorShots { Name = "VisitorShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--visitor-shots=".Length..] };
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

        var hs = _hospital;
        var sim = hs.Session.Simulation;
        var view3D = GameManager.Instance.Settings.View3D;
        switch (_step)
        {
            case 0:
                hs.Hud.Visible = false;
                GetViewport().WarpMouse(new Vector2(2, 2));
                Run(hs, () => sim.Now.Hour >= 14 && SeatedOnAdultWard(sim).Count() >= 3, TimeSpan.FromHours(40));
                var best = SeatedOnAdultWard(sim).GroupBy(v => v.WardId).OrderByDescending(g => g.Count()).First();
                _ward = sim.FindSpace(best.Key)!;
                Look(hs, _ward, view3D ? 1.5 : 0.95);
                break;
            case 1:
                Save("1_ward_visiting_time");
                var sitter = SeatedOnAdultWard(sim).FirstOrDefault(v => v.WardId == _ward!.Id) ?? SeatedOnAdultWard(sim).First();
                hs.Camera.SnapTo(IsoDraw.ToScreen(sitter.X, sitter.Y), view3D ? 3.2f : 2.4f);
                break;
            case 2:
                Save("2_ward_close_up");
                hs.Hud.Visible = true;
                hs.SetMode(GameMode.Build);
                hs.Hud.ShowRoom(_ward!);
                break;
            case 3:
                Save("3_visiting_hours_panel");
                hs.Hud.Visible = false;
                hs.SetMode(GameMode.Play);
                hs.Hud.HideRightPanels();
                var icu = sim.Wards.First(w => sim.UnitOf(w) == Simulation.Data.Models.WardUnit.IntensiveCare);
                Run(hs, () => sim.VisitorsOnWard(icu).Any(v => v.Activity == VisitorActivity.Visiting), TimeSpan.FromHours(24));
                Look(hs, icu, view3D ? 2.2 : 1.5);
                break;
            case 4:
                Save("4_icu_visitors");
                var cafe = sim.SpacesOfKind(CareSpaceKind.Cafeteria).First();
                Run(hs, () => sim.RefreshmentTrips.Count(t => t.Person is Visitor or Simulation.Staff.StaffMember) >= 2 && sim.RefreshmentTrips.Count >= 4, TimeSpan.FromHours(6));
                Look(hs, cafe, view3D ? 2.0 : 1.4);
                break;
            case 5:
                Save("5_cafe");
                var waiting = sim.MainWaitingRoom ?? sim.SpacesOfKind(CareSpaceKind.Waiting).First();
                foreach (var _ in Enumerable.Range(0, 30))
                {
                    sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
                }

                Run(hs, () => sim.Strolls.Count >= 2, TimeSpan.FromHours(4));
                Look(hs, waiting, view3D ? 1.6 : 1.0);
                break;
            case 6:
                Save("6_ae_waiting_and_walks");
                GD.Print("[visitors] Done.");
                GetTree().Quit(0);
                return;
        }

        _step++;
        _frame = 0;
    }

    /// <summary>Visitors sitting on a bedside chair on an ordinary adult ward.</summary>
    private static System.Collections.Generic.IEnumerable<Visitor> SeatedOnAdultWard(HospitalSimulation sim) =>
        sim.Visitors.Where(v => v.Activity == VisitorActivity.Visiting && v.BabyId is null && !v.IsMoving && sim.IsSeat(v.Tile)
                                && sim.FindSpace(v.WardId) is { } w && sim.UnitOf(w) == Simulation.Data.Models.WardUnit.None && !sim.IsMaternityWard(w)
                                && !w.Furniture.Any(f => f.ItemId == "parent_bed"));

    private static void Look(HospitalScene hospital, CareSpace room, double zoom)
    {
        var sim = hospital.Session.Simulation;
        hospital.ShowFloor(sim.Site.FloorOf(new GridCoord(room.Rect.X, room.Rect.Y)));
        hospital.Camera.SnapTo(IsoDraw.ToScreen(room.Rect.X + (room.Rect.Width / 2.0), room.Rect.Y + (room.Rect.Length / 2.0)), (float)zoom);
    }

    private static void Run(HospitalScene hospital, Func<bool> until, TimeSpan limit)
    {
        var sim = hospital.Session.Simulation;
        var end = sim.Now + limit;
        var clock = hospital.Session.Clock;
        while (sim.Now < end && !until())
        {
            clock.Step();
        }

        GD.Print($"[visitors] {sim.Now:ddd HH:mm}: {(until() ? "reached" : "gave up")} · {sim.Visitors.Count} visitors, {sim.Strolls.Count} walking, {sim.RefreshmentTrips.Count} at the café");
    }

    private void Save(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        var path = $"{_directory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[visitors] Saved {path}");
    }
}
