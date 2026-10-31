#!/bin/sh
set -eu
cd "$(dirname "$0")"
layout_temp=$(mktemp -d)
trap 'rm -rf "$layout_temp"' EXIT
layout_headers="${UNITY_PLUGIN_HEADERS:-/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/PluginAPI}"
clang++ -std=c++17 -Wall -Wextra -Werror test-layout.cpp -o "$layout_temp/selection"
"$layout_temp/selection"
clang++ -std=c++17 -O2 -fobjc-arc -I "$layout_headers" test-window-layout.mm \
  -framework AppKit -framework Metal -framework QuartzCore -framework CoreGraphics -framework ColorSync -o "$layout_temp/windows"
"$layout_temp/windows"
