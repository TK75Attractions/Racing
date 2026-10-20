# P1のハンドルだけESP32を使うデバッグ

P1のハンドルをESP32から読み、ペダルはキーボードまたは一定の踏み込みへ置き換える。ESP32のペダル値はこのモードでは使用しない。P2は元の入力設定を維持し、P2自動運転とも併用できる。Editorと通常ビルドの両方に対応する。

## 使い方

1. ESP32を接続して起動する。ゲームのキーボード入力を受けるウィンドウでF8（Mac設定によってはfn+F8）を押す。
2. **P1 steering test**のボタンを選ぶ。
3. 走行だけ試す場合は、同じパネルの **Start test race - P1 manual / P2 AUTO** から開始する。車選択・練習を省略し、通常のカウントダウン後にP1のハンドルを試せる。

| ボタン | P1のハンドル | P1のペダル |
|---|---|---|
| Normal | 起動時の入力設定に戻る | 起動時の入力設定に戻る |
| W/S pedal | ESP32。既存のSteering Divisorも適用 | Wで前進1、Sで後退／制動-1、どちらも離すと0 |
| Constant pedal | ESP32。キーボードのA/Dは使用しない | レース中は指定値（既定100%）。スライダーで0〜100%。Sを押している間は0 |

W/S pedalでもA/Dは操舵へ影響しない。Spaceは復帰、Enterは準備操作。Constant pedalでもタイトル・車選択・練習・カウントダウン・結果では常時入力にせず、W/Sの操作へ戻る。選択や練習の停車課題、結果の確定を勝手に進めないための仕様。

走行中にモードを変更した場合は既存の入力ゲートがペダル解放を待つ。W/S pedalではW/Sを離し、Constant pedalでは**一度Sを押してから離す**。再接続やポーズ解除で「ペダルを離して再開」と表示された時も同じ。Constant pedalのSは踏み込み解除であり、即座に車体速度を0にする操作ではない。

ESP32が未接続・通信切れ・READY未到達・TELの鮮度切れの場合、代替ペダルを選んでいても入力を止める。キーボードが使えない場合も、このデバッグ入力は無効になる。通常の接続条件と再開ゲートを通す。

## 設定と既存モード

- 起動時の既定はNormal。F8の変更は実行中だけで、次回起動は保存されたシーンの設定に戻る。
- Inspectorの`InputManager > P1 Steering Test`で事前設定もできる。`P1 Steering Test Mode`と`P1 Constant Pedal`を指定する。
- `Is Debug Mode`でキーボード専用起動していても、F8でハンドルテストを選ぶとP1用のシリアルを開く。P2はキーボードのまま。Normalへ戻すとP1も元のキーボードへ戻る。
- `Is P1 Serial P2 Keyboard Debug Mode`なら、P2の矢印キー等を維持したままP1ペダルだけを置き換える。
- 通常のシリアル2人設定ならP2はシリアルのまま。Serial Lab使用時は、ハンドルに指定PTYのP1値を使う。
- モード変更でポート全体や車両入力を作り直さず、同じP1アダプターを使う。走行中・練習中の入力ゲートとP2を維持する。キーボード専用へ戻した後も、再切替のためシリアルを待機させる場合がある。
- ゲーム機能14スイッチとは別の入力デバッグ設定。`All added features OFF`では変えず、解除はこの欄のNormalで行う。
- F8パネル表示中は、従来の周回加算・即ゴール用デバッグ欄を隠し、操作箇所の重なりを避ける。

## 検証

[検証記録](../Artifacts/P1SteeringTest/README.md)。シリアル送信器を用意したmacOSでは次で実PTY＋本番車両の検証を実行できる。

```sh
python3 Tools/TestP1SteeringInput.py
```

Unityは`ProjectVersion.txt`のバージョンを使う。別の実行ファイルは`UNITY_EDITOR`、出力先は`RACING_STEERING_TEST_OUTPUT`で指定できる。既定出力は`Logs/P1SteeringTest/`。事前に`Builds/ESP32Simulator/esp32-simulator-cli`が必要で、[Serial Labのビルド手順](ESP32Simulator.md)を参照。
