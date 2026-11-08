"""Generates the ElevenLabs sound effects listed in sounds.json.

    ELEVENLABS_API_KEY=... python3 tools/SoundBaker/generate_sfx.py [name ...]

Each take is saved as tools/SoundBaker/takes/<name>_<n>.mp3 (not committed). Takes that already
exist are skipped, so re-running never spends credits twice. Pick the best take with
choose_take.py, which converts it to OGG in game/Audio/Sounds/.
"""

import json
import os
import sys

import requests

HERE = os.path.dirname(os.path.abspath(__file__))
TAKES = os.path.join(HERE, "takes")
URL = "https://api.elevenlabs.io/v1/sound-generation"


def main():
    key = os.environ["ELEVENLABS_API_KEY"]
    with open(os.path.join(HERE, "sounds.json")) as f:
        sounds = json.load(f)
    wanted = set(sys.argv[1:])
    os.makedirs(TAKES, exist_ok=True)
    spent = 0
    for s in sounds:
        if wanted and s["name"] not in wanted:
            continue
        for take in range(1, s["takes"] + 1):
            path = os.path.join(TAKES, f"{s['name']}_{take}.mp3")
            if os.path.exists(path):
                continue
            body = {
                "text": s["prompt"],
                "duration_seconds": s["seconds"],
                "prompt_influence": s["influence"],
                "loop": s["loop"],
                "model_id": "eleven_text_to_sound_v2",
            }
            r = requests.post(URL, headers={"xi-api-key": key}, json=body, params={"output_format": "mp3_44100_128"}, timeout=180)
            if r.status_code != 200:
                print(f"FAILED {s['name']} take {take}: {r.status_code} {r.text[:300]}")
                return 1
            with open(path, "wb") as out:
                out.write(r.content)
            cost = r.headers.get("character-cost") or r.headers.get("x-character-count") or "?"
            print(f"{s['name']}_{take}.mp3  {len(r.content) // 1024} KB  credits {cost}", flush=True)
            if cost.isdigit():
                spent += int(cost)
    print(f"Credits reported this run: {spent}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
