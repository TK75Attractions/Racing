#!/usr/bin/env python3
"""Summarize actual input-only playtest traces; signed lead is AI progress minus assigned human progress."""
import argparse
import csv
import json
from pathlib import Path


def analyze(folder):
    summary = json.loads((folder / 'summary.json').read_text())
    frames = {}
    for row in csv.DictReader((folder / 'trace.csv').open()):
        time = float(row['time'])
        if time >= 25:
            frames.setdefault(time, {})[row['car']] = row
    auto = any(car["label"] == "P2" and car["usedAutodrive"] for car in summary["cars"])
    metrics = {}
    for car in summary['cars']:
        label = car['label']
        if not label.startswith('CPU') and not (label == 'P2' and car['usedAutodrive']):
            continue
        gaps = []
        for group in frames.values():
            if label not in group:
                continue
            row = group[label]
            rival = 'P1' if label == 'P2' or auto else 'P' + str(int(row['cpuRival']) + 1)
            if rival not in group:
                continue
            gaps.append(float(row['progress']) - float(group[rival]['progress']))
        metrics[label] = {
            'reference': 'P1' if auto else 'assigned human',
            'samples': len(gaps),
            'mean_lead_m': sum(gaps) / len(gaps) if gaps else None,
            'mean_absolute_gap_m': sum(abs(gap) for gap in gaps) / len(gaps) if gaps else None,
            'within_40m_percent': 100 * sum(abs(gap) < 40 for gap in gaps) / len(gaps) if gaps else None,
            'ahead_more_than_80m_percent': 100 * sum(gap > 80 for gap in gaps) / len(gaps) if gaps else None,
            'minimum_lead_m': min(gaps) if gaps else None,
            'maximum_lead_m': max(gaps) if gaps else None,
            'mean_kph': car['meanSpeed'] * 3.6,
            'peak_kph': car['maxSpeed'] * 3.6,
            'off_course_seconds': car['offCourseSeconds'],
            'recoveries': car['cpuRecoveries'] + car['humanRecoveries'],
        }
    return {'scenario': summary['name'], 'window_seconds': summary['physicsSeconds'], 'metrics': metrics}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    result = []
    for summary in sorted(args.root.glob('*/summary.json')):
        result.append(analyze(summary.parent))
    value = json.dumps(result, indent=2, ensure_ascii=False) + '\n'
    if args.output:
        args.output.write_text(value)
    print(value, end='')


if __name__ == '__main__':
    main()
