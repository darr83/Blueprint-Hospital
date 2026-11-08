using Godot;

namespace BlueprintHospital.UI;

/// <summary>Small building blocks shared by the in-game panels: avatars, chips, stat tiles and bars.</summary>
public static class HudWidgets
{
    /// <summary>A round badge with someone's initials.</summary>
    public static (PanelContainer Badge, Label Initials) Avatar(int size = 52)
    {
        var badge = new PanelContainer { CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
        badge.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Blue, size / 2));
        var initials = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        initials.AddThemeColorOverride("font_color", Colors.White);
        initials.AddThemeFontSizeOverride("font_size", size / 3);
        badge.AddChild(initials);
        return (badge, initials);
    }

    public static void SetAvatar((PanelContainer Badge, Label Initials) avatar, string name, Color colour)
    {
        var parts = name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1],
            _ => $"{parts[0][0]}{parts[^1][0]}",
        };
        avatar.Initials.Text = initials.ToUpperInvariant();
        avatar.Initials.AddThemeColorOverride("font_color", colour.Luminance > 0.6f ? UiStyle.TextDark : Colors.White);
        avatar.Badge.AddThemeStyleboxOverride("panel", UiStyle.Box(colour, (int)avatar.Badge.CustomMinimumSize.X / 2));
    }

    /// <summary>A small rounded label, e.g. a triage category or a room.</summary>
    public static (PanelContainer Chip, Label Text) Chip()
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var text = new Label();
        text.AddThemeFontSizeOverride("font_size", 13);
        chip.AddChild(text);
        SetChip((chip, text), string.Empty, UiStyle.Ground);
        return (chip, text);
    }

    public static void SetChip((PanelContainer Chip, Label Text) chip, string text, Color background)
    {
        chip.Text.Text = text;
        chip.Chip.Visible = text.Length > 0;
        var box = UiStyle.Box(background, 8);
        box.ContentMarginLeft = box.ContentMarginRight = 10;
        box.ContentMarginTop = box.ContentMarginBottom = 3;
        chip.Chip.AddThemeStyleboxOverride("panel", box);
        chip.Text.AddThemeColorOverride("font_color", background.Luminance > 0.55f ? UiStyle.TextDark : Colors.White);
    }

    /// <summary>A bordered tile with a big figure and a caption under it.</summary>
    public static (PanelContainer Tile, Label Value) StatTile(string caption)
    {
        var tile = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        tile.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 10, new Color("#E6ECF1"), 10));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        tile.AddChild(column);
        var value = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        value.AddThemeFontSizeOverride("font_size", 19);
        column.AddChild(value);
        var label = UiStyle.Caption(caption, 12);
        column.AddChild(label);
        return (tile, value);
    }

    /// <summary>A tinted box for the one thing that matters most right now ("Doing now", "Waiting for").</summary>
    public static (PanelContainer Box, Label Heading, Label Text, Label Detail) FocusCard()
    {
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#F2F6FC"), 12, new Color("#D6E2F3"), 12));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        box.AddChild(column);
        var heading = UiStyle.Caption(string.Empty, 12);
        heading.AddThemeColorOverride("font_color", UiStyle.Blue);
        column.AddChild(heading);
        var text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        text.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(text);
        var detail = UiStyle.Body(string.Empty, muted: true);
        detail.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(detail);
        return (box, heading, text, detail);
    }

    /// <summary>A labelled bar from 0 to 1, e.g. energy or morale.</summary>
    public static (VBoxContainer Root, Label Value, ProgressBar Bar) Meter(string caption)
    {
        var root = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 4);
        var row = new HBoxContainer();
        var name = UiStyle.Caption(caption, 12);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(name);
        var value = new Label();
        value.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(value);
        root.AddChild(row);
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.01, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
        bar.AddThemeStyleboxOverride("background", UiStyle.Box(new Color("#E6ECF1"), 4));
        root.AddChild(bar);
        return (root, value, bar);
    }

    public static void SetMeter((VBoxContainer Root, Label Value, ProgressBar Bar) meter, double value, string text, Color colour)
    {
        meter.Bar.Value = value;
        meter.Value.Text = text;
        meter.Bar.AddThemeStyleboxOverride("fill", UiStyle.Box(colour, 4));
    }

    /// <summary>Small grey section title.</summary>
    public static Label Section(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", UiStyle.TextDark);
        return label;
    }

    /// <summary>A thin divider line.</summary>
    public static Control Divider() => new ColorRect { Color = new Color("#E6ECF1"), CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };
}
