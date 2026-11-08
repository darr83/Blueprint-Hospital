using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The staff rota: who is on the day team (08:00 to 20:00) and the night team (20:00 to 08:00) in
/// each place, today's breaks, and everyone in one list. Drag someone to another place or shift, or
/// use their card on the right to change their shift, move them, send them on a break or let them go.
/// </summary>
public partial class StaffRotaPanel : PanelContainer
{
    private enum Tab
    {
        Rota,
        Breaks,
        Everyone,
    }

    /// <summary>A row of the rota: A&amp;E, a ward, a clinic, a service room or the whole hospital (porters).</summary>
    internal sealed record Place(string? RoomId, string Name, string Sub, StaffRole[] Roles);

    private static readonly Color Hair = new("#E6ECF1");
    private static readonly Color Night = new("#24324A");
    private static readonly Color NightTint = new("#E9ECF3");
    private static readonly Color Amber = new(ClinicalText.AmberHex);
    private static readonly Color GreyDot = new("#A7B2BC");

    private readonly Dictionary<Tab, Button> _tabs = new();
    private readonly Dictionary<string, Button> _filters = new();
    private readonly VBoxContainer _content = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label _caption = UiStyle.Caption(string.Empty, 13);
    private readonly Label _shiftPill = new();
    private PanelContainer _shiftPillBox = null!;
    private readonly List<(PanelContainer Tile, Label Value)> _stats = new();
    private HospitalSimulation _sim = null!;
    private Tab _tab = Tab.Rota;
    private string _filter = "all";
    private int? _selectedId;
    private string _signature = string.Empty;
    private double _refresh;

    // The person card on the right.
    private readonly VBoxContainer _card = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label _empty = UiStyle.Body("Pick someone to see their shift, their breaks and where they work.", muted: true);
    private (PanelContainer Badge, Label Initials) _avatar;
    private readonly Label _name = new();
    private readonly Label _role = UiStyle.Caption(string.Empty, 13);
    private (PanelContainer Box, Label Heading, Label Text, Label Detail) _now;
    private Button _dayButton = null!;
    private Button _nightButton = null!;
    private readonly OptionButton _worksIn = new() { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), ThemeTypeVariation = UiStyle.Secondary };
    private List<StaffPosting> _postings = new();
    private string _postingsSignature = string.Empty;
    private (VBoxContainer Root, Label Value, ProgressBar Bar) _tiredness;
    private (VBoxContainer Root, Label Value, ProgressBar Bar) _skill;
    private readonly VBoxContainer _breakList = new();
    private string _breakSignature = string.Empty;
    private readonly GridContainer _buttons = new() { Columns = 2 };
    private Button _sendOnBreak = null!;
    private Button _letGo = null!;
    private double _confirmLetGo;

    /// <summary>Raised when the player asks to see someone on the map.</summary>
    public event Action<StaffMember>? ShowOnMapRequested;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card(20));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        AddChild(column);

        column.AddChild(BuildHeader());
        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", 10);
        foreach (var caption in new[] { "On shift now", "Working", "On a break", "Off duty", "Gaps in cover" })
        {
            var tile = HudWidgets.StatTile(caption);
            tile.Value.AddThemeFontSizeOverride("font_size", 22);
            _stats.Add(tile);
            stats.AddChild(tile.Tile);
        }

        column.AddChild(stats);
        column.AddChild(BuildTabBar());

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        var main = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        main.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 12, Hair, 0));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            pad.AddThemeConstantOverride($"margin_{side}", 12);
        }

        _content.AddThemeConstantOverride("separation", 0);
        pad.AddChild(_content);
        scroll.AddChild(pad);
        main.AddChild(scroll);
        body.AddChild(main);
        body.AddChild(BuildCard());
        column.AddChild(body);
    }

    /// <summary>Switches to the breaks view (used by the smoke test's screenshots).</summary>
    public void ShowBreaksForTest()
    {
        _tab = Tab.Breaks;
        Rebuild();
    }

    /// <summary>Opens the rota on someone (e.g. from their staff card).</summary>
    public void Select(StaffMember? member)
    {
        _selectedId = member?.Id;
        Rebuild();
    }

    public override void _Process(double delta)
    {
        if (!Visible)
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
        if (_refresh > 0)
        {
            return;
        }

        _refresh = 0.5;
        RefreshHeader();
        RefreshCard();

        // Leave the lists alone while someone is being dragged, so the drop target stays put.
        if (!GetViewport().GuiIsDragging())
        {
            RefreshContent(force: false);
        }
    }

    /// <summary>Rebuilds everything now (after the player changes something).</summary>
    public void Rebuild()
    {
        _signature = string.Empty;
        _postingsSignature = string.Empty;
        _breakSignature = string.Empty;
        RefreshHeader();
        RefreshCard();
        RefreshContent(force: true);
    }

    // ---------------------------------------------------------------- header, stats and tabs

    private Control BuildHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        var title = new Label { Text = "Staff rota" };
        title.AddThemeFontSizeOverride("font_size", 22);
        titles.AddChild(title);
        _caption.Text = "Day shift 08:00 to 20:00 · Night shift 20:00 to 08:00 · Breaks are taken in the staff room";
        titles.AddChild(_caption);
        header.AddChild(titles);

        _shiftPillBox = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _shiftPill.AddThemeFontSizeOverride("font_size", 13);
        _shiftPillBox.AddChild(_shiftPill);
        header.AddChild(_shiftPillBox);

        var close = UiStyle.SecondaryButton("✕", () => Visible = false);
        close.TooltipText = "Close (Esc)";
        close.CustomMinimumSize = new Vector2(40, 40);
        header.AddChild(close);
        return header;
    }

    private Control BuildTabBar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);
        foreach (var (tab, text) in new[] { (Tab.Rota, "Rota"), (Tab.Breaks, "Breaks this shift"), (Tab.Everyone, "Everyone") })
        {
            var button = UiStyle.SecondaryButton(text, () =>
            {
                _tab = tab;
                Rebuild();
            });
            button.ToggleMode = true;
            button.FocusMode = FocusModeEnum.None;
            _tabs[tab] = button;
            bar.AddChild(button);
        }

        bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        foreach (var (key, text) in new[] { ("all", "Everyone"), ("nurse", "Nurses"), ("doctor", "Doctors"), ("porter", "Porters"), ("other", "Others") })
        {
            var chip = UiStyle.SecondaryButton(text, () =>
            {
                _filter = key;
                Rebuild();
            });
            chip.ToggleMode = true;
            chip.FocusMode = FocusModeEnum.None;
            chip.CustomMinimumSize = new Vector2(0, 34);
            chip.AddThemeFontSizeOverride("font_size", 13);
            _filters[key] = chip;
            bar.AddChild(chip);
        }

        return bar;
    }

    private void RefreshHeader()
    {
        foreach (var (tab, button) in _tabs)
        {
            button.SetPressedNoSignal(tab == _tab);
        }

        foreach (var (key, chip) in _filters)
        {
            chip.SetPressedNoSignal(key == _filter);
        }

        var day = _sim.CurrentShift == StaffShift.Day;
        var left = _sim.CurrentShiftEnd - _sim.Now;
        _shiftPill.Text = day
            ? $"Day shift · night team in at 20:00 ({ClinicalText.Duration(left)})"
            : $"Night shift · day team in at 08:00 ({ClinicalText.Duration(left)})";
        _shiftPill.AddThemeColorOverride("font_color", day ? UiStyle.AmberText : Colors.White);
        var pill = UiStyle.Box(day ? UiStyle.AmberTint : Night, 14, day ? UiStyle.AmberBorder : Night, 0);
        pill.ContentMarginLeft = pill.ContentMarginRight = 12;
        pill.ContentMarginTop = pill.ContentMarginBottom = 5;
        _shiftPillBox.AddThemeStyleboxOverride("panel", pill);

        var onDuty = _sim.OnDutyStaff.ToList();
        var gaps = _sim.RecommendedStaffing().Count(n => n.Short > 0);
        _stats[0].Value.Text = onDuty.Count.ToString();
        _stats[1].Value.Text = onDuty.Count(s => s.CurrentTaskId is not null || s.Activity == StaffActivity.Working).ToString();
        _stats[2].Value.Text = onDuty.Count(s => s.IsOnBreak).ToString();
        _stats[3].Value.Text = _sim.Staff.Count(s => !s.IsOnDuty).ToString();
        _stats[4].Value.Text = gaps.ToString();
        _stats[4].Value.AddThemeColorOverride("font_color", gaps > 0 ? UiStyle.AmberText : UiStyle.TextDark);
        _stats[4].Tile.AddThemeStyleboxOverride("panel", gaps > 0
            ? UiStyle.Box(UiStyle.AmberTint, 10, UiStyle.AmberBorder, 10)
            : UiStyle.Box(UiStyle.Surface, 10, Hair, 10));
    }

    // ---------------------------------------------------------------- the three views

    private void RefreshContent(bool force)
    {
        var signature = $"{_tab}|{_filter}|{_selectedId}|{_sim.CurrentShift}|" + string.Join(",", _sim.Staff.Select(s =>
            $"{s.Id}{s.Name}{s.Shift}{s.BaseRoomId}{s.HasPendingMove}{Status(s)}{(int)(s.Fatigue * 20)}{s.Breaks.Count(b => b.Done)}{s.Breaks.Count}{s.Breaks.Count(_sim.IsBreakLate)}"))
            + "|" + string.Join(",", _sim.Spaces.Where(s => s.IsWard || s.IsServiceRoom || s.Kind is CareSpaceKind.Clinic or CareSpaceKind.OutpatientReception).Select(s => s.Id + s.Name))
            + (_tab == Tab.Breaks ? "|" + (_sim.Now.Hour * 12) + (_sim.Now.Minute / 5) : string.Empty);
        if (!force && signature == _signature)
        {
            return;
        }

        _signature = signature;
        foreach (var child in _content.GetChildren())
        {
            child.QueueFree();
        }

        switch (_tab)
        {
            case Tab.Rota:
                BuildRota();
                break;
            case Tab.Breaks:
                BuildBreaks();
                break;
            default:
                BuildEveryone();
                break;
        }
    }

    private bool Shown(StaffMember m) => Shown(m.Role);

    private bool Shown(StaffRole role) => _filter switch
    {
        "nurse" => role is StaffRole.Nurse or StaffRole.HealthcareAssistant,
        "doctor" => role == StaffRole.Doctor,
        "porter" => role == StaffRole.Porter,
        "other" => role is not (StaffRole.Nurse or StaffRole.HealthcareAssistant or StaffRole.Doctor or StaffRole.Porter),
        _ => true,
    };

    /// <summary>Every place someone can work, in rota order.</summary>
    private List<Place> Places()
    {
        var places = new List<Place> { new(null, "A&E", "Emergency department", new[] { StaffRole.Nurse, StaffRole.HealthcareAssistant, StaffRole.Doctor, StaffRole.Receptionist }) };
        foreach (var ward in _sim.Wards.OrderBy(w => w.Id, StringComparer.Ordinal))
        {
            var used = ward.Beds.Count(b => b.OccupantId.HasValue);
            var doctors = _sim.UnitOf(ward) == WardUnit.IntensiveCare ? StaffRole.Anaesthetist : StaffRole.Doctor;
            places.Add(new(ward.Id, ward.Name, $"{ward.Beds.Count} beds · {used} in use", new[] { StaffRole.Nurse, StaffRole.HealthcareAssistant, doctors }));
        }

        foreach (var clinic in _sim.ClinicRooms)
        {
            places.Add(new(clinic.Id, clinic.Name, "Outpatient clinic · days only", new[] { StaffRole.Doctor }));
        }

        foreach (var desk in _sim.SpacesOfKind(CareSpaceKind.OutpatientReception))
        {
            places.Add(new(desk.Id, desk.Name, "Outpatients · days only", new[] { StaffRole.Receptionist }));
        }

        foreach (var room in _sim.Spaces.Where(s => s.IsServiceRoom).OrderBy(s => s.Kind).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            var role = HospitalSimulation.RoleForRoom(room.Kind);
            places.Add(room.Kind == CareSpaceKind.Theatre
                ? new(room.Id, room.Name, "Surgeons and anaesthetists", new[] { role, StaffRole.Anaesthetist })
                : new(room.Id, room.Name, Capitalise(StaffGrades.RoleName(role, plural: true)), new[] { role }));
        }

        places.Add(new(null, "Whole hospital", "Porters", new[] { StaffRole.Porter }));
        return places;
    }

    private static bool IsDayOnly(Place place) => place.Sub.EndsWith("days only", StringComparison.Ordinal);

    private void BuildRota()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        var placeHead = UiStyle.Caption("Place", 12);
        placeHead.CustomMinimumSize = new Vector2(160, 0);
        header.AddChild(placeHead);
        header.AddChild(ShiftHeading(StaffShift.Day));
        header.AddChild(ShiftHeading(StaffShift.Night));
        _content.AddChild(header);
        _content.AddChild(Spacer(8));

        var needs = _sim.RecommendedStaffing();
        foreach (var place in Places())
        {
            if (!place.Roles.Any(Shown))
            {
                continue;
            }

            _content.AddChild(HudWidgets.Divider());
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var label = new VBoxContainer { CustomMinimumSize = new Vector2(160, 0) };
            label.AddThemeConstantOverride("separation", 0);
            var name = new Label { Text = place.Name, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            name.AddThemeFontSizeOverride("font_size", 15);
            label.AddChild(name);
            label.AddChild(UiStyle.Caption(place.Sub, 12));
            row.AddChild(label);
            row.AddChild(Cell(place, StaffShift.Day, needs));
            row.AddChild(Cell(place, StaffShift.Night, needs));
            var wrap = new MarginContainer();
            wrap.AddThemeConstantOverride("margin_top", 8);
            wrap.AddThemeConstantOverride("margin_bottom", 8);
            wrap.AddChild(row);
            _content.AddChild(wrap);
        }

        _content.AddChild(Spacer(8));
        var hint = UiStyle.Caption("Drag someone to another place or to the other shift. A move happens once they've finished with their patient; a change of shift sends them home or brings them in.", 12);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(hint);
    }

    private Control ShiftHeading(StaffShift shift)
    {
        var box = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(Dot(shift == StaffShift.Day ? Amber : Night, 10));
        var on = _sim.CurrentShift == shift ? " · on now" : string.Empty;
        var text = new Label { Text = $"{(shift == StaffShift.Day ? "Day" : "Night")} · {HospitalSimulation.ShiftHoursText(shift)}{on}" };
        text.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(text);
        return box;
    }

    private Control Cell(Place place, StaffShift shift, IReadOnlyList<StaffingNeed> needs)
    {
        var cell = new RotaCell
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CanAccept = id => _sim.FindStaff(id) is { } m && place.Roles.Contains(m.Role) && _sim.CanMoveTo(m, place.RoomId) && !(IsDayOnly(place) && shift == StaffShift.Night),
            Accept = id => Drop(id, place, shift),
            Normal = UiStyle.Box(shift == StaffShift.Day ? new Color("#FBFCFD") : new Color("#F6F7FA"), 10, null, 8),
            Highlight = UiStyle.Box(UiStyle.BlueTint, 10, UiStyle.Blue, 8),
        };
        cell.ShowNormal();
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        cell.AddChild(column);

        if (IsDayOnly(place) && shift == StaffShift.Night)
        {
            column.AddChild(UiStyle.Caption("Closed at night", 12));
            return cell;
        }

        var placeNeeds = needs.Where(n => n.RoomId == place.RoomId && n.Shift == shift && place.Roles.Contains(n.Role)).ToList();
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 4);
        foreach (var need in placeNeeds)
        {
            var chip = HudWidgets.Chip();
            var role = need.Recommended == 1 ? StaffGrades.RoleName(need.Role) : StaffGrades.RoleName(need.Role, plural: true);
            HudWidgets.SetChip(chip, $"{need.Have}/{need.Recommended} {role}", need.Short > 0 ? UiStyle.AmberTint : UiStyle.BlueTint);
            chip.Text.AddThemeColorOverride("font_color", need.Short > 0 ? UiStyle.AmberText : UiStyle.Blue);
            chip.Text.AddThemeFontSizeOverride("font_size", 12);
            chips.AddChild(chip.Chip);
        }

        column.AddChild(chips);

        var people = new HFlowContainer();
        people.AddThemeConstantOverride("h_separation", 6);
        people.AddThemeConstantOverride("v_separation", 6);
        foreach (var member in _sim.Staff.Where(s => s.Shift == shift && s.BaseRoomId == place.RoomId && place.Roles.Contains(s.Role) && Shown(s))
                     .OrderBy(s => s.Role).ThenByDescending(s => s.Skill))
        {
            people.AddChild(Pill(member));
        }

        if (people.GetChildCount() > 0)
        {
            column.AddChild(people);
        }

        var missing = placeNeeds.Where(n => n.Have == 0).Select(n => StaffGrades.RoleName(n.Role)).ToList();
        if (missing.Count > 0)
        {
            var gap = new PanelContainer();
            gap.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.AmberTint, 8, UiStyle.AmberBorder, 8));
            var text = UiStyle.Body($"No {string.Join(" or ", missing)} on {(shift == StaffShift.Day ? "days" : "nights")}. Drag someone here or hire one.");
            text.AddThemeColorOverride("font_color", UiStyle.AmberText);
            text.AddThemeFontSizeOverride("font_size", 12);
            gap.AddChild(text);
            column.AddChild(gap);
        }

        return cell;
    }

    private void Drop(int staffId, Place place, StaffShift shift)
    {
        if (_sim.FindStaff(staffId) is not { } member)
        {
            return;
        }

        if (member.BaseRoomId != place.RoomId || member.HasPendingMove)
        {
            _sim.MoveStaff(staffId, place.RoomId);
        }

        if (member.Shift != shift)
        {
            _sim.SetShift(staffId, shift);
        }

        _selectedId = staffId;
        Rebuild();
    }

    private Control Pill(StaffMember member)
    {
        var pill = new StaffPill { StaffId = member.Id, TooltipText = $"{member.Name}, {member.Grade}\n{member.StatusText}" };
        pill.Clicked += () => Select(_sim.FindStaff(member.Id));
        var selected = member.Id == _selectedId;
        var box = UiStyle.Box(member.IsInBuilding ? UiStyle.Surface : new Color("#F7F9FB"), 15, selected ? UiStyle.Blue : UiStyle.Border, 0);
        box.ContentMarginLeft = 3;
        box.ContentMarginRight = 10;
        box.ContentMarginTop = box.ContentMarginBottom = 3;
        if (selected)
        {
            box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = 2;
        }

        pill.AddThemeStyleboxOverride("panel", box);
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 6);
        line.AddChild(Avatar(member, 26));
        var name = new Label { Text = member.Name, MouseFilter = MouseFilterEnum.Ignore };
        name.AddThemeFontSizeOverride("font_size", 13);
        if (!member.IsInBuilding)
        {
            name.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        }

        line.AddChild(name);
        var grade = UiStyle.Caption(ShortGrade(member.Grade), 12);
        grade.MouseFilter = MouseFilterEnum.Ignore;
        line.AddChild(grade);
        if (member.HasPendingMove)
        {
            var moving = UiStyle.Caption($"→ {_sim.PlaceName(member.Role, member.PendingRoomId)}", 12);
            moving.AddThemeColorOverride("font_color", UiStyle.Blue);
            moving.MouseFilter = MouseFilterEnum.Ignore;
            line.AddChild(moving);
        }

        var dot = Dot(StatusColour(member), 8);
        dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        line.AddChild(dot);
        pill.AddChild(line);
        pill.Preview = () =>
        {
            var preview = new PanelContainer { Theme = UiStyle.Theme };
            preview.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 15, UiStyle.Blue, 6));
            var text = new Label { Text = member.Name };
            text.AddThemeFontSizeOverride("font_size", 13);
            preview.AddChild(text);
            return preview;
        };
        return pill;
    }

    private void BuildBreaks()
    {
        var shift = _sim.CurrentShift;
        var start = _sim.CurrentShiftStart;
        var end = _sim.CurrentShiftEnd;

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        var who = UiStyle.Caption($"{(shift == StaffShift.Day ? "Day" : "Night")} team", 12);
        who.CustomMinimumSize = new Vector2(200, 0);
        head.AddChild(who);
        head.AddChild(new TimeAxis { Start = start, End = end, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 18) });
        _content.AddChild(head);

        var any = false;
        foreach (var place in Places())
        {
            var team = _sim.Staff.Where(s => s.Shift == shift && s.BaseRoomId == place.RoomId && place.Roles.Contains(s.Role) && s.IsOnDuty).ToList();
            var shown = team.Where(Shown).ToList();
            if (shown.Count == 0)
            {
                continue;
            }

            any = true;
            _content.AddChild(Spacer(10));
            var title = UiStyle.Caption(place.Name.ToUpperInvariant(), 12);
            _content.AddChild(title);

            var cover = new HBoxContainer();
            cover.AddThemeConstantOverride("separation", 10);
            var coverLabel = UiStyle.Caption("On the floor", 12);
            coverLabel.CustomMinimumSize = new Vector2(200, 0);
            cover.AddChild(coverLabel);
            cover.AddChild(new CoverTrack { Team = team, Start = start, End = end, Now = _sim.Now, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 14) });
            _content.AddChild(cover);

            foreach (var member in shown)
            {
                var row = new StaffPill { StaffId = member.Id, MouseDefaultCursorShape = CursorShape.PointingHand };
                row.Clicked += () => Select(_sim.FindStaff(member.Id));
                row.AddThemeStyleboxOverride("panel", UiStyle.Box(member.Id == _selectedId ? UiStyle.BlueTint : new Color(0, 0, 0, 0), 6, null, 2));
                var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                line.AddThemeConstantOverride("separation", 10);
                var name = new HBoxContainer { CustomMinimumSize = new Vector2(200, 0), MouseFilter = MouseFilterEnum.Ignore };
                name.AddThemeConstantOverride("separation", 8);
                name.AddChild(Avatar(member, 22));
                var label = new Label { Text = member.Name, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
                label.AddThemeFontSizeOverride("font_size", 13);
                name.AddChild(label);
                line.AddChild(name);
                line.AddChild(new BreakTrack { Member = member, Sim = _sim, Start = start, End = end, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 22), MouseFilter = MouseFilterEnum.Ignore });
                row.AddChild(line);
                _content.AddChild(row);
            }
        }

        if (!any)
        {
            _content.AddChild(Spacer(10));
            _content.AddChild(UiStyle.Body("Nobody is at work on this shift yet.", muted: true));
        }

        _content.AddChild(Spacer(12));
        var legend = new HFlowContainer();
        legend.AddThemeConstantOverride("h_separation", 16);
        legend.AddChild(Legend(BreakTrack.TakenColour, false, "Taken"));
        legend.AddChild(Legend(Amber, false, "On a break now"));
        legend.AddChild(Legend(Amber, true, "Coming up"));
        legend.AddChild(Legend(UiStyle.Red, true, "Put off: patients need them"));
        legend.AddChild(Legend(UiStyle.AmberTint, false, "A third or more of the team away"));
        _content.AddChild(legend);
        var other = shift == StaffShift.Day ? "night" : "day";
        var note = UiStyle.Caption($"Each shift has a 30-minute meal break and two 15-minute tea breaks at random times. The {other} team's breaks are planned when they come in.", 12);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(Spacer(6));
        _content.AddChild(note);
    }

    private static Control Legend(Color colour, bool outline, string text)
    {
        var item = new HBoxContainer();
        item.AddThemeConstantOverride("separation", 6);
        var swatch = new PanelContainer { CustomMinimumSize = new Vector2(18, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        swatch.AddThemeStyleboxOverride("panel", outline ? UiStyle.Box(UiStyle.Surface, 3, colour) : UiStyle.Box(colour, 3));
        item.AddChild(swatch);
        item.AddChild(UiStyle.Caption(text, 12));
        return item;
    }

    private void BuildEveryone()
    {
        var widths = new[] { 230f, 150f, 150f, 80f, 120f, 120f, 70f };
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        foreach (var (text, i) in new[] { "Name", "Grade", "Works in", "Shift", "Now", "Tiredness", "Skill" }.Select((t, i) => (t, i)))
        {
            var label = UiStyle.Caption(text, 12);
            label.CustomMinimumSize = new Vector2(widths[i], 0);
            header.AddChild(label);
        }

        _content.AddChild(header);
        _content.AddChild(Spacer(6));

        var rows = _sim.Staff.Where(Shown)
            .OrderBy(s => s.Shift == _sim.CurrentShift ? 0 : 1)
            .ThenBy(s => _sim.PlaceName(s.Role, s.BaseRoomId))
            .ThenBy(s => s.Role)
            .ThenBy(s => s.Name);
        foreach (var member in rows)
        {
            _content.AddChild(HudWidgets.Divider());
            var row = new StaffPill { StaffId = member.Id, MouseDefaultCursorShape = CursorShape.PointingHand };
            row.Clicked += () => Select(_sim.FindStaff(member.Id));
            row.AddThemeStyleboxOverride("panel", UiStyle.Box(member.Id == _selectedId ? UiStyle.BlueTint : new Color(0, 0, 0, 0), 6, null, 5));
            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", 8);

            var name = new HBoxContainer { CustomMinimumSize = new Vector2(widths[0], 0), MouseFilter = MouseFilterEnum.Ignore };
            name.AddThemeConstantOverride("separation", 8);
            name.AddChild(Avatar(member, 24));
            var nameText = new Label { Text = member.Name, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            nameText.AddThemeFontSizeOverride("font_size", 13);
            name.AddChild(nameText);
            line.AddChild(name);
            line.AddChild(Cellish(Capitalise(member.Grade), widths[1]));
            line.AddChild(Cellish(_sim.PlaceName(member.Role, member.BaseRoomId), widths[2]));
            line.AddChild(ChipCell(member.Shift == StaffShift.Day ? "Day" : "Night", member.Shift == StaffShift.Day ? UiStyle.AmberTint : Night, widths[3]));
            line.AddChild(ChipCell(StatusWord(member), StatusTint(member), widths[4]));
            line.AddChild(Bar(member.Fatigue, member.Fatigue >= 0.6 ? Amber : UiStyle.Green, widths[5]));
            line.AddChild(Cellish($"{member.Skill:P0}", widths[6]));
            row.AddChild(line);
            _content.AddChild(row);
        }
    }

    private static Control Cellish(string text, float width)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }

    private static Control ChipCell(string text, Color tint, float width)
    {
        var holder = new HBoxContainer { CustomMinimumSize = new Vector2(width, 0), MouseFilter = MouseFilterEnum.Ignore };
        var chip = HudWidgets.Chip();
        HudWidgets.SetChip(chip, text, tint);
        chip.Text.AddThemeFontSizeOverride("font_size", 12);
        chip.Chip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        holder.AddChild(chip.Chip);
        return holder;
    }

    private static Control Bar(double value, Color colour, float width)
    {
        var holder = new HBoxContainer { CustomMinimumSize = new Vector2(width, 0), MouseFilter = MouseFilterEnum.Ignore };
        holder.AddThemeConstantOverride("separation", 6);
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = value, ShowPercentage = false, CustomMinimumSize = new Vector2(64, 6), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("background", UiStyle.Box(Hair, 3));
        bar.AddThemeStyleboxOverride("fill", UiStyle.Box(colour, 3));
        holder.AddChild(bar);
        holder.AddChild(UiStyle.Caption($"{value:P0}", 12));
        return holder;
    }

    // ---------------------------------------------------------------- the person card

    private Control BuildCard()
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(310, 0) };
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 12, Hair, 14));
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 10);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        _card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _card.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_card);
        outer.AddChild(scroll);
        card.AddChild(outer);

        _empty.CustomMinimumSize = new Vector2(270, 0);
        _card.AddChild(_empty);

        var who = new HBoxContainer();
        who.AddThemeConstantOverride("separation", 12);
        _avatar = HudWidgets.Avatar(52);
        who.AddChild(_avatar.Badge);
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        names.AddThemeConstantOverride("separation", 0);
        _name.AddThemeFontSizeOverride("font_size", 18);
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        names.AddChild(_name);
        _role.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        names.AddChild(_role);
        who.AddChild(names);
        _card.AddChild(who);

        _now = HudWidgets.FocusCard();
        _now.Text.AddThemeFontSizeOverride("font_size", 14);
        _card.AddChild(_now.Box);

        _card.AddChild(UiStyle.Caption("Shift", 12));
        var shifts = new HBoxContainer();
        shifts.AddThemeConstantOverride("separation", 6);
        _dayButton = ShiftButton("Day  08–20", StaffShift.Day);
        _nightButton = ShiftButton("Night  20–08", StaffShift.Night);
        shifts.AddChild(_dayButton);
        shifts.AddChild(_nightButton);
        _card.AddChild(shifts);

        _card.AddChild(UiStyle.Caption("Works in", 12));
        _worksIn.ItemSelected += index =>
        {
            if (Selected() is { } m && index >= 0 && index < _postings.Count)
            {
                _sim.MoveStaff(m.Id, _postings[(int)index].RoomId);
                Rebuild();
            }
        };
        _card.AddChild(_worksIn);

        _tiredness = HudWidgets.Meter("Tiredness");
        _card.AddChild(_tiredness.Root);
        _skill = HudWidgets.Meter("Skill");
        _card.AddChild(_skill.Root);

        _card.AddChild(UiStyle.Caption("Breaks this shift", 12));
        _breakList.AddThemeConstantOverride("separation", 4);
        _card.AddChild(_breakList);

        var buttons = _buttons;
        buttons.AddThemeConstantOverride("h_separation", 8);
        buttons.AddThemeConstantOverride("v_separation", 8);
        _sendOnBreak = UiStyle.SecondaryButton("Send on break", () =>
        {
            if (Selected() is { } m)
            {
                _sim.SendOnBreak(m.Id);
                Rebuild();
            }
        });
        _sendOnBreak.TooltipText = "A 15-minute break as soon as they're free";
        var map = UiStyle.SecondaryButton("Show on map", () =>
        {
            if (Selected() is { IsInBuilding: true } m)
            {
                ShowOnMapRequested?.Invoke(m);
            }
        });
        map.TooltipText = "Show them on the map";
        _letGo = UiStyle.SecondaryButton("Let go", OnLetGo);
        foreach (var b in new[] { _sendOnBreak, map, _letGo })
        {
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.FocusMode = FocusModeEnum.None;
            b.AddThemeFontSizeOverride("font_size", 14);
            var box = UiStyle.Box(UiStyle.Surface, 10, UiStyle.Border);
            box.ContentMarginLeft = box.ContentMarginRight = 6;
            box.ContentMarginTop = box.ContentMarginBottom = 8;
            b.AddThemeStyleboxOverride("normal", box);
            buttons.AddChild(b);
        }

        outer.AddChild(buttons);
        return card;
    }

    private Button ShiftButton(string text, StaffShift shift)
    {
        var button = UiStyle.SecondaryButton(text, () =>
        {
            if (Selected() is { } m)
            {
                _sim.SetShift(m.Id, shift);
                Rebuild();
            }
        });
        button.ToggleMode = true;
        button.FocusMode = FocusModeEnum.None;
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.AddThemeFontSizeOverride("font_size", 14);
        if (shift == StaffShift.Night)
        {
            var on = UiStyle.Box(Night, 10, Night);
            on.ContentMarginLeft = on.ContentMarginRight = 16;
            on.ContentMarginTop = on.ContentMarginBottom = 8;
            button.AddThemeStyleboxOverride("pressed", on);
            button.AddThemeStyleboxOverride("hover_pressed", on);
            button.AddThemeColorOverride("font_pressed_color", Colors.White);
            button.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
        }
        else
        {
            var on = UiStyle.Box(UiStyle.AmberTint, 10, UiStyle.AmberBorder);
            on.ContentMarginLeft = on.ContentMarginRight = 16;
            on.ContentMarginTop = on.ContentMarginBottom = 8;
            button.AddThemeStyleboxOverride("pressed", on);
            button.AddThemeStyleboxOverride("hover_pressed", on);
            button.AddThemeColorOverride("font_pressed_color", UiStyle.AmberText);
            button.AddThemeColorOverride("font_hover_pressed_color", UiStyle.AmberText);
        }

        return button;
    }

    private StaffMember? Selected() => _selectedId is { } id ? _sim.FindStaff(id) : null;

    private void RefreshCard()
    {
        var m = Selected();
        if (m is null)
        {
            _selectedId = null;
        }

        foreach (var child in _card.GetChildren().OfType<Control>())
        {
            child.Visible = (m is null) == (child == _empty);
        }

        _buttons.Visible = m is not null;
        if (m is null)
        {
            return;
        }

        _name.Text = m.Name;
        _role.Text = $"{Capitalise(m.Grade)} · {_sim.PlaceName(m.Role, m.BaseRoomId)}";
        HudWidgets.SetAvatar(_avatar, m.Name.Replace("Dr ", string.Empty), RecruitmentPanel.RoleColour(m.Role));

        _now.Heading.Text = StatusWord(m).ToUpperInvariant();
        _now.Text.Text = m.StatusText;
        _now.Detail.Text = m.HasPendingMove
            ? $"Moving to {_sim.PlaceName(m.Role, m.PendingRoomId)} once they finish with their patient."
            : m.Shift != _sim.CurrentShift && m.IsOnDuty ? "Their shift is over: going home once they're free." : string.Empty;
        _now.Detail.Visible = _now.Detail.Text.Length > 0;

        _dayButton.SetPressedNoSignal(m.Shift == StaffShift.Day);
        _nightButton.SetPressedNoSignal(m.Shift == StaffShift.Night);
        var clinic = m.BaseRoomId is { } roomId && _sim.FindSpace(roomId) is { Kind: CareSpaceKind.Clinic or CareSpaceKind.OutpatientReception };
        _nightButton.Disabled = clinic;
        _nightButton.TooltipText = clinic ? "Clinics only run in the day" : string.Empty;

        var postings = _sim.PostingsFor(m.Role).Where(p => _sim.CanMoveTo(m, p.RoomId)).ToList();
        var signature = $"{m.Id}|{m.BaseRoomId}|{m.PendingRoomId}|" + string.Join(",", postings.Select(p => p.RoomId + p.Name));
        if (signature != _postingsSignature)
        {
            _postingsSignature = signature;
            _postings = postings;
            _worksIn.Clear();
            foreach (var posting in postings)
            {
                _worksIn.AddItem(posting.Name);
            }

            var target = m.HasPendingMove ? m.PendingRoomId : m.BaseRoomId;
            _worksIn.Select(Math.Max(0, postings.FindIndex(p => p.RoomId == target)));
            _worksIn.Disabled = postings.Count <= 1;
        }

        HudWidgets.SetMeter(_tiredness, m.Fatigue, m.Fatigue >= 0.75 ? "Exhausted" : m.Fatigue >= 0.5 ? "Tired" : "Fresh", m.Fatigue >= 0.5 ? Amber : UiStyle.Green);
        HudWidgets.SetMeter(_skill, m.Skill, $"{m.Skill:P0}", UiStyle.Blue);
        RefreshBreakList(m);

        _sendOnBreak.Disabled = !m.IsOnDuty || m.IsOnBreak || m.Breaks.Any(b => b.Requested && !b.Done);
    }

    private void RefreshBreakList(StaffMember m)
    {
        var signature = $"{m.Id}{m.IsOnDuty}" + string.Join(",", m.Breaks.Select(b => $"{b.Start:HHmm}{b.Minutes}{b.TakenAt}{b.Done}{_sim.IsBreakLate(b)}"));
        if (signature == _breakSignature)
        {
            return;
        }

        _breakSignature = signature;
        foreach (var child in _breakList.GetChildren())
        {
            child.QueueFree();
        }

        if (!m.IsOnDuty)
        {
            var off = UiStyle.Body($"Off duty. Breaks are planned when they come in at {_sim.NextStart(m.Shift):HH:mm}.", muted: true);
            off.AddThemeFontSizeOverride("font_size", 13);
            _breakList.AddChild(off);
            return;
        }

        if (m.Breaks.Count == 0)
        {
            var none = UiStyle.Body("No more breaks planned this shift.", muted: true);
            none.AddThemeFontSizeOverride("font_size", 13);
            _breakList.AddChild(none);
            return;
        }

        foreach (var b in m.Breaks)
        {
            var taking = b.TakenAt is not null && !b.Done;
            var late = _sim.IsBreakLate(b);
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiStyle.Box(taking ? UiStyle.AmberTint : late ? new Color("#FBE9EA") : UiStyle.Ground, 8, null, 7));
            var line = new HBoxContainer();
            var kind = new Label { Text = $"{(b.Requested ? "Extra break" : b.IsMeal ? "Meal break" : "Tea break")} · {b.Minutes} min", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            kind.AddThemeFontSizeOverride("font_size", 13);
            var when = new Label { Text = b.Done ? $"✓ {(b.TakenAt ?? b.Start):HH:mm}" : taking ? $"now · till {b.TakenAt!.Value.AddMinutes(b.Minutes):HH:mm}" : late ? $"{b.Start:HH:mm} · put off" : $"{b.Start:HH:mm}" };
            when.AddThemeFontSizeOverride("font_size", 13);
            var colour = b.Done ? UiStyle.TextMuted : taking ? UiStyle.AmberText : late ? UiStyle.Red : UiStyle.TextDark;
            kind.AddThemeColorOverride("font_color", b.Done ? UiStyle.TextMuted : colour);
            when.AddThemeColorOverride("font_color", colour);
            line.AddChild(kind);
            line.AddChild(when);
            row.AddChild(line);
            _breakList.AddChild(row);
        }
    }

    private void OnLetGo()
    {
        if (Selected() is not { } m)
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

        _sim.Fire(m.Id);
        _selectedId = null;
        ResetLetGo();
        Rebuild();
    }

    private void ResetLetGo()
    {
        _confirmLetGo = 0;
        _letGo.Text = "Let go";
        _letGo.RemoveThemeColorOverride("font_color");
    }

    // ---------------------------------------------------------------- small helpers

    private static string Status(StaffMember m) => m.Activity switch
    {
        StaffActivity.OffDuty => "off",
        StaffActivity.GoingHome => "home",
        StaffActivity.OnBreak or StaffActivity.WalkingToBreak => "break",
        _ when m.CurrentTaskId is not null || m.Activity == StaffActivity.Working => "work",
        _ => "free",
    };

    private static string StatusWord(StaffMember m) => Status(m) switch
    {
        "off" => "Off duty",
        "home" => "Going home",
        "break" => "On a break",
        "work" => "Working",
        _ => "Available",
    };

    private static Color StatusColour(StaffMember m) => Status(m) switch
    {
        "work" => UiStyle.Green,
        "free" => UiStyle.Blue,
        "break" => Amber,
        _ => GreyDot,
    };

    private static Color StatusTint(StaffMember m) => Status(m) switch
    {
        "work" => new Color("#E5F3EA"),
        "free" => UiStyle.BlueTint,
        "break" => UiStyle.AmberTint,
        _ => UiStyle.Ground,
    };

    private static Control Avatar(StaffMember member, int size)
    {
        var avatar = HudWidgets.Avatar(size);
        HudWidgets.SetAvatar(avatar, member.Name.Replace("Dr ", string.Empty), RecruitmentPanel.RoleColour(member.Role));
        avatar.Initials.AddThemeFontSizeOverride("font_size", Math.Max(9, size * 2 / 5));
        return avatar.Badge;
    }

    private static Control Dot(Color colour, int size)
    {
        var dot = new PanelContainer { CustomMinimumSize = new Vector2(size, size), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        dot.AddThemeStyleboxOverride("panel", UiStyle.Box(colour, size / 2));
        return dot;
    }

    private static Control Spacer(int height) => new Control { CustomMinimumSize = new Vector2(0, height), MouseFilter = MouseFilterEnum.Ignore };

    private static string ShortGrade(string grade) => grade.Replace(" nurse", string.Empty);

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

/// <summary>A rota cell that takes a member of staff dropped on it.</summary>
public partial class RotaCell : PanelContainer
{
    public Func<int, bool> CanAccept { get; set; } = _ => false;

    public Action<int> Accept { get; set; } = _ => { };

    public StyleBox Normal { get; set; } = new StyleBoxEmpty();

    public StyleBox Highlight { get; set; } = new StyleBoxEmpty();

    public void ShowNormal() => AddThemeStyleboxOverride("panel", Normal);

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        var ok = data.VariantType == Variant.Type.Int && CanAccept(data.AsInt32());
        AddThemeStyleboxOverride("panel", ok ? Highlight : Normal);
        return ok;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        ShowNormal();
        Accept(data.AsInt32());
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationMouseExit or (int)NotificationDragEnd)
        {
            ShowNormal();
        }
    }
}

/// <summary>A member of staff on the rota: click to pick them, drag to move them.</summary>
public partial class StaffPill : PanelContainer
{
    public int StaffId { get; init; }

    /// <summary>Builds the little label that follows the mouse while dragging; null means rows can't be dragged.</summary>
    public Func<Control>? Preview { get; set; }

    public event Action? Clicked;

    public StaffPill()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            Clicked?.Invoke();
            AcceptEvent();
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Preview is null)
        {
            return default;
        }

        SetDragPreview(Preview());
        return StaffId;
    }
}

/// <summary>Hour marks across a shift.</summary>
public partial class TimeAxis : Control
{
    public DateTime Start { get; init; }

    public DateTime End { get; init; }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var span = (End - Start).TotalMinutes;
        for (var m = 0; m <= span; m += 120)
        {
            var x = (float)(m / span * Size.X);
            var text = Start.AddMinutes(m).ToString("HH:mm");
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 11).X;
            var left = Math.Clamp(x - (width / 2), 0, Math.Max(0, Size.X - width));
            DrawString(font, new Vector2(left, 13), text, HorizontalAlignment.Left, -1, 11, UiStyle.TextMuted);
        }
    }
}

/// <summary>One person's shift as a bar, with their breaks on it and a line for now.</summary>
public partial class BreakTrack : Control
{
    public static readonly Color TakenColour = new("#E3D2B0");

    public StaffMember Member { get; init; } = null!;

    public HospitalSimulation Sim { get; init; } = null!;

    public DateTime Start { get; init; }

    public DateTime End { get; init; }

    public override void _Draw()
    {
        var span = (End - Start).TotalMinutes;
        float X(DateTime t) => (float)Math.Clamp((t - Start).TotalMinutes / span * Size.X, 0, Size.X);

        DrawStyleBox(UiStyle.Box(UiStyle.Ground, 6), new Rect2(Vector2.Zero, Size));
        for (var m = 120; m < span; m += 120)
        {
            var x = (float)(m / span * Size.X);
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), new Color("#E6ECF1"));
        }

        var amber = new Color(ClinicalText.AmberHex);
        foreach (var b in Member.Breaks)
        {
            var from = b.TakenAt ?? b.Start;
            var rect = new Rect2(X(from), 3, Math.Max(3, X(from.AddMinutes(b.Minutes)) - X(from)), Size.Y - 6);
            if (b.Done)
            {
                DrawStyleBox(UiStyle.Box(TakenColour, 4), rect);
            }
            else if (b.TakenAt is not null)
            {
                DrawStyleBox(UiStyle.Box(amber, 4), rect);
            }
            else
            {
                var late = Sim.IsBreakLate(b);
                if (late)
                {
                    rect.Position = new Vector2(X(Sim.Now), rect.Position.Y);
                }

                DrawStyleBox(UiStyle.Box(UiStyle.Surface, 4, late ? UiStyle.Red : amber), rect);
            }
        }

        var now = X(Sim.Now);
        DrawLine(new Vector2(now, -2), new Vector2(now, Size.Y + 2), UiStyle.Blue, 2);
    }
}

/// <summary>How much of a team is on the floor across the shift: tinted where a third or more are away on breaks.</summary>
public partial class CoverTrack : Control
{
    public IReadOnlyList<StaffMember> Team { get; init; } = Array.Empty<StaffMember>();

    public DateTime Start { get; init; }

    public DateTime End { get; init; }

    public DateTime Now { get; init; }

    public override void _Draw()
    {
        var span = (End - Start).TotalMinutes;
        const int step = 5;
        var width = (float)(step / span * Size.X);
        for (var m = 0; m < span; m += step)
        {
            var t = Start.AddMinutes(m);
            var away = Team.Count(s => s.Breaks.Any(b =>
            {
                var from = b.TakenAt ?? b.Start;
                return t >= from && t < from.AddMinutes(b.Minutes);
            }));
            var colour = away == 0 ? new Color("#E5F3EA") : away * 3 >= Team.Count ? UiStyle.AmberTint : UiStyle.BlueTint;
            DrawRect(new Rect2((float)(m / span * Size.X), 2, width + 0.6f, Size.Y - 4), colour);
        }

        var now = (float)Math.Clamp((Now - Start).TotalMinutes / span * Size.X, 0, Size.X);
        DrawLine(new Vector2(now, 0), new Vector2(now, Size.Y), UiStyle.Blue, 2);
    }
}
