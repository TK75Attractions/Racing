#!/usr/bin/env python3
"""Audit recorded production-physics races, including tests that merely reached a timeout."""
import argparse
import csv
import json
import math
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("folder", type=Path)
args = parser.parse_args()
report = json.loads((args.folder / "report.json").read_text())
failures, summaries = [], []
if not report["completed"] or report.get("runtimeErrors"):
    failures.append("incomplete suite or runtime errors")
if "FORWARD-ONLY" not in report["methodology"]:
    failures.append("not a forward-only suite")
for run in report["runs"]:
    rows = list(csv.DictReader((args.folder / run["name"] / "trace.csv").open()))
    if run["itemsEnabled"] and sum(c["itemsUsed"] for c in run["cars"][:2]) == 0:
        failures.append(run["name"] + ": no player item effects exercised")
    events = list(csv.DictReader((args.folder / run["name"] / "events.csv").open()))
    if run["name"] == "forward-wall-stress" and not any(e["event"] == "forward-steering-mistake" for e in events):
        failures.append(run["name"] + ": steering stress never executed")
    for cpu in run["cars"][2:]:
        samples = [r for r in rows if r["car"] == cpu["label"]]
        if any(float(before["progress"])-float(after["progress"]) > report["courseLength"]*.5
               and before["lap"] == after["lap"] and before["recoveries"] == after["recoveries"]
               for before, after in zip(samples, samples[1:])):
            failures.append(run["name"] + "/" + cpu["label"] + ": missed CPU lap")
    summary = {"name": run["name"], "termination": run["termination"], "players": []}
    for car in run["cars"][:2]:
        samples = [r for r in rows if r["car"] == car["label"]]
        prefix = run["name"] + "/" + car["label"]
        if not samples or car["manualResets"] or car["usedAutodrive"]:
            failures.append(prefix + ": missing trace, manual reset or production autodrive")
        if any(float(r["pedal"]) < -0.0001 or not math.isfinite(float(r["speed"])) for r in samples):
            failures.append(prefix + ": reverse input or non-finite speed")
        if any((r["connected"] == "False" or r["needsRelease"] == "True") and abs(float(r["pedal"])) > .0001 for r in samples):
            failures.append(prefix + ": input was not gated during disconnect/release wait")
        if run["name"] == "skill-disconnect-rejoin" and car["label"] == "P2":
            if not any(r["connected"] == "False" for r in samples) or not any(float(r["time"]) > 50 and float(r["pedal"]) > .1 for r in samples):
                failures.append(prefix + ": disconnect/rejoin was not exercised")
        if run["name"] == "skill-stop-rejoin" and car["label"] == "P2":
            if sum(45 <= float(r["time"]) < 57 and float(r["pedal"]) == 0 for r in samples) < 20:
                failures.append(prefix + ": pedal release interval was not exercised")
        if not car["finished"] and run["name"] != "forward-novice":
            failures.append(prefix + ": failed to finish")
        if car["finished"] and car["laps"] != run["goalLaps"]:
            failures.append(prefix + ": finish with incorrect lap count")
        missed_wraps = sum(float(before["progress"])-float(after["progress"]) > report["courseLength"]*.5
                           and before["lap"] == after["lap"] and before["recoveries"] == after["recoveries"]
                           for before, after in zip(samples, samples[1:]))
        if missed_wraps:
            failures.append(prefix + ": crossed the finish without earning a lap")
        # Measure forward progress rather than speed: jitter against a wall can look fast.
        best = None
        stopped_at = None
        longest = 0.0
        last_recovery = None
        for r in samples:
            now, progress = float(r["time"]), float(r["progress"])
            recovery = int(r["recoveries"])
            active = (float(r["pedal"]) >= .4 and r["connected"] == "True" and
                      r["needsRelease"] == "False" and r["spin"] == "False")
            if best is None or last_recovery != recovery or not active:
                best, stopped_at = progress, now
            elif progress > best + 2:
                best, stopped_at = progress, now
            else:
                longest = max(longest, now - stopped_at)
            last_recovery = recovery
        if longest > 12:
            failures.append(prefix + ": no forward progress under pedal for %.2fs" % longest)
        summary["players"].append({"label": car["label"], "finished": car["finished"], "laps": car["laps"],
            "automaticRecoveries": car["automaticRecoveries"], "missedLapWraps": missed_wraps, "maxSpeedStall": car["maxStall"],
            "maxDemandWithoutProgress": round(longest, 2), "manualResets": car["manualResets"]})
    summaries.append(summary)
result = {"passed": not failures, "failures": failures, "runs": summaries}
(args.folder / "audit.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
print(json.dumps(result, ensure_ascii=False, indent=2))
raise SystemExit(bool(failures))
