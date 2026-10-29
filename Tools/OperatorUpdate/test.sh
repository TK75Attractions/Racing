#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
cd "$root/Tools/DashboardRelay"
dotnet build DashboardRelay.csproj --nologo -v:q
dotnet run --project "$root/Tests~/OperatorUpdate/HostTests.csproj"
cd "$root"
mkdir -p Temp/OperatorUpdate
mcs -define:UNITY_EDITOR -out:Temp/OperatorUpdate/RuntimeTests.exe 'Tests~/OperatorUpdate/RuntimeTests.cs' Assets/Managers/RaceOperatorUpdate.cs
mono Temp/OperatorUpdate/RuntimeTests.exe
python3 'Tests~/OperatorUpdate/test_update.py'
python3 'Tests~/OperatorUpdate/test_bridge_update.py'
node --test 'Tests~/Dashboard/dashboard-ui.test.cjs'
