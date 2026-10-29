#!/bin/bash
set -euo pipefail
# Run against a detached, owned checkout. The operator's scene and branch stay intact.
repo=$(cd "$1" && pwd)
unity=${2:-}
parent_pid=${3:-0}
request=${4:-"$repo/Logs/OperatorUpdate/$(date +%Y%m%d-%H%M%S)-$$"}
mkdir -p "$request"
request=$(cd "$request" && pwd)
exec >> "$request/update.log" 2>&1
export PATH="$PATH:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
lock="$repo/Logs/OperatorUpdate.lock"
source_dir=''
checkout_created=false
locked=false
message='更新に失敗しました。update.log と build.log を確認してください。'
status() { message=$1; printf '%s\n' "$message" > "$request/status.tmp"; mv "$request/status.tmp" "$request/status.txt"; echo "$message"; }
fail() { status "$1"; exit 1; }
cleanup() {
    code=$?
    if [ "$checkout_created" = true ]; then git -C "$repo" worktree remove --force "$source_dir" || true; fi
    if [ "$code" -ne 0 ]; then printf '%s\n' "$message" > "$request/status.txt"; touch "$request/failed"; fi
    if [ "$locked" = true ]; then rm -f "$lock/owner.pid" "$lock/request.txt"; rmdir "$lock"; fi
}
trap cleanup EXIT
cd "$repo"
if [ -z "$unity" ]; then
    unity=${RACING_UNITY_PATH:-}
    if [ -z "$unity" ]; then
        version=$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt | tr -d '\r')
        unity="/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity"
    fi
fi
case "$unity" in *.app) unity="$unity/Contents/MacOS/Unity";; esac
test -x "$unity" || fail 'Unity Editor が見つかりません。RACING_UNITY_PATHを指定してください。'
mkdir -p "$repo/Logs"
if ! mkdir "$lock" 2>/dev/null; then
    fail '別の更新が実行中です。Logs/OperatorUpdate の進行状況を確認してください。'
fi
locked=true
printf '%s\n' "$$" > "$lock/owner.pid"
printf '%s\n' "$request" > "$lock/request.txt"
status '最新mainを取得しています。作業中の変更は保持します。'
git fetch origin main:refs/remotes/origin/main || fail 'mainを取得できません。ネット接続とGit認証を確認してください。'
commit=$(git rev-parse --verify refs/remotes/origin/main)
printf '%s\n' "$commit" > "$request/commit.txt"
# Catch a known build blocker before asking a running player to quit.
asset_kb=$(du -sk "$repo/Assets" | awk '{print $1}')
free_kb=$(df -Pk "$repo" | awk 'NR==2 {print $4}')
required_kb=$((asset_kb * 3 + 65536))
if [ "$free_kb" -lt "$required_kb" ]; then
    fail "ビルド用の空き容量が不足しています。目安 $((required_kb / 1024)) MB以上、現在 $((free_kb / 1024)) MB。空きを増やして再実行してください。"
fi
if [ "${5:-}" = '--check' ]; then status 'OPERATOR_UPDATE_PREFLIGHT_PASS'; exit 0; fi
touch "$request/ready"
if [ "$parent_pid" -gt 0 ]; then
    for ((i=0; i<120; i++)); do
        if ! kill -0 "$parent_pid" 2>/dev/null; then break; fi
        sleep 1
    done
    if kill -0 "$parent_pid" 2>/dev/null; then fail 'Unityの終了を待機中にタイムアウトしました。'; fi
fi
source_dir="$repo/Builds/OperatorSource/$(basename "$request")"
mkdir -p "$(dirname "$source_dir")"
status '取得したmainを別フォルダーで準備しています。'
git worktree add --detach "$source_dir" "$commit" || fail '更新用フォルダーを準備できません。ディスク容量とGit LFSを確認してください。'
checkout_created=true
cd "$source_dir"
status '音声プラグインをビルドしています。'
sh Native~/RaceAudio/build-macos.sh || fail '音声プラグインのビルドに失敗しました。update.logを確認してください。'
export RACING_PROJECT_PATH="$repo"
export RACING_OPERATOR_BUILD="$repo/Builds/Operator/$(basename "$request")/Race.app"
status 'Unityでビルドしています。Unityを手動で起動する必要はありません。'
"$unity" -batchmode -quit -projectPath "$source_dir" -executeMethod OperatorPlayerBuild.Build -logFile "$request/build.log" || fail 'Unityビルドに失敗しました。build.logを確認してください。'
test -d "$RACING_OPERATOR_BUILD" || fail 'ビルドの出力がありません。build.logを確認してください。'
open "$RACING_OPERATOR_BUILD" || fail 'ビルド済みアプリを起動できません。Builds/Operatorを確認してください。'
status '更新したゲームを起動しました。'
echo 'OPERATOR_UPDATE_BUILD_RUN_PASS'
touch "$request/succeeded"
