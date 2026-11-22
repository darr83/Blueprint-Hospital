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

    /// <summary>
    /// Each newly built room comes with the minimum staff it needs (hired on the lowest grade and paid
    /// as normal). Off by default: rooms come empty and the player hires everyone.
    /// </summary>
    public bool AutoStaffNewRooms { get; set; }

    /// <summary>Christmas trees, Halloween pumpkins and Easter bunting go up by themselves at the right time of year (games with seasons).</summary>
    public bool SeasonalDecorations { get; set; } = true;

    /// <summary>Scale applied to the whole interface, for readability on large or small screens.</summary>
    public float UiScale { get; set; } = 1.0f;

    /// <summary>
    /// In 3D: deeper contact shading, finer shadows, a fill light and smoothed edges. In 2D: shadows
    /// and evening light. On by default; turning it off is lighter work for slower computers.
    /// </summary>
    public bool EnhancedLighting { get; set; } = true;

    /// <summary>Walls, floors and furniture drawn with surface detail (paint, vinyl, wood grain, fabric...).</summary>
    public bool SurfaceTextures { get; set; } = true;

    /// <summary>Thin dark outlines round everything in the 3D view, for a crisp, clean look.</summary>
    public bool Outlines { get; set; } = true;

    /// <summary>
    /// How the hospital is drawn: false for the 2D view from directly above, true for the 3D view.
    /// Chosen on the main menu; a game keeps the view it was started or loaded with.
    /// </summary>
    public bool View3D { get; set; }

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
        settings.SurfaceTextures = (bool)config.GetValue("display", "surface_textures", settings.SurfaceTextures);
        settings.Outlines = (bool)config.GetValue("display", "outlines", settings.Outlines);
        settings.View3D = (bool)config.GetValue("display", "view_3d", settings.View3D);
        settings.UiScale = Mathf.Clamp((float)config.GetValue("display", "ui_scale", settings.UiScale), 0.75f, 1.5f);
        settings.CameraPanSpeed = Mathf.Clamp((float)config.GetValue("camera", "pan_speed", settings.CameraPanSpeed), 0.25f, 3f);
        settings.EdgeScrolling = (bool)config.GetValue("camera", "edge_scrolling", settings.EdgeScrolling);
        settings.ShowGridByDefault = (bool)config.GetValue("gameplay", "show_grid", settings.ShowGridByDefault);
        settings.AutoStaffNewRooms = (bool)config.GetValue("gameplay", "auto_staff_new_rooms", settings.AutoStaffNewRooms);
        settings.SeasonalDecorations = (bool)config.GetValue("gameplay", "seasonal_decorations", settings.SeasonalDecorations);
        return settings;
    }

    public Error Save()
    {
        var config = new ConfigFile();
        config.SetValue("display", "fullscreen", Fullscreen);
        config.SetValue("display", "vsync", VSync);
        config.SetValue("display", "enhanced_lighting", EnhancedLighting);
        config.SetValue("display", "surface_textures", SurfaceTextures);
        config.SetValue("display", "outlines", Outlines);
        config.SetValue("display", "view_3d", View3D);
        config.SetValue("display", "ui_scale", UiScale);
        config.SetValue("camera", "pan_speed", CameraPanSpeed);
        config.SetValue("camera", "edge_scrolling", EdgeScrolling);
        config.SetValue("gameplay", "show_grid", ShowGridByDefault);
        config.SetValue("gameplay", "auto_staff_new_rooms", AutoStaffNewRooms);
        config.SetValue("gameplay", "seasonal_decorations", SeasonalDecorations);
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
