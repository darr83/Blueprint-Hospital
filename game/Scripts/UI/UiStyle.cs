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

    // Older names, kept so every screen picks up the new look.
    public static readonly Color NhsBlue = Blue;
    public static readonly Color DarkBlue = BluePressed;
    public static readonly Color PanelBackground = Surface;
    public static readonly Color TopBarBackground = Surface;

    /// <summary>Theme type variation for white buttons with a grey border.</summary>
    public const string Secondary = "SecondaryButton";

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
    public static StyleBoxFlat Card(int margin = 16, int radius = 16)
    {
        var box = Box(Surface, radius, Border, margin);
        box.ShadowColor = new Color(0.09f, 0.14f, 0.18f, 0.12f);
        box.ShadowSize = 10;
        box.ShadowOffset = new Vector2(0, 4);
        return box;
    }

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = 16 };
        theme.SetStylebox("panel", "PanelContainer", Card(24));

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

        theme.SetColor("font_color", "Label", TextDark);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
        {
            theme.SetColor(state, "CheckBox", TextDark);
        }

        theme.SetColor("default_color", "RichTextLabel", TextDark);
        theme.SetColor("font_color", "ItemList", TextDark);
        theme.SetColor("font_selected_color", "ItemList", Colors.White);

        var field = Box(Surface, 8, new Color("#B9C4CE"));
        field.ContentMarginLeft = field.ContentMarginRight = 10;
        field.ContentMarginTop = field.ContentMarginBottom = 7;
        theme.SetStylebox("normal", "LineEdit", field);
        var focused = Box(Surface, 8, Blue);
        focused.ContentMarginLeft = focused.ContentMarginRight = 10;
        focused.ContentMarginTop = focused.ContentMarginBottom = 7;
        theme.SetStylebox("focus", "LineEdit", focused);
        theme.SetColor("font_color", "LineEdit", TextDark);
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
        var box = Box(colour, 10, border);
        box.ContentMarginLeft = box.ContentMarginRight = 16;
        box.ContentMarginTop = box.ContentMarginBottom = 8;
        return box;
    }

    public static Label Heading(string text, int size = 28)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", TextDark);
        return label;
    }

    /// <summary>A small grey caption, e.g. above a figure.</summary>
    public static Label Caption(string text, int size = 13)
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
}

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
