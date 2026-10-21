#!/usr/bin/env python3
"""Author the original race SFX masters offline. Runtime only reads the committed WAVs.
Requires numpy. No recordings, downloaded samples, services, or third-party audio.
"""
import argparse
import hashlib
import json
from pathlib import Path
import wave

import numpy as np

RATE = 48000
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Resources/Audio/SFX'
ART = ROOT / 'Artifacts/SoundEffects'
RNG = np.random.default_rng(20261021)


def timeline(seconds):
    return np.arange(round(seconds * RATE)) / RATE


def envelope(t, attack=.004, decay=.2):
    return np.minimum(t / attack, 1) * np.exp(-t / decay)


def tone(seconds, frequency, decay=.2, body=.25):
    t = timeline(seconds)
    # Detuned body, warm second harmonic and a struck, inharmonic metallic partial.
    phase = 2 * np.pi * frequency * t
    return envelope(t, decay=decay) * (np.sin(phase) + body * np.sin(phase * 2.003) +
                                       .09 * np.sin(phase * 3.97) * np.exp(-t / .035))


def noise(seconds, low=200, high=6000):
    t = timeline(seconds)
    frequencies = np.fft.rfftfreq(len(t), 1 / RATE)
    spectrum = np.fft.rfft(RNG.standard_normal(len(t)))
    # Smooth bandpass in the frequency domain; no brittle unfiltered white-noise layer.
    band = (1 - np.exp(-(frequencies / low) ** 4)) * np.exp(-(frequencies / high) ** 4)
    signal = np.fft.irfft(spectrum * band, n=len(t))
    return signal / max(np.sqrt(np.mean(signal ** 2)), 1e-9)


def sweep(seconds, start, end, decay=.25):
    t = timeline(seconds)
    ratio = np.log(end / start) / seconds
    phase = 2 * np.pi * start * np.expm1(t * ratio) / ratio
    return np.sin(phase) * envelope(t, .006, decay)


class Cue:
    def __init__(self, seconds):
        self.audio = np.zeros((round(seconds * RATE), 2))

    def add(self, signal, at=0, gain=1, pan=0):
        start = round(at * RATE)
        size = min(len(signal), len(self.audio) - start)
        if size <= 0:
            return self
        # Equal power placement, with a restrained width so mono remains useful.
        angle = (pan + 1) * np.pi / 4
        self.audio[start:start + size] += signal[:size, None] * gain * np.array([np.cos(angle), np.sin(angle)])
        return self

    def note(self, frequency, at=0, gain=.5, decay=.22, pan=0):
        return self.add(tone(min(decay * 6, len(self.audio) / RATE - at), frequency, decay), at, gain, pan)

    def hit(self, at=0, gain=.25):
        t = timeline(.3)
        self.add(sweep(.3, 155, 52, .065), at, gain)
        self.add(noise(.12, 800, 6500) * envelope(timeline(.12), .001, .018), at, gain * .28)
        return self

    def air(self, seconds=.4, at=0, gain=.1, rise=False):
        t = timeline(seconds)
        shape = np.sin(np.pi * t / seconds) ** (1.5 if rise else .65)
        shape *= np.linspace(.1, 1, len(t)) if rise else np.exp(-t / (seconds * .4))
        self.add(noise(seconds, 400, 5500) * shape, at, gain, -.35)
        self.add(noise(seconds, 500, 6500) * shape, at, gain, .35)
        return self

    def finish(self, peak=.3, space=.18):
        # Baked early reflections and a short, diffuse stereo tail.
        dry = self.audio.copy()
        for delay, gain in [(.019, .42), (.037, .29), (.067, .22), (.103, .16), (.163, .1), (.239, .065)]:
            offset = round(delay * RATE)
            self.audio[offset:] += dry[:-offset, ::-1] * gain * space
        # Remove subsonics/DC, saturate the body gently, preserve authored relative gain.
        spectrum = np.fft.rfft(self.audio, axis=0)
        f = np.fft.rfftfreq(len(self.audio), 1 / RATE)
        spectrum *= (1 - np.exp(-(f / 35) ** 4))[:, None]
        self.audio = np.tanh(np.fft.irfft(spectrum, n=len(self.audio), axis=0) * 1.1)
        self.audio *= peak / max(np.max(np.abs(self.audio)), 1e-9)
        fade = min(round(.025 * RATE), len(self.audio) // 3)
        self.audio[:96] *= np.linspace(0, 1, 96)[:, None]
        self.audio[-fade:] *= np.linspace(1, 0, fade)[:, None]
        self.audio[0] = self.audio[-1] = 0
        return self.audio


def compose():
    bank = {}
    bank['Select'] = Cue(.16).note(1450, gain=.5, decay=.025).note(2175, .008, .12, .02, .2).finish(.13, .06)
    bank['Press'] = Cue(.19).hit(gain=.25).note(430, gain=.24, decay=.03).finish(.19, .07)
    bank['Confirm'] = Cue(.62).hit(gain=.1).note(660, gain=.4, decay=.12, pan=-.2).note(990, .065, .45, .18, .2).note(1320, .09, .16, .15).finish(.27)
    bank['Cancel'] = Cue(.38).note(650, gain=.38, decay=.065, pan=.15).note(390, .06, .42, .085, -.15).air(.17, gain=.035).finish(.22, .1)
    ready = Cue(.95).air(.3, gain=.055, rise=True)
    for i, hz in enumerate([523.25, 659.25, 783.99]):
        ready.note(hz, .06 * i, .33, .23, (i - 1) * .3)
    bank['Ready'] = ready.finish(.28, .3)
    bank['Countdown'] = Cue(.42).hit(gain=.11).note(880, gain=.6, decay=.075).note(1760, gain=.13, decay=.055).finish(.32, .12)
    go = Cue(1.22).hit(gain=.45).air(.75, gain=.13).add(sweep(.6, 180, 1450, .28), gain=.2)
    for i, hz in enumerate([880, 1108.73, 1320, 1760]):
        go.note(hz, .015 * i, .25, .26, (i - 1.5) * .2)
    bank['Go'] = go.finish(.34, .32)
    finish = Cue(2.9).air(.6, gain=.1).hit(gain=.4)
    for at, hz in [(0, 523.25), (.16, 659.25), (.32, 783.99), (.52, 1046.5)]:
        finish.note(hz, at, .43, .28 if at < .5 else .55, -.2 if at < .3 else .2)
    for hz, pan in [(261.63, -.25), (659.25, .25), (783.99, -.4), (1046.5, .4)]:
        finish.note(hz, .54, .23, .5, pan)
    for at in [.54, .78, 1.02]:
        finish.hit(at, .12)
    bank['Finish'] = finish.finish(.35, .5)
    result = Cue(1.65).hit(gain=.15)
    for i, hz in enumerate([783.99, 987.77, 1174.66, 1567.98]):
        result.note(hz, i * .075, .35, .3, (-1) ** i * .3)
    bank['Results'] = result.finish(.29, .4)
    skid = Cue(.63)
    t = timeline(.55)
    rubber = np.sin(2 * np.pi * (930 * t + 70 / (2 * np.pi * 13) * np.sin(2 * np.pi * 13 * t)))
    skid.add((.48 * rubber + .36 * noise(.55, 750, 3700)) * np.sin(np.pi * t / .55) ** .7, gain=.5)
    bank['TireSkid'] = skid.finish(.18, .08)
    bank['DriftCharge'] = Cue(.62).note(1174.66, gain=.4, decay=.12, pan=-.25).note(1567.98, .045, .35, .18, .25).air(.25, gain=.055, rise=True).finish(.23, .22)
    drift = Cue(.98).hit(gain=.35).air(.64, gain=.18).add(sweep(.55, 360, 1650, .25), gain=.24)
    drift.note(1567.98, .04, .17, .16, -.3).note(2093, .08, .13, .15, .3)
    bank['DriftBoost'] = drift.finish(.31, .22)
    bank['Boost'] = Cue(1.3).hit(gain=.4).air(.95, gain=.23).add(sweep(.7, 110, 870, .38), gain=.34).finish(.32, .25)
    bank['Landing'] = Cue(.85).hit(gain=.65).note(523.25, .025, .16, .12, -.25).note(1046.5, .075, .21, .18, .25).air(.24, gain=.1).finish(.29, .18)
    bank['Charge'] = Cue(.72).note(1318.51, gain=.4, decay=.12, pan=-.3).note(1975.53, .04, .34, .2, .3).note(2637, .08, .12, .13).finish(.23, .32)
    pickup = Cue(.95).air(.25, gain=.08, rise=True)
    for i, hz in enumerate([659.25, 880, 1318.51, 1760]):
        pickup.note(hz, i * .045, .32, .18, (-1) ** i * .35)
    bank['ItemPickup'] = pickup.finish(.26, .3)
    shield = Cue(1.25).air(.55, gain=.1, rise=True).add(sweep(.42, 260, 1040, .22), gain=.18)
    for hz, pan in [(523.25, -.35), (783.99, .35), (1567.98, 0)]:
        shield.note(hz, .09, .26, .3, pan)
    bank['Shield'] = shield.finish(.26, .5)
    impact = Cue(.68).hit(gain=.75).air(.28, gain=.18)
    for i, hz in enumerate([243, 417, 683, 1121, 1913]):
        impact.note(hz, .002 * i, .09, .065 + i * .012, (-1) ** i * .2)
    bank['Impact'] = impact.finish(.31, .12)
    final = Cue(1.9).hit(gain=.18).air(.45, gain=.06)
    for i, hz in enumerate([659.25, 783.99, 987.77, 1318.51]):
        final.note(hz, i * .11, .35, .28, (-1) ** i * .25)
    final.note(329.63, .33, .17, .38)
    bank['FinalLap'] = final.finish(.3, .4)
    return bank


def write_wav(path, pcm):
    with wave.open(str(path), 'wb') as stream:
        stream.setnchannels(2)
        stream.setsampwidth(2)
        stream.setframerate(RATE)
        stream.writeframes(np.rint(pcm * 32767).astype('<i2').tobytes())


def write_meta(path):
    meta = Path(str(path) + '.meta')
    if meta.exists():
        return
    guid = hashlib.md5(('Racing-original-SFX/' + path.name).encode()).hexdigest()
    meta.write_text(f'''fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 48000
    compressionFormat: 0
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
''')


def validate(bank):
    report = {}
    for name in bank:
        with wave.open(str(OUT / (name + '.wav')), 'rb') as stream:
            assert (stream.getframerate(), stream.getnchannels(), stream.getsampwidth()) == (RATE, 2, 2), name
            pcm = np.frombuffer(stream.readframes(stream.getnframes()), dtype='<i2').reshape(-1, 2) / 32768
        peak = float(np.max(np.abs(pcm)))
        dc = float(np.abs(pcm.mean(axis=0)).max())
        assert .02 < peak < .36 and dc < .002, (name, peak, dc)
        assert np.all(pcm[0] == 0) and np.all(pcm[-1] == 0), name
        assert np.sqrt(np.mean(pcm ** 2)) > .008, name
        report[name] = {'seconds': round(len(pcm) / RATE, 3), 'peak_dbfs': round(20 * np.log10(peak), 2),
                        'rms_dbfs': round(20 * np.log10(np.sqrt(np.mean(pcm ** 2))), 2), 'dc': round(dc, 7),
                        'stereo_difference_rms': round(float(np.sqrt(np.mean(np.diff(pcm, axis=1) ** 2))), 6)}
    (ART / 'analysis.json').write_text(json.dumps(report, indent=2) + '\n')
    print(f'SFX_ASSET_VALIDATION_PASS: {len(report)} stereo PCM masters; no clipping/DC; silent endpoints.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Validate committed files without rebuilding them.')
    args = parser.parse_args()
    ART.mkdir(parents=True, exist_ok=True)
    bank = compose()
    if not args.check:
        OUT.mkdir(parents=True, exist_ok=True)
        for name, pcm in bank.items():
            path = OUT / (name + '.wav')
            write_wav(path, pcm)
            write_meta(path)
        preview = np.concatenate([part for pcm in bank.values() for part in [pcm, np.zeros((RATE // 3, 2))]])
        write_wav(ART / 'preview.wav', preview)
    validate(bank)


if __name__ == '__main__':
    main()
