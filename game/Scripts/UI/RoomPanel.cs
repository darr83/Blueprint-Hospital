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
    private readonly HBoxContainer _doorRow = new();
    private readonly VBoxContainer _visiting = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label _visitFrom = new() { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(58, 0) };
    private readonly Label _visitTo = new() { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(70, 0) };
    private readonly Label _visitAtOnce = new() { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(30, 0) };
    private readonly Label _visitNote = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Dictionary<WallSide, Button> _doorButtons = new();
    private HospitalSimulation _sim = null!;
    private string? _roomId;
    private double _refresh;
    private int _doorTick;

    /// <summary>Raised when the player wants to move or resize the room.</summary>
    public event Action<CareSpace>? MoveRequested;

    /// <summary>Raised when the player wants to place the room's door by hand, anywhere along any wall.</summary>
    public event Action<CareSpace>? DoorMoveRequested;

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
        UiStyle.Strong(_name, 18);
        _name.TextSubmitted += text =>
        {
            if (_roomId is not null)
            {
                _sim.RenameRoom(_roomId, text);
            }

            _name.ReleaseFocus();
        };
        header.AddChild(_name);
        var close = UiStyle.CloseButton(Close);
        header.AddChild(close);
        column.AddChild(header);

        _info.AddThemeColorOverride("default_color", UiStyle.TextDark);
        column.AddChild(_info);
        _access.AddThemeConstantOverride("separation", 2);
        column.AddChild(_access);
        BuildVisiting();
        column.AddChild(_visiting);

        // Door: one button per wall. Moving the door turns the room's layout with it.
        _doorRow.AddThemeConstantOverride("separation", 4);
        var doorLabel = new Label { Text = "Door", CustomMinimumSize = new Vector2(44, 0), VerticalAlignment = VerticalAlignment.Center };
        doorLabel.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        _doorRow.AddChild(doorLabel);
        foreach (var side in new[] { WallSide.West, WallSide.North, WallSide.East, WallSide.South })
        {
            var button = new Button { Text = ShortFacing(side), ToggleMode = true, FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiStyle.Secondary, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
            button.Pressed += () => MoveDoor(side);
            _doorButtons[side] = button;
            _doorRow.AddChild(button);
        }

        // Or place it anywhere along any wall, with the furniture laid out again around it.
        var place = new Button { Text = "Place…", TooltipText = "Point at any wall of the room and click to put the door exactly there; the furniture is laid out again around it", FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiStyle.Secondary, CustomMinimumSize = new Vector2(64, 32) };
        place.Pressed += () =>
        {
            if (_roomId is not null && _sim.FindSpace(_roomId) is { } room)
            {
                DoorMoveRequested?.Invoke(room);
            }
        };
        _doorRow.AddChild(place);

        column.AddChild(_doorRow);

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
        _doorTick = 0;
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
        var text = template is null ? string.Empty : BuildBar.Describe(template, room, _sim);
        text = text.Replace($"[color=#5B6B79]Cost[/color]  £{room.BuildCost:N0}", $"[color=#5B6B79]Value[/color]  £{room.BuildCost:N0}");
        var header = $"[color=#5B6B79]{template?.Name ?? room.Kind.ToString()} · door faces {Facing(room.DoorSide)}[/color]";
        var notInUse = room.InUse ? string.Empty : "[color=#C62828][b]NOT IN USE[/b]: its door doesn't open onto a corridor. Lay a corridor to the door, or move the door.[/color]\n";
        _info.Text = UiStyle.Facts($"{notInUse}{header}\n{text}\n[color=#5B6B79]In use[/color]  {Usage(room)}");

        var check = _sim.CheckDemolish(room.Id);
        _demolish.Text = $"Demolish (+£{-check.Cost:N0})";
        _move.Visible = room.Kind != CareSpaceKind.Corridor;
        RefreshDoors(room);
        RefreshVisiting(room);
    }

    /// <summary>Visiting hours for a ward: from, to, and how many visitors may be at a bed at once.</summary>
    private void BuildVisiting()
    {
        _visiting.AddThemeConstantOverride("separation", 4);
        var heading = new Label { Text = "Visiting hours" };
        heading.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        _visiting.AddChild(heading);

        var hours = new HBoxContainer();
        hours.AddThemeConstantOverride("separation", 2);
        hours.AddChild(Step("−", "Start visiting an hour earlier", h => h with { FromHour = h.FromHour - 1 }));
        hours.AddChild(_visitFrom);
        hours.AddChild(Step("+", "Start visiting an hour later", h => h with { FromHour = h.FromHour + 1 }));
        var to = new Label { Text = "to", HorizontalAlignment = HorizontalAlignment.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        to.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        hours.AddChild(to);
        hours.AddChild(Step("−", "End visiting an hour earlier", h => h with { ToHour = h.ToHour - 1 }));
        hours.AddChild(_visitTo);
        hours.AddChild(Step("+", "End visiting an hour later", h => h with { ToHour = h.ToHour + 1 }));
        _visiting.AddChild(hours);

        var atOnce = new HBoxContainer();
        atOnce.AddThemeConstantOverride("separation", 2);
        var label = new Label { Text = "Visitors at a bed", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.AddThemeColorOverride("font_color", UiStyle.TextDark);
        atOnce.AddChild(label);
        atOnce.AddChild(Step("−", "Fewer visitors at a bed at a time", h => h with { AtOnce = h.AtOnce - 1 }));
        atOnce.AddChild(_visitAtOnce);
        atOnce.AddChild(Step("+", "More visitors at a bed at a time", h => h with { AtOnce = h.AtOnce + 1 }));
        _visiting.AddChild(atOnce);

        _visitNote.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        _visiting.AddChild(_visitNote);
        foreach (var l in new[] { _visitFrom, _visitTo, _visitAtOnce })
        {
            l.AddThemeColorOverride("font_color", UiStyle.TextDark);
        }
    }

    private Button Step(string text, string tip, Func<VisitingHours, VisitingHours> change)
    {
        var button = new Button { Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiStyle.Secondary, CustomMinimumSize = new Vector2(30, 28) };
        button.Pressed += () =>
        {
            if (_roomId is null || _sim.FindSpace(_roomId) is not { } room || !_sim.HasVisiting(room))
            {
                return;
            }

            var next = change(_sim.VisitingHoursOf(room));
            _sim.SetVisitingHours(room.Id, next.FromHour, next.ToHour, next.AtOnce);
            RefreshVisiting(room);
        };
        return button;
    }

    private void RefreshVisiting(CareSpace room)
    {
        _visiting.Visible = _sim.HasVisiting(room);
        if (!_visiting.Visible)
        {
            return;
        }

        var hours = _sim.VisitingHoursOf(room);
        _visitFrom.Text = $"{hours.FromHour:00}:00";
        _visitTo.Text = hours.ToHour == 24 ? "midnight" : $"{hours.ToHour:00}:00";
        _visitAtOnce.Text = hours.AtOnce.ToString();
        var here = _sim.VisitorsOnWard(room).Count();
        var open = hours.IsOpenAt(_sim.Now) ? $"Open now · {here} visitor{(here == 1 ? string.Empty : "s")} on the ward" : $"Closed now · {here} visitor{(here == 1 ? string.Empty : "s")} on the ward";
        var partner = _sim.IsMaternityWard(room) ? "\nPartners can visit at any time." : string.Empty;
        _visitNote.Text = open + partner;
    }

    /// <summary>Lights the wall the door is on; the others say what moving it there would cost, or why it can't go there.</summary>
    private void RefreshDoors(CareSpace room)
    {
        var template = _sim.Database.RoomTemplates.GetValueOrDefault(room.TemplateId);
        _doorRow.Visible = room.Kind is not (CareSpaceKind.Corridor or CareSpaceKind.Waiting) && template is { IsShaft: false };
        // Each check lays the room out again and walks the routes, so only every couple of seconds.
        if (!_doorRow.Visible || (_doorTick++ % 4 != 0 && _doorButtons[room.DoorSide].ButtonPressed))
        {
            return;
        }

        foreach (var (side, button) in _doorButtons)
        {
            button.SetPressedNoSignal(side == room.DoorSide);
            if (side == room.DoorSide)
            {
                button.Disabled = false;
                button.TooltipText = $"The door is on the {Facing(side)} wall";
                continue;
            }

            var check = _sim.CheckDoorSide(room.Id, side);
            button.Disabled = !check.CanBuild;
            button.TooltipText = check.CanBuild
                ? $"Move the door to the {Facing(side)} wall and lay the room out again from there: £{check.Cost:N0}" + (check.Notes.Count > 0 ? $"\n⚠ {check.Notes[0]}" : string.Empty)
                : string.Join("\n", check.Problems);
        }
    }

    private void MoveDoor(WallSide side)
    {
        if (_roomId is null || _sim.FindSpace(_roomId) is not { } room || room.DoorSide == side)
        {
            Refresh();
            return;
        }

        var result = _sim.MoveDoor(_roomId, side);
        _doorTick = 0;
        _problems.Text = result.CanBuild ? string.Empty : $"[color=#C4373D]✗ {string.Join("\n✗ ", result.Problems)}[/color]";
        Refresh();
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

        if (_sim.FixedTheatreDepartments(room) is { Count: > 0 } fixedFor)
        {
            // A dedicated theatre, such as the C-section theatre, is only ever used by its own department.
            var names = fixedFor.Select(id => _sim.Database.Departments.TryGetValue(id, out var d) ? d.Name : id);
            var only = new Label { Text = $"Used only by {string.Join(" and ", names)}.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            only.AddThemeColorOverride("font_color", UiStyle.TextMuted);
            _access.AddChild(only);
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

        if (room.IsMultiBed)
        {
            return $"{room.PlacesUsed} of {room.Beds.Count} beds in use";
        }

        if (room.IsWard && _sim.UnitOf(room) == Simulation.Data.Models.WardUnit.Neonatal)
        {
            // Babies, not patients, lie in a neonatal unit's incubators.
            var babies = _sim.BabiesOnUnit(room).OrderBy(b => b.CotIndex).ToList();
            var coming = _sim.BabiesIn(room).Count(b => b.Place != Simulation.Patients.BabyPlace.NeonatalUnit);
            var nurses = _sim.Staff.Where(s => s.BaseRoomId == room.Id && s.Role == Simulation.Staff.StaffRole.Nurse).Select(s => s.Name).ToList();
            var closed = _sim.IsNeonatalUnitOpen(room) ? string.Empty : "[color=#C4373D]Closed: post a nurse here to open it[/color]\n";
            var list = babies.Count == 0 ? "none" : string.Join(", ", babies.Select(b => $"{b.FullName} (incubator {b.CotIndex + 1})"));
            return $"{closed}{babies.Count} of {room.Beds.Count} incubators in use{(coming > 0 ? $" · {coming} on the way" : string.Empty)}"
                   + $"\n[color=#5B6B79]Babies[/color]  {list}"
                   + $"\n[color=#5B6B79]Nurses[/color]  {(nurses.Count > 0 ? string.Join(", ", nurses) : "[color=#C4373D]nobody[/color]")}";
        }

        if (room.IsWard)
        {
            var used = room.Beds.Count(b => b.OccupantId is not null);
            string Team(Simulation.Staff.StaffShift shift)
            {
                var names = _sim.Staff.Where(s => s.BaseRoomId == room.Id && s.Shift == shift).Select(s => s.Name).ToList();
                return names.Count > 0 ? string.Join(", ", names) : "[color=#C4373D]nobody[/color]";
            }

            var closed = _sim.UnitOf(room) == Simulation.Data.Models.WardUnit.IntensiveCare && !_sim.IsIntensiveCareOpen(room)
                ? "[color=#C4373D]Closed: post an anaesthetist here to open it[/color]\n"
                : string.Empty;
            if (_sim.OutbreakOn(room.Id) is { } outbreak)
            {
                var cases = $"{outbreak.CaseIds.Count} case{(outbreak.CaseIds.Count == 1 ? string.Empty : "s")}";
                var cleaned = outbreak.Cleaned ? "deep-cleaned" : $"needs a deep clean by {outbreak.SpreadsAt:HH:mm} or it spreads";
                closed += outbreak.IsClosedAt(_sim.Now)
                    ? $"[color=#C4373D]Closed to admissions until {outbreak.ClosedUntil:ddd HH:mm}: norovirus ({cases}, {cleaned})[/color]\n"
                    : $"[color=#B7791F]Norovirus: {cases}, {cleaned}. {HospitalSimulation.OutbreakCasesToClose} cases close the ward.[/color]\n";
            }

            return $"{closed}{used} of {room.Beds.Count} beds\n[color=#5B6B79]Days[/color]  {Team(Simulation.Staff.StaffShift.Day)}\n[color=#5B6B79]Nights[/color]  {Team(Simulation.Staff.StaffShift.Night)}";
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

        if (room.Kind == CareSpaceKind.Theatre && _sim.TheatreProblems(room) is { Count: > 0 } theatreProblems)
        {
            return $"[color=#C4373D]Closed: needs {string.Join(" and ", theatreProblems)}[/color]";
        }

        if (room.Kind == CareSpaceKind.AnaestheticRoom)
        {
            return _sim.TheatreOf(room) is { } theatre
                ? $"Joined to {theatre.Name} by automatic sliding doors"
                : "[color=#C4373D]Not joined to a theatre: build it against a theatre wall[/color]";
        }

        if (room.Kind == CareSpaceKind.TheatreRecovery)
        {
            var open = _sim.RecoveryBaysOpen(room);
            var used = room.Beds.Count(b => b.OccupantId is not null);
            var line = $"{used} of {room.Beds.Count} bays in use · {open} open";
            return open < room.Beds.Count
                ? $"{line}\n[color=#C4373D]Needs a recovery nurse on duty for every {HospitalSimulation.BaysPerRecoveryNurse} bays[/color]"
                : line;
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

    /// <summary>Short label for a wall, as the player sees it in the default view.</summary>
    public static string ShortFacing(WallSide side) => side switch
    {
        WallSide.North => "NE",
        WallSide.East => "SE",
        WallSide.South => "SW",
        _ => "NW",
    };

    public static string Facing(WallSide side) => side switch
    {
        WallSide.North => "north-east",
        WallSide.East => "south-east",
        WallSide.South => "south-west",
        _ => "north-west",
    };
}
