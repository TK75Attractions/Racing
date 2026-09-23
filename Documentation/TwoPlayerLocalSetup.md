# 2人ローカル対戦（フェーズ1〜5）

## 実行条件

- 1台のマイコンをUSB接続し、P1/P2の4列入力を改行区切りで送信できること
- PCにモニターを2台接続してからゲームを起動すること
- `SampleScene` の `InputManager > Is Debug Mode` は通常プレイでは無効にすること

ゲーム起動時にDisplay 1（2台目）を有効化し、次の表示を割り当てます。

- Display 0: Player 1の追従カメラとHUD
- Display 1: Player 2の追従カメラとHUD
- タイトルとリザルト: 両Displayに同じ内容

## レース進行

1. 両プレイヤーがペダルを離すと開始入力を受け付けます。
2. P1、P2がそれぞれペダルを押す（またはプロトコルのReadyを送る）とレースを開始します。
3. 2台の車両はスタート地点の左右に4m間隔で生成されます。
4. 両Displayに同じ3秒カウントダウンを表示し、その間は車両入力を無効化します。
5. プレイヤーが完走すると、そのプレイヤーのDisplayだけにゴール画面を表示し、車両を停止して衝突判定を無効化します。
6. ゴール画面の後、完走済みプレイヤーのDisplayは未完走プレイヤーのカメラとHUDへ切り替わります。画面外周の枠と `WATCHING P2` のようなラベルで観戦対象を示します。
7. 未完走プレイヤーのDisplayだけに残り時間を表示し、完走を最大40秒待ちます。時間切れの場合は未完走車をDNFとして確定します。
8. 全員の完走または時間切れ後、両Displayに同じ順位・タイム（またはDNF）を表示します。

`Gmanager` の `Start Grid Spacing` と `Second Place Timeout Seconds` で、それぞれスタート間隔と待ち時間を調整できます。待ち時間の標準値は40秒です。

## キーボード確認

Unity Editorで `InputManager > Is Debug Mode` を有効にすると、ESP32なしでも確認できます。

- P1: WASD / Space / Enter
- P2: 矢印キー / Right Ctrl / Right Shift

シリアル仕様は [ESP32SerialProtocol.md](ESP32SerialProtocol.md) を参照してください。

自動検証と実機チェックリストは [MultiplayerValidation.md](MultiplayerValidation.md) を参照してください。
