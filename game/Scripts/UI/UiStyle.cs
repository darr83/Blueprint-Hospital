using Godot;

namespace BlueprintHospital.UI;

/// <summary>Shared look for all interface screens: one theme and a few layout helpers.</summary>
public static class UiStyle
{
    public static readonly Color NhsBlue = new("#005EB8");
    public static readonly Color DarkBlue = new("#003087");
    public static readonly Color PanelBackground = new("#F4F6F8");
    public static readonly Color TextDark = new("#212B32");
    public static readonly Color TextMuted = new("#4C6272");
    public static readonly Color TopBarBackground = new("#1D2B36");

    private static Theme? _theme;

    public static Theme Theme => _theme ??= BuildTheme();

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = 17 };

        var panel = new StyleBoxFlat
        {
            BgColor = PanelBackground,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 20, ContentMarginBottom = 20,
            ShadowColor = new Color(0, 0, 0, 0.35f), ShadowSize = 12,
        };
        theme.SetStylebox("panel", "PanelContainer", panel);

        theme.SetStylebox("normal", "Button", ButtonBox(NhsBlue));
        theme.SetStylebox("hover", "Button", ButtonBox(NhsBlue.Lightened(0.12f)));
        theme.SetStylebox("pressed", "Button", ButtonBox(DarkBlue));
        theme.SetStylebox("hover_pressed", "Button", ButtonBox(DarkBlue));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetStylebox("disabled", "Button", ButtonBox(new Color("#AEB7BD")));
        theme.SetColor("font_color", "Button", Colors.White);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Colors.White);
        theme.SetColor("font_hover_pressed_color", "Button", Colors.White);
        theme.SetColor("font_disabled_color", "Button", new Color("#E8EDEE"));

        theme.SetColor("font_color", "Label", TextDark);
        theme.SetColor("font_color", "CheckBox", TextDark);
        theme.SetColor("font_hover_color", "CheckBox", TextDark);
        theme.SetColor("font_pressed_color", "CheckBox", TextDark);
        theme.SetColor("font_hover_pressed_color", "CheckBox", TextDark);
        theme.SetColor("font_color", "ItemList", TextDark);
        theme.SetColor("font_selected_color", "ItemList", Colors.White);

        var field = new StyleBoxFlat
        {
            BgColor = Colors.White, BorderColor = new Color("#AEB7BD"),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        theme.SetStylebox("normal", "LineEdit", field);
        theme.SetColor("font_color", "LineEdit", TextDark);
        theme.SetStylebox("panel", "ItemList", field);
        var selected = new StyleBoxFlat { BgColor = NhsBlue };
        theme.SetStylebox("selected", "ItemList", selected);
        theme.SetStylebox("selected_focus", "ItemList", selected);

        return theme;
    }

    private static StyleBoxFlat ButtonBox(Color colour) => new()
    {
        BgColor = colour,
        CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
        ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 8, ContentMarginBottom = 8,
    };

    public static Label Heading(string text, int size = 28)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", DarkBlue);
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

        AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.45f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });

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
