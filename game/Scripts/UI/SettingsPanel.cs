using BlueprintHospital.Core;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>Settings screen, used from both the main menu and the in-game pause menu.</summary>
public partial class SettingsPanel : ModalPanel
{
    public SettingsPanel()
    {
        var settings = GameManager.Instance.Settings;

        Content.AddChild(UiStyle.Heading("Settings"));

        var fullscreen = new CheckBox { Text = "Fullscreen", ButtonPressed = settings.Fullscreen };
        var vsync = new CheckBox { Text = "VSync", ButtonPressed = settings.VSync };
        var edge = new CheckBox { Text = "Scroll the camera at screen edges", ButtonPressed = settings.EdgeScrolling };
        var grid = new CheckBox { Text = "Show building grid when a game starts", ButtonPressed = settings.ShowGridByDefault };

        Content.AddChild(UiStyle.Body("Display", muted: true));
        Content.AddChild(fullscreen);
        Content.AddChild(vsync);
        var uiScale = AddSlider("Interface size", 0.75, 1.5, 0.05, settings.UiScale, v => $"{v * 100:0}%");

        Content.AddChild(UiStyle.Body("Camera", muted: true));
        var pan = AddSlider("Pan speed", 0.25, 3, 0.05, settings.CameraPanSpeed, v => $"{v:0.00}x");
        Content.AddChild(edge);
        Content.AddChild(grid);

        var audio = BlueprintHospital.Audio.AudioSettings.Current;
        Content.AddChild(UiStyle.Body("Sound", muted: true));
        var master = AddSlider("Master volume", 0, 1, 0.05, audio.Master, Percent);
        var ambience = AddSlider("Ambience", 0, 1, 0.05, audio.Ambience, Percent);
        var effects = AddSlider("Effects", 0, 1, 0.05, audio.Effects, Percent);
        var interfaceSounds = AddSlider("Interface", 0, 1, 0.05, audio.Interface, Percent);
        var music = AddSlider("Music", 0, 1, 0.05, audio.Music, Percent);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(UiStyle.SecondaryButton("Cancel", Close, 120));
        buttons.AddChild(UiStyle.Button("Save", () =>
        {
            settings.Fullscreen = fullscreen.ButtonPressed;
            settings.VSync = vsync.ButtonPressed;
            settings.UiScale = (float)uiScale.Value;
            settings.CameraPanSpeed = (float)pan.Value;
            settings.EdgeScrolling = edge.ButtonPressed;
            settings.ShowGridByDefault = grid.ButtonPressed;
            GameManager.Instance.SaveSettings();
            audio.Master = (float)master.Value;
            audio.Ambience = (float)ambience.Value;
            audio.Effects = (float)effects.Value;
            audio.Interface = (float)interfaceSounds.Value;
            audio.Music = (float)music.Value;
            audio.Save();
            audio.Apply();
            Close();
        }, 120));
        Content.AddChild(buttons);
    }

    private static string Percent(double v) => $"{v * 100:0}%";

    private HSlider AddSlider(string label, double min, double max, double step, double value, System.Func<double, string> format)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var name = new Label { Text = label, CustomMinimumSize = new Vector2(140, 0) };
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(180, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        var readout = new Label { Text = format(value), CustomMinimumSize = new Vector2(60, 0) };
        slider.ValueChanged += v => readout.Text = format(v);
        row.AddChild(name);
        row.AddChild(slider);
        row.AddChild(readout);
        Content.AddChild(row);
        return slider;
    }
}
