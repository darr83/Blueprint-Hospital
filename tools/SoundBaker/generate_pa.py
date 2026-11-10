"""Makes the tannoy announcements with ElevenLabs text to speech.

    python3 tools/SoundBaker/generate_pa.py --list              # write pa_lines.json and count characters (free)
    ELEVENLABS_API_KEY=... python3 tools/SoundBaker/generate_pa.py [--voices f,m] [key ...]

Every line is recorded once per announcer voice (f = female, m = male) into takes/pa/<key>_<voice>.mp3
(not committed), then converted to game/Audio/Sounds/pa/<key>_<voice>.ogg with silence trimmed and
loudness evened out. The tannoy speaker sound is added in the game (Announcements bus), not baked in.
Existing takes are skipped, so re-running never spends credits twice. Needs ffmpeg.

The keys match the clip names the simulation sends (HospitalSimulation.Announcements.cs):
call_<doctor|registrar|consultant>_resus_<1-6|x>, arrest_<resus_N|majors|ae|ward template id|room>, paed_arrest, obstetric_emergency,
trauma_<resus_N|resus_x>, amb_due_<5|10>, assess_<condition id>, gen_<name>.
"""

import glob
import json
import os
import subprocess
import sys

import requests

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
TAKES = os.path.join(HERE, "takes", "pa")
OUT = os.path.join(ROOT, "game", "Audio", "Sounds", "pa")
CONDITIONS = os.path.join(ROOT, "game", "Data", "Conditions")
LINES_FILE = os.path.join(HERE, "pa_lines.json")

# ElevenLabs premade British voices: calm, clear announcers.
VOICES = {
    "f": "Xb7hH8MSUJpSbSDYk0k2",  # Alice: clear, engaging British female
    "m": "onwK4e9ZLuTAKqWW03F9",  # Daniel: steady British male newsreader
}
MODEL = "eleven_multilingual_v2"
BAYS = 6

GENERAL = {
    "gen_fire_test": "This is a test of the fire alarm system. No action is required.",
    "gen_visiting_over": "Visiting time is now over. Thank you for your cooperation.",
    "gen_visiting_open": "Afternoon visiting has now started. Please use the hand gel as you enter the wards.",
    "gen_main_entrance": "The main entrance will close at eight p.m. Please use the A and E entrance after this time.",
    "gen_morning": "Good morning. The hospital shop and café are now open on the ground floor.",
    "gen_quiet": "Please keep noise to a minimum. Patients on the wards are sleeping.",
    "gen_full_capacity": "A and E is now at full capacity.",
    "gen_porters": "Would all available porters please report to A and E.",
    "gen_no_smoking": "This is a no smoking site, including the car park and entrances.",
    "gen_car_park": "Would the owner of the blue estate car parked in the ambulance bay please move it immediately.",
    "gen_lost_child": "Would the parent of a little boy called Oliver please come to the A and E reception desk.",
    "gen_hand_hygiene": "Please remember to clean your hands when entering and leaving clinical areas.",
}


def build_lines():
    lines = {}
    for who in ("Doctor", "Registrar", "Consultant"):
        for bay in [*range(1, BAYS + 1), None]:
            place = f"Resus {bay}" if bay else "Resus"
            lines[f"call_{who.lower()}_resus_{bay or 'x'}"] = f"{who} is required at {place}."
    for bay in [*range(1, BAYS + 1), None]:
        place, key = (f"Resus {bay}", f"resus_{bay}") if bay else ("Resus", "resus_x")
        lines[f"arrest_{key}"] = f"Adult cardiac arrest, {place}. Adult cardiac arrest, {place}."
        lines[f"trauma_{key}"] = f"Trauma team to {place}."
    lines["arrest_majors"] = "Adult cardiac arrest, Majors. Adult cardiac arrest, Majors."
    lines["arrest_ae"] = "Adult cardiac arrest, A and E. Adult cardiac arrest, A and E."
    # Crash calls anywhere in the hospital: each kind of ward by name, a few other rooms, children and maternity.
    with open(os.path.join(ROOT, "game", "Data", "RoomTemplates", "ward_rooms.json")) as f:
        for ward in json.load(f):
            spoken = ward["name"].replace("A&E", "A and E")
            lines[f"arrest_{ward['id']}"] = f"Adult cardiac arrest, {spoken}. Adult cardiac arrest, {spoken}."
    for key, place in [("theatre", "Theatres"), ("xray", "X-ray"), ("ct", "C T"), ("cathlab", "the cath lab"), ("endoscopy", "Endoscopy"), ("physiogym", "the physio gym"),
                       ("endoscopyrecovery", "Endoscopy recovery"), ("plasterroom", "the plaster room"), ("clinic", "Outpatients"),
                       ("echo", "Echo"), ("eeg", "E E G"), ("ultrasound", "Ultrasound"), ("waiting", "the A and E waiting room"),
                       ("triage", "Triage"), ("minors", "Minors"), ("toilets", "the public toilets"), ("accessibletoilet", "the public toilets"),
                       ("cafeteria", "the café"), ("pharmacy", "Pharmacy"), ("outpatientreception", "Outpatients reception")]:
        lines[f"arrest_{key}"] = f"Adult cardiac arrest, {place}. Adult cardiac arrest, {place}."
    lines["arrest_x"] = "Adult cardiac arrest. Adult cardiac arrest."
    lines["paed_arrest"] = "Paediatric cardiac arrest. Paediatric cardiac arrest."
    lines["obstetric_emergency"] = "Obstetric emergency, Maternity. Obstetric emergency, Maternity."
    lines["amb_due_5"] = "Ambulance due in five minutes."
    lines["amb_due_10"] = "Ambulance due in ten minutes."
    for path in sorted(glob.glob(os.path.join(CONDITIONS, "*.json"))):
        with open(path) as f:
            data = json.load(f)
        for c in data if isinstance(data, list) else next(iter(data.values())):
            lines[f"assess_{c['id']}"] = f"Assessment: {c['crew_assessment']}."
    lines.update(GENERAL)
    return lines


def convert(src, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    trim = "silenceremove=start_periods=1:start_threshold=-50dB,areverse,silenceremove=start_periods=1:start_threshold=-50dB,areverse"
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", src, "-af", f"{trim},loudnorm=I=-18:TP=-1.5:LRA=11",
                    "-ar", "44100", "-ac", "1", "-c:a", "libvorbis", "-q:a", "5", dst], check=True)


def main():
    lines = build_lines()
    with open(LINES_FILE, "w") as f:
        json.dump(lines, f, indent=1, ensure_ascii=False)
    chars = sum(len(t) for t in lines.values())
    if "--list" in sys.argv:
        print(f"{len(lines)} lines, {chars} characters per voice")
        return 0

    voices = list(VOICES)
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--voices" in sys.argv:
        voices = sys.argv[sys.argv.index("--voices") + 1].split(",")
        args = [a for a in args if a != sys.argv[sys.argv.index("--voices") + 1]]
    wanted = set(args)
    key = os.environ["ELEVENLABS_API_KEY"]
    os.makedirs(TAKES, exist_ok=True)
    spent = 0
    for name, text in lines.items():
        if wanted and name not in wanted:
            continue
        for v in voices:
            take = os.path.join(TAKES, f"{name}_{v}.mp3")
            if not os.path.exists(take):
                r = requests.post(
                    f"https://api.elevenlabs.io/v1/text-to-speech/{VOICES[v]}",
                    headers={"xi-api-key": key},
                    params={"output_format": "mp3_44100_128"},
                    json={"text": text, "model_id": MODEL,
                          "voice_settings": {"stability": 0.75, "similarity_boost": 0.75, "style": 0.0, "speed": 0.95}},
                    timeout=120)
                if r.status_code != 200:
                    print(f"FAILED {name}_{v}: {r.status_code} {r.text[:300]}")
                    return 1
                with open(take, "wb") as out:
                    out.write(r.content)
                cost = r.headers.get("character-cost") or r.headers.get("x-character-count") or "?"
                if cost.isdigit():
                    spent += int(cost)
                print(f"{name}_{v}  credits {cost}", flush=True)
            convert(take, os.path.join(OUT, f"{name}_{v}.ogg"))
    print(f"Credits reported this run: {spent}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
