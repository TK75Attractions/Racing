#!/bin/sh
set -eu
usb_test_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
usb_test_root=$(CDPATH= cd -- "$usb_test_dir/../.." && pwd)
cd "$usb_test_root/Tests~/DashboardUsb"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# The real gateway uses ephemeral test ports and an isolated fake upstream, never 8765/8766.
dotnet run --project UsbTests.csproj -- "$usb_test_root/Artifacts/DashboardUsb/verification.json"
cd "$usb_test_root"
node --test 'Tests~/DashboardUsb/android.test.cjs' 'Tests~/DashboardRelay/live-client.test.cjs' 'Tests~/Dashboard/dashboard-ui.test.cjs'
