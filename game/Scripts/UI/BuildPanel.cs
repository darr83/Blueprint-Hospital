using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The build menu. Pick a room type and a size (preset or custom, in metres) to see its area,
/// capacity, cost, furniture and staffing, then place it. Corridors are dragged out to any size
/// and join any corridor they touch or cross; the building can be extended in any direction.
/// </summary>
public partial class BuildPanel : PanelContainer
{

    private readonly VBoxContainer _body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly RichTextLabel _status = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false };
    private readonly Label _balance = new();
    private readonly Dictionary<string, Button> _tabs = new();
    private HospitalSimulation _sim = null!;
    private BuildTool _tool = null!;
    private string _tab = "A&E";
    private RoomTemplate? _selected;

    public void Initialise(HospitalSimulation simulation, BuildTool tool)
    {
        _sim = simulation;
        _tool = tool;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());
        CustomMinimumSize = new Vector2(420, 0);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        var title = new Label { Text = "Build", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        header.AddChild(title);
        _balance.AddThemeColorOverride("font_color", new Color("#2E8B57"));
        _balance.AddThemeFontSizeOverride("font_size", 18);
        header.AddChild(_balance);
        var close = new Button { Text = "✕", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(40, 40), TooltipText = "Close (B)" };
        close.Pressed += () =>
        {
            _tool.Cancel();
            Visible = false;
        };
        header.AddChild(close);
        column.AddChild(header);

        var tabs = new HFlowContainer();
        tabs.AddThemeConstantOverride("h_separation", 6);
        tabs.AddThemeConstantOverride("v_separation", 6);
        var categories = _sim.Database.RoomTemplates.Values.Where(t => !t.IsCorridor).Select(t => t.Category).Distinct().ToList();
        foreach (var name in categories.Append("Corridors").Append("Building").Append("Departments"))
        {
            var tab = new Button { Text = name, ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34), ThemeTypeVariation = UiStyle.Secondary };
            tab.AddThemeFontSizeOverride("font_size", 14);
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            {
                var box = (StyleBoxFlat)UiStyle.Theme.GetStylebox(state, UiStyle.Secondary).Duplicate();
                box.ContentMarginLeft = box.ContentMarginRight = 12;
                box.ContentMarginTop = box.ContentMarginBottom = 4;
                tab.AddThemeStyleboxOverride(state, box);
            }
            tab.Pressed += () => ShowTab(name);
            _tabs[name] = tab;
            tabs.AddChild(tab);
        }

        column.AddChild(tabs);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_body);
        column.AddChild(scroll);

        _status.CustomMinimumSize = new Vector2(0, 40);
        _status.AddThemeColorOverride("default_color", new Color("#17232E"));
        column.AddChild(_status);
        column.AddChild(Hint("Drag to build · Right click or Esc stops"));

        _tool.CheckChanged += RefreshStatus;
        _tool.Built += text => _status.Text = $"[color=#2E8B57]{text}[/color]";
        ShowTab(_tab);
    }

    public override void _Process(double delta)
    {
        if (Visible)
        {
            _balance.Text = $"£{_sim.Balance:N0}";
        }
    }

    public void ShowTab(string name)
    {
        _tab = name;
        foreach (var (key, button) in _tabs)
        {
            button.SetPressedNoSignal(key == name);
        }

        foreach (var child in _body.GetChildren())
        {
            child.QueueFree();
        }


        switch (name)
        {
            case "Corridors":
                ShowCorridors();
                break;
            case "Building":
                ShowBuilding();
                break;
            case "Departments":
                ShowDepartments();
                break;
            default:
                ShowRooms(name);
                break;
        }
    }

    // ------------------------------------------------------------------ rooms

    private void ShowRooms(string category)
    {
        var templates = _sim.Database.RoomTemplates.Values.Where(t => t.Category == category && !t.IsCorridor).ToList();
        if (_selected is null || _selected.Category != category)
        {
            _selected = templates.FirstOrDefault();
        }

        var list = new GridContainer { Columns = 2 };
        list.AddThemeConstantOverride("h_separation", 4);
        list.AddThemeConstantOverride("v_separation", 4);
        foreach (var template in templates)
        {
            var min = $"{template.MinWidthM:0.#} × {template.MinLengthM:0.#} m";
            var locked = !_sim.IsUnlocked(template.Department);
            var button = new Button
            {
                Text = locked ? $"{template.Name}\nLocked" : $"{template.Name}\nmin {min}",
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 50),
                TooltipText = template.Description,
                ClipText = true,
            };
            button.AddThemeFontSizeOverride("font_size", 15);
            button.SetPressedNoSignal(template == _selected && _tool.Mode == BuildMode.Room);
            if (locked)
            {
                button.AddThemeColorOverride("font_color", UiStyle.TextMuted);
            }

            button.Pressed += () =>
            {
                _selected = template;
                if (_sim.IsUnlocked(template.Department))
                {
                    _tool.StartRoom(template);
                }
                else
                {
                    _tool.Cancel();
                }

                ShowTab(_tab);
            };
            list.AddChild(button);
        }

        _body.AddChild(list);
        if (_selected is not { } t)
        {
            return;
        }

        if (!_sim.IsUnlocked(t.Department) && _sim.Database.Departments.TryGetValue(t.Department, out var dept))
        {
            _body.AddChild(Text($"[b]{t.Name}[/b]\n[color=#5B6B79]{t.Description}[/color]\n\n[color=#8A5300]Part of {dept.Name}, which isn't open yet. Unlock it to build its rooms.[/color]"));
            _body.AddChild(UnlockButton(dept));
            return;
        }

        _body.AddChild(Text(
            $"[b]{t.Name}[/b]\n[color=#5B6B79]{t.Description}[/color]\n"
            + $"[color=#5B6B79]Minimum[/color]  {t.MinWidthM:0.#} × {t.MinLengthM:0.#} m   [color=#5B6B79]Maximum[/color]  {t.MaxWidthM:0.#} × {t.MaxLengthM:0.#} m\n"
            + (t.IsShaft
                ? $"[color=#5B6B79]Cost[/color]  £{t.BaseCost:N0} + £{t.CostPerSquareMetre:N0} per m², on each of the {_sim.FloorCount} floor{(_sim.FloorCount == 1 ? string.Empty : "s")}\n"
                : $"[color=#5B6B79]Cost[/color]  £{t.BaseCost:N0} + £{t.CostPerSquareMetre:N0} per m² + furniture\n")
            + $"[color=#5B6B79]Staffing[/color]  {t.Staffing}"));

        // How the room fills up as it grows, laid out at a few example sizes.
        var rows = t.Sizes.Select(size =>
        {
            var (w, d) = (Units.MetresToTiles(size.WidthM), Units.MetresToTiles(size.LengthM));
            var room = RoomLayoutEngine.Build(t, _sim.Database, "preview", t.Name, new GridRect(0, 0, w, d), WallSide.South);
            return $"{size.WidthM:0.#} × {size.LengthM:0.#} m  →  {Capacity(t, room)}, about £{room.BuildCost:N0}";
        });
        _body.AddChild(Text("[color=#5B6B79]Grows with size[/color]\n" + string.Join("\n", rows)));
        _body.AddChild(Hint("Click a room type, then drag on the floor from corner to corner. The door goes on the wall that touches a corridor, and the furniture fills the room to fit."));
    }

    /// <summary>Size, capacity, cost, equipment and staffing of a laid-out room, for the build menu and room panel.</summary>
    public static string Describe(RoomTemplate template, CareSpace room, HospitalSimulation sim)
    {
        var (w, d) = RoomLayoutEngine.LocalSize(room.Rect, room.DoorSide);
        var furniture = new List<string>();
        var equipment = new List<string>();
        foreach (var group in room.Furniture.GroupBy(f => f.ItemId))
        {
            if (!sim.Database.Furniture.TryGetValue(group.Key, out var item))
            {
                continue;
            }

            var entry = group.Count() == 1 ? item.Name : $"{group.Count()} × {item.Name.ToLowerInvariant()}";
            (item.IsEquipment ? equipment : furniture).Add(entry);
        }

        var lines = new List<string>
        {
            $"[color=#5B6B79]Size[/color]  {Units.TilesToMetres(w):0.#} × {Units.TilesToMetres(d):0.#} m · {room.AreaSquareMetres:0} m²",
            $"[color=#5B6B79]Capacity[/color]  {Capacity(template, room)}",
            $"[color=#5B6B79]Cost[/color]  £{room.BuildCost:N0}",
        };
        if (equipment.Count > 0)
        {
            lines.Add($"[color=#5B6B79]Equipment[/color]  {string.Join(", ", equipment)}");
        }

        if (furniture.Count > 0)
        {
            lines.Add($"[color=#5B6B79]Furniture[/color]  {string.Join(", ", furniture)}");
        }

        lines.Add($"[color=#5B6B79]Staffing[/color]  {template.Staffing}");
        return string.Join("\n", lines);
    }

    private static string Capacity(RoomTemplate template, CareSpace room) => template.Kind switch
    {
        CareSpaceKind.XRay or CareSpaceKind.CT => "1 patient at a time",
        CareSpaceKind.Theatre => "1 operation at a time",
        CareSpaceKind.Pharmacy => "Dispenses for every ward",
        CareSpaceKind.Clinic => "1 booked patient at a time",
        CareSpaceKind.CathLab => "1 procedure at a time",
        CareSpaceKind.PlasterRoom => "1 patient at a time",
        CareSpaceKind.PhysioGym => "Physios visit patients on every ward",
        CareSpaceKind.OutreachOffice => "Outreach nurses check every ward",
        CareSpaceKind.EndoscopyRecovery => "1 patient coming round",
        CareSpaceKind.Lift => "Beds, trolleys, wheelchairs and walkers, to every floor",
        CareSpaceKind.Stairs => "People on foot, a floor at a time",
        CareSpaceKind.Echo or CareSpaceKind.Eeg or CareSpaceKind.Ultrasound or CareSpaceKind.Endoscopy => "1 patient at a time",
        _ => LayoutCapacity(template, room),
    };

    private static string LayoutCapacity(RoomTemplate template, CareSpace room) => template.Layout switch
    {
        RoomLayoutKind.BedSpace => "1 patient on a bed",
        RoomLayoutKind.Ward => room.Beds.Count == 1 ? "1 bed" : $"{room.Beds.Count} beds",
        RoomLayoutKind.Triage => "1 patient at a time",
        RoomLayoutKind.Waiting => $"{room.Seats.Count} seats, queue of {room.QueueTiles.Count}",
        RoomLayoutKind.Office or RoomLayoutKind.StaffRoom => $"{room.Seats.Count} staff",
        _ => "—",
    };

    // ------------------------------------------------------------------ corridors and building

    private void ShowCorridors()
    {
        var corridor = _sim.Database.RoomTemplates.Values.First(t => t.IsCorridor);
        _body.AddChild(Text($"[b]Corridors[/b]\n[color=#5B6B79]{corridor.Description}[/color]"));
        _body.AddChild(Text($"Cost £{corridor.CostPerSquareMetre:N0} per m² of new floor. Drag out any size from corner to corner (at least {corridor.MinWidthM:0.#} m wide). A corridor must touch or cross another corridor or an entrance; where it crosses one they join up, and floor that is already corridor is free."));
        var lay = new Button
        {
            Text = "Lay a corridor",
            FocusMode = FocusModeEnum.None,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44),
        };
        lay.Pressed += () => _tool.StartCorridor();
        _body.AddChild(lay);
        _body.AddChild(Hint("2.5 m is the usual minimum in clinical areas; 4 m lets two trolleys pass."));

        _body.AddChild(Text($"[b]Waiting chairs[/b]\n[color=#5B6B79]Clinic patients wait on chairs in Outpatients reception and on chairs placed along the corridors. Each chair costs £{_sim.CorridorChairCost:N0}. Click a corridor tile to place one; keep doorways clear. In Build mode, click a chair to remove it.[/color]"));
        var chairs = new Button
        {
            Text = $"Place waiting chairs ({_sim.CorridorChairs.Count} placed)",
            FocusMode = FocusModeEnum.None,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44),
        };
        chairs.Pressed += () => _tool.StartChairs();
        _body.AddChild(chairs);
    }

    private void ShowBuilding()
    {
        var building = _sim.MainBuilding;
        var b = building.Bounds;
        _body.AddChild(Text(
            $"[b]{building.Name}[/b]\n"
            + $"[color=#5B6B79]Floor area[/color]  {building.AreaSquareMetres:N0} m²\n"
            + $"[color=#5B6B79]Overall size[/color]  {Units.TilesToMetres(b.Width):0.#} × {Units.TilesToMetres(b.Length):0.#} m\n"
            + $"[color=#5B6B79]Extensions[/color]  {building.Extensions.Count}"));
        _body.AddChild(Text($"Extend the building in any direction onto grass or paving. Costs £{HospitalSimulation.ExtensionCostPerSquareMetre:N0} per m² of new floor. Keep the entrances, the car park and the ambulance bay clear."));
        var extend = UiStyle.Button("Extend the building", () => _tool.StartExtension());
        extend.FocusMode = FocusModeEnum.None;
        _body.AddChild(extend);

        // Floors: each new one has the ground floor's outline; lifts and stairs are carried up through it.
        var floors = _sim.FloorCount;
        var floorList = string.Join(", ", Enumerable.Range(0, floors).Select(SiteMap.FloorName));
        _body.AddChild(Text(
            $"[b]Floors[/b]\n[color=#5B6B79]{floors} floor{(floors == 1 ? string.Empty : "s")}: {floorList}.[/color]\n"
            + $"A new floor has the same outline as the ground floor (extensions grow every floor). It costs £{HospitalSimulation.FloorCostPerSquareMetre:N0} per m² for the first floor up, "
            + $"£{HospitalSimulation.FloorCostRisePerSquareMetre:N0} per m² more for each floor higher, plus every lift and staircase carried up to it. "
            + "Build a lift or stairs (Lifts & stairs tab) to reach it: wheelchairs and trolleys need a lift."));
        var check = _sim.CheckAddFloor();
        var next = SiteMap.FloorName(floors).ToLowerInvariant();
        var add = UiStyle.Button(floors > SiteMap.MaxUpperFloors ? "The building is as tall as it can be" : $"Build the {next} (£{check.Cost:N0})", () =>
        {
            var result = _sim.AddFloor();
            _status.Text = result.CanBuild
                ? $"[color=#2E8B57]✓ Built the {next} for £{result.Cost:N0}. Page Up to look at it.[/color]"
                : $"[color=#C4373D]✗ {string.Join("\n✗ ", result.Problems)}[/color]";
            ShowTab(_tab);
        });
        add.FocusMode = FocusModeEnum.None;
        add.Disabled = !check.CanBuild;
        add.TooltipText = check.CanBuild ? string.Empty : string.Join("\n", check.Problems);
        _body.AddChild(add);
    }

    // ------------------------------------------------------------------ departments

    private void ShowDepartments()
    {
        _body.AddChild(Hint("A&E, General Medicine and the shared services are open from the start. Unlock a specialty to build its ward, then hire its team. Patients whose specialty you don't have are transferred to another hospital."));
        foreach (var department in _sim.Database.Departments.Values.OrderBy(d => d.IsSharedService).ThenBy(d => d.OpeningCost).ThenBy(d => d.Name))
        {
            var unlocked = _sim.IsUnlocked(department.Id);
            var wards = _sim.WardsOf(department.Id).ToList();
            var open = department.StartsUnlocked || department.OpeningCost == 0 ? "Open from the start" : "Unlocked";
            var wardText = wards.Count == 0 ? "no ward yet" : $"{wards.Count} ward{(wards.Count == 1 ? string.Empty : "s")}, {wards.Sum(w => w.Beds.Count)} beds";
            var status = !unlocked
                ? $"[color=#8A5300]Locked · £{department.OpeningCost:N0} to unlock[/color]"
                : department.IsSharedService
                    ? "[color=#2E8B57]Shared service, open from the start[/color]"
                    : $"[color=#2E8B57]{open}[/color] · {wardText}";
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Ground, 10, UiStyle.Border, 10));
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 4);
            card.AddChild(column);
            column.AddChild(Text($"[color={department.Colour}]●[/color] [b]{department.Name}[/b]\n[color=#5B6B79]{department.Description}[/color]\n{status}"));
            if (!unlocked)
            {
                column.AddChild(UnlockButton(department));
            }

            _body.AddChild(card);
        }
    }

    private Button UnlockButton(DepartmentDefinition department)
    {
        var check = _sim.CheckUnlock(department.Id);
        var button = UiStyle.Button($"Unlock {department.Name} (£{department.OpeningCost:N0})", () =>
        {
            var result = _sim.UnlockDepartment(department.Id);
            _status.Text = result.CanBuild
                ? $"[color=#2E8B57]✓ {department.Name} unlocked. Its rooms are in the build menu.[/color]"
                : $"[color=#C4373D]✗ {string.Join("\n✗ ", result.Problems)}[/color]";
            ShowTab(_tab);
        });
        button.FocusMode = FocusModeEnum.None;
        button.Disabled = !check.CanBuild;
        button.TooltipText = check.CanBuild ? string.Empty : string.Join("\n", check.Problems);
        return button;
    }

    // ------------------------------------------------------------------ status

    private void RefreshStatus()
    {
        if (_tool.Check is not { } check)
        {
            _status.Text = string.Empty;
            return;
        }

        if (check.CanBuild)
        {
            var verb = _tool.Mode == BuildMode.Chair ? "Click to place a chair" : _tool.MovingId is null ? "Release to build" : "Release to rebuild here";
            _status.Text = $"[color=#2E8B57]✓ {verb}: £{check.Cost:N0}[/color]";
            return;
        }

        _status.Text = $"[color=#C4373D]✗ {string.Join("\n✗ ", check.Problems.Take(4))}[/color]";
    }

    // ------------------------------------------------------------------ helpers

    private static RichTextLabel Text(string bbcode)
    {
        var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Text = bbcode, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.AddThemeColorOverride("default_color", new Color("#17232E"));
        return label;
    }

    private static Label Small(string text)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeColorOverride("font_color", new Color("#5B6B79"));
        return label;
    }

    private static Label Hint(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", new Color("#5B6B79"));
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }
}
