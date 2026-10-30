#!/bin/sh
set -eu
export PATH="/usr/local/bin:/opt/homebrew/bin:$PATH"
usb_android_adb=${RACING_ADB:-$(command -v adb || true)}
if [ -z "$usb_android_adb" ] && [ -x "${ANDROID_HOME:-/nonexistent}/platform-tools/adb" ]; then
  usb_android_adb="$ANDROID_HOME/platform-tools/adb"
fi
if [ -z "$usb_android_adb" ] && [ -x "$HOME/Library/Android/sdk/platform-tools/adb" ]; then
  usb_android_adb="$HOME/Library/Android/sdk/platform-tools/adb"
fi
if [ -z "$usb_android_adb" ]; then
  echo 'Android SDK Platform-Tools (adb)が必要です。https://developer.android.com/tools/releases/platform-tools'
  exit 1
fi
if ! usb_android_serial=$("$usb_android_adb" -d get-serialno 2>/dev/null) ||
   [ "$("$usb_android_adb" -d get-state 2>/dev/null || true)" != 'device' ]; then
  echo 'AndroidをUSBで1台接続し、USBデバッグとこのPCの許可を端末で確認してください。'
  exit 1
fi
# -d selects a physical USB device, never a Wi-Fi ADB connection. Freeze that identity.
usb_android_routes=$("$usb_android_adb" -s "$usb_android_serial" reverse --list)
if ! printf '%s\n' "$usb_android_routes" | awk '$2 == "tcp:8766" && $3 == "tcp:8766" { found=1 } END { exit !found }'; then
  # Do not replace another tool's reverse mapping or remove existing mappings.
  "$usb_android_adb" -s "$usb_android_serial" reverse --no-rebind tcp:8766 tcp:8766
fi
printf '\nUSB直通を準備しました。接続したAndroidのChromeで開いてください:\nhttp://127.0.0.1:8766/\n\n'
printf '通常のDashboard中継は起動したまま使います。USBを抜くとこの接続は終了します。\n'
