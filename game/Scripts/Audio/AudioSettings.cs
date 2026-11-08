using Godot;

namespace BlueprintHospital.Audio;

/// <summary>
/// Volume sliders, saved to user://audio.cfg. Each group has its own audio bus, created when the
/// game starts, so the sliders work without any bus layout file.
/// </summary>
public sealed class AudioSettings
{
    private const string FilePath = "user://audio.cfg";

    public const string AmbienceBus = "Ambience";
    public const string EffectsBus = "Effects";
    public const string InterfaceBus = "Interface";
    public const string MusicBus = "Music";
    public const string AnnouncementsBus = "Announcements";

    private static AudioSettings? _current;

    /// <summary>The settings in use, loaded on first access.</summary>
    public static AudioSettings Current => _current ??= Load();

    public float Master { get; set; } = 0.8f;

    public float Ambience { get; set; } = 0.8f;

    public float Effects { get; set; } = 0.9f;

    public float Interface { get; set; } = 0.7f;

    public float Music { get; set; } = 0.6f;

    /// <summary>Tannoy announcements; 0 turns them off.</summary>
    public float Announcements { get; set; } = 0.8f;

    public static AudioSettings Load()
    {
        var settings = new AudioSettings();
        var config = new ConfigFile();
        if (config.Load(FilePath) == Error.Ok)
        {
            settings.Master = Read(config, "master", settings.Master);
            settings.Ambience = Read(config, "ambience", settings.Ambience);
            settings.Effects = Read(config, "effects", settings.Effects);
            settings.Interface = Read(config, "interface", settings.Interface);
            settings.Music = Read(config, "music", settings.Music);
            settings.Announcements = Read(config, "announcements", settings.Announcements);
        }

        return settings;
    }

    public void Save()
    {
        var config = new ConfigFile();
        config.SetValue("volume", "master", Master);
        config.SetValue("volume", "ambience", Ambience);
        config.SetValue("volume", "effects", Effects);
        config.SetValue("volume", "interface", Interface);
        config.SetValue("volume", "music", Music);
        config.SetValue("volume", "announcements", Announcements);
        config.Save(FilePath);
    }

    /// <summary>Makes sure the buses exist and sets their volumes.</summary>
    public void Apply()
    {
        SetBus("Master", Master);
        SetBus(AmbienceBus, Ambience);
        SetBus(EffectsBus, Effects);
        SetBus(InterfaceBus, Interface);
        SetBus(MusicBus, Music);
        SetBus(AnnouncementsBus, Announcements);
        Announcer.SetUpTannoyBus();
    }

    private static void SetBus(string name, float volume)
    {
        var index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }

        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(volume, 0.0001f)));
        AudioServer.SetBusMute(index, volume <= 0.001f);
    }

    private static float Read(ConfigFile config, string key, float fallback) =>
        Mathf.Clamp((float)config.GetValue("volume", key, fallback), 0f, 1f);
}
