#!/usr/bin/env python3
"""Optional headless smoke check for Racing > Build > ESP32 Simulator Player.

Run with Unity Editor out of Play mode and no other Race Player running.
"""
import json
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.request

root = Path(__file__).resolve().parents[3]
output = root / "Artifacts/ESP32Simulator"
output.mkdir(parents=True, exist_ok=True)
scratch = root / "Temp/ESP32Simulator/Standalone"
scratch.mkdir(parents=True, exist_ok=True)
player = root / "Builds/ESP32Simulator/Player/Race.app/Contents/MacOS/Race"
if not player.exists():
    raise SystemExit("Build Racing > Build > ESP32 Simulator Player first.")
try:
    urllib.request.urlopen("http://127.0.0.1:8765/api/status", timeout=1)
except (urllib.error.URLError, TimeoutError):
    pass
else:
    raise SystemExit("Another Unity dashboard is running. Stop Editor Play mode / other Race Player first.")

scenario = json.loads((root / "Tools/ESP32Simulator/Scenarios/02-input-and-timeout.json").read_text())
scenario_file = scratch / "smoke.json"
scenario_file.write_text(json.dumps(scenario))
port_file = scratch / "sender.port"
port_file.unlink(missing_ok=True)
start_file = scratch / "start"
start_file.unlink(missing_ok=True)
sender = subprocess.Popen([str(root / "Builds/ESP32Simulator/esp32-simulator-cli"), "--duration", "45",
                           "--scenario", str(scenario_file), "--port-file", str(port_file), "--start-file", str(start_file)],
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
game = None
checks = dict(connected=False, numeric=False, independent=False, atomicStop=False, resumed=False, silence=False, recovered=False)
last = None
last_network_error = ""
try:
    deadline = time.monotonic() + 5
    port = ""
    while time.monotonic() < deadline and sender.poll() is None:
        try:
            candidate = port_file.read_text().strip()
            if re.fullmatch(r"/dev/ttys[0-9]+", candidate):
                port = candidate
                break
        except FileNotFoundError:
            pass
        time.sleep(.02)
    if not port:
        raise RuntimeError("Sender did not publish its PTY.")
    game = subprocess.Popen([str(player), "-batchmode", "-nographics", "-logFile", str(output / "standalone-player.log"),
                             "--esp32-simulator-port", port, "--esp32-simulator-divisor", "6.5"],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        if game.poll() is not None:
            raise RuntimeError(f"Race Player exited: {game.returncode}")
        try:
            with urllib.request.urlopen("http://127.0.0.1:8765/api/status", timeout=1) as response:
                ready = json.load(response)
            if ready["serial"]["port"] == port:
                start_file.touch()
                break
        except (urllib.error.URLError, TimeoutError):
            pass
        time.sleep(.1)
    if not start_file.exists():
        raise RuntimeError("Race Player did not become ready within 45 seconds.")
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        if game.poll() is not None:
            raise RuntimeError(f"Race Player exited: {game.returncode}; see standalone-player.log")
        try:
            with urllib.request.urlopen("http://127.0.0.1:8765/api/status", timeout=1) as response:
                status = json.load(response)
            if status.get("inputMode") != "Serial Simulator P1/P2 (macOS PTY)" or status["serial"]["port"] != port:
                raise RuntimeError("Dashboard does not belong to this simulator session.")
            last = status
            last_network_error = ""
            p1, p2 = status["players"]
            serial = status["serial"]
            checks["connected"] |= p1["connected"] and p2["connected"]
            checks["numeric"] |= abs(p1["pedal"]-.75)<.001 and abs(p2["pedal"]-.5)<.001 and p1["connected"] and p2["connected"]
            checks["independent"] |= abs(p1["pedal"]-.4)<.001 and abs(p2["pedal"]+.55)<.001
            checks["atomicStop"] |= not p1["connected"] and not p2["connected"] and any(
                ",STATE,WAIT_ANGLE*" in line["line"] for line in serial["lines"])
            checks["resumed"] |= checks["atomicStop"] and p1["connected"] and p2["connected"]
            checks["silence"] |= checks["resumed"] and not p1["connected"] and not p2["connected"] and serial["portOpen"] and serial["lastResult"]=="STALE"
            checks["recovered"] |= checks["silence"] and p1["connected"] and p2["connected"]
            if all(checks.values()):
                break
        except (urllib.error.URLError, TimeoutError) as error:
            last_network_error = str(error)
        time.sleep(.1)
    result = {"checks": checks, "status": last, "lastNetworkError": last_network_error}
    (output / "standalone-smoke.json").write_text(json.dumps(result, ensure_ascii=False, indent=2))
    if not all(checks.values()):
        raise RuntimeError(f"Missing checks: {checks}; network: {last_network_error}")
    print("STANDALONE_SIMULATOR_PASS: " + json.dumps(checks))
finally:
    for process in [game, sender]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=8)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=3)
