#!/usr/bin/env python3
"""Launch only owned test processes; verify native broadcast on fresh and saved-history startup."""
import argparse
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import time

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('--app', type=Path, default=root / 'Builds/DisplayValidation/Race.app')
parser.add_argument('--history', type=Path, default=root / 'Logs/BroadcastPlaythrough/History')
parser.add_argument('--output', type=Path, default=root / 'Logs/BroadcastStartup')
args = parser.parse_args()
args.app = args.app.resolve()
args.history = args.history.resolve()
args.output = args.output.resolve()
args.output.mkdir(parents=True, exist_ok=True)
simulator = root / 'Builds/ESP32Simulator/esp32-simulator-cli'
port_file = args.output / 'sender.port'
port_file.unlink(missing_ok=True)
with (args.output / 'sender.log').open('w') as log:
    sender = subprocess.Popen([str(simulator), '--duration', '240', '--port-file', str(port_file), '--frame', '0,0,0,0,0,0'], stdout=log, stderr=log)
    try:
        deadline = time.monotonic() + 8
        port = ''
        while time.monotonic() < deadline and sender.poll() is None:
            candidate = port_file.read_text().strip() if port_file.exists() else ''
            if re.fullmatch(r'/dev/ttys[0-9]+', candidate):
                port = candidate
                break
            time.sleep(.05)
        if not port:
            raise RuntimeError('The owned ESP32 simulator did not publish a PTY')
        for name, history in [('Fresh', args.output / 'FreshHistory'), ('History', args.history)]:
            out = args.output / name
            out.mkdir(exist_ok=True)
            history.mkdir(parents=True, exist_ok=True)
            for file in ['report.json', 'broadcast.png', 'process-id.txt']:
                (out / file).unlink(missing_ok=True)
            command = ['open', '-n', '-W', '-a', str(args.app.resolve())]
            for key, value in {'RACING_PERFORMANCE_OUTPUT': out, 'RACING_BROADCAST_SMOKE': '1',
                               'RACING_BROADCAST_HISTORY': history.resolve(), 'RACING_BROADCAST_CAPTURE': out / 'broadcast.png'}.items():
                command += ['--env', f'{key}={value}']
            command += ['--args', '-logFile', str(out / 'player.log'), '-screen-fullscreen', '0',
                        '--esp32-simulator-port', port, '--esp32-simulator-divisor', '6.5']
            try:
                subprocess.run(command, check=True, timeout=100)
            except subprocess.TimeoutExpired:
                # This unique output folder was passed only to this owned launch.
                pid_file = out / 'process-id.txt'
                if pid_file.exists():
                    try:
                        os.kill(int(pid_file.read_text()), signal.SIGTERM)
                    except ProcessLookupError:
                        pass
                raise
            report = json.loads((out / 'report.json').read_text())
            print(name, json.dumps({key: report[key] for key in ['broadcastMode', 'broadcastFrames', 'broadcastRaces',
                  'broadcastWidth', 'broadcastHeight', 'broadcastBuiltIn', 'broadcastFillsScreen', 'errors']}, ensure_ascii=False), flush=True)
            if report['errors'] or not report['broadcastFillsScreen'] or report['broadcastFrames'] < 10:
                raise RuntimeError(f'{name}: broadcast startup failed')
            if not (out / 'broadcast.png').is_file():
                raise RuntimeError(f'{name}: no native Metal drawable was captured')
            expected = 'COURSE PV' if name == 'Fresh' else 'REPLAY'
            if report['broadcastMode'] != expected:
                raise RuntimeError(f'{name}: expected {expected}')
    finally:
        sender.terminate()
        try:
            sender.wait(timeout=3)
        except subprocess.TimeoutExpired:
            sender.kill()
            sender.wait()
