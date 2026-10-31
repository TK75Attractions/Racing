#!/bin/sh
set -eu
statistics_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$statistics_root/Tools/DashboardRelay"
dotnet build DashboardRelay.csproj --no-restore --nologo -v:q
dotnet run --project "$statistics_root/Tests~/PlayStatistics/StatisticsTests.csproj"
cd "$statistics_root"
sh Tools/Validate-Rgp.sh
node --test Tests~/Dashboard/*.test.cjs
