#!/bin/sh
set -eu
usb_start_test_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$usb_start_test_dir/../.."
mkdir -p Temp
mcs -langversion:latest -define:UNITY_EDITOR -r:System.IO.Compression.FileSystem \
  -out:Temp/UsbStartupTests.exe 'Tests~/DashboardIPhone/StartupHarness.cs' \
  Assets/Managers/RaceUsbAutoStart.cs Assets/Editor/RaceUsbPlayerPackaging.cs
mono Temp/UsbStartupTests.exe
