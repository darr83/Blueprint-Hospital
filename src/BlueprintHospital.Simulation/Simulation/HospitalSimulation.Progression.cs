using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>How a game is played: everything open from the start, or unlocked level by level.</summary>
public enum GameType
{
    /// <summary>Every department, room, staff type and feature is open from the start.</summary>
    Sandbox,

    /// <summary>Start as a small walk-in centre and unlock the rest by meeting each level's goals.</summary>
    Career,
}

/// <summary>Published when a Career hospital reaches a new level.</summary>
public sealed record LevelReached(LevelDefinition Level) : IGameEvent;

/// <summary>How far the player is towards one of the current level's goals.</summary>
/// <param name="Current">The figure now, e.g. 14 patients, 0.82 of patients within 4 hours, 2 floors.</param>
/// <param name="Progress">How far along, from 0 to 1 (for a progress bar).</param>
/// <param name="Detail">The figure in words, e.g. "14 of 20" or "82%, needs 80% (12 of 20 patients so far)".</param>
public sealed record GoalProgress(LevelGoal Goal, double Current, double Progress, bool Done, string Detail);

/// <summary>Saved Career progress (absent from Sandbox games and older saves).</summary>
public sealed class ProgressionState
{
    public GameType Type { get; init; }

    public int Level { get; init; }

    /// <summary>Each goal figure's all-time total when the level was reached.</summary>
    public Dictionary<string, double> Baselines { get; init; } = new();

    public int ProfitableDays { get; init; }

    public DateTime ProgressDay { get; init; }
}

/// <summary>
/// Career progression. A Career hospital starts on level 1 with only a few rooms, staff types and
/// kinds of patient; meeting every goal of its level takes it to the next, which pays a grant and
/// unlocks more. Goals count only what happens while on the level. In a Sandbox game everything is
/// unlocked and none of this applies.
/// </summary>
public sealed partial class HospitalSimulation
{
    private readonly Dictionary<string, double> _levelBaselines = new(StringComparer.Ordinal);
    private int _profitableDaysThisLevel;
    private DateTime _progressDay;
    private DateTime _nextProgressCheck;

    /// <summary>Sandbox or Career.</summary>
    public GameType Type { get; private set; }

    /// <summary>True when this is a Career game (and the game data has levels).</summary>
    public bool IsCareer => Type == GameType.Career && Database.Levels.Count > 0;

    /// <summary>The Career level reached, from 1. 0 in a Sandbox game.</summary>
    public int Level { get; private set; }

    /// <summary>The level the hospital is on, or null in a Sandbox game.</summary>
    public LevelDefinition? CurrentLevel => IsCareer && Level >= 1 ? Database.Levels[Math.Min(Level, Database.Levels.Count) - 1] : null;

    /// <summary>The level the current goals lead to, or null on the last level (or in a Sandbox game).</summary>
    public LevelDefinition? NextLevel => IsCareer && Level < Database.Levels.Count ? Database.Levels[Level] : null;

    /// <summary>Days finished in profit since reaching this level.</summary>
    public int ProfitableDaysThisLevel => _profitableDaysThisLevel;

    /// <summary>How far the player is towards each of the current level's goals (empty on the last level).</summary>
    public IReadOnlyList<GoalProgress> Goals =>
        NextLevel is null || CurrentLevel is not { } level ? Array.Empty<GoalProgress>() : level.Goals.Select(Evaluate).ToList();

    // ---------------------------------------------------------------- unlocks

    /// <summary>The level that unlocks a room or build bar item, or 0 if no level lists it (then it is always open).</summary>
    public int LevelOfRoom(string roomId) => FirstLevel(l => l.Rooms.Contains(roomId));

    /// <summary>The level from which a department can be opened, or 0 if no level lists it.</summary>
    public int LevelOfDepartment(string departmentId) => FirstLevel(l => l.Departments.Contains(departmentId));

    /// <summary>The level from which a staff role can be hired, or 0 if no level lists it.</summary>
    public int LevelOfRole(StaffRole role) => FirstLevel(l => l.Staff.Contains(role));

    /// <summary>The level from which a staff grade can be hired: its own level, or its role's if no level lists the grade.</summary>
    public int LevelOfGrade(StaffGrades.Grade grade)
    {
        var own = FirstLevel(l => l.Grades.Contains(grade.Title));
        return own > 0 ? Math.Max(own, LevelOfRole(grade.Role)) : LevelOfRole(grade.Role);
    }

    /// <summary>The level that switches a feature on (see <see cref="LevelFeatures"/>), or 0 if none does.</summary>
    public int LevelOfFeature(string feature) => FirstLevel(l => l.Features.Contains(feature));

    /// <summary>Whether a room or build bar item can be built now.</summary>
    public bool IsRoomUnlocked(string roomId) => !IsCareer || LevelOfRoom(roomId) <= Level;

    /// <summary>Whether a department can be opened now (it may still have an opening cost).</summary>
    public bool IsDepartmentAvailable(string departmentId) => !IsCareer || LevelOfDepartment(departmentId) <= Level;

    /// <summary>Whether a staff role can be hired now.</summary>
    public bool IsRoleUnlocked(StaffRole role) => !IsCareer || LevelOfRole(role) <= Level;

    /// <summary>Whether a staff grade can be hired now.</summary>
    public bool IsGradeUnlocked(StaffGrades.Grade grade) => !IsCareer || LevelOfGrade(grade) <= Level;

    /// <summary>Whether a part of the game is switched on: always in Sandbox; from its level in Career. A feature no level lists is always on.</summary>
    public bool IsFeatureOn(string feature) => !IsCareer || LevelOfFeature(feature) <= Level;

    /// <summary>How busy A&amp;E is at this level, as a share of a full hospital's arrivals (1 in Sandbox).</summary>
    public double LevelArrivalRate => CurrentLevel?.ArrivalRate ?? 1;

    /// <summary>The worst a patient can start before red patients are unlocked.</summary>
    public const double MaxSeverityBeforeRedPatients = 0.4;

    /// <summary>The age from which patients count as adults: before children are unlocked, nobody younger comes in.</summary>
    public const int AdultAge = 16;

    /// <summary>
    /// Whether patients with a condition come in at this level: conditions of departments the hospital
    /// has opened (A&amp;E always is), or ones A&amp;E can sort out and send home with only tests this
    /// level has unlocked (a broken wrist once X-ray is unlocked); no one who starts very ill until red
    /// patients are unlocked, and no conditions only children get until children are unlocked.
    /// </summary>
    private bool CanArriveAtThisLevel(ConditionDefinition condition) =>
        !IsCareer || ((IsDepartmentOpen(condition.Department) || CanBeSentHomeFromEmergency(condition))
                      && (IsFeatureOn(LevelFeatures.RedPatients) || condition.InitialSeverity.Length < 2 || condition.InitialSeverity[1] <= MaxSeverityBeforeRedPatients)
                      && (IsFeatureOn(LevelFeatures.Children) || condition.AgeRange.Length < 2 || condition.AgeRange[1] >= AdultAge));

    /// <summary>A condition A&amp;E treats and sends home, whose tests all need only rooms unlocked at this level.</summary>
    private bool CanBeSentHomeFromEmergency(ConditionDefinition condition) =>
        condition.Disposition == Disposition.Discharge
        && condition.KeyInvestigations.All(id => !Database.Investigations.TryGetValue(id, out var test) || test.Room is not { } room
                                                 || Database.RoomTemplates.Values.Any(t => t.Kind == room && IsRoomUnlocked(t.Id)));

    /// <summary>
    /// Whether a newly made patient suits this level: an adult until children are unlocked, and no
    /// sicker on arrival than the hospital can look after (green until amber patients are unlocked,
    /// then amber until red ones are), judged the way the triage nurse will judge them.
    /// </summary>
    private bool SuitsThisLevel(Patient patient)
    {
        if (!IsCareer)
        {
            return true;
        }

        if (!IsFeatureOn(LevelFeatures.Children) && patient.Age < AdultAge)
        {
            return false;
        }

        var worst = WorstTriageAtThisLevel;
        return worst == TriageCategory.Red
               || !TriageSystem.Assess(patient.CurrentObservations(), patient.Symptoms, Database).Category.IsMoreUrgentThan(worst);
    }

    /// <summary>The most urgent triage category this level's hospital is set up for: green on a walk-in centre, amber once majors arrive, red once resus does.</summary>
    public TriageCategory WorstTriageAtThisLevel =>
        !IsFeatureOn(LevelFeatures.AmberPatients) ? TriageCategory.Green
        : !IsFeatureOn(LevelFeatures.RedPatients) ? TriageCategory.Amber
        : TriageCategory.Red;

    /// <summary>A triage decision kept within what this level's hospital is set up for (see <see cref="WorstTriageAtThisLevel"/>).</summary>
    private TriageDecision ForThisLevel(TriageDecision decision) =>
        decision.Category.IsMoreUrgentThan(WorstTriageAtThisLevel) ? decision with { Category = WorstTriageAtThisLevel } : decision;

    /// <summary>
    /// The worst a patient can get while in the hospital at this level: on a walk-in centre nobody gets
    /// worse than they came in; before red patients nobody gets very ill; before the crash team, nobody's
    /// heart stops.
    /// </summary>
    private double WorstSeverityAtThisLevel(Patient patient)
    {
        var condition = patient.Condition;
        var worst = condition.CanBeFatal && IsFeatureOn(LevelFeatures.Arrests) ? 1.0
            : IsFeatureOn(LevelFeatures.RedPatients) ? 0.9
            : MaxSeverityBeforeRedPatients + 0.1;
        return IsFeatureOn(LevelFeatures.AmberPatients) ? worst : Math.Min(worst, patient.InitialSeverity);
    }

    /// <summary>
    /// The kinds of space a triage category is looked after in. In a Career game a category whose rooms
    /// aren't unlocked yet uses the most acute kind that is, so no one waits for a room that can't be built.
    /// </summary>
    private CareSpaceKind[] SpacePreference(TriageCategory category)
    {
        var kinds = BaseSpacePreference(category);
        if (!IsCareer || kinds.Any(KindUnlocked))
        {
            return kinds;
        }

        return new[] { CareSpaceKind.Majors, CareSpaceKind.Minors }.Where(KindUnlocked).Take(1).DefaultIfEmpty(CareSpaceKind.Minors).ToArray();

        bool KindUnlocked(CareSpaceKind kind) => kind switch
        {
            CareSpaceKind.Resus => IsRoomUnlocked("resus_bay"),
            CareSpaceKind.Majors => IsRoomUnlocked("majors_cubicle"),
            _ => true,
        };
    }

    private int FirstLevel(Func<LevelDefinition, bool> unlocks) =>
        Database.Levels.FirstOrDefault(unlocks)?.Number ?? 0;

    // ---------------------------------------------------------------- levelling up

    /// <summary>Sets the game's mode when it is created. Career games start on level 1.</summary>
    private void StartProgression(GameType type)
    {
        Type = type;
        if (IsCareer)
        {
            EnterLevel(1, grant: false);
        }
    }

    /// <summary>Checks the goals every game minute and moves up a level once they are all met.</summary>
    private void UpdateProgression()
    {
        if (!IsCareer || Now < _nextProgressCheck)
        {
            return;
        }

        _nextProgressCheck = Now.AddMinutes(1);
        CountProfitableDays();
        if (NextLevel is { } next && Goals.All(g => g.Done))
        {
            EnterLevel(next.Number, grant: true);
        }
    }

    /// <summary>Moves to a level: pays its grant, opens its free departments and starts counting its goals afresh.</summary>
    private void EnterLevel(int number, bool grant)
    {
        number = Math.Clamp(number, 1, Database.Levels.Count);
        Level = number;
        var level = Database.Levels[number - 1];

        _levelBaselines.Clear();
        var history = History;
        foreach (var key in level.Goals.SelectMany(g => g.Of.Append(g.Stat ?? string.Empty)).Where(k => k.Length > 0).Distinct())
        {
            _levelBaselines[key] = history.Total(StatsPeriod.AllTime, Now, key);
        }

        _profitableDaysThisLevel = 0;
        _progressDay = Now.Date;

        // Departments with no opening cost open as soon as their level is reached (or skipped past, with the cheat).
        foreach (var d in Database.Levels.Take(number).SelectMany(l => l.Departments).Where(d => Database.Departments.TryGetValue(d, out var def) && def.OpeningCost == 0))
        {
            _unlockedDepartments.Add(d);
        }

        if (grant)
        {
            if (level.Grant > 0)
            {
                Book(MoneyCategory.Grants, level.Grant);
            }

            var money = level.Grant > 0 ? $" Grant of £{level.Grant:N0} received." : string.Empty;
            Notifications.Add(Now, NotificationLevel.Info, $"Level {level.Number} reached: {level.Name}.{money} {level.Description}");
            RefreshCandidatesForNewLevel();
            Events.Publish(new LevelReached(level));
        }
    }

    /// <summary>Jumps straight to a level, without its grant (the "level" cheat).</summary>
    public void SetLevel(int number)
    {
        if (!IsCareer)
        {
            return;
        }

        EnterLevel(number, grant: false);
        RefreshCandidatesForNewLevel();
        Events.Publish(new LevelReached(CurrentLevel!));
    }

    /// <summary>Counts each finished day that took in more money than it spent.</summary>
    private void CountProfitableDays()
    {
        if (Now.Date <= _progressDay)
        {
            return;
        }

        if (_days.FirstOrDefault(d => d.Date == _progressDay) is { } day && day.Net > 0)
        {
            _profitableDaysThisLevel++;
        }

        _progressDay = Now.Date;
    }

    // ---------------------------------------------------------------- goals

    /// <summary>How much a figure has gone up since this level was reached.</summary>
    private double SinceLevelStart(string key) =>
        History.Total(StatsPeriod.AllTime, Now, key) - _levelBaselines.GetValueOrDefault(key);

    private GoalProgress Evaluate(LevelGoal goal)
    {
        static string Whole(double v) => Math.Floor(v + 1e-9).ToString("N0");

        switch (goal.Type)
        {
            case LevelGoalType.Count:
            {
                var n = SinceLevelStart(goal.Stat!);
                return new GoalProgress(goal, n, Fraction(n, goal.Target), n >= goal.Target, $"{Whole(Math.Min(n, goal.Target))} of {Whole(goal.Target)}");
            }

            case LevelGoalType.ShareAtLeast:
            {
                var part = SinceLevelStart(goal.Stat!);
                var whole = goal.Of.Sum(SinceLevelStart);
                var share = whole <= 0 ? 0 : part / whole;
                var enough = whole >= goal.MinCount;
                var done = enough && share >= goal.Target - 1e-9;

                // Halfway there for numbers, halfway for the share.
                var progress = goal.MinCount <= 0 ? Fraction(share, goal.Target) : (0.5 * Fraction(whole, goal.MinCount)) + (0.5 * Fraction(share, goal.Target));
                var detail = whole <= 0
                    ? $"needs {Percent(goal.Target)}, nobody counted yet"
                    : $"{Percent(share)}, needs {Percent(goal.Target)}" + (enough ? string.Empty : $" ({Whole(whole)} of {goal.MinCount} counted)");
                return new GoalProgress(goal, share, done ? 1 : Math.Min(progress, 0.99), done, detail);
            }

            case LevelGoalType.AverageAtMost:
            {
                var total = SinceLevelStart(goal.Stat!);
                var count = goal.Of.Sum(SinceLevelStart);
                var average = count <= 0 ? 0 : total / count;
                var enough = count >= goal.MinCount;
                var done = enough && average <= goal.Target + 1e-9;
                var progress = (0.5 * Fraction(count, goal.MinCount)) + (0.5 * (average <= goal.Target ? 1 : Fraction(goal.Target, average)));
                var detail = count <= 0
                    ? $"needs {Whole(goal.Target)} or less, none counted yet"
                    : $"{average:0} now, needs {Whole(goal.Target)} or less" + (enough ? string.Empty : $" ({Whole(count)} of {goal.MinCount} counted)");
                return new GoalProgress(goal, average, done ? 1 : Math.Min(progress, 0.99), done, detail);
            }

            case LevelGoalType.Rooms:
            {
                var n = _spaces.Values.Count(s => s.TemplateId == goal.Room);
                return new GoalProgress(goal, n, Fraction(n, goal.Target), n >= goal.Target, $"{Whole(Math.Min(n, goal.Target))} of {Whole(goal.Target)}");
            }

            case LevelGoalType.DepartmentsOpen:
            {
                var n = goal.Departments.Count(IsDepartmentOpen);
                return new GoalProgress(goal, n, Fraction(n, goal.Target), n >= goal.Target, $"{Whole(Math.Min(n, goal.Target))} of {Whole(goal.Target)} open");
            }

            case LevelGoalType.ProfitableDays:
            {
                var n = _profitableDaysThisLevel;
                var today = Today.Net;
                var detail = $"{Whole(Math.Min(n, goal.Target))} of {Whole(goal.Target)} (today so far: {(today >= 0 ? "+" : "-")}£{Math.Abs(today):N0})";
                return new GoalProgress(goal, n, Fraction(n, goal.Target), n >= goal.Target, detail);
            }

            case LevelGoalType.Floors:
            {
                var n = FloorCount;
                return new GoalProgress(goal, n, Fraction(n - 1, goal.Target - 1), n >= goal.Target, $"{n} of {Whole(goal.Target)} floors");
            }

            default:
                return new GoalProgress(goal, 0, 0, false, string.Empty);
        }
    }

    private static string Percent(double share) => $"{Math.Floor((share * 100) + 1e-9):0}%";

    private static double Fraction(double value, double target) => target <= 0 ? 1 : Math.Clamp(value / target, 0, 1);

    /// <summary>A department is open once it has been paid for and has at least one room built.</summary>
    public bool IsDepartmentOpen(string departmentId) =>
        IsUnlocked(departmentId) && _spaces.Values.Any(s => s.DepartmentId == departmentId);

    // ---------------------------------------------------------------- saving

    private ProgressionState? CaptureProgression() => Type == GameType.Sandbox ? null : new ProgressionState
    {
        Type = Type,
        Level = Level,
        Baselines = new Dictionary<string, double>(_levelBaselines),
        ProfitableDays = _profitableDaysThisLevel,
        ProgressDay = _progressDay,
    };

    /// <summary>Puts back Career progress; a save without any is a Sandbox game.</summary>
    private void RestoreProgression(ProgressionState? state)
    {
        Type = state?.Type ?? GameType.Sandbox;
        Level = IsCareer ? Math.Clamp(state!.Level, 1, Database.Levels.Count) : 0;
        _levelBaselines.Clear();
        if (state is not null)
        {
            foreach (var (key, value) in state.Baselines)
            {
                _levelBaselines[key] = value;
            }
        }

        _profitableDaysThisLevel = state?.ProfitableDays ?? 0;
        _progressDay = state?.ProgressDay ?? Now.Date;
        _nextProgressCheck = default;
    }
}
