using System;
using System.Collections.Generic;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.Audio;

/// <summary>
/// The hospital tannoy. Announcements from the simulation queue up and play one at a time:
/// the ding-dong chime, then each recorded line. The Announcements bus band-limits and adds a
/// little room echo so the voice sounds like it comes from ceiling speakers. Lines that are
/// missing are skipped, and stale announcements are dropped rather than played late.
/// </summary>
public partial class Announcer : Node
{
    /// <summary>Real seconds an announcement may wait in the queue before it's no longer worth saying.</summary>
    private const double StaleAfter = 12;

    private const double GapBetweenLines = 0.25;

    private readonly Queue<(Announcement Item, double QueuedAt)> _queue = new();
    private AudioStreamPlayer _player = null!;
    private AudioStreamPlayer _hiss = null!;
    private readonly Queue<string> _lines = new();
    private double _time;
    private double _resumeAt;
    private bool _busy;

    /// <summary>Prints each line as it plays, when the game is run with -- --log-sounds.</summary>
    private static readonly bool Log = Array.IndexOf(OS.GetCmdlineUserArgs(), "--log-sounds") >= 0;

    public override void _Ready()
    {
        _player = new AudioStreamPlayer { Bus = AudioSettings.AnnouncementsBus };
        _player.Finished += () => _resumeAt = _time + GapBetweenLines;
        AddChild(_player);
        _hiss = new AudioStreamPlayer { Bus = AudioSettings.AnnouncementsBus, VolumeDb = -6 };
        AddChild(_hiss);
    }

    public void Enqueue(Announcement announcement)
    {
        if (AudioSettings.Current.Announcements <= 0.001f)
        {
            return;
        }

        // Emergencies jump the queue; atmosphere is dropped if anything is already waiting.
        if (announcement.Kind == AnnouncementKind.General && (_busy || _queue.Count > 0))
        {
            return;
        }

        if (announcement.Kind is AnnouncementKind.CardiacArrest or AnnouncementKind.TraumaCall && _queue.Count > 0)
        {
            var rest = _queue.ToArray();
            _queue.Clear();
            _queue.Enqueue((announcement, _time));
            foreach (var item in rest)
            {
                _queue.Enqueue(item);
            }

            return;
        }

        _queue.Enqueue((announcement, _time));
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_player.Playing || _time < _resumeAt)
        {
            return;
        }

        if (_lines.Count > 0)
        {
            PlayLine(_lines.Dequeue());
            return;
        }

        _busy = false;
        while (_queue.Count > 0)
        {
            var (item, queuedAt) = _queue.Dequeue();
            if (_time - queuedAt > StaleAfter)
            {
                continue;
            }

            _busy = true;
            _lines.Enqueue("pa_chime");
            if (SoundLibrary.Get("tannoy_open") is { } hiss)
            {
                _hiss.Stream = hiss;
                _hiss.Play();
            }

            foreach (var clip in item.Clips)
            {
                _lines.Enqueue(clip);
            }

            if (Log)
            {
                GD.Print($"[tannoy] {item.Text}");
            }

            PlayLine(_lines.Dequeue());
            return;
        }
    }

    private void PlayLine(string name)
    {
        // Two announcer voices: the same line is recorded as name_f and name_m; one is picked per line.
        var stream = SoundLibrary.Get(VoiceFor(name)) ?? SoundLibrary.Get(name);
        if (stream is null)
        {
            _resumeAt = _time;
            return;
        }

        if (Log)
        {
            GD.Print($"[sound] {name}");
        }

        _player.Stream = stream;
        _player.Play();
    }

    private bool _maleVoice;

    private string VoiceFor(string name)
    {
        if (name == "pa_chime")
        {
            // Each announcement alternates voices; the chime marks the start of a new one.
            _maleVoice = !_maleVoice;
            return name;
        }

        return "pa/" + name + (_maleVoice ? "_m" : "_f");
    }

    /// <summary>Adds the tannoy sound to the Announcements bus: telephone-band filter and a short echo.</summary>
    public static void SetUpTannoyBus()
    {
        var index = AudioServer.GetBusIndex(AudioSettings.AnnouncementsBus);
        if (index < 0 || AudioServer.GetBusEffectCount(index) > 0)
        {
            return;
        }

        AudioServer.AddBusEffect(index, new AudioEffectHighPassFilter { CutoffHz = 350 });
        AudioServer.AddBusEffect(index, new AudioEffectLowPassFilter { CutoffHz = 4200 });
        AudioServer.AddBusEffect(index, new AudioEffectDistortion { Mode = AudioEffectDistortion.ModeEnum.Overdrive, Drive = 0.12f, PostGain = -2 });
        AudioServer.AddBusEffect(index, new AudioEffectReverb { RoomSize = 0.55f, Damping = 0.6f, Wet = 0.18f, Dry = 0.9f, Spread = 0.4f });
    }
}
