using Godot;

namespace BlueprintHospital.Core;

/// <summary>Player preferences, saved to user://settings.cfg and applied on startup.</summary>
public sealed class GameSettings
{
    private const string FilePath = "user://settings.cfg";

    public bool Fullscreen { get; set; }

    public bool VSync { get; set; } = true;

    /// <summary>Multiplier for keyboard and edge camera panning.</summary>
    public float CameraPanSpeed { get; set; } = 1.0f;

    public bool EdgeScrolling { get; set; }

    public bool ShowGridByDefault { get; set; } = true;

    /// <summary>Scale applied to the whole interface, for readability on large or small screens.</summary>
    public float UiScale { get; set; } = 1.0f;

    /// <summary>
    /// Deeper contact shading, finer shadows, a fill light and smoothed edges in the 3D view.
    /// On by default; turning it off gives the plain lighting, which is lighter work for slower computers.
    /// </summary>
    public bool EnhancedLighting { get; set; } = true;

    public static GameSettings Load()
    {
        var settings = new GameSettings();
        var config = new ConfigFile();
        if (config.Load(FilePath) != Error.Ok)
        {
            return settings;
        }

        settings.Fullscreen = (bool)config.GetValue("display", "fullscreen", settings.Fullscreen);
        settings.VSync = (bool)config.GetValue("display", "vsync", settings.VSync);
        settings.EnhancedLighting = (bool)config.GetValue("display", "enhanced_lighting", settings.EnhancedLighting);
        settings.UiScale = Mathf.Clamp((float)config.GetValue("display", "ui_scale", settings.UiScale), 0.75f, 1.5f);
        settings.CameraPanSpeed = Mathf.Clamp((float)config.GetValue("camera", "pan_speed", settings.CameraPanSpeed), 0.25f, 3f);
        settings.EdgeScrolling = (bool)config.GetValue("camera", "edge_scrolling", settings.EdgeScrolling);
        settings.ShowGridByDefault = (bool)config.GetValue("gameplay", "show_grid", settings.ShowGridByDefault);
        return settings;
    }

    public Error Save()
    {
        var config = new ConfigFile();
        config.SetValue("display", "fullscreen", Fullscreen);
        config.SetValue("display", "vsync", VSync);
        config.SetValue("display", "enhanced_lighting", EnhancedLighting);
        config.SetValue("display", "ui_scale", UiScale);
        config.SetValue("camera", "pan_speed", CameraPanSpeed);
        config.SetValue("camera", "edge_scrolling", EdgeScrolling);
        config.SetValue("gameplay", "show_grid", ShowGridByDefault);
        return config.Save(FilePath);
    }

    /// <summary>Applies display settings to the running window.</summary>
    public void Apply(SceneTree tree)
    {
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        }

        tree.Root.ContentScaleFactor = UiScale;
    }
}
