#!/bin/sh
set -eu
wired_dashboard_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec sh "$wired_dashboard_dir/Start USB Dashboard.command" "$@"
