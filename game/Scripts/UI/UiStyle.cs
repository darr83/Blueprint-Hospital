using Godot;

namespace BlueprintHospital.UI;

/// <summary>Shared look for all interface screens: one theme and a few layout helpers.</summary>
public static class UiStyle
{
    // "Clinical light": white surfaces, one clear blue, plain dark text.
    public static readonly Color Blue = new("#1F5FBF");
    public static readonly Color BlueHover = new("#2A6FD6");
    public static readonly Color BluePressed = new("#174A94");
    public static readonly Color BlueTint = new("#E6EEFA");
    public static readonly Color Surface = Colors.White;
    public static readonly Color Ground = new("#F2F5F8");
    public static readonly Color Border = new("#D9E1E8");
    public static readonly Color TextDark = new("#17232E");
    public static readonly Color TextMuted = new("#5B6B79");
    public static readonly Color Green = new("#2E8B57");
    public static readonly Color Red = new("#C4373D");
    public static readonly Color AmberText = new("#8A5300");
    public static readonly Color AmberTint = new("#FFF6E6");
    public static readonly Color AmberBorder = new("#F0C878");
    public static readonly Color GreenTint = new("#E8F5EC");
    public static readonly Color RedTint = new("#FCECEC");
    public static readonly Color Hairline = new("#E6ECF1");
    public static readonly Color Subtle = new("#F7F9FB");
    public static readonly Color FieldBorder = new("#B9C4CE");

    // One type scale for every screen. Use these instead of loose numbers.
    public const int SizeDisplay = 26;  // modal and menu titles
    public const int SizeFigure = 22;   // the big number on a stat tile
    public const int SizeTitle = 20;    // side panel titles
    public const int SizeCardTitle = 16; // the name on a card inside a panel
    public const int SizeHeading = 15;  // section headings inside a panel
    public const int SizeBody = 14;
    public const int SizeSmall = 13;
    public const int SizeCaption = 12;

    // One set of corners: panels, cards inside panels, buttons and fields, chips.
    public const int RadiusPanel = 14;
    public const int RadiusCard = 10;
    public const int RadiusControl = 8;
    public const int RadiusChip = 6;

    // Spacing.
    public const int PanelPadding = 20;
    public const int Gap = 12;
    public const int GapSmall = 8;
    public const int CloseSize = 32;

    private static Font? _regular, _medium, _semiBold, _bold;

    /// <summary>Inter, the interface typeface. Falls back to Godot's font for symbols Inter lacks.</summary>
    public static Font Regular => _regular ??= LoadFont("Inter-Regular");
    public static Font Medium => _medium ??= LoadFont("Inter-Medium");
    public static Font SemiBold => _semiBold ??= LoadFont("Inter-SemiBold");
    public static Font Bold => _bold ??= LoadFont("Inter-Bold");

    private static Font LoadFont(string name)
    {
        if (ResourceLoader.Load<FontFile>($"res://Fonts/{name}.ttf") is not { } file)
        {
            return ThemeDB.FallbackFont;
        }

        var font = (FontFile)file.Duplicate();
        font.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
        font.Hinting = TextServer.Hinting.Light;
        font.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        return font;
    }

    // Older names, kept so every screen picks up the new look.
    public static readonly Color NhsBlue = Blue;
    public static readonly Color DarkBlue = BluePressed;
    public static readonly Color PanelBackground = Surface;
    public static readonly Color TopBarBackground = Surface;

    /// <summary>Theme type variation for white buttons with a grey border.</summary>
    public const string Secondary = "SecondaryButton";

    /// <summary>Theme type variation for tabs: white in a grey track, the open one solid blue.</summary>
    public const string Tab = "TabButton";

    /// <summary>Theme type variation for the left-hand navigation buttons.</summary>
    public const string Nav = "NavButton";

    private static Theme? _theme;

    public static Theme Theme => _theme ??= BuildTheme();

    public static StyleBoxFlat Box(Color background, int radius = 12, Color? border = null, int margin = 0)
    {
        var box = new StyleBoxFlat
        {
            BgColor = background,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
        };
        if (border is { } colour)
        {
            box.BorderColor = colour;
            box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = 1;
        }

        return box;
    }

    /// <summary>A white card with a soft border and shadow, used for every panel.</summary>
    public static StyleBoxFlat Card(int margin = PanelPadding, int radius = RadiusPanel)
    {
        var box = Box(Surface, radius, Border, margin);
        box.ShadowColor = new Color(0.09f, 0.14f, 0.18f, 0.14f);
        box.ShadowSize = 12;
        box.ShadowOffset = new Vector2(0, 4);
        box.AntiAliasing = true;
        return box;
    }

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = 15, DefaultFont = Regular };
        theme.SetStylebox("panel", "PanelContainer", Card(24));
        theme.SetFont("font", "Button", SemiBold);
        theme.SetFontSize("font_size", "Button", SizeBody);
        AddControls(theme);

        theme.SetStylebox("normal", "Button", ButtonBox(Blue));
        theme.SetStylebox("hover", "Button", ButtonBox(BlueHover));
        theme.SetStylebox("pressed", "Button", ButtonBox(BluePressed));
        theme.SetStylebox("hover_pressed", "Button", ButtonBox(BluePressed));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetStylebox("disabled", "Button", ButtonBox(new Color("#C3CCD4")));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
        {
            theme.SetColor(state, "Button", Colors.White);
        }

        theme.SetColor("font_disabled_color", "Button", new Color("#F4F6F8"));

        theme.SetTypeVariation(Secondary, "Button");
        theme.SetStylebox("normal", Secondary, ButtonBox(Surface, Border));
        theme.SetStylebox("hover", Secondary, ButtonBox(Ground, Border));
        theme.SetStylebox("pressed", Secondary, ButtonBox(BlueTint, Blue));
        theme.SetStylebox("hover_pressed", Secondary, ButtonBox(BlueTint, Blue));
        theme.SetStylebox("focus", Secondary, new StyleBoxEmpty());
        theme.SetStylebox("disabled", Secondary, ButtonBox(Ground, Border));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_focus_color" })
        {
            theme.SetColor(state, Secondary, TextDark);
        }

        theme.SetColor("font_pressed_color", Secondary, Blue);
        theme.SetColor("font_hover_pressed_color", Secondary, Blue);
        theme.SetColor("font_disabled_color", Secondary, new Color("#9AA6B1"));

        theme.SetTypeVariation(Tab, "Button");
        StyleBoxFlat TabBox(Color colour, Color border)
        {
            var box = Box(colour, RadiusControl, border);
            box.ContentMarginLeft = box.ContentMarginRight = 14;
            box.ContentMarginTop = box.ContentMarginBottom = 6;
            return box;
        }

        var tabOn = TabBox(Blue, Blue);
        tabOn.ShadowColor = new Color(0.12f, 0.37f, 0.75f, 0.3f);
        tabOn.ShadowSize = 4;
        tabOn.ShadowOffset = new Vector2(0, 2);
        theme.SetStylebox("normal", Tab, TabBox(Surface, Border));
        theme.SetStylebox("hover", Tab, TabBox(new Color("#F5F8FB"), FieldBorder));
        theme.SetStylebox("pressed", Tab, tabOn);
        theme.SetStylebox("hover_pressed", Tab, tabOn);
        theme.SetStylebox("focus", Tab, new StyleBoxEmpty());
        theme.SetStylebox("disabled", Tab, TabBox(Ground, Border));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_focus_color" })
        {
            theme.SetColor(state, Tab, TextDark);
        }

        theme.SetColor("font_pressed_color", Tab, Colors.White);
        theme.SetColor("font_hover_pressed_color", Tab, Colors.White);
        theme.SetFontSize("font_size", Tab, SizeSmall);

        theme.SetTypeVariation(Nav, "Button");
        var navNormal = Box(new Color(0, 0, 0, 0), 10);
        theme.SetStylebox("normal", Nav, navNormal);
        theme.SetStylebox("hover", Nav, Box(Ground, 10));
        theme.SetStylebox("pressed", Nav, Box(BlueTint, 10));
        theme.SetStylebox("hover_pressed", Nav, Box(BlueTint, 10));
        theme.SetStylebox("focus", Nav, new StyleBoxEmpty());
        foreach (var state in new[] { "font_color", "font_hover_color", "font_focus_color" })
        {
            theme.SetColor(state, Nav, TextMuted);
        }

        theme.SetColor("font_pressed_color", Nav, Blue);
        theme.SetColor("font_hover_pressed_color", Nav, Blue);
        theme.SetFontSize("font_size", Nav, 12);
        theme.SetFont("font", Nav, Medium);

        theme.SetColor("font_color", "Label", TextDark);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
        {
            theme.SetColor(state, "CheckBox", TextDark);
        }

        theme.SetColor("default_color", "RichTextLabel", TextDark);
        theme.SetColor("font_color", "ItemList", TextDark);
        theme.SetColor("font_selected_color", "ItemList", Colors.White);

        var field = Box(Surface, RadiusControl, FieldBorder);
        field.ContentMarginLeft = field.ContentMarginRight = 10;
        field.ContentMarginTop = field.ContentMarginBottom = 7;
        theme.SetStylebox("normal", "LineEdit", field);
        var focused = Box(Surface, RadiusControl, Blue);
        focused.ContentMarginLeft = focused.ContentMarginRight = 10;
        focused.ContentMarginTop = focused.ContentMarginBottom = 7;
        theme.SetStylebox("focus", "LineEdit", focused);
        theme.SetColor("font_color", "LineEdit", TextDark);
        theme.SetColor("font_placeholder_color", "LineEdit", new Color("#8C99A5"));
        theme.SetColor("caret_color", "LineEdit", Blue);
        theme.SetColor("selection_color", "LineEdit", BlueTint);
        theme.SetStylebox("panel", "ItemList", field);
        var selected = Box(Blue, 6);
        theme.SetStylebox("selected", "ItemList", selected);
        theme.SetStylebox("selected_focus", "ItemList", selected);

        theme.SetColor("font_color", "TooltipLabel", TextDark);
        var tip = Box(Surface, 8, Border, 8);
        theme.SetStylebox("panel", "TooltipPanel", tip);

        return theme;
    }

    private static StyleBoxFlat ButtonBox(Color colour, Color? border = null)
    {
        var box = Box(colour, RadiusControl, border);
        box.ContentMarginLeft = box.ContentMarginRight = 16;
        box.ContentMarginTop = box.ContentMarginBottom = 8;
        return box;
    }

    public static Label Heading(string text, int size = SizeDisplay)
    {
        var label = new Label { Text = text };
        label.AddThemeFontOverride("font", SemiBold);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", TextDark);
        return label;
    }

    /// <summary>A small grey caption, e.g. above a figure.</summary>
    public static Label Caption(string text, int size = SizeSmall)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", TextMuted);
        return label;
    }

    public static Label Body(string text, bool muted = false)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        if (muted)
        {
            label.AddThemeColorOverride("font_color", TextMuted);
        }

        return label;
    }

    public static Button Button(string text, System.Action onPressed, float minWidth = 0)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 40) };
        button.Pressed += onPressed;
        return button;
    }

    /// <summary>A white button with a grey border, for less important actions.</summary>
    public static Button SecondaryButton(string text, System.Action onPressed, float minWidth = 0)
    {
        var button = Button(text, onPressed, minWidth);
        button.ThemeTypeVariation = Secondary;
        return button;
    }

    /// <summary>The same small square close button on every panel.</summary>
    public static Button CloseButton(System.Action onPressed)
    {
        var button = new Button
        {
            Text = "✕",
            ThemeTypeVariation = Secondary,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(CloseSize, CloseSize),
            TooltipText = "Close (Esc)",
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var normal = ButtonBox(Surface, Border);
        normal.ContentMarginLeft = normal.ContentMarginRight = normal.ContentMarginTop = normal.ContentMarginBottom = 0;
        var hover = ButtonBox(Ground, Border);
        hover.ContentMarginLeft = hover.ContentMarginRight = hover.ContentMarginTop = hover.ContentMarginBottom = 0;
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", TextMuted);
        button.AddThemeColorOverride("font_hover_color", TextDark);
        button.Pressed += onPressed;
        return button;
    }

    /// <summary>
    /// The header every side panel and window uses: title, optional grey subtitle, an actions slot on the
    /// right (tabs, buttons) and the close button.
    /// </summary>
    public static PanelHeaderParts PanelHeader(string title, string? subtitle = null, System.Action? onClose = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 2);
        var titleLabel = new Label { Text = title, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        titleLabel.AddThemeFontOverride("font", SemiBold);
        titleLabel.AddThemeFontSizeOverride("font_size", SizeTitle);
        titleLabel.AddThemeColorOverride("font_color", TextDark);
        titles.AddChild(titleLabel);
        var subtitleLabel = Caption(subtitle ?? string.Empty, SizeSmall);
        subtitleLabel.Visible = !string.IsNullOrEmpty(subtitle);
        subtitleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        titles.AddChild(subtitleLabel);
        row.AddChild(titles);
        var actions = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        actions.AddThemeConstantOverride("separation", 6);
        row.AddChild(actions);
        Button? close = null;
        if (onClose is not null)
        {
            close = CloseButton(onClose);
            close.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            row.AddChild(close);
        }

        return new PanelHeaderParts(row, titleLabel, subtitleLabel, actions, close);
    }

    /// <summary>The grey track that holds a row of tabs, as on the patient record.</summary>
    public static PanelContainer TabTrack(Control row)
    {
        var track = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        track.AddThemeStyleboxOverride("panel", Box(new Color("#E9EEF2"), RadiusControl + 3, null, 3));
        if (row is BoxContainer box)
        {
            box.AddThemeConstantOverride("separation", 3);
        }

        track.AddChild(row);
        return track;
    }

    /// <summary>Makes a toggle button a tab.</summary>
    public static Button AsTab(Button button)
    {
        button.ThemeTypeVariation = Tab;
        button.ToggleMode = true;
        button.FocusMode = Control.FocusModeEnum.None;
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        return button;
    }

    /// <summary>A heading for a group of rows inside a panel, e.g. "Display" or "Waiting for".</summary>
    public static Label SectionHeading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontOverride("font", SemiBold);
        label.AddThemeFontSizeOverride("font_size", SizeHeading);
        label.AddThemeColorOverride("font_color", TextDark);
        return label;
    }

    /// <summary>Sets a label in the semibold weight, for figures and names.</summary>
    public static T Strong<T>(T control, int size = 0) where T : Control
    {
        control.AddThemeFontOverride("font", SemiBold);
        if (size > 0)
        {
            control.AddThemeFontSizeOverride("font_size", size);
        }

        return control;
    }

    private static readonly System.Text.RegularExpressions.Regex FactLine = new(@"^\[color=#5B6B79\]([^\[\]]{1,40})\[/color\]  (.*)$");

    /// <summary>
    /// Lines written as "[color=#5B6B79]Label[/color]  value" become a two-column table, so every value
    /// lines up under the last instead of running on straight after its label.
    /// </summary>
    public static string Facts(string text)
    {
        if (text.Contains("[table", System.StringComparison.Ordinal))
        {
            return text;
        }

        var sb = new System.Text.StringBuilder();
        var inTable = false;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var match = FactLine.Match(lines[i]);
            if (match.Success)
            {
                if (!inTable)
                {
                    sb.Append("[table=2]");
                    inTable = true;
                }

                sb.Append($"[cell padding=0,2,14,2][color=#5B6B79]{match.Groups[1].Value}[/color][/cell][cell expand=1 padding=0,2,0,2]{match.Groups[2].Value}[/cell]");
                continue;
            }

            if (inTable)
            {
                sb.Append("[/table]\n");
                inTable = false;
            }

            sb.Append(lines[i]);
            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }
        }

        if (inTable)
        {
            sb.Append("[/table]");
        }

        return sb.ToString();
    }

    /// <summary>A white card inside a panel, with a thin border.</summary>
    public static StyleBoxFlat Inset(int margin = 12, Color? background = null, Color? border = null) =>
        Box(background ?? Surface, RadiusCard, border ?? Hairline, margin);

    private static void AddControls(Theme theme)
    {
        // Tick boxes: drawn boxes rather than the button look they inherit.
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            var empty = new StyleBoxEmpty { ContentMarginLeft = 0, ContentMarginRight = 4, ContentMarginTop = 4, ContentMarginBottom = 4 };
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            {
                theme.SetStylebox(state, type, empty);
            }

            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            {
                theme.SetColor(state, type, TextDark);
            }

            theme.SetColor("font_disabled_color", type, new Color("#9AA6B1"));
            theme.SetConstant("h_separation", type, 10);
            theme.SetFont("font", type, Regular);
            theme.SetFontSize("font_size", type, SizeBody);
        }

        theme.SetIcon("checked", "CheckBox", Svg(TickBox(true, false), 20));
        theme.SetIcon("unchecked", "CheckBox", Svg(TickBox(false, false), 20));
        theme.SetIcon("checked_disabled", "CheckBox", Svg(TickBox(true, true), 20));
        theme.SetIcon("unchecked_disabled", "CheckBox", Svg(TickBox(false, true), 20));
        theme.SetIcon("radio_checked", "CheckBox", Svg(Radio(true), 20));
        theme.SetIcon("radio_unchecked", "CheckBox", Svg(Radio(false), 20));
        theme.SetIcon("checked", "CheckButton", Svg(Switch(true), 36, 20));
        theme.SetIcon("unchecked", "CheckButton", Svg(Switch(false), 36, 20));

        // Sliders: a slim grey track, blue fill, white round handle.
        var track = Box(new Color("#E1E7ED"), 3);
        track.ContentMarginTop = track.ContentMarginBottom = 3;
        theme.SetStylebox("slider", "HSlider", track);
        var fill = Box(Blue, 3);
        fill.ContentMarginTop = fill.ContentMarginBottom = 3;
        theme.SetStylebox("grabber_area", "HSlider", fill);
        var fillHover = Box(BlueHover, 3);
        fillHover.ContentMarginTop = fillHover.ContentMarginBottom = 3;
        theme.SetStylebox("grabber_area_highlight", "HSlider", fillHover);
        theme.SetIcon("grabber", "HSlider", Svg(Handle(false), 18));
        theme.SetIcon("grabber_highlight", "HSlider", Svg(Handle(true), 18));

        // Scroll bars: slim and rounded.
        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            var vertical = type == "VScrollBar";
            StyleBoxFlat Bar(Color colour)
            {
                var bar = Box(colour, 4);
                if (vertical)
                {
                    bar.ContentMarginLeft = bar.ContentMarginRight = 4;
                }
                else
                {
                    bar.ContentMarginTop = bar.ContentMarginBottom = 4;
                }

                return bar;
            }

            theme.SetStylebox("scroll", type, Bar(new Color(0, 0, 0, 0)));
            theme.SetStylebox("scroll_focus", type, Bar(new Color(0, 0, 0, 0)));
            theme.SetStylebox("grabber", type, Bar(new Color("#CBD3DB")));
            theme.SetStylebox("grabber_highlight", type, Bar(new Color("#AEB9C4")));
            theme.SetStylebox("grabber_pressed", type, Bar(new Color("#94A1AE")));
        }

        // Drop-down lists look like the white secondary buttons, with a chevron.
        theme.SetStylebox("normal", "OptionButton", ButtonBox(Surface, FieldBorder));
        theme.SetStylebox("hover", "OptionButton", ButtonBox(Ground, FieldBorder));
        theme.SetStylebox("pressed", "OptionButton", ButtonBox(BlueTint, Blue));
        theme.SetStylebox("hover_pressed", "OptionButton", ButtonBox(BlueTint, Blue));
        theme.SetStylebox("focus", "OptionButton", new StyleBoxEmpty());
        theme.SetStylebox("disabled", "OptionButton", ButtonBox(Ground, Border));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
        {
            theme.SetColor(state, "OptionButton", TextDark);
        }

        theme.SetFont("font", "OptionButton", Regular);
        theme.SetIcon("arrow", "OptionButton", Svg(Chevron(), 14));
        theme.SetConstant("arrow_margin", "OptionButton", 10);

        // Menus that drop down from those lists.
        var menu = Box(Surface, RadiusControl, Border, 6);
        menu.ShadowColor = new Color(0.09f, 0.14f, 0.18f, 0.14f);
        menu.ShadowSize = 8;
        theme.SetStylebox("panel", "PopupMenu", menu);
        theme.SetStylebox("hover", "PopupMenu", Box(BlueTint, RadiusChip));
        theme.SetColor("font_color", "PopupMenu", TextDark);
        theme.SetColor("font_hover_color", "PopupMenu", Blue);
        theme.SetConstant("v_separation", "PopupMenu", 8);

        // Progress bars.
        theme.SetStylebox("background", "ProgressBar", Box(Hairline, 4));
        theme.SetStylebox("fill", "ProgressBar", Box(Blue, 4));

        theme.SetColor("font_color", "Label", TextDark);
        theme.SetConstant("separation", "HSeparator", 12);
        theme.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = Hairline, Thickness = 1 });

        theme.SetFont("bold_font", "RichTextLabel", SemiBold);
        theme.SetFont("normal_font", "RichTextLabel", Regular);
        theme.SetColor("table_odd_row_bg", "RichTextLabel", new Color(0, 0, 0, 0));
        theme.SetColor("table_even_row_bg", "RichTextLabel", new Color(0, 0, 0, 0));
    }

    private static string Hex(Color c) => "#" + c.ToHtml(false);

    private static string TickBox(bool on, bool disabled)
    {
        var fill = disabled ? "#E6ECF1" : on ? Hex(Blue) : "#FFFFFF";
        var stroke = disabled ? "#C3CCD4" : on ? Hex(Blue) : Hex(FieldBorder);
        var tick = on ? $"<path d='M5.5 10.3l3 3 6-6.6' fill='none' stroke='{(disabled ? "#9AA6B1" : "#FFFFFF")}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/>" : string.Empty;
        return $"<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><rect x='1' y='1' width='18' height='18' rx='5' fill='{fill}' stroke='{stroke}' stroke-width='1.5'/>{tick}</svg>";
    }

    private static string Radio(bool on) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><circle cx='10' cy='10' r='8.5' fill='#FFFFFF' stroke='{(on ? Hex(Blue) : Hex(FieldBorder))}' stroke-width='1.5'/>{(on ? $"<circle cx='10' cy='10' r='4.5' fill='{Hex(Blue)}'/>" : string.Empty)}</svg>";

    private static string Switch(bool on) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='36' height='20'><rect x='0.5' y='0.5' width='35' height='19' rx='9.5' fill='{(on ? Hex(Blue) : "#C9D2DA")}'/><circle cx='{(on ? 26 : 10)}' cy='10' r='7.5' fill='#FFFFFF'/></svg>";

    private static string Handle(bool hover) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='18' height='18'><circle cx='9' cy='9' r='7.5' fill='#FFFFFF' stroke='{(hover ? Hex(BlueHover) : Hex(Blue))}' stroke-width='2'/></svg>";

    private static string Chevron() =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='14' height='14'><path d='M3.5 5.5l3.5 3.5 3.5-3.5' fill='none' stroke='{Hex(TextMuted)}' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'/></svg>";

    /// <summary>Renders a small SVG at the current interface scale.</summary>
    private static Texture2D Svg(string svg, int width, int height = 0)
    {
        var image = new Image();
        var error = image.LoadSvgFromString(svg, 1f);
        if (error != Error.Ok)
        {
            image = Image.CreateEmpty(width, height == 0 ? width : height, false, Image.Format.Rgba8);
        }

        return ImageTexture.CreateFromImage(image);
    }
}

/// <summary>The parts of a standard panel header.</summary>
public sealed record PanelHeaderParts(HBoxContainer Row, Label Title, Label Subtitle, HBoxContainer Actions, Button? Close);

/// <summary>A dimmed full-screen overlay with a centred panel. Closes with Escape.</summary>
public partial class ModalPanel : Control
{
    private readonly VBoxContainer _content = new() { CustomMinimumSize = new Vector2(420, 0) };

    public ModalPanel()
    {
        Theme = UiStyle.Theme;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;

        AddChild(new ColorRect { Color = new Color(0.09f, 0.14f, 0.18f, 0.45f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });

        var centre = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(centre);
        var panel = new PanelContainer();
        centre.AddChild(panel);
        _content.AddThemeConstantOverride("separation", 12);
        panel.AddChild(_content);
    }

    public VBoxContainer Content => _content;

    /// <summary>Raised when the panel closes, by button or Escape.</summary>
    public event System.Action? Closed;

    public bool CloseOnEscape { get; set; } = true;

    public void Close()
    {
        Closed?.Invoke();
        QueueFree();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (CloseOnEscape && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }
}
