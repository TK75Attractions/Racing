#!/bin/bash
set -euo pipefail
# Arguments are data; no eval or caller-provided command is executed.
repo=$1
unity=$2
parent_pid=$3
request=$4
mkdir -p "$request"
exec >> "$request/update.log" 2>&1
original_plugin=''
cleanup() {
    code=$?
    if [ -n "$original_plugin" ]; then cp "$original_plugin" "$repo/Assets/Plugins/RaceAudio/macOS/libRaceAudio.dylib"; fi
    if [ "$code" -ne 0 ]; then echo "更新失敗 ($code)。update.log と build.log を確認してください。"; touch "$request/failed"; fi
}
trap cleanup EXIT
export PATH="$PATH:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
cd "$repo"
preflight() {
    test -x "$unity" || { echo 'Unity Editor が見つかりません。RACING_UNITY_PATHを指定してください。'; return 1; }
    test -z "$(git status --porcelain)" || { echo '未コミットの変更があります。変更を保存・コミットしてから更新してください。'; return 1; }
    test -z "$(git ls-files -u)" || return 1
    git_dir=$(git rev-parse --absolute-git-dir)
    for marker in rebase-merge rebase-apply MERGE_HEAD CHERRY_PICK_HEAD; do
        test ! -e "$git_dir/$marker" || { echo 'Gitの操作が進行中です。'; return 1; }
    done
}
preflight
git fetch origin main:refs/remotes/origin/main
git merge-base --is-ancestor main origin/main || { echo 'ローカルmainが分岐しています。自動更新を停止しました。'; exit 1; }
if [ "${5:-}" = '--check' ]; then echo 'OPERATOR_UPDATE_PREFLIGHT_PASS'; exit 0; fi
# Keep the game open on preflight failure. Only this marker authorizes its shutdown.
touch "$request/ready"
for ((i=0; i<120; i++)); do
    if ! kill -0 "$parent_pid" 2>/dev/null; then break; fi
    sleep 1
done
if kill -0 "$parent_pid" 2>/dev/null; then echo 'Unityの終了を待機中にタイムアウトしました。'; exit 1; fi
preflight
git switch main
git merge --ff-only origin/main
original_plugin="$request/original-libRaceAudio.dylib"
cp Assets/Plugins/RaceAudio/macOS/libRaceAudio.dylib "$original_plugin"
sh Native~/RaceAudio/build-macos.sh
# Build each version in its own directory; a failed build leaves the previous app usable.
export RACING_OPERATOR_BUILD="$repo/Builds/Operator/$(date +%Y%m%d-%H%M%S)-$$/Race.app"
"$unity" -batchmode -quit -projectPath "$repo" -executeMethod OperatorPlayerBuild.Build -logFile "$request/build.log"
test -d "$RACING_OPERATOR_BUILD" || { echo 'ビルドの出力がありません。'; exit 1; }
open "$RACING_OPERATOR_BUILD"
echo 'OPERATOR_UPDATE_BUILD_RUN_PASS'
touch "$request/succeeded"
