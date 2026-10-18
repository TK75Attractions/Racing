#!/bin/sh
set -eu
service_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$service_dir"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
exec dotnet run --project DashboardRelay.csproj --no-launch-profile -- --uninstall-service
