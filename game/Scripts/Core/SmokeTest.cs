using System;
using System.Collections.Generic;
using BlueprintHospital.Hospital;
using BlueprintHospital.UI;
using System.Linq;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Automated launch check, run with: godot --headless --path game -- --smoke-test
/// Starts a new game, checks patients arrive, are triaged and treated, opens a patient record,
/// lets the clock run, saves, reloads and compares, then exits
/// with code 0 on success or 1 on failure. Used to verify each build actually runs.
/// </summary>
public partial class SmokeTest : Node
{
    private const string SlotName = "smoke_test";
    private int _frame;
    private Stage _stage = Stage.WaitForMenu;
    private DateTime _savedTime;
    private CameraState? _savedCamera;
    private CameraState? _overview;
    private AmbulanceVehicle? _ambulance;
    private string? _screenshotDirectory;
    private int _savedRooms;
    private long _savedBalance;
    private int _savedPatients;
    private int _savedBabies;
    private Simulation.Patients.Patient? _mother;
    private int _savedOnWard;
    private int _savedAppointments;
    private int _savedChairs;
    private int _savedFloors;
    private Simulation.Staff.StaffMember? _climber;

    private enum Stage
    {
        WaitForMenu,
        NewGameChoice,
        EmptyBuilding,
        WaitForHospital,
        RunSimulation,
        Notifications,
        CloseUp,
        WaitingRoom,
        WaitingRoomTurned,
        PatientRecord,
        StaffPanel,
        BuildPreview,
        RoomPanel,
        BuildItem,
        Departments,
        BuildBarMaternity,
        Recruitment,
        Rota,
        RotaBreaks,
        Stats,
        StatsWeek,
        Ward,
        PlanWard,
        PlanEmergency,
        PlanOverview,
        World3D,
        World3DTurned,
        WardBed,
        CrashCall,
        CrashPanelOpen,
        WardFall,
        Transport,
        Imaging,
        Theatre,
        Clinics,
        ReceptionistPanel,
        ClinicChair,
        ClinicCalendar,
        Specialty,
        Cafe,
        Vending,
        Toilets,
        ToiletsRotated,
        Pharmacy,
        MedicineDelivery,
        CriticalCare,
        UpperFloor,
        UpperFloorWalk,
        Maternity,
        MaternityRecord,
        Rotate,
        Ambulance,
        Finances,
        WaitForLoad,
        Done,
    }

    public static void Begin(Node menu)
    {
        var manager = GameManager.Instance;
        if (manager.GetNodeOrNull("SmokeTest") is not null)
        {
            return;
        }

        var test = new SmokeTest { Name = "SmokeTest", ProcessMode = ProcessModeEnum.Always };
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshots=", StringComparison.Ordinal))
            {
                test._screenshotDirectory = arg["--screenshots=".Length..];
            }
        }

        manager.AddChild(test);
        GD.Print("[smoke] Main menu loaded.");
    }

    public override void _Process(double delta)
    {
        _frame++;
        try
        {
            Step();
        }
        catch (Exception ex)
        {
            Fail(ex.ToString());
        }

        if (_frame > 2000)
        {
            Fail($"Timed out in stage {_stage}.");
        }
    }

    private void Step()
    {
        var manager = GameManager.Instance;
        var scene = GetTree().CurrentScene;

        switch (_stage)
        {
            case Stage.WaitForMenu when _frame >= 10:
                Screenshot("01_main_menu");
                foreach (var panel in new UI.ModalPanel[] { new UI.SettingsPanel(), new UI.LoadGamePanel() })
                {
                    scene.AddChild(panel);
                    panel.Close();
                }

                GD.Print("[smoke] Settings and Load Game panels open and close.");
                Check(manager.Database is not null, "Game data failed to load: " + manager.DataLoadError);
                ((UI.MainMenu)scene).ShowNewGame();
                _frame = 0;
                _stage = Stage.NewGameChoice;
                break;

            case Stage.NewGameChoice when _frame >= 5:
                Screenshot("01b_new_game");
                manager.StartNewGame("Smoke Test Empty", Simulation.Simulation.StartingHospital.EmptyBuilding);
                _frame = 0;
                _stage = Stage.EmptyBuilding;
                break;

            case Stage.EmptyBuilding when scene is HospitalScene empty && empty.IsNodeReady() && _frame >= 15:
                Screenshot("01c_empty_building");
                Check(!empty.Session.Simulation.Spaces.Any(), "The empty building should have no rooms, not even a waiting room.");
                Check(!empty.Session.Simulation.Staff.Any(), "The empty building should have no staff.");
                Check(!empty.Session.Simulation.IsEmergencyOpen, "A&E should be closed in an empty building.");
                GD.Print($"[smoke] Empty building: no rooms, no staff, £{empty.Session.Simulation.Balance:N0}, closed until it has {string.Join(", ", empty.Session.Simulation.ClosedReasons)}.");
                manager.StartNewGame("Smoke Test General");
                _stage = Stage.WaitForHospital;
                break;

            case Stage.WaitForHospital when scene is HospitalScene hospital && hospital.IsNodeReady():
                Check(hospital.Session.Site.Buildings.Count == 1, "Starter building missing.");
                Check(hospital.Camera.IsCurrent(), "Camera is not active.");
                hospital.Session.Clock.SetSpeed(SimulationSpeed.VeryFast);
                GD.Print("[smoke] Hospital scene loaded.");
                FitOutWaitingRoom(hospital);
                CheckPatientJourney(hospital);
                _frame = 0;
                _stage = Stage.RunSimulation;
                break;

            case Stage.RunSimulation when scene is HospitalScene hospital && _frame >= 30:
                Screenshot("02_emergency_department");
                Check(hospital.Hud.Feed.GetGlobalRect().End.Y <= UI.MainHUD.TopBarHeight, "The notifications box is not in the top bar.");
                hospital.Hud.Feed.PinnedOpen = true;
                _frame = 0;
                _stage = Stage.Notifications;
                break;

            case Stage.Notifications when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("02b_notifications");
                Check(hospital.Hud.Feed.Dropdown.Visible, "Hovering the notifications box did not show the recent events.");
                Check(hospital.Hud.Feed.RowsShown == Math.Min(UI.NotificationFeed.Shown, hospital.Session.Simulation.Notifications.Count), $"The notifications list shows {hospital.Hud.Feed.RowsShown} events, not up to {UI.NotificationFeed.Shown}.");
                hospital.Hud.Feed.PinnedOpen = false;
                GD.Print($"[smoke] Notifications: latest in the top bar, {hospital.Hud.Feed.RowsShown} on hover.");
                _overview = hospital.CaptureCamera();
                // Close up on a bed with a patient in it, if there is one.
                var sim = hospital.Session.Simulation;
                var bay = sim.Spaces.Where(sp => sp.IsTreatmentSpace).OrderBy(sp => sp.IsFree).ThenBy(sp => sp.Id).First().Rect;
                hospital.Camera.SnapTo(IsoDraw.ToScreen(bay.X + 5, bay.Y + 6), 1.5f);
                _frame = 0;
                _stage = Stage.CloseUp;
                break;

            case Stage.CloseUp when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("03_close_up");
                LookAtWaitingRoom(hospital);
                _frame = 0;
                _stage = Stage.WaitingRoom;
                break;

            case Stage.WaitingRoom when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("03a_waiting_room");
                hospital.RotateView(1);
                LookAtWaitingRoom(hospital);
                _frame = 0;
                _stage = Stage.WaitingRoomTurned;
                break;

            case Stage.WaitingRoomTurned when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("03b_waiting_room_turned");
                hospital.RotateView(-1);
                hospital.Camera.SnapTo(new Vector2((float)_overview!.X, (float)_overview.Y), (float)_overview.Zoom);
                OpenARecord(hospital);
                _frame = 0;
                _stage = Stage.PatientRecord;
                break;

            case Stage.PatientRecord when scene is HospitalScene hospital && _frame >= 10:
                Check(hospital.Session.Clock.TickCount > 0, "Simulation clock did not advance.");
                Screenshot("04_patient_record");
                var member = hospital.Session.Simulation.Staff.OrderBy(m => m.CurrentTaskId is null ? 1 : 0).First();
                hospital.SelectStaff(member, centre: true);
                Check(hospital.Hud.StaffInfo.Visible && !hospital.Hud.Record.Visible, "Clicking a member of staff did not open their panel.");
                _frame = 0;
                _stage = Stage.StaffPanel;
                break;

            case Stage.StaffPanel when scene is HospitalScene hospital && _frame >= 20:
                Screenshot("04b_staff_panel");
                hospital.ClearSelectionForTest();
                Check(!hospital.Hud.StaffInfo.Visible, "Staff panel did not close.");
                StartBuilding(hospital);
                _frame = 0;
                _stage = Stage.BuildPreview;
                break;

            case Stage.BuildPreview when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("05_build_preview");
                var built = hospital.Session.Simulation.Spaces.Count();
                hospital.BuildTool.Confirm();
                Check(hospital.Session.Simulation.Spaces.Count() == built + 1, "The previewed cubicle was not built.");
                hospital.BuildTool.Cancel();
                var newRoom = hospital.Session.Simulation.Spaces.Where(s => s.TemplateId == "majors_cubicle").OrderBy(s => s.Rect.X).First();
                ClickRoomFloor(hospital, newRoom);
                GD.Print($"[smoke] Built {newRoom.Name}; balance £{hospital.Session.Simulation.Balance:N0}.");
                _frame = 0;
                _stage = Stage.RoomPanel;
                break;

            case Stage.RoomPanel when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("06_room_panel");
                ClickBuildItem(hospital);
                _frame = 0;
                _stage = Stage.BuildItem;
                break;

            case Stage.BuildItem when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("06a_build_item");
                hospital.Hud.HideRightPanels();
                hospital.SetMode(GameMode.Build);
                CheckBuildBar(hospital);
                hospital.Hud.Build.Open("emergency");
                _frame = 0;
                _stage = Stage.Departments;
                break;

            case Stage.Departments when scene is HospitalScene hospital && _frame == 5:
                CheckBuildPopup(hospital);
                hospital.Hud.Build.ShowHover("minors_cubicle");
                break;

            case Stage.Departments when scene is HospitalScene hospital && _frame == 8:
            {
                Screenshot("06c_build_bar_ae");

                // Waiting chairs: the colour row shows under the tiles, and a dragged row previews as ghost chairs.
                hospital.Hud.Build.Choose(Simulation.Data.BuildMenu.WaitingChairs);
                Check(hospital.BuildTool.Mode == BuildMode.Chair, "Picking waiting chairs did not start the chair tool.");
                var room = hospital.Session.Simulation.MainWaitingRoom!.Rect;
                hospital.Camera.SnapTo(IsoDraw.ToScreen(room.X + 10, room.Bottom - 6), 1.1f);
                hospital.BuildTool.ChairColour = Simulation.Hospital.ChairColours.All[3].Hex;
                hospital.BuildTool.FaceChairs(Simulation.World.WallSide.North);
                hospital.BuildTool.PreviewDrag(new Simulation.World.GridCoord(room.X + 2, room.Bottom - 2), new Simulation.World.GridCoord(room.X + 9, room.Bottom - 2));
                break;
            }

            case Stage.Departments when scene is HospitalScene hospital && _frame == 10:
                Screenshot("06c2_build_bar_chairs");
                Check(hospital.BuildTool.Check is { CanBuild: true }, "The chair row preview should be placeable: " + string.Join("; ", hospital.BuildTool.Check?.Problems ?? Array.Empty<string>()));
                hospital.BuildTool.Cancel();
                hospital.BuildTool.FollowMouse = true;
                hospital.Hud.Build.Open("cardiology");
                break;

            case Stage.Departments when scene is HospitalScene hospital && _frame == 12:
                CheckBuildPopup(hospital);
                Screenshot("06d_build_bar_locked");
                hospital.Hud.Build.Open("building");
                break;

            case Stage.Departments when scene is HospitalScene hospital && _frame == 15:
                hospital.Hud.Build.ShowHover("lift");
                break;

            case Stage.Departments when scene is HospitalScene hospital && _frame >= 18:
                CheckBuildPopup(hospital);
                Screenshot("06e_build_bar_building");
                hospital.Hud.Build.Open("maternity");
                _frame = 0;
                _stage = Stage.BuildBarMaternity;
                break;

            case Stage.BuildBarMaternity when scene is HospitalScene hospital && _frame == 3:
                hospital.Hud.Build.ShowHover("delivery_room");
                break;

            case Stage.BuildBarMaternity when scene is HospitalScene hospital && _frame == 6:
                CheckBuildPopup(hospital);
                Screenshot("06f_build_bar_maternity");
                hospital.Hud.Build.Open("facilities");
                break;

            case Stage.BuildBarMaternity when scene is HospitalScene hospital && _frame == 9:
                CheckBuildPopup(hospital);
                Screenshot("06g_build_bar_facilities");
                Check(hospital.Hud.Build.PopupOpen, "The Facilities pop-up did not open.");
                StartDecorating(hospital);
                break;

            case Stage.BuildBarMaternity when scene is HospitalScene hospital && _frame == 12:
                Screenshot("06h_decorate_panel");
                FinishDecorating(hospital);
                break;

            case Stage.BuildBarMaternity when scene is HospitalScene hospital && _frame >= 15:
                Screenshot("06i_decorated");
                hospital.SetMode(GameMode.Play);
                Check(!hospital.Hud.Decorate.Visible, "Leaving Build mode should close the Decorate panel.");
                Check(!hospital.Hud.Build.PopupOpen, "Leaving Build mode should close the build pop-up.");
                Check(!hospital.Hud.Build.Visible && !hospital.Hud.ItemInfo.Visible, "Play mode should close the build menu and item panel.");
                BuildWard(hospital);
                hospital.Hud.ToggleRecruitment();
                Check(hospital.Hud.Recruitment.Visible, "Recruitment did not open.");
                _frame = 0;
                _stage = Stage.Recruitment;
                break;

            case Stage.Recruitment when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("06b_recruitment");
                var recruitment = hospital.Hud.Recruitment;
                Check(recruitment.GetCombinedMinimumSize().X <= recruitment.Size.X + 1
                      && recruitment.GetGlobalRect().End.X <= recruitment.GetViewportRect().Size.X + 1,
                    $"Recruitment is wider than its dock ({recruitment.GetCombinedMinimumSize().X:0} > {recruitment.Size.X:0}).");
                hospital.Hud.ToggleRecruitment();
                StaffWardAndAdmit(hospital);
                OpenRota(hospital);
                _frame = 0;
                _stage = Stage.Rota;
                break;

            case Stage.Rota when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("06d_rota");
                hospital.Hud.Rota.ShowBreaksForTest();
                _frame = 0;
                _stage = Stage.RotaBreaks;
                break;

            case Stage.RotaBreaks when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("06e_rota_breaks");
                hospital.Hud.ToggleRota();
                Check(!hospital.Hud.Rota.Visible, "The rota did not close.");
                hospital.Hud.ToggleStats();
                Check(hospital.Hud.StatsScreen.Visible, "The Stats screen did not open.");
                hospital.Hud.StatsScreen.ShowPeriod(BlueprintHospital.Simulation.Simulation.StatsPeriod.Last24Hours);
                _frame = 0;
                _stage = Stage.Stats;
                break;

            case Stage.Stats when scene is HospitalScene hospital && _frame >= 10:
            {
                Screenshot("06f_stats");
                var stats = hospital.Hud.StatsScreen;
                Check(stats.GetCombinedMinimumSize().X <= stats.Size.X + 1, $"The Stats screen is wider than the screen ({stats.GetCombinedMinimumSize().X:0} > {stats.Size.X:0}).");
                var statsSim = hospital.Session.Simulation;
                var arrivals = statsSim.History.Total(BlueprintHospital.Simulation.Simulation.StatsPeriod.AllTime, statsSim.Now, BlueprintHospital.Simulation.Simulation.StatKeys.Arrivals);
                Check(arrivals == statsSim.Stats.ArrivalsTotal, $"Stats all-time arrivals {arrivals} don't match A&E's {statsSim.Stats.ArrivalsTotal}.");
                GD.Print($"[smoke] Stats: {arrivals} arrivals all time.");
                stats.ShowPeriod(BlueprintHospital.Simulation.Simulation.StatsPeriod.Last7Days);
                _frame = 0;
                _stage = Stage.StatsWeek;
                break;
            }

            case Stage.StatsWeek when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("06g_stats_week");
                hospital.Hud.ToggleStats();
                Check(!hospital.Hud.StatsScreen.Visible, "The Stats screen did not close.");
                _frame = 0;
                _stage = Stage.Ward;
                break;

            case Stage.Ward when scene is HospitalScene hospital && _frame >= 90:
                Screenshot("07_ward");
                hospital.TogglePlanView();
                Check(hospital.PlanView && !hospital.World.Visible, "V did not switch to the 2D plan.");
                _frame = 0;
                _stage = Stage.PlanWard;
                break;

            case Stage.PlanWard when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("07p_plan_ward");
                CheckPlanClick(hospital);
                {
                    var cubicle = hospital.Session.Simulation.Spaces.Where(sp => sp.IsTreatmentSpace).OrderBy(sp => sp.Id).First().Rect;
                    hospital.Camera.SnapTo(IsoDraw.ToScreen(cubicle.X + 4, cubicle.Y + 6), 0.9f);
                }

                _frame = 0;
                _stage = Stage.PlanEmergency;
                break;

            case Stage.PlanEmergency when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("07q_plan_emergency");
                hospital.Camera.SnapTo(IsoDraw.ToScreen(Simulation.World.StarterSite.MainBuildingFootprint.X + 22, Simulation.World.StarterSite.MainBuildingFootprint.Y + 16), 0.42f);
                _frame = 0;
                _stage = Stage.PlanOverview;
                break;

            case Stage.PlanOverview when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("07r_plan_overview");
                hospital.TogglePlanView();
                Check(!hospital.PlanView && hospital.World.Visible && !IsoDraw.Plan, "V did not switch back to the 3D view.");
                GD.Print("[smoke] 2D plan view: on, click picks a circle, back to 3D.");
                hospital.Set3D(true);
                _frame = 0;
                _stage = Stage.World3D;
                break;

            case Stage.World3D when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("07s_world3d");
                Check(hospital.View3D.Visible, "The 3D world did not show.");
                Check(hospital.View3D.Root.GetNode("Walls").GetChildCount() > 0, "The 3D world has no walls.");
                Check(hospital.View3D.People.Count > 0, "Nobody is standing in the 3D world.");
                Check(hospital.View3D.Beds.Count > 0, "The 3D world has no beds.");
                Check(hospital.View3D.Furniture.Count > 0, "The 3D world has no furniture.");
                hospital.RotateView(1);
                hospital.ToggleWalls();
                _frame = 0;
                _stage = Stage.World3DTurned;
                break;

            case Stage.World3DTurned when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("07t_world3d_turned_cutaway");
                Check(hospital.View3D.Cutaway, "X did not cut the walls down.");
                hospital.ToggleWalls();
                hospital.RotateView(-1);
                hospital.Set3D(false);
                GD.Print("[smoke] 3D world: walls and floors built, turned, walls cut down and back.");
                {
                    var ward = hospital.Session.Simulation.Wards.Single().Rect;
                    hospital.Camera.SnapTo(IsoDraw.ToScreen(ward.X + (ward.Width / 2.0), ward.Y + (ward.Length / 2.0)), 0.9f);
                }

                ClickWardBed(hospital);
                _frame = 0;
                _stage = Stage.WardBed;
                break;

            case Stage.WardBed when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("07b_ward_bed");
                hospital.Hud.HideRightPanels();
                StartCrashCall(hospital);
                _frame = 0;
                _stage = Stage.CrashCall;
                break;

            case Stage.CrashCall when scene is HospitalScene hospital && _frame == 5:
                if (hospital.Session.Simulation.Arrests.FirstOrDefault() is { } shown && hospital.Session.Simulation.FindPatient(shown.PatientId) is { } arrested)
                {
                    hospital.Camera.SnapTo(IsoDraw.ToScreen(arrested.X, arrested.Y), 2.2f);
                }

                break;

            case Stage.CrashCall when scene is HospitalScene hospital && _frame >= 10:
                // The banner only refreshes every 0.2 real seconds, and headless frames can be quicker than that.
                hospital.Hud.Crash.Refresh();
                Check(hospital.Hud.Crash.Visible, "The crash call banner did not show.");
                Screenshot("07c_crash_call");
                hospital.Hud.Crash.Expanded = true;
                _frame = 0;
                _stage = Stage.CrashPanelOpen;
                break;

            case Stage.CrashPanelOpen when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("07d_crash_team");
                hospital.Hud.Crash.Expanded = false;
                FinishCrashCall(hospital);
                StartWardFall(hospital);
                _frame = 0;
                _stage = Stage.WardFall;
                break;

            case Stage.WardFall when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("07e_ward_fall");
                FinishWardFall(hospital);
                hospital.Session.Clock.SetSpeed(SimulationSpeed.VeryFast);
                BuildImagingAndSendForScan(hospital);
                _frame = 0;
                _stage = Stage.Transport;
                break;

            case Stage.Transport when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("09a_porter_trolley");
                FinishScan(hospital);
                _frame = 0;
                _stage = Stage.Imaging;
                break;

            case Stage.Imaging when scene is HospitalScene hospital && _frame >= 20:
                Screenshot("09_imaging");
                BuildTheatre(hospital);
                _frame = 0;
                _stage = Stage.Theatre;
                break;

            case Stage.Theatre when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("09b_theatre_access");
                BuildClinics(hospital);
                _frame = 0;
                _stage = Stage.Clinics;
                break;

            case Stage.Clinics when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09c_clinic_waiting");
                hospital.SetMode(GameMode.Play);
                hospital.SelectStaff(hospital.Session.Simulation.Staff.First(m => m.Role == Simulation.Staff.StaffRole.Receptionist), centre: true);
                _frame = 0;
                _stage = Stage.ReceptionistPanel;
                break;

            case Stage.ReceptionistPanel when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("09c2_receptionist_panel");
                CheckStaffPanelsFit(hospital);
                hospital.ClearSelectionForTest();
                hospital.SetMode(GameMode.Build);
                ClickCorridorChair(hospital);
                _frame = 0;
                _stage = Stage.ClinicChair;
                break;

            case Stage.ClinicChair when scene is HospitalScene hospital && _frame >= 4:
                Screenshot("09d_corridor_chair");
                hospital.SetMode(GameMode.Play);
                ClickOutpatient(hospital);
                _frame = 0;
                _stage = Stage.ClinicCalendar;
                break;

            case Stage.ClinicCalendar when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09e_clinic_calendar");
                BuildSpecialtyRooms(hospital);
                _frame = 0;
                _stage = Stage.Specialty;
                break;

            case Stage.Specialty when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09f_specialty_rooms");
                BuildRefreshments(hospital);
                _frame = 0;
                _stage = Stage.Cafe;
                break;

            case Stage.Cafe when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09g_cafeteria");

                // Turned round, to see the vending machines' glass fronts and the food counter.
                hospital.RotateView(2);
                var vendingRoom = hospital.Session.Simulation.SpacesOfKind(Simulation.Hospital.CareSpaceKind.VendingPoint).First();
                hospital.Camera.SnapTo(IsoDraw.ToScreen(vendingRoom.Rect.X - 4, vendingRoom.Rect.Y + 6) + new Vector2(-180, 0), 1.2f);
                _frame = 0;
                _stage = Stage.Vending;
                break;

            case Stage.Vending when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09h_vending_point");
                hospital.RotateView(2);
                BuildToilets(hospital);
                _frame = 0;
                _stage = Stage.Toilets;
                break;

            case Stage.Toilets when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09i_toilets");

                // The same toilets from the other side.
                hospital.RotateView(2);
                hospital.Camera.SnapTo(ToiletsCentre(hospital) + new Vector2(120, 0), 1.15f);
                _frame = 0;
                _stage = Stage.ToiletsRotated;
                break;

            case Stage.ToiletsRotated when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09j_toilets_rotated");
                hospital.RotateView(2);
                BuildPharmacy(hospital);
                _frame = 0;
                _stage = Stage.Pharmacy;
                break;

            case Stage.Pharmacy when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09m_pharmacy_collection");
                FollowMedicineDelivery(hospital);
                _frame = 0;
                _stage = Stage.MedicineDelivery;
                break;

            case Stage.MedicineDelivery when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09n_porter_with_medicines");
                BuildIntensiveCare(hospital);
                _frame = 0;
                _stage = Stage.CriticalCare;
                break;

            case Stage.CriticalCare when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09i_intensive_care");
                _climber = BuildUpperFloor(hospital);
                _frame = 0;
                _stage = Stage.UpperFloor;
                break;

            case Stage.UpperFloor when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09j_lift_and_stairs");
                hospital.ShowFloor(1);
                Check(IsoDraw.ViewFloor == 1, "Page Up did not show the first floor.");
                var upstairsLift = hospital.Session.Simulation.Spaces.First(sp => sp.Kind == Simulation.Hospital.CareSpaceKind.Lift && hospital.Session.Simulation.FloorOf(sp) == 1);
                hospital.Camera.SnapTo(IsoDraw.ToScreen(upstairsLift.Rect.X - 6, upstairsLift.Rect.Y + 2), 0.6f);
                _frame = 0;
                _stage = Stage.UpperFloorWalk;
                break;

            case Stage.UpperFloorWalk when scene is HospitalScene hospital && _frame >= 6:
                Screenshot("09k_first_floor");
                var floorSim = hospital.Session.Simulation;
                GD.Print($"[smoke] Viewed the first floor; {floorSim.Staff.Count(m => m.IsInBuilding && floorSim.FloorOf(m) == 1)} staff up there.");
                hospital.ShowFloor(0);
                Check(IsoDraw.ViewFloor == 0, "Page Down did not go back to the ground floor.");
                _mother = BuildMaternity(hospital);
                _frame = 0;
                _stage = Stage.Maternity;
                break;

            case Stage.Maternity when scene is HospitalScene hospital && _frame >= 8:
                Screenshot("09l_maternity");
                CheckStaffPanelsFit(hospital);
                hospital.Hud.HideRightPanels();
                if (_mother is not null)
                {
                    hospital.SelectPatient(_mother, centre: false);
                }

                _frame = 0;
                _stage = Stage.MaternityRecord;
                break;

            case Stage.MaternityRecord when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("09m_mother_and_baby_record");
                hospital.SetMode(GameMode.Play);
                hospital.Hud.Record.Visible = false;
                hospital.ClearSelectionForTest();
                hospital.ToggleRoutes();
                Check(hospital.RoutesShown, "Routes did not turn on.");
                for (var i = 0; i < 3; i++)
                {
                    hospital.Session.Simulation.SpawnPatient(ArrivalMethod.WalkIn);
                }

                // Pause with people mid-walk so their routes show in the screenshots.
                for (var i = 0; i < 4; i++)
                {
                    hospital.Session.Clock.Step();
                }

                hospital.Session.Clock.SetSpeed(SimulationSpeed.Paused);

                _frame = 0;
                _stage = Stage.Rotate;
                break;

            case Stage.Rotate when scene is HospitalScene hospital && _frame == 2:
                // Look at the busiest walking: whoever has the longest way to go.
                var walker = hospital.Session.Simulation.Patients.Cast<Simulation.Agents.Agent>().Concat(hospital.Session.Simulation.Staff)
                    .Where(a => a.IsMoving).OrderByDescending(a => a.RemainingPath.Count()).FirstOrDefault();
                GD.Print($"[smoke] Route shots centred on: {walker?.GetType().Name ?? "none"} with {walker?.RemainingPath.Count() ?? 0} tiles to go.");
                if (walker is not null)
                {
                    hospital.Camera.SnapTo(hospital.World.FootPosition(walker), 0.8f);
                }

                break;

            case Stage.Rotate when scene is HospitalScene hospital && _frame >= 8:
                Screenshot($"10_view_{IsoDraw.Rotation}");
                hospital.RotateView(1);
                _frame = 0;
                if (IsoDraw.Rotation != 0)
                {
                    break;
                }

                hospital.ToggleRoutes();
                GD.Print("[smoke] Rotated the view through all four angles with routes shown.");
                StartAmbulanceShots(hospital);
                _frame = 0;
                _stage = Stage.Ambulance;
                break;

            case Stage.Ambulance when scene is HospitalScene hospital && _frame >= 6:
                if (_ambulance is not null)
                {
                    Screenshot($"11_ambulance_{IsoDraw.Rotation}");
                    hospital.RotateView(1);
                    hospital.Camera.SnapTo(hospital.World.FootPosition(_ambulance) + new Vector2(0, -40), 1.2f);
                    _frame = 0;
                    if (IsoDraw.Rotation != 0)
                    {
                        break;
                    }
                }

                CheckCheatConsole(hospital);
                hospital.Hud.ToggleFinances();
                Check(hospital.Hud.Finances.Visible, "Finances did not open.");
                Check(hospital.Session.Simulation.Accounts.Any(d => d[Simulation.Simulation.MoneyCategory.Construction] < 0), "Building work was not booked.");
                _frame = 0;
                _stage = Stage.Finances;
                break;

            case Stage.Finances when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("08_finances");
                PressCtrlC(hospital);
                Check(!hospital.Hud.Cheats.Visible, "Ctrl+C did not close the cheat console.");
                hospital.Hud.ToggleFinances();
                var clock = hospital.Session.Clock;
                GD.Print($"[smoke] Clock advanced {clock.TickCount} ticks to {clock.Now:yyyy-MM-dd HH:mm:ss}.");
                hospital.Session.Clock.SetSpeed(SimulationSpeed.Paused);
                _savedTime = clock.Now;
                _savedCamera = hospital.CaptureCamera();
                _savedRooms = hospital.Session.Simulation.Spaces.Count();
                _savedBalance = hospital.Session.Simulation.Balance;
                _savedPatients = hospital.Session.Simulation.Patients.Count();
                _savedBabies = hospital.Session.Simulation.Babies.Count;
                _savedOnWard = hospital.Session.Simulation.Patients.Count(p => p.Stage == Simulation.Patients.PatientStage.OnWard);
                _savedAppointments = hospital.Session.Simulation.Appointments.Count;
                _savedChairs = hospital.Session.Simulation.Chairs.Count;
                _savedFloors = hospital.Session.Simulation.FloorCount;
                manager.SaveGame(SlotName, _savedCamera);
                GD.Print("[smoke] Game saved.");
                manager.LoadGame(SlotName);
                _stage = Stage.WaitForLoad;
                break;

            case Stage.WaitForLoad when scene is HospitalScene hospital && hospital.IsNodeReady() && hospital.Session.Clock.Now == _savedTime:
                Check(hospital.Session.HospitalName == "Smoke Test General", "Hospital name not restored.");
                Check(hospital.CaptureCamera() == _savedCamera, "Camera not restored.");
                Check(hospital.Session.Simulation.Spaces.Count() == _savedRooms, "Built rooms not restored.");
                Check(hospital.Session.Simulation.Balance == _savedBalance, "Balance not restored.");
                Check(hospital.Session.Simulation.Accounts.Take(2).Sum(d => d[Simulation.Simulation.MoneyCategory.Cheats]) == CheatMoney, "The cheat money line was not restored.");
                Check(hospital.Session.Simulation.Patients.Count() == _savedPatients, $"Patients not restored ({hospital.Session.Simulation.Patients.Count()} of {_savedPatients}).");
                Check(hospital.Session.Simulation.Patients.Count(p => p.Stage == Simulation.Patients.PatientStage.OnWard) == _savedOnWard, "Ward patients not restored.");
                Check(hospital.Session.Simulation.Babies.Count == _savedBabies && _savedBabies > 0, $"Babies not restored ({hospital.Session.Simulation.Babies.Count} of {_savedBabies}).");
                Check(hospital.Session.Simulation.Appointments.Count == _savedAppointments, "Clinic appointments not restored.");
                Check(hospital.Session.Simulation.Chairs.Count == _savedChairs, "Waiting chairs not restored.");
                Check(hospital.Session.Simulation.FloorCount == _savedFloors && _savedFloors > 1, $"Floors not restored ({hospital.Session.Simulation.FloorCount} of {_savedFloors}).");
                Check(hospital.Session.Simulation.Nav.Links.Any(), "Lifts and stairs not linked after loading.");
                GD.Print($"[smoke] {_savedPatients} patients restored, {_savedOnWard} on the ward.");
                SaveManager.Delete(SlotName);
                GD.Print("[smoke] Save reloaded and verified.");
                GD.Print("[smoke] PASSED");
                _stage = Stage.Done;
                GetTree().Quit(0);
                break;
        }
    }

    /// <summary>
    /// Brings in a walk-in and an ambulance patient, runs the simulation for game time and checks
    /// both are triaged, then opens a record. Background arrivals make the department busy for the screenshot.
    /// </summary>
    private const long CheatMoney = 123_456;

    /// <summary>Ctrl+C opens the cheat console; "money" adds to the balance under a Cheats line.</summary>
    /// <summary>
    /// Every kind of staff member's panel must fit inside its dock and the screen: a long role, place or
    /// footer line used to push the receptionist's panel off the right-hand edge.
    /// </summary>
    private static void CheckStaffPanelsFit(HospitalScene hospital)
    {
        var panel = hospital.Hud.StaffInfo;
        foreach (var member in hospital.Session.Simulation.Staff.GroupBy(m => m.Role).Select(g => g.First()))
        {
            hospital.Hud.ShowStaff(member);
            var width = panel.GetCombinedMinimumSize().X;
            GD.Print($"[smoke] Staff panel for {member.Role}: needs {width:0}, has {panel.Size.X:0}");
            Check(width <= panel.Size.X + 1 && panel.GetGlobalRect().End.X <= panel.GetViewportRect().Size.X + 1,
                $"The {member.Role} staff panel is wider than its dock ({width:0} > {panel.Size.X:0}).");
        }
    }

    private static void CheckCheatConsole(HospitalScene hospital)
    {
        PressCtrlC(hospital);
        Check(hospital.Hud.Cheats.Visible, "Ctrl+C did not open the cheat console.");
        Check(!hospital.Hud.Clinics.Visible, "Ctrl+C should not open the clinics (C).");
        var sim = hospital.Session.Simulation;
        var before = sim.Balance;
        var result = hospital.Hud.Cheats.Run($"money {CheatMoney}");
        Check(result.Succeeded && sim.Balance == before + CheatMoney, "The money cheat did not add to the balance: " + result.Message);
        Check(sim.Today[Simulation.Simulation.MoneyCategory.Cheats] == CheatMoney, "The money cheat was not booked as a Cheats line.");
        Check(!hospital.Hud.Cheats.Run("money lots").Succeeded && sim.Balance == before + CheatMoney, "A bad money command should change nothing.");
        hospital.Hud.Cheats.Run($"money {CheatMoney}");
        hospital.Hud.Cheats.Run($"money -{CheatMoney}");
        Check(sim.Balance == before + CheatMoney, "Taking money away with the cheat did not work.");
    }

    private static void PressCtrlC(HospitalScene hospital)
    {
        var press = new InputEventKey { Keycode = Key.C, PhysicalKeycode = Key.C, CtrlPressed = true, Pressed = true };
        hospital.GetViewport().PushInput(press);
        var release = new InputEventKey { Keycode = Key.C, PhysicalKeycode = Key.C, CtrlPressed = true, Pressed = false };
        hospital.GetViewport().PushInput(release);
    }

    private static void CheckPatientJourney(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var clock = hospital.Session.Clock;
        var walkIn = sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        var ambulance = sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_myocardial_infarction");

        for (var i = 0; i < 3 * 3600 && (walkIn.Triage is null || ambulance.Triage is null); i++)
        {
            clock.Step();
        }

        Check(walkIn.Triage is not null, "Walk-in patient was not triaged: " + string.Join(" / ", walkIn.Timeline.Select(t => t.Text)));
        Check(ambulance.Triage is not null, "Ambulance patient was not triaged: " + string.Join(" / ", ambulance.Timeline.Select(t => t.Text)));
        GD.Print($"[smoke] Walk-in triaged {walkIn.Triage}, ambulance patient triaged {ambulance.Triage} by {clock.Now:HH:mm}.");

        // Let the department fill up with ordinary arrivals.
        for (var i = 0; i < 2 * 3600; i++)
        {
            clock.Step();
        }

        Check(sim.Stats.ArrivalsTotal >= 2, "No patients arrived.");
        Check(sim.Staff.Sum(s => s.TasksCompleted) > 5, "Staff did not complete any work.");
        GD.Print($"[smoke] {sim.Stats.ArrivalsTotal} arrivals, {sim.Stats.PatientsInDepartment} in department, {sim.Staff.Sum(s => s.TasksCompleted)} staff tasks done by {clock.Now:HH:mm}.");

    }

    /// <summary>Extends the building west, lays a corridor and previews a new Majors cubicle in the build tool.</summary>
    private static void StartBuilding(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        hospital.Hud.Record.Visible = false;

        var extension = sim.ExtendBuilding(new Simulation.World.GridRect(b.X - 30, b.Y, 30, 34));
        Check(extension.CanBuild, "Extension refused: " + string.Join("; ", extension.Problems));
        var corridor = sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(b.X - 30, b.Y + 9, 34, 8), Simulation.World.WallSide.South));
        Check(corridor.CanBuild, "Corridor refused: " + string.Join("; ", corridor.Problems));

        hospital.SetMode(GameMode.Build);
        Check(hospital.Mode == GameMode.Build && hospital.Hud.Build.Visible, "Build mode did not open the build menu.");
        var template = sim.Database.RoomTemplates["majors_cubicle"];
        hospital.BuildTool.StartRoom(template);
        hospital.BuildTool.PreviewDrag(new Simulation.World.GridCoord(b.X - 10, b.Y), new Simulation.World.GridCoord(b.X - 1, b.Y + 8));
        Check(hospital.BuildTool.Check is { CanBuild: true }, "Preview should be buildable: " + string.Join("; ", hospital.BuildTool.Check?.Problems ?? Array.Empty<string>()));

        // Z moves the door to the next wall with a corridor beside it, and the preview stays buildable.
        var walls = sim.DoorSides(template.Id, new Simulation.World.GridRect(b.X - 10, b.Y, 10, 9));
        Check(walls.Count > 0 && hospital.BuildTool.PlannedDoorSide is { } first && walls.Contains(first), "The cubicle's door should be on a corridor wall.");
        hospital.BuildTool.Turn();
        Check(hospital.BuildTool.PlannedDoorSide is { } turned && walls.Contains(turned), "Z moved the door off the corridor.");
        Check(hospital.BuildTool.Check is { CanBuild: true }, "Preview should still be buildable after Z: " + string.Join("; ", hospital.BuildTool.Check?.Problems ?? Array.Empty<string>()));
        hospital.Camera.SnapTo(IsoDraw.ToScreen(b.X - 2, b.Y + 8), 0.9f);
        GD.Print("[smoke] Building extended, corridor laid, cubicle previewed.");
    }

    /// <summary>Unlocks Gynaecology, extends the wing west and builds a theatre, then ticks it for gynaecology and opens its room panel.</summary>
    private static void BuildTheatre(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        sim.AdjustBalance(1_000_000);
        Check(sim.UnlockDepartment("gynaecology").CanBuild, "Could not unlock gynaecology.");
        var wing = sim.ExtendBuilding(new Simulation.World.GridRect(b.X - 32, b.Y - 14, 32, 14));
        Check(wing.CanBuild, "Theatre wing refused: " + string.Join("; ", wing.Problems));
        var corridor = sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(b.X - 20, b.Y - 14, 6, 24), Simulation.World.WallSide.South));
        Check(corridor.CanBuild, "Theatre corridor refused: " + string.Join("; ", corridor.Problems));
        var built = sim.BuildRoom(sim.AutoPlan("operating_theatre", new Simulation.World.GridRect(b.X - 32, b.Y - 14, 12, 12)));
        Check(built.CanBuild, "Theatre refused: " + string.Join("; ", built.Problems));
        var theatre = sim.Spaces.Single(s => s.Kind == Simulation.Hospital.CareSpaceKind.Theatre);
        Check(theatre.AllowedDepartments.Count == 0, "A new theatre should not be open to every department.");
        sim.SetTheatreAccess(theatre.Id, "gynaecology", true);
        hospital.ClearSelectionForTest();
        hospital.SetMode(GameMode.Build);
        hospital.Hud.ShowRoom(theatre);
        hospital.Camera.SnapTo(IsoDraw.ToScreen(theatre.Rect.X + 6, theatre.Rect.Y + 6), 0.9f);
    }

    /// <summary>
    /// Builds an Outpatients reception and a medical clinic east of the theatre corridor, staffs them,
    /// puts waiting chairs in the corridor and runs the clock until clinic patients are waiting and being seen.
    /// </summary>
    private static void BuildClinics(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        hospital.SetMode(GameMode.Build);
        hospital.Hud.HideRightPanels();
        var reception = sim.BuildRoom(sim.AutoPlan("outpatient_reception", new Simulation.World.GridRect(b.X - 14, b.Y - 14, 12, 8)));
        Check(reception.CanBuild, "Outpatients reception refused: " + string.Join("; ", reception.Problems));
        var clinicBuilt = sim.BuildRoom(sim.AutoPlan("medical_clinic", new Simulation.World.GridRect(b.X - 14, b.Y - 6, 8, 6)));
        Check(clinicBuilt.CanBuild, "Medical clinic refused: " + string.Join("; ", clinicBuilt.Problems));
        var desk = sim.Spaces.Single(s => s.Kind == Simulation.Hospital.CareSpaceKind.OutpatientReception);
        var clinic = sim.Spaces.Single(s => s.Kind == Simulation.Hospital.CareSpaceKind.Clinic);
        Check(sim.ClinicProblems(clinic).Count == 2, "A new clinic should need a doctor and a receptionist.");
        Check(sim.PostingsFor(Simulation.Staff.StaffRole.Doctor).Any(p => p.RoomId == clinic.Id), "Doctors can't be posted to the clinic.");
        // Clinics run in the day, so hire their team for days whatever time this step happens to start.
        sim.HireStaff(Simulation.Staff.StaffRole.Receptionist, desk.Id, skill: 0.7, shift: Simulation.Staff.StaffShift.Day);
        sim.HireStaff(Simulation.Staff.StaffRole.Doctor, clinic.Id, skill: 0.7, shift: Simulation.Staff.StaffShift.Day);
        Check(sim.IsClinicRunning(clinic), "The staffed clinic is not running: " + string.Join(", ", sim.ClinicProblems(clinic)));

        // Waiting chairs along the corridor, through the build tool like a player would.
        var tool = hospital.BuildTool;
        tool.StartChairs();
        var placed = 0;
        for (var y = b.Y - 13; y < b.Y - 1 && placed < 6; y++)
        {
            var tile = new Simulation.World.GridCoord(b.X - 15, y);
            if (!sim.CheckChair(tile, tool.FacingFor(tile), tool.ChairColour).CanBuild)
            {
                continue;
            }

            tool.PreviewDrag(tile, tile);
            tool.Confirm();
            placed = sim.Chairs.Count(c => c.Tile.X == b.X - 15);
        }

        tool.Cancel();
        tool.FollowMouse = true;
        Check(placed >= 4, $"Only {placed} corridor chairs could be placed.");
        Check(sim.CheckChair(new Simulation.World.GridCoord(desk.Rect.X + 2, desk.Rect.Y + 2), Simulation.World.WallSide.South, Simulation.Hospital.ChairColours.Default).Problems.Any(p => p.Contains("corridor")), "A chair should not go inside a room.");

        var clock = hospital.Session.Clock;
        // Clinics run 09:00 to 16:30 on weekdays: run on to the next weekday morning's clinic if need be
        // (a Friday evening build would otherwise stop on Saturday, when there are no clinics).
        var nextClinicDay = clock.Now.Date.AddDays(1);
        while (nextClinicDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            nextClinicDay = nextClinicDay.AddDays(1);
        }

        var limit = nextClinicDay.AddHours(12);
        while (clock.Now < limit && !(sim.Appointments.Any(a => a.Status == Simulation.Patients.AppointmentStatus.Seen)
                                      && sim.Outpatients.Count(o => o.Stage == Simulation.Patients.OutpatientStage.Waiting && !o.IsMoving) >= 1
                                      && sim.Outpatients.Any(o => o.Stage == Simulation.Patients.OutpatientStage.BeingSeen)))
        {
            clock.Step();
        }

        // Let whoever was just checked in reach their chair.
        for (var i = 0; i < 120 && sim.Outpatients.Any(o => o.Stage == Simulation.Patients.OutpatientStage.Waiting && o.IsMoving); i++)
        {
            clock.Step();
        }

        var booked = sim.CalendarFor(clinic.Id, clock.Now).Count();
        GD.Print($"[smoke] Clinic at {clock.Now:HH:mm}: {booked} booked today, {sim.Appointments.Count(a => a.Status == Simulation.Patients.AppointmentStatus.Seen)} seen, {sim.Outpatients.Count} outpatients in the building.");
        Check(sim.Appointments.Any(a => a.Status == Simulation.Patients.AppointmentStatus.Seen), "No clinic patient was seen.");
        Check(sim.Accounts.Take(2).Any(d => d[Simulation.Simulation.MoneyCategory.Clinics] > 0), "Clinic income was not booked.");
        clock.SetSpeed(SimulationSpeed.Paused);
        hospital.SetMode(GameMode.Play);
        hospital.Hud.HideRightPanels();
        hospital.Camera.SnapTo(IsoDraw.ToScreen(b.X - 14, b.Y - 7), 0.95f);
    }

    /// <summary>A cardiology wing north of the theatre: a cath lab, an echo room and an early pregnancy unit, each with its specialist.</summary>
    private static void BuildSpecialtyRooms(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        hospital.Hud.HideRightPanels();
        sim.AdjustBalance(2_000_000);
        Check(sim.UnlockDepartment("cardiology").CanBuild, "Could not unlock cardiology.");
        var wing = sim.ExtendBuilding(new Simulation.World.GridRect(b.X - 32, b.Y - 28, 32, 14));
        Check(wing.CanBuild, "Specialty wing refused: " + string.Join("; ", wing.Problems));
        var corridor = sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(b.X - 20, b.Y - 28, 6, 15), Simulation.World.WallSide.South));
        Check(corridor.CanBuild, "Specialty corridor refused: " + string.Join("; ", corridor.Problems));
        foreach (var (template, rect) in new[]
        {
            ("cath_lab", new Simulation.World.GridRect(b.X - 32, b.Y - 28, 12, 13)),
            ("echo_room", new Simulation.World.GridRect(b.X - 14, b.Y - 28, 8, 7)),
            ("early_pregnancy_unit", new Simulation.World.GridRect(b.X - 14, b.Y - 21, 9, 7)),
        })
        {
            var built = sim.BuildRoom(sim.AutoPlan(template, rect));
            Check(built.CanBuild, $"{template} refused: " + string.Join("; ", built.Problems));
        }

        var lab = sim.Spaces.Single(s => s.Kind == Simulation.Hospital.CareSpaceKind.CathLab);
        var echo = sim.Spaces.Single(s => s.Kind == Simulation.Hospital.CareSpaceKind.Echo);
        sim.HireStaff(Simulation.Staff.StaffRole.CardiacPhysiologist, lab.Id, skill: 0.7);
        sim.HireStaff(Simulation.Staff.StaffRole.CardiacPhysiologist, echo.Id, skill: 0.7);
        sim.HireStaff(Simulation.Staff.StaffRole.Sonographer, skill: 0.7);
        Check(sim.CanDo(hospital.Session.Database.Investigations["echocardiogram"]), "Echocardiograms can't be done with a staffed echo room.");
        Check(sim.CanDo(hospital.Session.Database.Investigations["pelvic_ultrasound"]), "Pelvic ultrasound can't be done with a staffed early pregnancy unit.");
        Check(sim.HasProcedureRoomFor(hospital.Session.Database.Conditions["acute_myocardial_infarction"].Operation!, "cardiology"), "The cath lab can't do primary PCI.");
        for (var i = 0; i < 60; i++)
        {
            hospital.Session.Clock.Step();
        }

        GD.Print($"[smoke] Built {lab.Name}, {echo.Name} and the early pregnancy unit with their specialists.");
        hospital.ClearSelectionForTest();
        hospital.Camera.SnapTo(IsoDraw.ToScreen(b.X - 21, b.Y - 22), 0.8f);
    }

    private static Vector2 ToiletsCentre(HospitalScene hospital)
    {
        var block = hospital.Session.Simulation.SpacesOfKind(Simulation.Hospital.CareSpaceKind.Toilets).Single();
        return IsoDraw.ToScreen(block.Rect.X + 5, block.Rect.Y + 1);
    }

    /// <summary>
    /// A small wing west of the facilities wing, either side of a corridor, with a toilet block, an accessible toilet and a staff toilet.
    /// The clock runs until someone is using the toilets, and a wet patch is left on the corridor floor to show what happens
    /// when patients can't get to one.
    /// </summary>
    private static void BuildToilets(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        sim.AdjustBalance(500_000);
        var wing = sim.ExtendBuilding(new Simulation.World.GridRect(b.X - 40, b.Y + 20, 10, 24));
        Check(wing.CanBuild, "Toilet wing refused: " + string.Join("; ", wing.Problems));
        var corridor = sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(b.X - 40, b.Y + 32, 10, 4), Simulation.World.WallSide.South));
        Check(corridor.CanBuild, "Toilet corridor refused: " + string.Join("; ", corridor.Problems));
        foreach (var (template, rect) in new[]
        {
            ("public_toilets", new Simulation.World.GridRect(b.X - 40, b.Y + 36, 10, 8)),
            ("accessible_toilet", new Simulation.World.GridRect(b.X - 40, b.Y + 27, 5, 5)),
            ("staff_toilet", new Simulation.World.GridRect(b.X - 34, b.Y + 27, 3, 5)),
        })
        {
            var built = sim.BuildRoom(sim.AutoPlan(template, rect));
            Check(built.CanBuild, $"{template} refused: " + string.Join("; ", built.Problems));
        }

        var block = sim.SpacesOfKind(Simulation.Hospital.CareSpaceKind.Toilets).Single();
        Check(block.ToiletSpots.Count == 4, $"The toilet block has {block.ToiletSpots.Count} cubicles, not 4.");
        Check(block.SinkSpots.Count >= 1, "The toilet block has no basins.");
        Check(sim.SpacesOfKind(Simulation.Hospital.CareSpaceKind.AccessibleToilet).Single().ToiletSpots.Count == 1, "The accessible toilet has no toilet.");
        Check(sim.SpacesOfKind(Simulation.Hospital.CareSpaceKind.StaffToilet).Single().ToiletSpots.Count == 1, "The staff toilet has no toilet.");
        Check(sim.HasPublicToilets, "The hospital should have public toilets now.");

        for (var i = 0; i < 16; i++)
        {
            sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        }

        // Run until someone is in a cubicle and someone else is washing or walking to the toilets.
        var clock = hospital.Session.Clock;
        var limit = clock.Now.AddHours(10);
        while (clock.Now < limit && !(sim.ToiletTrips.Any(t => t.Stage == Simulation.Simulation.ToiletStage.Using) && sim.ToiletTrips.Count >= 2))
        {
            clock.Step();
        }

        GD.Print($"[smoke] Toilets: {block.ToiletSpots.Count} cubicles, {block.SinkSpots.Count} basins; {sim.ToiletTrips.Count} people at the toilets at {clock.Now:HH:mm}.");
        Check(sim.ToiletTrips.Count > 0, "Nobody went to the toilets in ten hours.");
        sim.SpillForTest(new Simulation.World.GridCoord(b.X - 37, b.Y + 33));
        Check(sim.FloorAccidents.Count == 1, "The wet patch wasn't left on the floor.");
        clock.SetSpeed(SimulationSpeed.Paused);
        hospital.SetMode(GameMode.Play);
        hospital.ClearSelectionForTest();
        hospital.Camera.SnapTo(ToiletsCentre(hospital) - new Vector2(220, 40), 1.15f);
    }

    /// <summary>
    /// A pharmacy off a corridor near the west wing. Walk-in patients with cellulitis are sent home
    /// with antibiotics, so they queue at the pharmacy on the way out; the clock runs until one is at the counter.
    /// </summary>
    private static void BuildPharmacy(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        sim.AdjustBalance(500_000);
        // The first free 10 x 8 spot off a corridor, nearest the west wing.
        var spot = Enumerable.Range(-60, 120).SelectMany(dy => Enumerable.Range(-45, 110).Select(dx => new Simulation.World.GridRect(b.X + dx, b.Y + dy, 10, 8)))
            .OrderBy(r => Math.Abs(r.X - (b.X - 15)) + Math.Abs(r.Y - (b.Y + 5)))
            .FirstOrDefault(r => sim.CheckRoom(sim.AutoPlan("pharmacy", r)).CanBuild);
        Check(spot.Width > 0, "Found nowhere to build a pharmacy.");
        var built = sim.BuildRoom(sim.AutoPlan("pharmacy", spot));
        Check(built.CanBuild, "Pharmacy refused: " + string.Join("; ", built.Problems));
        var pharmacy = sim.SpacesOfKind(Simulation.Hospital.CareSpaceKind.Pharmacy).Single();
        Check(pharmacy.QueueTiles.Count > 0, "The pharmacy has nowhere to queue.");
        sim.HireStaff(Simulation.Staff.StaffRole.Pharmacist, pharmacy.Id, skill: 0.7);
        Check(sim.HasPharmacy, "The pharmacy should be open once a pharmacist is hired.");

        for (var i = 0; i < 10; i++)
        {
            sim.SpawnPatient(ArrivalMethod.WalkIn, "cellulitis");
        }

        var clock = hospital.Session.Clock;
        var limit = clock.Now.AddHours(16);
        bool Busy() => sim.PharmacyTrips.Any(t => t.Stage == Simulation.Simulation.PharmacyTripStage.BeingServed)
                       && sim.PharmacyTrips.Any(t => t.Stage == Simulation.Simulation.PharmacyTripStage.Queueing);
        while (clock.Now < limit && !Busy())
        {
            clock.Step();
        }

        if (!Busy())
        {
            // Nobody arrived together: settle for someone at the counter.
            limit = clock.Now.AddHours(8);
            while (clock.Now < limit && !sim.PharmacyTrips.Any(t => t.Stage == Simulation.Simulation.PharmacyTripStage.BeingServed))
            {
                clock.Step();
            }
        }

        GD.Print($"[smoke] Pharmacy: {sim.PharmacyTrips.Count} people collecting prescriptions at {clock.Now:HH:mm}.");
        Check(sim.PharmacyTrips.Count > 0, "Nobody came to collect a prescription.");
        clock.SetSpeed(SimulationSpeed.Paused);
        hospital.SetMode(GameMode.Play);
        hospital.ClearSelectionForTest();
        hospital.Camera.SnapTo(IsoDraw.ToScreen(pharmacy.Rect.X + 5, pharmacy.Rect.Y + 5), 1.6f);
    }

    /// <summary>Runs the clock until a porter is carrying a ward patient's medicines, and looks at them.</summary>
    private static void FollowMedicineDelivery(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var clock = hospital.Session.Clock;
        var limit = clock.Now.AddHours(48);
        Simulation.Staff.StaffMember? carrier = null;
        while (clock.Now < limit && (carrier = sim.Staff.FirstOrDefault(sim.IsCarryingMedicines)) is null)
        {
            clock.Step();
        }

        GD.Print(carrier is null
            ? "[smoke] No ward medicines were carried in 48 hours."
            : $"[smoke] {carrier.Name} ({carrier.Role}) is carrying medicines to the ward at {clock.Now:HH:mm}.");
        if (carrier is not null)
        {
            // Let them get out into the corridor with the bag.
            var (fromX, fromY) = (carrier.X, carrier.Y);
            var until = clock.Now.AddMinutes(3);
            while (clock.Now < until && sim.IsCarryingMedicines(carrier) && Math.Abs(carrier.X - fromX) + Math.Abs(carrier.Y - fromY) < 8)
            {
                clock.Step();
            }

            hospital.Camera.SnapTo(IsoDraw.ToScreen(carrier.X, carrier.Y), 2.6f);
        }

        clock.SetSpeed(SimulationSpeed.Paused);
    }

    /// <summary>
    /// A new wing south of the ward, reached by corridors beside and below it, with a cafeteria and a vending point. A crowd of
    /// walk-in patients is brought in and the clock run until some of them are out getting food and drink.
    /// </summary>
    private static void BuildRefreshments(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        hospital.Hud.HideRightPanels();
        sim.AdjustBalance(1_000_000);
        Check(sim.IsUnlocked("facilities"), "Facilities should be open from the start.");
        var wing = sim.ExtendBuilding(new Simulation.World.GridRect(b.X - 30, b.Y + 34, 26, 16));
        Check(wing.CanBuild, "Facilities wing refused: " + string.Join("; ", wing.Problems));
        foreach (var rect in new[] { new Simulation.World.GridRect(b.X - 4, b.Y + 17, 4, 17), new Simulation.World.GridRect(b.X - 30, b.Y + 32, 26, 4) })
        {
            var corridor = sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", rect, Simulation.World.WallSide.South));
            Check(corridor.CanBuild, "Facilities corridor refused: " + string.Join("; ", corridor.Problems));
        }

        foreach (var (template, rect) in new[]
        {
            ("cafeteria", new Simulation.World.GridRect(b.X - 30, b.Y + 36, 16, 14)),
            ("vending_point", new Simulation.World.GridRect(b.X - 13, b.Y + 36, 8, 6)),
        })
        {
            var built = sim.BuildRoom(sim.AutoPlan(template, rect));
            Check(built.CanBuild, $"{template} refused: " + string.Join("; ", built.Problems));
        }

        var cafe = sim.SpacesOfKind(Simulation.Hospital.CareSpaceKind.Cafeteria).Single();
        var vending = sim.SpacesOfKind(Simulation.Hospital.CareSpaceKind.VendingPoint).Single();
        Check(cafe.Furniture.Count(f => f.ItemId == "vending_machine") is >= 1 and <= 4, "The cafeteria has the wrong number of vending machines.");
        Check(cafe.Furniture.Any(f => f.ItemId == "food_counter") && cafe.Furniture.Any(f => f.ItemId == "water_cooler"), "The cafeteria is missing its counter or water machine.");
        Check(cafe.Seats.Count >= 8, $"The cafeteria only has {cafe.Seats.Count} chairs.");
        Check(vending.VendingSpots.Count >= 2, "The vending point has fewer than two machines.");

        for (var i = 0; i < 16; i++)
        {
            sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        }

        // Run until someone is sitting down to eat in the cafe (or a long day has passed).
        var clock = hospital.Session.Clock;
        var limit = clock.Now.AddHours(10);
        bool Eating(Simulation.Simulation.RefreshmentTrip t) => t.RoomId == cafe.Id && t.Stage == Simulation.Simulation.RefreshmentStage.Eating && !t.Person.IsMoving;
        while (clock.Now < limit && !sim.RefreshmentTrips.Any(Eating))
        {
            clock.Step();
        }

        var takings = sim.Accounts.Take(2).Sum(d => d[Simulation.Simulation.MoneyCategory.Catering]);
        GD.Print($"[smoke] Cafeteria: {cafe.Seats.Count} seats, {cafe.Furniture.Count(f => f.ItemId == "vending_machine")} vending machines; vending point: {vending.VendingSpots.Count} machines; {sim.RefreshmentTrips.Count} people out for food at {clock.Now:HH:mm}, £{takings} catering takings.");
        Check(takings > 0, "Nobody bought anything from the cafe or vending point.");
        clock.SetSpeed(SimulationSpeed.Paused);
        hospital.SetMode(GameMode.Play);
        hospital.ClearSelectionForTest();
        hospital.Camera.SnapTo(IsoDraw.ToScreen(cafe.Rect.X + (cafe.Rect.Width / 2.0), cafe.Rect.Y + (cafe.Rect.Length / 2.0)) + new Vector2(-200, 0), 1.2f);
    }

    /// <summary>
    /// An intensive care unit in a wing north of A&E, reached by a corridor from the end of the A&E corridor: hires its anaesthetist and
    /// nurses, then sends in patients with septic shock and runs the clock until one is on ICU.
    /// </summary>
    private static void BuildIntensiveCare(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        hospital.Hud.HideRightPanels();
        sim.AdjustBalance(2_000_000);
        Check(sim.UnlockDepartment("critical_care").CanBuild, "Could not unlock critical care.");
        var wing = sim.ExtendBuilding(new Simulation.World.GridRect(b.X + 30, b.Y - 14, 30, 14));
        Check(wing.CanBuild, "Critical care wing refused: " + string.Join("; ", wing.Problems));

        // Up from the east end of the A&E corridor, past the resus bay.
        var corridor = sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(b.X + 56, b.Y - 14, 4, 23), Simulation.World.WallSide.South));
        Check(corridor.CanBuild, "Critical care corridor refused: " + string.Join("; ", corridor.Problems));
        var built = sim.BuildRoom(sim.AutoPlan("intensive_care_unit", new Simulation.World.GridRect(b.X + 30, b.Y - 12, 26, 10)));
        Check(built.CanBuild, "ICU refused: " + string.Join("; ", built.Problems));
        var icu = sim.Wards.Single(w => w.TemplateId == "intensive_care_unit");
        Check(icu.Furniture.Count(f => f.ItemId == "ventilator") == icu.Beds.Count, "Every ICU bed should have a ventilator.");
        Check(!sim.IsIntensiveCareOpen(icu), "An ICU should not open without an anaesthetist.");
        foreach (var need in sim.RecommendedStaffing().Where(n => n.RoomId == icu.Id).ToList())
        {
            for (var i = 0; i < need.Short; i++)
            {
                sim.HireStaff(need.Role, icu.Id, 0.75, need.Shift);
            }
        }

        Check(sim.IsIntensiveCareOpen(icu), "The ICU did not open with an anaesthetist.");
        var patients = Enumerable.Range(0, 3).Select(_ => sim.SpawnPatient(Simulation.Patients.ArrivalMethod.Ambulance, "septic_shock")).ToList();
        var clock = hospital.Session.Clock;
        bool InIcuBed(Simulation.Patients.Patient p) =>
            p.Stage == Simulation.Patients.PatientStage.OnWard && p.CareSpaceId == icu.Id && p.BedIndex is { } bed && p.Tile == icu.Beds[bed].PatientSpot && !p.IsMoving;
        for (var i = 0; i < 12 * 3600 && !patients.Any(InIcuBed); i++)
        {
            clock.Step();
        }

        var onIcu = sim.Patients.Count(InIcuBed);
        Check(onIcu >= 1, "No patient reached intensive care: " + string.Join(" | ", patients.Select(p => p.Timeline.LastOrDefault()?.Text)));
        hospital.ClearSelectionForTest();
        hospital.Camera.SnapTo(IsoDraw.ToScreen(icu.Rect.X + (icu.Rect.Width / 2.0), icu.Rect.Y + (icu.Rect.Length / 2.0)), 1.1f);
        GD.Print($"[smoke] Built {icu.Name} with {icu.Beds.Count} ventilated beds; {onIcu} on ICU at {clock.Now:HH:mm}.");
    }

    /// <summary>
    /// A maternity wing north of A&amp;E, off the end of the ICU corridor: a delivery room, a neonatal unit, a mother and baby
    /// ward with cots and the C-section theatre, all staffed. Women in labour are brought in and the clock run until a
    /// baby is born and back on the ward with mum. Returns a mother to open the record of.
    /// </summary>
    private static Simulation.Patients.Patient? BuildMaternity(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        hospital.Hud.HideRightPanels();
        sim.AdjustBalance(3_000_000);
        Check(sim.UnlockDepartment(Simulation.Simulation.HospitalSimulation.MaternityDepartmentId).CanBuild, "Could not unlock maternity.");
        foreach (var rect in new[] { new Simulation.World.GridRect(b.X, b.Y - 28, 60, 14), new Simulation.World.GridRect(b.X, b.Y - 14, 30, 14) })
        {
            var wing = sim.ExtendBuilding(rect);
            Check(wing.CanBuild, "Maternity wing refused: " + string.Join("; ", wing.Problems));
        }

        var corridor = sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(b.X, b.Y - 18, 60, 4), Simulation.World.WallSide.South));
        Check(corridor.CanBuild, "Maternity corridor refused: " + string.Join("; ", corridor.Problems));
        foreach (var (template, rect) in new[]
        {
            ("delivery_room", new Simulation.World.GridRect(b.X, b.Y - 28, 10, 10)),
            ("neonatal_unit", new Simulation.World.GridRect(b.X + 11, b.Y - 28, 18, 10)),
            ("mother_baby_ward", new Simulation.World.GridRect(b.X, b.Y - 14, 18, 13)),
            ("caesarean_theatre", new Simulation.World.GridRect(b.X + 18, b.Y - 14, 12, 12)),
        })
        {
            var built = sim.BuildRoom(sim.AutoPlan(template, rect));
            Check(built.CanBuild, $"{template} refused: " + string.Join("; ", built.Problems));
        }

        var theatre = sim.Spaces.Single(s => s.TemplateId == "caesarean_theatre");
        Check(theatre.AllowedDepartments.SetEquals(new[] { "maternity" }), "The C-section theatre should be for maternity only.");
        var ward = sim.MotherAndBabyWards.Single();
        Check(ward.Furniture.Count(f => f.ItemId == "cot") >= ward.Beds.Count && ward.Beds.Count >= 2, $"The mother and baby ward has {ward.Beds.Count} beds and {ward.Furniture.Count(f => f.ItemId == "cot")} cots.");
        foreach (var need in sim.RecommendedStaffing().Where(n => n.RoomId is not null && sim.FindSpace(n.RoomId)?.DepartmentId == "maternity").ToList())
        {
            for (var i = 0; i < need.Short; i++)
            {
                sim.HireStaff(need.Role, need.RoomId, 0.8, need.Shift);
            }
        }

        Check(sim.IsMaternityOpen, "Maternity did not open: needs " + string.Join(", ", sim.MaternityClosedReasons));
        var women = new[] { "established_labour", "preterm_labour" }.Select(c => sim.SpawnMaternityPatient(c)).ToList();
        Check(women.All(w => w is not null), "A woman in labour was turned away from an open maternity unit: " + sim.Notifications.Recent.LastOrDefault()?.Text);

        // Run until a mum is back in her ward bed with her baby in the cot beside her (or two days pass).
        var clock = hospital.Session.Clock;
        var limit = clock.Now.AddHours(48);
        bool BackOnWard(Simulation.Patients.Patient p) => p.Delivered && p.CareSpaceId == ward.Id && sim.BedOf(p) is { } bed && p.Tile == bed.PatientSpot && !p.IsMoving
            && sim.BabiesOf(p).Any(baby => baby.Place == Simulation.Patients.BabyPlace.WithMother);
        while (clock.Now < limit && !women.Any(w => BackOnWard(w!)))
        {
            clock.Step();
        }

        var mother = women.FirstOrDefault(w => BackOnWard(w!)) ?? women.FirstOrDefault(w => w!.Delivered);
        GD.Print($"[smoke] Maternity at {clock.Now:HH:mm}: {sim.MaternityStats.Births} born, {sim.MaternityStats.Caesareans} caesareans, {sim.MaternityStats.NeonatalAdmissions} to the neonatal unit; "
                 + string.Join(", ", sim.Babies.Select(baby => $"{baby.FullName} {baby.WeightKg:0.0} kg {baby.Place}")));
        Check(sim.MaternityStats.Births >= 1 && mother is not null, "No baby was born: " + string.Join(" | ", women.Select(w => w!.Timeline.LastOrDefault()?.Text)));
        clock.SetSpeed(SimulationSpeed.Paused);
        hospital.SetMode(GameMode.Play);
        hospital.ClearSelectionForTest();
        var focus = mother is not null ? (mother.X, mother.Y) : (b.X + 9.0, b.Y - 8.0);
        hospital.Camera.SnapTo(IsoDraw.ToScreen(focus.Item1, focus.Item2) + new Vector2(0, -30), 1.4f);
        return mother;
    }

    /// <summary>
    /// Adds a first floor, puts a lift and stairs beside the A&amp;E corridor, lays a corridor upstairs from
    /// their doors and builds a ward up there, then sends a nurse up. Returns the nurse.
    /// </summary>
    private static Simulation.Staff.StaffMember? BuildUpperFloor(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var site = sim.Site;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        hospital.Hud.HideRightPanels();
        sim.AdjustBalance(2_000_000);
        var floor = sim.AddFloor();
        Check(floor.CanBuild, "First floor refused: " + string.Join("; ", floor.Problems));
        Check(sim.FloorCount == 2, "The building should have two floors.");

        // Find room for a lift and stairs next to a ground-floor corridor.
        Simulation.Hospital.CareSpace? Place(string template, int w, int l)
        {
            for (var y = b.Y; y + l <= b.Bottom; y++)
            {
                for (var x = b.X; x + w <= b.Right; x++)
                {
                    var rect = new Simulation.World.GridRect(x, y, w, l);
                    if (sim.Spaces.Any(s => s.Rect.Intersects(new Simulation.World.GridRect(x - 1, y - 1, w + 2, l + 2)) && s.Kind != Simulation.Hospital.CareSpaceKind.Corridor)
                        || !sim.Spaces.Any(s => s.Kind == Simulation.Hospital.CareSpaceKind.Corridor && s.Rect.Intersects(new Simulation.World.GridRect(x - 1, y - 1, w + 2, l + 2)))
                        || sim.Spaces.Any(s => s.Rect.Intersects(rect)))
                    {
                        continue;
                    }

                    var check = sim.BuildRoom(sim.AutoPlan(template, rect));
                    if (check.CanBuild)
                    {
                        return sim.Spaces.Last(s => s.TemplateId == template && sim.FloorOf(s) == 0);
                    }
                }
            }

            return null;
        }

        var lift = Place("lift", 5, 6);
        Check(lift is not null, "Found nowhere to put a lift.");
        var stairs = Place("stairs", 5, 8);
        Check(stairs is not null, "Found nowhere to put stairs.");
        GD.Print($"[smoke] {lift!.Name} at {lift.Rect} and {stairs!.Name} at {stairs.Rect}, through {sim.FloorCount} floors.");

        // Upstairs: a corridor from the lift door across the building, then a ward off it.
        var upLift = sim.ShaftOf(lift).Single(s => sim.FloorOf(s) == 1);
        var door = upLift.Doors[0];
        var r = upLift.Rect;
        var outside = door.Side switch
        {
            Simulation.World.WallSide.North => new Simulation.World.GridCoord(r.X + door.Offset, r.Y - 1),
            Simulation.World.WallSide.South => new Simulation.World.GridCoord(r.X + door.Offset, r.Bottom),
            Simulation.World.WallSide.West => new Simulation.World.GridCoord(r.X - 1, r.Y + door.Offset),
            _ => new Simulation.World.GridCoord(r.Right, r.Y + door.Offset),
        };
        var up = site.OnFloor(new Simulation.World.GridRect(b.X, b.Y, b.Width, b.Length), 1);
        var corridorRect = door.Side is Simulation.World.WallSide.North or Simulation.World.WallSide.South
            ? new Simulation.World.GridRect(up.X, door.Side == Simulation.World.WallSide.North ? outside.Y - 5 : outside.Y, up.Width, 6)
            : new Simulation.World.GridRect(door.Side == Simulation.World.WallSide.West ? outside.X - 5 : outside.X, up.Y, 6, up.Length);
        var corridor = sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", corridorRect, Simulation.World.WallSide.South));
        Check(corridor.CanBuild, $"Upstairs corridor {corridorRect} refused: " + string.Join("; ", corridor.Problems));

        Simulation.Hospital.CareSpace? ward = null;
        var horizontal = corridorRect.Width > corridorRect.Length;
        for (var i = 0; i < 60 && ward is null; i += 2)
        {
            foreach (var rect in horizontal
                         ? new[] { new Simulation.World.GridRect(up.X + 2 + i, corridorRect.Y - 10, 14, 10), new Simulation.World.GridRect(up.X + 2 + i, corridorRect.Bottom, 14, 10) }
                         : new[] { new Simulation.World.GridRect(corridorRect.X - 10, up.Y + 2 + i, 10, 14), new Simulation.World.GridRect(corridorRect.Right, up.Y + 2 + i, 10, 14) })
            {
                if (sim.BuildRoom(sim.AutoPlan("medical_ward", rect)).CanBuild)
                {
                    ward = sim.Wards.Single(w => sim.FloorOf(w) == 1);
                    break;
                }
            }
        }

        Check(ward is not null, "Found nowhere upstairs for a ward.");
        GD.Print($"[smoke] Upstairs: corridor {corridorRect} and {ward!.Name} with {ward.Beds.Count} beds.");

        // The ward's own nurses walk up to it.
        foreach (var shift in new[] { Simulation.Staff.StaffShift.Day, Simulation.Staff.StaffShift.Night })
        {
            sim.HireStaff(Simulation.Staff.StaffRole.Nurse, ward.Id, 0.6, shift);
        }

        var clock = hospital.Session.Clock;
        Simulation.Staff.StaffMember? nurse = null;
        for (var i = 0; i < 2 * 3600 && nurse is null; i++)
        {
            clock.Step();
            nurse = sim.Staff.FirstOrDefault(m => m.IsInBuilding && sim.FloorOf(m) == 1);
        }

        Check(nurse is not null, "No member of staff made it up to the first floor.");
        GD.Print($"[smoke] {nurse!.Name} went up to the first floor at {clock.Now:HH:mm}.");
        hospital.Camera.SnapTo(IsoDraw.ToScreen(stairs.Rect.X - 10, stairs.Rect.Y + 2), 0.9f);
        return nurse;
    }

    /// <summary>
    /// A new game has no waiting room and A&amp;E stays shut until there is one. Builds it with the build tools,
    /// like a player: drag out the room, place the desk, the entrance on the front path, a door onto the
    /// corridor, then rows of chairs.
    /// </summary>
    private static void FitOutWaitingRoom(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        Check(!sim.SpacesOfKind(Simulation.Hospital.CareSpaceKind.Waiting).Any(), "A new game should start without a waiting room.");
        Check(!sim.IsEmergencyOpen && sim.ClosedReasons.Contains("a waiting room"), "A&E should wait for a waiting room: " + string.Join(", ", sim.ClosedReasons));
        var tool = hospital.BuildTool;
        Simulation.World.GridCoord At(int x, int y) => new(b.X + x, b.Y + y);
        void Do(string what)
        {
            Check(tool.Check is { CanBuild: true }, $"{what} refused: " + string.Join("; ", tool.Check?.Problems ?? Array.Empty<string>()));
            tool.Confirm();
        }

        tool.StartRoom(sim.Database.RoomTemplates["waiting_room"]);
        tool.PreviewDrag(At(15, 26), At(36, 39));
        Do("The waiting room");
        var room = sim.SpacesOfKind(Simulation.Hospital.CareSpaceKind.Waiting).Single();
        Check(room.Doors.Count == 0 && sim.DeskOf(room) is null, "A new waiting room should be empty.");

        tool.StartDesk();
        tool.PreviewAt(At(30, 32));
        Do("The reception desk");
        tool.StartDoors();
        tool.PreviewAt(At(26, 39));
        Do("The entrance door");
        tool.PreviewAt(At(26, 26));
        Do("The door onto the corridor");

        // Rows of chairs facing the desk, in two colours.
        tool.StartChairs(Simulation.Hospital.ChairColours.All[1].Hex);
        tool.FaceChairs(Simulation.World.WallSide.East);
        foreach (var x in new[] { 18, 21, 24 })
        {
            tool.PreviewDrag(At(x, 28), At(x, 36));
            Do("A row of chairs");
        }

        tool.ChairColour = Simulation.Hospital.ChairColours.All[5].Hex;
        tool.FaceChairs(Simulation.World.WallSide.West);
        tool.PreviewDrag(At(36, 28), At(36, 30));
        Do("A row of chairs against the east wall");
        tool.ChairColour = Simulation.Hospital.ChairColours.All[6].Hex;
        tool.FaceChairs(Simulation.World.WallSide.South);
        tool.PreviewDrag(At(31, 26), At(35, 26));
        Do("A row of chairs against the north wall");
        tool.Cancel();
        tool.FollowMouse = true;

        Check(sim.WaitingRoomProblems(sim.FindSpace(room.Id)!).Count == 0, "The waiting room is not ready: " + string.Join(", ", sim.WaitingRoomProblems(sim.FindSpace(room.Id)!)));
        Check(sim.IsEmergencyOpen, "A&E did not open once its waiting room was fitted out: " + string.Join(", ", sim.ClosedReasons));
        GD.Print($"[smoke] Waiting room fitted out: desk, 2 doors, {sim.Chairs.Count} chairs; balance £{sim.Balance:N0}.");
    }

    /// <summary>Centres the camera on the waiting room, to the right of the A&amp;E board.</summary>
    private static void LookAtWaitingRoom(HospitalScene hospital)
    {
        var room = hospital.Session.Simulation.MainWaitingRoom!.Rect;
        hospital.Camera.SnapTo(IsoDraw.ToScreen(room.X + (room.Width / 2.0), room.Y + (room.Length / 2.0)) - new Vector2(160, 0), 0.95f);
    }

    /// <summary>Build mode: clicking a corridor chair opens it with a Remove button.</summary>
    private static void ClickCorridorChair(HospitalScene hospital)
    {
        hospital.SetMode(GameMode.Build);
        hospital.Hud.HideRightPanels();
        var sim = hospital.Session.Simulation;
        var chair = sim.Chairs.First(c => sim.Spaces.Any(s => s.Kind == Simulation.Hospital.CareSpaceKind.Corridor && s.Rect.Contains(c.Tile)) && !sim.Spaces.Any(s => s.HasWalls && s.Rect.Contains(c.Tile)));
        hospital.ClickAt(IsoDraw.ToScreen(chair.Tile.X + 0.5, chair.Tile.Y + 0.5) + new Vector2(0, -8));
        Check(hospital.Hud.ItemInfo.Visible && hospital.Hud.ItemInfo.Item?.Tile == chair.Tile, "Clicking a corridor chair in build mode did not open it.");
    }

    /// <summary>The Decorate tool: clicking the staff room opens its panel, and picks show in the world before they are paid for.</summary>
    private static void StartDecorating(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        hospital.Hud.Build.Open(Simulation.Data.BuildMenu.Decorate);
        Check(hospital.Hud.Build.Decorating, "The Decorate icon did not open the Decorate tool.");
        var room = sim.Spaces.First(s => s.Kind == Simulation.Hospital.CareSpaceKind.StaffRoom);
        var (cx, cy) = (room.Rect.X + (room.Rect.Width / 2.0), room.Rect.Y + (room.Rect.Length / 2.0));
        hospital.Camera.SnapTo(IsoDraw.ToScreen(cx, cy), 1.4f);
        hospital.ClickAt(IsoDraw.ToScreen(cx, cy));
        Check(hospital.Hud.Decorate.Visible && hospital.Hud.Decorate.RoomId == room.Id, "Clicking a room with the Decorate tool did not open its Decorate panel.");
        Check(!hospital.Hud.RoomInfo.Visible, "The Decorate tool should open the Decorate panel, not the room panel.");
        var before = sim.DecorOf(room);
        hospital.Hud.Decorate.PickFloor(Simulation.Hospital.FloorFinish.CarpetTiles, "#8FA7B8");
        hospital.Hud.Decorate.PickWalls(Simulation.Hospital.WallFinish.Stripe, "#DCEBF5");
        Check(sim.DecorOf(room) == before, "Picking a finish should not pay for it before Apply.");
        Check(DecorArt.Preview is { } preview && preview.RoomId == room.Id, "Picking a finish should show it in the world.");
    }

    private static void FinishDecorating(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var room = sim.FindSpace(hospital.Hud.Decorate.RoomId!)!;
        var balance = sim.Balance;
        var cost = sim.DecorateCost(room, hospital.Hud.Decorate.Draft);
        hospital.Hud.Decorate.Apply();
        Check(sim.DecorOf(room).Floor == Simulation.Hospital.FloorFinish.CarpetTiles && sim.DecorOf(room).Walls == Simulation.Hospital.WallFinish.Stripe, "Apply did not redecorate the room.");
        Check(cost > 0 && sim.Balance == balance - cost, $"Redecorating should cost £{cost:N0}.");
        Check(DecorArt.Preview is null, "After Apply nothing should be left on trial.");
        hospital.Hud.Build.ClosePopup();
    }

    /// <summary>Play mode: clicking a clinic patient opens the clinic calendars.</summary>
    private static void ClickOutpatient(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var outpatient = sim.Outpatients.Where(o => !o.IsMoving).OrderBy(o => o.Stage == Simulation.Patients.OutpatientStage.Waiting ? 0 : 1).First();
        hospital.ClickAt(hospital.World.FootPosition(outpatient) + new Vector2(0, -30));
        Check(hospital.World.Selected == outpatient, "Clicking a clinic patient did not select them.");
        Check(hospital.Hud.Clinics.Visible, "Clicking a clinic patient did not open the clinic calendars.");
    }

    /// <summary>Build mode: clicking bare floor in the new cubicle opens the room, and clicking a member of staff does nothing.</summary>
    private static void ClickRoomFloor(HospitalScene hospital, Simulation.Hospital.CareSpace room)
    {
        Check(hospital.Mode == GameMode.Build, "Should still be in build mode.");
        var floor = Enumerable.Range(0, room.Rect.Width * room.Rect.Length)
            .Select(i => IsoDraw.ToScreen(room.Rect.X + (i % room.Rect.Width) + 0.5, room.Rect.Y + (i / room.Rect.Width) + 0.5))
            .First(p => hospital.ItemAt(p, bedsOnly: false) is null);
        hospital.ClickAt(floor);
        Check(hospital.Hud.RoomInfo.Visible && hospital.Hud.RoomInfo.RoomId == room.Id, "Clicking the room in build mode did not open it.");

        var member = hospital.Session.Simulation.Staff.First();
        hospital.ClickAt(hospital.World.FootPosition(member) + new Vector2(0, -30));
        Check(hospital.World.Selected is null && !hospital.Hud.StaffInfo.Visible, "Build mode should not select staff.");
        hospital.ClickAt(floor);
    }

    /// <summary>Build mode: clicking the new cubicle's bed opens it as an item.</summary>
    private static void ClickBuildItem(HospitalScene hospital)
    {
        var room = hospital.Session.Simulation.Spaces.Where(s => s.TemplateId == "majors_cubicle").OrderBy(s => s.Rect.X).First();
        var bed = room.Furniture.First(f => hospital.Session.Database.Furniture[f.ItemId].Style == Simulation.Data.Models.FurnitureStyle.Bed);
        var (bx, by, bw, bl) = hospital.Footprint(room, bed);
        hospital.ClickAt(IsoDraw.ToScreen(bx + (bw / 2), by + (bl / 2)));
        Check(hospital.Hud.ItemInfo.Visible && !hospital.Hud.ItemInfo.AsBed && hospital.Hud.ItemInfo.Item == bed, "Clicking the bed in build mode did not open it as an item.");
    }

    /// <summary>Play mode: clicking the ward floor picks nothing; clicking an empty bed opens the bed.</summary>
    /// <summary>In the 2D plan a click on someone's circle picks them, as clicking their figure does in 3D.</summary>
    private static void CheckPlanClick(HospitalScene hospital)
    {
        var member = hospital.Session.Simulation.Staff.FirstOrDefault(m => m.IsInBuilding && hospital.World.OnViewFloor(m) && !m.IsMoving);
        if (member is null)
        {
            return;
        }

        hospital.ClickAt(hospital.World.FootPosition(member));
        Check(hospital.World.Selected is StaffMember, "Clicking a staff circle in the 2D plan did not pick anyone.");
        hospital.Hud.HideRightPanels();
        hospital.World.Selected = null;
    }

    private static void ClickWardBed(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var ward = sim.Wards.Single();
        Check(hospital.Mode == GameMode.Play, "Should be in play mode on the ward.");
        var floor = IsoDraw.ToScreen(ward.Rect.X + 0.5, ward.Rect.Y + (ward.Rect.Length / 2.0));
        if (hospital.World.HitTest(floor) is null && hospital.ItemAt(floor, bedsOnly: true) is null)
        {
            hospital.ClickAt(floor);
            Check(!hospital.Hud.RoomInfo.Visible && !hospital.Hud.ItemInfo.Visible, "Play mode should not select rooms.");
        }

        var bed = ward.Furniture
            .Where(f => sim.Database.Furniture[f.ItemId].Style == Simulation.Data.Models.FurnitureStyle.Bed)
            .Select(f => (Item: f, At: IsoDraw.ToScreen(f.Tile.X + 0.5, f.Tile.Y + 0.5)))
            .First(b => ItemPanel.BedOccupant(ward, b.Item) is null && hospital.World.HitTest(b.At) is null);
        hospital.ClickAt(bed.At);
        Check(hospital.Hud.ItemInfo.Visible && hospital.Hud.ItemInfo.AsBed, "Clicking an empty bed in play mode did not open it.");
    }

    /// <summary>A patient on the ward arrests: runs the clock until CPR is going and the resus trolley is at the bed.</summary>
    private static void StartCrashCall(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var patient = sim.Patients.First(p => p.Stage == Simulation.Patients.PatientStage.OnWard && p.IsAlive);
        var arrest = sim.StartArrest(patient, "smoke test");
        var clock = hospital.Session.Clock;
        for (var i = 0; i < 20 * 60 && arrest.IsActive && (arrest.TrolleyArrivedAt is null || arrest.CprStartedAt is null); i++)
        {
            clock.Step();
        }

        // Hold the scene still for the screenshots (compressions still animate in real time).
        clock.SetSpeed(SimulationSpeed.Paused);
        Check(arrest.CrashCalled, "No crash call was put out.");
        Check(arrest.CprStartedAt is not null || !arrest.IsActive, "Nobody started CPR.");
        Check(sim.ResusTrolleys.Count > 0, "The ward has no resus trolley.");
        var ward = sim.Wards.Single();
        hospital.ClearSelectionForTest();
        hospital.Camera.SnapTo(IsoDraw.ToScreen(patient.X, patient.Y), 2.0f);
        GD.Print($"[smoke] Crash call: CPR at {arrest.CprStartedAt}, trolley at {arrest.TrolleyArrivedAt}, team {arrest.Team.Count}, still going {arrest.IsActive} on {ward.Name}.");
    }

    /// <summary>A patient in a ward bed falls: they lie on the floor beside the bed until the team help them up.</summary>
    private static void StartWardFall(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var patient = sim.Patients.FirstOrDefault(p => p.IsAlive && p.Stage == Simulation.Patients.PatientStage.OnWard && sim.BedOf(p) is not null && p.Emergency is null && !p.InArrest && !p.IsMoving);
        Check(patient is not null, "Nobody in a ward bed to have a fall.");
        sim.StartWardEmergency(patient!, hospital.Session.Database.Emergencies["fall"]);
        Check(patient!.OnFloorAfterFall, "The fall did not put the patient on the floor.");
        hospital.Session.Clock.SetSpeed(SimulationSpeed.Paused);
        var bed = sim.BedOf(patient)!;
        hospital.Camera.SnapTo(IsoDraw.ToScreen(bed.PatientSpot.X, bed.PatientSpot.Y + 0.5), 2.0f);
    }

    /// <summary>Runs the clock until the fall has been reviewed and the patient helped back to bed.</summary>
    private static void FinishWardFall(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var clock = hospital.Session.Clock;
        for (var i = 0; i < 3 * 3600 && sim.Patients.Any(p => p.OnFloorAfterFall); i++)
        {
            clock.Step();
        }

        Check(!sim.Patients.Any(p => p.OnFloorAfterFall), "Nobody helped the fallen patient back to bed within three hours.");
        GD.Print($"[smoke] Fall dealt with; ward emergencies {sim.Stats.WardEmergencies}, treated {sim.Stats.WardEmergenciesTreated}.");
    }

    /// <summary>Runs the clock until the arrest is over and the team have stood down.</summary>
    private static void FinishCrashCall(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var clock = hospital.Session.Clock;
        clock.SetSpeed(SimulationSpeed.VeryFast);
        for (var i = 0; i < 60 * 60 && sim.Arrests.Any(a => a.IsActive); i++)
        {
            clock.Step();
        }

        Check(!sim.Arrests.Any(a => a.IsActive), "The arrest did not end within an hour.");
        GD.Print($"[smoke] Crash call over: {sim.Arrests.Last().Outcome}; arrests {sim.Stats.CardiacArrests}, ROSC {sim.Stats.ArrestsRosc}.");
    }

    /// <summary>The build bar fits on screen and every room can be reached from it.</summary>
    private void CheckBuildBar(HospitalScene hospital)
    {
        var bar = hospital.Hud.Build;
        Check(bar.Visible, "Build mode did not show the build bar.");
        var screen = GetViewport().GetVisibleRect().Size;
        var rect = bar.BarRect;
        Check(rect.Position.X >= MainHUD.NavWidth && rect.End.X <= screen.X && rect.End.Y <= screen.Y, $"The build bar does not fit on screen: {rect} on {screen}.");
        var listed = bar.Menu.Icons.SelectMany(i => i.Items).ToHashSet();
        foreach (var id in hospital.Session.Database.RoomTemplates.Keys.Where(id => !listed.Contains(id)))
        {
            Check(false, $"Room '{id}' cannot be built from the build bar.");
        }

        Check(bar.Menu.Icons.Any(i => i.Id == "maternity" && i.Items.Contains("delivery_room")), "The build bar has no Maternity icon with a delivery room.");
        Check(bar.Menu.Icons.Any(i => i.Id == "facilities" && i.Items.Contains("public_toilets")), "The Facilities icon has no toilets.");
    }

    /// <summary>The open pop-up sits above the bar and inside the screen.</summary>
    private void CheckBuildPopup(HospitalScene hospital)
    {
        var bar = hospital.Hud.Build;
        var screen = GetViewport().GetVisibleRect().Size;
        var popup = bar.PopupRect;
        Check(bar.PopupOpen, "The build pop-up did not open.");
        Check(popup.Position.X >= 0 && popup.End.X <= screen.X && popup.End.Y <= bar.BarRect.Position.Y, $"The {bar.OpenIcon} pop-up does not fit: {popup} on {screen}.");
    }

    /// <summary>Builds a ward south of the new corridor, then sends in patients with pneumonia and runs the clock until some are on it.</summary>
    private static void BuildWard(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        var ward = sim.BuildRoom(sim.AutoPlan("medical_ward", new Simulation.World.GridRect(b.X - 30, b.Y + 17, 26, 15)));
        Check(ward.CanBuild, "Ward refused: " + string.Join("; ", ward.Problems));
        var built = sim.Wards.Single();
        Check(built.Beds.Count >= 8, $"Ward should have two rows of beds, has {built.Beds.Count}.");
        Check(!sim.Staff.Any(s => s.BaseRoomId == built.Id), "A new ward should come with no staff.");
        Check(sim.RecommendedStaffing().Any(n => n.RoomId == built.Id && n.Short > 0), "The new ward should show as short of staff.");
    }

    /// <summary>Hires the ward's recommended team from the candidates (topping up directly if none fit), then admits patients.</summary>
    private static void StaffWardAndAdmit(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var built = sim.Wards.Single();
        foreach (var need in sim.RecommendedStaffing().Where(n => n.RoomId == built.Id).ToList())
        {
            for (var i = 0; i < need.Short; i++)
            {
                var candidate = sim.Candidates.FirstOrDefault(c => c.Role == need.Role);
                if (candidate is not null)
                {
                    sim.Hire(candidate.Id, built.Id);
                }
                else
                {
                    sim.HireStaff(need.Role, built.Id, 0.7);
                }
            }
        }

        Check(sim.Staff.Count(s => s.BaseRoomId == built.Id) >= 2, "Ward team not hired.");

        var patients = Enumerable.Range(0, 4).Select(_ => sim.SpawnPatient(Simulation.Patients.ArrivalMethod.Ambulance, "community_acquired_pneumonia")).ToList();
        var clock = hospital.Session.Clock;
        for (var i = 0; i < 12 * 3600 && patients.Count(p => p.Stage == Simulation.Patients.PatientStage.OnWard) < 2; i++)
        {
            clock.Step();
        }

        var onWard = sim.Patients.Count(p => p.Stage == Simulation.Patients.PatientStage.OnWard);
        Check(onWard >= 1, "No patient was admitted to the ward.");
        hospital.Camera.SnapTo(IsoDraw.ToScreen(built.Rect.X + (built.Rect.Width / 2.0), built.Rect.Y + (built.Rect.Length / 2.0)), 1.1f);
        GD.Print($"[smoke] Built {built.Name} with {built.Beds.Count} beds; {onWard} admitted, Admitted stat {sim.Stats.Admitted}.");
    }

    /// <summary>Builds an X-ray room north of the corridor, then X-rays a patient with a broken ankle.</summary>
    private void BuildImagingAndSendForScan(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        var xray = sim.BuildRoom(sim.AutoPlan("xray_room", new Simulation.World.GridRect(b.X - 30, b.Y, 10, 9)));
        Check(xray.CanBuild, "X-ray room refused: " + string.Join("; ", xray.Problems));
        var room = sim.Spaces.Single(s => s.Kind == Simulation.Hospital.CareSpaceKind.XRay);
        sim.HireStaff(Simulation.Staff.StaffRole.Radiographer);

        // And one for nights, so a patient whose X-ray is asked for in the evening isn't left waiting until morning.
        sim.HireStaff(Simulation.Staff.StaffRole.Radiographer, shift: Simulation.Staff.StaffShift.Night);
        Check(sim.Staff.Any(s => s.BaseRoomId == room.Id && s.Role == Simulation.Staff.StaffRole.Radiographer), "No radiographer posted to the X-ray room.");

        // Ambulance patients come in: anyone who can't walk is pushed by a porter. Catch one out in a corridor.
        var patients = new[] { "ankle_fracture", "community_acquired_pneumonia", "community_acquired_pneumonia" }
            .Select(c => sim.SpawnPatient(Simulation.Patients.ArrivalMethod.Ambulance, c)).ToList();
        _scanPatient = patients[0];
        var clock = hospital.Session.Clock;
        var pushedFor = new Dictionary<int, int>();
        Simulation.Patients.Patient? pushed = null;
        for (var i = 0; i < 8 * 3600 && pushed is null; i++)
        {
            clock.Step();
            foreach (var p in sim.Patients)
            {
                // Out in a corridor, clear of doorways, for the screenshot.
                var inCorridor = sim.Spaces.Any(c => c.Kind == Simulation.Hospital.CareSpaceKind.Corridor && c.Rect.Contains(p.Tile));
                // (A nurse, not a porter, wheels a patient into corridor care, so those don't count here.)
                pushedFor[p.Id] = p.PushedByStaffId is not null && p.IsMoving && inCorridor && p.CorridorSpot is null ? pushedFor.GetValueOrDefault(p.Id) + 1 : 0;
                if (pushedFor[p.Id] >= 4)
                {
                    pushed = p;
                }
            }
        }

        clock.SetSpeed(SimulationSpeed.Paused);
        Check(pushed is not null, "No porter pushed anyone in 8 hours.");
        var porter = sim.FindStaff(pushed!.PushedByStaffId!.Value)!;
        Check(porter.Role == Simulation.Staff.StaffRole.Porter, $"Pushed by a {porter.Role}, not a porter.");
        hospital.Camera.SnapTo(hospital.World.FootPosition(pushed) + new Vector2(0, -20), 1.6f);
        GD.Print($"[smoke] {porter.Name} (porter) is pushing {pushed.FullName} ({pushed.Mobility}) at {clock.Now:HH:mm}.");
    }

    private Simulation.Patients.Patient? _scanPatient;

    /// <summary>Runs on until a patient is on the X-ray table (a new one if the first was scanned while we watched the porter).</summary>
    private void FinishScan(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var room = sim.Spaces.Single(s => s.Kind == Simulation.Hospital.CareSpaceKind.XRay);
        var clock = hospital.Session.Clock;
        var patient = _scanPatient!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0)
            {
                patient = sim.SpawnPatient(Simulation.Patients.ArrivalMethod.Ambulance, "ankle_fracture");
            }

            for (var i = 0; i < 8 * 3600 && (patient.IsMoving || patient.Tile != room.PatientSpot) && patient.IsInDepartment
                            && !patient.Investigations.Any(o => o.Status != InvestigationStatus.Requested && sim.Database.Investigations[o.InvestigationId].Room is not null); i++)
            {
                clock.Step();
            }

            if (!patient.IsMoving && patient.Tile == room.PatientSpot)
            {
                break;
            }
        }

        Check(!patient.IsMoving && patient.Tile == room.PatientSpot, "Patient was not X-rayed: " + string.Join(" / ", patient.Timeline.Select(t => t.Text)));
        hospital.Camera.SnapTo(IsoDraw.ToScreen(room.Rect.X + 9, room.Rect.Y + 6), 1.1f);
        hospital.SelectPatient(patient);
        GD.Print($"[smoke] Built {room.Name}; {patient.FullName} is on the X-ray table at {clock.Now:HH:mm}.");
    }

    private static void OpenARecord(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        // Arrivals are random, so the department may be empty: fall back to a patient who has left.
        var focus = sim.Patients.Where(p => p.IsInDepartment && p.Triage is not null).OrderBy(p => p.Triage).FirstOrDefault()
                    ?? sim.DepartedPatients.First();
        hospital.SelectPatient(focus);
        Check(hospital.Hud.Record.Visible, "Patient record did not open.");
        GD.Print($"[smoke] Opened the record for {focus.FullName}.");
    }

    /// <summary>
    /// Opens the staff rota, checks the night team are at home, moves an A&amp;E nurse to the ward and
    /// back, and picks a doctor so their card shows.
    /// </summary>
    private void OpenRota(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        Check(sim.Staff.Any(s => s.Shift != sim.CurrentShift && !s.IsInBuilding), "The other shift's team should be at home.");
        Check(sim.OnDutyStaff.All(s => s.Shift == sim.CurrentShift || s.Activity == StaffActivity.GoingHome || s.CurrentTaskId is not null || s.IsOnBreak), "Only the team on shift should be at work.");
        var ward = sim.Wards.First();
        var nurse = sim.OnDutyStaff.First(s => s.Role == StaffRole.Nurse && s.BaseRoomId is null);
        Check(sim.MoveStaff(nurse.Id, ward.Id), "Could not move a nurse to the ward.");
        Check(sim.MoveStaff(nurse.Id, null), "Could not move the nurse back to A&E.");
        foreach (var member in sim.OnDutyStaff.Take(3))
        {
            member.Breaks.Insert(0, new PlannedBreak { Start = sim.Now.AddMinutes(-40), Minutes = 15, TakenAt = sim.Now.AddMinutes(-40), Done = true });
        }

        hospital.Hud.ToggleRota();
        Check(hospital.Hud.Rota.Visible, "The rota did not open.");
        hospital.Hud.Rota.Select(sim.OnDutyStaff.FirstOrDefault(s => s.Role == StaffRole.Doctor) ?? sim.Staff.First());
        GD.Print($"[smoke] Rota: {sim.OnDutyStaff.Count()} on shift, {sim.Staff.Count(s => !s.IsOnDuty)} off duty.");
    }

    /// <summary>Saves the current frame when run with "--screenshots=DIR" (needs a real display, not --headless).</summary>
    /// <summary>Brings an ambulance onto the site and centres on it, to check the vehicle sprite from every angle.</summary>
    private void StartAmbulanceShots(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        sim.SpawnPatient(ArrivalMethod.Ambulance);
        hospital.Session.Clock.SetSpeed(SimulationSpeed.Paused);
        for (var i = 0; i < 600 && !sim.Ambulances.Any(a => a.State is AmbulanceState.AtBay or AmbulanceState.WaitingForBay); i++)
        {
            hospital.Session.Clock.Step();
        }

        _ambulance = sim.Ambulances.FirstOrDefault(a => a.State is AmbulanceState.AtBay or AmbulanceState.WaitingForBay)
                     ?? sim.Ambulances.FirstOrDefault(a => a.State != AmbulanceState.Gone);
        GD.Print($"[smoke] Ambulance shots: {(_ambulance is null ? "no ambulance on site" : $"{_ambulance.CallSign} {_ambulance.State}")}.");
        if (_ambulance is not null)
        {
            hospital.Camera.SnapTo(hospital.World.FootPosition(_ambulance) + new Vector2(0, -40), 1.2f);
        }
    }

    private void Screenshot(string name)
    {
        if (_screenshotDirectory is null || DisplayServer.GetName() == "headless")
        {
            return;
        }

        DirAccess.MakeDirRecursiveAbsolute(_screenshotDirectory);
        var image = GetViewport().GetTexture().GetImage();
        var path = $"{_screenshotDirectory}/{name}.png";
        image.SavePng(path);
        GD.Print($"[smoke] Screenshot saved to {path}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void Fail(string message)
    {
        if (_stage == Stage.Done)
        {
            return;
        }

        _stage = Stage.Done;
        GD.PrintErr("[smoke] FAILED: " + message);
        GetTree().Quit(1);
    }
}
