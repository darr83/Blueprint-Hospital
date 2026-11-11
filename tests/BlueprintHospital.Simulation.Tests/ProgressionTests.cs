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
        new(seed: seed, arrivals: arrivals, arrivalMultiplier: arrivalMultiplier, mode: GameMode.Career);

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
        var h = Career(seed: 3, arrivals: true, arrivalMultiplier: 0.5);
        h.StaffToRecommended();
        var reached = new List<LevelDefinition>();
        h.Events.Subscribe<LevelReached>(e => reached.Add(e.Level));

        h.RunFor(TimeSpan.FromDays(3));
        Assert.True(h.Sim.Level >= 2, "Still on level 1: " + string.Join("; ", h.Sim.Goals.Select(g => $"{g.Goal.Text}: {g.Detail}")) + " " + string.Join(",", h.Sim.Accounts.Select(d => d.Net)));

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
