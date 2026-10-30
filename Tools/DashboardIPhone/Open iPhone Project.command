#!/bin/sh
set -eu
iphone_project_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
# Opens the prepared app project only. Signing, account login and device permissions stay with the operator.
/usr/bin/open -a Xcode "$iphone_project_dir/RacingUsb.xcodeproj"
