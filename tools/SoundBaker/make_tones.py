"""Makes the game's code-generated sounds: monitor beeps, alarms, chimes and interface sounds.

These are simple tones (real bedside monitors use simple tones too), so they're made here
for free instead of spending ElevenLabs credits. Run from the repository root:

    python3 tools/SoundBaker/make_tones.py

Writes 44.1 kHz mono 16-bit WAV files to game/Audio/Tones/.
"""

import os
import wave

import numpy as np

RATE = 44100
OUT = os.path.join("game", "Audio", "Tones")
rng = np.random.default_rng(7)


def silence(seconds):
    return np.zeros(int(RATE * seconds))


def tone(freq, seconds, harmonics=(1.0,), attack=0.005, release=0.02, decay=None):
    """A tone with optional harmonics and either a flat body or an exponential (bell) decay."""
    t = np.arange(int(RATE * seconds)) / RATE
    wave_ = sum(a * np.sin(2 * np.pi * freq * (i + 1) * t) for i, a in enumerate(harmonics))
    wave_ /= max(1e-9, sum(abs(a) for a in harmonics))
    env = np.ones_like(t)
    a = int(RATE * attack)
    r = int(RATE * release)
    if a:
        env[:a] = np.linspace(0, 1, a)
    if decay:
        env *= np.exp(-t / decay)
    if r:
        env[-r:] *= np.linspace(1, 0, r)
    return wave_ * env


def bell(freq, seconds, decay=0.35):
    # Inharmonic partials give a soft, chime-like bell.
    t = np.arange(int(RATE * seconds)) / RATE
    partials = [(1.0, 1.0), (2.0, 0.35), (2.76, 0.18), (5.4, 0.06)]
    out = sum(amp * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-t / (decay / ratio ** 0.5)) for ratio, amp in partials)
    out[: int(RATE * 0.003)] *= np.linspace(0, 1, int(RATE * 0.003))
    return out / 1.6


def noise(seconds, lowpass=None, highpass=None):
    n = rng.standard_normal(int(RATE * seconds))
    if lowpass:
        n = one_pole(n, lowpass)
    if highpass:
        n = n - one_pole(n, highpass)
    return n / (np.max(np.abs(n)) + 1e-9)


def one_pole(x, cutoff):
    alpha = 1 - np.exp(-2 * np.pi * cutoff / RATE)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc += alpha * (v - acc)
        y[i] = acc
    return y


def sweep(f0, f1, seconds):
    t = np.arange(int(RATE * seconds)) / RATE
    freq = np.linspace(f0, f1, len(t))
    return np.sin(2 * np.pi * np.cumsum(freq) / RATE)


def fade(x, fin=0.005, fout=0.02):
    x = x.copy()
    a, r = int(RATE * fin), int(RATE * fout)
    if a:
        x[:a] *= np.linspace(0, 1, a)
    if r:
        x[-r:] *= np.linspace(1, 0, r)
    return x


def seq(*parts):
    return np.concatenate(parts)


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


def at(x, offset_seconds, total=None):
    start = int(RATE * offset_seconds)
    out = np.zeros(max(start + len(x), int(RATE * total) if total else 0))
    out[start : start + len(x)] = x
    return out


def save(name, x, peak=0.8):
    os.makedirs(OUT, exist_ok=True)
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    data = (x * 32767).astype("<i2").tobytes()
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(data)
    print(f"{name}.wav  {len(x) / RATE:.2f}s")


ALARM = (1.0, 0.5, 0.35, 0.2, 0.12)  # harmonic-rich so alarms cut through the ambience


def main():
    # --- Monitors -----------------------------------------------------------
    # One soft pulse-oximeter pip. The game plays it at the patient's heart rate.
    save("monitor_beep", tone(940, 0.11, harmonics=(1.0, 0.08), attack=0.003, release=0.05), peak=0.55)

    # Medium priority (amber): three rising pulses, like the hospital alarm standard.
    amber = seq(*(seq(tone(f, 0.2, ALARM, 0.02, 0.04), silence(0.12)) for f in (523, 659, 784)))
    save("alarm_amber", seq(amber, silence(0.3)), peak=0.6)

    # High priority (flashing red): 3 + 2 fast pulses, higher and more urgent.
    def red_pulse(f):
        return seq(tone(f, 0.13, ALARM, 0.01, 0.025), silence(0.07))

    burst = seq(red_pulse(880), red_pulse(1109), red_pulse(1319), silence(0.25), red_pulse(1109), red_pulse(1319))
    save("alarm_red", seq(burst, silence(0.6), burst, silence(0.4)), peak=0.75)

    # Cardiac arrest: the flat continuous tone. Loops seamlessly (whole number of cycles).
    save("flatline", tone(1000, 2.0, harmonics=(1.0, 0.15), attack=0, release=0), peak=0.5)

    # Crash call: overhead rising two-tone, three times.
    crash = seq(*(seq(tone(700, 0.25, ALARM, 0.01, 0.03), tone(1050, 0.25, ALARM, 0.01, 0.05), silence(0.15)) for _ in range(3)))
    save("crash_call", crash, peak=0.7)

    # --- Building sounds ----------------------------------------------------
    save("pa_chime", mix(bell(659, 1.6, 0.6), at(bell(523, 1.8, 0.7), 0.45)), peak=0.6)

    # UK telephone double ring (400 + 450 Hz), one cycle with the pause, so it loops.
    def ring(seconds):
        return fade(mix(tone(400, seconds, attack=0.01, release=0.01), tone(450, seconds, attack=0.01, release=0.01)), 0.01, 0.01)

    save("phone_ring", seq(ring(0.4), silence(0.2), ring(0.4), silence(2.0)), peak=0.45)

    # --- Interface ----------------------------------------------------------
    click = mix(tone(2200, 0.025, attack=0.001, release=0.02) * 0.6, noise(0.02, lowpass=6000) * np.linspace(1, 0, int(RATE * 0.02)) * 0.4)
    save("ui_click", fade(seq(click, silence(0.02)), 0.001, 0.01), peak=0.5)

    whoosh = noise(0.22, lowpass=3500, highpass=400) * np.sin(np.linspace(0, np.pi, int(RATE * 0.22)))
    save("ui_panel_open", mix(whoosh * 0.5, fade(sweep(500, 900, 0.18) * 0.25, 0.01, 0.1)), peak=0.4)

    thud = fade(tone(110, 0.25, (1.0, 0.3), attack=0.002, decay=0.06), 0.002, 0.05)
    save("room_built", mix(thud, at(bell(784, 1.0, 0.35) * 0.6, 0.08), at(bell(1047, 1.2, 0.4) * 0.6, 0.22)), peak=0.6)

    knock = noise(0.08, lowpass=1200) * np.exp(-np.arange(int(RATE * 0.08)) / RATE / 0.015)
    save("furniture_placed", mix(fade(tone(180, 0.2, (1.0, 0.4), attack=0.001, decay=0.04), 0.001, 0.05), knock * 0.5), peak=0.55)

    def buzz(seconds):
        t = np.arange(int(RATE * seconds)) / RATE
        sq = np.sign(np.sin(2 * np.pi * 140 * t)) * 0.5 + np.sin(2 * np.pi * 140 * t) * 0.5
        return fade(one_pole(sq, 1500), 0.005, 0.03)

    save("ui_invalid", seq(buzz(0.12), silence(0.06), buzz(0.12)), peak=0.4)

    crumble = noise(0.7, lowpass=900) * np.exp(-np.arange(int(RATE * 0.7)) / RATE / 0.25)
    save("demolish", mix(crumble, fade(tone(70, 0.5, (1.0, 0.5), attack=0.002, decay=0.15), 0.002, 0.1)), peak=0.6)

    save("money_in", mix(bell(1568, 0.8, 0.25), at(bell(2093, 0.9, 0.3), 0.07)), peak=0.5)
    save("money_low", mix(bell(659, 1.2, 0.5), at(bell(523, 1.2, 0.5), 0.25), at(bell(415, 1.4, 0.6), 0.5)), peak=0.55)
    save("staff_hired", mix(bell(523, 1.2, 0.4), at(bell(659, 1.2, 0.4), 0.12), at(bell(784, 1.4, 0.5), 0.24)), peak=0.55)
    save("staff_let_go", mix(bell(587, 1.1, 0.45), at(bell(440, 1.3, 0.5), 0.18)), peak=0.5)
    save("notify", bell(1175, 0.9, 0.3), peak=0.45)
    save("notify_urgent", mix(bell(1319, 0.7, 0.2), at(bell(1319, 0.7, 0.2), 0.16), at(bell(1760, 0.9, 0.3), 0.32)), peak=0.6)
    save("new_day", mix(bell(784, 1.5, 0.5), at(bell(988, 1.5, 0.5), 0.2), at(bell(1175, 1.8, 0.6), 0.4)), peak=0.45)
    save("discharged", mix(bell(880, 1.0, 0.35), at(bell(1319, 1.2, 0.4), 0.12)), peak=0.45)
    save("transferred", mix(whoosh * 0.4, at(bell(784, 1.0, 0.35) * 0.7, 0.1)), peak=0.45)
    save("game_saved", mix(click * 0.6, at(bell(1397, 0.7, 0.25), 0.03)), peak=0.4)


if __name__ == "__main__":
    main()
