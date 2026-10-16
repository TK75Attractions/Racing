#!/bin/sh
set -eu
relay_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
relay_root=$(CDPATH= cd -- "$relay_dir/../.." && pwd)
mkdir -p "$relay_root/Temp/DashboardRelay" "$relay_root/Artifacts/DashboardRealtime"
cd "$relay_dir"
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build DashboardRelay.csproj --nologo -v:q
cd "$relay_root"
mcs -langversion:latest -define:UNITY_STANDALONE_OSX -r:System.Web.Extensions \
  -out:Temp/DashboardRelay/BridgeHarness.exe 'Tests~/DashboardRelay/BridgeHarness.cs' \
  Assets/Managers/RaceDashboardBridge.cs Assets/Managers/Multiplayer/Core/SerialInputProtocol.cs \
  Assets/Managers/Multiplayer/Core/RgpHostSession.cs Assets/Managers/Multiplayer/Core/RgpSerialJournal.cs \
  Assets/Managers/Input/TwoPlayerSerialInputSource.cs Assets/Managers/Input/MacPseudoTerminalLineReader.cs \
  Assets/Managers/Input/Esp32SerialConfiguration.cs Assets/Managers/Input/DriveInputState.cs
node --test --test-concurrency=1 'Tests~/DashboardRelay/live-client.test.cjs' 'Tests~/DashboardRelay/quickstart.test.cjs' 'Tests~/DashboardRelay/relay.test.cjs'
