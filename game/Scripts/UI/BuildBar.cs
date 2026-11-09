using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The build menu: a bar of icons along the bottom of the screen in Build mode. The first icon is
/// the hospital building (extensions, floors, corridors, lifts and stairs); then one icon per
/// department and shared service. Clicking an icon pops up a row of its rooms; hovering a room shows
/// everything about it, and clicking it starts the drag-to-build tool. A locked department's pop-up
/// offers to open it. The icons and their rooms come from Data/BuildMenu/build_menu.json.
/// </summary>
public partial class BuildBar : Control
{
    /// <summary>The height kept clear at the bottom of the screen for the bar, gap included.</summary>
    public const float Reserve = 112;

    private const float Edge = 12;
    private const float TileWidth = 108;
    private const float HoverWidth = 360;

    private readonly PanelContainer _bar = new() { Name = "Bar" };
    private readonly PanelContainer _popup = new() { Name = "Popup", Visible = false };
    private readonly VBoxContainer _popupBody = new();
    private readonly PanelContainer _hover = new() { Name = "Hover", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
    private readonly RichTextLabel _hoverText = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore };
    private readonly RichTextLabel _status = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.Off };
    private readonly Dictionary<string, BarIcon> _icons = new();
    private readonly Dictionary<string, Button> _tiles = new();
    private HospitalSimulation _sim = null!;
    private BuildTool _tool = null!;
    private Button? _openButton;

    /// <summary>Colour swatches and turning for waiting chairs, shown while placing them.</summary>
    private HBoxContainer? _chairRow;
    private readonly List<(string Hex, Button Swatch)> _swatches = new();
    private Control? _hoverAnchor;
    private string? _selectedItem;
    private string? _flash;
    private double _refresh;

    /// <summary>The icons and their rooms, as loaded.</summary>
    public BuildMenu Menu { get; private set; } = new();

    /// <summary>The icon whose pop-up is showing, if any.</summary>
    public string? OpenIcon { get; private set; }

    public bool PopupOpen => _popup.Visible;

    /// <summary>The bar's rectangle on screen, for tests.</summary>
    public Rect2 BarRect => _bar.GetGlobalRect();

    /// <summary>The pop-up's rectangle on screen, for tests.</summary>
    public Rect2 PopupRect => _popup.GetGlobalRect();

    public void Initialise(HospitalSimulation simulation, BuildTool tool, BuildMenu menu)
    {
        _sim = simulation;
        _tool = tool;
        Menu = menu;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        // The bar, centred in the space right of the left-hand buttons.
        _bar.AnchorLeft = _bar.AnchorRight = 0.5f;
        _bar.AnchorTop = _bar.AnchorBottom = 1;
        _bar.GrowHorizontal = GrowDirection.Both;
        _bar.GrowVertical = GrowDirection.Begin;
        _bar.OffsetLeft = _bar.OffsetRight = MainHUD.NavWidth / 2;
        _bar.OffsetTop = _bar.OffsetBottom = -Edge;
        _bar.MouseFilter = MouseFilterEnum.Stop;
        var barBox = UiStyle.Card(0, 16);
        barBox.ContentMarginLeft = barBox.ContentMarginRight = 10;
        barBox.ContentMarginTop = 8;
        barBox.ContentMarginBottom = 4;
        _bar.AddThemeStyleboxOverride("panel", barBox);
        AddChild(_bar);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        _bar.AddChild(row);
        var first = true;
        foreach (var group in Menu.Groups)
        {
            if (!first)
            {
                var line = new ColorRect { Color = UiStyle.Border, CustomMinimumSize = new Vector2(1, 0), MouseFilter = MouseFilterEnum.Ignore };
                var gap = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
                gap.AddThemeConstantOverride("margin_left", 6);
                gap.AddThemeConstantOverride("margin_right", 6);
                gap.AddThemeConstantOverride("margin_top", 4);
                gap.AddThemeConstantOverride("margin_bottom", 4);
                gap.AddChild(line);
                row.AddChild(gap);
            }

            first = false;
            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.AddThemeConstantOverride("separation", 0);
            var icons = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            icons.AddThemeConstantOverride("separation", 4);
            foreach (var icon in group.Icons)
            {
                var button = new BarIcon(icon, this);
                button.Pressed += () => Toggle(icon.Id);
                _icons[icon.Id] = button;
                icons.AddChild(button);
            }

            column.AddChild(icons);
            var caption = new Label { Text = group.Name.ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            caption.AddThemeFontSizeOverride("font_size", 10);
            caption.AddThemeColorOverride("font_color", UiStyle.TextMuted);
            column.AddChild(caption);
            row.AddChild(column);
        }

        // The pop-up row of rooms, placed above the open icon each frame.
        var popupBox = UiStyle.Card(14, 16);
        popupBox.ContentMarginTop = popupBox.ContentMarginBottom = 12;
        popupBox.ShadowSize = 16;
        _popup.AddThemeStyleboxOverride("panel", popupBox);
        _popup.MouseFilter = MouseFilterEnum.Stop;
        _popupBody.AddThemeConstantOverride("separation", 10);
        _popup.AddChild(_popupBody);
        AddChild(_popup);

        // The hover card with everything about one room.
        var hoverBox = UiStyle.Card(14, 12);
        hoverBox.ContentMarginTop = hoverBox.ContentMarginBottom = 12;
        hoverBox.ShadowSize = 18;
        hoverBox.ShadowColor = new Color(0.09f, 0.14f, 0.18f, 0.25f);
        _hover.AddThemeStyleboxOverride("panel", hoverBox);
        _hover.CustomMinimumSize = new Vector2(HoverWidth, 0);
        _hoverText.CustomMinimumSize = new Vector2(HoverWidth - 28, 0);
        _hoverText.AddThemeColorOverride("default_color", UiStyle.TextDark);
        _hoverText.AddThemeFontSizeOverride("normal_font_size", 14);
        _hoverText.AddThemeFontSizeOverride("bold_font_size", 16);
        _hover.AddChild(_hoverText);
        AddChild(_hover);

        _status.AddThemeColorOverride("default_color", UiStyle.TextMuted);
        _status.AddThemeFontSizeOverride("normal_font_size", 13);
        _status.CustomMinimumSize = new Vector2(300, 0);

        _tool.CheckChanged += RefreshStatus;
        _tool.Built += text =>
        {
            _flash = $"[color=#2E8B57]{text}[/color]";
            RefreshStatus();
        };
    }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && !Visible)
        {
            ClosePopup();
        }
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
            foreach (var icon in _icons.Values)
            {
                icon.QueueRedraw();
            }

            RefreshTiles();
        }

        PlacePopup();
        PlaceHover();
    }

    /// <summary>Opens an icon's pop-up, or closes it if it is already open.</summary>
    public void Toggle(string iconId)
    {
        if (OpenIcon == iconId && _popup.Visible)
        {
            ClosePopup();
            return;
        }

        Open(iconId);
    }

    /// <summary>Opens an icon's pop-up (by icon id, such as "building" or "cardiology").</summary>
    public void Open(string iconId)
    {
        if (!_icons.ContainsKey(iconId))
        {
            return;
        }

        OpenIcon = iconId;
        _flash = null;
        RebuildPopup();
        _popup.Visible = true;
        foreach (var icon in _icons.Values)
        {
            icon.QueueRedraw();
        }
    }

    public void ClosePopup()
    {
        _popup.Visible = false;
        _hover.Visible = false;
        _hoverAnchor = null;
        OpenIcon = null;
        foreach (var icon in _icons.Values)
        {
            icon.QueueRedraw();
        }
    }

    /// <summary>Shows the hover card for a room in the open pop-up, as if the mouse were over it (for tests and screenshots).</summary>
    public void ShowHover(string itemId)
    {
        if (_tiles.TryGetValue(itemId, out var tile))
        {
            ShowHover(itemId, tile);
        }
    }

    public bool IsLocked(BuildMenuIcon icon) => icon.Department is { } d && !_sim.IsUnlocked(d);

    // ------------------------------------------------------------------ pop-up

    private BuildMenuIcon OpenMenuIcon => Menu.Icons.First(i => i.Id == OpenIcon);

    private void RebuildPopup()
    {
        foreach (var child in _popupBody.GetChildren())
        {
            _popupBody.RemoveChild(child);
            if (child != _status)
            {
                child.QueueFree();
            }
        }

        _tiles.Clear();
        _openButton = null;
        _chairRow = null;
        _swatches.Clear();
        _hover.Visible = false;
        _hoverAnchor = null;
        if (OpenIcon is null)
        {
            return;
        }

        var icon = OpenMenuIcon;
        var colour = new Color(icon.Colour);
        var locked = IsLocked(icon);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        var dot = new Panel { CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        dot.AddThemeStyleboxOverride("panel", UiStyle.Box(colour, 3));
        header.AddChild(dot);
        var name = new Label { Text = icon.Name };
        name.AddThemeFontSizeOverride("font_size", 17);
        name.AddThemeColorOverride("font_color", UiStyle.TextDark);
        header.AddChild(name);
        var count = new Label { Text = $"{icon.Items.Count} to build", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        count.AddThemeFontSizeOverride("font_size", 13);
        count.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        header.AddChild(count);
        if (icon.Department is not null && !locked)
        {
            var open = new Label { Text = "Open", SizeFlagsVertical = SizeFlags.ShrinkCenter };
            open.AddThemeFontSizeOverride("font_size", 12);
            open.AddThemeColorOverride("font_color", UiStyle.Green);
            var chip = new PanelContainer();
            var chipBox = UiStyle.Box(new Color("#E9F5EE"), 10);
            chipBox.ContentMarginLeft = chipBox.ContentMarginRight = 10;
            chipBox.ContentMarginTop = chipBox.ContentMarginBottom = 2;
            chip.AddThemeStyleboxOverride("panel", chipBox);
            chip.AddChild(open);
            header.AddChild(chip);
        }

        var close = new Button { Text = "✕", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 30), TooltipText = "Close (Esc)" };
        close.AddThemeFontSizeOverride("font_size", 13);
        close.Pressed += ClosePopup;
        header.AddChild(close);
        _popupBody.AddChild(header);

        if (locked && _sim.Database.Departments.TryGetValue(icon.Department!, out var department))
        {
            var banner = new PanelContainer();
            var bannerBox = UiStyle.Box(UiStyle.AmberTint, 10, UiStyle.AmberBorder);
            bannerBox.ContentMarginLeft = bannerBox.ContentMarginRight = 12;
            bannerBox.ContentMarginTop = bannerBox.ContentMarginBottom = 6;
            banner.AddThemeStyleboxOverride("panel", bannerBox);
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 12);
            var text = new Label { Text = $"{department.Name} is closed. Open it to build its rooms and treat its patients.", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
            text.AddThemeColorOverride("font_color", UiStyle.AmberText);
            text.AddThemeFontSizeOverride("font_size", 14);
            line.AddChild(text);
            _openButton = UiStyle.Button(department.OpeningCost == 0 ? "Open it" : $"Open for £{department.OpeningCost:N0}", () => Unlock(department));
            _openButton.FocusMode = FocusModeEnum.None;
            line.AddChild(_openButton);
            banner.AddChild(line);
            _popupBody.AddChild(banner);
        }

        // A long row wraps onto a second line so the pop-up stays on screen.
        const int perRow = 12;
        var tiles = new HFlowContainer { CustomMinimumSize = new Vector2(Math.Min(icon.Items.Count, perRow) * (TileWidth + 8), 0) };
        tiles.AddThemeConstantOverride("h_separation", 8);
        tiles.AddThemeConstantOverride("v_separation", 8);
        foreach (var item in icon.Items)
        {
            var tile = Tile(icon, item);
            _tiles[item] = tile;
            tiles.AddChild(tile);
        }

        _popupBody.AddChild(tiles);
        if (icon.Items.Contains(BuildMenu.WaitingChairs))
        {
            _chairRow = ChairOptions();
            _popupBody.AddChild(_chairRow);
        }

        _popupBody.AddChild(_status);
        RefreshTiles();
        RefreshStatus();
        _popup.Size = Vector2.Zero;
    }

    /// <summary>The row under the tiles while placing chairs: upholstery colour, and which way they face.</summary>
    private HBoxContainer ChairOptions()
    {
        var row = new HBoxContainer { Visible = false };
        row.AddThemeConstantOverride("separation", 6);
        var label = new Label { Text = "Chair colour", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        row.AddChild(label);
        foreach (var (name, hex) in ChairColours.All)
        {
            var swatch = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 30), TooltipText = name, ToggleMode = true };
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            {
                var box = UiStyle.Box(new Color(hex), 6, state is "pressed" or "hover_pressed" ? UiStyle.TextDark : state == "hover" ? Colors.White : new Color(hex).Darkened(0.2f));
                box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = state is "pressed" or "hover_pressed" ? 3 : 1;
                swatch.AddThemeStyleboxOverride(state, box);
            }

            swatch.Pressed += () =>
            {
                _tool.ChairColour = hex;
                RefreshStatus();
            };
            _swatches.Add((hex, swatch));
            row.AddChild(swatch);
        }

        row.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
        var turn = new Button { Text = "Turn (Z)", FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiStyle.Secondary, CustomMinimumSize = new Vector2(0, 30), TooltipText = "Turn the chairs a quarter" };
        turn.Pressed += _tool.Turn;
        row.AddChild(turn);
        var auto = new Button { Text = "Back to wall", FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiStyle.Secondary, CustomMinimumSize = new Vector2(0, 30), TooltipText = "Each chair faces away from the nearest wall, or lines up with the chair beside it" };
        auto.Pressed += _tool.AutoFacing;
        row.AddChild(auto);
        return row;
    }

    private Button Tile(BuildMenuIcon icon, string item)
    {
        var colour = new Color(icon.Colour);
        var template = _sim.Database.RoomTemplates.GetValueOrDefault(item);
        var tile = new Button { Name = item, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(TileWidth, 96), ToggleMode = true };
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            var selected = state is "pressed" or "hover_pressed";
            var box = UiStyle.Box(selected ? new Color("#F5F9FE") : UiStyle.Surface, 12, selected ? UiStyle.Blue : state == "hover" ? new Color("#9DB6DA") : UiStyle.Border);
            if (selected)
            {
                box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = 2;
            }

            tile.AddThemeStyleboxOverride(state, box);
        }

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        column.OffsetTop = 8;
        column.OffsetLeft = 4;
        column.OffsetRight = -4;
        column.AddThemeConstantOverride("separation", 4);
        var circle = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(40, 40), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        var tint = UiStyle.Box(new Color(colour, 0.18f), 20);
        circle.AddThemeStyleboxOverride("panel", tint);
        var glyph = new TextureRect
        {
            Texture = BuildIcons.Get(BuildIcons.RoomGlyph(item, template), colour.Darkened(0.2f), 48),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(24, 24),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        circle.AddChild(glyph);
        column.AddChild(circle);
        var label = new Label
        {
            Text = ItemName(item),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(TileWidth - 8, 36),
            MaxLinesVisible = 2,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", UiStyle.TextDark);
        label.AddThemeConstantOverride("line_spacing", -2);
        column.AddChild(label);
        tile.AddChild(column);

        // Rooms that sit under more than one department are tagged.
        if (icon.Department is not null && Menu.IconsWith(item).Count(i => i.Department is not null) > 1)
        {
            var tag = new Label { Text = "SHARED", MouseFilter = MouseFilterEnum.Ignore };
            tag.AddThemeFontSizeOverride("font_size", 9);
            tag.AddThemeColorOverride("font_color", UiStyle.Blue);
            var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            var box = UiStyle.Box(UiStyle.BlueTint, 4);
            box.ContentMarginLeft = box.ContentMarginRight = 4;
            chip.AddThemeStyleboxOverride("panel", box);
            chip.AddChild(tag);
            chip.SetAnchorsPreset(LayoutPreset.TopRight);
            chip.GrowHorizontal = GrowDirection.Begin;
            chip.OffsetRight = -5;
            chip.OffsetTop = 5;
            tile.AddChild(chip);
        }

        tile.Pressed += () => Pick(item);
        tile.MouseEntered += () => ShowHover(item, tile);
        tile.MouseExited += () =>
        {
            if (_hoverAnchor == tile)
            {
                _hover.Visible = false;
                _hoverAnchor = null;
            }
        };
        return tile;
    }

    /// <summary>Greys out rooms whose department is shut and marks the one being built.</summary>
    private void RefreshTiles()
    {
        foreach (var (item, tile) in _tiles)
        {
            var available = Available(item);
            tile.Modulate = available ? Colors.White : new Color(1, 1, 1, 0.5f);
            tile.SetPressedNoSignal(item == _selectedItem && _tool.Active);
        }

        if (_openButton is not null && OpenIcon is not null && OpenMenuIcon.Department is { } d)
        {
            var check = _sim.CheckUnlock(d);
            _openButton.Disabled = !check.CanBuild;
            _openButton.TooltipText = check.CanBuild ? string.Empty : string.Join("\n", check.Problems);
        }
    }

    private bool Available(string item) =>
        !_sim.Database.RoomTemplates.TryGetValue(item, out var template) || template.IsCorridor || _sim.IsUnlocked(template.Department);

    /// <summary>Picks a tile in the open pop-up as if clicked (for the smoke test).</summary>
    public void Choose(string item) => Pick(item);

    private void Pick(string item)
    {
        _flash = null;
        if (!Available(item))
        {
            var template = _sim.Database.RoomTemplates[item];
            _tool.Cancel();
            _selectedItem = null;
            _flash = $"[color=#8A5300]Open {_sim.DepartmentName(template.Department)} first to build a {template.Name.ToLowerInvariant()}.[/color]";
            RefreshTiles();
            RefreshStatus();
            return;
        }

        _selectedItem = item;
        switch (item)
        {
            case BuildMenu.ExtendBuilding:
                _tool.StartExtension();
                break;
            case BuildMenu.WaitingChairs:
                _tool.StartChairs();
                break;
            case BuildMenu.WaitingDesk:
                _tool.StartDesk();
                break;
            case BuildMenu.WaitingDoors:
                _tool.StartDoors();
                break;
            case BuildMenu.AddFloor:
                _tool.Cancel();
                _selectedItem = null;
                AddFloor();
                break;
            default:
                var template = _sim.Database.RoomTemplates[item];
                if (template.IsCorridor)
                {
                    _tool.StartCorridor();
                }
                else
                {
                    _tool.StartRoom(template);
                }

                break;
        }

        RefreshTiles();
        RefreshStatus();
    }

    private void AddFloor()
    {
        var next = SiteMap.FloorName(_sim.FloorCount).ToLowerInvariant();
        var result = _sim.AddFloor();
        _flash = result.CanBuild
            ? $"[color=#2E8B57]✓ Built the {next} for £{result.Cost:N0}. Page Up to look at it.[/color]"
            : $"[color=#C4373D]✗ {string.Join("  ✗ ", result.Problems)}[/color]";
        if (_hoverAnchor is not null && _tiles.TryGetValue(BuildMenu.AddFloor, out var tile))
        {
            ShowHover(BuildMenu.AddFloor, tile);
        }
    }

    private void Unlock(DepartmentDefinition department)
    {
        var result = _sim.UnlockDepartment(department.Id);
        var flash = result.CanBuild
            ? $"[color=#2E8B57]✓ {department.Name} is open. Build its rooms, then hire its team.[/color]"
            : $"[color=#C4373D]✗ {string.Join("  ✗ ", result.Problems)}[/color]";
        RebuildPopup();
        _flash = flash;
        RefreshStatus();
        foreach (var icon in _icons.Values)
        {
            icon.QueueRedraw();
        }
    }

    private void RefreshStatus()
    {
        if (_chairRow is not null)
        {
            _chairRow.Visible = _tool.Mode == BuildMode.Chair;
            foreach (var (hex, swatch) in _swatches)
            {
                swatch.SetPressedNoSignal(string.Equals(hex, _tool.ChairColour, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (_tool.Check is { } check && _tool.Active)
        {
            _flash = null;
            if (check.CanBuild)
            {
                var verb = _tool.Mode switch
                {
                    BuildMode.Chair => $"Click to place a chair, drag for a row · facing {_tool.FacingFor(_tool.Hover).ToString().ToLowerInvariant()} · Z turns",
                    BuildMode.Desk => $"Click to place the reception desk · Z turns",
                    BuildMode.Door when _tool.RemovingDoor => "Click to take this door out",
                    BuildMode.Door => "Click to put a door here",
                    _ => _tool.MovingId is null ? "Release to build" : "Release to rebuild here",
                };
                var price = check.Cost switch
                {
                    > 0 => $": £{check.Cost:N0}",
                    < 0 => $": refund £{-check.Cost:N0}",
                    _ => ": free",
                };
                _status.Text = $"[color=#2E8B57]✓ {verb}{price}[/color]";
            }
            else
            {
                _status.Text = $"[color=#C4373D]✗ {string.Join("   ✗ ", check.Problems.Take(3))}[/color]";
            }

            return;
        }

        if (_flash is not null)
        {
            _status.Text = _flash;
            return;
        }

        if (!_tool.Active)
        {
            _selectedItem = null;
        }

        _status.Text = _tool.Active
            ? _tool.Mode switch
            {
                BuildMode.Chair => "Click a waiting room or corridor tile for a chair, drag for a row · Z turns · Right click or Esc stops",
                BuildMode.Desk => "Click inside a waiting room to place its reception desk · Z turns · Right click or Esc stops",
                BuildMode.Door => "Click a waiting room wall: on the front path for the entrance, on a corridor for the way through · Right click or Esc stops",
                _ => "Drag on the floor from corner to corner · Right click or Esc stops",
            }
            : "Hover over a room for details · Click it, then drag on the floor";
    }

    // ------------------------------------------------------------------ hover card

    private void ShowHover(string item, Control anchor)
    {
        _hoverText.Text = HoverText(item);
        _hover.Size = Vector2.Zero;
        _hover.Visible = true;
        _hoverAnchor = anchor;
        PlaceHover();
    }

    private void PlaceHover()
    {
        if (!_hover.Visible || _hoverAnchor is null || !IsInstanceValid(_hoverAnchor))
        {
            _hover.Visible = false;
            return;
        }

        var size = _hover.GetCombinedMinimumSize();
        _hover.Size = size;
        var anchor = _hoverAnchor.GetGlobalRect();
        var screen = GetViewportRect().Size;
        var x = Mathf.Clamp(anchor.GetCenter().X - (size.X / 2), MainHUD.NavWidth + Edge, screen.X - size.X - Edge);
        var y = Mathf.Max(MainHUD.TopBarHeight + Edge, anchor.Position.Y - size.Y - 10);
        _hover.GlobalPosition = new Vector2(x, y);
    }

    private void PlacePopup()
    {
        if (!_popup.Visible || OpenIcon is null || !_icons.TryGetValue(OpenIcon, out var icon))
        {
            return;
        }

        var size = _popup.GetCombinedMinimumSize();
        _popup.Size = size;
        var screen = GetViewportRect().Size;
        var centre = icon.GetGlobalRect().GetCenter().X;
        var x = Mathf.Clamp(centre - (size.X / 2), MainHUD.NavWidth + Edge, Mathf.Max(MainHUD.NavWidth + Edge, screen.X - size.X - Edge));
        var y = _bar.GetGlobalRect().Position.Y - size.Y - 10;
        _popup.GlobalPosition = new Vector2(x, y);
    }

    private string ItemName(string item) => item switch
    {
        BuildMenu.ExtendBuilding => "Extend building",
        BuildMenu.AddFloor => "Add a floor",
        BuildMenu.WaitingChairs => "Waiting chairs",
        BuildMenu.WaitingDesk => "Reception desk",
        BuildMenu.WaitingDoors => "Waiting room doors",
        _ => _sim.Database.RoomTemplates.TryGetValue(item, out var t) ? t.Name : item,
    };

    private string HoverText(string item)
    {
        const string muted = "[color=#5B6B79]";
        var lines = new List<string> { $"[b]{ItemName(item)}[/b]" };
        switch (item)
        {
            case BuildMenu.ExtendBuilding:
            {
                var building = _sim.MainBuilding;
                var b = building.Bounds;
                lines.Add("Drag out more floor space next to the building, onto grass or paving. Keep the entrances, the car park and the ambulance bay clear. Extensions grow every floor.");
                lines.Add($"{muted}Cost[/color]  £{HospitalSimulation.ExtensionCostPerSquareMetre:N0} per m² of new floor, on each floor");
                lines.Add($"{muted}Building now[/color]  {building.AreaSquareMetres:N0} m², {Units.TilesToMetres(b.Width):0.#} × {Units.TilesToMetres(b.Length):0.#} m, {building.Extensions.Count} extension{(building.Extensions.Count == 1 ? string.Empty : "s")}");
                break;
            }

            case BuildMenu.AddFloor:
            {
                var floors = _sim.FloorCount;
                var check = _sim.CheckAddFloor();
                lines.Add("A new floor with the same outline as the ground floor. Lifts and stairs already built carry on up to it. Wheelchairs and trolleys need a lift.");
                lines.Add($"{muted}Floors now[/color]  {string.Join(", ", Enumerable.Range(0, floors).Select(SiteMap.FloorName))}");
                lines.Add($"{muted}Cost[/color]  £{HospitalSimulation.FloorCostPerSquareMetre:N0} per m² for the first floor up, £{HospitalSimulation.FloorCostRisePerSquareMetre:N0} per m² more for each floor higher, plus each lift and staircase");
                lines.Add(check.CanBuild
                    ? $"[color=#2E8B57]Click to build the {SiteMap.FloorName(floors).ToLowerInvariant()} now for £{check.Cost:N0}.[/color]"
                    : $"[color=#C4373D]{string.Join(" ", check.Problems)}[/color]");
                break;
            }

            case BuildMenu.WaitingChairs:
            {
                var inRooms = _sim.Chairs.Count(c => _sim.Spaces.Any(r => r.Kind == CareSpaceKind.Waiting && r.Rect.Contains(c.Tile)));
                lines.Add("Place chairs in a waiting room or anywhere along a corridor. Patients waiting for triage sit in the waiting room; everyone else sits on the free chair nearest where they are going, or stands if there isn't one. Drag for a row, Z turns them, and pick a colour below.");
                lines.Add($"{muted}Cost[/color]  £{_sim.ChairCost:N0} each; turning or recolouring a placed chair is free");
                lines.Add($"{muted}Placed[/color]  {_sim.Chairs.Count} ({inRooms} in waiting rooms, {_sim.Chairs.Count - inRooms} in corridors)");
                break;
            }

            case BuildMenu.WaitingDesk:
            {
                lines.Add("Every waiting room needs a reception desk. The receptionist sits behind it and patients queue in front. Z turns it; placing it again moves it for free.");
                lines.Add($"{muted}Cost[/color]  £{_sim.ReceptionDeskCost:N0}");
                lines.Add($"{muted}Size[/color]  2.5 m long, with a queue of up to {WaitingRoomLayout.MaxQueue} in front");
                break;
            }

            case BuildMenu.WaitingDoors:
            {
                lines.Add("A waiting room needs an entrance door on the front path (the blue mat outside the main entrance) and a door onto a corridor so patients can go through to be seen. Click a door again to take it out.");
                lines.Add($"{muted}Cost[/color]  £{HospitalSimulation.EntranceDoorCost:N0} for the entrance, £{HospitalSimulation.InnerDoorCost:N0} for an inner door; 2 m wide, up to {HospitalSimulation.MaxWaitingDoors} per room");
                if (_sim.MainWaitingRoom is { } room)
                {
                    var problems = _sim.WaitingRoomProblems(room);
                    lines.Add(problems.Count == 0 ? $"[color=#2E8B57]{room.Name} is ready.[/color]" : $"[color=#8A5300]{room.Name}: {string.Join(" ", problems)}[/color]");
                }

                break;
            }

            default:
                if (!_sim.Database.RoomTemplates.TryGetValue(item, out var t))
                {
                    break;
                }

                lines.Add(t.Description);
                if (t.IsCorridor)
                {
                    lines.Add($"{muted}Size[/color]  any, at least {t.MinWidthM:0.#} m wide (2.5 m in clinical areas; 4 m lets two trolleys pass)");
                    lines.Add($"{muted}Cost[/color]  £{t.CostPerSquareMetre:N0} per m² of new floor; floor that is already corridor is free");
                    lines.Add($"{muted}Rule[/color]  must touch or cross another corridor or an entrance");
                    break;
                }

                lines.Add($"{muted}Minimum[/color]  {t.MinWidthM:0.#} × {t.MinLengthM:0.#} m    {muted}Maximum[/color]  {t.MaxWidthM:0.#} × {t.MaxLengthM:0.#} m");
                lines.Add(t.IsShaft
                    ? $"{muted}Cost[/color]  £{t.BaseCost:N0} + £{t.CostPerSquareMetre:N0} per m², on each of the {_sim.FloorCount} floor{(_sim.FloorCount == 1 ? string.Empty : "s")}"
                    : $"{muted}Cost[/color]  £{t.BaseCost:N0} + £{t.CostPerSquareMetre:N0} per m² + furniture");
                lines.Add($"{muted}Staffing[/color]  {t.Staffing}");
                if (t.Sizes.Count > 0 && !t.IsShaft)
                {
                    lines.Add($"{muted}Grows with size[/color]");
                    foreach (var size in t.Sizes)
                    {
                        var (w, d) = (Units.MetresToTiles(size.WidthM), Units.MetresToTiles(size.LengthM));
                        var room = RoomLayoutEngine.Build(t, _sim.Database, "preview", t.Name, new GridRect(0, 0, w, d), WallSide.South);
                        lines.Add($"   {size.WidthM:0.#} × {size.LengthM:0.#} m  →  {Capacity(t, room)}, about £{room.BuildCost:N0}");
                    }
                }

                var users = Menu.IconsWith(item).Where(i => i.Department is not null).Select(i => i.Label).ToList();
                if (users.Count > 1)
                {
                    lines.Add($"{muted}Shared by[/color]  {string.Join(", ", users)}");
                }

                if (!_sim.IsUnlocked(t.Department))
                {
                    lines.Add($"[color=#8A5300]Part of {_sim.DepartmentName(t.Department)}, which isn't open yet.[/color]");
                }

                break;
        }

        return string.Join("\n", lines);
    }

    // ------------------------------------------------------------------ room facts, shared with the room panel

    /// <summary>Size, capacity, cost, equipment and staffing of a laid-out room, for the room panel.</summary>
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
        CareSpaceKind.DeliveryRoom => "1 woman in labour at a time",
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

    /// <summary>One icon in the bar: a white tile with its department's colour across the top, like a hospital sign.</summary>
    private sealed partial class BarIcon : Button
    {
        private readonly BuildMenuIcon _icon;
        private readonly BuildBar _bar;

        public BarIcon(BuildMenuIcon icon, BuildBar bar)
        {
            _icon = icon;
            _bar = bar;
            Name = icon.Id;
            FocusMode = FocusModeEnum.None;
            CustomMinimumSize = new Vector2(66, 76);
            TooltipText = icon.Name;
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            {
                AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            }

            MouseEntered += QueueRedraw;
            MouseExited += QueueRedraw;
        }

        public override void _Draw()
        {
            var size = Size;
            var open = _bar.OpenIcon == _icon.Id && _bar.PopupOpen;
            var hovered = GetGlobalRect().HasPoint(GetGlobalMousePosition());
            if (open)
            {
                var box = UiStyle.Box(UiStyle.BlueTint, 10, UiStyle.Blue);
                box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = 2;
                DrawStyleBox(box, new Rect2(Vector2.Zero, size));
            }
            else if (hovered)
            {
                DrawStyleBox(UiStyle.Box(UiStyle.Ground, 10), new Rect2(Vector2.Zero, size));
            }

            var colour = new Color(_icon.Colour);
            var locked = _bar.IsLocked(_icon);
            var badge = new Rect2((size.X - 44) / 2, 5, 44, 46);
            var body = UiStyle.Box(UiStyle.Surface, 8, UiStyle.Border);
            DrawStyleBox(body, badge);
            var cap = new StyleBoxFlat { BgColor = colour, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8 };
            DrawStyleBox(cap, new Rect2(badge.Position, new Vector2(badge.Size.X, 8)));
            var glyph = BuildIcons.Get(_icon.Glyph, UiStyle.TextDark, 52);
            DrawTextureRect(glyph, new Rect2(badge.Position.X + 9, badge.Position.Y + 14, 26, 26), false, locked ? new Color(1, 1, 1, 0.75f) : Colors.White);

            var font = GetThemeFont("font", "Label");
            const int fontSize = 12;
            DrawString(font, new Vector2(0, size.Y - 9), _icon.Label, HorizontalAlignment.Center, size.X, fontSize, locked ? UiStyle.TextMuted : UiStyle.TextDark);

            if (locked)
            {
                var centre = new Vector2(badge.End.X - 1, badge.Position.Y + 1);
                DrawCircle(centre, 9, UiStyle.Surface);
                DrawArc(centre, 9, 0, Mathf.Tau, 24, UiStyle.Border, 1, true);
                DrawTextureRect(BuildIcons.Get("lock", UiStyle.TextMuted, 24), new Rect2(centre - new Vector2(6, 6), new Vector2(12, 12)), false);
            }
        }
    }
}
