#!/usr/bin/env python3
"""Summarize recorded Unity playtests using only the Python standard library."""
import csv
import json
import sys
from collections import Counter, defaultdict
from html import escape
from pathlib import Path


def analyze(root):
    report = json.loads((root / "report.json").read_text())
    runs = report["runs"]
    fields = ["run", "seed", "items", "termination", "seconds", "car", "finished", "rank",
              "laps", "progress_m", "mean_speed_mps", "max_stall_s", "off_course_s",
              "vehicle_contacts", "environment_contacts", "items_used", "afflictions", "manual_resets", "cpu_recoveries"]
    with (root / "summary.csv").open("w", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(fields)
        for run in runs:
            for car in run["cars"]:
                writer.writerow([run["name"], run["seed"], run["itemsEnabled"], run["termination"],
                                 round(run["seconds"], 2), car["label"], car["finished"], car["position"], car["laps"],
                                 round(car["progress"], 2), round(car["meanSpeed"], 2), round(car["maxStall"], 2),
                                 round(car["offCourseSeconds"], 2), car["vehicleContacts"], car["environmentContacts"],
                                 car["itemsUsed"], car["afflictions"], car["manualResets"], car["cpuRecoveries"]])
    colors = {"P1": "#2563eb", "P2": "#db2777", "CPU 1": "#d97706", "CPU 2": "#059669"}
    width, panel_height = 1100, 230
    svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{80 + panel_height * len(runs)}" viewBox="0 0 {width} {80 + panel_height * len(runs)}">',
           '<rect width="100%" height="100%" fill="#f8fafc"/>',
           '<g font-family="sans-serif" fill="#0f172a">',
           '<text x="55" y="30" font-size="22">Balance playtest — validated race progress</text>',
           '<text x="55" y="54" font-size="13">Horizontal: simulation seconds (0–360). Vertical: race progress in meters. Flat lines indicate lost progress.</text>',
           '<text x="750" y="30" font-size="14"><tspan fill="#2563eb">P1  </tspan><tspan fill="#db2777">P2  </tspan><tspan fill="#d97706">CPU 1  </tspan><tspan fill="#059669">CPU 2</tspan></text>']
    stats = []
    for index, run in enumerate(runs):
        top = 88 + index * panel_height
        rows_by_car = defaultdict(list)
        trace_path = root / run["name"] / "trace.csv"
        if trace_path.exists():
            with trace_path.open() as stream:
                for row in csv.DictReader(stream):
                    rows_by_car[row["car"]].append(row)
        svg.append(f'<text x="55" y="{top}" font-size="15">{escape(run["name"])} | {escape(run["termination"])} | {run["seconds"]:.1f}s</text>')
        x0, y0, w, h = 65, top + 20, 960, 155
        ymax = max(report["courseLength"] * run["goalLaps"], 1)
        for t in (0, 120, 240, 360):
            x = x0 + w * t / 360
            svg.append(f'<path d="M{x},{y0}v{h}" stroke="#cbd5e1"/><text x="{x}" y="{y0+h+18}" font-size="11">{t}s</text>')
        for lap in range(run["goalLaps"] + 1):
            y = y0 + h * (1 - lap / run["goalLaps"])
            svg.append(f'<path d="M{x0},{y}h{w}" stroke="#cbd5e1"/><text x="4" y="{y+4}" font-size="11">{lap} lap</text>')
        for label, rows in rows_by_car.items():
            points = []
            for row in rows[::5]:
                x = x0 + w * float(row["time"]) / 360
                y = y0 + h * (1 - max(0, float(row["progress"])) / ymax)
                points.append(f"{x:.1f},{y:.1f}")
            color = colors.get(label, "#64748b")
            svg.append(f'<polyline fill="none" stroke="{color}" stroke-width="1.8" points="{" ".join(points)}"/>')
        events_path = root / run["name"] / "events.csv"
        counter = Counter()
        details = Counter()
        if events_path.exists():
            with events_path.open() as stream:
                for event in csv.DictReader(stream):
                    counter[(event["car"], event["event"])] += 1
                    if event["event"] in ("item", "afflicted", "environment-contact"):
                        details[(event["car"], event["event"], event["detail"])] += 1
        stats.append({"run": run["name"], "events": [{"car": car, "event": event, "count": count} for (car, event), count in counter.items()],
                      "details": [{"car": car, "event": event, "detail": detail, "count": count} for (car, event, detail), count in details.items()]})
        unfinished = "racing" if run["termination"] == "observation-window-ended" else "DNF"
        legend = "   ".join(f'{c["label"]}: {"finish" if c["finished"] else unfinished}, {c["laps"]} laps, max stall {c["maxStall"]:.1f}s' for c in run["cars"])
        svg.append(f'<text x="55" y="{y0+h+40}" font-size="11">{escape(legend)}</text>')
    svg.append('</g></svg>')
    (root / "progress.svg").write_text("\n".join(svg))
    (root / "event-summary.json").write_text(json.dumps(stats, indent=2, ensure_ascii=False) + "\n")
    for run in runs:
        unfinished = "RACING" if run["termination"] == "observation-window-ended" else "DNF"
        print(run["name"], run["termination"], round(run["seconds"], 2),
              "; ".join(f'{c["label"]}:{"FIN" if c["finished"] else unfinished} lap={c["laps"]} progress={c["progress"]:.1f} stall={c["maxStall"]:.1f} items={c["itemsUsed"]}' for c in run["cars"]))


if __name__ == "__main__":
    analyze(Path(sys.argv[1] if len(sys.argv) > 1 else "Artifacts/BalancePlaytest"))
