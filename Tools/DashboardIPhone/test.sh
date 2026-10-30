#!/bin/sh
set -eu
iphone_test_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
iphone_test_root=$(CDPATH= cd -- "$iphone_test_dir/../.." && pwd)
cd "$iphone_test_root"
mkdir -p Temp/UsbNativeModuleCache Artifacts/DashboardIPhone
xcrun swiftc -swift-version 5 -module-cache-path Temp/UsbNativeModuleCache \
  Tools/DashboardIPhone/RacingUsb/UsbTransport.swift Tools/DashboardIPhone/RacingUsb/LocalProxy.swift \
  'Tests~/DashboardIPhone/Harness.swift' -o Temp/iphone-usb-harness
cd "$iphone_test_root/Tests~/DashboardIPhone"
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet run --project NativeTests.csproj
