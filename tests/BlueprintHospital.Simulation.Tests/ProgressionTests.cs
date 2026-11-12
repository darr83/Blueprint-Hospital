using System.Reflection;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class ProgressionTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static SimulationHarness Career(int seed = 1234, bool arrivals = false, double arrivalMultiplier = 1.0) =>
        new(seed: seed, arrivals: arrivals, arrivalMultiplier: arrivalMultiplier, type: GameType.Career);

    [Fact]
    public void Ten_levels_unlock_every_room_staff_role_and_department_between_them()
    {
        var db = new SimulationHarness().Database;
        Assert.Equal(Enumerable.Range(1, 10), db.Levels.Select(l => l.Number));

        var rooms = db.Levels.SelectMany(l => l.Rooms).ToList();
        Assert.Equal(rooms.Count, rooms.Distinct().Count());
        Assert.Empty(db.RoomTemplates.Keys.Except(rooms));

        var roles = db.Levels.SelectMany(l => l.Staff).ToList();
        Assert.Equal(roles.Count, roles.Distinct().Count());
        Assert.Empty(Enum.GetValues<StaffRole>().Except(roles));

        var departments = db.Levels.SelectMany(l => l.Departments).ToList();
        Assert.Equal(departments.Count, departments.Distinct().Count());
        Assert.Empty(db.Departments.Keys.Except(departments));

        // Only the last level has no goals.
        Assert.All(db.Levels.SkipLast(1), l => Assert.NotEmpty(l.Goals));
        Assert.Empty(db.Levels[^1].Goals);
    }

    [Fact]
    public void Every_goal_counts_a_figure_the_stats_screen_keeps()
    {
        var keys = typeof(StatKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        var db = new SimulationHarness().Database;
        foreach (var goal in db.Levels.SelectMany(l => l.Goals))
        {
            foreach (var stat in goal.Of.Append(goal.Stat).OfType<string>())
            {
                Assert.True(keys.Contains(stat), $"Goal \"{goal.Text}\" counts unknown figure '{stat}'.");
            }
        }
    }

    [Fact]
    public void A_sandbox_game_has_no_levels_and_everything_unlocked()
    {
        var h = new SimulationHarness();

        Assert.False(h.Sim.IsCareer);
        Assert.Equal(0, h.Sim.Level);
        Assert.Empty(h.Sim.Goals);
        Assert.True(h.Sim.IsRoomUnlocked("caesarean_theatre"));
        Assert.True(h.Sim.IsRoleUnlocked(StaffRole.Midwife));
        Assert.True(h.Sim.IsFeatureOn(LevelFeatures.Maternity));
        Assert.True(h.Sim.IsDepartmentAvailable("critical_care"));
        Assert.False(h.Sim.RunCheat("level 3").Succeeded);
    }

    [Fact]
    public void A_career_starts_on_level_one_with_only_the_walk_in_centre_unlocked()
    {
        var h = Career();

        Assert.True(h.Sim.IsCareer);
        Assert.Equal(1, h.Sim.Level);
        Assert.Equal("Walk-in centre", h.Sim.CurrentLevel!.Name);
        Assert.Equal(2, h.Sim.NextLevel!.Number);

        Assert.True(h.Sim.IsRoomUnlocked("triage_room"));
        Assert.True(h.Sim.IsRoomUnlocked("corridor"));
        Assert.False(h.Sim.IsRoomUnlocked("resus_bay"));
        Assert.Equal(3, h.Sim.LevelOfRoom("resus_bay"));
        Assert.False(h.Sim.IsRoomUnlocked("add_floor"));

        Assert.True(h.Sim.IsRoleUnlocked(StaffRole.Nurse));
        Assert.False(h.Sim.IsRoleUnlocked(StaffRole.Porter));
        Assert.False(h.Sim.IsFeatureOn(LevelFeatures.Ambulances));
        Assert.False(h.Sim.IsDepartmentAvailable("general_medicine"));

        StaffGrades.Grade Grade(string title) => StaffGrades.All.Single(g => g.Title == title);
        Assert.True(h.Sim.IsGradeUnlocked(Grade("Junior Doctor")));
        Assert.False(h.Sim.IsGradeUnlocked(Grade("Registrar")));
        Assert.Equal(3, h.Sim.LevelOfGrade(Grade("Registrar")));

        // A grade no level names comes with its role.
        Assert.Equal(5, h.Sim.LevelOfGrade(Grade("Consultant anaesthetist")));

        Assert.Equal(3, h.Sim.Goals.Count);
        Assert.All(h.Sim.Goals, g => Assert.False(g.Done));
    }

    [Fact]
    public void Reaching_a_level_opens_its_free_departments()
    {
        var h = Career();
        Assert.True(h.Sim.RunCheat("level 4").Succeeded);

        Assert.Equal(4, h.Sim.Level);
        Assert.True(h.Sim.IsDepartmentAvailable("general_medicine"));
        Assert.True(h.Sim.IsUnlocked("general_medicine"));
        Assert.True(h.Sim.IsRoomUnlocked("medical_ward"));
        Assert.True(h.Sim.IsFeatureOn(LevelFeatures.Arrests));
        Assert.False(h.Sim.IsFeatureOn(LevelFeatures.Clinics));
    }

    [Fact]
    public void A_room_goal_is_met_once_the_room_is_built()
    {
        var h = Career();
        h.Sim.SetLevel(3);
        h.Sim.AdjustBalance(2_000_000);
        var goal = h.Sim.Goals.Single(g => g.Goal.Type == LevelGoalType.Rooms);
        Assert.False(goal.Done);
        Assert.Equal("0 of 1", goal.Detail);

        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        var check = h.Sim.BuildRoom(h.Sim.AutoPlan("cleaning_room", new GridRect(B.X - 11, B.Y + 17, 5, 6)));
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));

        goal = h.Sim.Goals.Single(g => g.Goal.Type == LevelGoalType.Rooms);
        Assert.True(goal.Done);
        Assert.Equal(1, goal.Progress);
    }

    [Fact]
    public void Goals_only_count_what_happens_on_the_level()
    {
        var h = Career(seed: 7, arrivals: true, arrivalMultiplier: 1.5);
        h.StaffToRecommended();
        h.RunUntil(() => h.Sim.Stats.DepartedTotal >= 3, TimeSpan.FromHours(12), "three patients to leave A&E");
        var treated = h.Sim.Goals[0];
        Assert.Equal(LevelGoalType.Count, treated.Goal.Type);
        Assert.True(treated.Current >= 3);

        h.Sim.SetLevel(2);
        Assert.Equal(0, h.Sim.Goals[0].Current);
    }

    [Fact]
    public void Meeting_every_goal_moves_up_a_level_and_pays_its_grant()
    {
        var h = Career(seed: 3, arrivals: true);

        // Like a player would: two more cubicles where majors and resus will go later, and two more nurses.
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("minors_cubicle", new GridRect(B.X + 28, B.Y, 8, 9))).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("minors_cubicle", new GridRect(B.X + 36, B.Y, 8, 9))).CanBuild);
        foreach (var nurse in h.Sim.Candidates.Where(c => c.Role == StaffRole.Nurse).Take(2).ToList())
        {
            Assert.NotNull(h.Sim.Hire(nurse.Id, null));
        }

        var reached = new List<LevelDefinition>();
        h.Events.Subscribe<LevelReached>(e => reached.Add(e.Level));

        h.RunFor(TimeSpan.FromDays(3));
        Assert.True(h.Sim.Level >= 2, "Still on level 1: " + string.Join("; ", h.Sim.Goals.Select(g => $"{g.Goal.Text}: {g.Detail}")));

        Assert.Equal(2, Assert.Single(reached).Number);
        Assert.Equal(h.Database.Levels[1].Grant, h.Sim.Today[MoneyCategory.Grants] + h.Sim.Accounts.Where(d => d != h.Sim.Today).Sum(d => d[MoneyCategory.Grants]));
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.StartsWith("Level 2 reached", StringComparison.Ordinal));
        Assert.All(h.Sim.Goals, g => Assert.False(g.Done));
    }

    [Fact]
    public void Career_progress_survives_save_and_load_and_old_saves_load_as_sandbox()
    {
        var from = Career();
        from.Sim.SetLevel(3);
        from.RunFor(TimeSpan.FromMinutes(5));
        var json = SaveSerializer.Serialize(new SaveGame
        {
            Clock = from.Clock.CaptureState(),
            Layout = from.Sim.CaptureLayout(),
            People = from.Sim.CapturePeople(),
        });

        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);

        Assert.True(to.Sim.IsCareer);
        Assert.Equal(3, to.Sim.Level);
        Assert.Equal(from.Sim.Goals.Select(g => g.Detail), to.Sim.Goals.Select(g => g.Detail));

        // A Sandbox game saves no progress, so it loads as Sandbox.
        var sandbox = new SimulationHarness();
        Assert.Null(sandbox.Sim.CapturePeople().Progression);
        var career = Career();
        career.Sim.RestorePeople(sandbox.Sim.CapturePeople());
        Assert.False(career.Sim.IsCareer);
        Assert.Equal(0, career.Sim.Level);
    }
}

public class CareerGatingTests
{
    private static SimulationHarness Career(int seed = 1234, bool arrivals = false, double arrivalMultiplier = 1.0) =>
        new(seed: seed, arrivals: arrivals, arrivalMultiplier: arrivalMultiplier, type: GameType.Career);

    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    [Fact]
    public void A_career_starts_as_a_staffed_walk_in_centre_that_is_open_to_patients()
    {
        var h = Career();

        Assert.True(h.Sim.IsEmergencyOpen, string.Join(", ", h.Sim.ClosedReasons));
        Assert.All(h.Sim.Spaces, s => Assert.True(h.Sim.IsRoomUnlocked(s.TemplateId), s.TemplateId));
        Assert.DoesNotContain(h.Sim.Spaces, s => s.TemplateId is "resus_bay" or "majors_cubicle");
        Assert.Contains(h.Sim.Spaces, s => s.TemplateId == "waiting_room");
        Assert.Equal(8, h.Sim.Staff.Count);
        Assert.All(h.Sim.Staff, m => Assert.True(h.Sim.IsRoleUnlocked(m.Role)));
    }

    [Fact]
    public void Locked_rooms_floors_trolleys_and_departments_cannot_be_built()
    {
        var h = Career();
        h.Sim.AdjustBalance(5_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);

        var resus = h.Sim.CheckRoom(h.Sim.AutoPlan("resus_bay", new GridRect(B.X - 30, B.Y + 17, 12, 9)));
        Assert.Contains("unlocks at level 3", Assert.Single(resus.Problems));
        Assert.Contains(h.Sim.CheckAddFloor().Problems, p => p.Contains("level 6"));
        Assert.Contains(h.Sim.CheckUnlock("cardiology").Problems, p => p.Contains("level 8"));
        Assert.False(h.Sim.IsUnlocked("general_medicine"));
        Assert.False(h.Sim.IsUnlocked("pharmacy"));

        h.Sim.SetLevel(3);
        Assert.DoesNotContain(h.Sim.CheckRoom(h.Sim.AutoPlan("resus_bay", new GridRect(B.X - 30, B.Y + 17, 12, 9))).Problems, p => p.Contains("level"));
    }

    [Fact]
    public void Candidates_are_only_the_staff_unlocked_so_far()
    {
        var h = Career();
        Assert.True(h.Sim.Candidates.Count >= HospitalSimulation.MinimumCandidates);
        Assert.All(h.Sim.Candidates, c => Assert.Contains(c.Role, new[] { StaffRole.Receptionist, StaffRole.Nurse, StaffRole.Doctor }));
        Assert.DoesNotContain(h.Sim.Candidates, c => c.Grade is "Registrar" or "Consultant" or "Band 7 nurse");
        Assert.All(h.Sim.RecommendedStaffing(), n => Assert.True(h.Sim.IsRoleUnlocked(n.Role)));

        h.Sim.SetLevel(2);
        Assert.Contains(h.Sim.Candidates, c => c.Role == StaffRole.Porter);
    }

    [Fact]
    public void Early_levels_only_get_walk_in_patients_a_and_e_can_send_home_who_are_not_very_ill()
    {
        var h = Career(seed: 11, arrivals: true, arrivalMultiplier: 3);
        var arrived = new List<Patients.Patient>();
        h.Events.Subscribe<PatientArrived>(e => arrived.Add(e.Patient));
        h.RunFor(TimeSpan.FromHours(10));

        Assert.NotEmpty(arrived);
        foreach (var p in arrived)
        {
            var condition = p.Condition;
            Assert.Equal(Patients.ArrivalMethod.WalkIn, p.ArrivalMethod);
            Assert.True(condition.Department == "emergency" || condition.Disposition == Disposition.Discharge, condition.Id);
            Assert.True(condition.InitialSeverity[1] <= HospitalSimulation.MaxSeverityBeforeRedPatients, condition.Id);
        }
    }

    [Fact]
    public void A_sandbox_game_still_gets_every_kind_of_patient()
    {
        var h = new SimulationHarness(seed: 11, arrivals: true, arrivalMultiplier: 3);
        var arrived = new List<Patients.Patient>();
        h.Events.Subscribe<PatientArrived>(e => arrived.Add(e.Patient));
        h.RunFor(TimeSpan.FromHours(10));

        Assert.Contains(arrived, p => p.ArrivalMethod == Patients.ArrivalMethod.Ambulance);
        Assert.Contains(arrived, p => p.Condition.Department != "emergency");
    }
}

public class HireForShortagesTests
{
    [Fact]
    public void Hire_minimum_team_fills_empty_posts_from_the_list()
    {
        var h = new SimulationHarness(type: GameType.Career);
        var before = h.Sim.RecommendedStaffing().Sum(n => n.Short);
        Assert.True(before > 0);
        var expected = h.Sim.ShortagesHireable();
        Assert.True(expected > 0);

        var hired = h.Sim.HireForShortages();

        Assert.Equal(expected, hired.Count);
        Assert.True(h.Sim.RecommendedStaffing().Sum(n => n.Short) < before);
        Assert.Equal(hired.Count, (int)h.Sim.History.Total(StatsPeriod.AllTime, h.Sim.Now, StatKeys.Hired));
    }
}
