#!/bin/sh
set -eu
cd "$(dirname "$0")"
mkdir -p ../../Assets/Plugins/RaceAudio/macOS
clang++ -std=c++17 -O2 -fvisibility=hidden -dynamiclib -arch arm64 -arch x86_64 \
  -mmacosx-version-min=11.0 RaceAudio.cpp -o ../../Assets/Plugins/RaceAudio/macOS/libRaceAudio.dylib \
  -framework CoreFoundation -framework CoreAudio -framework AudioToolbox -lpthread
