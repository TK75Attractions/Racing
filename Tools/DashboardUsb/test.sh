#!/bin/sh
set -eu
usb_test_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
usb_test_root=$(CDPATH= cd -- "$usb_test_dir/../.." && pwd)
cd "$usb_test_root"
sh Tools/DashboardIPhone/test.sh
node --test 'Tests~/DashboardRelay/live-client.test.cjs' 'Tests~/Dashboard/dashboard-ui.test.cjs'
