#!/usr/bin/env python3
"""Validate hybrid steering input through a real PTY and the production Unity car."""
import os
from pathlib import Path
import subprocess
import time

root = Path(__file__).resolve().parents[1]
version = (root / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(":", 1)[1].strip()
unity = os.environ.get("UNITY_EDITOR", f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity")
output = Path(os.environ.get("RACING_STEERING_TEST_OUTPUT", str(root / "Logs/P1SteeringTest")))
output.mkdir(parents=True, exist_ok=True)
port_file, start_file = output / "port.txt", output / "start.txt"
wheel_port, wheel_start = output / "wheel-port.txt", output / "wheel-start.txt"
port_file.unlink(missing_ok=True)
start_file.unlink(missing_ok=True)
wheel_port.unlink(missing_ok=True)
wheel_start.unlink(missing_ok=True)
with (output / "sender.log").open("w") as log:
    sender = subprocess.Popen([
        str(root / "Builds/ESP32Simulator/esp32-simulator-cli"),
        "--frame", "78,0,0,-39,0.5,0", "--rate", "100", "--duration", "300",
        "--port-file", str(port_file), "--start-file", str(start_file),
    ], stdout=log, stderr=subprocess.STDOUT)
    wheel_sender = subprocess.Popen([
        str(root / "Builds/ESP32Simulator/esp32-simulator-cli"),
        "--scenario", str(root / "Tools/ESP32Simulator/Scenarios/05-p1-steering-only.json"), "--duration", "300",
        "--port-file", str(wheel_port), "--start-file", str(wheel_start),
    ], stdout=log, stderr=subprocess.STDOUT)
    try:
        deadline = time.monotonic() + 8
        while (not port_file.exists() or not wheel_port.exists()) and time.monotonic() < deadline and sender.poll() is None and wheel_sender.poll() is None:
            time.sleep(.02)
        if not port_file.exists() or not wheel_port.exists():
            raise RuntimeError("Simulator did not create a PTY; see " + str(output / "sender.log"))
        # Wait for Unity to finish importing before starting TEL, so an unread PTY cannot fill up.
        result = subprocess.run([
            unity, "-batchmode", "-projectPath", str(root), "-executeMethod",
            "P1SteeringTestValidation.RunAll", "-quit", "-logFile", str(output / "unity.log"),
        ], env=dict(os.environ, RACING_STEERING_TEST_PTY=port_file.read_text().strip(),
                    RACING_STEERING_TEST_START=str(start_file), RACING_WHEEL_ONLY_TEST_PTY=wheel_port.read_text().strip(),
                    RACING_WHEEL_ONLY_TEST_START=str(wheel_start)), timeout=300)
        lines = (output / "unity.log").read_text().splitlines()
        markers = [line for line in lines if line.startswith(("P1_STEERING_TEST_", "SKILL_GAMEPLAY_RULES_PASS", "RACE_FEATURE_VALIDATION_PASS"))]
        print("\n".join(markers), flush=True)
        if result.returncode or not any(line.startswith("P1_STEERING_TEST_PTY_PASS:") for line in markers):
            raise RuntimeError("Steering validation failed; see " + str(output / "unity.log"))
    finally:
        if sender.poll() is None:
            sender.terminate()
        sender.wait(timeout=10)
        if wheel_sender.poll() is None:
            wheel_sender.terminate()
        wheel_sender.wait(timeout=10)
