#!/bin/bash
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
# Reuse the guarded live-game handoff when the dashboard helper is running.
if /usr/bin/curl --silent --fail --max-time 2 http://127.0.0.1:8766/api/connection >/dev/null; then
    result=$(/usr/bin/curl --silent --fail --max-time 15 -H 'Origin: http://127.0.0.1:8766' -H 'Content-Type: application/json' \
        --data "{\"id\":\"$(/usr/bin/uuidgen)\"}" http://127.0.0.1:8766/api/operator-update)
    if [ $? -ne 0 ]; then
        printf '管理画面の更新サービスに接続できません。Dashboardの常駐プログラムを更新してください。\nEnterで閉じます。\n'
        read -r answer; exit 1
    fi
    printf '%s' "$result" | /usr/bin/plutil -extract message raw -o - -
    open 'http://127.0.0.1:8766/'
    if [ "$(printf '%s' "$result" | /usr/bin/plutil -extract ok raw -o - -)" != true ]; then
        printf '\nEnterで閉じます。\n'; read -r answer; exit 1
    fi
    exit 0
fi
if /usr/bin/curl --silent --fail --max-time 2 http://127.0.0.1:8765/api/status >/dev/null; then
    printf 'ゲームが起動中です。Dashboardを起動し、更新ボタンを使ってください。\nEnterで閉じます。\n'
    read -r answer; exit 1
fi
request="$repo/Logs/OperatorUpdate/$(date +%Y%m%d-%H%M%S)-$$"
printf '最新mainからビルドして起動します。Unityを開いておく必要はありません。\nログ: %s\n' "$request"
/bin/bash "$repo/Tools/OperatorUpdate/update-and-run.sh" "$repo" "" 0 "$request" &
update_pid=$!
previous=''
while kill -0 "$update_pid" 2>/dev/null; do
    if [ -f "$request/status.txt" ]; then
        current=$(cat "$request/status.txt")
        if [ "$current" != "$previous" ]; then printf '%s\n' "$current"; previous=$current; fi
    fi
    sleep 1
done
wait "$update_pid"
result=$?
if [ -f "$request/status.txt" ]; then cat "$request/status.txt"; fi
if [ "$result" -ne 0 ]; then
    printf '\n上の案内とログを確認してください。Enterで閉じます。\n'
    read -r answer
fi
exit "$result"
