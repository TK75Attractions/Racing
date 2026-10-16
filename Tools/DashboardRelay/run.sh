#!/bin/sh
set -eu
relay_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$relay_dir"
if [ -f relay.env ]; then
  set -a
  . ./relay.env
  set +a
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET 10 SDKをインストールしてください: https://dotnet.microsoft.com/download/dotnet/10.0"
  exit 1
fi
exec dotnet run --project DashboardRelay.csproj --no-launch-profile -- --auto "$@"
