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
        var lighting = new CheckBox { Text = BlueprintHospital.Core.GameView.ThreeD ? "Enhanced lighting (turn off on slower computers)" : "Shadows and evening light (turn off on slower computers)", ButtonPressed = settings.EnhancedLighting };
        var surfaces = new CheckBox { Text = BlueprintHospital.Core.GameView.ThreeD ? "Surface textures on walls, floors and furniture" : "Fine detail on the grounds and floors", ButtonPressed = settings.SurfaceTextures };
        var outlines = new CheckBox { Text = "Outlines round walls, furniture and people (can shimmer when scrolling)", ButtonPressed = settings.Outlines };
        var edge = new CheckBox { Text = "Scroll the camera at screen edges", ButtonPressed = settings.EdgeScrolling };
        var grid = new CheckBox { Text = "Show building grid when a game starts", ButtonPressed = settings.ShowGridByDefault };
        AutoStaff = new CheckBox { Text = "New rooms come with their minimum staff", ButtonPressed = settings.AutoStaffNewRooms };
        Decorations = new CheckBox { Text = "Seasonal decorations (Christmas, Halloween, Easter)", ButtonPressed = settings.SeasonalDecorations };

        // Two columns, so the whole panel (and its Save button) fits a 900-pixel-high screen.
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 28);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        left.AddThemeConstantOverride("separation", 12);
        right.AddThemeConstantOverride("separation", 12);
        columns.AddChild(left);
        columns.AddChild(right);
        Content.AddChild(columns);

        left.AddChild(UiStyle.Body("Display", muted: true));
        left.AddChild(fullscreen);
        left.AddChild(vsync);
        left.AddChild(lighting);
        left.AddChild(surfaces);
        if (BlueprintHospital.Core.GameView.ThreeD)
        {
            left.AddChild(outlines);
        }
        var uiScale = AddSlider(left, "Interface size", 0.75, 1.5, 0.05, settings.UiScale, v => $"{v * 100:0}%");

        left.AddChild(UiStyle.Body("Camera", muted: true));
        var pan = AddSlider(left, "Pan speed", 0.25, 3, 0.05, settings.CameraPanSpeed, v => $"{v:0.00}x");
        left.AddChild(edge);
        left.AddChild(grid);

        right.AddChild(UiStyle.Body("Gameplay", muted: true));
        right.AddChild(AutoStaff);
        var autoStaffNote = UiStyle.Body("Each room you build hires the staff it needs to open, on the lowest grade and paid as normal. Off: rooms come empty and you hire everyone (H).", muted: true);
        autoStaffNote.CustomMinimumSize = new Vector2(400, 0);
        right.AddChild(autoStaffNote);
        right.AddChild(Decorations);

        var audio = BlueprintHospital.Audio.AudioSettings.Current;
        right.AddChild(UiStyle.Body("Sound", muted: true));
        var master = AddSlider(right, "Master volume", 0, 1, 0.05, audio.Master, Percent);
        var ambience = AddSlider(right, "Ambience", 0, 1, 0.05, audio.Ambience, Percent);
        var effects = AddSlider(right, "Effects", 0, 1, 0.05, audio.Effects, Percent);
        var interfaceSounds = AddSlider(right, "Interface", 0, 1, 0.05, audio.Interface, Percent);
        var music = AddSlider(right, "Music", 0, 1, 0.05, audio.Music, Percent);
        var announcements = AddSlider(right, "Announcements", 0, 1, 0.05, audio.Announcements, v => v <= 0.001 ? "Off" : Percent(v));

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(UiStyle.SecondaryButton("Cancel", Close, 120));
        buttons.AddChild(SaveButton = UiStyle.Button("Save", () =>
        {
            settings.Fullscreen = fullscreen.ButtonPressed;
            settings.VSync = vsync.ButtonPressed;
            settings.EnhancedLighting = lighting.ButtonPressed;
            settings.SurfaceTextures = surfaces.ButtonPressed;
            settings.Outlines = outlines.ButtonPressed;
            settings.UiScale = (float)uiScale.Value;
            settings.CameraPanSpeed = (float)pan.Value;
            settings.EdgeScrolling = edge.ButtonPressed;
            settings.ShowGridByDefault = grid.ButtonPressed;
            settings.AutoStaffNewRooms = AutoStaff.ButtonPressed;
            settings.SeasonalDecorations = Decorations.ButtonPressed;
            GameManager.Instance.SaveSettings();
            audio.Master = (float)master.Value;
            audio.Ambience = (float)ambience.Value;
            audio.Effects = (float)effects.Value;
            audio.Interface = (float)interfaceSounds.Value;
            audio.Music = (float)music.Value;
            audio.Announcements = (float)announcements.Value;
            audio.Save();
            audio.Apply();
            Close();
        }, 120));
        Content.AddChild(buttons);
    }

    /// <summary>The "New rooms come with their minimum staff" tick box.</summary>
    public CheckBox AutoStaff { get; }

    /// <summary>The "Seasonal decorations" tick box.</summary>
    public CheckBox Decorations { get; }

    public Button SaveButton { get; private set; } = null!;

    private static string Percent(double v) => $"{v * 100:0}%";

    private static HSlider AddSlider(Container column, string label, double min, double max, double step, double value, System.Func<double, string> format)
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
        column.AddChild(row);
        return slider;
    }
}
