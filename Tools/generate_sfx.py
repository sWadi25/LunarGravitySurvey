#!/usr/bin/env python3
"""
Procedural sound effects for The Lunar Gravity Survey.

Every clip is synthesised from scratch (sine tones, filtered noise, envelopes), so the audio is the
team's own original work with no licensing to credit. Re-run any time:

    python3 Tools/generate_sfx.py            (from the project root; needs numpy)

Output: Assets/_Project/Audio/SFX/*.wav  (44.1 kHz, 16-bit mono)

Sound design note for the presentation: the Moon has no air, so nothing outside the suit can be heard.
Every sound here is justified as something the astronaut hears INSIDE the helmet - the radio (Quindar
tone, static, beacon), the suit's own instruments (dosimeter clicks, fan), or vibration carried through
the body and gloves (footsteps, the container clunk, the scanner's handle buzzer).
"""
import os
import wave

import numpy as np

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "_Project", "Audio", "SFX")
rng = np.random.default_rng(2026)


def t_axis(sec):
    return np.arange(int(SR * sec)) / SR


def env(n, attack=0.005, release=0.05, sec=None):
    """Linear attack, exponential-ish release envelope over n samples."""
    e = np.ones(n)
    a = max(1, int(SR * attack))
    r = max(1, int(SR * release))
    e[:a] = np.linspace(0, 1, a)
    e[-r:] *= np.linspace(1, 0, r) ** 2
    return e


def tone(freq, sec, harmonics=((1, 1.0),), decay=None):
    t = t_axis(sec)
    y = sum(amp * np.sin(2 * np.pi * freq * k * t) for k, amp in harmonics)
    if decay:
        y *= np.exp(-t / decay)
    return y


def noise(sec):
    return rng.standard_normal(int(SR * sec))


def band(x, lo, hi, loop=False):
    """FFT band-pass. With loop=True the result is perfectly periodic (seamless loop)."""
    n = len(x)
    pad = 0 if loop else n // 4
    X = np.fft.rfft(np.concatenate([x, np.zeros(pad)]))
    f = np.fft.rfftfreq(n + pad, 1 / SR)
    # trapezoid band with soft shoulders (no ringing)
    rise = np.clip((f - lo * 0.75) / max(lo * 0.5, 1e-6), 0, 1)
    fall = np.clip((hi * 1.25 - f) / (hi * 0.5), 0, 1)
    mask = rise * fall
    y = np.fft.irfft(X * mask, n + pad)
    return y[:n]


def silence(sec):
    return np.zeros(int(SR * sec))


def cat(*parts):
    return np.concatenate(parts)


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


def save(name, y, peak=0.85):
    os.makedirs(OUT, exist_ok=True)
    y = np.asarray(y, dtype=float)
    m = np.max(np.abs(y)) or 1.0
    y = y / m * peak
    data = (y * 32767).astype("<i2")
    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print(f"  {name}.wav  {len(y) / SR:.2f}s")


def main():
    print("Writing to", os.path.normpath(OUT))

    # Scanner: two quick rising "data" pips
    pip = lambda f: tone(f, 0.06, ((1, 1), (2, 0.25))) * env(int(SR * 0.06), 0.003, 0.03)
    save("scan_beep", cat(pip(1800), silence(0.03), pip(2400), silence(0.05)), 0.6)

    # Scanner: positive match - bright three-note chime (C6 E6 G6)
    def bell(f, sec):
        return tone(f, sec, ((1, 1), (2.01, 0.35), (3.02, 0.12)), decay=sec / 3) * env(int(SR * sec), 0.002, 0.04)
    save("scan_match", cat(bell(1047, 0.11), bell(1319, 0.11), bell(1568, 0.45)), 0.7)

    # Container: rejection buzz (two low square-ish pulses)
    def buzz(sec):
        t = t_axis(sec)
        sq = np.sign(np.sin(2 * np.pi * 170 * t)) * 0.6 + np.sin(2 * np.pi * 340 * t) * 0.3
        return band(sq, 80, 2500) * env(len(t), 0.004, 0.03)
    save("sample_reject", cat(buzz(0.14), silence(0.06), buzz(0.2), silence(0.05)), 0.6)

    # Container: sample locks in - low thump + latch click + short metallic ring
    thump = tone(62, 0.25, decay=0.07) * env(int(SR * 0.25), 0.002, 0.05)
    click = band(noise(0.012), 2000, 9000) * env(int(SR * 0.012), 0.0005, 0.008)
    ring = tone(930, 0.6, ((1, 1), (1.47, 0.6), (2.31, 0.35), (3.6, 0.15)), decay=0.12) * 0.35
    save("sample_lock", mix(thump * 1.0, cat(silence(0.01), click * 0.8), cat(silence(0.012), ring)), 0.8)

    # Container: lid closes - servo whine with vibrato, then a heavy latch
    t = t_axis(1.9)
    f = 210 - 25 * t / t[-1] + 4 * np.sin(2 * np.pi * 7 * t)
    phase = 2 * np.pi * np.cumsum(f) / SR
    servo = (np.sin(phase) + 0.5 * np.sin(2 * phase) + 0.25 * np.sin(3 * phase)) * 0.35
    servo = band(servo + band(noise(1.9), 300, 1800) * 0.08, 90, 3000) * env(len(t), 0.15, 0.25)
    latch = mix(tone(55, 0.35, decay=0.09), cat(silence(0.005), band(noise(0.02), 1500, 8000) * 0.7))
    save("lid_close", cat(servo, silence(0.05), latch * 1.3, silence(0.1)), 0.8)

    # Radio: Apollo "Quindar" intro tone - 2525 Hz, 250 ms (the real Mission Control tone)
    save("quindar_tone", tone(2525, 0.25) * env(int(SR * 0.25), 0.004, 0.01), 0.45)

    # Lander radio beacon: soft ping every 2 s (seamless loop, decays to silence before the end)
    ping = tone(1046, 0.7, ((1, 1), (1.5, 0.4)), decay=0.16) * env(int(SR * 0.7), 0.003, 0.2)
    save("lander_beacon_loop", cat(ping, silence(2.0 - 0.7)), 0.5)

    # Radio static from solar radio bursts (seamless 4 s loop): band-passed hiss + random crackles
    hiss = band(noise(4.0), 400, 6000, loop=True)
    hiss *= 0.75 + 0.25 * np.sin(2 * np.pi * 0.5 * t_axis(4.0)) ** 2  # slow breathing, periodic over 4 s
    crackle = np.zeros(int(SR * 4.0))
    for _ in range(140):
        i = rng.integers(0, len(crackle) - 400)
        crackle[i:i + 200] += rng.standard_normal(200) * np.exp(-np.arange(200) / 30) * rng.uniform(1, 4)
    save("radio_static_loop", hiss + band(crackle, 800, 9000, loop=True), 0.6)

    # Dosimeter / Geiger click
    gc = band(noise(0.025), 1500, 12000) * np.exp(-t_axis(0.025) / 0.0025)
    save("geiger_click", gc, 0.9)

    # Footsteps through the suit: low thud + muffled regolith crunch (three variations)
    for i in range(3):
        sec = 0.32
        f0 = 55 + i * 9
        body = tone(f0, sec, ((1, 1), (2, 0.3)), decay=0.06)
        crunch = band(noise(sec), 150 + 40 * i, 1200 + 200 * i) * np.exp(-t_axis(sec) / (0.05 + 0.01 * i)) * 0.55
        save(f"footstep_{i + 1}", (body + crunch) * env(int(SR * sec), 0.002, 0.08), 0.75)

    # Suit life-support fan (seamless 6 s loop): low air noise + faint motor hum
    air = band(noise(6.0), 60, 900, loop=True)
    hum = tone(120, 6.0) * 0.08 + tone(240, 6.0) * 0.03
    save("suit_fan_loop", air + hum, 0.5)

    print("Done.")


if __name__ == "__main__":
    main()
