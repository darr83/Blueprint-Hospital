using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Details of one room: its name (which can be changed), size, capacity, furniture and who is
/// using it, with buttons to move, resize, turn or demolish it.
/// </summary>
public partial class RoomPanel : PanelContainer
{
    private readonly LineEdit _name = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly RichTextLabel _info = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly VBoxContainer _access = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly RichTextLabel _problems = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false };
    private readonly Button _move = new() { Text = "Move or resize", TooltipText = "Drag out the room again where you want it, at the size you want", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38) };
    private readonly Button _demolish = new() { Text = "Demolish", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38) };
    private HospitalSimulation _sim = null!;
    private string? _roomId;
    private double _refresh;

    /// <summary>Raised when the player wants to move or resize the room.</summary>
    public event Action<CareSpace>? MoveRequested;

    public string? RoomId => _roomId;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());
        CustomMinimumSize = new Vector2(360, 0);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        _name.TooltipText = "Type a new name and press Enter";
        _name.TextSubmitted += text =>
        {
            if (_roomId is not null)
            {
                _sim.RenameRoom(_roomId, text);
            }

            _name.ReleaseFocus();
        };
        header.AddChild(_name);
        var close = new Button { Text = "✕", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(40, 40) };
        close.Pressed += Close;
        header.AddChild(close);
        column.AddChild(header);

        _info.AddThemeColorOverride("default_color", new Color("#17232E"));
        column.AddChild(_info);
        _access.AddThemeConstantOverride("separation", 2);
        column.AddChild(_access);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        buttons.AddChild(_move);
        buttons.AddChild(_demolish);
        column.AddChild(buttons);
        column.AddChild(_problems);

        _move.Pressed += () =>
        {
            if (_roomId is not null && _sim.FindSpace(_roomId) is { } room)
            {
                MoveRequested?.Invoke(room);
            }
        };
        _demolish.Pressed += Demolish;
    }

    public void Show(CareSpace room)
    {
        _roomId = room.Id;
        _name.Text = room.Name;
        _problems.Text = string.Empty;
        Visible = true;
        Refresh();
        BuildAccess(room);
    }

    public void Close()
    {
        Visible = false;
        _roomId = null;
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
            Refresh();
        }
    }

    private void Refresh()
    {
        _refresh = 0.5;
        if (_roomId is null || _sim.FindSpace(_roomId) is not { } room)
        {
            Close();
            return;
        }

        var template = _sim.Database.RoomTemplates.GetValueOrDefault(room.TemplateId);
        var text = template is null ? string.Empty : BuildPanel.Describe(template, room, _sim);
        text = text.Replace($"[color=#5B6B79]Cost[/color]  £{room.BuildCost:N0}", $"[color=#5B6B79]Value[/color]  £{room.BuildCost:N0}");
        var header = $"[color=#5B6B79]{template?.Name ?? room.Kind.ToString()} · door faces {Facing(room.DoorSide)}[/color]";
        _info.Text = $"{header}\n{text}\n[color=#5B6B79]In use[/color]  {Usage(room)}";

        var check = _sim.CheckDemolish(room.Id);
        _demolish.Text = $"Demolish (+£{-check.Cost:N0})";
        _move.Visible = room.Kind != CareSpaceKind.Corridor;
    }

    /// <summary>For a theatre: a tick for each department whose patients may be operated on in it, and one for all of them.</summary>
    private void BuildAccess(CareSpace room)
    {
        foreach (var child in _access.GetChildren())
        {
            child.QueueFree();
        }

        _access.Visible = room.Kind == CareSpaceKind.Theatre;
        if (!_access.Visible)
        {
            return;
        }

        var departments = _sim.SurgicalDepartments;
        var heading = new Label { Text = "Departments that can use this theatre" };
        heading.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        _access.AddChild(heading);

        var all = Tick("All departments", UiStyle.TextDark);
        all.SetPressedNoSignal(departments.All(d => room.AllowedDepartments.Contains(d.Id)));
        all.Toggled += on =>
        {
            foreach (var d in departments)
            {
                _sim.SetTheatreAccess(room.Id, d.Id, on);
            }

            BuildAccess(room);
        };
        _access.AddChild(all);

        foreach (var department in departments)
        {
            var unlocked = _sim.IsUnlocked(department.Id);
            var tick = Tick(unlocked ? department.Name : $"{department.Name} (locked)", unlocked ? UiStyle.TextDark : UiStyle.TextMuted);
            tick.TooltipText = unlocked ? $"Let {department.Name} patients have operations here" : $"Unlock {department.Name} in Build › Departments first";
            tick.SetPressedNoSignal(room.AllowedDepartments.Contains(department.Id));
            var id = department.Id;
            tick.Toggled += on =>
            {
                _sim.SetTheatreAccess(room.Id, id, on);
                BuildAccess(room);
            };
            _access.AddChild(tick);
        }

        if (room.AllowedDepartments.Count == 0)
        {
            var warning = new Label { Text = "Nobody can use this theatre until you tick a department.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            warning.AddThemeColorOverride("font_color", UiStyle.AmberText);
            _access.AddChild(warning);
        }
    }

    /// <summary>A plain tick box (the theme otherwise styles it like a button).</summary>
    private static CheckBox Tick(string text, Color colour)
    {
        var tick = new CheckBox { Text = text, FocusMode = FocusModeEnum.None, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
        {
            tick.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }

        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
        {
            tick.AddThemeColorOverride(state, colour);
        }

        return tick;
    }

    private string Usage(CareSpace room)
    {
        if (room.OccupantId is { } id && _sim.FindPatient(id) is { } patient)
        {
            return $"{patient.FullName} ({patient.Age}{(patient.Sex == Simulation.Patients.Sex.Female ? "F" : "M")})";
        }

        if (room.ReservedByStaffId is { } staffId && _sim.FindStaff(staffId) is { } member)
        {
            return $"{member.Name}";
        }

        if (room.IsWard)
        {
            var used = room.Beds.Count(b => b.OccupantId is not null);
            string Team(Simulation.Staff.StaffShift shift)
            {
                var names = _sim.Staff.Where(s => s.BaseRoomId == room.Id && s.Shift == shift).Select(s => s.Name).ToList();
                return names.Count > 0 ? string.Join(", ", names) : "[color=#C4373D]nobody[/color]";
            }

            return $"{used} of {room.Beds.Count} beds\n[color=#5B6B79]Days[/color]  {Team(Simulation.Staff.StaffShift.Day)}\n[color=#5B6B79]Nights[/color]  {Team(Simulation.Staff.StaffShift.Night)}";
        }

        if (room.Kind == CareSpaceKind.Clinic)
        {
            var problems = _sim.ClinicProblems(room);
            var today = _sim.CalendarFor(room.Id, _sim.Now).ToList();
            var seen = today.Count(a => a.Status == Simulation.Patients.AppointmentStatus.Seen);
            var doctors = _sim.Staff.Where(s => s.BaseRoomId == room.Id).Select(s => s.Name);
            return (problems.Count > 0 ? $"[color=#C4373D]Not running: needs {string.Join(" and ", problems)}[/color]" : $"{seen} of {today.Count} seen today (Clinics, C)")
                   + $"\n[color=#5B6B79]Clinic doctor[/color]  {string.Join(", ", doctors)}";
        }

        if (room.Kind == CareSpaceKind.OutpatientReception)
        {
            var desk = _sim.Staff.Where(s => s.BaseRoomId == room.Id).Select(s => s.Name).ToList();
            var waiting = _sim.Outpatients.Count(o => o.Stage == Simulation.Patients.OutpatientStage.Waiting);
            return $"{waiting} clinic patients waiting\n[color=#5B6B79]On the desk[/color]  {(desk.Count > 0 ? string.Join(", ", desk) : "[color=#C4373D]no receptionist[/color]")}";
        }

        var inside = _sim.Patients.Count(p => room.Rect.Contains(p.Tile) && !p.IsMoving) + _sim.Staff.Count(s => s.IsInBuilding && room.Rect.Contains(s.Tile));
        return room.Kind switch
        {
            CareSpaceKind.Corridor => "Corridor",
            _ when inside > 0 => $"{inside} {(inside == 1 ? "person" : "people")} here",
            _ when room.IsTreatmentSpace => "Free",
            _ => "Empty",
        };
    }

    private void Demolish()
    {
        if (_roomId is null)
        {
            return;
        }

        var result = _sim.DemolishRoom(_roomId);
        if (result.CanBuild)
        {
            Close();
            return;
        }

        _problems.Text = $"[color=#C4373D]✗ {string.Join("\n✗ ", result.Problems)}[/color]";
    }

    private static string Facing(WallSide side) => side switch
    {
        WallSide.North => "north-east",
        WallSide.East => "south-east",
        WallSide.South => "south-west",
        _ => "north-west",
    };
}
