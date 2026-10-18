#!/bin/sh
set -eu
monitor_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec "$monitor_dir/run.sh" "$@"
