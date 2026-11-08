# Sound baker

How the game's sounds are made.

- `make_tones.py` makes the monitor beeps, alarms, chimes and interface sounds with code (no credits).
  Output: `game/Audio/Tones/*.wav`.
- `sounds.json` lists every ElevenLabs sound: prompt, length, whether it loops, and how many takes to make.
- `generate_sfx.py` makes the takes into `takes/` (not committed). Existing takes are skipped, so
  re-running never spends credits twice. Needs `ELEVENLABS_API_KEY` set in the environment.
- `choose_take.py` converts the chosen take of each sound (`picks.json`) into `game/Audio/Sounds/*.ogg`.
  It evens out loudness, trims silence from one-shots and crossfades loop ends so they wrap without a click.
  `python3 tools/SoundBaker/choose_take.py siren=2` swaps one sound to another take.

In the game, `game/Scripts/Audio/HospitalAudio.cs` plays everything, and a missing file is just silent.
Run with `-- --log-sounds` to print each sound as it plays.
