#!/usr/bin/env python3
"""Measure exported engine WAVs; requires numpy. Does not change or normalize audition files."""
import csv
import json
import sys
import wave
from pathlib import Path
import numpy as np

folder = Path(sys.argv[1] if len(sys.argv) > 1 else 'Artifacts/EngineAudio')
report = {}
for path in sorted(folder.glob('*.wav')):
    with wave.open(str(path), 'rb') as stream:
        rate = stream.getframerate()
        pcm = np.frombuffer(stream.readframes(stream.getnframes()), dtype='<i2').astype(float) / 32768
        pcm = pcm.reshape(-1, stream.getnchannels())
    mono = pcm.mean(axis=1)
    # Ignore initial fades for steady-state spectral measurements.
    section = mono[rate:rate * 3] if len(mono) >= rate * 3 else mono
    spectrum = abs(np.fft.rfft(section * np.hanning(len(section)))) ** 2
    frequencies = np.fft.rfftfreq(len(section), 1 / rate)
    energy = spectrum.sum()
    rms = np.sqrt(np.mean(pcm ** 2))
    report[path.name] = {
        'seconds': round(len(pcm) / rate, 3),
        'peak_dbfs': round(20 * np.log10(max(np.max(abs(pcm)), 1e-12)), 2),
        'rms_dbfs': round(20 * np.log10(max(rms, 1e-12)), 2),
        'dc': round(float(mono.mean()), 7),
        'clipped_samples': int((abs(pcm) >= .999).sum()),
        'max_sample_step': round(float(np.max(abs(np.diff(pcm, axis=0)))), 5),
        'sub_40hz_percent': round(float(spectrum[frequencies < 40].sum() / energy * 100), 2),
        'over_8khz_percent': round(float(spectrum[frequencies > 8000].sum() / energy * 100), 2),
        'centroid_hz': round(float((frequencies * spectrum).sum() / energy), 1),
        'stereo_difference_rms': round(float(np.sqrt(np.mean((pcm[:, 0] - pcm[:, -1]) ** 2))), 5),
    }
    assert report[path.name]['clipped_samples'] == 0, f'{path.name}: clipped PCM'
    assert abs(mono.mean()) < .002, f'{path.name}: DC bias'
# Shifts must audibly lower RPM after each upshift; inspect real output telemetry.
with (folder / 'drive.csv').open() as stream:
    rows = list(csv.DictReader(stream))
shifts = []
for index in range(1, len(rows) - 15):
    if int(rows[index]['gear']) > int(rows[index - 1]['gear']):
        before = float(rows[index - 1]['rpm'])
        after = float(rows[index + 12]['rpm'])
        assert after < before - 400, 'Upshift must produce a clear RPM drop'
        shifts.append({'seconds': float(rows[index]['seconds']), 'gear': int(rows[index]['gear']),
                       'before_rpm': before, 'after_200ms_rpm': after})
assert len(shifts) == 4, 'The 166 km/h sample should use five longer gears'
assert shifts[0]['seconds'] > 8, 'First gear must carry the car past 60 km/h before shifting'
result = {'wav_metrics': report, 'upshifts': shifts}
exit_path = folder / '07-gameplay-exit.wav'
if exit_path.exists():
    with wave.open(str(exit_path), 'rb') as stream:
        rate = stream.getframerate()
        pcm = np.frombuffer(stream.readframes(stream.getnframes()), dtype='<i2').reshape(-1, 2).astype(float) / 32768
    difference = pcm[:, 0] - pcm[:, 1]
    first = float(np.sqrt(np.mean(difference[:rate // 2] ** 2)))
    last = float(np.sqrt(np.mean(difference[-rate // 2:] ** 2)))
    assert first > .0001 and last < first * .01, 'Tunnel tail must decay after the actual exit'
    result['actual_exit_tail'] = {'first_half_second_stereo_rms': first, 'last_half_second_stereo_rms': last}

(folder / 'analysis.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result, indent=2))
