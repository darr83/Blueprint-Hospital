using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The notifications box in the top bar. It shows only the latest notification; hovering over it drops
/// down the last five. Any notification about a patient can be clicked to open their record.
/// </summary>
public partial class NotificationFeed : PanelContainer
{
    /// <summary>How many notifications the hover list shows.</summary>
    public const int Shown = 5;

    private const double CloseDelay = 0.25;
    private readonly ColorRect _stripe = new() { CustomMinimumSize = new Vector2(4, 0), MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _time = new() { VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _text = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        ClipText = true,
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private readonly Label _count = new() { VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer _list = new();
    private HospitalSimulation _sim = null!;
    private IDisposable? _subscription;
    private bool _dirty = true;
    private double _closeTimer;
    private int? _latestPatient;

    /// <summary>Raised when the player clicks a notification about a patient.</summary>
    public event Action<int>? PatientClicked;

    /// <summary>The drop-down list of the last five notifications. The HUD adds it on top of the other panels.</summary>
    public PanelContainer Dropdown { get; } = new() { Name = "NotificationList", Visible = false, MouseFilter = MouseFilterEnum.Stop };

    /// <summary>Keeps the drop-down open without hovering (used by the smoke test's screenshots).</summary>
    public bool PinnedOpen { get; set; }

    /// <summary>How many rows the drop-down is showing.</summary>
    public int RowsShown => _list.GetChildren().OfType<HBoxContainer>().Count(c => !c.IsQueuedForDeletion());

    public void Initialise(HospitalSimulation simulation, EventBus events)
    {
        _sim = simulation;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var style = UiStyle.Box(UiStyle.Ground, 10, UiStyle.Border);
        style.ContentMarginLeft = 10;
        style.ContentMarginRight = 12;
        style.ContentMarginTop = style.ContentMarginBottom = 6;
        AddThemeStyleboxOverride("panel", style);
        CustomMinimumSize = new Vector2(220, 40);
        TooltipText = string.Empty;

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        AddChild(row);
        row.AddChild(_stripe);
        _time.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        _time.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(_time);
        _text.AddThemeColorOverride("font_color", UiStyle.TextDark);
        _text.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(_text);
        _count.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        _count.AddThemeFontSizeOverride("font_size", 12);
        _count.Text = "▾";
        row.AddChild(_count);

        Dropdown.AddThemeStyleboxOverride("panel", UiStyle.Card(12, 12));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        Dropdown.AddChild(column);
        var title = new Label { Text = $"Last {Shown} events" };
        title.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        title.AddThemeFontSizeOverride("font_size", 12);
        column.AddChild(title);
        _list.AddThemeConstantOverride("separation", 4);
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.AddChild(_list);

        _subscription = events.Subscribe<Notification>(_ => _dirty = true);
    }

    public override void _ExitTree() => _subscription?.Dispose();

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && _latestPatient is { } id)
        {
            PatientClicked?.Invoke(id);
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        if (_dirty)
        {
            _dirty = false;
            Refresh();
        }

        UpdateDropdown(delta);
    }

    private void UpdateDropdown(double delta)
    {
        // Open while the mouse is over the box or the list under it, with a short grace period so the
        // pointer can cross the gap between them.
        var mouse = GetViewport().GetMousePosition();
        var over = IsVisibleInTree() && (GetGlobalRect().Grow(2).HasPoint(mouse) || (Dropdown.Visible && Dropdown.GetGlobalRect().Grow(8).HasPoint(mouse)));
        if (over || PinnedOpen)
        {
            _closeTimer = CloseDelay;
        }
        else
        {
            _closeTimer -= delta;
        }

        var show = _closeTimer > 0 && IsVisibleInTree();
        if (show)
        {
            var rect = GetGlobalRect();
            var width = Math.Max(rect.Size.X, 440);
            Dropdown.Size = new Vector2(width, 0);
            Dropdown.GlobalPosition = new Vector2(rect.Position.X, rect.End.Y + 6);
        }

        Dropdown.Visible = show;
    }

    private void Refresh()
    {
        var items = _sim.Notifications.Recent.Take(Shown).ToList();
        var latest = items.FirstOrDefault();
        _latestPatient = latest?.PatientId;
        _stripe.Color = latest is null ? UiStyle.Border : StripeColour(latest);
        _time.Text = latest is null ? string.Empty : $"{latest.Time:HH:mm}";
        _time.Visible = latest is not null;
        _text.Text = latest?.Text ?? "All quiet.";
        _text.AddThemeColorOverride("font_color", latest is null ? UiStyle.TextMuted : UiStyle.TextDark);
        TooltipText = string.Empty;

        foreach (var child in _list.GetChildren())
        {
            child.QueueFree();
        }

        if (items.Count == 0)
        {
            var quiet = new Label { Text = "All quiet.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            quiet.AddThemeColorOverride("font_color", UiStyle.TextMuted);
            _list.AddChild(quiet);
        }

        foreach (var n in items)
        {
            _list.AddChild(Row(n));
        }
    }

    private HBoxContainer Row(Notification n)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new ColorRect { CustomMinimumSize = new Vector2(4, 0), Color = StripeColour(n) });

        var text = $"{n.Time:HH:mm}  {n.Text}";
        if (n.PatientId is { } id)
        {
            var button = HudStyle.RowButton(text, () => PatientClicked?.Invoke(id));
            button.TooltipText = "Open the patient's record";
            row.AddChild(button);
        }
        else
        {
            var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            label.AddThemeColorOverride("font_color", UiStyle.TextDark);
            label.AddThemeFontSizeOverride("font_size", 14);
            var box = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            box.AddThemeStyleboxOverride("panel", HudStyle.RowBox(new Color("#F7F9FB")));
            box.AddChild(label);
            row.AddChild(box);
        }

        return row;
    }

    private static Color StripeColour(Notification n) => n.Triage is { } t ? ClinicalText.TriageColour(t) : n.Level switch
    {
        NotificationLevel.Critical => new Color(ClinicalText.RedHex),
        NotificationLevel.Warning => new Color(ClinicalText.AmberHex),
        _ => new Color("#5B8DB8"),
    };
}
