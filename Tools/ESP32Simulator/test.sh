#!/bin/bash
set -euo pipefail
simulator_source_dir="$(cd "$(dirname "$0")" && pwd)"
simulator_project_root="$(cd "$simulator_source_dir/../.." && pwd)"
simulator_test_dir="$simulator_project_root/Temp/ESP32Simulator/Tests"
mkdir -p "$simulator_test_dir" "$simulator_test_dir/ModuleCache"
xcrun swiftc -swift-version 5 -module-cache-path "$simulator_test_dir/ModuleCache" \
  "$simulator_source_dir/Sources/Protocol.swift" \
  "$simulator_source_dir/Sources/PseudoTerminal.swift" \
  "$simulator_source_dir/Sources/SimulatorEngine.swift" \
  "$simulator_source_dir/Tests/CoreTests.swift" -o "$simulator_test_dir/core-tests"
"$simulator_test_dir/core-tests"
python3 "$simulator_source_dir/Tests/cli_gate.py"
python3 "$simulator_source_dir/Tests/integration.py"
