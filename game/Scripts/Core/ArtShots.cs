using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Art review shots, run with: godot --path game -- --art-shots=DIR (needs a display, not --headless).
/// Starts a new game, lets patients fill the beds, then photographs the department, a bed with
/// its monitor and a room corner from all four view angles, with the interface hidden, and quits.
/// </summary>
public partial class ArtShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _shot;
    private HospitalScene? _hospital;

    public static void Begin(Node menu)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--art-shots=", StringComparison.Ordinal))
            {
                var shots = new ArtShots { Name = "ArtShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--art-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Art Review General")).CallDeferred();
                return;
            }
        }
    }

    public override void _Process(double delta)
    {
        _frame++;
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

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-wing") >= 0)
                {
                    BuildShowroom(h);
                }

                if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--art-rooms=", StringComparison.Ordinal)) is { } list)
                {
                    BuildRooms(h, list["--art-rooms=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries));
                }

                // Bring in a few patients so there are people in the beds to look at.
                for (var i = 0; i < 6; i++)
                {
                    h.Session.Simulation.SpawnPatient(i % 2 == 0 ? ArrivalMethod.WalkIn : ArrivalMethod.Ambulance);
                }

                var clock = h.Session.Clock;
                clock.SetSpeed(SimulationSpeed.Paused);
                for (var i = 0; i < 2400 && h.Session.Simulation.Spaces.Count(s => s.IsTreatmentSpace && !s.IsFree) < 4; i++)
                {
                    clock.Step();
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-corridor") >= 0)
                {
                    // Busy night: more ambulances than cubicles, so the overflow goes into corridor care.
                    _corridor = true;
                    var sim = h.Session.Simulation;
                    for (var i = 0; i < 12; i++)
                    {
                        sim.SpawnPatient(ArrivalMethod.Ambulance);
                    }

                    for (var i = 0; i < 20000 && sim.CorridorPatients.Count(p => p.Triage.HasValue && p.CorridorSpot == p.Tile) < 5; i++)
                    {
                        clock.Step();
                    }

                    GD.Print($"[art] Corridor patients: {sim.CorridorPatients.Count()}, spots: {string.Join(" ", sim.CorridorCareSpots())}");
                }

                _frame = 0;
            }

            return;
        }

        // Each shot: frame the view, wait a few frames so it settles, then save it.
        const int settle = 6;
        if (_frame % settle != 0)
        {
            return;
        }

        var view = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--art-view=", StringComparison.Ordinal));
        var shots = view is not null
            ? new (string Name, Func<HospitalScene, (Vector2 At, float Zoom)> Frame)[]
            {
                ("view", _ =>
                {
                    var v = view["--art-view=".Length..].Split(',').Select(t => double.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    return (IsoDraw.ToScreen(v[0], v[1]), (float)v[2]);
                }),
            }
            : _corridor
            ? new (string Name, Func<HospitalScene, (Vector2 At, float Zoom)> Frame)[]
            {
                ("corridor", CorridorView),
                ("corridor_close", h => CorridorCloseUp(h, 0)),
                ("corridor_close_b", h => CorridorCloseUp(h, 2)),
                ("corridor_close_c", h => CorridorCloseUp(h, 4)),
            }
            : _rooms.Length > 0
            ? _rooms.Select(r => (Name: r.Name, Frame: (Func<HospitalScene, (Vector2 At, float Zoom)>)(_ => (IsoDraw.ToScreen(r.X, r.Y) + new Vector2(0, -24), 1.75f)))).ToArray()
            : _wing
            ? new (string Name, Func<HospitalScene, (Vector2 At, float Zoom)> Frame)[]
            {
                ("wing", _ => (IsoDraw.ToScreen(70, 16), 0.62f)),
                ("wing_west", _ => (IsoDraw.ToScreen(52, 16), 1.25f)),
                ("wing_east", _ => (IsoDraw.ToScreen(88, 16), 1.25f)),
                ("clinic", _ => (IsoDraw.ToScreen(60, 7), 2.0f)),
                ("rooms", _ => (IsoDraw.ToScreen(72, 22), 1.6f)),
            }
            : new (string Name, Func<HospitalScene, (Vector2 At, float Zoom)> Frame)[]
            {
                ("overview", Overview),
                ("bed", BedCloseUp),
                ("corner", Corner),
                ("triage", TriageDoor),
            };

        var rotation = _shot / (shots.Length * 2);
        var index = (_shot / 2) % shots.Length;
        if (rotation >= 4)
        {
            GD.Print("[art] Done.");
            GetTree().Quit(0);
            return;
        }

        if (_shot % 2 == 0)
        {
            if (index == 0 && IsoDraw.Rotation != rotation)
            {
                _hospital.RotateView(rotation - IsoDraw.Rotation);
            }

            var (at, zoom) = shots[index].Frame(_hospital);
            _hospital.Camera.SnapTo(at, zoom);
        }
        else
        {
            var image = GetViewport().GetTexture().GetImage();
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            var path = $"{_directory}/{shots[index].Name}_{rotation}.png";
            image.SavePng(path);
            GD.Print($"[art] Saved {path}");
        }

        _shot++;
    }

    private bool _wing;

    private bool _corridor;

    private static (Vector2, float) CorridorView(HospitalScene h)
    {
        var spots = h.Session.Simulation.CorridorPatients.Select(p => p.CorridorSpot!.Value).ToList();
        var (x, y) = (spots.Average(s => s.X), spots.Average(s => s.Y));
        return (IsoDraw.ToScreen(x, y), 0.9f);
    }

    private static (Vector2, float) CorridorCloseUp(HospitalScene h, int index)
    {
        var spots = h.Session.Simulation.CorridorPatients.Select(p => p.CorridorSpot!.Value).OrderBy(s => s.X).ThenBy(s => s.Y).ToList();
        var spot = spots[Math.Min(index, spots.Count - 1)];
        return (IsoDraw.ToScreen(spot.X + 0.5, spot.Y + 0.5) + new Vector2(0, -20), 2.4f);
    }

    /// <summary>Builds a wing of clinics and specialty rooms north of the A&amp;E, to review their decorations.</summary>
    private void BuildShowroom(HospitalScene h)
    {
        _wing = true;
        var sim = h.Session.Simulation;
        sim.AdjustBalance(10_000_000);
        foreach (var dept in new[] { "cardiology", "neurology", "gastroenterology", "gynaecology", "trauma_orthopaedics", "outpatients", "radiology", "pharmacy", "theatres" })
        {
            sim.UnlockDepartment(dept);
        }

        Report("extension", sim.ExtendBuilding(new Simulation.World.GridRect(40, 2, 60, 28)));
        Report("link", sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(40, 13, 4, 34), Simulation.World.WallSide.South)));
        Report("corridor", sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(44, 13, 56, 5), Simulation.World.WallSide.South)));
        var north = new[] { "fracture_clinic", "cardiology_clinic", "neurology_clinic", "gastro_clinic", "gynaecology_clinic" };
        var south = new[] { "outpatient_reception", "xray_room", "plaster_room", "physio_gym", "endoscopy_room" };
        for (var i = 0; i < 5; i++)
        {
            Report(north[i], sim.BuildRoom(sim.AutoPlan(north[i], new Simulation.World.GridRect(44 + (i * 11), 2, 11, 11))));
            Report(south[i], sim.BuildRoom(sim.AutoPlan(south[i], new Simulation.World.GridRect(44 + (i * 11), 18, 11, 12))));
        }
    }

    private (string Name, double X, double Y)[] _rooms = Array.Empty<(string, double, double)>();

    /// <summary>
    /// Builds any ten room templates (--art-rooms=a,b,c) in two rows either side of a corridor,
    /// and photographs each one close up, to review furniture and signs room by room.
    /// </summary>
    private void BuildRooms(HospitalScene h, string[] templates)
    {
        var sim = h.Session.Simulation;
        sim.AdjustBalance(10_000_000);
        foreach (var dept in sim.Database.Departments.Keys)
        {
            sim.UnlockDepartment(dept);
        }

        Report("extension", sim.ExtendBuilding(new Simulation.World.GridRect(40, 2, 60, 28)));
        Report("link", sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(40, 13, 4, 34), Simulation.World.WallSide.South)));
        Report("corridor", sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(44, 13, 56, 5), Simulation.World.WallSide.South)));
        var shots = new System.Collections.Generic.List<(string, double, double)>();
        for (var i = 0; i < templates.Length && i < 10; i++)
        {
            var plot = i < 5 ? new Simulation.World.GridRect(44 + (i * 11), 2, 11, 11) : new Simulation.World.GridRect(44 + ((i - 5) * 11), 18, 11, 12);
            // Some rooms have a largest size: try the whole plot, then smaller rooms on the corridor side of it.
            foreach (var size in new[] { 11, 8, 6, 5 })
            {
                var length = Math.Min(size, plot.Length);
                var rect = new Simulation.World.GridRect(plot.X + ((plot.Width - size) / 2), i < 5 ? plot.Bottom - length : plot.Y, size, length);
                var before = sim.Spaces.Count();
                var check = sim.BuildRoom(sim.AutoPlan(templates[i], size == 11 ? plot : rect));
                if (check.CanBuild || size == 5)
                {
                    Report(templates[i], check);
                    var built = sim.Spaces.Count() > before ? sim.Spaces.Last().Rect : plot;
                    shots.Add((templates.Count(t => t == templates[i]) > 1 ? $"{templates[i]}{i}" : templates[i], built.X + (built.Width / 2.0), built.Y + (built.Length / 2.0)));
                    break;
                }
            }
        }

        _rooms = shots.ToArray();
    }

    private static void Report(string what, Simulation.Simulation.BuildCheck check) =>
        GD.Print(check.CanBuild ? $"[art] Built {what}." : $"[art] {what} refused: {string.Join("; ", check.Problems)}");

    private static (Vector2, float) Overview(HospitalScene h)
    {
        var rooms = h.Session.Simulation.Spaces.Where(s => s.HasWalls).ToList();
        var (x0, y0) = (rooms.Min(r => r.Rect.X), rooms.Min(r => r.Rect.Y));
        var (x1, y1) = (rooms.Max(r => r.Rect.Right), rooms.Max(r => r.Rect.Bottom));
        return (IsoDraw.ToScreen((x0 + x1) / 2.0, (y0 + y1) / 2.0), 0.55f);
    }

    private static (Vector2, float) BedCloseUp(HospitalScene h)
    {
        var bay = h.Session.Simulation.Spaces.Where(s => s.IsTreatmentSpace).OrderBy(s => s.IsFree).ThenBy(s => s.Id).First();
        var spot = bay.PatientSpot;
        return (IsoDraw.ToScreen(spot.X + 0.5, spot.Y + 0.5) + new Vector2(0, -30), 2.2f);
    }

    private static (Vector2, float) TriageDoor(HospitalScene h)
    {
        var room = h.Session.Simulation.Spaces.First(s => s.Kind == CareSpaceKind.Triage);
        var r = room.Rect;
        return (IsoDraw.ToScreen(r.X + (r.Width / 2.0), r.Y + (r.Length / 2.0)) + new Vector2(0, 20), 1.5f);
    }

    private static (Vector2, float) Corner(HospitalScene h)
    {
        var room = h.Session.Simulation.Spaces.Where(s => s.HasWalls).OrderByDescending(s => s.Rect.Width * s.Rect.Length).First();
        var r = room.Rect;
        return (IsoDraw.ToScreen(r.Right, r.Bottom) + new Vector2(0, -20), 1.6f);
    }
}
