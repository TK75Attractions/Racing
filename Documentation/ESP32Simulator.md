# ESP32 Serial Lab

ESP32を接続せず、同じMac上の仮想シリアルポートからP1/P2の入力をUnityへ送る開発用アプリです。ペダル・ハンドル操作、シナリオ再生、軸ごとの`nan`、通信停止、不正行、起動ログ、分割送信を確認できます。

## 起動と接続

1. ビルド済みの`Builds/ESP32Simulator/ESP32 Simulator.app`を開きます。
2. アプリで「ポートを作成」を押します。`/dev/ttys000`のようなパスが表示されます。
3. Unityで`SampleScene`を開き、`Racing > ESP32 Simulator`を選びます。
4. 「アプリの接続情報を読み込む」→「有効化してPlay」を押します。
5. アプリで「送信開始」を押します。Unityの接続ウィンドウで受信数とP1/P2のConnectedを確認します。
6. 「両者の開始入力」を押すと、ペダルを離す→踏む→離すを送信し、各プレイヤーの車種選択へ進みます。

Unityの接続ウィンドウは実行中も再表示できます。「適用 / 再接続」で入力元を作り直せます。「無効化して通常入力に戻す」でシーンに保存された入力設定へ戻ります。Playを停止してもシミュレーターの有効設定は同じEditorセッション内に残ります。実機を使うときは無効化してください。

ポートと倍率はEditorの`SessionState`に保持され、保存済みシーンやPlayer Settingsを書き換えません。有効中はキーボードデバッグ設定よりシミュレーターを優先し、`Application.runInBackground`を一時的にONにします。通常入力への復帰または入力元の破棄で以前の値へ戻します。

接続情報はユーザーの一時ディレクトリの`racing-esp32-simulator.json`から読み込みます。読み込めない場合はアプリのパスをコピーし、Unityの`PTY Port`へ貼り付け、倍率を合わせてください。CLIと自動テストはこの接続情報を上書きしません。

## 操作

| 操作 | 動作 |
| --- | --- |
| ペダル | -1がブレーキ／後退、0が中立、+1がアクセル |
| ハンドル | Unityへ渡したい角度。範囲は±90度。送信時に「Unityの除算値」を掛ける |
| nan | 選んだ軸を使用不可として送信。他の有効軸は継続 |
| 開始入力 | 指定プレイヤーのペダルを0.5秒離す→0.5秒踏む→離す |
| 短押し / 長押し | 指定プレイヤーのペダルを一度離してから0.15秒 / 1.2秒踏む。選択項目移動／確定用 |
| 左30° / 直進 / 右30° | ハンドルをその角度に固定。選択画面で次を選ぶ前には直進へ戻す |
| 全て中立 | 手動操作へ戻して全軸0。送信中なら中立を継続送信 |
| 送信停止 | PTYを維持して無送信。最終有効入力から2秒超でUnityが切断扱いにする |
| ポート破棄 | PTYを閉じる。作成し直したらUnityで接続情報を読み込み直して再接続 |

現在の`SampleScene`の倍率は6.5です。例としてP1がペダル0.75・ハンドル+30度、P2がペダル0.5・ハンドル-15度なら、送信は`0.7500,195.0000,0.5000,-97.5000\n`となります。Editorの接続情報読込はアプリの倍率も取り込むので、倍率を変更したときは再接続してください。

キーボード操作をONにすると、アプリが前面の間だけP1のWASD／P2の矢印キーで操作できます。ハンドルは±30度、ペダルは±1です。キーを離すと0に戻り、アプリがフォーカスを失ったときも押下状態を解除します。数値欄の編集中には操作キーを取り込みません。

4列はP1/P2の軸です。Reset/Ready列はありません。開始・確定はペダル入力で行います。車両リセットはUnity側の既存キー（P1: Space、P2: `/`）を使用してください。

送信モニターには実際に書き込んだCSVを最大160件表示します。書込み成功は受信のACKではありません。受信の確認にはUnityの接続ウィンドウ、F8モニター、`http://127.0.0.1:8765/`の既存ローカルダッシュボードを使います。ダッシュボードのハンドル表示は±1へ制限されるため、角度の正確な確認は接続ウィンドウかF8モニターで行ってください。

## 異常通信とシナリオ

「3秒無送信」「P1切断」「全軸nan」「不正行」「起動ログ」「分割送信」をその場で再生できます。いずれも終了後は全軸中立の継続送信へ戻ります。手動操作や送信停止で再生を中止できます。分割中の行を中止すると、既に送った断片を改行で打ち切るため、Unity側に1行のINVALIDが出る場合があります。

「JSONを開く…」でシナリオを選び、「再生」を押します。同梱の`Scenarios`フォルダーには3例あります。

- `01-title-start.json`: 両者の開始入力。
- `02-input-and-timeout.json`: 数値入力、片軸nan、P1切断、全体無送信、復帰。
- `03-boot-and-fragments.json`: 起動ログ、不正行、10ms／300msの分割送信。

JSONの例:

```json
{
  "name": "右へ曲がって停止",
  "steps": [
    { "duration": 1, "label": "中立" },
    {
      "duration": 2,
      "label": "操作",
      "p1": { "pedal": 0.75, "steering": 30 },
      "p2": { "pedal": 0.5, "steering": -15 }
    },
    { "duration": 3, "label": "無送信", "silence": true },
    { "duration": 1, "label": "正常復帰" }
  ]
}
```

省略したプレイヤーと軸は、そのステップで中立になります。前のステップの値は引き継ぎません。プレイヤーには`pedalUnavailable`と`steeringUnavailable`も指定できます。`raw`は改行なしのASCII 1行をそのまま繰り返し送り、`silence: true`は無送信、`fragmentDelayMilliseconds`は1行の前半と後半の書込み間隔です。`raw`と`silence`は併用できません。

1ステップ0.05〜600秒、最大256ステップ・合計1時間・ファイル64KiBまでです。送信待ちは最大1行で、遅れた周期をまとめて送信しません。読取が止まりPTYが詰まった場合は、書込みが2秒進まないとエラーで送信を停止します。

シナリオは入力波形を再生します。練習完了には車速や実際の旋回が必要なので、時間だけでゲーム全体の進行完了を保証する機能ではありません。

## ビルド・テスト

macOS 13以降とXcodeのSwiftツールチェーンを使用します。リポジトリのルートで実行してください。

```sh
bash Tools/ESP32Simulator/build.sh
bash Tools/ESP32Simulator/test.sh
```

ビルドは実行したMacのアーキテクチャ用で、出力先は`Builds/ESP32Simulator`です。生成したアプリはローカル用のad-hoc署名で、App Store配布・公証用ではありません。実行時の追加ランタイムは不要です。

テストはSwiftのCSV生成・周期送信・キャンセル・raw PTY・送信詰まり・作成破棄を確認し、既存NUnitの`SerialInputProtocolTests`を実行します。さらにUnity付属Monoで本番の`TwoPlayerSerialInputSource`・PTYアダプター・パーサーをコンパイルし、実際のSwift送信器に接続してタイムアウトと分割送信を検証します。テスト用のUnity属性／時計スタブは`Tools`配下に置き、Unityプロジェクトには取り込みません。

CLIも利用できます（`--frame`のハンドルは角度です）。

```sh
Builds/ESP32Simulator/esp32-simulator-cli --duration 30 --frame 0.75,30,0.5,-15
Builds/ESP32Simulator/esp32-simulator-cli --duration 15 --scenario Tools/ESP32Simulator/Scenarios/02-input-and-timeout.json
```

CLIが表示した`PORT`をUnityの接続ウィンドウへ手入力できます。`--port-file`でパスをファイルに書き出せます。`--start-file`を指定すると、そのファイルができるまでポートだけを保持して送信を待ちます（待機の上限も`--duration`秒）。ゲームの起動待ちを含む自動試験に使用できます。Ctrl-Cまたは指定送信時間の経過で終了し、ポートを破棄します。

ゲームビルドの通信スモーク試験は、EditorのPlayと他のRace Playerを停止してから`python3 Tools/ESP32Simulator/Tests/standalone_smoke.py`で実行します。画面を描かずにゲームを起動してローカル診断APIで受信・切断・復帰を確認し、検証後に起動したプロセスを終了します。描画の検証は含みません。

Macのゲームビルドでは次の引数を指定できます。Unityの`Racing > Build > ESP32 Simulator Player`で対応するDevelopment Buildを作成できます。

```sh
"Builds/ESP32Simulator/Player/Race.app/Contents/MacOS/Race" \
  --esp32-simulator-port /dev/ttys000 --esp32-simulator-divisor 6.5
```

## 通信方式と制約

macOSのPTYには物理UARTのDTR/RTSがありません。このプロジェクトのUnity付属Monoで通常の`SerialPort.Open`を試すと、`SerialPortStream.SetSignal`で`Inappropriate ioctl for device`になります。

そのため、明示的に有効化したシミュレーターセッションだけ、`MacPseudoTerminalLineReader`がPTYをPOSIXの`open/poll/read`で読みます。受信後のキュー、4列パーサー、プレイヤーごとの有効軸判定、2秒タイムアウト、ゲーム入力は実機と同じ`TwoPlayerSerialInputSource`を使用します。実機のUSBポートは引き続き`SerialPort`で開きます。

PTYアダプターは受信途中の行を200msタイムアウト後も保持し、次の断片と結合します。Monoの物理シリアルの`ReadLine`と、部分行がタイムアウトした際の挙動が完全に一致することは保証しません。電気信号、USB列挙、CP2102ドライバー、DTR/RTSによるESP32再起動、実センサーの特性、115200bpsの物理転送時間は実機で確認してください。

ポート破棄後の自動再オープンは行いません。Editorでは「適用 / 再接続」、ゲームビルドでは新しいポート名を指定して再起動します。Windowsの仮想COMは対象外です。

2026-10-15の検証では、Swiftの34項目、CLI開始待機、既存プロトコルのNUnit 18ケース、Unity付属Monoでの2つの通信シナリオが通過しました。Editorでは両者の開始入力で車種選択へ進み、約10分の通常通信で35,726行を受信・解析し、エラー0件でした。Mac Development Buildでも正常入力、片軸nan、P1タイムアウト、全体無送信、復帰を確認しています。検証記録は`Artifacts/ESP32Simulator/validation.json`、`editor-status.json`、`standalone-smoke.json`にあります。Macビルドの通信試験は画面なしで実行しており、描画の合否は含みません。
