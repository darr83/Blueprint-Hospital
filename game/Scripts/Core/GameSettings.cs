using Godot;

namespace BlueprintHospital.Core;

/// <summary>Player preferences, saved to user://settings.cfg and applied on startup.</summary>
public sealed class GameSettings
{
    private const string FilePath = "user://settings.cfg";

    /// <summary>Fullscreen uses the screen's own resolution; windowed uses <see cref="Resolution"/>.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>
    /// The window size in windowed mode, one of <see cref="Resolutions"/>. Null until the player picks one,
    /// so the window keeps the size it opened at.
    /// </summary>
    public Vector2I? Resolution { get; set; }

    /// <summary>The window sizes on offer, from 720p up to 1440p (2K), in 16:9 and 16:10.</summary>
    public static readonly Vector2I[] Resolutions =
    {
        new(1280, 720), new(1280, 800), new(1366, 768), new(1440, 900), new(1600, 900),
        new(1680, 1050), new(1920, 1080), new(1920, 1200), new(2560, 1440),
    };

    /// <summary>"1920 × 1080 (1080p)" style name for a resolution.</summary>
    public static string ResolutionName(Vector2I size)
    {
        var tag = size switch
        {
            { X: 1280, Y: 720 } => " (720p)",
            { X: 1920, Y: 1080 } => " (1080p)",
            { X: 2560, Y: 1440 } => " (1440p, 2K)",
            _ => "",
        };
        return $"{size.X} × {size.Y}{tag}";
    }

    /// <summary>Reads "1920x1080"; null for anything that is not one of <see cref="Resolutions"/>.</summary>
    public static Vector2I? ParseResolution(string text)
    {
        var parts = text.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
        {
            var size = new Vector2I(w, h);
            if (System.Array.IndexOf(Resolutions, size) >= 0)
            {
                return size;
            }
        }

        return null;
    }

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

    /// <summary>
    /// Thin dark outlines round everything in the 3D view. Off by default: the one-pixel lines shimmer
    /// as the camera scrolls. (Saved under a new key, so games saved with them on start without them.)
    /// </summary>
    public bool Outlines { get; set; }

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
        settings.Resolution = ParseResolution((string)config.GetValue("display", "resolution", ""));
        settings.VSync = (bool)config.GetValue("display", "vsync", settings.VSync);
        settings.EnhancedLighting = (bool)config.GetValue("display", "enhanced_lighting", settings.EnhancedLighting);
        settings.SurfaceTextures = (bool)config.GetValue("display", "surface_textures", settings.SurfaceTextures);
        settings.Outlines = (bool)config.GetValue("display", "outlines_v2", settings.Outlines);
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
        config.SetValue("display", "resolution", Resolution is { } size ? $"{size.X}x{size.Y}" : "");
        config.SetValue("display", "vsync", VSync);
        config.SetValue("display", "enhanced_lighting", EnhancedLighting);
        config.SetValue("display", "surface_textures", SurfaceTextures);
        config.SetValue("display", "outlines_v2", Outlines);
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
            if (Fullscreen)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            }
            else
            {
                if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
                {
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                }

                if (Resolution is { } size)
                {
                    // Deferred, so a window just leaving fullscreen has settled before it is resized.
                    Callable.From(() => SetWindowSize(size)).CallDeferred();
                }
            }

            DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        }

        tree.Root.ContentScaleFactor = UiScale;
    }

    /// <summary>The space a window can use on the screen it is on (the screen less the taskbar).</summary>
    public static Vector2I UsableScreenSize()
    {
        return DisplayServer.GetName() == "headless"
            ? new Vector2I(int.MaxValue, int.MaxValue)
            : DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen()).Size;
    }

    /// <summary>True when a window of this size fits on the screen without spilling off it.</summary>
    public static bool FitsScreen(Vector2I size)
    {
        var screen = UsableScreenSize();
        return size.X <= screen.X && size.Y <= screen.Y;
    }

    /// <summary>
    /// Resizes the window and centres it on its screen. The whole interface is laid out for 1600 × 900
    /// and scaled to the window (project stretch mode), so every panel keeps the same layout at any size.
    /// </summary>
    private static void SetWindowSize(Vector2I size)
    {
        var screenIndex = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screenIndex);
        size = new Vector2I(Mathf.Min(size.X, usable.Size.X), Mathf.Min(size.Y, usable.Size.Y));
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
    }
}
