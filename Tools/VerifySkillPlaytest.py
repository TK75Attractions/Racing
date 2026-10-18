#!/usr/bin/env python3
"""Check gameplay invariants against recorded production Play Mode runs, without Unity."""
import csv
import json
import math
import sys
from pathlib import Path


def verify(root):
    report = json.loads((root / 'report.json').read_text())
    failures, checks = [], []

    def require(ok, message):
        (checks if ok else failures).append(message)

    require(report['completed'], 'The skill suite completed.')
    require(not report['runtimeErrors'], 'No game errors were logged while recording.')
    require(len(report['runs']) == 6, 'All six skill scenarios were run.')
    for run in report['runs']:
        require(run['completed'] and run['raceEnded'], run['name'] + ': reached a real race ending.')
        require(run['seconds'] < 361, run['name'] + ': respected the global race deadline.')
        rows = list(csv.DictReader((root / run['name'] / 'trace.csv').open()))
        for car in run['cars']:
            require(not car['finished'] or car['laps'] == run['goalLaps'],
                    run['name'] + '/' + car['label'] + ': finishing requires the configured laps.')
            previous = None
            travelled = 0
            for row in (r for r in rows if r['car'] == car['label']):
                position = [float(row[k]) for k in ('x', 'y', 'z')]
                if previous:
                    step = math.dist(position, previous)
                    if step < 8:  # Recovery teleports must not masquerade as driven distance.
                        travelled += step
                previous = position
                if int(row['lap']) > 0:
                    require(travelled > report['courseLength'] * .8,
                            run['name'] + '/' + car['label'] + ': a first lap required actual circuit travel.')
                    break
        if run['name'] == 'skill-operator-stop':
            require(all(not c['finished'] for c in run['cars']), 'Operator stop did not create any finishers.')
        if run['name'] == 'skill-disconnect-rejoin':
            disconnected = [r for r in rows if r['car'] == 'P2' and 45.2 < float(r['time']) < 49.8]
            require(bool(disconnected) and all(r['connected'] == 'False' and abs(float(r['pedal'])) < .001 for r in disconnected),
                    'The disconnected source remained neutral.')
            events = list(csv.DictReader((root / run['name'] / 'events.csv').open()))
            require(any(e['car'] == 'P2' and e['event'] == 'release-pedal-response' and float(e['time']) >= 50 for e in events),
                    'The reconnect scenario exercised the release-to-resume prompt.')
    result = {'passed': not failures, 'passedChecks': len(checks), 'failures': failures, 'checks': checks}
    (root / 'verification.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({k: result[k] for k in ('passed', 'passedChecks', 'failures')}, indent=2))
    return not failures


if __name__ == '__main__':
    sys.exit(0 if verify(Path(sys.argv[1])) else 1)
