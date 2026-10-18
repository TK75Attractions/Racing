"""Compile the real Editor integration with Unity API stubs and exercise process ownership."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

from test_monitor import ROOT


@unittest.skipUnless(shutil.which("mcs") and shutil.which("mono") and os.name != "nt", "Mono lifecycle harness needs mcs and mono")
class UnityLauncherTests(unittest.TestCase):
    def test_editor_start_play_stop_reload_and_quit(self):
        with tempfile.TemporaryDirectory(prefix="race monitor ") as directory:
            base = Path(directory)
            executable = base / "launcher.exe"
            fake = base / "fake-python"
            fake.write_text('#!/bin/sh\nprintf "%s\\n" RACING_MONITOR_READY\nwhile IFS= read -r line; do\n  if [ "$line" = stop ]; then exit 0; fi\ndone\n')
            fake.chmod(0o755)
            config = base / "monitor.json"
            config.write_text("{}")
            result = subprocess.run(["mcs", "-langversion:latest", "-out:" + str(executable),
                                     str(ROOT / "Tests~/RaceMonitor/UnityLauncherHarness.cs"),
                                     str(ROOT / "Assets/Editor/RaceMonitorLauncher.cs")], capture_output=True, text=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            env = {**os.environ, "RACING_MONITOR_PYTHON": str(fake), "RACING_MONITOR_CONFIG": str(config)}
            result = subprocess.run(["mono", str(executable), str(ROOT)], env=env, capture_output=True, text=True, timeout=40)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("UNITY_MONITOR_LAUNCHER_PASS", result.stdout)
