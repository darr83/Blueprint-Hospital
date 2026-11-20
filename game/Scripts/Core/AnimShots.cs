using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.World3D;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Animation review clips, run with: godot --path game --fixed-fps 15 -- --anim-shots=DIR (needs a display).
/// Builds an X-ray room, a CT scanner and a vending point beside the starter A&amp;E, fills it with patients,
/// then for each scene (a doctor examining, a scan, someone at the vending machine...) runs the game on
/// until it happens, frames it, and saves a few seconds of frames as DIR/scene/NNN.png.
/// </summary>
public partial class AnimShots : Node
{
    private string _directory = "";
    private HospitalScene? _hospital;
    private int _scene;
    private int _captured = -1;
    private int _settle;
    private Agent? _subject;

    private static readonly (string Name, Act? Act, Func<HospitalSimulation, Agent, bool> Where, float Zoom, int Frames, string[] Conditions)[] Scenes =
    {
        ("vending", Act.VendPress, (_, a) => a is not StaffMember, 3.0f, 120, Array.Empty<string>()),
        ("waiting", Act.Phone, (s, a) => a is not StaffMember && InRoom(s, a, CareSpaceKind.Waiting) && SeatedNear(s, a) >= 5, 2.6f, 120, Array.Empty<string>()),
        ("reception", Act.Typing, (_, a) => a is StaffMember { Role: StaffRole.Receptionist }, 3.0f, 60, Array.Empty<string>()),
        ("examine", Act.Examine, (_, a) => a is StaffMember, 3.0f, 120, Array.Empty<string>()),
        ("observations", Act.Observations, (_, a) => a is StaffMember { Role: StaffRole.Nurse }, 3.0f, 60, Array.Empty<string>()),
        ("xray", null, (s, a) => InRoom(s, a, CareSpaceKind.XRay) && Busy(s, a), 2.3f, 190, new[] { "wrist_fracture", "ankle_fracture" }),
        ("ct", null, (s, a) => InRoom(s, a, CareSpaceKind.CT) && Busy(s, a), 2.3f, 190, new[] { "minor_head_injury", "transient_ischaemic_attack" }),
        ("chat", Act.Talk, (_, a) => a is StaffMember { Activity: StaffActivity.Idle or StaffActivity.OnBreak }, 3.0f, 120, Array.Empty<string>()),
        ("night", null, (s, a) => a is Patient p && (s.Now.Hour >= 22 || s.Now.Hour < 5) && !p.IsMoving && p.Stage is PatientStage.InTreatmentSpace or PatientStage.OnWard && p.BusyWithTaskId is null, 2.4f, 90, Array.Empty<string>()),
    };

    /// <summary>Batch 4 (run with --anim-set=4): wards, getting around, front of house, specialist rooms, the night.</summary>
    private static readonly (string Name, Act? Act, Func<HospitalSimulation, Agent, bool> Where, float Zoom, int Frames, string[] Conditions)[] Scenes4 =
    {
        ("lunch", null, (s, a) => a is Patient p && OnWardBed(p) && s.Now.TimeOfDay.TotalHours is >= 12.3 and < 13.1 && WardCount(s, p) >= 3, 2.2f, 90, new[] { "ischaemic_stroke" }),
        ("inbed", null, (s, a) => a is Patient p && OnWardBed(p) && s.Now.Hour is >= 14 and < 17 && WardCount(s, p) >= 4, 2.2f, 90, new[] { "ischaemic_stroke" }),
        ("drip", Act.HangDrip, (_, a) => a is StaffMember, 2.4f, 90, new[] { "ischaemic_stroke", "acute_appendicitis" }),
        ("callbell", null, (s, a) => a is Patient { Maternity: false, BusyWithTaskId: null } p && OnWardBed(p) && Activity3D.PhysioWalk(p, World!, s, 0) is null && s.Now.Hour is >= 8 and < 21 && s.Tasks.Any(t => !t.Started && t.PatientId == p.Id && t.Role == StaffRole.Nurse && t.Kind is TaskKind.Observations or TaskKind.GiveMedication
            && (t.AssignedStaffId is not { } who || s.Staff.FirstOrDefault(m => m.Id == who) is not { } n || Math.Abs(n.X - p.X) + Math.Abs(n.Y - p.Y) > 12)), 2.6f, 60, new[] { "ischaemic_stroke" }),
        ("physio", null, (s, a) => a is Patient p && Activity3D.PhysioWalk(p, World!, s, 0) is not null, 2.6f, 180, new[] { "ischaemic_stroke" }),
        ("wheelchair", null, (_, a) => a is Patient p && p.IsMoving && World!.InWheelchair(p), 2.8f, 60, Array.Empty<string>()),
        ("entrance", null, (s, a) => a is not StaffMember && a.IsMoving && NearEntrance(s, a) && s.Now.Hour is >= 8 and < 19
            && a.RemainingPath.Any(t => !s.Site.Buildings.Any(b => b.Areas.Any(r => r.Contains(t)))), 2.2f, 120, Array.Empty<string>()),
        ("lift", null, (s, a) => a.IsMoving && s.Spaces.FirstOrDefault(r => r.Kind == CareSpaceKind.Lift && s.FloorOf(r) == 0) is { } lift && !lift.Rect.Contains(a.Tile)
            && RoomSigns.DoorMiddle(lift) is var (dx, dy) && Math.Abs(a.X + 0.5 - dx) + Math.Abs(a.Y + 0.5 - dy) is > 2.5 and < 6
            && a.RemainingPath.Any(t => lift.Rect.Contains(t)), 2.4f, 90, new[] { "acute_appendicitis" }),
        ("phone", Act.PhoneCall, (s, a) => a is StaffMember { Role: StaffRole.Receptionist, IsMoving: false } && s.Spaces.Any(r => r.Rect.Contains(a.Tile) && r.Furniture.Any(f => f.ItemId == "reception_desk")), 3.0f, 75, Array.Empty<string>()),
        ("checkin", Act.CheckIn, (_, a) => a is not StaffMember, 3.0f, 90, Array.Empty<string>()),
        ("pharmacy", null, (s, a) => a is StaffMember m && Activity3D.PharmacyVisit(m, World!, s, s.Tasks.Where(t => t.Started).ToDictionary(t => t.Id), 0) is not null, 2.8f, 180, Array.Empty<string>()),
        ("theatre", Act.Bag, (_, a) => a is StaffMember, 3.2f, 120, new[] { "acute_appendicitis" }),
        ("plaster", Act.Wrap, (_, a) => a is StaffMember, 2.8f, 150, new[] { "ankle_fracture", "wrist_fracture" }),
        ("maternity", null, (s, a) => a is Patient { Maternity: true, Stage: PatientStage.OnWard } p && World!.BabiesWith(p).Length > 0 && World.InBed(p), 2.4f, 90, new[] { "established_labour" }),
        ("night4", null, (s, a) => a is Patient p && OnWardBed(p) && s.Now.Hour is >= 23 or < 4 && WardCount(s, p) >= 3, 1.8f, 45, new[] { "ischaemic_stroke" }),
    };

    /// <summary>The cleaner (run with --anim-set=5): the cleaning room, mopping up a spill, mopping, sweeping and pushing the bucket.</summary>
    private static readonly (string Name, Act? Act, Func<HospitalSimulation, Agent, bool> Where, float Zoom, int Frames, string[] Conditions)[] Scenes5 =
    {
        ("cupboard", null, (s, a) => a is StaffMember { Role: StaffRole.Cleaner, IsMoving: false } m && InRoom(s, a, CareSpaceKind.CleaningRoom) && s.CleanerJobOf(m) is null or { Stage: CleanerStage.FetchingKit }, 3.4f, 45, Array.Empty<string>()),
        ("spill", Act.Mop, (s, a) => a is StaffMember m && s.CleanerJobOf(m) is { Spill: not null }, 3.0f, 120, Array.Empty<string>()),
        ("mop", Act.Mop, (s, a) => a is StaffMember m && s.CleanerJobOf(m) is { Spill: null } && !InRoom(s, a, CareSpaceKind.Toilets), 3.0f, 120, Array.Empty<string>()),
        ("sweep", Act.Sweep, (_, a) => a is StaffMember, 3.0f, 120, Array.Empty<string>()),
        ("push", null, (s, a) => a is StaffMember { Role: StaffRole.Cleaner, IsMoving: true } m && s.CleanerJobOf(m) is { HasKit: true, Stage: CleanerStage.Walking } && a.RemainingPath.Count() > 8, 3.0f, 90, Array.Empty<string>()),
    };

    private static WorldLayer? World;

    private static bool OnWardBed(Patient p) => p.Stage == PatientStage.OnWard && !p.IsMoving && World!.InBed(p)
        && World.Simulation.Spaces.FirstOrDefault(r => r.Rect.Contains(p.Tile)) is { IsWard: true };

    private static int WardCount(HospitalSimulation s, Patient p) =>
        s.Patients.Count(o => o.Stage == PatientStage.OnWard && o.CareSpaceId == p.CareSpaceId && !o.IsMoving);

    private static bool NearEntrance(HospitalSimulation s, Agent a) =>
        s.Site.Buildings.SelectMany(b => b.Entrances).Any(e => !e.IsAmbulanceEntrance && Math.Abs(a.X - e.Tile.X - (e.WidthTiles / 2.0)) < 3 && Math.Abs(a.Y - e.Tile.Y) is > 2.5 and < 6);

    private static bool Set4 => OS.GetCmdlineUserArgs().Contains("--anim-set=4");

    private static bool Set5 => OS.GetCmdlineUserArgs().Contains("--anim-set=5");

    private static (string Name, Act? Act, Func<HospitalSimulation, Agent, bool> Where, float Zoom, int Frames, string[] Conditions)[] Active => Set5 ? Scenes5 : Set4 ? Scenes4 : Scenes;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--anim-shots=", StringComparison.Ordinal))
            {
                var shots = new AnimShots { Name = "AnimShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--anim-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Animation Review General")).CallDeferred();
                return true;
            }
        }

        return false;
    }

    private static int SeatedNear(HospitalSimulation sim, Agent a) =>
        sim.Patients.Count(p => p.Seat is { } seat && p.Tile == seat && !p.IsMoving && Math.Abs(p.X - a.X) + Math.Abs(p.Y - a.Y) < 6);

    private static bool InRoom(HospitalSimulation sim, Agent a, CareSpaceKind kind) =>
        sim.Spaces.Any(s => s.Kind == kind && s.Rect.Contains(a.Tile));

    private static bool Busy(HospitalSimulation sim, Agent a) =>
        a is StaffMember { CurrentTaskId: { } id } && sim.Tasks.FirstOrDefault(t => t.Id == id) is { Started: true } task
        && sim.FindPatient(task.PatientId) is { IsMoving: false };

    public override void _Process(double delta)
    {
        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                h.Hud.Visible = false;
                if (h.GetNodeOrNull<CanvasItem>("TileCursor") is { } cursor)
                {
                    cursor.Visible = false;
                }

                h.ToggleWalls();
                Build(h);
            }

            return;
        }

        var sim = _hospital.Session.Simulation;
        var clock = _hospital.Session.Clock;
        var scenes = Active;
        if (_scene >= scenes.Length)
        {
            GD.Print("[anim] Done.");
            GetTree().Quit(0);
            return;
        }

        var only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--anim-scenes=", StringComparison.Ordinal))?["--anim-scenes=".Length..].Split(',');
        if (only is not null && !only.Contains(scenes[_scene].Name))
        {
            _scene++;
            return;
        }

        var scene = scenes[_scene];
        if (_captured < 0)
        {
            // Run the game on (fast, unseen) until the scene happens, with new arrivals to keep it busy.
            clock.SetSpeed(SimulationSpeed.Paused);
            if (_settle == 0 && _scene == 3 && !Set4)
            {
                // Enough doctors and nurses now that the queue has had time to build for the vending machine.
                for (var i = 0; i < 3; i++)
                {
                    sim.HireStaff(StaffRole.Doctor);
                    sim.HireStaff(StaffRole.Nurse);
                }
            }

            if (scene.Name is "vending" or "waiting" && _settle % 5 == 1)
            {
                // A busy waiting room, and (for the review only) a couple of them sent off for a snack now
                // rather than after the usual twenty minutes' wait.
                for (var i = 0; i < 6; i++)
                {
                    sim.SpawnPatient(ArrivalMethod.WalkIn);
                }

                var start = typeof(HospitalSimulation).GetMethod("StartTrip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var p in sim.Patients.Where(p => p.Seat is { } seat && p.Tile == seat && !p.IsMoving && p.CareSpaceId is null && sim.TripOf(p) is null).Take(scene.Name == "vending" ? 2 : 0).ToList())
                {
                    start?.Invoke(sim, new object[] { p, sim.RefreshmentRooms.ToList() });
                }
            }

            if (Set5 && scene.Name == "spill" && _settle % 6 == 1)
            {
                // A dropped drink in the corridor for the cleaner to deal with.
                sim.SpillForTest(new Simulation.World.GridCoord(50 + (_settle % 40), 15), SpillKind.Drink);
            }

            if (Set4 && _settle % 40 == 0 && scene.Name is "checkin" or "pharmacy" or "entrance" or "wheelchair")
            {
                for (var i = 0; i < 4; i++)
                {
                    sim.SpawnPatient(ArrivalMethod.WalkIn);
                }
            }

            if (Set4 && _settle % 25 == 0 && sim.Patients.Count() < 40)
            {
                // Only the patients this scene needs, so A&E keeps moving.
                foreach (var condition in scene.Conditions)
                {
                    sim.SpawnPatient(condition is "ankle_fracture" or "wrist_fracture" ? ArrivalMethod.WalkIn : ArrivalMethod.Ambulance, condition);
                }
            }

            if (!Set4 && _settle % 25 == 0)
            {
                for (var i = 0; i < 4; i++)
                {
                    var condition = scene.Conditions.Length > 0 ? scene.Conditions[i % scene.Conditions.Length] : null;
                    sim.SpawnPatient(i % 3 == 0 && condition is null ? ArrivalMethod.Ambulance : ArrivalMethod.WalkIn, condition);
                }
            }

            var tasks = sim.Tasks.Where(t => t.Started).ToDictionary(t => t.Id);
            for (var i = 0; i < 400; i++)
            {
                clock.Step();
                if (i % 20 != 0)
                {
                    continue;
                }

                tasks = sim.Tasks.Where(t => t.Started).ToDictionary(t => t.Id);

                // Moments that come and go on the animation clock (a phone call): wind that on too while we look.
                _hospital.View3D.People.AnimTime += 7.3;
                _subject = Everyone(sim).FirstOrDefault(a => scene.Where(sim, a) && (scene.Act is null || (Activity3D.For(a, _hospital.World, sim, tasks, _hospital.View3D.People.AnimTime).Act == scene.Act && Activity3D.For(a, _hospital.World, sim, tasks, _hospital.View3D.People.AnimTime + 3).Act == scene.Act)));
                if (_subject is not null)
                {
                    break;
                }
            }

            _settle++;
            if (_settle % 10 == 2)
            {
                GD.Print($"[anim] ... {scene.Name}: has seat {sim.Patients.Count(p => p.Seat is not null)}, on seat {sim.Patients.Count(p => p.Seat is { } st && p.Tile == st && !p.IsMoving)}, waited {sim.Patients.Count(p => p.Seat is not null && sim.Now - p.QueuedSince > TimeSpan.FromMinutes(20))}, seated {Everyone(sim).Count(a => _hospital.World.IsSeated(a))}, trips {sim.RefreshmentTrips.Count} ({string.Join(",", sim.RefreshmentTrips.Select(t => t.Stage))}), {sim.Now:HH:mm}");
            }

            if (_subject is null)
            {
                if (_settle > (scene.Name is "vending" or "waiting" ? 30 : Set4 ? 400 : 200))
                {
                    GD.Print($"[anim] Gave up waiting for {scene.Name}. Patients: {string.Join(", ", sim.Patients.GroupBy(p => p.Stage).Select(g => $"{g.Key} {g.Count()}"))}; seated {Everyone(sim).Count(a => _hospital.World.IsSeated(a))}; trips {sim.RefreshmentTrips.Count}; vending rooms {sim.RefreshmentRooms.Count()}; {sim.Now:HH:mm}");
                    (_scene, _settle) = (_scene + 1, 0);
                }

                return;
            }

            GD.Print($"[anim] {scene.Name}: found {_subject.GetType().Name} {_subject.Id} at {_subject.Tile} after {_settle} rounds; {sim.Now:HH:mm}.");
            _hospital.RotateView(0 - IsoDraw.Rotation);
            var (fx, fy) = (_subject.X + 0.5, _subject.Y + 0.5);

            if (Activity3D.For(_subject, _hospital.World, sim, tasks, 0).FaceTo is { } face && Math.Abs(face.X - fx) + Math.Abs(face.Y - fy) < 5)
            {
                (fx, fy) = ((fx + face.X) / 2, (fy + face.Y) / 2);
            }

            if (scene.Name is "xray" or "ct" or "pharmacy" or "plaster" or "theatre" or "cupboard" && sim.Spaces.FirstOrDefault(r => r.Rect.Contains(_subject.Tile)) is { } room)
            {
                (fx, fy) = (room.Rect.X + (room.Rect.Width / 2.0), room.Rect.Y + (room.Rect.Length / 2.0) - 1);
            }

            if (scene.Name is "pharmacy" or "cupboard")
            {
                // From the other side, so the shelves don't stand between the camera and the pharmacist.
                _hospital.RotateView(2 - IsoDraw.Rotation);
            }

            if (scene.Name is "lift" && sim.Spaces.FirstOrDefault(r => r.Kind == CareSpaceKind.Lift && sim.FloorOf(r) == 0) is { } lift)
            {
                // Face the lift doors: turn the view so their wall is towards the camera.
                (fx, fy) = RoomSigns.DoorMiddle(lift);
                var side = lift.Doors[0].Side;
                _hospital.RotateView((side is Simulation.World.WallSide.North or Simulation.World.WallSide.West ? 2 : 0) - IsoDraw.Rotation);
                GD.Print($"[anim] lift door on the {side} side at {fx:0.0},{fy:0.0}");
            }

            if (scene.Name is "physio" && _subject is Patient walker && Activity3D.PhysioWalk(walker, _hospital.World, sim, 0) is { } walk)
            {
                (fx, fy) = (walk.At.X, walk.At.Y + (sim.BedOf(walker)?.HeadSouth == true ? -1.1 : 1.1));
            }

            // Lift and entrance doors only show with the walls up; everything else is filmed cut away.
            var wallsUp = scene.Name is "lift" or "entrance";
            if (wallsUp == _hospital.View3D.Cutaway)
            {
                _hospital.ToggleWalls();
            }

            _hospital.Camera.SnapTo(IsoDraw.ToScreen(fx, fy) + new Vector2(0, -18), scene.Zoom);
            clock.SetSpeed(SimulationSpeed.Normal);
            (_captured, _settle) = (0, 0);
            return;
        }

        // Let the camera settle for a few frames, then save one frame each frame.
        if (scene.Name is "wheelchair" or "push" && _subject is not null && _settle > 0)
        {
            // Moving subjects: keep them in the middle of the shot.
            var (sx, sy) = _hospital.World.Interpolated(_subject);
            _hospital.Camera.SnapTo(IsoDraw.ToScreen(sx, sy) + new Vector2(0, -18), scene.Zoom);
        }

        if (_settle++ < 8)
        {
            return;
        }

        var image = GetViewport().GetTexture().GetImage();
        var dir = $"{_directory}/{scene.Name}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        image.SavePng($"{dir}/{_captured:000}.png");
        _captured++;
        if (_captured >= scene.Frames)
        {
            GD.Print($"[anim] Saved {scene.Name}.");
            (_scene, _captured, _settle, _subject) = (_scene + 1, -1, 0, null);
        }
    }

    private static IEnumerable<Agent> Everyone(HospitalSimulation sim) =>
        sim.Staff.Where(s => s.IsInBuilding).Cast<Agent>().Concat(sim.Patients).Concat(sim.Outpatients);

    private static void Build(HospitalScene h)
    {
        var sim = h.Session.Simulation;
        World = h.World;
        if (Set4)
        {
            Build4(h);
            return;
        }

        if (Set5)
        {
            // The batch 1 hospital, plus a cleaning room and toilets south of the corridor, and cleaners.
            Build1(h);
            Report("cleaning_room", sim.BuildRoom(sim.AutoPlan("cleaning_room", new Simulation.World.GridRect(46, 18, 5, 6))));
            Report("public_toilets", sim.BuildRoom(sim.AutoPlan("public_toilets", new Simulation.World.GridRect(52, 18, 10, 8))));
            Report("staff_room", sim.BuildRoom(sim.AutoPlan("staff_room", new Simulation.World.GridRect(63, 18, 10, 8))));
            for (var i = 0; i < 2; i++)
            {
                sim.HireStaff(StaffRole.Cleaner, skill: 0.6);
                sim.HireStaff(StaffRole.Cleaner, skill: 0.6, shift: StaffShift.Night);
            }

            return;
        }

        Build1(h);
    }

    private static void Build1(HospitalScene h)
    {
        var sim = h.Session.Simulation;

        sim.AdjustBalance(10_000_000);
        foreach (var dept in sim.Database.Departments.Keys)
        {
            sim.UnlockDepartment(dept);
        }

        Report("extension", sim.ExtendBuilding(new Simulation.World.GridRect(40, 2, 60, 28)));
        Report("link", sim.BuildRoom(new RoomPlan("corridor", new Simulation.World.GridRect(40, 13, 4, 34), Simulation.World.WallSide.South)));
        Report("corridor", sim.BuildRoom(new RoomPlan("corridor", new Simulation.World.GridRect(44, 13, 56, 5), Simulation.World.WallSide.South)));
        var rooms = new[] { "xray_room", "ct_scanner" };
        for (var i = 0; i < rooms.Length; i++)
        {
            Report(rooms[i], sim.BuildRoom(sim.AutoPlan(rooms[i], new Simulation.World.GridRect(44 + (i * 11), 2, 11, 11))));
        }

        Report("vending_point", sim.BuildRoom(sim.AutoPlan("vending_point", new Simulation.World.GridRect(67, 8, 8, 5))));
        Report("waiting_room", sim.BuildRoom(sim.AutoPlan("waiting_room", new Simulation.World.GridRect(77, 4, 10, 9))));

        // Chairs in both waiting rooms (a new game starts with none), so people sit while they wait.
        var placed = 0;
        foreach (var room in sim.Spaces.Where(s => s.Kind == CareSpaceKind.Waiting).ToList())
        {
            var r = room.Rect;
            foreach (var (y, facing) in new[] { (r.Y + 1, Simulation.World.WallSide.South), (r.Y + 3, Simulation.World.WallSide.North), (r.Y + 4, Simulation.World.WallSide.South), (r.Bottom - 2, Simulation.World.WallSide.North) })
            {
                for (var x = r.X + 1; x < r.Right - 1; x++)
                {
                    if (sim.PlaceChair(new Simulation.World.GridCoord(x, y), facing, "#2E7D9A").CanBuild)
                    {
                        placed++;
                    }
                }
            }
        }

        GD.Print($"[anim] Placed {placed} chairs.");

        foreach (var room in sim.Spaces.Where(s => s.Kind is CareSpaceKind.XRay or CareSpaceKind.CT).ToList())
        {
            sim.HireStaff(StaffRole.Radiographer, room.Id, skill: 0.8);
            sim.HireStaff(StaffRole.Radiographer, room.Id, skill: 0.8, shift: StaffShift.Night);
        }

        for (var i = 0; i < 24; i++)
        {
            sim.SpawnPatient(i % 6 == 0 ? ArrivalMethod.Ambulance : ArrivalMethod.WalkIn);
        }
    }

    /// <summary>
    /// Batch 4's hospital: on top of the starter A&amp;E, a north wing (stroke unit, physio gym, plaster room,
    /// theatre, pharmacy), a west wing (outpatient reception and clinic, a delivery room and the mother and
    /// baby ward), and a first floor with a surgical ward reached by a lift. Everything staffed as recommended.
    /// </summary>
    private static void Build4(HospitalScene h)
    {
        var sim = h.Session.Simulation;
        sim.AdjustBalance(30_000_000);
        foreach (var dept in sim.Database.Departments.Keys)
        {
            sim.UnlockDepartment(dept);
        }

        static Simulation.World.GridRect R(int x, int y, int w, int l) => new(x, y, w, l);
        Report("north wing", sim.ExtendBuilding(R(40, 2, 60, 28)));
        Report("link", sim.BuildRoom(new RoomPlan("corridor", R(40, 13, 4, 34), Simulation.World.WallSide.South)));
        Report("corridor", sim.BuildRoom(new RoomPlan("corridor", R(44, 13, 56, 5), Simulation.World.WallSide.South)));
        foreach (var (template, rect) in new[]
                 {
                     ("stroke_unit", R(44, 18, 16, 11)), ("physio_gym", R(60, 18, 12, 10)), ("plaster_room", R(72, 18, 9, 8)),
                     ("operating_theatre", R(81, 18, 12, 12)), ("pharmacy", R(93, 18, 7, 7)),
                     ("xray_room", R(44, 2, 11, 11)), ("staff_room", R(55, 5, 10, 8)),
                 })
        {
            Report(template, sim.BuildRoom(sim.AutoPlan(template, rect)));
        }

        Report("west wing", sim.ExtendBuilding(R(0, 30, 40, 34)));
        Report("west corridor", sim.BuildRoom(new RoomPlan("corridor", R(0, 40, 44, 6), Simulation.World.WallSide.South)));
        foreach (var (template, rect) in new[]
                 {
                     ("outpatient_reception", R(2, 32, 10, 8)), ("medical_clinic", R(12, 32, 8, 8)), ("delivery_room", R(28, 31, 11, 9)),
                     ("mother_baby_ward", R(2, 46, 25, 11)), ("waiting_room", R(20, 32, 8, 8)),
                 })
        {
            Report(template, sim.BuildRoom(sim.AutoPlan(template, rect)));
        }

        // Upstairs: a surgical ward, reached by a lift from the A&E corridor.
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        Report("first floor", sim.AddFloor());
        Report("lift", sim.BuildRoom(sim.AutoPlan("lift", R(b.X + 48, b.Y + 17, 5, 6))));
        Report("upstairs corridor", sim.BuildRoom(new RoomPlan("corridor", sim.Site.OnFloor(R(b.X + 4, b.Y + 9, 56, 8), 1), Simulation.World.WallSide.South)));
        Report("surgical ward", sim.BuildRoom(sim.AutoPlan("surgical_ward", sim.Site.OnFloor(R(b.X + 20, b.Y + 17, 16, 10), 1))));

        var placed = 0;
        foreach (var room in sim.Spaces.Where(s => s.Kind == CareSpaceKind.Waiting).ToList())
        {
            var r = room.Rect;
            foreach (var (y, facing) in new[] { (r.Y + 1, Simulation.World.WallSide.South), (r.Y + 3, Simulation.World.WallSide.North), (r.Bottom - 2, Simulation.World.WallSide.North) })
            {
                for (var x = r.X + 1; x < r.Right - 1; x++)
                {
                    if (sim.PlaceChair(new Simulation.World.GridCoord(x, y), facing, "#2E7D9A").CanBuild)
                    {
                        placed++;
                    }
                }
            }
        }

        GD.Print($"[anim] Placed {placed} chairs.");
        foreach (var need in sim.RecommendedStaffing())
        {
            for (var i = 0; i < need.Short; i++)
            {
                sim.HireStaff(need.Role, need.RoomId, skill: 0.8, shift: need.Shift);
            }
        }

        // Theatres aren't shared by default: open this one to every surgical department.
        foreach (var theatre in sim.Spaces.Where(r => r.Kind == CareSpaceKind.Theatre && sim.FixedTheatreDepartments(r).Count == 0).ToList())
        {
            foreach (var dept in sim.SurgicalDepartments)
            {
                sim.SetTheatreAccess(theatre.Id, dept.Id, true);
            }
        }

        for (var i = 0; i < 6; i++)
        {
            sim.HireStaff(StaffRole.Doctor);
            sim.HireStaff(StaffRole.Nurse);
            sim.HireStaff(StaffRole.Doctor, shift: StaffShift.Night);
            sim.HireStaff(StaffRole.Nurse, shift: StaffShift.Night);
            sim.HireStaff(StaffRole.Porter, shift: i % 2 == 0 ? StaffShift.Day : StaffShift.Night);
        }

        foreach (var condition in new[] { "ischaemic_stroke", "ischaemic_stroke", "ischaemic_stroke", "acute_appendicitis", "ankle_fracture", "established_labour", "established_labour" })
        {
            sim.SpawnPatient(condition is "ankle_fracture" ? ArrivalMethod.WalkIn : ArrivalMethod.Ambulance, condition);
        }

        for (var i = 0; i < 12; i++)
        {
            sim.SpawnPatient(i % 4 == 0 ? ArrivalMethod.Ambulance : ArrivalMethod.WalkIn);
        }
    }

    private static void Report(string what, BuildCheck check) =>
        GD.Print(check.CanBuild ? $"[anim] Built {what}." : $"[anim] {what} refused: {string.Join("; ", check.Problems)}");
}
