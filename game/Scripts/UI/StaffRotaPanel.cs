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
/// each department, today's breaks, and everyone in one list. Departments are listed down the left
/// with a green, amber or red dot for their cover; picking one shows its team by staff type and grade,
/// with an empty slot (and a Move or Hire button) for everyone missing. "Needs attention" cards at the
/// top list the worst gaps. Drag someone to another place or shift, or use their card on the right to
/// change their shift, move them, send them on a break or let them go.
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

    /// <summary>A department in the list on the left: one or more places shown together.</summary>
    internal sealed record Dept(string Key, string Name, IReadOnlyList<Place> Places);

    private const string AllDepartments = "all";

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
    private string? _grade;
    private string? _dept;
    private readonly HBoxContainer _gradeChips = new();
    private readonly HBoxContainer _attention = new();
    private readonly VBoxContainer _deptList = new();
    private PanelContainer _deptBox = null!;
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

    /// <summary>Raised when the player presses Hire on a gap: opens Recruitment.</summary>
    public event Action<StaffRole>? HireRequested;

    /// <summary>The department picked on the left ("all" for every department).</summary>
    public string? SelectedDepartment => _dept;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        AddChild(column);

        column.AddChild(BuildHeader());
        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", 10);
        foreach (var caption in new[] { "On shift now", "Working", "On a break", "Off duty", "Gaps in cover" })
        {
            var tile = HudWidgets.StatTile(caption);
            UiStyle.Strong(tile.Value, UiStyle.SizeFigure);
            _stats.Add(tile);
            stats.AddChild(tile.Tile);
        }

        column.AddChild(stats);
        _attention.AddThemeConstantOverride("separation", 10);
        column.AddChild(_attention);
        column.AddChild(BuildTabBar());

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        _deptBox = new PanelContainer { CustomMinimumSize = new Vector2(230, 0) };
        _deptBox.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Ground, 12, null, 8));
        var deptScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _deptList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _deptList.AddThemeConstantOverride("separation", 2);
        deptScroll.AddChild(_deptList);
        _deptBox.AddChild(deptScroll);
        body.AddChild(_deptBox);
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

    /// <summary>Shows one department on the rota ("all" for every department).</summary>
    public void ShowDepartment(string key)
    {
        _tab = Tab.Rota;
        _dept = key;
        Rebuild();
    }

    /// <summary>The departments in the list on the left, in order (used by the smoke test).</summary>
    public IReadOnlyList<string> DepartmentKeys() => Departments().Select(d => d.Key).ToList();

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
        UiStyle.Strong(title, UiStyle.SizeTitle);
        titles.AddChild(title);
        _caption.Text = "Day shift 08:00 to 20:00 · Night shift 20:00 to 08:00 · Breaks are taken in the staff room";
        titles.AddChild(_caption);
        header.AddChild(titles);

        _shiftPillBox = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _shiftPill.AddThemeFontSizeOverride("font_size", 13);
        _shiftPillBox.AddChild(_shiftPill);
        header.AddChild(_shiftPillBox);

        var close = UiStyle.CloseButton(() => Visible = false);
        header.AddChild(close);
        return header;
    }

    private Control BuildTabBar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);
        var tabRow = new HBoxContainer();
        foreach (var (tab, text) in new[] { (Tab.Rota, "Rota"), (Tab.Breaks, "Breaks this shift"), (Tab.Everyone, "Everyone") })
        {
            var button = UiStyle.SecondaryButton(text, () =>
            {
                _tab = tab;
                Rebuild();
            });
            UiStyle.AsTab(button);
            _tabs[tab] = button;
            tabRow.AddChild(button);
        }

        bar.AddChild(UiStyle.TabTrack(tabRow));
        bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        foreach (var (key, text) in new[] { ("all", "Everyone"), ("nurse", "Nurses"), ("doctor", "Doctors"), ("porter", "Porters"), ("other", "Others") })
        {
            var chip = UiStyle.SecondaryButton(text, () =>
            {
                _filter = key;
                _grade = null;
                Rebuild();
            });
            chip.ToggleMode = true;
            chip.FocusMode = FocusModeEnum.None;
            chip.CustomMinimumSize = new Vector2(0, 34);
            chip.AddThemeFontSizeOverride("font_size", 13);
            _filters[key] = chip;
            bar.AddChild(chip);
        }

        _gradeChips.AddThemeConstantOverride("separation", 4);
        bar.AddChild(_gradeChips);
        return bar;
    }

    /// <summary>Grade chips (Consultant, Registrar ... or Band 5 to 9) next to the Doctors or Nurses filter.</summary>
    private void RefreshGradeChips()
    {
        var role = _filter switch { "doctor" => StaffRole.Doctor, "nurse" => StaffRole.Nurse, _ => (StaffRole?)null };
        var key = $"{role}|{_grade}";
        if (_gradeChips.GetMeta("key", string.Empty).AsString() == key)
        {
            return;
        }

        _gradeChips.SetMeta("key", key);
        foreach (var child in _gradeChips.GetChildren())
        {
            child.QueueFree();
        }

        _gradeChips.Visible = role is not null;
        if (role is not { } r)
        {
            return;
        }

        _gradeChips.AddChild(new VSeparator());
        var grades = new List<(string? Title, string Text)> { (null, "All grades") };
        grades.AddRange(StaffGrades.For(r).Reverse().Select(g => ((string?)g.Title, ShortGrade(g.Title))));
        foreach (var (title, text) in grades)
        {
            var chip = UiStyle.SecondaryButton(text, () =>
            {
                _grade = title;
                Rebuild();
            });
            chip.ToggleMode = true;
            chip.FocusMode = FocusModeEnum.None;
            chip.CustomMinimumSize = new Vector2(0, 34);
            chip.AddThemeFontSizeOverride("font_size", 13);
            chip.SetPressedNoSignal(title == _grade);
            _gradeChips.AddChild(chip);
        }
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

        RefreshGradeChips();

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
        var needs = _sim.RecommendedStaffing();
        var signature = $"{_tab}|{_filter}|{_grade}|{_dept}|{_selectedId}|{_sim.CurrentShift}|" + string.Join(",", needs.Select(n => $"{n.RoomId}{n.Role}{n.Shift}{n.Have}/{n.Recommended}")) + "|" + string.Join(",", _sim.Staff.Select(s =>
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

        var departments = Departments();
        if (_dept is null || (_dept != AllDepartments && departments.All(d => d.Key != _dept)))
        {
            // Open on the first department with a gap, or A&E when everything is covered.
            _dept = departments.FirstOrDefault(d => Cover(d, needs).Short > 0)?.Key ?? departments.FirstOrDefault()?.Key ?? AllDepartments;
        }

        BuildAttention(needs, departments);
        _deptBox.Visible = _tab == Tab.Rota;
        if (_tab == Tab.Rota)
        {
            BuildDepartmentList(needs, departments);
        }

        switch (_tab)
        {
            case Tab.Rota:
                BuildRota(needs, departments);
                break;
            case Tab.Breaks:
                BuildBreaks();
                break;
            default:
                BuildEveryone();
                break;
        }
    }

    private bool Shown(StaffMember m) => Shown(m.Role) && (_grade is null || m.Grade == _grade);

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
            if (_sim.IsMentalHealthWard(ward))
            {
                places.Add(new(ward.Id, ward.Name, $"{ward.Beds.Count} bedrooms · {used} in use", new[] { StaffRole.MentalHealthNurse, StaffRole.SupportWorker, StaffRole.Psychiatrist }));
                continue;
            }

            var children = _sim.IsPaediatricWard(ward);
            var doctors = children ? StaffRole.Paediatrician : _sim.UnitOf(ward) == WardUnit.IntensiveCare ? StaffRole.Anaesthetist : StaffRole.Doctor;
            var nurses = children ? StaffRole.ChildrensNurse : StaffRole.Nurse;
            places.Add(new(ward.Id, ward.Name, $"{ward.Beds.Count} beds · {used} in use", new[] { nurses, StaffRole.HealthcareAssistant, doctors }));
        }

        foreach (var clinic in _sim.ClinicRooms)
        {
            places.Add(new(clinic.Id, clinic.Name, "Outpatient clinic · days only", new[] { StaffRole.Doctor }));
        }

        foreach (var desk in _sim.SpacesOfKind(CareSpaceKind.OutpatientReception))
        {
            places.Add(new(desk.Id, desk.Name, "Outpatients · days only", new[] { StaffRole.Receptionist }));
        }

        foreach (var office in _sim.PalsOffices)
        {
            places.Add(new(office.Id, office.Name, "Patient advice and liaison · days only", new[] { StaffRole.PalsOfficer }));
        }

        foreach (var room in _sim.Spaces.Where(s => s.IsServiceRoom).OrderBy(s => s.Kind).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            var role = HospitalSimulation.RoleForRoom(room.Kind);
            places.Add(room.Kind == CareSpaceKind.Theatre
                ? new(room.Id, room.Name, "Surgeons, anaesthetists and scrub nurses", new[] { role, StaffRole.Anaesthetist, StaffRole.ScrubNurse })
                : room.Kind == CareSpaceKind.MentalHealthAssessment
                ? new(room.Id, room.Name, "Liaison team: mental health nurses and psychiatrists", new[] { role, StaffRole.Psychiatrist })
                : new(room.Id, room.Name, Capitalise(StaffGrades.RoleName(role, plural: true)), new[] { role }));
        }

        places.Add(new(null, "Whole hospital", "Porters and cleaners", new[] { StaffRole.Porter, StaffRole.Cleaner }));
        return places;
    }

    /// <summary>The departments down the left: A&amp;E, each ward, then the services grouped by what they do.</summary>
    private List<Dept> Departments()
    {
        var places = Places();
        var departments = new List<Dept>();
        var groups = new Dictionary<string, List<Place>>();
        var order = new List<string>();
        void AddTo(string name, Place place)
        {
            if (!groups.TryGetValue(name, out var list))
            {
                groups[name] = list = new List<Place>();
                order.Add(name);
            }

            list.Add(place);
        }

        foreach (var place in places)
        {
            if (place.RoomId is null)
            {
                departments.Add(new(place.Name == "A&E" ? "ae" : "hospital", place.Name, new[] { place }));
                continue;
            }

            var space = _sim.FindSpace(place.RoomId);
            if (space is { IsWard: true })
            {
                departments.Add(new(place.RoomId, place.Name, new[] { place }));
            }
            else if (space?.Kind is CareSpaceKind.Clinic or CareSpaceKind.OutpatientReception)
            {
                AddTo("Clinics", place);
            }
            else if (space is not null)
            {
                AddTo(ServiceGroup(space.Kind), place);
            }
        }

        // Keep "Whole hospital" (porters and cleaners) at the end, after the services and clinics.
        var hospital = departments.Single(d => d.Key == "hospital");
        departments.Remove(hospital);
        departments.AddRange(order.Where(n => n != "Clinics").Select(n => new Dept("group:" + n, n, groups[n])));
        if (groups.TryGetValue("Clinics", out var clinics))
        {
            departments.Add(new("group:Clinics", "Clinics", clinics));
        }

        departments.Add(hospital);
        return departments;
    }

    private static string ServiceGroup(CareSpaceKind kind)
    {
        if (kind == CareSpaceKind.Theatre)
        {
            return "Theatres";
        }

        return HospitalSimulation.RoleForRoom(kind) switch
        {
            StaffRole.Radiographer or StaffRole.Sonographer => "Imaging",
            StaffRole.Pharmacist => "Pharmacy",
            StaffRole.Midwife => "Maternity",
            StaffRole.CardiacPhysiologist or StaffRole.Neurophysiologist => "Heart and brain tests",
            StaffRole.Endoscopist => "Endoscopy",
            StaffRole.PlasterTechnician => "Plaster room",
            StaffRole.Physiotherapist => "Physiotherapy",
            StaffRole.MentalHealthNurse => "Mental health",
            StaffRole.PalsOfficer => "PALS",
            var role => Capitalise(StaffGrades.RoleName(role, plural: true)),
        };
    }

    private static IEnumerable<StaffingNeed> NeedsIn(Place place, IEnumerable<StaffingNeed> needs) =>
        needs.Where(n => n.RoomId == place.RoomId && place.Roles.Contains(n.Role));

    /// <summary>How well a department is covered: gaps, whether a needed kind of staff is missing altogether, spares and headcount.</summary>
    private (int Short, bool Nobody, int Spare, int Staff) Cover(Dept dept, IReadOnlyList<StaffingNeed> needs)
    {
        var mine = dept.Places.SelectMany(p => NeedsIn(p, needs)).ToList();
        var staff = _sim.Staff.Count(s => dept.Places.Any(p => p.RoomId == s.BaseRoomId && p.Roles.Contains(s.Role)));
        return (mine.Sum(n => n.Short), mine.Any(n => n.Recommended > 0 && n.Have == 0), mine.Sum(n => Math.Max(0, n.Have - n.Recommended)), staff);
    }

    private static string ShiftWord(StaffShift shift) => shift == StaffShift.Day ? "days" : "nights";

    /// <summary>"A&amp;E is 1 nurse short on nights" or "ICU has no anaesthetist on nights".</summary>
    internal static string GapText(StaffingNeed need) => need.Have == 0
        ? $"{need.Place} has no {StaffGrades.RoleName(need.Role)} on {ShiftWord(need.Shift)}"
        : $"{need.Place} is {need.Short} {StaffGrades.RoleName(need.Role, need.Short > 1)} short on {ShiftWord(need.Shift)}";

    // ---------------------------------------------------------------- needs attention and the department list

    private void BuildAttention(IReadOnlyList<StaffingNeed> needs, List<Dept> departments)
    {
        foreach (var child in _attention.GetChildren())
        {
            child.QueueFree();
        }

        var gaps = needs.Where(n => n.Short > 0)
            .OrderBy(n => n.Have == 0 ? 0 : 1)
            .ThenBy(n => n.Shift == _sim.CurrentShift ? 0 : 1)
            .ThenByDescending(n => n.Short)
            .ToList();
        _attention.Visible = gaps.Count > 0;
        foreach (var need in gaps.Take(3))
        {
            var red = need.Have == 0;
            var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 };
            card.AddThemeStyleboxOverride("panel", red ? UiStyle.Box(new Color("#FCEBEC"), 12, new Color("#EBB1B4"), 12) : UiStyle.Box(UiStyle.AmberTint, 12, UiStyle.AmberBorder, 12));
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 6);
            var title = new HBoxContainer();
            title.AddThemeConstantOverride("separation", 8);
            title.AddChild(Dot(red ? UiStyle.Red : Amber, 10));
            var text = new Label { Text = GapText(need), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = GapText(need), MouseFilter = MouseFilterEnum.Pass };
            text.AddThemeFontSizeOverride("font_size", 14);
            title.AddChild(text);
            column.AddChild(title);
            column.AddChild(UiStyle.Caption($"{need.Have} of {need.Recommended} {StaffGrades.RoleName(need.Role, true)} · {(need.Shift == StaffShift.Day ? "Day" : "Night")} {HospitalSimulation.ShiftHoursText(need.Shift)}", 12));
            column.AddChild(FixButtons(need, departments, showButton: true));
            card.AddChild(column);
            _attention.AddChild(card);
        }

        if (gaps.Count > 3)
        {
            var more = UiStyle.Caption($"+{gaps.Count - 3} more", 13);
            more.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            _attention.AddChild(more);
        }
    }

    /// <summary>The Move (someone spare) and Hire buttons for a gap.</summary>
    private Control FixButtons(StaffingNeed need, List<Dept>? departments, bool showButton)
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 6);
        row.AddThemeConstantOverride("v_separation", 4);
        if (_sim.SpareFor(need) is { } spare)
        {
            var from = _sim.PlaceName(spare.Role, spare.BaseRoomId);
            var move = SmallButton($"Move {spare.Name} from {from}", false, () =>
            {
                _sim.MoveStaff(spare.Id, need.RoomId);
                _selectedId = spare.Id;
                Rebuild();
            });
            move.TooltipText = $"{spare.Name} ({spare.Grade}) is spare in {from} on {ShiftWord(need.Shift)}";
            row.AddChild(move);
        }

        var hire = SmallButton("Hire", true, () => HireRequested?.Invoke(need.Role));
        hire.TooltipText = $"Open Recruitment to hire a {StaffGrades.RoleName(need.Role)}";
        row.AddChild(hire);
        if (showButton && departments?.FirstOrDefault(d => d.Places.Any(p => p.RoomId == need.RoomId && p.Roles.Contains(need.Role))) is { } dept && (_tab != Tab.Rota || _dept != dept.Key))
        {
            row.AddChild(SmallButton("Show", false, () => ShowDepartment(dept.Key)));
        }

        return row;
    }

    private static Button SmallButton(string text, bool primary, Action onPressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30), MouseDefaultCursorShape = CursorShape.PointingHand };
        button.Pressed += onPressed;
        button.AddThemeFontSizeOverride("font_size", 12);
        StyleBoxFlat Style(Color fill, Color? border)
        {
            var box = UiStyle.Box(fill, 8, border);
            box.ContentMarginLeft = box.ContentMarginRight = 10;
            box.ContentMarginTop = box.ContentMarginBottom = 4;
            return box;
        }

        var (normal, hover, text_) = primary
            ? (Style(UiStyle.Blue, null), Style(UiStyle.BlueHover, null), Colors.White)
            : (Style(UiStyle.Surface, UiStyle.Border), Style(UiStyle.BlueTint, UiStyle.Blue), UiStyle.TextDark);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", primary ? Style(UiStyle.BluePressed, null) : hover);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            button.AddThemeColorOverride(state, state == "font_color" || primary ? text_ : UiStyle.Blue);
        }

        return button;
    }

    private void BuildDepartmentList(IReadOnlyList<StaffingNeed> needs, List<Dept> departments)
    {
        foreach (var child in _deptList.GetChildren())
        {
            child.QueueFree();
        }

        var covers = departments.Select(d => (Dept: d, Cover: Cover(d, needs))).ToList();
        var shortNow = covers.Where(c => c.Cover.Short > 0).OrderBy(c => c.Cover.Nobody ? 0 : 1).ToList();
        var covered = covers.Where(c => c.Cover.Short == 0).ToList();
        if (shortNow.Count > 0)
        {
            _deptList.AddChild(SectionLabel("SHORT NOW"));
            foreach (var (dept, cover) in shortNow)
            {
                _deptList.AddChild(DeptRow(dept.Key, dept.Name, cover.Nobody ? NobodyText(dept, needs) : $"{cover.Short} {(cover.Short == 1 ? "gap" : "gaps")} · {cover.Staff} staff", cover.Nobody ? UiStyle.Red : Amber, cover.Short.ToString(), cover.Nobody ? 2 : 1));
            }
        }

        if (covered.Count > 0)
        {
            _deptList.AddChild(SectionLabel("COVERED"));
            foreach (var (dept, cover) in covered)
            {
                var sub = cover.Spare > 0 ? $"{cover.Staff} staff · {cover.Spare} spare" : $"{cover.Staff} staff";
                _deptList.AddChild(DeptRow(dept.Key, dept.Name, sub, UiStyle.Green, cover.Spare > 0 ? $"+{cover.Spare}" : null, 0));
            }
        }

        _deptList.AddChild(Spacer(4));
        _deptList.AddChild(DeptRow(AllDepartments, "All departments", "Everyone on one list", GreyDot, null, 0));
    }

    private string NobodyText(Dept dept, IReadOnlyList<StaffingNeed> needs)
    {
        var need = dept.Places.SelectMany(p => NeedsIn(p, needs)).First(n => n.Recommended > 0 && n.Have == 0);
        return $"No {StaffGrades.RoleName(need.Role)} on {ShiftWord(need.Shift)}";
    }

    private static Control SectionLabel(string text)
    {
        var label = UiStyle.Caption(text, 11);
        var pad = new MarginContainer();
        pad.AddThemeConstantOverride("margin_left", 8);
        pad.AddThemeConstantOverride("margin_top", 6);
        pad.AddThemeConstantOverride("margin_bottom", 2);
        pad.AddChild(label);
        return pad;
    }

    /// <summary>One department on the left: a dot for its cover, its name, a line about it and a badge (gaps or spares).</summary>
    private Control DeptRow(string key, string name, string sub, Color dot, string? badge, int badgeKind)
    {
        var selected = key == _dept;
        var row = new StaffPill { StaffId = -1, MouseDefaultCursorShape = CursorShape.PointingHand };
        row.Clicked += () => ShowDepartment(key);
        var box = UiStyle.Box(selected ? UiStyle.Surface : new Color(0, 0, 0, 0), 9, null, 0);
        box.ContentMarginLeft = 10;
        box.ContentMarginRight = 8;
        box.ContentMarginTop = box.ContentMarginBottom = 6;
        if (selected)
        {
            box.BorderColor = UiStyle.Blue;
            box.BorderWidthLeft = 3;
            box.ShadowColor = new Color(0, 0, 0, 0.06f);
            box.ShadowSize = 2;
        }

        row.AddThemeStyleboxOverride("panel", box);
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 8);
        line.AddChild(Dot(dot, 10));
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 0);
        var title = new Label { Text = name, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontSizeOverride("font_size", 14);
        text.AddChild(title);
        var caption = UiStyle.Caption(sub, 12);
        caption.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        caption.MouseFilter = MouseFilterEnum.Ignore;
        text.AddChild(caption);
        line.AddChild(text);
        if (badge is not null)
        {
            var chip = HudWidgets.Chip();
            var (tint, colour) = badgeKind switch
            {
                2 => (new Color("#FCEBEC"), new Color("#9A2328")),
                1 => (UiStyle.AmberTint, UiStyle.AmberText),
                _ => (UiStyle.BlueTint, UiStyle.Blue),
            };
            HudWidgets.SetChip(chip, badge, tint);
            chip.Text.AddThemeColorOverride("font_color", colour);
            chip.Text.AddThemeFontSizeOverride("font_size", 12);
            chip.Chip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            chip.Chip.MouseFilter = MouseFilterEnum.Ignore;
            line.AddChild(chip.Chip);
        }

        row.AddChild(line);
        return row;
    }

    private static bool IsDayOnly(Place place) => place.Sub.EndsWith("days only", StringComparison.Ordinal);

    private void BuildRota(IReadOnlyList<StaffingNeed> needs, List<Dept> departments)
    {
        var showing = _dept == AllDepartments ? departments : departments.Where(d => d.Key == _dept).ToList();
        var places = showing.SelectMany(d => d.Places).Where(p => p.Roles.Any(Shown)).ToList();
        var first = true;
        foreach (var place in places)
        {
            var roles = place.Roles.Where(r => Shown(r) && (NeedsIn(place, needs).Any(n => n.Role == r) || _sim.Staff.Any(s => s.Role == r && s.BaseRoomId == place.RoomId))).ToList();
            if (roles.Count == 0)
            {
                continue;
            }

            if (!first)
            {
                _content.AddChild(Spacer(18));
            }

            first = false;
            var header = new HBoxContainer();
            header.AddThemeConstantOverride("separation", 10);
            var label = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0) };
            label.AddThemeConstantOverride("separation", 0);
            var name = new Label { Text = place.Name, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            name.AddThemeFontSizeOverride("font_size", 16);
            label.AddChild(name);
            label.AddChild(UiStyle.Caption(place.Sub, 12));
            header.AddChild(label);
            header.AddChild(ShiftHeading(StaffShift.Day));
            header.AddChild(ShiftHeading(StaffShift.Night));
            _content.AddChild(header);
            _content.AddChild(Spacer(6));

            foreach (var role in roles)
            {
                _content.AddChild(HudWidgets.Divider());
                _content.AddChild(RoleRow(place, role, needs));
            }
        }

        if (first)
        {
            _content.AddChild(UiStyle.Body(_sim.Staff.Count == 0 ? "Nobody works here yet. Hire staff in Recruitment (H)." : "Nobody of this kind works here.", muted: true));
        }

        _content.AddChild(Spacer(10));
        var hint = UiStyle.Caption("Drag someone to another place or to the other shift. A move happens once they've finished with their patient; a change of shift sends them home or brings them in. Recommended numbers are a guide: you can ignore them.", 12);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(hint);
    }

    /// <summary>One kind of staff at one place: day and night teams side by side, most senior first, with an empty slot for each gap.</summary>
    private Control RoleRow(Place place, StaffRole role, IReadOnlyList<StaffingNeed> needs)
    {
        var day = NeedsIn(place, needs).FirstOrDefault(n => n.Role == role && n.Shift == StaffShift.Day);
        var night = NeedsIn(place, needs).FirstOrDefault(n => n.Role == role && n.Shift == StaffShift.Night);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var label = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0) };
        label.AddThemeConstantOverride("separation", 2);
        var title = new Label { Text = Capitalise(StaffGrades.RoleName(role, plural: true)) };
        title.AddThemeFontSizeOverride("font_size", 14);
        label.AddChild(title);
        var recommended = new[] { day, night }.Where(n => n is not null).Select(n => n!).ToList();
        if (recommended.Count > 0)
        {
            var shortBy = recommended.Sum(n => n.Short);
            var nobody = recommended.Any(n => n.Recommended > 0 && n.Have == 0);
            var chip = HudWidgets.Chip();
            HudWidgets.SetChip(chip, shortBy == 0 ? "Covered" : $"{shortBy} short", shortBy == 0 ? new Color("#E5F3EA") : nobody ? new Color("#FCEBEC") : UiStyle.AmberTint);
            chip.Text.AddThemeColorOverride("font_color", shortBy == 0 ? new Color("#1E6B42") : nobody ? new Color("#9A2328") : UiStyle.AmberText);
            chip.Text.AddThemeFontSizeOverride("font_size", 12);
            var chipRow = new HBoxContainer();
            chipRow.AddChild(chip.Chip);
            label.AddChild(chipRow);
            var per = recommended.Max(n => n.Recommended);
            label.AddChild(UiStyle.Caption(IsDayOnly(place) ? $"{per} recommended, days only" : $"{per} per shift recommended", 12));
        }

        row.AddChild(label);
        row.AddChild(Cell(place, role, StaffShift.Day, day));
        row.AddChild(Cell(place, role, StaffShift.Night, night));
        var wrap = new MarginContainer();
        wrap.AddThemeConstantOverride("margin_top", 8);
        wrap.AddThemeConstantOverride("margin_bottom", 8);
        wrap.AddChild(row);
        return wrap;
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

    private Control Cell(Place place, StaffRole role, StaffShift shift, StaffingNeed? need)
    {
        var cell = new RotaCell
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CanAccept = id => _sim.FindStaff(id) is { } m && m.Role == role && _sim.CanMoveTo(m, place.RoomId) && !(IsDayOnly(place) && shift == StaffShift.Night),
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

        var people = new HFlowContainer();
        people.AddThemeConstantOverride("h_separation", 6);
        people.AddThemeConstantOverride("v_separation", 6);
        foreach (var member in _sim.Staff.Where(s => s.Shift == shift && s.BaseRoomId == place.RoomId && s.Role == role && Shown(s))
                     .OrderByDescending(s => GradeRank(s))
                     .ThenByDescending(s => s.Skill))
        {
            people.AddChild(Pill(member));
        }

        if (need is { Short: > 0 })
        {
            var nobody = need.Have == 0;
            for (var i = 0; i < Math.Min(need.Short, 3); i++)
            {
                people.AddChild(EmptySlot($"{Capitalise(StaffGrades.RoleName(role))} needed", nobody));
            }

            if (need.Short > 3)
            {
                people.AddChild(EmptySlot($"+{need.Short - 3} more", nobody));
            }
        }

        if (people.GetChildCount() > 0)
        {
            column.AddChild(people);
        }
        else
        {
            column.AddChild(UiStyle.Caption(_grade is null ? "Nobody" : "Nobody of this grade", 12));
        }

        if (need is { Short: > 0 })
        {
            column.AddChild(FixButtons(need, null, showButton: false));
        }

        return cell;
    }

    /// <summary>A gap on the rota: an amber slot (red when there's nobody of that kind at all).</summary>
    private static Control EmptySlot(string text, bool nobody)
    {
        var slot = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var box = UiStyle.Box(nobody ? new Color("#FCEBEC") : UiStyle.AmberTint, 15, nobody ? UiStyle.Red : Amber, 0);
        box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = 1;
        box.ContentMarginLeft = box.ContentMarginRight = 10;
        box.ContentMarginTop = box.ContentMarginBottom = 5;
        slot.AddThemeStyleboxOverride("panel", box);
        var label = new Label { Text = "+ " + text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", nobody ? new Color("#9A2328") : UiStyle.AmberText);
        slot.AddChild(label);
        return slot;
    }

    /// <summary>How senior someone's grade is within their kind of staff (0 = most junior).</summary>
    private static int GradeRank(StaffMember member)
    {
        var grades = StaffGrades.For(member.Role).ToList();
        return Math.Max(0, grades.FindIndex(g => g.Title == member.Grade));
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
        var box = UiStyle.Box(member.IsInBuilding ? UiStyle.Surface : UiStyle.Subtle, 15, selected ? UiStyle.Blue : UiStyle.Border, 0);
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
        _role.Text = $"{Capitalise(HospitalSimulation.JobTitle(m.Grade, m.Specialty))} · {_sim.PlaceName(m.Role, m.BaseRoomId)}";
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

    private static string ShortGrade(string grade) => grade.Replace(" nurse", string.Empty).Replace("Healthcare assistant (Band 3)", "Band 3")
        .Replace("Core surgical trainee", "Trainee").Replace("Surgical registrar", "Registrar");

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
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), UiStyle.Hairline);
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
