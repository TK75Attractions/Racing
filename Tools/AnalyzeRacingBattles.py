#!/usr/bin/env python3
"""Count sustained, nearby on-road order changes; resets and distant rank changes are excluded."""
import csv
import json
import math
import sys
from pathlib import Path


def analyze(folder):
    root = Path(folder)
    report = json.loads((root / 'report.json').read_text())
    results = []
    for run in report['runs']:
        frames = {}
        with (root / run['name'] / 'trace.csv').open() as stream:
            for row in csv.DictReader(stream):
                frames.setdefault(float(row['time']), {})[row['car']] = row
        states, previous, events, last_event = {}, {}, [], {}
        for time, cars in sorted(frames.items()):
            for human in ('P1', 'P2'):
                for cpu in ('CPU 1', 'CPU 2'):
                    key = (human, cpu)
                    if human not in cars or cpu not in cars:
                        states.pop(key, None)
                        continue
                    a, b = cars[human], cars[cpu]
                    progress = (float(a['progress']), float(b['progress']))
                    resets = (a['recoveries'], b['recoveries'])
                    gap = progress[0] - progress[1]
                    distance = math.dist([float(a[k]) for k in ('x', 'y', 'z')], [float(b[k]) for k in ('x', 'y', 'z')])
                    old = previous.get(key)
                    previous[key] = (progress, resets)
                    valid = (old is not None and old[1] == resets and
                             all(0 <= progress[i] - old[0][i] < 10 for i in (0, 1)) and
                             a['offCourse'] == 'False' and b['offCourse'] == 'False' and abs(gap) <= 45 and distance <= 40 and abs(float(a['y']) - float(b['y'])) <= 4 and
                             min(float(a['speed']), float(b['speed'])) >= 4 and a['connected'] == 'True')
                    if not valid:
                        states.pop(key, None)
                        continue
                    side = 1 if gap >= 3 else -1 if gap <= -3 else 0
                    if side == 0:
                        continue
                    if key in states and states[key] != side and time - last_event.get(key, -100) >= 5:
                        events.append(dict(time=time, human=human, cpu=cpu,
                                           event='human-passed-cpu' if side == 1 else 'cpu-passed-human'))
                        last_event[key] = time
                    states[key] = side
        results.append(dict(name=run['name'], events=events,
                            cpuPasses=sum(e['event'] == 'cpu-passed-human' for e in events),
                            humanPasses=sum(e['event'] == 'human-passed-cpu' for e in events),
                            cars=[{k: c[k] for k in ('label', 'finished', 'position', 'maxStall', 'offCourseSeconds',
                                                   'vehicleContacts', 'humanRecoveries', 'cpuRecoveries')} for c in run['cars']]))
    output = dict(method='Progress changes must clear +/-3m, be within 40m in world space, on the same height, moving forward, with no recovery. Five-second debounce per pair. These are observed order changes, not a human enjoyment score.',
                  runtimeErrors=report['runtimeErrors'], runs=results)
    (root / 'battles.json').write_text(json.dumps(output, ensure_ascii=False, indent=2) + '\n')
    for run in results:
        print(root.name, run['name'], 'CPU passes:', run['cpuPasses'], 'human passes:', run['humanPasses'])


if __name__ == '__main__':
    for folder in sys.argv[1:]:
        analyze(folder)
