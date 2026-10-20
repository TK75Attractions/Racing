# ESP32 Serial Lab — RGP/1

ESP32を接続せず、同じMac上の仮想シリアルポートで本番のUnity受信処理を検査するアプリです。RGP/1の2プレイヤーTEL改訂に対応し、旧CSV送信・部分更新モードは使用しません。

## 起動

1. `bash Tools/ESP32Simulator/build.sh` でアプリとCLIをビルドします。
2. `Builds/ESP32Simulator/ESP32 Simulator.app` を起動し、「ポートを作成」を押します。
3. Unityの `Racing > ESP32 Simulator` から「アプリの接続情報を読み込む」→「有効化してPlay」を選びます。
4. アプリのセッション表示とUnityのACKを確認し、「送信開始」でTELを開始します。
5. 「両者の開始入力」はアクセルを離す→踏む→離す操作です。

接続情報は一時ディレクトリの `racing-esp32-simulator.json` に保存します。情報形式はversion=2、wireProtocol=RGP/1です。古いアプリの接続情報は取り込みません。Editorの設定はSessionStateだけに保持し、保存済みシーンやPlayer Settingsを変更しません。CLIはこの接続情報を上書きしません。

ポートを作り直したら接続情報を読み込み直してください。実機へ戻す場合は「無効化して通常入力に戻す」を選びます。シミュレーター使用中だけrunInBackgroundを有効にし、破棄時には以前の設定へ戻します。

## 操作と送信値

- P1/P2ごとにアクセル・ブレーキを独立して0〜1で指定します。同時に踏む状態も再現できます。
- ハンドルは右が正の累積実角度（±3600°）。送信時の除算・倍率変換・wrapはありません。左30°／直進／右30°のボタンも利用できます。
- 「Unityの除算値」はUnityのゲーム入力変換用メタデータです。TELの角度には掛けません。
- TELは `P1_HANDLE,P1_ACCEL,P1_BRAKE,P2_HANDLE,P2_ACCEL,P2_BRAKE` の順、角度2桁・ペダル3桁。既定100 Hz、CLIは1〜100 Hz、GUIは30/60/100 Hzです。
- キーボード操作では、P1のW/S・P2の上/下がそれぞれアクセル／ブレーキです。両方のキーを押しても引き算せず、両ペダルを送ります。フォーカス喪失時は解除します。
- 1項目でも「センサー無効」にするとTEL全体を停止します。ペダルセンサーの無効化はERROR/I2C_LOST/RECOVERING、ハンドルの無効化はANGLE_REFERENCE_INVALID/ANGLE_UNLOCKED/WAIT_ANGLEを通知します。有効へ戻すとREADYと完全なTELで両者が復帰します。
- 「送信停止」はTELだけを止め、CMD受信とACKは継続します。Unityは `max(3000/TEL_RATE,100) ms` を超えると両者をstaleにします（100 Hzでは100 ms）。

例：P1が右30°・アクセル75%、P2が左15°・アクセル50%の場合、TELのデータ部は `30.00,0.750,0.000,-15.00,0.500,0.000` です。Unityの符号付きペダルは受信後に `accel-brake`、ゲームのハンドル値は `handle/Steering Divisor` へ変換します。

## 双方向プロトコル

ポート作成時にはSEQ=0/TIME=0のHELLOから、BOOT・初期化・模擬校正・READYを送ります。全TYPEで同じ送信SEQを使い、TIMEは起動経過msです。フレームはCRC-16/CCITT-FALSE、大文字4桁、LF終端。受信はCRLF、途中の@での再同期、256 bytes上限、1秒の途中フレーム破棄に対応します。

対応CMDはOPEN_SESSION、PING、GET_STATUS、GET_CONFIG、ANGLE_ZERO、SET_BETAと、TELに対するSET_RATE／SET_STREAMです。IMU/DBGの送信制御、CALIBRATE、SCAN_I2Cは引数を検証したうえでUNSUPPORTEDを返します。センサー校正は起動時の模擬イベントであり、実ハードウェアの校正処理は行いません。

同じSEQ・同じ本文の再送には、操作を再実行せず新しい送信SEQのACKだけを返します。本文が異なればSEQ_REUSEです。重複記録は「その後16件の別CMD」と「10秒」の両条件まで保持します。新しいSESSION_IDでは記録をリセットし、同じIDの再送ではリセットしません。

モニターはTX/RXを最大160件保持します。セッション成立はOPEN_SESSIONの受信を示し、TELがゲームへ反映されたことまでは保証しません。入力の確認にはUnityのF8モニターかWeb監視を使ってください。

## 異常通信・シナリオ

GUIでは3秒TEL停止、P1センサー無効、不正TEL、CRCエラー、再起動/HELLO、分割送信を再現できます。正常なTELにnanを混ぜたり、P1だけを部分更新したりはしません。「不正TEL」は受信拒否の検査専用です。「再起動/HELLO」は送信SEQとセッション記録を初期化し、Unityの再接続を確認します。

シナリオの例：

```json
{
  "name": "独立ペダルと通信停止",
  "steps": [
    { "duration": 1 },
    { "duration": 2, "p1": { "steering": 30, "accel": 0.7, "brake": 0.3 }, "p2": { "steering": -15, "brake": 0.8 } },
    { "duration": 1, "p1": { "steeringUnavailable": true } },
    { "duration": 0.5, "silence": true },
    { "duration": 1 }
  ]
}
```

各ステップで省略した値は中立です。`accelUnavailable`、`brakeUnavailable`、`steeringUnavailable`を指定できます。旧`pedal`／`pedalUnavailable`形式は明示的に拒否します。`raw`は検査専用のASCII行、`fragmentDelayMilliseconds`は1〜1000 msの分割待ちです。通常TELの分割でも待ち時間が100 msを超えれば、フレーム完成までstaleになることがあります。1秒以上byteが届かなければ受信途中のフレームを破棄します。

各ステップ0.05〜600秒、最大256ステップ、合計1時間、ファイル64KiBまで。送信待ちはデータ1件と制御応答最大64件に制限し、書込みが2秒間進まない場合は停止してエラーを表示します。シナリオ完了後は中立入力へ戻り、手動操作で再生を中止できます。

## CLIと検査

```sh
bash Tools/ESP32Simulator/build.sh
bash Tools/ESP32Simulator/test.sh
Builds/ESP32Simulator/esp32-simulator-cli --duration 30 --frame 30,0.75,0,-15,0.5,0
Builds/ESP32Simulator/esp32-simulator-cli --duration 15 --scenario Tools/ESP32Simulator/Scenarios/02-input-and-timeout.json
```

`--frame`はTELと同じ6項目順です。`--port-file`でPTYパスを書き出し、`--start-file`でTELだけの開始を待機できます。待機中もHELLOとCMD応答は利用できます。Ctrl-C/SIGTERMで終了してPTYを破棄します。

検査は共通CRC vectors、CMDの引数エラーvectors、重複・SEQ再使用・新セッション、フレーム再同期、実PTYの双方向ACK、送信停止・分割キャンセル、輻輳を確認します。さらにUnity付属Monoで本番の受信コードをコンパイルし、Swift CLIとの実PTY接続でセッション確立・独立ペダル・両者の同時停止・stale・復帰・CRC拒否・300ms分割を検査します。

描画が詰まった直後の物理更新も検査します。正常なTEL受信を続けたままメインスレッドを150／350／650ms停止し、次の通常入力更新より前に両者の本番入力ゲートを読みます。受信済みキューを先に反映するため、通信が正常ならペダル解除待ちには入りません。`04-queued-stale.json`では同じ停止中にTELも止め、古い受信キューを処理してもstaleのままで、両者がペダルを離して踏み直すまで再開しないことを確認します。鮮度の期限は処理時刻でなく受信時刻を基準にします。修正前後の記録は [PedalReleaseFix](../Artifacts/PedalReleaseFix/README.md) を参照してください。

Unityのゲームビルドでも使う場合は、`Racing > Build > ESP32 Simulator Player` で新しいDevelopment Buildを作ってください。起動引数は `--esp32-simulator-port /dev/ttys000 --esp32-simulator-divisor 6.5`。任意の `python3 Tools/ESP32Simulator/Tests/standalone_smoke.py` は、その更新済みビルドを検査します。旧CSV用のビルドは利用できません。

macOS PTYはDTR/RTS操作に対応しないため、明示的なシミュレーターセッションだけPOSIXのopen/poll/read/writeを使います。byte取得後はUSB実機と同じRGPデコーダー・セッション・stale判定・ゲーム変換を通します。USB列挙、実機リセット、115200bpsの物理転送速度、電圧校正は再現しません。自動で別ポートを開き直すこともありません。

`Artifacts/ESP32Simulator/`の旧editor-status／standalone-smokeは旧CSV版の過去の記録です。RGP/1版の今回の検証結果はrgp-validationを参照してください。
