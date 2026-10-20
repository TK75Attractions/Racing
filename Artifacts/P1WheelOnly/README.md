# P1ハンドル単体対応（2026-10-21）

## 入力の割当

| 設定 | P1ハンドル | P1ペダル | P2 |
|---|---|---|---|
| Is P1 Serial P2 Keyboard Debug Mode=ON | ESP32の0x68 | 既定W/S、F8でConstant pedal | キーボード |
| 両デバッグチェック=OFF、Normal | ESP32の0x68 | ESP32 | ESP32の0x69とペダル |

ONならADS1115・ESP32の両ペダル・P2ハンドルは不要。Normalを選び直した場合も、この構成の既定W/Sへ戻る。Serial LabのPTYでも同じチェックを優先する。通常2人シリアルでのW/S／ConstantはP1のみの置換で、P2は通常のESP32入力を維持する。

## ESP32の更新が必要

旧ファームは両ペダルがないとTEL全体を止めるため、Unityだけを変えてもハンドルを受信できない。handlefastimuを基点e17082dから修正し、`SET_INPUT_MODE,P1_STEERING`でP1ハンドルだけを必須にする機能を追加した。GET_STATUSのADS_OKは実際の不在を0として返す。モード選択後も実際のP1校正・角度基準・新しいTELが必要。P2をP1の代わりに使わない。

ファーム側commit：`a94f1b5b054e237372422b175ec94fceae500d7f`、ブランチ`codex/p1-steering-only`。独立した作業場所は`/private/tmp/racing-p1-steering-firmware`。元のhandlefastimu/main作業フォルダは変更していない。リモートへの公開状況はこの記録とは別に最終報告を参照。

- `firmware.patch`：元のhandlefastimu e17082dへ適用できる変更一式。`git am`用。
- `firmware.bin`：esp32dev向けアプリ領域のビルド成果物。
- `firmware.factory.bin`：同じビルドのブートローダー・パーティション等を含む結合イメージ。
- `firmware-manifest.json`：ソースcommit、対象ボード、SHA-256。

通常はファーム側ブランチで`pio run -t upload`を実行して書き込む。ボード・ポートは実機に合わせる。ビルドには既存のFastIMU 1.2.9とAdafruit ADS1X15 2.6.2を使用。ESP32への書き込みと実ハンドルでの試走はこの環境では行っていない。

## 検証

Unity 6000.3.9f1で`python3 Tools/TestP1SteeringInput.py`を実行。別々の実PTYで通常2人入力とP1ハンドル単体を検証した。単体シナリオはADS／両ペダル／P2ハンドルを使用不能にし、P1を一度切断して再接続する。

- チェックONでP1ハンドル78度/6.5=12、W/Sペダル、P2矢印キーを確認。ADS状態はFaultのままで、未使用機器を正常と偽らない。
- 同じ接続でConstant pedal 65%を確認。P1切断で0、再接続後もゲートを維持し、F8再開で復帰。P2キーボードは途切れない。
- 通常2人モードではP1=12、P2=-6のハンドルと個別ペダルを確認。キーボード専用起動からの途中有効化も確認。
- 本番車両の物理走行でConstant pedalの加速・旋回、通常入力への復帰、実ポート閉鎖時の停止を回帰確認。
- 各ハンドルの0x68/0x69割当と個別切断、成功ACKと新TELを待つ条件、旧ファームのUNSUPPORTED、新セッションで通常条件に戻すことを検証。
- ファームの実setup/loopと校正処理をハードウェアの代替実装で動かし、P1単体・ADSなし・ペダルなしでREADYとTELを確認。未使用P2の追加、P1の切断・再接続、ALL復帰、新ホストでのALL復帰も確認。
- ファームの既存C++回帰テストと8種のIMU識別テスト、ESP32向けPlatformIOビルドに成功。

主要結果は`validation.txt`。自動検証は実ESP32の配線・センサー精度・人による操作感を評価したものではない。
