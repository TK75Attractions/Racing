# チュートリアルの連続性と本番車両物理（2026-10-20）

原因は`DrivingTutorialController.Tick`がSTEP 2開始時に`Recover`を呼び、別の位置へ戻して速度を消していたこと。加えて練習専用`FixedUpdate`が水平速度を毎回10m/sへ上書きしていた。

段階切替は説明と旋回角の基準だけを更新し、車両の位置・向き・速度を引き継ぐ。練習専用の速度上限と、停車課題の復帰時の4m/s注入を除去した。本番と同じ選択車・入力ゲート・タイヤ力・操舵・走行抵抗を使い、課題用の速度上書きをしない。路外・転倒・明示リセットからの復帰、終了後の固定は残している。

Unity 6000.3.9f1、実SampleSceneの両プレイヤーで検証。段階1の成立直前から実`Tick`を通し、1→2で位置・向き・速度が変わらず、操舵の基準角が実際の向きになることを確認。その後は車の観測とハンドル／ペダル入力による実走で両者とも加速→操舵→ペダル解放による停止→レースのカウントダウンに到達した。最大速度25.562m/s、安定接地後の計測対象区間の最大上下速度0.000m/s。`validation.txt`に検証マーカー、`steer.png`に実際のSTEP 2画面を保存した。

入力をテスト用に交換する際も通常の`PlayerDriveInputGate`を経由するよう既存検証を修正した。通信断の安全条件を迂回しない。純粋C#の既存98テストも成功。起動時の既知のUnity Search索引例外は、この実走検証の成功と区別する。実ハンドルでの操作感は別途確認が必要。

再実行（`-quit`は付けず、検証完了で終了）：

```sh
RACING_TUTORIAL_QA_OUTPUT=/private/tmp/racing-tutorial-continuity \
  /Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$PWD" -executeMethod DrivingTutorialValidation.Run \
  -logFile /private/tmp/racing-tutorial-continuity.log
```
