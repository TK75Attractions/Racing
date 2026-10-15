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
                                "--start-file", str(gate), "--frame", "0.75,30,0.5,-15"],
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
        if select.select([descriptor], [], [], .2)[0]:
            raise RuntimeError("CLI transmitted before startup gate")
        gate.touch()
        if not select.select([descriptor], [], [], 2)[0]:
            raise RuntimeError("CLI did not transmit after startup gate")
        received = b""
        deadline = time.monotonic() + 1
        while b"\n" not in received and time.monotonic() < deadline:
            try:
                received += os.read(descriptor, 4096)
            except BlockingIOError:
                time.sleep(.01)
        if b"0.7500,195.0000,0.5000,-97.5000\n" not in received:
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
print("CLI_START_GATE_PASS: no early bytes, correct frame, clean SIGTERM")
