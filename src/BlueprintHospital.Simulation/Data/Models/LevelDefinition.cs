using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>
/// One hospital level of a Career game, loaded from Data/Levels. Unlocks add up: level 4 can use
/// everything levels 1 to 3 unlocked as well as its own. A level's <see cref="Goals"/> are what the
/// player must do while on it to reach the next level.
/// </summary>
public sealed class LevelDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    /// <summary>1 for the first level, then 2, 3... with no gaps.</summary>
    public int Number { get; init; }

    /// <summary>What the hospital is at this level, e.g. "Walk-in centre".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>One or two sentences shown when the level is reached.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Money given when the level is reached.</summary>
    public int Grant { get; init; }

    /// <summary>How busy A&amp;E is at this level, as a share of a full hospital's arrivals (1 = full).</summary>
    public double ArrivalRate { get; init; } = 1;

    /// <summary>Departments that can be opened from this level (they may still have an opening cost).</summary>
    public IReadOnlyList<string> Departments { get; init; } = Array.Empty<string>();

    /// <summary>Room templates and build bar items (corridor, add_floor, waiting_chairs...) unlocked at this level.</summary>
    public IReadOnlyList<string> Rooms { get; init; } = Array.Empty<string>();

    /// <summary>Staff roles that can be hired from this level.</summary>
    public IReadOnlyList<StaffRole> Staff { get; init; } = Array.Empty<StaffRole>();

    /// <summary>
    /// Staff grades unlocked at this level, by title ("Band 7 nurse", "Registrar"). A grade that no
    /// level lists comes with its role.
    /// </summary>
    public IReadOnlyList<string> Grades { get; init; } = Array.Empty<string>();

    /// <summary>Parts of the game switched on from this level: see <see cref="LevelFeatures"/>.</summary>
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();

    /// <summary>What to do on this level to reach the next one. Empty on the last level.</summary>
    public IReadOnlyList<LevelGoal> Goals { get; init; } = Array.Empty<LevelGoal>();

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        context.RequireRange("number", Number, 1, 100);
        context.RequireRange("grant", Grant, 0, 100_000_000);
        context.RequireRange("arrival_rate", ArrivalRate, 0, 5);
        context.RequireIds("departments", Departments);
        context.RequireIds("rooms", Rooms);
        context.RequireIds("features", Features);
        foreach (var feature in Features.Where(f => !LevelFeatures.All.Contains(f)))
        {
            context.Error("features", $"'{feature}' is not a feature. Use one of: {string.Join(", ", LevelFeatures.All)}.");
        }

        var titles = StaffGrades.All.Select(g => g.Title).ToHashSet(StringComparer.Ordinal);
        foreach (var grade in Grades.Where(g => !titles.Contains(g)))
        {
            context.Error("grades", $"'{grade}' is not a staff grade.");
        }

        for (var i = 0; i < Goals.Count; i++)
        {
            Goals[i].Validate(context, $"goals[{i}]");
        }
    }
}

/// <summary>The kinds of goal a level can set.</summary>
public enum LevelGoalType
{
    /// <summary>A figure (such as patients treated) goes up by <see cref="LevelGoal.Target"/> while on the level.</summary>
    Count,

    /// <summary>A figure as a share of others (such as leaving within 4 hours out of all leaving) is at least the target, over at least <see cref="LevelGoal.MinCount"/>.</summary>
    ShareAtLeast,

    /// <summary>A total divided by a count (such as handover minutes per handover) is at most the target, over at least <see cref="LevelGoal.MinCount"/>.</summary>
    AverageAtMost,

    /// <summary>The hospital has at least <see cref="LevelGoal.Target"/> rooms of <see cref="LevelGoal.Room"/>.</summary>
    Rooms,

    /// <summary>At least <see cref="LevelGoal.Target"/> of <see cref="LevelGoal.Departments"/> are open (paid for, with a room built).</summary>
    DepartmentsOpen,

    /// <summary>Days finished with more money in than out, while on the level.</summary>
    ProfitableDays,

    /// <summary>The building has at least this many floors, counting the ground floor.</summary>
    Floors,
}

/// <summary>One goal of a level, e.g. "Treat 20 A&amp;E patients".</summary>
public sealed class LevelGoal
{
    public LevelGoalType Type { get; init; }

    /// <summary>What the player sees, e.g. "Treat 20 A&amp;E patients".</summary>
    public string Text { get; init; } = string.Empty;

    public double Target { get; init; } = 1;

    /// <summary>The figure counted (a key from <c>StatKeys</c>, such as "ae.departed").</summary>
    public string? Stat { get; init; }

    /// <summary>For shares and averages: the figures it is out of (added together).</summary>
    public IReadOnlyList<string> Of { get; init; } = Array.Empty<string>();

    /// <summary>For shares and averages: how many there must be before the goal can be met.</summary>
    public int MinCount { get; init; }

    /// <summary>For room goals: the room template.</summary>
    public string? Room { get; init; }

    /// <summary>For department goals: the departments, any <see cref="Target"/> of which will do.</summary>
    public IReadOnlyList<string> Departments { get; init; } = Array.Empty<string>();

    internal void Validate(ValidationContext context, string field)
    {
        context.RequireText(field + ".text", Text);
        context.RequireRange(field + ".target", Target, 0, 1_000_000);
        switch (Type)
        {
            case LevelGoalType.Count when string.IsNullOrWhiteSpace(Stat):
            case LevelGoalType.ShareAtLeast or LevelGoalType.AverageAtMost when string.IsNullOrWhiteSpace(Stat) || Of.Count == 0:
                context.Error(field, $"a {Type} goal needs a stat{(Type == LevelGoalType.Count ? string.Empty : " and the stats it is out of (of)")}.");
                break;
            case LevelGoalType.Rooms when string.IsNullOrWhiteSpace(Room):
                context.Error(field + ".room", "a rooms goal needs a room.");
                break;
            case LevelGoalType.DepartmentsOpen when Departments.Count == 0:
                context.Error(field + ".departments", "a departments_open goal needs at least one department.");
                break;
        }
    }
}

/// <summary>Parts of the game that a Career game switches on level by level.</summary>
public static class LevelFeatures
{
    /// <summary>Patients arrive by ambulance (before this, everyone walks in).</summary>
    public const string Ambulances = "ambulances";

    /// <summary>Patients who need a majors cubicle (triaged amber) come in. Before this, everyone is well enough for minors.</summary>
    public const string AmberPatients = "amber_patients";

    /// <summary>Children come in. Before this, the hospital only sees adults (16 and over).</summary>
    public const string Children = "children";

    /// <summary>Patients sick enough to be triaged red come in.</summary>
    public const string RedPatients = "red_patients";

    /// <summary>Cardiac arrests and the crash team.</summary>
    public const string Arrests = "arrests";

    /// <summary>Spills that need cleaning up.</summary>
    public const string Spills = "spills";

    /// <summary>Sudden emergencies for patients in bed (as often as the New Game setting says).</summary>
    public const string WardEmergencies = "ward_emergencies";

    /// <summary>Booked outpatient appointments.</summary>
    public const string Clinics = "clinics";

    /// <summary>Weekday planned operation and procedure lists.</summary>
    public const string PlannedLists = "planned_lists";

    /// <summary>Women in labour and antenatal appointments.</summary>
    public const string Maternity = "maternity";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Ambulances, AmberPatients, Children, RedPatients, Arrests, Spills, WardEmergencies, Clinics, PlannedLists, Maternity,
    };
}
