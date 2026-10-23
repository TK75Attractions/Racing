# 実機起動の反復確認（2026-10-23）

ESP32のUSB接続、P1/P2のIMU、ADS1115と両ペダルで確認。シミュレーターや管理画面の入力代行は使用していない。P2ハンドルは軸に固定せず床に置いているため、その角度値を中央異常とは判定しない。

## 再現した問題と修正

- 校正完了直後の `ANGLE_ZERO` が `ANGLE_REFERENCE_INVALID` で失敗した。装置は校正後も約5秒間フィルターを安定させている。Unityは6秒間自動復帰を待ち、なお基準がない場合のみ一度リセットを要求する。READYと新TELの条件は緩和しない。
- 片方のIMUが初回初期化に失敗して約1秒後に認識された際、先に見つかった片方だけで校正対象が確定し、後から見つかった側が未校正のまま停止した。通常2人モードでは個別情報を使って両ハンドルの認識を待つ。P1単体モードと個別情報を持たない旧装置の扱いは維持する。
- 装置が自動で基準を確定しても、失敗した基準リセットが管理画面で未解決のまま残った。現在の基準・校正・READY・新しい正常入力を確認して復旧済みとし、コマンドが失敗した履歴自体は保持する。他のコマンド失敗を一律に解決扱いにはしない。
- 車選択画面の通信待ち案内に使うフォントに「通信」「確認してください」の一部がなく、文字欠けと大量の警告が出た。案内ラベルには同じ入力案内の文字を含む既存のHUDフォントを使う。共通案内文に含まれる日本語が全て収録されていることを確認した。

## 実機結果

修正前のUSB入力初期化3回では、2回が約21.5秒でREADYになったものの基準リセットが失敗し、1回はIMU認識遅れにより未校正のWAIT_ANGLEに留まった。

Editorを再起動し、起動順序と警告表示を修正した状態でPlay開始を3回反復した。

| 試行 | 最初のREADY・両者の新入力 | 基準リセット失敗 | その他 |
| --- | ---: | ---: | --- |
| 1 | 21.96秒 | 0 | 解析エラー0、警告なし |
| 2 | 21.89秒 | 0 | 初回IMU初期化失敗から両者認識を待って校正・復帰。解析エラー0。一時的な入力遅延3件 |
| 3 | 21.51秒 | 0 | ポート再開時に旧TEL途中とESP32起動ログがつながった不正フレーム1件を破棄。その後正常復帰 |

3回ともREADY、校正済み、基準有効、ADS/IMU正常、新入力ありで完了。各回で5秒間連続して入力が有効になることを確認した。遅れて認識されたIMUを含めて校正するケースも実機で通った。旧音声プラグイン警告はEditor再起動後に再発しなかった。

主な回帰確認はCoreテスト153件、管理画面テスト44件。生の試行記録とログは作業worktreeの `Temp/PhysicalMonitor/` に保存し、試験専用のEditorコードは除去した。文字欠け修正は上記3回の後に追加し、フォントの文字収録確認と実際のUnityコンパイラーによるCore・全ランタイムのコンパイル確認も成功した。これらの起動テストは走行感やレース完走を保証するものではない。

## 残る確認事項

- `STALE` は100msの有効TEL待ち制限を超えた警告。入力は中立化され、次の正常TELで復帰する。テスト中・その後のEditor操作中にも発生したため、通常起動での頻度と走行への影響を継続確認する。安全条件を緩めて隠してはいない。
- `1016こうしゃ/高校棟昇降口/Plane.049` に負スケールのBoxCollider警告が1件ある。Unityがサイズを正に補正しているが、実際の当たり判定の評価は別途必要。既存のシーン編集を変更してはいない。
- 既存のDiscord通知WebhookがHTTP 400を返す。通知送信の問題で、ゲームやチャット側のローカル状態記録とは別。通知先の変更や追加の外部送信は行っていない。
- Unity CloudのHTTP 403はクラウド接続の失敗。ローカルの実機入力準備とは分けて記録する。

## Intermittent pedal-release notice investigation

The notice can follow a brief unavailable/stale input, a course reset, a source replacement, or an input-mode change. `PlayerDriveInputGate` deliberately keeps acceleration neutral after recovery until a physical neutral pedal sample is observed. A healthy sensor status after the event does not clear this latch by itself. Dashboard `players` contains InputManager source values, not the car gate output, so it cannot establish the latch cause on its own.

Added transition-only `INPUT_GATE` diagnostics to the existing local serial journal and dashboard history: `RELEASE_REQUIRED` and `RELEASE_OBSERVED`, player number, reason (`connection_lost`, `course_reset`, `source_changed`, `input_mode_changed`), and current connection status. Reasons distinguish an input outage from normal course recovery without changing the safety timeout, release threshold, or driving behavior. The connection status is sampled when recording the transition; if a new sample arrives immediately after the failed connection check it can already read Ready. The `connection_lost` reason records the actual preceding failed check.

The ongoing local witness consumes `/api/events` continuously, preserving raw receive timestamps and before/after context around interruptions rather than relying on heartbeat snapshots. Witness data and PID files remain in ignored `Temp/PhysicalMonitor`; no external message is sent. Hardware timestamps advancing about 10 ms while PC receive records pause over 100 ms, together with severe system load, support investigating host/USB buffering or scheduling; they do not establish the root cause.

Validation: production gate/source tests cover one diagnostic per unavailable transition, continued neutral output while a recovered pedal is held, physical-release acknowledgement, and separate reset/mode/source reasons. These tests passed, the dashboard bridge harness compiled, and the full Unity runtime compiled using Unity's Roslyn references (existing warnings only). Runtime diagnostic sources were applied after the race reached Result and Play was stopped gracefully. The root-cause investigation remains open until actual diagnostic events identify the reported interruption and stable real play is verified.

### Journal contention repair

The new reason trace captured P1 entering `connection_lost / TelemetryStale` with its pedal near 88%, then clearing only after physical release. The first captured trigger was in Tutorial. Subsequent retained traces also contain five RELEASE_REQUIRED transitions during Game, all connection_lost / TelemetryStale, including P1 and P2. The actual rendered HUD text was not recorded, but the exact gate condition responsible for the notice was captured during driving. Adjacent wire timestamps advanced 11 ms while host receive records paused 172 ms, with later pauses over 500 ms.

The UART reader previously published a received frame only after synchronous journal work. That work could wait on a lock shared with dashboard readers, allowing a slow or paused diagnostic consumer to delay subsequent device reads. The serial thread sample also contained substantial managed monitor waits, although that sample does not identify the exact lock.

Journal producers now enqueue timestamped records and attempt a nonblocking flush. If a diagnostic reader holds the journal lock, acquisition continues; readers later drain a bounded amount of queued work. Pending storage is bounded at 8192 records, overflow explicitly reports `JOURNAL_GAP` and replay gaps, and timestamps retain the original acquisition time. Game input frames are queued before diagnostic work. This removes a proven blocking path without changing telemetry freshness or the pedal-release interlock. It does not establish that all system-load or USB delays are resolved.

Validation: 155 Core tests passed, including a reader-lock contention regression, preservation of receive timestamps, bounded overflow/loss reporting, existing replay ordering, and normal telemetry/history separation. Core and complete Unity runtime compilation passed using Unity references. Runtime improvement remains to be checked on real hardware. At deployment the editor was already stopped by the user, so Play was left stopped.

### Remaining stalls and diagnostic allocation reduction

After the nonblocking journal repair, a reconnected physical startup reached READY in 21.65 seconds with both players and zero parse errors. Real play traversed selection, both tutorials and countdown into Game. The continuous witness still captured a 121 ms host receive gap with an 11 ms device increment, followed by P1/P2 telemetry-stale release latches. Independent P2 course-reset release latches were also captured. The first repair removes lock coupling but has not eliminated all stalls.

Journal entries are now immutable and their JSON is built once, avoiding repeated string generation for each stream client and when measuring/pruning the journal. The dashboard also avoids re-parsing successful TEL lines already validated by the production receiver; other statuses retain the previous parsing path. This reduces diagnostic allocation/parse work while keeping CRC validation, freshness thresholds and the release policy intact. Core replay/concurrency/overflow tests (155 total) and bridge compilation passed. Runtime improvement must still be observed after deployment; validation tools and unrelated PC workloads can affect timing, so no controlled before/after performance claim is made.
