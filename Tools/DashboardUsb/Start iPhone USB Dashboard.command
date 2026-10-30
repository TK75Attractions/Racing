#!/bin/sh
set -eu
iphone_dashboard_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec sh "$iphone_dashboard_dir/Start USB Dashboard.command" --iphone "$@"
