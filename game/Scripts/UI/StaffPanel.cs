using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The selected member of staff: what they are doing now, how tired they are, what they have done
/// this shift and the work waiting for someone in their role. Opens when you click them on the map.
/// </summary>
public partial class StaffPanel : PanelContainer
{
    private readonly Label _name = new();
    private readonly Label _role = UiStyle.Caption(string.Empty, 14);
    private readonly VBoxContainer _queue = new();
    private readonly Label _footer = UiStyle.Caption(string.Empty, 13);
    private (PanelContainer Badge, Label Initials) _avatar;
    private (PanelContainer Box, Label Heading, Label Text, Label Detail) _now;
    private (VBoxContainer Root, Label Value, ProgressBar Bar) _energy;
    private (VBoxContainer Root, Label Value, ProgressBar Bar) _morale;
    private (PanelContainer Tile, Label Value) _tasks;
    private (PanelContainer Tile, Label Value) _skill;
    private (PanelContainer Tile, Label Value) _experience;
    private Label _queueTitle = null!;
    private HospitalSimulation _sim = null!;
    private StaffMember? _member;
    private string _queueSignature = string.Empty;
    private double _refresh;
    private Button _letGo = null!;
    private double _confirmLetGo;

    /// <summary>Raised when the player closes the panel.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when the player asks to see this member of staff on the map.</summary>
    public event Action<StaffMember>? FocusRequested;

    public StaffMember? Member => _member;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card(20));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 14);
        _avatar = HudWidgets.Avatar();
        header.AddChild(_avatar.Badge);
        var who = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        who.AddThemeConstantOverride("separation", 0);
        _name.AddThemeFontSizeOverride("font_size", 20);
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        who.AddChild(_name);
        who.AddChild(_role);
        header.AddChild(who);
        header.AddChild(NameEditing.Attach(who, _name, () => _member?.Name, name =>
        {
            if (_member is { } m)
            {
                _sim.RenameStaff(m.Id, name);
                Refresh();
            }
        }));
        var close = UiStyle.SecondaryButton("✕", () => CloseRequested?.Invoke());
        close.TooltipText = "Close (Esc)";
        close.CustomMinimumSize = new Vector2(40, 40);
        header.AddChild(close);
        column.AddChild(header);
        column.AddChild(HudWidgets.Divider());

        _now = HudWidgets.FocusCard();
        _now.Heading.Text = "Doing now";
        column.AddChild(_now.Box);

        var meters = new HBoxContainer();
        meters.AddThemeConstantOverride("separation", 14);
        _energy = HudWidgets.Meter("Energy");
        _morale = HudWidgets.Meter("Morale");
        meters.AddChild(_energy.Root);
        meters.AddChild(_morale.Root);
        column.AddChild(meters);

        var tiles = new HBoxContainer();
        tiles.AddThemeConstantOverride("separation", 8);
        _tasks = HudWidgets.StatTile("Jobs done");
        _skill = HudWidgets.StatTile("Clinical skill");
        _experience = HudWidgets.StatTile("Experience");
        tiles.AddChild(_tasks.Tile);
        tiles.AddChild(_skill.Tile);
        tiles.AddChild(_experience.Tile);
        column.AddChild(tiles);

        _queueTitle = HudWidgets.Section("Up next");
        column.AddChild(_queueTitle);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _queue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _queue.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_queue);
        column.AddChild(scroll);

        column.AddChild(_footer);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        var find = UiStyle.Button("Find on map", () =>
        {
            if (_member is not null)
            {
                FocusRequested?.Invoke(_member);
            }
        });
        find.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        find.CustomMinimumSize = new Vector2(0, 44);
        buttons.AddChild(find);
        _letGo = UiStyle.SecondaryButton("Let go", OnLetGo);
        _letGo.TooltipText = "Let this member of staff go. Whatever they were doing goes back to the team.";
        _letGo.CustomMinimumSize = new Vector2(0, 44);
        buttons.AddChild(_letGo);
        column.AddChild(buttons);
    }

    public void Show(StaffMember member)
    {
        _member = member;
        ResetLetGo();
        _queueSignature = string.Empty;
        Visible = true;
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _member is null)
        {
            return;
        }

        if (_confirmLetGo > 0)
        {
            _confirmLetGo -= delta;
            if (_confirmLetGo <= 0)
            {
                ResetLetGo();
            }
        }

        _refresh -= delta;
        if (_refresh <= 0)
        {
            _refresh = 0.4;
            Refresh();
        }
    }

    /// <summary>The first press asks to confirm; the second, within a few seconds, lets them go.</summary>
    private void OnLetGo()
    {
        if (_member is null)
        {
            return;
        }

        if (_confirmLetGo <= 0)
        {
            _confirmLetGo = 4;
            _letGo.Text = "Confirm: let go";
            _letGo.AddThemeColorOverride("font_color", UiStyle.Red);
            return;
        }

        _sim.Fire(_member.Id);
        _member = null;
        ResetLetGo();
        CloseRequested?.Invoke();
    }

    private void ResetLetGo()
    {
        _confirmLetGo = 0;
        _letGo.Text = "Let go";
        _letGo.RemoveThemeColorOverride("font_color");
    }

    private void Refresh()
    {
        if (_member is not { } m)
        {
            return;
        }

        _name.Text = m.Name;
        var place = m.BaseRoomId is { } roomId && _sim.Spaces.FirstOrDefault(s => s.Id == roomId) is { } room ? room.Name : "A&E";
        _role.Text = $"{m.Grade} · {place}";
        var uniform = Looks.Staff(m).Top;
        HudWidgets.SetAvatar(_avatar, m.Name, uniform.Luminance > 0.85f ? new Color("#5A6B7A") : uniform);

        var task = m.CurrentTaskId is { } taskId ? _sim.Tasks.FirstOrDefault(t => t.Id == taskId) : null;
        _now.Heading.Text = m.Activity switch
        {
            StaffActivity.OnBreak or StaffActivity.WalkingToBreak => "On a break",
            StaffActivity.Idle => "Free",
            _ => "Doing now",
        };
        _now.Text.Text = m.StatusText;
        var patient = task is null ? null : _sim.FindPatient(task.PatientId);
        _now.Detail.Text = m.Activity switch
        {
            StaffActivity.OnBreak or StaffActivity.WalkingToBreak when m.BreakEndsAt is { } back => $"Back at {back:HH:mm}",
            _ when patient is not null => $"For {patient.FullName}, {patient.Age}{(m.IsMoving ? " · on the way" : string.Empty)}",
            StaffActivity.Idle => "Waiting for the next job",
            _ => string.Empty,
        };
        _now.Detail.Visible = _now.Detail.Text.Length > 0;

        var energy = 1 - m.Fatigue;
        var energyColour = energy >= 0.5 ? UiStyle.Green : energy >= 0.25 ? new Color(ClinicalText.AmberHex) : UiStyle.Red;
        HudWidgets.SetMeter(_energy, energy, $"{energy:P0}", energyColour);
        var mood = m.Morale >= 0.7 ? "Good" : m.Morale >= 0.4 ? "OK" : "Low";
        HudWidgets.SetMeter(_morale, m.Morale, mood, UiStyle.Blue);

        _tasks.Value.Text = m.TasksCompleted.ToString();
        _skill.Value.Text = $"{m.Skill:P0}";
        _experience.Value.Text = m.YearsExperience == 1 ? "1 year" : $"{m.YearsExperience} years";
        _footer.Text = $"Age {m.Age} · £{m.Salary:N0} a year · shifts change at 08:00 and 20:00";

        RefreshQueue(m);
    }

    /// <summary>The most urgent work on the board that someone in this role could pick up next.</summary>
    private void RefreshQueue(StaffMember m)
    {
        var waiting = _sim.Tasks
            .Where(t => t.Role == m.Role && t.AssignedStaffId is null && t.Id != m.CurrentTaskId)
            .OrderByDescending(t => t.Priority)
            .Take(4)
            .Select(t => (Label: t.Label, Who: _sim.FindPatient(t.PatientId) is { } p ? $"{p.FullName}, {p.Age}" : string.Empty, Waiting: _sim.Now - t.CreatedAt))
            .ToList();
        var signature = string.Join("|", waiting.Select(w => $"{w.Label}{w.Who}{(int)w.Waiting.TotalMinutes}"));
        if (signature == _queueSignature)
        {
            return;
        }

        _queueSignature = signature;
        foreach (var child in _queue.GetChildren())
        {
            child.QueueFree();
        }

        _queueTitle.Text = $"Waiting for a {RoleName(m.Role)}";
        if (waiting.Count == 0)
        {
            _queue.AddChild(UiStyle.Body("Nothing waiting. They'll pick up the next job as it comes in.", muted: true));
            return;
        }

        foreach (var (label, who, wait) in waiting)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#F7F9FB"), 10, null, 10));
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            var text = new Label { Text = string.IsNullOrEmpty(who) ? label : $"{label} · {who}", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddThemeFontSizeOverride("font_size", 14);
            line.AddChild(text);
            var minutes = UiStyle.Caption(ClinicalText.Duration(wait), 13);
            if (wait.TotalMinutes >= 30)
            {
                minutes.AddThemeColorOverride("font_color", UiStyle.AmberText);
            }

            line.AddChild(minutes);
            row.AddChild(line);
            _queue.AddChild(row);
        }
    }

    private static string RoleName(StaffRole role) => StaffGrades.RoleName(role);
}
