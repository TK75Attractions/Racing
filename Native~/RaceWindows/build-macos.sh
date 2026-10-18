#!/bin/sh
set -eu
cd "$(dirname "$0")"
unity_plugin_headers="${UNITY_PLUGIN_HEADERS:-/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/PluginAPI}"
mkdir -p ../../Assets/Plugins/RaceWindows/macOS
clang++ -std=c++17 -O2 -fobjc-arc -fvisibility=hidden -dynamiclib -arch arm64 -arch x86_64 \
  -mmacosx-version-min=11.0 -I "$unity_plugin_headers" RaceWindows.mm \
  -o ../../Assets/Plugins/RaceWindows/macOS/libRaceWindows.dylib \
  -framework AppKit -framework Metal -framework QuartzCore
