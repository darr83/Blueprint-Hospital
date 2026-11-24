using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Art review shots, run with: godot --path game -- --art-shots=DIR [--3d] (needs a display, not --headless).
/// Starts a new game, lets patients fill the beds, then photographs the department, a bed with
/// its monitor and a room corner from all four view angles, with the interface hidden, and quits.
/// </summary>
public partial class ArtShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _shot;
    private HospitalScene? _hospital;

    /// <summary>How much closer the room shots are (--art-zoom=N, default 1).</summary>
    private static float Zoom => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--art-zoom=", StringComparison.Ordinal)) is { } z
        && float.TryParse(z["--art-zoom=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1f;

    public static void Begin(Node menu)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--art-shots=", StringComparison.Ordinal))
            {
                var shots = new ArtShots { Name = "ArtShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--art-shots=".Length..] };
                // The view is chosen on the main menu; --3d takes these shots in 3D instead of 2D.
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
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


                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-plain-light") >= 0)
                {
                    // For before/after shots of the lighting setting (not saved).
                    GameManager.Instance.Settings.EnhancedLighting = false;
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-no-surfaces") >= 0)
                {
                    // For before/after shots of surface textures (not saved).
                    GameManager.Instance.Settings.SurfaceTextures = false;
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-outlines") >= 0)
                {
                    // For shots with the outline pass on (not saved).
                    GameManager.Instance.Settings.Outlines = true;
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-cutaway") >= 0)
                {
                    h.ToggleWalls();
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-plan") >= 0)
                {
                    h.SetPlanView(true);
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-wing") >= 0)
                {
                    BuildShowroom(h);
                }

                if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--art-rooms=", StringComparison.Ordinal)) is { } list)
                {
                    BuildRooms(h, list["--art-rooms=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries));
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-chair-styles") >= 0)
                {
                    BuildChairStyles(h);
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-decor") >= 0)
                {
                    BuildDecor(h);
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-minors") >= 0)
                {
                    BuildMinorsRoom(h);
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

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-ambulances") >= 0)
                {
                    // A busy run of ambulances: every bay taken and more queueing on the road.
                    var sim = h.Session.Simulation;
                    for (var i = 0; i < 6; i++)
                    {
                        sim.SpawnPatient(ArrivalMethod.Ambulance);
                    }

                    for (var i = 0; i < 4000 && sim.Ambulances.Count(a => a.State == Simulation.Transport.AmbulanceState.AtBay) < 4; i++)
                    {
                        clock.Step();
                    }

                    GD.Print($"[art] Ambulances: {string.Join(", ", sim.Ambulances.Select(a => $"{a.CallSign} {a.State} {a.X:0.0},{a.Y:0.0}"))}");
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-cars") >= 0)
                {
                    // A busy front of house: cars in the car park, one arriving, and taxis at the rank.
                    var sim = h.Session.Simulation;
                    for (var i = 0; i < 5; i++)
                    {
                        sim.SpawnCarArrival(sim.GenerateWalkIn(), Simulation.Transport.CarTrip.Parks);
                        for (var t = 0; t < 40; t++)
                        {
                            clock.Step();
                        }
                    }

                    for (var i = 0; i < 4000 && sim.Cars.Any(c => c.State != Simulation.Transport.CarState.Parked); i++)
                    {
                        clock.Step();
                    }

                    sim.SpawnCarArrival(sim.GenerateWalkIn(), Simulation.Transport.CarTrip.Taxi);
                    sim.SpawnCarArrival(sim.GenerateWalkIn(), Simulation.Transport.CarTrip.DropOff);
                    for (var i = 0; i < 4000 && sim.Cars.Any(c => c.State != Simulation.Transport.CarState.Parked); i++)
                    {
                        clock.Step();
                    }

                    sim.SpawnCarArrival(sim.GenerateWalkIn(), Simulation.Transport.CarTrip.Parks);
                    for (var i = 0; i < 12; i++)
                    {
                        clock.Step();
                    }

                    GD.Print($"[art] Cars: {string.Join(", ", sim.Cars.Select(c => $"{c.Trip} {c.State} {c.X:0.0},{c.Y:0.0}"))}");
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

                if (_minors is { } minors)
                {
                    // Fill the minors room's beds.
                    var sim = h.Session.Simulation;
                    for (var i = 0; i < 10; i++)
                    {
                        sim.SpawnPatient(ArrivalMethod.WalkIn);
                    }

                    for (var i = 0; i < 30000 && sim.Patients.Count(p => p.CareSpaceId == minors.Id && sim.BedOf(p) is { } bed && p.Tile == bed.PatientSpot && !p.IsMoving) < 4; i++)
                    {
                        clock.Step();
                    }

                    GD.Print($"[art] Minors room: {minors.PlacesUsed} of {minors.Beds.Count} beds in use");
                }

                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-doctor") >= 0)
                {
                    // Be A Doctor: the view follows the first doctor on shift, with their panel up.
                    var doctor = h.Session.Simulation.PlayableDoctors.First();
                    GD.Print($"[art] Playing {doctor.Name}: {h.EnterDoctorMode(doctor)}");
                    if (h.Session.Simulation.Patients.FirstOrDefault(p => p.IsInDepartment && p.IsInEmergency) is { } patient)
                    {
                        h.DoctorHud.OpenCard(patient);
                    }
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
            : _decor
            ? new (string Name, Func<HospitalScene, (Vector2 At, float Zoom)> Frame)[]
            {
                ("decor_corridor", h => { DecorUi(h, null); return (IsoDraw.ToScreen(55, 15), 1.9f); }),
                ("open_area", h => { DecorUi(h, null); return (IsoDraw.ToScreen(83, 15), 0.95f); }),
                ("lounge", h => { DecorUi(h, null); return (IsoDraw.ToScreen(76, 9), 2.3f); }),
                ("cafe", h => { DecorUi(h, null); return (IsoDraw.ToScreen(89, 9), 2.0f); }),
                ("decor_preview", h => { DecorUi(h, "preview"); return (IsoDraw.ToScreen(88, 19), 2.0f); }),
                ("decor_bar", h => { DecorUi(h, "bar"); return (IsoDraw.ToScreen(83, 15), 0.95f); }),
                ("decor_panel", h => { DecorUi(h, "panel"); return (IsoDraw.ToScreen(55, 15), 1.9f); }),
            }
            : _chairStyles
            ? new (string Name, Func<HospitalScene, (Vector2 At, float Zoom)> Frame)[]
            {
                ("chair_styles", h => { Preview(h, false); return (IsoDraw.ToScreen(60, 15), 1.9f); }),
                ("chair_preview", h => { Preview(h, true); return (IsoDraw.ToScreen(60, 15), 1.9f); }),
                ("room_colours", h => { Preview(h, false); return (IsoDraw.ToScreen(55, 11), 1.4f); }),
                ("room_panel", h => { Preview(h, false, panel: true); return (IsoDraw.ToScreen(55, 11), 1.4f); }),
            }
            : _minors is { } room
            ? new (string Name, Func<HospitalScene, (Vector2 At, float Zoom)> Frame)[]
            {
                ("minors_room", _ => (IsoDraw.ToScreen(room.Rect.X + (room.Rect.Width / 2.0), room.Rect.Y + (room.Rect.Length / 2.0)), 1.3f)),
                ("minors_room_close", _ => (IsoDraw.ToScreen(room.Rect.X + 8, room.Rect.Y + 4), 2.4f)),
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
            ? _rooms.Select(r => (Name: r.Name, Frame: (Func<HospitalScene, (Vector2 At, float Zoom)>)(_ => (IsoDraw.ToScreen(r.X, r.Y) + new Vector2(0, -24), 1.75f * Zoom)))).ToArray()
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

    private bool _chairStyles;

    private bool _decor;

    /// <summary>
    /// Review: the test wing with a corridor dressed with decorations, opening into an open area with a lounge
    /// corner, cafe tables, planters and a row of each chair style.
    /// </summary>
    private void BuildDecor(HospitalScene h)
    {
        _decor = true;
        var sim = h.Session.Simulation;
        sim.AdjustBalance(10_000_000);
        foreach (var dept in new[] { "outpatients", "cardiology", "neurology", "radiology" })
        {
            sim.UnlockDepartment(dept);
        }

        Simulation.World.GridRect R(int x, int y, int w, int l) => new(x, y, w, l);
        Report("extension", sim.ExtendBuilding(R(40, 2, 60, 28)));
        Report("link", sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", R(40, 13, 4, 34), Simulation.World.WallSide.South)));
        Report("corridor", sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", R(44, 13, 22, 5), Simulation.World.WallSide.South)));
        Report("open area", sim.BuildRoom(new Simulation.Simulation.RoomPlan(DecorItems.OpenAreaTemplate, R(66, 2, 34, 28), Simulation.World.WallSide.South)));
        Report("north 1", sim.BuildRoom(sim.AutoPlan("cardiology_clinic", R(44, 2, 11, 11))));
        Report("north 2", sim.BuildRoom(sim.AutoPlan("neurology_clinic", R(55, 2, 11, 11))));
        Report("south 1", sim.BuildRoom(sim.AutoPlan("outpatient_reception", R(44, 18, 11, 12))));
        Report("south 2", sim.BuildRoom(sim.AutoPlan("xray_room", R(55, 18, 11, 12))));

        var (n, s, e, w) = (Simulation.World.WallSide.North, Simulation.World.WallSide.South, Simulation.World.WallSide.East, Simulation.World.WallSide.West);

        // Places an item so its middle is as near (x, y) as its tiles allow.
        void D(string id, double x, double y, Simulation.World.WallSide facing, string? colour = null)
        {
            var item = DecorItems.Find(id)!;
            var best = new Simulation.World.GridCoord((int)x, (int)y);
            var bestGap = double.MaxValue;
            for (var tx = (int)x - 4; tx <= (int)x + 4; tx++)
            {
                for (var ty = (int)y - 4; ty <= (int)y + 4; ty++)
                {
                    var (cx, cy) = DecorItems.Centre(item, new Simulation.World.GridCoord(tx, ty), facing);
                    var gap = Math.Abs(cx - x) + Math.Abs(cy - y);
                    if (gap < bestGap - 1e-6)
                    {
                        (best, bestGap) = (new Simulation.World.GridCoord(tx, ty), gap);
                    }
                }
            }

            Report($"{id} at {best}", sim.PlaceDecoration(id, best, facing, colour));
        }

        // The corridor: gel, a bench, a plant, a water cooler and leaflets along the clinic fronts; a fish tank,
        // bins, a palm and another bench opposite.
        D("gel_station", 44.5, 13.5, s);
        D("bench", 53.0, 13.5, s);
        D("plant", 45.5, 13.5, s);
        D("water_cooler", 49.5, 13.5, s);
        D("leaflet_rack", 63.5, 13.5, s);
        D("fish_tank", 49.5, 17.5, n);
        D("bins", 53.0, 17.5, n);
        D("bench", 59.5, 17.5, n);
        D("palm", 64.0, 17.0, n);

        // The open area: a lounge corner on a rug, cafe tables, bookcases, palms and planters; pictures, a screen
        // and a clock on the outside walls.
        D("rug", 75.0, 9.5, s, "#4F7FB0");
        D("sofa", 75.0, 7.0, s, "#2A9187");
        D("armchair", 71.5, 10.0, e, "#E07A32");
        D("armchair", 78.5, 10.0, w, "#E07A32");
        D("coffee_table", 75.0, 10.5, s);
        D("bookcase", 70.0, 2.5, s);
        D("bookcase", 72.0, 2.5, s);
        D("palm", 67.0, 4.0, s);
        D("palm", 99.0, 3.0, s);
        D("palm", 99.0, 29.0, n);
        D("floor_lamp", 79.5, 6.5, s);
        foreach (var (x, y) in new[] { (85.5, 5.5), (91.5, 5.5), (85.5, 10.5), (91.5, 10.5) })
        {
            D("cafe_table", x, y, s, "#D6A72C");
        }

        D("planter", 70.5, 15.5, s);
        D("planter", 80.5, 15.5, s);
        D("sculpture", 91.5, 18.5, s, "#4F7FB0");
        D("notice_stand", 98.0, 13.5, w);
        D("water_cooler", 99.5, 9.5, w);
        D("bins", 99.5, 16.0, w);
        D("picture", 83.0, 2.5, s);
        D("tv", 89.0, 2.5, s);
        D("clock", 99.5, 21.5, w);
        D("notice_board", 99.5, 25.5, w);

        // A row of each chair style.
        var colours = new[] { "#4F7FB0", "#2A9187", "#7E6BB5", "#4A5058" };
        for (var r = 0; r < ChairStyles.All.Count; r++)
        {
            for (var i = 0; i < 5; i++)
            {
                Report($"chair {ChairStyles.All[r].ItemId}", sim.PlaceChair(new Simulation.World.GridCoord(70 + i + (r * 7), 25), s, colours[r], ChairStyles.All[r].ItemId));
            }
        }
    }

    /// <summary>For the decoration shots: nothing, a sofa being placed, the Decorations pop-up, or the fish tank's panel.</summary>
    private static void DecorUi(HospitalScene h, string? what)
    {
        if (h.BuildTool.Active)
        {
            h.BuildTool.Cancel();
        }

        h.Hud.Visible = what is "bar" or "panel";
        h.Hud.ItemInfo.Close();
        if (what is "bar" or "panel" or "preview")
        {
            h.SetMode(GameMode.Build);
        }
        else
        {
            h.SetMode(GameMode.Play);
        }

        switch (what)
        {
            case "preview":
                h.BuildTool.StartDecoration("sofa");
                h.BuildTool.DecorColour = "#A23B4C";
                h.BuildTool.PreviewAt(new Simulation.World.GridCoord(86, 20));
                break;
            case "bar":
                h.Hud.Build.Open(Simulation.Data.BuildMenu.Decorations);
                h.Hud.Build.Choose(DecorItems.TilePrefix + "palm");
                h.BuildTool.PreviewAt(new Simulation.World.GridCoord(86, 20));
                break;
            case "panel" when h.Session.Simulation.PlacedDecorations.FirstOrDefault(d => d.ItemId == "fish_tank") is { } tank
                && h.Session.Simulation.Spaces.FirstOrDefault(sp => sp.Rect.Contains(tank.Tile)) is { } home:
                h.Hud.ItemInfo.ShowItem(home, new PlacedFurniture($"{HospitalScene.DecorationItemPrefix}{tank.Id}", tank.Tile.X, tank.Tile.Y, 1, 1));
                break;
        }
    }

    /// <summary>Review: a corridor with a row of each chair style, clinics in picked colour schemes.</summary>
    private void BuildChairStyles(HospitalScene h)
    {
        _chairStyles = true;
        var sim = h.Session.Simulation;
        sim.AdjustBalance(10_000_000);
        foreach (var dept in new[] { "outpatients", "cardiology", "neurology", "gastroenterology" })
        {
            sim.UnlockDepartment(dept);
        }

        Report("extension", sim.ExtendBuilding(new Simulation.World.GridRect(40, 2, 60, 28)));
        Report("link", sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(40, 13, 4, 34), Simulation.World.WallSide.South)));
        Report("corridor", sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(44, 13, 44, 6), Simulation.World.WallSide.South)));
        var north = new[] { "cardiology_clinic", "neurology_clinic", "gastro_clinic", "medical_clinic" };
        for (var i = 0; i < north.Length; i++)
        {
            Report(north[i], sim.BuildRoom(sim.AutoPlan(north[i], new Simulation.World.GridRect(44 + (i * 11), 2, 11, 11))));
        }

        foreach (var (prefix, hex) in new[] { ("Cardiology", "#00897B"), ("Neurology", "#F9A825"), ("Gastro", "#D46A9F") })
        {
            if (sim.Spaces.FirstOrDefault(s => s.Name.StartsWith(prefix, StringComparison.Ordinal)) is { } room)
            {
                sim.SetRoomColour(room.Id, hex);
            }
        }

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--art-chair-check") >= 0)
        {
            for (var x = 60; x < 64; x++)
            {
                sim.PlaceChair(new Simulation.World.GridCoord(x, 13), Simulation.World.WallSide.South, "#E07A32", "lounge_chair");
            }
        }

        var colours = new[] { "#4F7FB0", "#2A9187", "#7E6BB5", "#4A5058" };
        for (var s = 0; s < ChairStyles.All.Count; s++)
        {
            for (var i = 0; i < 5; i++)
            {
                Report($"chair {ChairStyles.All[s].ItemId}", sim.PlaceChair(new Simulation.World.GridCoord(46 + (s * 7) + i, 18), Simulation.World.WallSide.North, colours[s], ChairStyles.All[s].ItemId));
            }
        }
    }

    /// <summary>Shows (or hides) a row of see-through padded chairs being placed, and the room panel.</summary>
    private static void Preview(HospitalScene h, bool chairs, bool panel = false)
    {
        if (chairs)
        {
            h.BuildTool.StartChairs("#E07A32");
            h.BuildTool.ChairStyle = "lounge_chair";
            h.BuildTool.FaceChairs(Simulation.World.WallSide.South);
            h.BuildTool.PreviewDrag(new Simulation.World.GridCoord(52, 13), new Simulation.World.GridCoord(58, 13));
        }
        else if (h.BuildTool.Active)
        {
            h.BuildTool.Cancel();
        }

        h.Hud.Visible = panel;
        if (panel && h.Session.Simulation.Spaces.FirstOrDefault(s => s.Name.StartsWith("Cardiology", StringComparison.Ordinal)) is { } room)
        {
            h.Hud.RoomInfo.Show(room);
        }
        else
        {
            h.Hud.RoomInfo.Close();
        }
    }

    private bool _corridor;

    private CareSpace? _minors;

    /// <summary>Swaps A&amp;E's minors cubicles for a big minors room in a new wing, to review its beds and curtains.</summary>
    private void BuildMinorsRoom(HospitalScene h)
    {
        var sim = h.Session.Simulation;
        sim.AdjustBalance(10_000_000);
        Report("extension", sim.ExtendBuilding(new Simulation.World.GridRect(40, 2, 60, 28)));
        Report("link", sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(40, 13, 4, 34), Simulation.World.WallSide.South)));
        Report("corridor", sim.BuildRoom(new Simulation.Simulation.RoomPlan("corridor", new Simulation.World.GridRect(44, 13, 56, 5), Simulation.World.WallSide.South)));
        Report("minors room", sim.BuildRoom(sim.AutoPlan("minors_room", new Simulation.World.GridRect(44, 2, 26, 11))));
        foreach (var cubicle in sim.Spaces.Where(s => s.Kind == CareSpaceKind.Minors && !s.HasBeds).ToList())
        {
            Report(cubicle.Name, sim.DemolishRoom(cubicle.Id));
        }

        _minors = sim.Spaces.FirstOrDefault(s => s.TemplateId == "minors_room");
    }

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
