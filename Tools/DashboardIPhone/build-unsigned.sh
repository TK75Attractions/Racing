#!/bin/sh
# Compile device code without creating certificates, enabling Developer Mode, or installing an app.
set -eu
iphone_build_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
iphone_build_root=$(CDPATH= cd -- "$iphone_build_dir/../.." && pwd)
cd "$iphone_build_root"
iphone_sdk=$(xcrun --sdk iphoneos --show-sdk-path)
iphone_output="$iphone_build_root/Temp/DashboardIPhoneUnsigned.app"
mkdir -p "$iphone_output" Temp/UsbNativeModuleCache
SDKROOT="$iphone_sdk" xcrun --sdk iphoneos swiftc -swift-version 5 -parse-as-library -sdk "$iphone_sdk" \
 -target arm64-apple-ios16.0 -module-cache-path Temp/UsbNativeModuleCache \
 Tools/DashboardIPhone/RacingUsb/App.swift Tools/DashboardIPhone/RacingUsb/UsbTransport.swift \
 Tools/DashboardIPhone/RacingUsb/LocalProxy.swift -o "$iphone_output/RacingUsb"
python3 - "$iphone_build_dir/RacingUsb/Info.plist" "$iphone_output/Info.plist" <<'PY'
import plistlib,sys
with open(sys.argv[1],'rb') as f:p=plistlib.load(f)
p.update(CFBundleIdentifier='jp.tk75attractions.racing.usb',CFBundleExecutable='RacingUsb',CFBundleName='RacingUsb',MinimumOSVersion='16.0',UIDeviceFamily=[1,2])
with open(sys.argv[2],'wb') as f:plistlib.dump(p,f)
PY
printf 'IPHONE_USB_UNSIGNED_BUILD_PASS: %s\n' "$iphone_output"
printf 'This app still needs development signing and device setup before installation.\n'
