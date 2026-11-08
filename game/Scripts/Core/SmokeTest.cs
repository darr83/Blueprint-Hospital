using System;
using BlueprintHospital.Hospital;
using System.Linq;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Patients;
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
    private int _savedOnWard;

    private enum Stage
    {
        WaitForMenu,
        WaitForHospital,
        RunSimulation,
        CloseUp,
        PatientRecord,
        StaffPanel,
        BuildPreview,
        RoomPanel,
        Recruitment,
        Ward,
        Transport,
        Imaging,
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
                manager.StartNewGame("Smoke Test General");
                _stage = Stage.WaitForHospital;
                break;

            case Stage.WaitForHospital when scene is HospitalScene hospital && hospital.IsNodeReady():
                Check(hospital.Session.Site.Buildings.Count == 1, "Starter building missing.");
                Check(hospital.Camera.IsCurrent(), "Camera is not active.");
                hospital.Session.Clock.SetSpeed(SimulationSpeed.VeryFast);
                GD.Print("[smoke] Hospital scene loaded.");
                CheckPatientJourney(hospital);
                _frame = 0;
                _stage = Stage.RunSimulation;
                break;

            case Stage.RunSimulation when scene is HospitalScene hospital && _frame >= 30:
                Screenshot("02_emergency_department");
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
                hospital.Hud.ShowRoom(newRoom);
                GD.Print($"[smoke] Built {newRoom.Name}; balance £{hospital.Session.Simulation.Balance:N0}.");
                _frame = 0;
                _stage = Stage.RoomPanel;
                break;

            case Stage.RoomPanel when scene is HospitalScene hospital && _frame >= 5:
                Screenshot("06_room_panel");
                hospital.Hud.RoomInfo.Close();
                BuildWard(hospital);
                hospital.Hud.ToggleRecruitment();
                Check(hospital.Hud.Recruitment.Visible, "Recruitment did not open.");
                _frame = 0;
                _stage = Stage.Recruitment;
                break;

            case Stage.Recruitment when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("06b_recruitment");
                hospital.Hud.ToggleRecruitment();
                StaffWardAndAdmit(hospital);
                _frame = 0;
                _stage = Stage.Ward;
                break;

            case Stage.Ward when scene is HospitalScene hospital && _frame >= 90:
                Screenshot("07_ward");
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

                hospital.Hud.ToggleFinances();
                Check(hospital.Hud.Finances.Visible, "Finances did not open.");
                Check(hospital.Session.Simulation.Today[Simulation.Simulation.MoneyCategory.Construction] < 0, "Building work was not booked.");
                _frame = 0;
                _stage = Stage.Finances;
                break;

            case Stage.Finances when scene is HospitalScene hospital && _frame >= 10:
                Screenshot("08_finances");
                hospital.Hud.ToggleFinances();
                var clock = hospital.Session.Clock;
                GD.Print($"[smoke] Clock advanced {clock.TickCount} ticks to {clock.Now:yyyy-MM-dd HH:mm:ss}.");
                hospital.Session.Clock.SetSpeed(SimulationSpeed.Paused);
                _savedTime = clock.Now;
                _savedCamera = hospital.CaptureCamera();
                _savedRooms = hospital.Session.Simulation.Spaces.Count();
                _savedBalance = hospital.Session.Simulation.Balance;
                _savedPatients = hospital.Session.Simulation.Patients.Count();
                _savedOnWard = hospital.Session.Simulation.Patients.Count(p => p.Stage == Simulation.Patients.PatientStage.OnWard);
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
                Check(hospital.Session.Simulation.Patients.Count() == _savedPatients, $"Patients not restored ({hospital.Session.Simulation.Patients.Count()} of {_savedPatients}).");
                Check(hospital.Session.Simulation.Patients.Count(p => p.Stage == Simulation.Patients.PatientStage.OnWard) == _savedOnWard, "Ward patients not restored.");
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

        hospital.Hud.ToggleBuild();
        var template = sim.Database.RoomTemplates["majors_cubicle"];
        hospital.BuildTool.StartRoom(template);
        hospital.BuildTool.PreviewDrag(new Simulation.World.GridCoord(b.X - 10, b.Y), new Simulation.World.GridCoord(b.X - 1, b.Y + 8));
        Check(hospital.BuildTool.Check is { CanBuild: true }, "Preview should be buildable: " + string.Join("; ", hospital.BuildTool.Check?.Problems ?? Array.Empty<string>()));
        hospital.Camera.SnapTo(IsoDraw.ToScreen(b.X - 2, b.Y + 8), 0.9f);
        GD.Print("[smoke] Building extended, corridor laid, cubicle previewed.");
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
        Check(sim.Staff.Any(s => s.BaseRoomId == room.Id && s.Role == Simulation.Staff.StaffRole.Radiographer), "No radiographer posted to the X-ray room.");

        // An ambulance patient with pneumonia goes to X-ray on a trolley, pushed by a porter.
        var patient = sim.SpawnPatient(Simulation.Patients.ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        _scanPatient = patient;
        var clock = hospital.Session.Clock;
        var pushedFor = 0;
        for (var i = 0; i < 8 * 3600 && pushedFor < 4 && patient.Stage != Simulation.Patients.PatientStage.Gone; i++)
        {
            clock.Step();
            // Wait until they are out in a corridor, clear of doorways, for the screenshot.
            var inCorridor = sim.Spaces.Any(c => c.Kind == Simulation.Hospital.CareSpaceKind.Corridor && c.Rect.Contains(patient.Tile));
            pushedFor = patient.PushedByStaffId is not null && patient.IsMoving && patient.Destination == room.PatientSpot && inCorridor ? pushedFor + 1 : 0;
        }

        clock.SetSpeed(SimulationSpeed.Paused);
        Check(pushedFor >= 4, "No porter pushed the patient: " + string.Join(" / ", patient.Timeline.Select(t => t.Text)));
        var porter = sim.FindStaff(patient.PushedByStaffId!.Value)!;
        Check(porter.Role == Simulation.Staff.StaffRole.Porter, $"Pushed by a {porter.Role}, not a porter.");
        hospital.Camera.SnapTo(hospital.World.FootPosition(patient) + new Vector2(0, -20), 1.6f);
        GD.Print($"[smoke] Patient at {patient.X:0.0},{patient.Y:0.0}, porter at {porter.X:0.0},{porter.Y:0.0}.");
        GD.Print($"[smoke] {porter.Name} (porter) is pushing {patient.FullName} ({patient.Mobility}) at {clock.Now:HH:mm}.");
    }

    private Simulation.Patients.Patient? _scanPatient;

    /// <summary>Runs on until the patient is on the X-ray table.</summary>
    private void FinishScan(HospitalScene hospital)
    {
        var sim = hospital.Session.Simulation;
        var room = sim.Spaces.Single(s => s.Kind == Simulation.Hospital.CareSpaceKind.XRay);
        var patient = _scanPatient!;
        var clock = hospital.Session.Clock;
        for (var i = 0; i < 8 * 3600 && (patient.IsMoving || patient.Tile != room.PatientSpot) && patient.Stage != Simulation.Patients.PatientStage.Gone; i++)
        {
            clock.Step();
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
