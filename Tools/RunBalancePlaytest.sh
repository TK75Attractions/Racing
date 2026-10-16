#!/bin/bash
set -euo pipefail
project="$(cd "$(dirname "$0")/.." && pwd)"
unity="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity}"
export RACING_BALANCE_OUTPUT="${RACING_BALANCE_OUTPUT:-$project/Artifacts/BalancePlaytest}"
export RACING_BALANCE_REVISION="$(git -C "$project" rev-parse HEAD)"
mkdir -p "$RACING_BALANCE_OUTPUT"
# Showroom initialization needs graphics. The asynchronous agent owns termination.
"$unity" -batchmode -projectPath "$project" -executeMethod BalancePlaytestValidation.RunBatch \
  -logFile "$RACING_BALANCE_OUTPUT/unity.log"
python3 "$project/Tools/AnalyzeBalancePlaytest.py" "$RACING_BALANCE_OUTPUT"
