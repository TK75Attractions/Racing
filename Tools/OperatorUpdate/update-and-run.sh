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
child_pid=''
cancelled=false
message='更新に失敗しました。update.log と build.log を確認してください。'
status() { message=$1; if [ -n "${2:-}" ]; then printf '%s\n' "$2" > "$request/phase.tmp"; mv "$request/phase.tmp" "$request/phase.txt"; fi; printf '%s\n' "$message" > "$request/status.tmp"; mv "$request/status.tmp" "$request/status.txt"; echo "$message"; }
fail() { status "$1" failed; exit 1; }
cancel_job() { cancelled=true; status '更新をキャンセルしています。既存のRaceは終了しません。' cancelling; exit 130; }
check_cancel() { if [ -f "$request/decision" ] && [ "$(cat "$request/decision")" != launch ]; then cancel_job; fi; }
process_tree() {
    local process=$1 child
    for child in $(pgrep -P "$process" 2>/dev/null || true); do process_tree "$child"; done
    printf '%s\n' "$process"
}
stop_child() {
    if [ -z "$child_pid" ]; then return; fi
    local owned process
    owned=$(process_tree "$child_pid")
    for process in $owned; do kill -TERM "$process" 2>/dev/null || true; done
    for ((stop_attempt=0;stop_attempt<50;stop_attempt++)); do
        if ! kill -0 "$child_pid" 2>/dev/null; then break; fi
        sleep .1
    done
    for process in $owned; do kill -KILL "$process" 2>/dev/null || true; done
    wait "$child_pid" 2>/dev/null || true
    child_pid=''
}
run() {
    check_cancel
    "$@" & child_pid=$!
    while kill -0 "$child_pid" 2>/dev/null; do check_cancel; sleep .2; done
    local result=0
    wait "$child_pid" || result=$?
    child_pid=''
    check_cancel
    return "$result"
}
race_running() {
    local state
    state=$(ps -p "$1" -o stat= 2>/dev/null) || return 1
    [[ "$state" != *Z* ]]
}
stop_existing_players() {
    local process command candidates
    candidates="$parent_pid $(pgrep -u "$(id -u)" -f '/Contents/MacOS/[R]ace([[:space:]]|$)' 2>/dev/null || true)"
    for process in $candidates; do
        [[ "$process" =~ ^[0-9]+$ ]] && [ "$process" -gt 0 ] || continue
        command=$(ps -p "$process" -o command= 2>/dev/null) || continue
        # Stop game players, never the working Unity Editor or another application's process.
        case "$command" in *.app/Contents/MacOS/Race|*.app/Contents/MacOS/Race\ *) ;; *) continue;; esac
        race_running "$process" || continue
        kill -TERM "$process" 2>/dev/null || fail '既存のRaceを終了できません。アプリの状態を確認してください。'
        for ((stop_attempt=0;stop_attempt<100;stop_attempt++)); do
            race_running "$process" || break
            sleep .1
        done
        if race_running "$process"; then kill -KILL "$process" 2>/dev/null || fail '既存のRaceを終了できません。'; fi
    done
}
cleanup() {
    code=$?
    stop_child
    if [ "$checkout_created" = true ]; then
        cd "$repo"
        if ! git -C "$repo" worktree remove --force "$source_dir"; then
            # Unity helpers or Finder can leave files after Git unregisters the checkout.
            # This directory was created by this request; never prune other worktrees.
            for cleanup_attempt in 1 2 3; do
                if rm -rf -- "$source_dir"; then break; fi
                sleep 1
            done
            git -C "$repo" worktree remove --force "$source_dir" 2>/dev/null || true
            if [ -d "$source_dir" ]; then echo "一時フォルダーが残っています: $source_dir"; fi
        fi
    fi
    if [ "$cancelled" = true ]; then
        status '更新をキャンセルしました。既存のRaceはそのまま使用できます。' cancelled
        touch "$request/cancelled"
    elif [ "$code" -ne 0 ]; then
        printf '%s\n' "$message" > "$request/status.txt"; touch "$request/failed"
    fi
    if [ "$locked" = true ]; then rm -f "$lock/owner.pid" "$lock/request.txt"; rmdir "$lock"; fi
}
trap cleanup EXIT
trap cancel_job INT TERM
printf '2\n' > "$request/control-version"
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
status '最新mainを取得しています。既存のRaceはビルド完了まで使用できます。' fetching
run git fetch origin main:refs/remotes/origin/main || fail 'mainを取得できません。ネット接続とGit認証を確認してください。'
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
source_dir="$repo/Builds/OperatorSource/$(basename "$request")"
mkdir -p "$(dirname "$source_dir")"
status '取得したmainを別フォルダーで準備しています。' preparing
test ! -e "$source_dir" || fail '更新用フォルダーが既に存在します。別の要求で再実行してください。'
checkout_created=true
run git worktree add --detach "$source_dir" "$commit" || fail '更新用フォルダーを準備できません。ディスク容量とGit LFSを確認してください。'
cd "$source_dir"
status '音声プラグインをビルドしています。' native
run sh Native~/RaceAudio/build-macos.sh || fail '音声プラグインのビルドに失敗しました。update.logを確認してください。'
export RACING_PROJECT_PATH="$repo"
export RACING_OPERATOR_BUILD="$repo/Builds/Operator/$(basename "$request")/Race.app"
status 'Unityでビルドしています。完了まで既存のRaceは終了しません。' building
run "$unity" -batchmode -quit -projectPath "$source_dir" -executeMethod OperatorPlayerBuild.Build -logFile "$request/build.log" || fail 'Unityビルドに失敗しました。build.logを確認してください。'
test -x "$RACING_OPERATOR_BUILD/Contents/MacOS/Race" || fail 'ビルドの出力がありません。build.logを確認してください。'
check_cancel
if [ -f "$request/handoff.wait" ]; then
    status 'ビルドが完了しました。現在の試合終了を待っています。待機中もキャンセルできます。' waiting-race
    touch "$request/built"
    while [ ! -f "$request/launch.allowed" ]; do
        check_cancel
        if [ -f "$request/handoff.failed" ]; then fail "$(cat "$request/handoff.failed")"; fi
        sleep .1
    done
fi
check_cancel
# Cancellation and replacement compete for one atomic decision. Once replacement wins,
# the UI no longer offers cancellation and no late cancel can interrupt app handoff.
if ! (set -o noclobber; printf 'launch\n' > "$request/decision") 2>/dev/null; then
    check_cancel
    fail '起動への切り替えを確定できません。更新の進行状況を確認してください。'
fi
status 'ビルドが完了しました。既存のRaceを終了して新しいアプリへ切り替えます。' switching
touch "$request/ready"
if [ -f "$request/handoff.wait" ]; then
    for ((handoff_attempt=0;handoff_attempt<300;handoff_attempt++)); do
        if [ -f "$request/handoff.failed" ]; then fail "$(cat "$request/handoff.failed")"; fi
        [ ! -f "$request/released" ] || break
        sleep .1
    done
    test -f "$request/released" || fail '起動の引き継ぎを確認できません。管理画面の常駐プログラムを確認してください。'
fi
stop_existing_players
# An Editor must release Play-mode devices, but must never be terminated as a process.
if [ -f "$request/handoff.wait" ]; then
    for ((release_attempt=0;release_attempt<100;release_attempt++)); do
        if ! curl --silent --fail --max-time 1 http://127.0.0.1:8765/api/status >/dev/null; then break; fi
        sleep .1
    done
    if curl --silent --fail --max-time 1 http://127.0.0.1:8765/api/status >/dev/null; then
        fail 'UnityのPlayが機器を使用しています。EditorのPlayを停止してから再実行してください。ビルド済みアプリは保存されています。'
    fi
fi
status 'ビルド済みのRaceを起動しています。' launching
open -n "$RACING_OPERATOR_BUILD" || fail 'ビルド済みアプリを起動できません。Builds/Operatorを確認してください。'
status '更新したゲームを起動しました。' completed
echo 'OPERATOR_UPDATE_BUILD_RUN_PASS'
touch "$request/succeeded"
