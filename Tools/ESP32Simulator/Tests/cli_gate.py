#!/usr/bin/env python3
"""Check the CLI's startup gate and signal cleanup over a real PTY."""
import os
from pathlib import Path
import re
import select
import subprocess
import tempfile
import time

root = Path(__file__).resolve().parents[3]
cli = root / "Builds/ESP32Simulator/esp32-simulator-cli"
with tempfile.TemporaryDirectory(prefix="esp32-cli-gate-") as directory:
    folder = Path(directory)
    gate = folder / "start"
    port_file = folder / "port"
    process = subprocess.Popen([str(cli), "--duration", "5", "--port-file", str(port_file),
                                "--start-file", str(gate), "--frame", "30,0.75,0,-15,0.5,0"],
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    descriptor = -1
    try:
        deadline = time.monotonic() + 4
        port = ""
        while time.monotonic() < deadline and process.poll() is None:
            if port_file.exists():
                candidate = port_file.read_text().strip()
                if re.fullmatch(r"/dev/ttys[0-9]+", candidate):
                    port = candidate
                    break
            time.sleep(.01)
        if not port:
            raise RuntimeError("CLI did not create its PTY")
        descriptor = os.open(port, os.O_RDONLY | os.O_NONBLOCK | os.O_NOCTTY)
        time.sleep(.15)
        early = b""
        while select.select([descriptor], [], [], 0)[0]:
            early += os.read(descriptor, 4096)
        if b"@TEL," in early:
            raise RuntimeError("CLI sent TEL before startup gate")
        if b"@SYS,0,0,HELLO,RGP,1.0," not in early:
            raise RuntimeError("CLI did not send HELLO before telemetry startup")
        gate.touch()
        received = b""
        deadline = time.monotonic() + 2
        while b",30.00,0.750,0.000,-15.00,0.500,0.000*" not in received and time.monotonic() < deadline:
            try:
                received += os.read(descriptor, 4096)
            except BlockingIOError:
                time.sleep(.01)
        if b",30.00,0.750,0.000,-15.00,0.500,0.000*" not in received:
            raise RuntimeError(f"Unexpected gate output: {received!r}")
        process.terminate()
        stdout, stderr = process.communicate(timeout=3)
        if process.returncode:
            raise RuntimeError(stderr + stdout)
    finally:
        if descriptor >= 0:
            os.close(descriptor)
        if process.poll() is None:
            process.terminate()
            process.communicate(timeout=3)
print("CLI_START_GATE_PASS: HELLO before gate, no early TEL, six-value frame, clean SIGTERM")
