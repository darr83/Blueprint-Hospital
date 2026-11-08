using System.Collections.Generic;
using Godot;

namespace BlueprintHospital.Audio;

/// <summary>
/// Finds sounds by name. Recorded sounds live in res://Audio/Sounds (OGG) and code-made tones in
/// res://Audio/Tones (WAV). A missing sound is simply silent, so the game never breaks over audio.
/// </summary>
public static class SoundLibrary
{
    private static readonly string[] Folders = { "res://Audio/Sounds/", "res://Audio/Tones/" };
    private static readonly string[] Extensions = { ".ogg", ".wav", ".mp3" };
    private static readonly Dictionary<(string, bool), AudioStream?> Cache = new();

    /// <summary>The sound called <paramref name="name"/>, set to loop if asked, or null if there isn't one.</summary>
    public static AudioStream? Get(string name, bool loop = false)
    {
        if (Cache.TryGetValue((name, loop), out var cached))
        {
            return cached;
        }

        AudioStream? stream = null;
        foreach (var folder in Folders)
        {
            foreach (var extension in Extensions)
            {
                var path = folder + name + extension;
                if (ResourceLoader.Exists(path))
                {
                    stream = ResourceLoader.Load<AudioStream>(path);
                    break;
                }
            }

            if (stream is not null)
            {
                break;
            }
        }

        if (stream is not null)
        {
            // Copies, so the looping and one-shot versions don't change each other.
            stream = (AudioStream)stream.Duplicate();
            SetLoop(stream, loop);
        }

        Cache[(name, loop)] = stream;
        return stream;
    }

    /// <summary>Whether a sound with this name exists.</summary>
    public static bool Has(string name) => Get(name) is not null;

    private static void SetLoop(AudioStream stream, bool loop)
    {
        switch (stream)
        {
            case AudioStreamOggVorbis ogg:
                ogg.Loop = loop;
                break;
            case AudioStreamMP3 mp3:
                mp3.Loop = loop;
                break;
            case AudioStreamWav wav:
                wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                wav.LoopBegin = 0;
                wav.LoopEnd = loop ? (int)(wav.GetLength() * wav.MixRate) : 0;
                break;
        }
    }
}
