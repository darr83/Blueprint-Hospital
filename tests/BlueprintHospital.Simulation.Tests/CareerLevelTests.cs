using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Xunit.Abstractions;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>
/// Plays each Career level the way a player would: builds only rooms that level has unlocked (and the
/// ones before it), hires the recommended team, lets patients arrive, and checks every goal of the
/// level is met within a few game days. One class per level so they run side by side.
/// </summary>
public abstract class CareerLevelTest(ITestOutputHelper output)
{
    /// <summary>The longest a level may take in the test, in game days.</summary>
    protected virtual double DaysAllowed => 5;

    protected void Plays(int level)
    {
        var h = new SimulationHarness(seed: 4100 + level, arrivals: true, type: GameType.Career);
        h.Sim.SetLevel(level);
        h.Sim.AdjustBalance(100_000_000);
        var builder = new CareerHospitalBuilder(h);
        builder.BuildUpTo(level);

        var reached = 0;
        h.Events.Subscribe<LevelReached>(e => reached = Math.Max(reached, e.Level.Number));

        // Tops up the team when candidates refresh, like a player using "Hire minimum team".
        var end = h.Clock.Now.AddDays(DaysAllowed);
        var nextHire = h.Clock.Now.AddHours(4);
        while (reached <= level && h.Clock.Now < end)
        {
            h.RunFor(TimeSpan.FromMinutes(30));
            if (h.Clock.Now >= nextHire)
            {
                h.Sim.HireForShortages();
                nextHire = h.Clock.Now.AddHours(4);
            }
        }

        var report = string.Join(Environment.NewLine, h.Sim.Goals.Select(g => $"  {g.Goal.Text}: {g.Detail}"));
        output.WriteLine($"Level {level} after {(h.Clock.Now - (end - TimeSpan.FromDays(DaysAllowed))).TotalHours:0} game hours: {(reached > level ? "complete" : "NOT complete")}");
        output.WriteLine(report);
        output.WriteLine($"  Beds: {string.Join(", ", h.Sim.Wards.GroupBy(w => w.TemplateId).Select(g => $"{g.Key} {g.Sum(w => w.Beds.Count)}"))}");
        output.WriteLine($"  Staff {h.Sim.Staff.Count()}; patients now {h.Sim.Patients.Count()} ({string.Join(", ", h.Sim.Patients.GroupBy(p => p.Stage).Select(g => $"{g.Key} {g.Count()}"))})");
        Assert.True(reached > level, $"Level {level} ({h.Database.Levels[level - 1].Name}) was not completed in {DaysAllowed} game days:{Environment.NewLine}{report}");
    }
}

public sealed class CareerLevel1Test(ITestOutputHelper o) : CareerLevelTest(o)
{
    [Fact]
    public void Level_1_walk_in_centre_can_be_completed() => Plays(1);
}

public sealed class CareerLevel2Test(ITestOutputHelper o) : CareerLevelTest(o)
{
    [Fact]
    public void Level_2_emergency_department_can_be_completed() => Plays(2);
}

public sealed class CareerLevel3Test(ITestOutputHelper o) : CareerLevelTest(o)
{
    [Fact]
    public void Level_3_resus_can_be_completed() => Plays(3);
}

public sealed class CareerLevel4Test(ITestOutputHelper o) : CareerLevelTest(o)
{
    [Fact]
    public void Level_4_acute_medicine_can_be_completed() => Plays(4);
}

public sealed class CareerLevel5Test(ITestOutputHelper o) : CareerLevelTest(o)
{
    [Fact]
    public void Level_5_general_surgery_can_be_completed() => Plays(5);
}

public sealed class CareerLevel6Test(ITestOutputHelper o) : CareerLevelTest(o)
{
    [Fact]
    public void Level_6_outpatients_can_be_completed() => Plays(6);
}

public sealed class CareerLevel7Test(ITestOutputHelper o) : CareerLevelTest(o)
{
    [Fact]
    public void Level_7_critical_care_can_be_completed() => Plays(7);
}

public sealed class CareerLevel8Test(ITestOutputHelper o) : CareerLevelTest(o)
{
    [Fact]
    public void Level_8_bones_and_hearts_can_be_completed() => Plays(8);
}

public sealed class CareerLevel9Test(ITestOutputHelper o) : CareerLevelTest(o)
{
    [Fact]
    public void Level_9_brain_gut_and_womens_health_can_be_completed() => Plays(9);
}

/// <summary>Checks the patients each level sends match what it has unlocked.</summary>
public class CareerArrivalTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Patients_are_no_sicker_or_younger_than_the_level_can_look_after(int level)
    {
        var h = new SimulationHarness(seed: 21 + level, arrivals: true, arrivalMultiplier: 3, type: GameType.Career);
        h.Sim.SetLevel(level);
        h.Sim.AdjustBalance(100_000_000);
        new CareerHospitalBuilder(h).BuildUpTo(level);

        var arrived = new List<Patients.Patient>();
        var triaged = new List<TriageCategory>();
        var stillOnLevel = true;
        h.Events.Subscribe<LevelReached>(_ => stillOnLevel = false);
        h.Events.Subscribe<PatientArrived>(e => { if (stillOnLevel) { arrived.Add(e.Patient); } });
        h.Events.Subscribe<PatientTriaged>(e => { if (stillOnLevel && arrived.Contains(e.Patient)) { triaged.Add(e.Category); } });
        var worst = h.Sim.WorstTriageAtThisLevel;
        h.RunFor(TimeSpan.FromHours(level == 1 ? 6 : 16));

        Assert.True(arrived.Count > 20, $"Only {arrived.Count} patients came.");
        Assert.DoesNotContain(triaged, c => c.IsMoreUrgentThan(worst));
        if (level == 1)
        {
            Assert.Equal(TriageCategory.Green, worst);
            Assert.All(arrived, p => Assert.True(p.Age >= HospitalSimulation.AdultAge, $"{p.FullName} is {p.Age}."));
            Assert.All(arrived, p => Assert.Equal(Patients.ArrivalMethod.WalkIn, p.ArrivalMethod));
        }
        else
        {
            // Few children are well enough for majors (the youngest look red by adult scores), so look for them once resus is open.
            Assert.True(level < 3 || arrived.Any(p => p.Age < HospitalSimulation.AdultAge), "No children came.");
            Assert.True(arrived.Any(p => p.ArrivalMethod == Patients.ArrivalMethod.Ambulance), "No ambulances came.");
            Assert.True(triaged.Contains(worst), $"Nobody was triaged {worst}: {string.Join(", ", triaged)}.");
        }

        // On the walk-in centre nobody is left waiting for a kind of room it can't build.
        Assert.True(level > 1 || !h.Sim.Patients.Any(p => p.Stage == Patients.PatientStage.WaitingForSpace && p.ArrivedAt < h.Clock.Now.AddHours(-4)), "Someone has waited over 4 hours for a cubicle.");
    }

    [Fact]
    public void Only_patients_of_departments_the_player_has_opened_come_in_or_ones_a_and_e_sends_home()
    {
        var h = new SimulationHarness(seed: 5, arrivals: true, arrivalMultiplier: 3, type: GameType.Career);
        h.Sim.SetLevel(4);
        var arrived = new List<Patients.Patient>();
        h.Events.Subscribe<PatientArrived>(e => arrived.Add(e.Patient));
        h.RunFor(TimeSpan.FromHours(10));

        // General Medicine is free and available on level 4, but has no rooms yet.
        Assert.True(h.Sim.IsUnlocked("general_medicine"));
        Assert.False(h.Sim.IsDepartmentOpen("general_medicine"));
        Assert.NotEmpty(arrived);
        Assert.All(arrived, p => Assert.True(p.Condition.Department == "emergency" || p.Condition.Disposition == Disposition.Discharge, p.Condition.Id));
        Assert.Contains(arrived, p => p.Condition.Department != "emergency");
    }
}

/// <summary>
/// Builds a Career hospital level by level, the way a player might: extends the building into a north
/// wing and a west wing with corridors, then fills them with the rooms each level unlocks.
/// </summary>
internal sealed class CareerHospitalBuilder(SimulationHarness h)
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>
    /// A strip of room plots beside a corridor. Rooms fill it one after another along the corridor and
    /// reach back from it as far as the strip allows.
    /// </summary>
    private sealed class Row(GridRect area, WallSide corridor)
    {
        public GridRect Area { get; } = area;

        /// <summary>Which side of the strip the corridor is on.</summary>
        public WallSide Corridor { get; } = corridor;

        /// <summary>Rooms run along X when the corridor is to the north or south, else along Y.</summary>
        public bool AlongX => Corridor is WallSide.North or WallSide.South;

        public int Length => AlongX ? Area.Width : Area.Length;

        public int Depth => AlongX ? Area.Length : Area.Width;

        public int Used { get; set; }

        /// <summary>The plot for the next room, <paramref name="width"/> along the corridor and <paramref name="depth"/> back from it.</summary>
        public GridRect Plot(int width, int depth) => Corridor switch
        {
            WallSide.North => new GridRect(Area.X + Used, Area.Y, width, depth),
            WallSide.South => new GridRect(Area.X + Used, Area.Bottom - depth, width, depth),
            WallSide.West => new GridRect(Area.X, Area.Y + Used, depth, width),
            _ => new GridRect(Area.Right - depth, Area.Y + Used, depth, width),
        };
    }

    private readonly List<Row> _rows = new();

    private HospitalSimulation Sim => h.Sim;

    public void BuildUpTo(int level)
    {
        Wings();
        for (var l = 1; l <= level; l++)
        {
            Level(l);
        }

        foreach (var theatre in Sim.Spaces.Where(s => s.TemplateId == "operating_theatre"))
        {
            foreach (var d in h.Database.Departments.Keys.Where(Sim.IsUnlocked))
            {
                Sim.SetTheatreAccess(theatre.Id, d, true);
            }
        }

        // The minimum team, from Recruitment where possible, then the rest of the recommended team.
        Sim.HireForShortages();
        h.StaffToRecommended();
    }

    private void Level(int level)
    {
        switch (level)
        {
            case 1:
                Room("minors_cubicle", 7);
                Room("minors_cubicle", 7);
                Room("public_toilets", 7);

                // "Hire 2 more staff": two more nurses from Recruitment.
                foreach (var nurse in Sim.Candidates.Where(c => c.Role == Staff.StaffRole.Nurse).Take(2).ToList())
                {
                    Assert.NotNull(Sim.Hire(nurse.Id, null));
                }

                break;
            case 2:
                Room("majors_cubicle", 7);
                Room("majors_cubicle", 7);
                Room("majors_cubicle", 7);
                Room("xray_room", 8);
                Room("emergency_ward", 12);
                Room("accessible_toilet", 5);
                break;
            case 3:
                Room("resus_bay", 10);
                Room("cleaning_room", 4);
                break;
            case 4:
                Ward("acute_medical_unit");
                Ward("medical_ward");
                Room("pharmacy", 6);
                Room("cafeteria", 10);
                break;
            case 5:
                Room("operating_theatre", 12);
                Ward("surgical_assessment_unit");
                Ward("surgical_ward");
                Room("ct_scanner", 9);
                break;
            case 6:
                Room("outpatient_reception", 6);
                Room("medical_clinic", 7);
                Room("surgical_clinic", 7);
                Assert.True(Sim.AddFloor().CanBuild);
                break;
            case 7:
                Ward("intensive_care_unit");
                Ward("high_dependency_unit");
                break;
            case 8:
                Ward("orthopaedic_ward");
                break;
            case 9:
                Ward("neurology_ward");
                Ward("gastro_ward");
                break;
        }
    }

    /// <summary>A north wing and a west wing joined to A&amp;E's corridor, with room plots along each corridor.</summary>
    private void Wings()
    {
        Build(Sim.ExtendBuilding(new GridRect(2, 0, B.Right - 2, B.Y)), "north wing");
        Build(Sim.ExtendBuilding(new GridRect(2, B.Y, B.X - 2, B.Length)), "west wing");

        Corridor(new GridRect(2, B.Y + 9, B.X + 4 - 2, 8));     // A&E's corridor carried west
        Corridor(new GridRect(18, 16, 4, B.Bottom - 16));       // up the west wing

        Corridor(new GridRect(2, 16, B.Right - 2, 3));          // along the north wing

        _rows.Add(new Row(new GridRect(2, 0, B.Right - 2, 16), WallSide.South));        // north of the north corridor
        _rows.Add(new Row(new GridRect(22, 19, B.Right - 22, 11), WallSide.North));     // south of it
        _rows.Add(new Row(new GridRect(2, 19, 16, B.Y + 9 - 19), WallSide.East));       // west of the west corridor
        _rows.Add(new Row(new GridRect(2, B.Y + 17, 16, B.Bottom - B.Y - 17), WallSide.East));
        _rows.Add(new Row(new GridRect(22, B.Y + 17, 18, B.Bottom - B.Y - 17), WallSide.West)); // east of it
        _rows.Add(new Row(new GridRect(22, B.Y, 18, 9), WallSide.South));
        _rows.Add(new Row(new GridRect(B.X + 28, B.Y, 32, 9), WallSide.South));        // beside A&E's cubicles
        _rows.Add(new Row(new GridRect(88, B.Y + 17, 12, 9), WallSide.North));
        _rows.Add(new Row(new GridRect(54, B.Y + 17, 8, 9), WallSide.North));
    }

    private void Corridor(GridRect rect) =>
        Build(Sim.BuildRoom(new RoomPlan("corridor", rect, WallSide.South)), $"corridor at {rect}");

    /// <summary>A ward 7 m along the corridor in the deepest plot left.</summary>
    private void Ward(string templateId) => Room(templateId, 14, deep: true);

    /// <summary>
    /// Builds a room <paramref name="width"/> tiles along the corridor, as deep as the plot allows: in the
    /// shallowest plot it fits, or the deepest for a <paramref name="deep"/> room such as a ward.
    /// </summary>
    private void Room(string templateId, int width, bool deep = false)
    {
        var template = h.Database.RoomTemplates[templateId];
        if (!Sim.IsUnlocked(template.Department) && h.Database.Departments.ContainsKey(template.Department))
        {
            Build(Sim.UnlockDepartment(template.Department), $"unlock {template.Department}");
        }

        var minDepth = (int)Math.Ceiling(Math.Max(template.MinLengthM, template.MinWidthM) / Units.MetresPerTile);
        var tried = new List<string>();
        foreach (var row in _rows.Where(r => r.Length - r.Used >= width && r.Depth >= minDepth).OrderBy(r => deep ? -r.Depth : r.Depth))
        {
            // A ward in a plot beside the west wing's corridor takes the whole plot, for more beds.
            var along = deep && !row.AlongX ? row.Length - row.Used : width;
            for (var depth = row.Depth; depth >= Math.Min(row.Depth, Math.Max(4, minDepth)); depth--)
            {
                var plan = Sim.AutoPlan(templateId, row.Plot(along, depth), doorSide: row.Corridor);
                var check = Sim.CheckRoom(plan);
                if (check.CanBuild)
                {
                    Build(Sim.BuildRoom(plan), templateId);
                    row.Used += along;
                    return;
                }

                tried.Add($"{plan.Rect}: {string.Join(" ", check.Problems)}");
            }
        }

        throw new Xunit.Sdk.XunitException($"Nowhere to build {templateId} ({width} wide):{Environment.NewLine}{string.Join(Environment.NewLine, tried.Take(12))}{Environment.NewLine}Plots: {string.Join("; ", _rows.Select(r => $"{r.Area} used {r.Used}"))}{Environment.NewLine}Rooms: {string.Join("; ", Sim.Spaces.Select(sp => $"{sp.TemplateId} {sp.Rect.X},{sp.Rect.Y} {sp.Rect.Width}x{sp.Rect.Length}"))}");
    }

    private static void Build(BuildCheck check, string what) =>
        Assert.True(check.CanBuild, $"Couldn't build {what}: {string.Join(" ", check.Problems)}");
}
