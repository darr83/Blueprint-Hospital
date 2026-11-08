"""Converts the chosen ElevenLabs take of each sound into the game.

    python3 tools/SoundBaker/choose_take.py              # every sound in picks.json
    python3 tools/SoundBaker/choose_take.py siren=2      # change one pick, then convert it

Reads tools/SoundBaker/takes/<name>_<take>.mp3, evens out the loudness, trims silence from the
start of one-shots and writes game/Audio/Sounds/<name>.ogg (OGG loops without gaps in Godot).
Needs ffmpeg.
"""

import json
import os
import subprocess
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "game", "Audio", "Sounds")

# Ambience beds sit quietly under everything; voices and machines a little louder.
LOUDNESS = {"amb_": -26, "chatter_": -22, "xray_hum": -24, "ct_scanner": -22, "footsteps": -24, "wheelchair": -24, "trolley": -24,
            "ventilator": -26, "oxygen_hiss": -30, "snore": -26, "echo_room": -26, "cath_lab": -24, "eeg_room": -26,
            "endoscopy": -26, "physio_gym": -24}
DEFAULT_LOUDNESS = -18


def target(name):
    for prefix, lufs in LOUDNESS.items():
        if name.startswith(prefix):
            return lufs
    return DEFAULT_LOUDNESS


def convert(name, take, loop):
    src = os.path.join(HERE, "takes", f"{name}_{take}.mp3")
    dst = os.path.join(OUT, f"{name}.ogg")
    filters = []
    if not loop:
        filters.append("silenceremove=start_periods=1:start_threshold=-50dB")
    filters.append(f"loudnorm=I={target(name)}:TP=-1.5:LRA=11")
    filters.append("aresample=44100")
    channels = 2 if name.startswith("amb_") else 1
    raw = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", src, "-af", ",".join(filters), "-ac", str(channels), "-f", "f32le", "-"],
        check=True, capture_output=True,
    ).stdout
    audio = np.frombuffer(raw, dtype="<f4").reshape(-1, channels).copy()
    if loop:
        audio = crossfade_loop(audio)
    subprocess.run(
        ["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", "44100", "-ac", str(channels), "-i", "-",
         "-c:a", "libvorbis", "-q:a", "5", dst],
        input=audio.astype("<f4").tobytes(), check=True,
    )
    print(f"{name}.ogg  <- take {take}")


def crossfade_loop(audio, seconds=0.3):
    """Blends the end into the start so the loop has no click or jump where it wraps round."""
    n = int(44100 * seconds)
    fade_in = np.linspace(0, 1, n)[:, None]
    head = audio[:n] * fade_in + audio[-n:] * (1 - fade_in)
    return np.concatenate([head, audio[n:-n]])


def main():
    with open(os.path.join(HERE, "sounds.json")) as f:
        loops = {s["name"]: s["loop"] for s in json.load(f)}
    picks_path = os.path.join(HERE, "picks.json")
    with open(picks_path) as f:
        picks = json.load(f)

    only = []
    for arg in sys.argv[1:]:
        name, _, take = arg.partition("=")
        if take:
            picks[name] = int(take)
        only.append(name)
    with open(picks_path, "w") as f:
        json.dump(picks, f, indent=2)
        f.write("\n")

    os.makedirs(OUT, exist_ok=True)
    for name, take in picks.items():
        if not only or name in only:
            convert(name, take, loops.get(name, False))


if __name__ == "__main__":
    main()
