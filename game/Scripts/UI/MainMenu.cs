using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Simulation;
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
        else if (!BlueprintHospital.Core.ModelShots.Begin() && !BlueprintHospital.Core.PeopleShots3D.Begin() && !BlueprintHospital.Core.AnimShots.Begin() && !BlueprintHospital.Core.SeatShots.Begin() && !BlueprintHospital.Core.CareerShots.Begin(this))
        {
            ArtShots.Begin(this);
        }
    }

    /// <summary>Opens the New Game panel: the hospital's name and how to start.</summary>
    public void ShowNewGame()
    {
        var panel = new ModalPanel();
        panel.Content.AddChild(UiStyle.Heading("New Game"));
        panel.Content.AddChild(UiStyle.Body("Name your hospital"));
        var name = new LineEdit { Text = "Blueprint General Hospital", MaxLength = 48, CustomMinimumSize = new Vector2(380, 0) };
        panel.Content.AddChild(name);
        panel.Content.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        panel.Content.AddChild(UiStyle.Body("How do you want to start?"));

        var start = StartingHospital.SmallEmergency;
        var type = GameType.Career;
        var choices = new VBoxContainer();
        choices.AddThemeConstantOverride("separation", 8);
        var group = new ButtonGroup();
        Button Choice(string title, string detail, StartingHospital value, GameType gameType = GameType.Sandbox)
        {
            var button = new Button
            {
                Text = $"{title}\n{detail}",
                ToggleMode = true,
                ButtonGroup = group,
                ThemeTypeVariation = UiStyle.Secondary,
                Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(380, 64),
                FocusMode = FocusModeEnum.None,
            };
            button.Toggled += on =>
            {
                if (on)
                {
                    start = value;
                    type = gameType;
                }

                // The chosen card is tinted blue.
                var box = UiStyle.Box(on ? UiStyle.BlueTint : UiStyle.Surface, 10, on ? UiStyle.Blue : UiStyle.Border, 12);
                foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
                {
                    button.AddThemeStyleboxOverride(state, box);
                }
            };
            return button;
        }

        var career = Choice("Career (recommended)", "Start at level 1 as a small walk-in centre with a team. Meet each level's goals to unlock more rooms, staff, patients and departments.", StartingHospital.SmallEmergency, GameType.Career);
        var small = Choice("Sandbox: small A&E with a team", "Everything unlocked. Cubicles, resus, triage, offices and 9 staff. Patients start arriving straight away.", StartingHospital.SmallEmergency);
        var empty = Choice("Sandbox: empty building", "Everything unlocked. Just the building and roads, with more money. Build and hire everything; A&E opens when it has a waiting room, a triage room, a cubicle, a nurse and a doctor.", StartingHospital.EmptyBuilding);
        choices.AddChild(career);
        choices.AddChild(small);
        choices.AddChild(empty);
        panel.Content.AddChild(choices);
        career.ButtonPressed = true;
        foreach (var other in new[] { small, empty })
        {
            other.SetPressedNoSignal(false);
            other.EmitSignal(BaseButton.SignalName.Toggled, false);
        }

        // How often patients already in a bed have a sudden emergency (a fall, sepsis, a heart attack...).
        panel.Content.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2) });
        panel.Content.AddChild(UiStyle.Body("Emergencies for patients in a bed"));
        var emergencies = WardEmergencyFrequency.Realistic;
        var frequency = new HBoxContainer();
        frequency.AddThemeConstantOverride("separation", 8);
        var frequencyGroup = new ButtonGroup();
        var frequencyNote = UiStyle.Body(string.Empty);
        frequencyNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        frequencyNote.CustomMinimumSize = new Vector2(380, 0);
        frequencyNote.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        void ShowNote() => frequencyNote.Text = emergencies switch
        {
            WardEmergencyFrequency.Off => "None: patients in bed only get better.",
            WardEmergencyFrequency.Rare => "Now and then: about one every three days with 30 patients in bed.",
            _ => "Like a real hospital: falls, sepsis, heart attacks and more, about one a day with 30 patients in bed.",
        };
        foreach (var (label, value) in new[] { ("Off", WardEmergencyFrequency.Off), ("Rare", WardEmergencyFrequency.Rare), ("Realistic", WardEmergencyFrequency.Realistic) })
        {
            var option = new Button
            {
                Text = label,
                ToggleMode = true,
                ButtonGroup = frequencyGroup,
                ThemeTypeVariation = UiStyle.Secondary,
                CustomMinimumSize = new Vector2(120, 34),
                FocusMode = FocusModeEnum.None,
            };
            option.Toggled += on =>
            {
                if (on)
                {
                    emergencies = value;
                    ShowNote();
                }

                var box = UiStyle.Box(on ? UiStyle.BlueTint : UiStyle.Surface, 8, on ? UiStyle.Blue : UiStyle.Border, 8);
                foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
                {
                    option.AddThemeStyleboxOverride(state, box);
                }
            };
            frequency.AddChild(option);
            option.ButtonPressed = value == WardEmergencyFrequency.Realistic;
            if (value != WardEmergencyFrequency.Realistic)
            {
                option.EmitSignal(BaseButton.SignalName.Toggled, false);
            }
        }

        panel.Content.AddChild(frequency);
        panel.Content.AddChild(frequencyNote);
        ShowNote();

        var unlimited = new CheckBox
        {
            Text = "Unlimited money: keep building and hiring when the balance goes into the red",
            FocusMode = FocusModeEnum.None,
            TooltipText = "Money and finances are still counted and shown; you just can't run out.",
        };
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
        {
            unlimited.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }

        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
        {
            unlimited.AddThemeColorOverride(state, UiStyle.TextDark);
        }

        panel.Content.AddChild(unlimited);

        void Start() => GameManager.Instance.StartNewGame(name.Text, start, unlimited.ButtonPressed, emergencies, type);
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
