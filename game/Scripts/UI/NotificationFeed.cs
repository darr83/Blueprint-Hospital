using System;
using System.Linq;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The latest department notifications. Each one about a patient can be clicked to open their record.
/// </summary>
public partial class NotificationFeed : PanelContainer
{
    private const int Shown = 5;
    private readonly VBoxContainer _list = new();
    private HospitalSimulation _sim = null!;
    private IDisposable? _subscription;
    private bool _dirty = true;

    /// <summary>Raised when the player clicks a notification about a patient.</summary>
    public event Action<int>? PatientClicked;

    public void Initialise(HospitalSimulation simulation, EventBus events)
    {
        _sim = simulation;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());
        CustomMinimumSize = new Vector2(318, 0);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        AddChild(column);
        var title = new Label { Text = "Notifications" };
        title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        title.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(title);
        _list.AddThemeConstantOverride("separation", 3);
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // Scrolls rather than running off the bottom of the screen on smaller displays.
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(_list);
        column.AddChild(scroll);

        _subscription = events.Subscribe<Notification>(_ => _dirty = true);
    }

    public override void _ExitTree() => _subscription?.Dispose();

    public override void _Process(double delta)
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        foreach (var child in _list.GetChildren())
        {
            child.QueueFree();
        }

        var items = _sim.Notifications.Recent.Take(Shown).ToList();
        if (items.Count == 0)
        {
            var quiet = new Label { Text = "All quiet.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            quiet.AddThemeColorOverride("font_color", new Color("#5B6B79"));
            _list.AddChild(quiet);
        }

        foreach (var n in items)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            var stripe = new ColorRect
            {
                CustomMinimumSize = new Vector2(4, 0),
                Color = n.Triage is { } t ? ClinicalText.TriageColour(t) : n.Level switch
                {
                    NotificationLevel.Critical => new Color(ClinicalText.RedHex),
                    NotificationLevel.Warning => new Color(ClinicalText.AmberHex),
                    _ => new Color("#5B8DB8"),
                },
            };
            row.AddChild(stripe);

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
                label.AddThemeColorOverride("font_color", new Color("#17232E"));
                label.AddThemeFontSizeOverride("font_size", 14);
                row.AddChild(label);
            }

            _list.AddChild(row);
        }
    }
}
