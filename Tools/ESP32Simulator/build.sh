#!/bin/bash
set -euo pipefail
simulator_source_dir="$(cd "$(dirname "$0")" && pwd)"
simulator_project_root="$(cd "$simulator_source_dir/../.." && pwd)"
simulator_output_dir="${1:-$simulator_project_root/Builds/ESP32Simulator}"
simulator_bundle="$simulator_output_dir/ESP32 Simulator.app"
simulator_cache="$simulator_project_root/Temp/ESP32Simulator/ModuleCache"
mkdir -p "$simulator_bundle/Contents/MacOS" "$simulator_bundle/Contents/Resources/Scenarios" "$simulator_cache"

simulator_common=(
  "$simulator_source_dir/Sources/RgpDevice.swift"
  "$simulator_source_dir/Sources/Protocol.swift"
  "$simulator_source_dir/Sources/PseudoTerminal.swift"
  "$simulator_source_dir/Sources/SimulatorEngine.swift"
)
simulator_flags=(-swift-version 5 -O -module-cache-path "$simulator_cache" -target "$(uname -m)-apple-macosx13.0")
xcrun swiftc "${simulator_flags[@]}" "${simulator_common[@]}" "$simulator_source_dir/Sources/SimulatorApp.swift" -o "$simulator_bundle/Contents/MacOS/ESP32Simulator"
xcrun swiftc "${simulator_flags[@]}" "${simulator_common[@]}" "$simulator_source_dir/Sources/SimulatorCLI.swift" -o "$simulator_output_dir/esp32-simulator-cli"
cp "$simulator_source_dir"/Scenarios/*.json "$simulator_bundle/Contents/Resources/Scenarios/"
cat > "$simulator_bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>ESP32Simulator</string>
<key>CFBundleIdentifier</key><string>jp.ennichi.racing.esp32-simulator</string>
<key>CFBundleName</key><string>ESP32 Serial Lab</string>
<key>CFBundleDisplayName</key><string>ESP32 Serial Lab</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleVersion</key><string>2</string>
<key>CFBundleShortVersionString</key><string>2.0</string>
<key>LSMinimumSystemVersion</key><string>13.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
codesign --force --sign - "$simulator_bundle"
printf 'App: %s\nCLI: %s\n' "$simulator_bundle" "$simulator_output_dir/esp32-simulator-cli"
