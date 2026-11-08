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
/// capacity, cost, furniture and staffing, then place it. Corridors come in standard widths and
/// are dragged out; the building can be extended in any direction.
/// </summary>
public partial class BuildPanel : PanelContainer
{
    private static readonly double[] CorridorWidths = { 2.0, 2.5, 3.0, 4.0 };

    private readonly VBoxContainer _body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly RichTextLabel _status = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false };
    private readonly Label _balance = new();
    private readonly Dictionary<string, Button> _tabs = new();
    private HospitalSimulation _sim = null!;
    private BuildTool _tool = null!;
    private string _tab = "A&E";
    private RoomTemplate? _selected;
    private SpinBox? _width;
    private SpinBox? _length;
    private RichTextLabel? _details;

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
        column.AddChild(Hint("R turns the room · Left click builds · Right click or Esc stops"));

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

        _width = _length = null;
        _details = null;

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
            var button = new Button
            {
                Text = template.Name,
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 36),
                TooltipText = template.Description,
            };
            button.SetPressedNoSignal(template == _selected);
            button.Pressed += () =>
            {
                _selected = template;
                _tool.Cancel();
                ShowTab(_tab);
            };
            list.AddChild(button);
        }

        _body.AddChild(list);
        if (_selected is not { } t)
        {
            return;
        }

        _body.AddChild(Text($"[b]{t.Name}[/b]\n[color=#B8C4CE]{t.Description}[/color]"));

        var sizes = new HBoxContainer();
        sizes.AddThemeConstantOverride("separation", 4);
        foreach (var size in t.Sizes)
        {
            var s = size;
            var button = new Button
            {
                Text = $"{s.Name}\n{s.WidthM:0.#} × {s.LengthM:0.#} m",
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 50),
                ClipText = true,
            };
            button.AddThemeFontSizeOverride("font_size", 15);
            button.Pressed += () =>
            {
                _width!.SetValueNoSignal(s.WidthM);
                _length!.SetValueNoSignal(s.LengthM);
                StartPlacing();
            };
            sizes.AddChild(button);
        }

        _body.AddChild(sizes);

        // Custom size in metres, in half-metre steps.
        var custom = new HBoxContainer();
        custom.AddThemeConstantOverride("separation", 6);
        custom.AddChild(Small("Size"));
        var standard = t.Sizes.FirstOrDefault(s => s.Name == "Standard") ?? t.Sizes[0];
        _width = Spin(t.MinWidthM, t.MaxWidthM, standard.WidthM);
        _length = Spin(t.MinLengthM, t.MaxLengthM, standard.LengthM);
        custom.AddChild(_width);
        custom.AddChild(Small("×"));
        custom.AddChild(_length);
        custom.AddChild(Small("m"));
        var place = UiStyle.Button("Place", StartPlacing, 80);
        place.FocusMode = FocusModeEnum.None;
        custom.AddChild(place);
        _body.AddChild(custom);
        _body.AddChild(Hint("Width runs along the wall with the door; length is the depth of the room."));

        _details = Text(string.Empty);
        _body.AddChild(_details);
        _width.ValueChanged += _ => RefreshDetails();
        _length.ValueChanged += _ => RefreshDetails();
        RefreshDetails();
    }

    private void StartPlacing()
    {
        if (_selected is null || _width is null || _length is null)
        {
            return;
        }

        _tool.StartRoom(_selected, Units.MetresToTiles(_width.Value), Units.MetresToTiles(_length.Value), _tool.DoorSide);
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        if (_selected is not { } t || _details is null || _width is null || _length is null)
        {
            return;
        }

        var (w, d) = (Units.MetresToTiles(_width.Value), Units.MetresToTiles(_length.Value));
        var room = RoomLayoutEngine.Build(t, _sim.Database, "preview", t.Name, new GridRect(0, 0, w, d), WallSide.South);
        _details.Text = Describe(t, room, _sim);
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

    private static string Capacity(RoomTemplate template, CareSpace room) => template.Layout switch
    {
        RoomLayoutKind.BedSpace => "1 patient on a bed",
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
        _body.AddChild(Text($"Cost £{corridor.CostPerSquareMetre:N0} per m². Pick a width, then drag along the floor. A corridor must join another corridor or an entrance."));

        var widths = new GridContainer { Columns = 2 };
        widths.AddThemeConstantOverride("h_separation", 4);
        widths.AddThemeConstantOverride("v_separation", 4);
        foreach (var width in CorridorWidths)
        {
            var label = width switch
            {
                2.0 => "Narrow",
                2.5 => "Standard",
                3.0 => "Wide",
                _ => "Trolley",
            };
            var button = new Button
            {
                Text = $"{label}\n{width:0.0} m",
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 50),
            };
            var tiles = Units.MetresToTiles(width);
            button.Pressed += () => _tool.StartCorridor(tiles);
            widths.AddChild(button);
        }

        _body.AddChild(widths);
        _body.AddChild(Hint("Trolley-width (4 m) corridors let beds pass each other; 2.5 m is the usual minimum in clinical areas."));
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
            var verb = _tool.MovingId is null ? "Click to build" : "Click to move here";
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

    private static SpinBox Spin(double min, double max, double value) => new()
    {
        MinValue = min,
        MaxValue = max,
        Step = 0.5,
        Value = value,
        CustomMinimumSize = new Vector2(78, 0),
    };
}
