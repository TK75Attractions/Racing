#!/bin/sh
set -eu
usb_dashboard_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
export PATH="/usr/local/bin:/opt/homebrew/bin:/usr/local/share/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$usb_dashboard_dir"
if ! command -v dotnet >/dev/null 2>&1; then
  echo '.NET 10 SDKが必要です: https://dotnet.microsoft.com/download/dotnet/10.0'
  read -r usb_dashboard_answer
  exit 1
fi
# Builds only this helper. Never invokes the game updater or restarts the normal dashboard.
dotnet run --project "$usb_dashboard_dir/DashboardUsb.csproj" --no-launch-profile -- "$@"
