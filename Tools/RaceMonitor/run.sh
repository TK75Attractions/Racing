#!/bin/sh
set -eu
monitor_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec python3 "$monitor_dir/monitor.py" "$@"
