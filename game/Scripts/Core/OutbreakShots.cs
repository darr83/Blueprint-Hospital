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
/// Review shots of a norovirus outbreak, run with: godot --path game -- --outbreak-shots=DIR [--3d] (needs a
/// display). Loads the example hospital, runs it until an adult ward is busy, closes it with norovirus, then
/// photographs the yellow sign at its door, the whole ward, and its room panel.
/// </summary>
public partial class OutbreakShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;
    private CareSpace? _ward;

    public static bool Begin()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.FirstOrDefault(a => a.StartsWith("--outbreak-shots=", StringComparison.Ordinal)) is not { } arg)
        {
            return false;
        }

        GameManager.Instance.Settings.View3D = Array.IndexOf(args, "--3d") >= 0;
        var shots = new OutbreakShots { Name = "OutbreakShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--outbreak-shots=".Length..] };
        GameManager.Instance.AddChild(shots);
        Callable.From(() => GameManager.Instance.LoadExampleHospital()).CallDeferred();
        return true;
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
                GetViewport().WarpMouse(new Vector2(2, 2));
                Run(hs, () => AdultWards(sim).Any(w => OnWard(sim, w) >= 4), TimeSpan.FromHours(30));
                _ward = AdultWards(sim).OrderByDescending(w => OnWard(sim, w)).First();
                GD.Print($"[outbreak] {sim.RunCheat($"norovirus {_ward.Name}").Message}");
                GD.Print($"[outbreak] {sim.RunCheat("norovirus close").Message}");
                _ward = sim.Outbreaks.Where(o => o.IsClosedAt(sim.Now)).Select(o => sim.FindSpace(o.WardId)).First(w => w is not null)!;
                var sign = OutbreakSigns.Plan(sim).First(s => s.Ward == _ward.Name);
                if (view3D && _ward.Doors[0].Side is WallSide.North or WallSide.West)
                {
                    // Turn round so the ward's door, and its sign, face the camera.
                    hs.RotateView(2);
                }

                hs.ShowFloor(sim.Site.FloorOf(new GridCoord(_ward.Rect.X, _ward.Rect.Y)));
                hs.Camera.SnapTo(IsoDraw.ToScreen(sign.X, sign.Y), view3D ? 3.4f : 2.4f);
                break;
            case 1:
                Save("1_closed_ward_sign");
                Look(hs, _ward!, view3D ? 1.4 : 0.95);
                break;
            case 2:
                Save("2_closed_ward");
                hs.SetMode(GameMode.Build);
                hs.Hud.ShowRoom(_ward!);
                break;
            case 3:
                Save("3_room_panel");
                GD.Print("[outbreak] Done.");
                GetTree().Quit(0);
                return;
        }

        _step++;
        _frame = 0;
    }

    private static System.Collections.Generic.IEnumerable<CareSpace> AdultWards(HospitalSimulation sim) =>
        sim.Wards.Where(w => sim.UnitOf(w) == Simulation.Data.Models.WardUnit.None && !sim.IsMaternityWard(w) && w.DepartmentId is not ("paediatrics" or "emergency"));

    private static int OnWard(HospitalSimulation sim, CareSpace ward) =>
        sim.Patients.Count(p => p.Stage == PatientStage.OnWard && p.CareSpaceId == ward.Id);

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

        GD.Print($"[outbreak] {sim.Now:ddd HH:mm}: {(until() ? "reached" : "gave up")}");
    }

    private void Save(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        var path = $"{_directory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[outbreak] Saved {path}");
    }
}
