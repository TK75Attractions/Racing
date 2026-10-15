# ESP32 シリアルコントローラープロトコル（RGP/1）

Unityは1台のESP32からP1/P2の入力を受け取ります。参照仕様は添付RGPの `spec/RGP-1.md`（1.0候補・2プレイヤーTEL改訂）です。旧CSV・旧2項目TELとの自動混在は行いません。ESP32側も同じ候補版へ更新してください。

## 接続

通信条件は115200 bps / 8N1 / フロー制御なし。空のPort NameではUSBシリアルポートを自動選択し、CP2102の `SLAB_USBtoUART` を優先します。必要なら `InputManager > ESP32 USB Serial > Port Name` を指定してください。

起動時にランダムな大文字16進16桁のIDで `OPEN_SESSION`（PC側SEQ=0）を送り、成功ACKの後に `PING`、`GET_STATUS`、`GET_CONFIG` を送ります。起動イベントを見逃しても状態を照会できます。ACKはREQUEST_SEQとCOMMANDで対応付け、1秒で応答がなければ同一フレームを最大2回再送します。結果不明のコマンドを新しいSEQで再実行しません。未知ACK STATUSは失敗扱いで完了します。

HELLOを受けたら入力・TELのSEQ追跡・未完了CMDを破棄し、新しいIDで接続し直します。異なるmajorのHELLOを受けた場合は入力反映とCMD送信を停止します。

## フレームと入力

```text
@TYPE,SEQ,TIME[,DATA...]*CRC\n
@TEL,13,8210,-12.50,0.750,0.000,25.00,0.000,0.400*7118\n
```

TELの順序は `P1_HANDLE,P1_ACCEL,P1_BRAKE,P2_HANDLE,P2_ACCEL,P2_BRAKE`。ハンドルは右旋回が正の累積角度（±3600 degree）、各ペダルは0〜1です。通信層では6項目を独立して保持します。ゲーム入力への変換時に `pedal = accel - brake`、`steering = handle / Steering Divisor` とします。既存シーンのSteering Divisor=6.5は維持します。リセットは従来どおりキーボード操作です。

CRCは `@` の直後から `*` の直前までに対するCRC-16/CCITT-FALSE（初期値FFFF、多項式1021、反転なし、XOR Out=0）です。CRCは大文字16進4桁、LF終端、CRLF受信も許可します。最大長はLF込み256 bytes（CRLF時のみ257 bytes）。前後の空白を取り除いて受理せず、非ASCII・空フィールド・不正な数値構文・範囲外・CRC不一致を拒否します。

受信はbyte単位で処理し、開始前のログを無視し、新しい `@` で再同期します。途中のフレームで1秒間byteが来なければ破棄します。DBGとERRの文章は正規のUTF-8パーセントエンコードを検証し、文章から装置状態は判定しません。未知TYPE・イベント・SYS KEY・設定名は無視します。

有効TELだけをP1/P2へ一括反映し、片方だけ・一部の軸だけの更新は行いません。重複・古いTELを除外し、SEQ/TIMEの32bit周回を許可します。最終有効TELの**PC受信時刻**から `max(3000 / TEL_RATE, 100) ms` を超えたら両プレイヤーを中立化します（標準100 Hzでは100 ms）。処理待ちの古いTELを取り出しても、その時刻を新しい受信時刻として扱いません。

READY以外への状態遷移、TEL_STREAM停止通知、ERROR/FATAL、角度基準喪失、I2C切断、シリアル切断では直ちに入力を無効化します。未知STATEもREADYとして扱いません。再開にはREADYと新しい有効TELが必要です。

## デバッグモード

`Is P1 Serial P2 Keyboard Debug Mode` はP1をRGP/1、P2を従来のキーボードに割り当てます。受信するTELはこのモードでも2人分の6項目をすべて検証します。P2シリアル値は車両入力に使いません。キーボードのみの `Is Debug Mode` より優先します。

シリアルモニターでは `HELLO`、`ACK_OK`、`EVENT`、`OK`、`OLD_TEL`、`NOT_READY`、`INVALID` などを表示します。開始前の生ログは入力フレームとして記録されません。

## 検証

添付の3ファイルを改変せず `Tests~/Rgp/Vectors/` に保存しています。PC向けの正常・異常vectors、共通フレーミング異常、CRC全件を `Assets/Tests/EditMode/SerialInputProtocolTests.cs` で検査します。ESP側専用のコマンド引数・実行・重複排除の期待値はESP32ファームウェアの責務です。

Unity Test RunnerのEditModeで実行するか、macOSではMonoとUnity付属NUnitを使って次を実行できます。

```sh
./Tools/Validate-Rgp.sh
```

このスクリプトは本番のCoreソースと既存のEditModeテストをコンパイルし、`Artifacts/Rgp/test-results.xml` を出力します。Unityの場所は `UNITY_EDITOR_ROOT`、NUnit DLLは `NUNIT_FRAMEWORK` で変更できます。vectorsの場所は `RGP_VECTOR_ROOT` で変更できます。

追加検査には、セッション確立、ACK照合・再送上限、HELLOによる再接続、major不一致、P1/P2一括更新、独立ペダル、古いTEL・SEQ周回、受信時刻基準のstale判定、状態遷移・切断、再同期・途中フレーム破棄を含みます。実機の電圧校正やUSB接続での動作確認は別途行ってください。
