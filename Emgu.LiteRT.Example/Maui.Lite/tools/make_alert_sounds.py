#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Synthesize the two alert sounds the demo ships (Resources/Raw/alert_white_noise.wav and alert_lullaby.wav).

Both are made from scratch here, so they have no licence beyond the repository's: a soft, seamlessly looping noise, and a short music-box tune
of our own (three-note falling phrases on a pentatonic scale, in 3/4 at 60 bpm). 22.05 kHz, mono, 16 bit."""
import math, random, struct, wave
from pathlib import Path

RATE = 22050
OUT = Path(__file__).resolve().parent.parent / "Resources" / "Raw"


def write(name, samples):
    peak = max(abs(s) for s in samples) or 1
    data = b"".join(struct.pack("<h", int(max(-1, min(1, s / peak)) * 0.7 * 32767)) for s in samples)
    with wave.open(str(OUT / name), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(data)
    print(name, len(samples) / RATE, "s")


def white_noise(seconds=20):
    """Brown-ish noise (a gentle hush): integrated white noise, high-passed, then cross-faded so the end joins the start."""
    rnd = random.Random(11)
    n = int(RATE * seconds)
    fade = RATE * 2
    raw = []
    x = 0.0
    for _ in range(n + fade):
        x = 0.985 * x + rnd.uniform(-1, 1)
        raw.append(x)
    # remove the slow drift
    mean = sum(raw) / len(raw)
    raw = [v - mean for v in raw]
    out = raw[:n]
    for i in range(fade):                      # the tail beyond n fades in over the start, equal-power
        a = i / fade
        out[i] = out[i] * math.sin(a * math.pi / 2) + raw[n + i] * math.cos(a * math.pi / 2)
    return out


def music_box():
    """Our own tune: a falling three-note phrase on C D E G A, three bars of it, answered by a rising one, looped."""
    scale = {"C": 523.25, "D": 587.33, "E": 659.25, "G": 783.99, "A": 880.0, "c": 261.63, "d": 293.66, "e": 329.63, "g": 392.0, "a": 440.0}
    # (note, beats) at 60 bpm, 3/4: each bar is three beats
    melody = [("A", 1), ("G", 1), ("E", 1), ("G", 1), ("E", 1), ("D", 1), ("E", 1), ("D", 1), ("C", 1), ("D", 2), ("E", 1),
              ("G", 1), ("A", 1), ("G", 1), ("E", 1), ("D", 1), ("E", 1), ("C", 3)]
    bass = ["c", "g", "e", "g", "a", "g"]       # one bass note per bar, soft
    total = sum(b for _, b in melody)
    n = int(RATE * (total + 2.5))
    out = [0.0] * n

    def bell(freq, start, length, gain):
        s0 = int(start * RATE)
        for i in range(int(length * RATE)):
            t = i / RATE
            env = math.exp(-3.2 * t) * min(1, i / (0.004 * RATE))
            v = math.sin(2 * math.pi * freq * t) + 0.35 * math.sin(2 * math.pi * freq * 2.76 * t) * math.exp(-6 * t) + 0.12 * math.sin(2 * math.pi * freq * 5.4 * t) * math.exp(-10 * t)
            if s0 + i < n:
                out[s0 + i] += gain * env * v

    t = 0.0
    for note, beats in melody:
        bell(scale[note], t, 2.4, 0.5)
        t += beats
    for bar, note in enumerate(bass * 2):
        if bar * 3 < total:
            bell(scale[note], bar * 3, 2.8, 0.28)
    return out


if __name__ == "__main__":
    write("alert_white_noise.wav", white_noise())
    write("alert_lullaby.wav", music_box())
