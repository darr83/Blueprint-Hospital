using BlueprintHospital.Core;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>Title screen: new game, load game, settings and quit.</summary>
public partial class MainMenu : Control
{
    public override void _Ready()
    {
        Theme = UiStyle.Theme;
        var manager = GameManager.Instance;

        AddChild(new ColorRect { Color = UiStyle.Ground, AnchorRight = 1, AnchorBottom = 1 });
        AddChild(new ColorRect { Color = UiStyle.NhsBlue, AnchorRight = 1, OffsetBottom = 10 });

        var centre = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(centre);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        column.AddThemeConstantOverride("separation", 14);
        centre.AddChild(column);

        var title = UiStyle.Heading("Blueprint Hospital", 56);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);
        var subtitle = UiStyle.Body("An NHS-inspired hospital management simulation", muted: true);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(subtitle);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        var newGame = UiStyle.Button("New Game", ShowNewGame);
        var loadGame = UiStyle.SecondaryButton("Load Game", () => AddChild(new LoadGamePanel()));
        column.AddChild(newGame);
        column.AddChild(loadGame);
        column.AddChild(UiStyle.SecondaryButton("Settings", () => AddChild(new SettingsPanel())));
        column.AddChild(UiStyle.SecondaryButton("Quit", () => GetTree().Quit()));

        if (manager.DataLoadError is { } error)
        {
            newGame.Disabled = true;
            loadGame.Disabled = true;
            var errorLabel = UiStyle.Body("The game data could not be loaded, so a game can't be started. Fix these problems and restart:\n\n" + error);
            errorLabel.AddThemeColorOverride("font_color", new Color("#B71C1C"));
            errorLabel.CustomMinimumSize = new Vector2(700, 0);
            column.AddChild(errorLabel);
        }

        var version = new Label { Text = $"Version {ProjectSettings.GetSetting("application/config/version")}" };
        version.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        version.SetAnchorsPreset(LayoutPreset.BottomLeft);
        version.Position = new Vector2(16, -36);
        version.GrowVertical = GrowDirection.Begin;
        AddChild(version);

        if (manager.IsSmokeTest)
        {
            SmokeTest.Begin(this);
        }
    }

    private void ShowNewGame()
    {
        var panel = new ModalPanel();
        panel.Content.AddChild(UiStyle.Heading("New Game"));
        panel.Content.AddChild(UiStyle.Body("Name your hospital"));
        var name = new LineEdit { Text = "Blueprint General Hospital", MaxLength = 48, CustomMinimumSize = new Vector2(380, 0) };
        panel.Content.AddChild(name);
        panel.Content.AddChild(UiStyle.Body(
            "You start with an empty hospital building, an A&E entrance and an ambulance bay.",
            muted: true));

        void Start() => GameManager.Instance.StartNewGame(name.Text);
        name.TextSubmitted += _ => Start();

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(UiStyle.SecondaryButton("Back", panel.Close, 120));
        buttons.AddChild(UiStyle.Button("Start", Start, 120));
        panel.Content.AddChild(buttons);
        AddChild(panel);
        name.GrabFocus();
        name.SelectAll();
    }
}
