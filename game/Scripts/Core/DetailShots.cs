using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.World;
using BlueprintHospital.UI;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the detail panels, run with: godot --path game -- --detail-shots=DIR [--3d] (needs a display).
/// Loads the example hospital, runs it to a busy weekday late morning, then photographs a ward's Patients, Staff
/// and Today tabs, the big bedside monitor, a clinic patient's card, a clinic's list, the Inpatients and Clinics
/// boards, the illness journal and the hover tags over a monitor and a bed.
/// </summary>
public partial class DetailShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _step;
    private HospitalScene? _hospital;
    private CareSpace? _ward;
    private ObsMonitorInfo? _monitor;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--detail-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new DetailShots { Name = "DetailShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--detail-shots=".Length..] };
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

        if (_frame < 14)
        {
            return;
        }

        var hs = _hospital;
        var sim = hs.Session.Simulation;
        var view3D = GameManager.Instance.Settings.View3D;
        var hud = hs.Hud;
        switch (_step)
        {
            case 0:
                GetViewport().WarpMouse(new Vector2(2, 2));
                Run(hs, () => sim.Now.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday && sim.Now.Hour >= 11 && sim.Patients.Count(p => p.Stage == PatientStage.OnWard) >= 6 && sim.Journal.Count >= 6, TimeSpan.FromDays(4));
                _ward = sim.Wards.OrderByDescending(w => w.Beds.Count(b => b.OccupantId is not null)).First();
                Look(hs, _ward, view3D ? 1.3 : 0.9);
                hs.SetMode(GameMode.Play);
                hud.ShowRoom(_ward, people: true);
                break;
            case 1:
                Save("1_ward_patients");
                hud.RoomInfo.ShowTab(RoomPanel.RoomTab.Staff);
                break;
            case 2:
                Save("2_ward_staff");
                hud.RoomInfo.ShowTab(RoomPanel.RoomTab.Today);
                break;
            case 3:
                Save("3_ward_today");
                hud.HideRightPanels();

                // Monitors are listed for the floor on view: find the floor with a patient on one.
                for (var floor = 0; floor < sim.FloorCount; floor++)
                {
                    hs.ShowFloor(floor);
                    if (hs.World.Monitors.Any(m => hs.World.ConnectedPatient(m) is not null))
                    {
                        break;
                    }
                }

                _monitor = hs.World.Monitors.Where(m => hs.World.ConnectedPatient(m) is not null).OrderByDescending(m => hs.World.ConnectedPatient(m)!.LiveNews2).FirstOrDefault();
                GD.Print($"[details] {hs.World.Monitors.Count} monitors, {hs.World.Monitors.Count(m => hs.World.ConnectedPatient(m) is not null)} with a patient");
                if (_monitor is not null)
                {
                    hs.ShowFloor(sim.Site.FloorOf(new GridCoord(_monitor.Space.Rect.X, _monitor.Space.Rect.Y)));
                    hs.Camera.SnapTo(IsoDraw.ToScreen(_monitor.X, _monitor.Y), view3D ? 2.2f : 1.8f);
                }

                break;
            case 4:
                if (_monitor is not null)
                {
                    // Hover the monitor for its tag, then open it.
                    var world = view3D ? IsoDraw.ToScreen(_monitor.X, _monitor.Y) + new Vector2(0, -Figures.MonitorHeight - 20) : IsoDraw.ToScreen(_monitor.X, _monitor.Y);
                    GetViewport().WarpMouse(GetViewport().GetCanvasTransform() * world);
                }

                break;
            case 5:
                Save("4_monitor_hover");
                if (_monitor is not null)
                {
                    hs.OpenMonitor(_monitor);
                }

                break;
            case 6:
                Save("5_big_monitor");
                hud.Monitor.Close();
                GetViewport().WarpMouse(new Vector2(2, 2));
                var outpatient = sim.Outpatients.FirstOrDefault(o => o.Stage == OutpatientStage.BeingSeen) ?? sim.Outpatients.FirstOrDefault(o => o.Stage == OutpatientStage.Waiting) ?? sim.Outpatients.FirstOrDefault();
                if (outpatient is not null)
                {
                    hs.SelectOutpatient(outpatient, centre: true);
                }

                break;
            case 7:
                Save("6_clinic_patient_card");
                var clinic = sim.ClinicRooms.OrderByDescending(c => sim.CalendarFor(c.Id, sim.Now).Count()).FirstOrDefault();
                if (clinic is not null)
                {
                    hs.ClearSelectionForTest();
                    hud.ShowRoom(clinic, people: true);
                }

                break;
            case 8:
                Save("7_clinic_list");
                hud.HideRightPanels();
                hud.Dashboard.ShowBoard(DepartmentDashboard.Board.Inpatients);
                break;
            case 9:
                Save("8_board_inpatients");
                hud.Dashboard.ShowBoard(DepartmentDashboard.Board.Clinics);
                break;
            case 10:
                Save("9_board_clinics");
                hud.Dashboard.ShowBoard(DepartmentDashboard.Board.Emergency);
                hud.ToggleJournal();
                break;
            case 11:
                Save("10_journal");
                hud.Journal.SetOrder(JournalPanel.Order.MostTreated);
                break;
            case 12:
                Save("11_journal_most_treated");
                GD.Print("[details] Done.");
                GetTree().Quit(0);
                return;
        }

        _step++;
        _frame = 0;
    }

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

        GD.Print($"[details] {sim.Now:ddd HH:mm}: {(until() ? "reached" : "gave up")} · {sim.Patients.Count(p => p.Stage == PatientStage.OnWard)} on wards, {sim.Journal.Count} illnesses in the journal, {sim.Outpatients.Count} clinic patients");
    }

    private void Save(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(_directory);
        var path = $"{_directory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[details] Saved {path}");
    }
}
