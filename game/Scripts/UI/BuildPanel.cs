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
        title.AddThemeColorOverride("font_color", Colors.White);
        header.AddChild(title);
        _balance.AddThemeColorOverride("font_color", new Color("#9BE7B5"));
        _balance.AddThemeFontSizeOverride("font_size", 18);
        header.AddChild(_balance);
        var close = new Button { Text = "✕", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(34, 32), TooltipText = "Close (B)" };
        close.Pressed += () =>
        {
            _tool.Cancel();
            Visible = false;
        };
        header.AddChild(close);
        column.AddChild(header);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        var categories = _sim.Database.RoomTemplates.Values.Where(t => !t.IsCorridor).Select(t => t.Category).Distinct().ToList();
        foreach (var name in categories.Append("Corridors").Append("Building"))
        {
            var tab = new Button { Text = name, ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
            tab.AddThemeFontSizeOverride("font_size", 15);
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
        _status.AddThemeColorOverride("default_color", new Color("#E8EDEE"));
        column.AddChild(_status);
        column.AddChild(Hint("Drag to build · Right click or Esc stops"));

        _tool.CheckChanged += RefreshStatus;
        _tool.Built += text => _status.Text = $"[color=#9BE7B5]{text}[/color]";
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
            var button = new Button
            {
                Text = $"{template.Name}\nmin {min}",
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 50),
                TooltipText = template.Description,
                ClipText = true,
            };
            button.AddThemeFontSizeOverride("font_size", 15);
            button.SetPressedNoSignal(template == _selected && _tool.Mode == BuildMode.Room);
            button.Pressed += () =>
            {
                _selected = template;
                _tool.StartRoom(template);
                ShowTab(_tab);
            };
            list.AddChild(button);
        }

        _body.AddChild(list);
        if (_selected is not { } t)
        {
            return;
        }

        _body.AddChild(Text(
            $"[b]{t.Name}[/b]\n[color=#B8C4CE]{t.Description}[/color]\n"
            + $"[color=#B8C4CE]Minimum[/color]  {t.MinWidthM:0.#} × {t.MinLengthM:0.#} m   [color=#B8C4CE]Maximum[/color]  {t.MaxWidthM:0.#} × {t.MaxLengthM:0.#} m\n"
            + $"[color=#B8C4CE]Cost[/color]  £{t.BaseCost:N0} + £{t.CostPerSquareMetre:N0} per m² + furniture\n"
            + $"[color=#B8C4CE]Staffing[/color]  {t.Staffing}"));

        // How the room fills up as it grows, laid out at a few example sizes.
        var rows = t.Sizes.Select(size =>
        {
            var (w, d) = (Units.MetresToTiles(size.WidthM), Units.MetresToTiles(size.LengthM));
            var room = RoomLayoutEngine.Build(t, _sim.Database, "preview", t.Name, new GridRect(0, 0, w, d), WallSide.South);
            return $"{size.WidthM:0.#} × {size.LengthM:0.#} m  →  {Capacity(t, room)}, about £{room.BuildCost:N0}";
        });
        _body.AddChild(Text("[color=#B8C4CE]Grows with size[/color]\n" + string.Join("\n", rows)));
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
            $"[color=#B8C4CE]Size[/color]  {Units.TilesToMetres(w):0.#} × {Units.TilesToMetres(d):0.#} m · {room.AreaSquareMetres:0} m²",
            $"[color=#B8C4CE]Capacity[/color]  {Capacity(template, room)}",
            $"[color=#B8C4CE]Cost[/color]  £{room.BuildCost:N0}",
        };
        if (equipment.Count > 0)
        {
            lines.Add($"[color=#B8C4CE]Equipment[/color]  {string.Join(", ", equipment)}");
        }

        if (furniture.Count > 0)
        {
            lines.Add($"[color=#B8C4CE]Furniture[/color]  {string.Join(", ", furniture)}");
        }

        lines.Add($"[color=#B8C4CE]Staffing[/color]  {template.Staffing}");
        return string.Join("\n", lines);
    }

    private static string Capacity(RoomTemplate template, CareSpace room) => template.Kind switch
    {
        CareSpaceKind.XRay or CareSpaceKind.CT => "1 patient at a time",
        CareSpaceKind.Theatre => "1 operation at a time",
        CareSpaceKind.Pharmacy => "Dispenses for every ward",
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
        _body.AddChild(Text($"[b]Corridors[/b]\n[color=#B8C4CE]{corridor.Description}[/color]"));
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
    }

    private void ShowBuilding()
    {
        var building = _sim.MainBuilding;
        var b = building.Bounds;
        _body.AddChild(Text(
            $"[b]{building.Name}[/b]\n"
            + $"[color=#B8C4CE]Floor area[/color]  {building.AreaSquareMetres:N0} m²\n"
            + $"[color=#B8C4CE]Overall size[/color]  {Units.TilesToMetres(b.Width):0.#} × {Units.TilesToMetres(b.Length):0.#} m\n"
            + $"[color=#B8C4CE]Extensions[/color]  {building.Extensions.Count}"));
        _body.AddChild(Text($"Extend the building in any direction onto grass or paving. Costs £{HospitalSimulation.ExtensionCostPerSquareMetre:N0} per m² of new floor. Keep the entrances, the car park and the ambulance bay clear."));
        var extend = UiStyle.Button("Extend the building", () => _tool.StartExtension());
        extend.FocusMode = FocusModeEnum.None;
        _body.AddChild(extend);
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
            var verb = _tool.MovingId is null ? "Release to build" : "Release to rebuild here";
            _status.Text = $"[color=#9BE7B5]✓ {verb}: £{check.Cost:N0}[/color]";
            return;
        }

        _status.Text = $"[color=#FFB4AE]✗ {string.Join("\n✗ ", check.Problems.Take(4))}[/color]";
    }

    // ------------------------------------------------------------------ helpers

    private static RichTextLabel Text(string bbcode)
    {
        var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Text = bbcode, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.AddThemeColorOverride("default_color", new Color("#E8EDEE"));
        return label;
    }

    private static Label Small(string text)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeColorOverride("font_color", new Color("#B8C4CE"));
        return label;
    }

    private static Label Hint(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", new Color("#8FA0AD"));
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }
}
