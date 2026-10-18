# 運用エラーのWebhook通知

ブラウザーとは独立したPythonプロセスが、ゲームPCのUnityから状態を読み取り、Discord・Slack・汎用JSONのWebhookへ通知します。管理サイト・Firebase・Tailscaleの起動は不要です。Unityの `/api/status` は利用するため、ゲームPCでUnityをPlayするか、ダッシュボードブリッジを含むゲームを動かしてください。監視は読み取り専用で、レース操作・校正・再起動を自動実行しません。

## 最初の設定

Python 3.10以降を使います。追加パッケージは不要です。以下はリポジトリのルートで実行します。

```sh
cp Tools/RaceMonitor/monitor.example.json Tools/RaceMonitor/monitor.local.json
chmod 600 Tools/RaceMonitor/monitor.local.json
```

`monitor.local.json` の `webhook_url` に通知先のWebhook URLを設定します。Discordなら `webhook_format` は `discord` のままです。Slackは `slack`、独自の受信サーバーは `json` を指定します。通知先はHTTPSのみ対応します。URLは認証情報に相当するため、この設定ファイルはGitの追跡対象から除外しています。チャットや共有資料に貼る必要はありません。

試運転は通知を送信せず、検知した項目と通知予定の内容をターミナルへ表示します。既存の通知状態も書き換えません。

```sh
python3 Tools/RaceMonitor/monitor.py --dry-run
```

本番起動：

```sh
Tools/RaceMonitor/run.sh
```

Finderから `Tools/RaceMonitor/Start Monitor.command` を開くこともできます。ターミナルを開いたまま運用し、停止はControl+Cです。Windowsは `Tools/RaceMonitor/run.ps1` または `python Tools/RaceMonitor/monitor.py` で起動します。

## 何を通知するか

| 検知する状態 | 既定の判定 |
| --- | --- |
| ESP32のポート切断・新しい応答なし | 30秒継続 |
| ADS1115・IMUの異常、センサー正常性が未確認 | 30秒継続 |
| ハンドル基準・校正が未完了、入力配信の停止・不正・途絶 | 準備完了状態でも30秒継続 |
| 初期化・校正・接続復旧が終わらない | 180秒継続 |
| ESP32がFATAL状態 | 30秒継続 |
| 解析エラーの増加 | 直近60秒で5件以上の状態が30秒継続 |
| 装置・通信エラー、警告、操作失敗・応答なしの通知の増加 | 新しい履歴が直近60秒で5件以上の状態が30秒継続 |
| Unityの接続・状態更新の停止 | 一度状態を取得した後、30秒継続 |

状態は約5秒間隔で確認します。累積エラー数や取得時から残っている古い履歴だけで通知することはありません。同じ履歴IDを繰り返し取得しても再集計しません。キーボードのデバッグモードでは機器接続を要求しません。ペダルの0%は正常な入力として扱い、物理的な断線までは判定しません。

初回の通知には日本語の検知内容・確認事項・継続秒数が入ります。正常な状態では通知しません。同じ異常は30分ごとにまとめて再通知し、検出条件が30秒間解消すると復旧を通知します。複数の異常は1回の送信にまとめます。Unityから状態を取得できない間、機器が復旧したと判断することはありません。

起動時にUnityがまだ動いていなければ静かに待機します。Unityの未起動も通知対象にする運用では `arm_after_first_snapshot` を `false` にしてください。一度起動したUnityを意図的に停止する場合は、先に監視を停止・一時停止してください。

## 設定項目

`monitor.local.json` には必要な項目だけ指定できます。省略時は以下の値を使用します。

| 項目 | 既定値 | 内容 |
| --- | --- | --- |
| `webhook_format` | `discord` | `discord` / `slack` / `json` |
| `name` | `Racing` | 通知に表示する設置場所名 |
| `status_url` | `http://127.0.0.1:8765/api/status` | ゲームPCのループバックHTTPのみ |
| `poll_seconds` | `5` | 取得間隔 |
| `request_timeout_seconds` | `3` | HTTP通信のタイムアウト |
| `hold_seconds` | `30` | 通知までに必要な継続時間 |
| `recovery_seconds` | `30` | 復旧通知までの安定時間 |
| `reminder_seconds` | `1800` | 同じ異常の再通知間隔 |
| `stale_seconds` | `15` | Unityの状態データを古いと判断する秒数 |
| `error_threshold` | `5` | 解析エラー・エラー通知の頻度判定の件数 |
| `error_window_seconds` | `60` | 件数を集計する時間幅 |
| `preparation_seconds` | `180` | 初期化・校正・復旧処理の猶予 |
| `arm_after_first_snapshot` | `true` | 初回の状態取得まで通知を待つ |
| `state_file` | `monitor.state.json` | 通知済み状態の保存先 |
| `pause_file` | `monitor.paused` | このファイルがある間は一時停止 |

状態・一時停止ファイルの相対パスは設定ファイルのあるフォルダーを基準にします。状態保存は一時ファイルから置換し、再起動後も通知間隔を維持します。同じ状態ファイルを使う監視の二重起動を防ぎます。保存するのは生成済みの異常概要と時刻のみで、Webhook URLや機器の生データは含みません。

送信失敗時は5秒から最大5分まで待ち時間を延ばし、最新の状態を改めて判定してから再試行します。HTTP 429の場合は受信側の待ち時間も尊重します。400・401・403・404・410は設定確認が必要として、そのプロセスでの送信を止めます。URL・トークン・応答本文はエラーログに出しません。受信側へ送信済みなのに応答が失われた場合には、再試行で重複する可能性があります。

Discordは `wait=true` で送信結果を確認し、`allowed_mentions.parse=[]` で意図しないメンションを抑止します。仕様は[Discord Webhook公式資料](https://docs.discord.com/developers/resources/webhook#execute-webhook)と[Rate Limits](https://docs.discord.com/developers/topics/rate-limits)を参照しています。Discord・Slack側の通知設定も有効にしてください。

## Macのログイン時に自動起動する

設定済みのWebhook URLを使います。次のコマンドを実行したときだけ、ログインユーザーのLaunchAgentを登録します。

```sh
python3 Tools/RaceMonitor/install_service.py
```

監視本体と設定を `~/Library/Application Support/RacingMonitor/` にコピーし、ログイン時に起動します。元のワークツリーを削除しても継続して動作します。常駐中は手動版を同時に起動しないでください。設定変更はコピー先の `monitor.local.json` を編集してから、次のコマンドで再起動します。

```sh
launchctl kickstart -k "gui/$(id -u)/com.racing.monitor"
```

ログ：

- `~/Library/Logs/RacingMonitor/monitor.log`
- `~/Library/Logs/RacingMonitor/monitor-error.log`

常駐を解除するには、`python3 Tools/RaceMonitor/install_service.py --uninstall` を実行します。設定・状態・ログは残ります。監視コードを更新した場合はインストールを再実行してください。

## メンテナンス時の一時停止

手動起動の場合：

```sh
touch Tools/RaceMonitor/monitor.paused
# 作業完了後
rm Tools/RaceMonitor/monitor.paused
```

常駐版の場合は、`~/Library/Application Support/RacingMonitor/monitor.paused` を作成・削除してください。通常は5秒以内に反映します。一時停止中は状態取得も通知も行わず、継続時間と通知履歴をリセットします。再開後は再び初回の状態取得から監視します。

PCの停止・スリープ中やインターネット未接続では通知を送れません。この仕組みはゲームPC上で動くため、PC全体の停止まで外部から検知する監視は別途必要です。

## 検証

```sh
python3 -m unittest discover -s Tests~/RaceMonitor -v
```

30件のテストで継続時間、復旧、再発、履歴の重複排除、再起動、通信障害、HTTP 429、認証エラー、メンテナンス、二重起動、CLI試運転、常駐設定を確認しています。送信経路の統合テストはループバック上の模擬受信サーバーのみを使い、Discordなどの外部へメッセージを送りません。
