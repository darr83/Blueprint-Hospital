using System;
using System.Collections.Generic;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The Decorate panel: choose a room's floor and wall finish and colour. Every pick shows straight
/// away in the world, and Apply pays for it. Opened by clicking a room with the Decorate tool.
/// </summary>
public partial class DecoratePanel : PanelContainer
{
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly GridContainer _floors = new() { Columns = 4 };
    private readonly Label _floorName = new();
    private readonly HFlowContainer _floorColours = new();
    private readonly VBoxContainer _wallSection = new();
    private readonly GridContainer _walls = new() { Columns = 6 };
    private readonly Label _wallName = new();
    private readonly HFlowContainer _wallColours = new();
    private readonly CheckBox _forNew = new() { FocusMode = FocusModeEnum.None };
    private readonly Label _cost = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
    private readonly Label _problem = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
    private readonly Button _apply = new() { Text = "Apply", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(96, 36) };
    private readonly Button _undo = new() { Text = "Undo changes", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) };
    private readonly List<(FloorFinish Finish, Swatch Button)> _floorButtons = new();
    private readonly List<(WallFinish Finish, Swatch Button)> _wallButtons = new();
    private readonly List<(string? Hex, Swatch Button)> _floorDots = new();
    private readonly List<(string? Hex, Swatch Button)> _wallDots = new();
    private HospitalSimulation _sim = null!;
    private CareSpace? _room;
    private RoomDecor _draft = RoomDecor.Standard;

    /// <summary>The room being decorated, or null when closed.</summary>
    public string? RoomId => _room?.Id;

    /// <summary>The look being tried out (for tests).</summary>
    public RoomDecor Draft => _draft;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(column);

        var header = new HBoxContainer();
        var dot = new Panel { CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        dot.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Blue, 3));
        header.AddChild(dot);
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        names.AddThemeConstantOverride("separation", 0);
        _title.AddThemeFontSizeOverride("font_size", 18);
        _title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        _subtitle.AddThemeFontSizeOverride("font_size", 13);
        _subtitle.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        names.AddChild(_title);
        names.AddChild(_subtitle);
        header.AddChild(names);
        var close = new Button { Text = "✕", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 36), TooltipText = "Close (Esc)" };
        close.Pressed += Close;
        header.AddChild(close);
        column.AddChild(header);

        column.AddChild(Section("FLOOR"));
        _floors.AddThemeConstantOverride("h_separation", 6);
        _floors.AddThemeConstantOverride("v_separation", 6);
        foreach (var finish in DecorCatalogue.Floors)
        {
            var f = finish;
            var button = new Swatch(this) { FloorFinish = f, TooltipText = $"{DecorCatalogue.Name(f)} · £{DecorCatalogue.PricePerSquareMetre(f)} per m²", CustomMinimumSize = new Vector2(80, 50) };
            button.Pressed += () => Change(_draft with { Floor = f });
            _floorButtons.Add((f, button));
            _floors.AddChild(button);
        }

        column.AddChild(_floors);
        column.AddChild(Strong(_floorName));
        Dots(_floorColours, _floorDots, DecorCatalogue.FloorColours, hex => Change(_draft with { FloorColour = hex }));
        column.AddChild(_floorColours);

        _wallSection.AddThemeConstantOverride("separation", 8);
        _wallSection.AddChild(Section("WALLS"));
        _walls.AddThemeConstantOverride("h_separation", 6);
        foreach (var finish in DecorCatalogue.Walls)
        {
            var f = finish;
            var button = new Swatch(this) { WallFinish = f, TooltipText = $"{DecorCatalogue.Name(f)} · £{DecorCatalogue.PricePerSquareMetre(f)} per m²", CustomMinimumSize = new Vector2(54, 54) };
            button.Pressed += () => Change(_draft with { Walls = f });
            _wallButtons.Add((f, button));
            _walls.AddChild(button);
        }

        _wallSection.AddChild(_walls);
        _wallSection.AddChild(Strong(_wallName));
        Dots(_wallColours, _wallDots, DecorCatalogue.WallColours, hex => Change(_draft with { WallColour = hex }));
        _wallSection.AddChild(_wallColours);
        column.AddChild(_wallSection);

        _forNew.AddThemeColorOverride("font_color", UiStyle.TextDark);
        _forNew.AddThemeFontSizeOverride("font_size", 14);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
        {
            _forNew.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }

        _forNew.Toggled += _ => Change(_draft);
        column.AddChild(_forNew);

        column.AddChild(new HSeparator());
        _cost.AddThemeColorOverride("font_color", UiStyle.TextDark);
        _cost.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_cost);
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        _undo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _apply.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(_undo);
        footer.AddChild(_apply);
        column.AddChild(footer);
        _problem.AddThemeColorOverride("font_color", UiStyle.Red);
        column.AddChild(_problem);

        _apply.Pressed += Apply;
        _undo.Pressed += () =>
        {
            if (_room is not null)
            {
                Change(_sim.DecorOf(_room));
            }
        };
    }

    /// <summary>Opens the panel on a room, starting from the finish it has now.</summary>
    public void Show(CareSpace room)
    {
        _room = room;
        _draft = _sim.DecorOf(room);
        _title.Text = room.Name;
        var template = _sim.Database.RoomTemplates.GetValueOrDefault(room.TemplateId);
        _subtitle.Text = $"{template?.Name ?? room.Kind.ToString()} · {Units.TilesToMetres(room.Rect.Width):0.#} × {Units.TilesToMetres(room.Rect.Length):0.#} m · {room.AreaSquareMetres:0} m²";
        var kind = template?.Name.ToLowerInvariant() ?? "room";
        _forNew.Text = $"Use this look for every new {kind}";
        _forNew.SetPressedNoSignal(false);
        _wallSection.Visible = room.HasWalls;
        _problem.Visible = false;
        Visible = true;
        Change(_draft);
    }

    /// <summary>Closes the panel and puts the room back as it is.</summary>
    public void Close()
    {
        Visible = false;
        _room = null;
        if (DecorArt.Preview is not null)
        {
            DecorArt.Preview = null;
            _sim.Events.Publish(new LayoutChanged());
        }
    }

    /// <summary>Picks a floor finish as if clicked (for the smoke test).</summary>
    public void PickFloor(FloorFinish finish, string? colour = null) => Change(_draft with { Floor = finish, FloorColour = colour });

    /// <summary>Picks a wall finish as if clicked (for the smoke test).</summary>
    public void PickWalls(WallFinish finish, string? colour = null) => Change(_draft with { Walls = finish, WallColour = colour });

    /// <summary>Pays for the look being tried out and keeps it.</summary>
    public void Apply()
    {
        if (_room is null)
        {
            return;
        }

        var result = _sim.Decorate(_room.Id, _draft, _forNew.ButtonPressed);
        _problem.Visible = !result.CanBuild;
        _problem.Text = string.Join("\n", result.Problems);
        if (result.CanBuild)
        {
            DecorArt.Preview = null;
            _forNew.SetPressedNoSignal(false);
            Change(_sim.DecorOf(_room));
        }
    }

    private void Change(RoomDecor look)
    {
        if (_room is null)
        {
            return;
        }

        _draft = look;
        var current = _sim.DecorOf(_room);
        DecorArt.Preview = look == current ? null : (_room.Id, look);
        _sim.Events.Publish(new LayoutChanged());

        foreach (var (finish, button) in _floorButtons)
        {
            button.Selected = finish == look.Floor;
            button.QueueRedraw();
        }

        foreach (var (finish, button) in _wallButtons)
        {
            button.Selected = finish == look.Walls;
            button.QueueRedraw();
        }

        foreach (var (hex, button) in _floorDots)
        {
            button.Selected = hex == look.FloorColour;
            button.QueueRedraw();
        }

        foreach (var (hex, button) in _wallDots)
        {
            button.Selected = hex == look.WallColour;
            button.QueueRedraw();
        }

        _floorName.Text = $"{DecorCatalogue.Name(look.Floor)} · {ColourName(DecorCatalogue.FloorColours, look.FloorColour)}   £{DecorCatalogue.PricePerSquareMetre(look.Floor)} per m²";
        _wallName.Text = $"{DecorCatalogue.Name(look.Walls)} · {ColourName(DecorCatalogue.WallColours, look.WallColour)}   £{DecorCatalogue.PricePerSquareMetre(look.Walls)} per m²";
        var cost = _sim.DecorateCost(_room, look);
        var changed = look != current;
        _cost.Text = !changed ? "No changes yet" : cost == 0 ? "Free" : $"Redecorate for £{cost:N0}";
        _cost.AddThemeColorOverride("font_color", changed && !_sim.CanAfford(cost) ? UiStyle.Red : UiStyle.TextDark);
        _apply.Disabled = !changed && !_forNew.ButtonPressed;
        _undo.Disabled = !changed;
        _problem.Visible = false;
    }

    private static string ColourName(IReadOnlyList<DecorColour> palette, string? hex)
    {
        if (hex is null)
        {
            return "usual colour";
        }

        foreach (var colour in palette)
        {
            if (string.Equals(colour.Hex, hex, StringComparison.OrdinalIgnoreCase))
            {
                return colour.Name.ToLowerInvariant();
            }
        }

        return hex;
    }

    private void Dots(HFlowContainer row, List<(string? Hex, Swatch Button)> list, IReadOnlyList<DecorColour> palette, Action<string?> pick)
    {
        row.AddThemeConstantOverride("h_separation", 5);
        row.AddThemeConstantOverride("v_separation", 6);
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.CustomMinimumSize = new Vector2(340, 0);
        var usual = new Swatch(this) { Usual = true, TooltipText = "The finish's usual colour for this room", CustomMinimumSize = new Vector2(58, 26) };
        usual.Pressed += () => pick(null);
        list.Add((null, usual));
        row.AddChild(usual);
        foreach (var colour in palette)
        {
            var hex = colour.Hex;
            var dot = new Swatch(this) { Dot = new Color(hex), TooltipText = colour.Name, CustomMinimumSize = new Vector2(26, 26) };
            dot.Pressed += () => pick(hex);
            list.Add((hex, dot));
            row.AddChild(dot);
        }
    }

    private static Label Section(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        return label;
    }

    private static Label Strong(Label label)
    {
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", UiStyle.TextDark);
        return label;
    }

    /// <summary>A clickable sample: a patch of floor, a piece of wall, a colour dot or the "usual colour" chip.</summary>
    private sealed partial class Swatch : Button
    {
        private readonly DecoratePanel _panel;

        public Swatch(DecoratePanel panel)
        {
            _panel = panel;
            FocusMode = FocusModeEnum.None;
            ClipContents = true;
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            {
                AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            }

            MouseEntered += QueueRedraw;
            MouseExited += QueueRedraw;
        }

        public FloorFinish? FloorFinish { get; init; }

        public WallFinish? WallFinish { get; init; }

        public Color? Dot { get; init; }

        public bool Usual { get; init; }

        public bool Selected { get; set; }

        public override void _Draw()
        {
            var size = Size;
            var hovered = GetGlobalRect().HasPoint(GetGlobalMousePosition());
            var ring = Selected ? UiStyle.Blue : hovered ? new Color("#9DB6DA") : UiStyle.Border;
            var room = _panel._room;
            if (Dot is { } colour)
            {
                var centre = size / 2;
                var radius = Mathf.Min(size.X, size.Y) / 2 - 3;
                DrawCircle(centre, radius, colour);
                DrawArc(centre, radius, 0, Mathf.Tau, 32, colour.Darkened(0.2f), 1, true);
                if (Selected || hovered)
                {
                    DrawArc(centre, radius + 2, 0, Mathf.Tau, 32, ring, 2, true);
                }

                return;
            }

            if (Usual)
            {
                DrawStyleBox(UiStyle.Box(Selected ? UiStyle.BlueTint : UiStyle.Surface, 13, ring), new Rect2(Vector2.Zero, size));
                var font = GetThemeFont("font", "Label");
                DrawString(font, new Vector2(0, (size.Y / 2) + 5), "Usual", HorizontalAlignment.Center, size.X, 13, Selected ? UiStyle.Blue : UiStyle.TextDark);
                return;
            }

            if (room is null)
            {
                return;
            }

            var draft = _panel._draft;
            if (FloorFinish is { } floor)
            {
                // A small patch of floor, seen from the same angle as the game.
                var look = draft with { Floor = floor, FloorColour = floor == draft.Floor ? draft.FloorColour : null };
                var k = size.X / 3;
                var top = (size.Y / 2) - (1.5f * k);
                Vector2 Map(double gx, double gy) => new((float)((size.X / 2) + ((gx - gy) * k)), (float)(top + ((gx + gy) * k / 2)));
                DecorArt.DrawFloor(this, 0, 0, 3, 3, floor, DecorArt.FloorColour(room, look), Map, 7);
                DrawRect(new Rect2(0, size.Y - 17, size.X, 17), new Color(1, 1, 1, 0.88f));
                DrawString(GetThemeFont("font", "Label"), new Vector2(0, size.Y - 4), DecorCatalogue.Name(floor), HorizontalAlignment.Center, size.X, 11, UiStyle.TextDark);
            }
            else if (WallFinish is { } walls)
            {
                var look = draft with { Walls = walls, WallColour = walls == draft.Walls ? draft.WallColour : null };
                DrawRect(new Rect2(Vector2.Zero, size), new Color("#D9DFE5"));
                var a = new Vector2(0, size.Y);
                var b = new Vector2(size.X, size.Y);
                DecorArt.DrawWallFace(this, a, b, size.Y, 0, walls, DecorArt.WallColour(look), DecorArt.WallAccent(room, look), new Color("#9FB0BE"), 0.6f);
                var label = walls switch { Simulation.Hospital.WallFinish.Tiles => "Tiles", Simulation.Hospital.WallFinish.WoodPanel => "Wood", _ => DecorCatalogue.Name(walls) };
                DrawRect(new Rect2(0, size.Y - 16, size.X, 16), new Color(1, 1, 1, 0.88f));
                DrawString(GetThemeFont("font", "Label"), new Vector2(0, size.Y - 4), label, HorizontalAlignment.Center, size.X, 10, UiStyle.TextDark);
            }

            var box = UiStyle.Box(Colors.Transparent, 8, ring);
            box.DrawCenter = false;
            box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = Selected ? 3 : 1;
            DrawStyleBox(box, new Rect2(Vector2.Zero, size));
        }
    }
}
