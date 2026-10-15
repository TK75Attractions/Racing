#!/usr/bin/env python3
"""Run the Swift sender against the production Unity receiver on Unity's Mono."""
import os
import re
import shutil
from pathlib import Path
import subprocess
import sys
import time

root = Path(__file__).resolve().parents[3]
tool = root / "Tools/ESP32Simulator"
output = root / "Temp/ESP32Simulator/Tests"
output.mkdir(parents=True, exist_ok=True)
version = (root / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(":", 1)[1].strip()
contents = Path(os.environ.get("UNITY_CONTENTS", f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents"))
mono_root = contents / "Resources/Scripting/MonoBleedingEdge"
mono = mono_root / "bin/mono"
compiler = mono_root / "lib/mono/4.5/mcs.exe"
if not compiler.exists():
    compiler = mono_root / "lib/mono/4.5/csc.exe"
probe = output / "ReceiverProbe.exe"
sources = [tool / "Tests/UnityStubs.cs", tool / "Tests/ReceiverProbe.cs",
           root / "Assets/Managers/Input/MacPseudoTerminalLineReader.cs",
           root / "Assets/Managers/Input/Esp32SerialConfiguration.cs",
           root / "Assets/Managers/Input/TwoPlayerSerialInputSource.cs",
           root / "Assets/Managers/Input/DriveInputState.cs",
           root / "Assets/Managers/Multiplayer/Core/SerialInputProtocol.cs"]
env = dict(os.environ, DYLD_LIBRARY_PATH=str(contents / "Frameworks"))
subprocess.run([str(mono), str(compiler), "-langversion:latest", "-define:UNITY_STANDALONE_OSX",
                f"-out:{probe}", *map(str, sources)], check=True, env=env)
nunit = next((root / "Library/PackageCache").glob("com.unity.ext.nunit*/net40/unity-custom/nunit.framework.dll"))
shutil.copy2(nunit, output / "nunit.framework.dll")
regression = output / "ProtocolRegression.exe"
subprocess.run([str(mono), str(compiler), "-langversion:latest", f"-r:{nunit}", f"-out:{regression}",
                str(tool / "Tests/ProtocolRegression.cs"), str(root / "Assets/Tests/EditMode/SerialInputProtocolTests.cs"),
                str(root / "Assets/Managers/Multiplayer/Core/SerialInputProtocol.cs")], check=True, env=env)
subprocess.run([str(mono), str(regression)], check=True, env=env)
for mode, scenario, duration in [("timeout", "02-input-and-timeout.json", 13.5),
                                 ("fragments", "03-boot-and-fragments.json", 8)]:
    port_file = output / f"{mode}.port"
    port_file.unlink(missing_ok=True)
    sender = subprocess.Popen([str(root / "Builds/ESP32Simulator/esp32-simulator-cli"),
                               "--scenario", str(tool / "Scenarios" / scenario),
                               "--duration", str(duration), "--port-file", str(port_file)],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
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
            time.sleep(.01)
        if not port:
            raise RuntimeError("Sender did not create a port: " + str(sender.communicate()))
        result = subprocess.run([str(mono), str(probe), port, mode],
                                env=env, text=True, capture_output=True, timeout=18)
        print(result.stdout, end="")
        if result.returncode:
            print(result.stderr, file=sys.stderr)
            raise RuntimeError(f"Receiver failed: {mode}")
        stdout, stderr = sender.communicate(timeout=3)
        print(stdout, end="")
        if sender.returncode:
            raise RuntimeError(stderr)
    finally:
        if sender.poll() is None:
            sender.terminate()
            sender.communicate(timeout=5)
print("MONO_INTEGRATION_TESTS_PASS")
