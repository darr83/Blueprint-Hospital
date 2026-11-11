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

    private static readonly (string Name, Act Act, Func<HospitalSimulation, Agent, bool> Where, float Zoom, int Frames, string[] Conditions)[] Scenes =
    {
        ("vending", Act.VendPress, (_, a) => a is not StaffMember, 3.0f, 120, Array.Empty<string>()),
        ("waiting", Act.Phone, (s, a) => a is not StaffMember && InRoom(s, a, CareSpaceKind.Waiting) && SeatedNear(s, a) >= 5, 2.6f, 120, Array.Empty<string>()),
        ("reception", Act.Typing, (_, a) => a is StaffMember { Role: StaffRole.Receptionist }, 3.0f, 60, Array.Empty<string>()),
        ("examine", Act.Examine, (_, a) => a is StaffMember, 3.0f, 120, Array.Empty<string>()),
        ("observations", Act.Observations, (_, a) => a is StaffMember { Role: StaffRole.Nurse }, 3.0f, 60, Array.Empty<string>()),
        ("xray", Act.None, (s, a) => InRoom(s, a, CareSpaceKind.XRay) && Busy(s, a), 3.0f, 90, new[] { "wrist_fracture", "ankle_fracture" }),
        ("ct", Act.None, (s, a) => InRoom(s, a, CareSpaceKind.CT) && Busy(s, a), 2.6f, 150, new[] { "minor_head_injury", "transient_ischaemic_attack" }),
    };

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
        if (_scene >= Scenes.Length)
        {
            GD.Print("[anim] Done.");
            GetTree().Quit(0);
            return;
        }

        var only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--anim-scenes=", StringComparison.Ordinal))?["--anim-scenes=".Length..].Split(',');
        if (only is not null && !only.Contains(Scenes[_scene].Name))
        {
            _scene++;
            return;
        }

        var scene = Scenes[_scene];
        if (_captured < 0)
        {
            // Run the game on (fast, unseen) until the scene happens, with new arrivals to keep it busy.
            clock.SetSpeed(SimulationSpeed.Paused);
            if (_settle == 0 && _scene == 3)
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

            if (_settle % 25 == 0)
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
                _subject = Everyone(sim).FirstOrDefault(a => scene.Where(sim, a) && (scene.Act == Act.None ? Activity3D.For(a, _hospital.World, sim, tasks, 0).Act != Act.None : Activity3D.For(a, _hospital.World, sim, tasks, 0).Act == scene.Act));
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
                if (_settle > (scene.Name is "vending" or "waiting" ? 30 : 200))
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

            _hospital.Camera.SnapTo(IsoDraw.ToScreen(fx, fy) + new Vector2(0, -18), scene.Zoom);
            clock.SetSpeed(SimulationSpeed.Normal);
            (_captured, _settle) = (0, 0);
            return;
        }

        // Let the camera settle for a few frames, then save one frame each frame.
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

    private static void Report(string what, BuildCheck check) =>
        GD.Print(check.CanBuild ? $"[anim] Built {what}." : $"[anim] {what} refused: {string.Join("; ", check.Problems)}");
}
