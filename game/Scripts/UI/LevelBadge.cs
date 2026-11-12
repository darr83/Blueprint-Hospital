using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Career levels in the HUD: a level badge in the top bar, the goals card it opens, and the pop-up
/// that says what a new level unlocks. Only shown in a Career game.
/// </summary>
public sealed partial class LevelBadge : Button
{
    private HospitalSimulation _sim = null!;
    private BuildMenu? _menu;
    private IDisposable? _subscription;
    private double _refresh;
    private string _cardSignature = string.Empty;

    /// <summary>The goals card that drops down under the badge.</summary>
    public PanelContainer Card { get; } = new() { Name = "Goals", Visible = false };

    private readonly VBoxContainer _cardBody = new();

    /// <summary>Asks the HUD to open the build bar on one of its icons ("Show me" on a goal).</summary>
    public event Action<string>? ShowMeRequested;

    /// <summary>Asks the HUD to show the level-up pop-up.</summary>
    public event Action<Control>? PopupRequested;

    public void Initialise(HospitalSimulation sim, EventBus events, BuildMenu? menu)
    {
        _sim = sim;
        _menu = menu;
        Name = "LevelBadge";
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(230, 44);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        TooltipText = "Your level and the goals for the next one (L)";
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
        {
            AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }

        Pressed += ToggleCard;
        MouseEntered += QueueRedraw;
        MouseExited += QueueRedraw;

        Card.Theme = UiStyle.Theme;
        Card.MouseFilter = MouseFilterEnum.Stop;
        var box = UiStyle.Card(16, 14);
        box.ShadowSize = 16;
        box.ShadowColor = new Color(0.09f, 0.14f, 0.18f, 0.22f);
        Card.AddThemeStyleboxOverride("panel", box);
        Card.CustomMinimumSize = new Vector2(340, 0);
        _cardBody.AddThemeConstantOverride("separation", 10);
        Card.AddChild(_cardBody);

        _subscription = events.Subscribe<LevelReached>(e => CallDeferred(nameof(ShowLevelUp), e.Level.Number));
    }

    public override void _ExitTree() => _subscription?.Dispose();

    public void ToggleCard()
    {
        Card.Visible = !Card.Visible;
        _cardSignature = string.Empty;
        RefreshCard();
    }

    public override void _Process(double delta)
    {
        if (Card.Visible)
        {
            var rect = GetGlobalRect();
            Card.Position = new Vector2(rect.Position.X, rect.End.Y + 10);
        }

        _refresh -= delta;
        if (_refresh > 0)
        {
            return;
        }

        _refresh = 0.5;
        QueueRedraw();
        RefreshCard();
    }

    // ------------------------------------------------------------------ badge

    public override void _Draw()
    {
        var level = _sim.CurrentLevel;
        if (level is null)
        {
            return;
        }

        var size = Size;
        var hovered = GetGlobalRect().HasPoint(GetGlobalMousePosition()) || Card.Visible;
        DrawStyleBox(UiStyle.Box(hovered ? UiStyle.BlueHover : UiStyle.Blue, 10), new Rect2(Vector2.Zero, size));

        var font = GetThemeFont("font", "Label");
        var number = level.Number.ToString();
        DrawString(font, new Vector2(12, 31), number, HorizontalAlignment.Left, -1, 24, Colors.White);
        var left = 14 + font.GetStringSize(number, HorizontalAlignment.Left, -1, 24).X + 10;
        var width = size.X - left - 12;
        DrawString(font, new Vector2(left, 19), level.Name, HorizontalAlignment.Left, width, 13, Colors.White);

        var goals = _sim.Goals;
        var caption = goals.Count == 0 ? "Top level reached" : $"{goals.Count(g => g.Done)} of {goals.Count} goals";
        DrawString(font, new Vector2(left, 33), caption, HorizontalAlignment.Left, width, 11, new Color(1, 1, 1, 0.85f));

        // A thin bar of overall progress along the bottom.
        var progress = goals.Count == 0 ? 1f : (float)goals.Average(g => g.Progress);
        var track = new Rect2(left, size.Y - 7, width, 3);
        DrawRect(track, new Color(1, 1, 1, 0.25f));
        DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * progress, track.Size.Y)), Colors.White);
    }

    // ------------------------------------------------------------------ goals card

    private void RefreshCard()
    {
        if (!Card.Visible)
        {
            return;
        }

        var goals = _sim.Goals;
        var signature = $"{_sim.Level}|" + string.Join("|", goals.Select(g => $"{g.Done}{g.Detail}{g.Progress:0.00}"));
        if (signature == _cardSignature)
        {
            return;
        }

        _cardSignature = signature;
        foreach (var child in _cardBody.GetChildren())
        {
            child.QueueFree();
        }

        var header = new HBoxContainer();
        var next = _sim.NextLevel;
        var title = new Label
        {
            Text = next is null ? $"Level {_sim.Level}: {_sim.CurrentLevel?.Name}" : $"Goals for level {next.Number}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        title.AddThemeFontSizeOverride("font_size", 17);
        title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        header.AddChild(title);
        var close = new Button { Text = "✕", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 28) };
        close.Pressed += () => Card.Visible = false;
        header.AddChild(close);
        _cardBody.AddChild(header);

        if (next is null)
        {
            _cardBody.AddChild(Wrapped("Every department, room and staff type is unlocked. Keep your hospital running well.", UiStyle.TextMuted, 13));
            return;
        }

        foreach (var goal in goals)
        {
            _cardBody.AddChild(GoalRow(goal));
        }

        var reward = next.Grant > 0 ? $"Reward: £{next.Grant:N0} and {next.Name}" : $"Reward: {next.Name}";
        _cardBody.AddChild(HudWidgets.Divider());
        _cardBody.AddChild(Wrapped(reward, UiStyle.Blue, 13));
        _cardBody.AddChild(Wrapped(UnlockSummary(next), UiStyle.TextMuted, 12));
    }

    private Control GoalRow(GoalProgress goal)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var tick = new PanelContainer { CustomMinimumSize = new Vector2(20, 20), SizeFlagsVertical = SizeFlags.ShrinkBegin };
        tick.AddThemeStyleboxOverride("panel", UiStyle.Box(goal.Done ? UiStyle.Green : UiStyle.Surface, 10, goal.Done ? UiStyle.Green : UiStyle.Border));
        if (goal.Done)
        {
            var mark = new Label { Text = "✓", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            mark.AddThemeFontSizeOverride("font_size", 12);
            mark.AddThemeColorOverride("font_color", Colors.White);
            tick.AddChild(mark);
        }

        row.AddChild(tick);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 3);
        column.AddChild(Wrapped(goal.Goal.Text, UiStyle.TextDark, 14));
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = goal.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6) };
        bar.AddThemeStyleboxOverride("background", UiStyle.Box(UiStyle.Ground, 3));
        bar.AddThemeStyleboxOverride("fill", UiStyle.Box(goal.Done ? UiStyle.Green : UiStyle.Blue, 3));
        column.AddChild(bar);
        column.AddChild(Wrapped(goal.Detail, UiStyle.TextMuted, 12));
        if (!goal.Done && ShowMeIcon(goal.Goal) is { } iconId)
        {
            var show = new LinkButton { Text = "Show me ›", FocusMode = FocusModeEnum.None, Underline = LinkButton.UnderlineMode.OnHover };
            show.AddThemeColorOverride("font_color", UiStyle.Blue);
            show.AddThemeFontSizeOverride("font_size", 12);
            show.Pressed += () =>
            {
                Card.Visible = false;
                ShowMeRequested?.Invoke(iconId);
            };
            column.AddChild(show);
        }

        row.AddChild(column);
        return row;
    }

    /// <summary>The build bar icon that a goal's "Show me" opens, if building something helps.</summary>
    private string? ShowMeIcon(LevelGoal goal)
    {
        if (_menu is null)
        {
            return null;
        }

        return goal.Type switch
        {
            LevelGoalType.Rooms when goal.Room is { } room => _menu.IconsWith(room).FirstOrDefault()?.Id,
            LevelGoalType.DepartmentsOpen => _menu.Icons.FirstOrDefault(i => i.Department is { } d && goal.Departments.Contains(d) && !_sim.IsDepartmentOpen(d))?.Id,
            LevelGoalType.Floors => "building",
            _ => null,
        };
    }

    private static Label Wrapped(string text, Color colour, int size)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }

    // ------------------------------------------------------------------ level-up pop-up

    /// <summary>What a level unlocks, in a line: departments, rooms, staff and new patients.</summary>
    private string UnlockSummary(LevelDefinition level) =>
        string.Join(" · ", UnlockGroups(level).Select(g => $"{g.Title}: {g.Text}"));

    private List<(string Title, string Text)> UnlockGroups(LevelDefinition level)
    {
        var groups = new List<(string, string)>();
        if (level.Departments.Count > 0)
        {
            groups.Add(("Departments", string.Join(", ", level.Departments.Select(_sim.DepartmentName))));
        }

        var rooms = level.Rooms.Select(r => _sim.Database.RoomTemplates.TryGetValue(r, out var t) ? t.Name : RoomWord(r)).ToList();
        if (rooms.Count > 0)
        {
            groups.Add(("Rooms", string.Join(", ", rooms)));
        }

        var staff = level.Staff.Select(r => Capitalise(StaffGrades.RoleName(r))).Concat(level.Grades).ToList();
        if (staff.Count > 0)
        {
            groups.Add(("Staff", string.Join(", ", staff)));
        }

        var features = level.Features.Select(FeatureWord).ToList();
        if (features.Count > 0)
        {
            groups.Add(("New", string.Join(", ", features)));
        }

        return groups;
    }

    private static string RoomWord(string item) => item switch
    {
        BuildMenu.AddFloor => "Upper floors",
        BuildMenu.ResusTrolleys => "Resus trolleys",
        BuildMenu.ExtendBuilding => "Extend the building",
        BuildMenu.WaitingChairs => "Chairs",
        BuildMenu.WaitingDesk => "Reception desk",
        BuildMenu.WaitingDoors => "Waiting room doors",
        BuildMenu.Decorate => "Decorating",
        _ => item.Replace('_', ' '),
    };

    private static string FeatureWord(string feature) => feature switch
    {
        LevelFeatures.Ambulances => "ambulances",
        LevelFeatures.AmberPatients => "amber patients",
        LevelFeatures.Children => "children",
        LevelFeatures.RedPatients => "red patients",
        LevelFeatures.Arrests => "cardiac arrests and the crash team",
        LevelFeatures.Spills => "spills to clean",
        LevelFeatures.WardEmergencies => "emergencies on the wards",
        LevelFeatures.Clinics => "booked clinics",
        LevelFeatures.PlannedLists => "planned operation lists",
        LevelFeatures.Maternity => "women in labour",
        _ => feature.Replace('_', ' '),
    };

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>Shows the pop-up for a level just reached (also used by screenshots).</summary>
    public void ShowLevelUp(int number)
    {
        if (number < 1 || number > _sim.Database.Levels.Count)
        {
            return;
        }

        var level = _sim.Database.Levels[number - 1];
        var panel = new ModalPanel { Name = "LevelUp" };
        panel.Content.CustomMinimumSize = new Vector2(520, 0);
        var eyebrow = new Label { Text = $"LEVEL {level.Number} REACHED" };
        eyebrow.AddThemeFontSizeOverride("font_size", 12);
        eyebrow.AddThemeColorOverride("font_color", UiStyle.Blue);
        panel.Content.AddChild(eyebrow);
        panel.Content.AddChild(UiStyle.Heading(level.Name, 26));
        var description = Wrapped(level.Description, UiStyle.TextDark, 15);
        description.CustomMinimumSize = new Vector2(500, 0);
        panel.Content.AddChild(description);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        var cells = UnlockGroups(level);
        if (level.Grant > 0)
        {
            cells.Insert(0, ("Grant", $"£{level.Grant:N0}"));
        }

        foreach (var (title, text) in cells)
        {
            var cell = new PanelContainer { CustomMinimumSize = new Vector2(250, 0) };
            cell.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Ground, 8, UiStyle.Border, 10));
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 2);
            var heading = new Label { Text = title.ToUpperInvariant() };
            heading.AddThemeFontSizeOverride("font_size", 11);
            heading.AddThemeColorOverride("font_color", UiStyle.TextMuted);
            column.AddChild(heading);
            var body = Wrapped(text, UiStyle.TextDark, 13);
            body.CustomMinimumSize = new Vector2(228, 0);
            column.AddChild(body);
            cell.AddChild(column);
            grid.AddChild(cell);
        }

        panel.Content.AddChild(grid);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(UiStyle.SecondaryButton("Later", panel.Close, 110));
        var firstIcon = _menu?.Icons.FirstOrDefault(i => i.Department is { } d && level.Departments.Contains(d))?.Id
                        ?? (level.Rooms.Count > 0 ? _menu?.IconsWith(level.Rooms[0]).FirstOrDefault()?.Id : null);
        buttons.AddChild(UiStyle.Button("Show me", () =>
        {
            panel.Close();
            if (firstIcon is not null)
            {
                ShowMeRequested?.Invoke(firstIcon);
            }
        }, 140));
        panel.Content.AddChild(buttons);
        PopupRequested?.Invoke(panel);
    }
}
