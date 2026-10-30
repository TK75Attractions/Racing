#!/bin/sh
set -eu
usb_pack_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
usb_pack_root=$(CDPATH= cd -- "$usb_pack_dir/../.." && pwd)
export PATH="/usr/local/bin:/opt/homebrew/bin:/usr/local/share/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
usb_pack_output=${1:-"$usb_pack_root/Assets/StreamingAssets/DashboardHelpers.zip"}
usb_pack_temp=$(mktemp -d "${TMPDIR:-/tmp}/racing-usb-pack.XXXXXX")
trap 'rm -rf "$usb_pack_temp"' EXIT HUP INT TERM
cd "$usb_pack_dir"
dotnet publish DashboardUsb.csproj -c Release --self-contained false -p:UseAppHost=false -o "$usb_pack_temp/Usb" --nologo -v:q
cd "$usb_pack_root/Tools/DashboardRelay"
dotnet publish DashboardRelay.csproj -c Release --self-contained false -p:UseAppHost=false -o "$usb_pack_temp/Relay" --nologo -v:q
python3 - "$usb_pack_temp" "$usb_pack_output" <<'PY'
import pathlib,sys,zipfile
source,output=map(pathlib.Path,sys.argv[1:]);output.parent.mkdir(parents=True,exist_ok=True)
temporary=output.with_suffix('.zip.tmp')
with zipfile.ZipFile(temporary,'w',compression=zipfile.ZIP_DEFLATED) as z:
    for f in sorted(source.rglob('*')):
        if f.is_file():z.write(f,f.relative_to(source))
temporary.replace(output)
print('USB_PLAYER_PAYLOAD_READY: '+str(output))
PY
