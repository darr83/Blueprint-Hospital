using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// The ready-made example hospital offered in Load Game: the building extended to 47 × 33 m on three floors,
/// every department open with every kind of room, and staffed to the recommended numbers. It is built with the
/// same rules and checks the player builds with, so it is an ordinary game once loaded.
/// <para>
/// Every floor shares one plan of corridors: a 4 m spine running east from the ambulance doors, a corridor along
/// the north rooms, and links at the ends; two bed lifts and a staircase stand between them. The ground floor
/// adds a corridor along the front, behind the main waiting room. All coordinates are site tiles (0.5 m).
/// </para>
/// </summary>
public static class ExampleHospital
{
    public const string HospitalName = "Example Hospital";

    /// <summary>The ready-built save shipped with the game, in Data/Examples (rebuilt by the SimulationRunner's --example-hospital).</summary>
    public const string FileName = "example_hospital.json";

    /// <summary>Money left in the bank once it is built.</summary>
    public const long OpeningBalance = 2_000_000;

    /// <summary>Game time it runs for before it is handed over, so staff are at their posts and patients are in.</summary>
    public static readonly TimeSpan WarmUp = TimeSpan.FromHours(3);

    /// <summary>The building after its extension: x 6–100, y 4–70.</summary>
    public static readonly GridRect Footprint = new(6, 4, 94, 66);

    // Rows (y) shared by every floor.
    private const int NorthRowY = 4, NorthRowDepth = 16;     // 8 m: wards, theatres, scanners
    private const int NorthCorridorY = 20, NorthCorridorDepth = 5;
    private const int MiddleRowY = 25, MiddleRowDepth = 14;  // 7 m, doors onto the spine
    private const int SpineY = 39, SpineDepth = 8;            // level with the ambulance doors
    private const int SouthY = 47;                            // below the spine

    private sealed record Room(string Template, int X, int Y, int Width, int Depth, WallSide Door, string? Name = null);

    /// <summary>Builds the example as a save, ready to load.</summary>
    public static SaveGame CreateSave(GameDatabase database, int seed = 20271104)
    {
        var events = new EventBus();
        var clock = new SimulationClock(events, new DateTime(2027, 1, 4, 8, 0, 0));
        var site = StarterSite.Create(SitePlan.Standard);
        var sim = Build(database, clock, events, site, seed);
        clock.Ticked += _ => sim.Tick();
        var ticks = (long)(WarmUp.TotalSeconds / SimulationClock.TickLength.TotalSeconds);
        for (var i = 0; i < ticks; i++)
        {
            clock.Step();
        }

        return new SaveGame
        {
            HospitalName = HospitalName,
            SavedAtUtc = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc),
            Clock = clock.CaptureState(),
            // Looking at the middle of the building, a little further out than a new game.
            Camera = new CameraState(32 * (53 - 37), 16 * (53 + 37), 0.35),
            SitePlan = site.Layout?.Plan,
            Layout = sim.CaptureLayout(),
            People = sim.CapturePeople(),
        };
    }

    /// <summary>Builds and staffs the example hospital on a new empty-building sandbox game.</summary>
    public static HospitalSimulation Build(GameDatabase database, SimulationClock clock, EventBus events, SiteMap site, int seed = 20271104)
    {
        var sim = new HospitalSimulation(database, clock, events, site, new SimulationOptions
        {
            Seed = seed,
            Start = StartingHospital.EmptyBuilding,
            Type = GameType.Sandbox,
            WardEmergencies = WardEmergencyFrequency.Realistic,
        });

        sim.AdjustBalance(500_000_000);
        foreach (var department in database.Departments.Keys.Where(d => !sim.IsUnlocked(d)))
        {
            Must(sim.UnlockDepartment(department), $"unlock {department}");
        }

        Must(sim.ExtendBuilding(Footprint), "extend the building");
        Must(sim.AddFloor(), "add the first floor");
        Must(sim.AddFloor(), "add the second floor");

        // Ground floor corridors: the spine joins the ambulance doors; everything else joins the spine.
        Corridor(sim, 0, 6, SpineY, 94, SpineDepth);
        Corridor(sim, 0, 6, NorthCorridorY, 5, 42);     // west link, down to the front corridor
        Corridor(sim, 0, 6, NorthCorridorY, 94, NorthCorridorDepth);
        Corridor(sim, 0, 95, NorthCorridorY, 5, SpineY - NorthCorridorY); // east link
        Corridor(sim, 0, 62, SouthY, 8, 10);            // central link to the front corridor
        Corridor(sim, 0, 6, 57, 94, 5);                 // front corridor

        sim.BuildFittedWaitingRoom(new GridRect(52, 62, 28, 8), WallSide.North, "example_waiting_room", "Main waiting room");

        // Lifts and stairs go through every floor, doors onto the spine.
        Build(sim, new Room("lift", 11, 29, 7, 10, WallSide.South, "West bed lifts"));
        Build(sim, new Room("stairs", 18, MiddleRowY, 6, MiddleRowDepth, WallSide.South, "Main stairs"));
        Build(sim, new Room("lift", 88, 29, 7, 10, WallSide.South, "East bed lifts"));

        for (var floor = 1; floor < 3; floor++)
        {
            Corridor(sim, floor, 6, SpineY, 94, SpineDepth);
            Corridor(sim, floor, 6, NorthCorridorY, 5, SpineY - NorthCorridorY);
            Corridor(sim, floor, 6, NorthCorridorY, 94, NorthCorridorDepth);
            Corridor(sim, floor, 95, NorthCorridorY, 5, SpineY - NorthCorridorY);
        }

        // Landings in front of the lifts' back walls, so the north corridor runs round them.
        for (var floor = 0; floor < 3; floor++)
        {
            Corridor(sim, floor, 11, MiddleRowY, 7, 4);
            Corridor(sim, floor, 88, MiddleRowY, 7, 4);
        }

        foreach (var room in GroundFloor)
        {
            Build(sim, room);
        }

        foreach (var room in FirstFloor)
        {
            Build(sim, room with { Y = room.Y + site.FloorStride });
        }

        foreach (var room in SecondFloor)
        {
            Build(sim, room with { Y = room.Y + (2 * site.FloorStride) });
        }

        // Every theatre takes every surgical specialty (the caesarean theatre is maternity's own).
        foreach (var theatre in sim.Spaces.Where(s => s.Kind == CareSpaceKind.Theatre && sim.FixedTheatreDepartments(s).Count == 0))
        {
            foreach (var department in sim.SurgicalDepartments)
            {
                sim.SetTheatreAccess(theatre.Id, department.Id, true);
            }
        }

        // Staff every post to the recommended numbers, day and night.
        for (var round = 0; round < 3; round++)
        {
            foreach (var need in sim.RecommendedStaffing().Where(n => n.Short > 0))
            {
                for (var i = 0; i < need.Short; i++)
                {
                    sim.HireStaff(need.Role, need.RoomId, skill: 0.6 + (0.05 * (i % 4)), shift: need.Shift);
                }
            }
        }

        foreach (var member in sim.Staff)
        {
            member.PlaceAt(sim.HomeTile(member));
        }

        sim.AdjustBalance(OpeningBalance - sim.Balance);
        return sim;
    }

    // ------------------------------------------------------------------ the floors

    private static readonly Room[] GroundFloor =
    {
        // North row (8 m), doors onto the north corridor.
        new("emergency_ward", 6, NorthRowY, 20, NorthRowDepth, WallSide.South, "A&E observation ward"),
        new("acute_medical_unit", 26, NorthRowY, 18, NorthRowDepth, WallSide.South),
        new("medical_ward", 44, NorthRowY, 14, NorthRowDepth, WallSide.South),
        new("ct_scanner", 58, NorthRowY, 12, NorthRowDepth, WallSide.South),
        new("xray_room", 70, NorthRowY, 12, NorthRowDepth, WallSide.South),
        new("cafeteria", 82, NorthRowY, 18, NorthRowDepth, WallSide.South, "Hospital café"),

        // Middle row (7 m), doors onto the spine.
        new("resus_bay", 24, MiddleRowY, 12, MiddleRowDepth, WallSide.South, "Resus 1"),
        new("resus_bay", 36, MiddleRowY, 12, MiddleRowDepth, WallSide.South, "Resus 2"),
        new("majors_cubicle", 48, MiddleRowY, 8, MiddleRowDepth, WallSide.South, "Majors 1"),
        new("majors_cubicle", 56, MiddleRowY, 8, MiddleRowDepth, WallSide.South, "Majors 2"),
        new("majors_cubicle", 64, MiddleRowY, 8, MiddleRowDepth, WallSide.South, "Majors 3"),
        new("nurses_station", 72, MiddleRowY, 10, MiddleRowDepth, WallSide.South),
        new("doctors_office", 82, MiddleRowY, 6, MiddleRowDepth, WallSide.South),

        // South of the spine (5 m), doors onto the spine.
        new("triage_room", 11, SouthY, 8, 10, WallSide.North, "Triage 1"),
        new("triage_room", 19, SouthY, 8, 10, WallSide.North, "Triage 2"),
        new("minors_room", 27, SouthY, 20, 10, WallSide.North, "Minors"),
        new("minors_cubicle", 47, SouthY, 8, 10, WallSide.North, "Minors cubicle"),
        new("staff_room", 55, SouthY, 7, 10, WallSide.North),
        new("pharmacy", 70, SouthY, 10, 10, WallSide.North),
        new("plaster_room", 80, SouthY, 10, 10, WallSide.North),
        new("outpatient_reception", 90, SouthY, 10, 10, WallSide.North),

        // Front row (4 m), doors onto the front corridor; the main waiting room sits in the middle.
        new("fracture_clinic", 6, 62, 13, 8, WallSide.North),
        new("medical_clinic", 19, 62, 8, 8, WallSide.North),
        new("surgical_clinic", 27, 62, 8, 8, WallSide.North),
        new("public_toilets", 35, 62, 10, 8, WallSide.North),
        new("accessible_toilet", 45, 62, 7, 8, WallSide.North),
        new("vending_point", 80, 62, 6, 8, WallSide.North),
        new("cleaning_room", 86, 62, 8, 8, WallSide.North),
        new("staff_toilet", 94, 62, 6, 6, WallSide.North),
    };

    private static readonly Room[] FirstFloor =
    {
        // North row: critical care and theatres.
        new("intensive_care_unit", 6, NorthRowY, 23, NorthRowDepth, WallSide.South),
        new("high_dependency_unit", 29, NorthRowY, 21, NorthRowDepth, WallSide.South),
        new("operating_theatre", 50, NorthRowY, 14, NorthRowDepth, WallSide.South, "Theatre 1"),
        new("operating_theatre", 64, NorthRowY, 14, NorthRowDepth, WallSide.South, "Theatre 2"),
        new("doctors_office", 78, NorthRowY, 12, NorthRowDepth, WallSide.South, "Theatre office"),
        new("staff_room", 90, NorthRowY, 10, NorthRowDepth, WallSide.South, "Theatre staff room"),

        // Middle row: surgical admissions, day surgery and endoscopy.
        new("surgical_assessment_unit", 24, MiddleRowY, 30, MiddleRowDepth, WallSide.South),
        new("gastro_clinic", 54, MiddleRowY, 7, MiddleRowDepth, WallSide.South),
        new("endoscopy_room", 61, MiddleRowY, 9, MiddleRowDepth, WallSide.South),
        new("endoscopy_recovery", 70, MiddleRowY, 8, MiddleRowDepth, WallSide.South),
        new("outreach_office", 78, MiddleRowY, 10, MiddleRowDepth, WallSide.South),

        // South of the spine (11.5 m): big wards and the gym.
        new("orthopaedic_ward", 6, SouthY, 26, 23, WallSide.North),
        new("gastro_ward", 32, SouthY, 22, 23, WallSide.North),
        new("day_surgery_unit", 54, SouthY, 16, 23, WallSide.North),
        new("physio_gym", 70, SouthY, 14, 23, WallSide.North),
        new("surgical_ward", 84, SouthY, 16, 23, WallSide.North),
    };

    private static readonly Room[] SecondFloor =
    {
        // North row: maternity.
        new("mother_baby_ward", 6, NorthRowY, 24, NorthRowDepth, WallSide.South),
        new("neonatal_unit", 30, NorthRowY, 18, NorthRowDepth, WallSide.South),
        new("caesarean_theatre", 48, NorthRowY, 14, NorthRowDepth, WallSide.South),
        new("delivery_room", 62, NorthRowY, 12, NorthRowDepth, WallSide.South, "Delivery room 1"),
        new("delivery_room", 74, NorthRowY, 12, NorthRowDepth, WallSide.South, "Delivery room 2"),
        new("ambulatory_care_unit", 86, NorthRowY, 14, NorthRowDepth, WallSide.South),

        // Middle row: clinics and tests.
        new("antenatal_scan_room", 24, MiddleRowY, 8, MiddleRowDepth, WallSide.South),
        new("early_pregnancy_unit", 32, MiddleRowY, 7, MiddleRowDepth, WallSide.South),
        new("gynaecology_clinic", 39, MiddleRowY, 7, MiddleRowDepth, WallSide.South),
        new("gynae_exam_room", 46, MiddleRowY, 7, MiddleRowDepth, WallSide.South),
        new("cardiology_clinic", 53, MiddleRowY, 7, MiddleRowDepth, WallSide.South),
        new("echo_room", 60, MiddleRowY, 7, MiddleRowDepth, WallSide.South),
        new("eeg_room", 67, MiddleRowY, 7, MiddleRowDepth, WallSide.South),
        new("neurology_clinic", 74, MiddleRowY, 7, MiddleRowDepth, WallSide.South),
        new("antenatal_clinic", 81, MiddleRowY, 7, MiddleRowDepth, WallSide.South),

        // South of the spine: cardiology, neurology and gynaecology beds.
        new("cath_lab", 6, SouthY, 19, 23, WallSide.North),
        new("coronary_care_unit", 25, SouthY, 14, 23, WallSide.North),
        new("cardiology_ward", 39, SouthY, 15, 23, WallSide.North),
        new("stroke_unit", 54, SouthY, 15, 23, WallSide.North),
        new("neurology_ward", 69, SouthY, 15, 23, WallSide.North),
        new("gynaecology_ward", 84, SouthY, 16, 23, WallSide.North),
    };

    // ------------------------------------------------------------------ helpers

    private static void Corridor(HospitalSimulation sim, int floor, int x, int y, int width, int length)
    {
        var rect = sim.Site.OnFloor(new GridRect(x, y, width, length), floor);
        Must(sim.BuildRoom(new RoomPlan("corridor", rect, WallSide.South)), $"corridor at {rect} on floor {floor}");
    }

    private static void Build(HospitalSimulation sim, Room room)
    {
        var rect = new GridRect(room.X, room.Y, room.Width, room.Depth);
        var plan = sim.AutoPlan(room.Template, rect, doorSide: room.Door);
        Must(sim.BuildRoom(plan, room.Name ?? sim.Database.RoomTemplates[room.Template].Name), $"{room.Template} at {rect}");
    }

    private static void Must(BuildCheck check, string what)
    {
        if (!check.CanBuild)
        {
            throw new InvalidOperationException($"Example hospital: could not {what}: {string.Join("; ", check.Problems)}");
        }
    }
}
