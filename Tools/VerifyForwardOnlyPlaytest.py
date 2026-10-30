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
    if run["name"].startswith(("forward-full-lock", "forward-flick-", "forward-pedal-chatter")):
        stress = [e for e in events if e["event"] == "novice-input-stress"]
        if not stress:
            failures.append(run["name"] + ": novice input stress never executed")
        elif run["name"].endswith("-control"):
            start = float(stress[0]["time"])
            duration = 8 if "full-lock" in run["name"] else 2.4 if "flick" in run["name"] else 6
            owner = "P2" if "pedal" in run["name"] else "P1"
            exercised = [r for r in rows if r["car"] == owner and start <= float(r["time"]) < start+duration]
            if len(exercised) < duration*7:
                failures.append(run["name"] + ": stress duration was not exercised")
            effective = [r for r in exercised if r["rocket"] == "False" and int(r["grounded"]) >= 2]
            if "pedal" in run["name"]:
                if not any(float(r["pedal"]) >= .9 for r in effective) or not any(float(r["pedal"]) == 0 for r in effective):
                    failures.append(run["name"] + ": both pedal states must reach a grounded driver")
            elif sum(abs(float(r["appliedSteering"])) > .1 for r in effective) < 5:
                failures.append(run["name"] + ": rocket/input suppression masked the steering stress")
            if "flick" in run["name"] and not (any(float(r["steering"]) > 20 for r in effective) and any(float(r["steering"]) < -20 for r in effective)):
                failures.append(run["name"] + ": both steering directions must be exercised")
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
        if any(float(r["pedal"]) < -0.0001 or any(not math.isfinite(float(r[key]))
               for key in ("x", "y", "z", "speed", "progress", "pedal", "steering")) for r in samples):
            failures.append(prefix + ": reverse input or non-finite physics/input")
        if any((r["connected"] == "False" or r["needsRelease"] == "True") and abs(float(r["pedal"])) > .0001 for r in samples):
            failures.append(prefix + ": input was not gated during disconnect/release wait")
        if run["name"] == "skill-disconnect-rejoin" and car["label"] == "P2":
            if not any(r["connected"] == "False" for r in samples) or not any(float(r["time"]) > 50 and float(r["pedal"]) > .1 for r in samples):
                failures.append(prefix + ": disconnect/rejoin was not exercised")
        if run["name"] == "skill-stop-rejoin" and car["label"] == "P2":
            if sum(45 <= float(r["time"]) < 57 and float(r["pedal"]) == 0 for r in samples) < 20:
                failures.append(prefix + ": pedal release interval was not exercised")
        profile = run.get("p1Profile" if car["label"] == "P1" else "p2Profile", "")
        target_speed = run.get("p1TargetSpeed" if car["label"] == "P1" else "p2TargetSpeed", 50)
        deadline = run.get("maximumSeconds", 180)
        slow_deadline = (profile in ("novice", "cautious", "intermediate") and target_speed <= 24
                         and run["seconds"] >= deadline - .05)
        if not car["finished"] and not slow_deadline:
            failures.append(prefix + ": failed to finish without a justified slow-profile deadline")
        if run["name"] == "skill-stop-rejoin" and car["label"] == "P2":
            stopped = [r for r in samples if 45 <= float(r["time"]) < 57]
            if stopped and int(stopped[-1]["recoveries"]) != int(stopped[0]["recoveries"]):
                failures.append(prefix + ": intentional pedal release triggered an unwanted recovery")
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
            active = (float(r["pedal"]) > .05 and r["connected"] == "True" and
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
