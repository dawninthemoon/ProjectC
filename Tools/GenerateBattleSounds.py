"""Deterministic, sample-free ProjectC SFX; Python standard library only.

Generated WAVs are dedicated to CC0-1.0. This script never downloads or invokes an API.
"""
from array import array
import argparse
import json
import math
from pathlib import Path
import random
import sys
import wave

RATE = 44100
PEAK = 0.55
ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = ROOT / 'ProjectC.Client/Assets/Audios/ProjectC_Synth'


def bell(t, pitch, decay=0.2):
    if t < 0:
        return 0.0
    attack = 1.0 - math.exp(-t / 0.003)
    return attack * sum(gain * math.sin(2 * math.pi * pitch * ratio * t)
                        * math.exp(-t / (decay / (1 + index * .65)))
                        for index, (ratio, gain) in enumerate([(1, 1), (2.71, .25), (4.07, .12)]))


def drum(t, pitch=80):
    if t < 0:
        return 0.0
    # An exponentially falling oscillator and a short wooden overtone.
    phase = 2 * math.pi * (pitch * t + 80 * .025 * (1 - math.exp(-t / .025)))
    return math.sin(phase) * (1 - math.exp(-t / .002)) * math.exp(-t / .075) \
        + .2 * math.sin(2 * math.pi * 920 * t) * math.exp(-t / .013)


def make(name, duration, seed):
    rng = random.Random(seed)
    result = []
    low = 0.0
    for index in range(round(duration * RATE)):
        t = index / RATE
        noise = rng.uniform(-1, 1)
        low = .85 * low + .15 * noise
        high = noise - low
        paper = high * (1 - math.exp(-t / .012)) * math.exp(-t / .042)
        if name == 'Draw':
            value = .65 * paper + .08 * bell(t - .025, 1180, .07)
        elif name == 'CardUse':
            value = .5 * paper + .4 * drum(t - .025, 155) + .12 * bell(t - .05, 587.33, .12)
        elif name == 'Summon':
            value = .85 * drum(t, 65) + .28 * bell(t - .035, 293.66, .24) + .08 * paper
        elif name == 'Attack':
            envelope = math.sin(math.pi * t / duration) ** 2
            sweep = math.sin(2 * math.pi * (1000 * t - 750 * t * t / (2 * duration)))
            value = .48 * high * envelope * math.exp(-t / .16) + .12 * sweep * envelope
        elif name == 'Hit':
            value = .85 * drum(t, 92) + .34 * high * math.exp(-t / .024)
        elif name == 'Destroy':
            value = .35 * low * math.exp(-t / .12) + .25 * high * math.exp(-t / .09)
            value += .28 * bell(t, 196, .14) + .18 * bell(t - .025, 155.56, .12)
        elif name == 'Transform':
            value = .3 * bell(t, 293.66, .3) + .32 * bell(t - .055, 392, .28)
            value += .33 * bell(t - .11, 440, .24) + .22 * bell(t - .16, 587.33, .22)
            value += .13 * low * math.sin(math.pi * t / duration) ** 2
        elif name == 'Effect':
            value = .5 * bell(t, 622.25, .17) + .25 * bell(t - .04, 880, .16) + .15 * paper
        elif name == 'TurnStart':
            value = .28 * drum(t, 110) + .48 * bell(t - .04, 587.33, .27) + .2 * bell(t - .08, 880, .24)
        elif name == 'Victory':
            value = sum(.35 * bell(t - delay, pitch, .29) for delay, pitch in
                        [(0, 587.33), (.12, 783.99), (.24, 880), (.38, 1174.66)])
            value += .16 * drum(t - .38, 92)
        elif name == 'Defeat':
            value = .3 * bell(t, 440, .25) + .35 * bell(t - .13, 311.13, .29)
            value += .4 * bell(t - .28, 146.83, .32) + .18 * drum(t - .28, 62)
        else:
            raise ValueError(name)
        # Smooth both endpoints, remove DC below, never leave a hard waveform edge.
        fade = min(1.0, t / .003, (duration - t) / .035)
        result.append(value * max(0.0, fade))
    average = sum(result) / len(result)
    result = [(value - average) * min(1.0, index / (.003 * RATE),
              (len(result) - index - 1) / (.035 * RATE)) for index, value in enumerate(result)]
    maximum = max(abs(value) for value in result)
    return array('h', (round(value * PEAK / maximum * 32767) for value in result))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    manifest = []
    sounds = [('Draw', .22), ('CardUse', .28), ('Summon', .45), ('Attack', .22),
              ('Hit', .25), ('Destroy', .4), ('Transform', .65), ('Effect', .4),
              ('TurnStart', .55), ('Victory', 1.0), ('Defeat', .9)]
    for index, (name, duration) in enumerate(sounds):
        samples = make(name, duration, 1700 + index)
        if sys.byteorder != 'little':
            samples.byteswap()
        path = args.output / (name + '.wav')
        with wave.open(str(path), 'wb') as output:
            output.setnchannels(1)
            output.setsampwidth(2)
            output.setframerate(RATE)
            output.writeframes(samples.tobytes())
        manifest.append({'cue': name, 'file': path.name, 'duration': duration,
                         'rate': RATE, 'channels': 1, 'peak': PEAK, 'seed': 1700 + index})
    (args.output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print(f'Generated {len(sounds)} mono PCM16 WAV files in {args.output}')


if __name__ == '__main__':
    main()
