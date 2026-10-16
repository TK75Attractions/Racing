#!/bin/sh
relay_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
export PATH="/usr/local/bin:/opt/homebrew/bin:/usr/local/share/dotnet:$PATH"
"$relay_dir/run.sh"
relay_status=$?
if [ "$relay_status" -ne 0 ]; then
  printf '\n起動できませんでした。上の案内を確認してEnterを押してください。\n'
  read -r relay_answer
fi
exit "$relay_status"
