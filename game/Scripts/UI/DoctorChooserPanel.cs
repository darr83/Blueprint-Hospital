using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Be A Doctor: the doctors on shift now, each with a button to play as them. Opens from the Doctor
/// button on the left of the main screen.
/// </summary>
public partial class DoctorChooserPanel : PanelContainer
{
    private readonly VBoxContainer _rows = new();
    private readonly Label _note = UiStyle.Caption(string.Empty, 13);
    private HospitalSimulation _sim = null!;
    private string _signature = "\0";
    private double _refresh;

    /// <summary>Raised when the player picks a doctor to play.</summary>
    public event Action<StaffMember>? PlayRequested;

    /// <summary>Raised when the player closes the panel.</summary>
    public event Action? CloseRequested;

    /// <summary>How many doctors are listed (for the smoke test).</summary>
    public int RowsShown => _rows.GetChildCount();

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card(20));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        AddChild(column);

        var header = new HBoxContainer();
        var title = UiStyle.Heading("Be a doctor", 22);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = UiStyle.SecondaryButton("✕", () => CloseRequested?.Invoke());
        close.TooltipText = "Close (Esc)";
        close.CustomMinimumSize = new Vector2(40, 40);
        header.AddChild(close);
        column.AddChild(header);

        var intro = UiStyle.Body("Pick a doctor on shift and see the hospital through their eyes. You walk to your patients, take the history, choose the tests, make the diagnosis and give the treatment. Everyone else carries on as usual.", muted: true);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(intro);
        column.AddChild(HudWidgets.Divider());

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rows.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_rows);
        column.AddChild(scroll);

        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_note);
    }

    public void Open()
    {
        Visible = true;
        _signature = "\0";
        Say(string.Empty);
        Refresh();
    }

    /// <summary>Shows why a doctor could not be taken over (or clears the line).</summary>
    public void Say(string message)
    {
        _note.Text = message.Length > 0 ? message : "Tip: you can also click any doctor on the map and press Be this doctor.";
        _note.AddThemeColorOverride("font_color", message.Length > 0 ? UiStyle.Red : UiStyle.TextMuted);
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _refresh -= delta;
        if (_refresh <= 0)
        {
            _refresh = 0.5;
            Refresh();
        }
    }

    private void Refresh()
    {
        var doctors = _sim.PlayableDoctors.OrderBy(d => d.Name).ToList();
        var signature = string.Join("|", doctors.Select(d => $"{d.Id}:{d.Name}:{d.StatusText}:{d.BaseRoomId}"));
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        if (doctors.Count == 0)
        {
            var none = UiStyle.Body("No doctor is on shift right now. Hire one (H), or wait for the next shift.", muted: true);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _rows.AddChild(none);
            return;
        }

        foreach (var doctor in doctors)
        {
            _rows.AddChild(Row(doctor));
        }
    }

    private Control Row(StaffMember doctor)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Ground, 12, UiStyle.Border, 12));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        card.AddChild(row);

        var avatar = HudWidgets.Avatar(44);
        var uniform = Looks.UniformColour(doctor);
        HudWidgets.SetAvatar(avatar, doctor.Name, uniform.Luminance > 0.85f ? new Color("#5A6B7A") : uniform);
        row.AddChild(avatar.Badge);

        var who = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        who.AddThemeConstantOverride("separation", 0);
        var name = new Label { Text = doctor.Name, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        name.AddThemeFontSizeOverride("font_size", 16);
        who.AddChild(name);
        var place = doctor.BaseRoomId is { } roomId && _sim.Spaces.FirstOrDefault(s => s.Id == roomId) is { } room ? room.Name : "A&E";
        var detail = UiStyle.Caption($"{doctor.Grade} · {place}", 13);
        detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        who.AddChild(detail);
        var doing = UiStyle.Caption(doctor.StatusText, 12);
        doing.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        who.AddChild(doing);
        row.AddChild(who);

        var play = UiStyle.Button("Play", () => PlayRequested?.Invoke(doctor), 84);
        play.FocusMode = FocusModeEnum.None;
        play.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(play);
        return card;
    }
}
