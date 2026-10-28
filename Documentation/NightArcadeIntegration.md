# 新UI × 夜の縁日：試験統合と評価

確認日：2026-10-26。統合ブランチ：`codex/night-ennichi-arcade-ui`。

2026-10-28のmain取り込み後の状態は [mainとの統合確認](NightArcadeMainReconciliation.md) を参照。以下の画像と結果は10月26日時点の記録。

新UIは `codex/arcade-sport-ui` の `6eb6360`（デザイン2）、夜景は `raymee/night-ennichi-visuals` の `c0d6158` を使用しています。両ブランチの履歴を保持してマージしました。

## 統合で調整した点

- 新UIのロゴ・青／ピンクのプレイヤー色・2連メーター・前周比・車選択・結果・終了画面を維持。
- 夜景側の空・桜・提灯・道路・チューブ・車体演出を維持。新UI側の昼用キャンパス生成が、夜景の素材や桜を置き換えないようにしました。
- 夜景の3D縮小と `NativeResolutionUI` のUI等倍合成を使用。撮影も `PlayerDisplayRig.RenderToTarget` で同じ描画経路を使用します。
- 夜景の通知キューを新UIの短い通知行へ接続。逆走・急カーブ・ジャンプの警告を残し、接続／復帰待ち・ペダル解放待ちは中央表示。道路警告はアイテム効果・接近より優先します。
- 新しい音源・プレイヤー別のBGM選択と、夜景側のフェード・車両位置別の効果音を両立。効果音ミュートは連続タイヤ音にも適用。
- 長押しは押した瞬間からの実時間で計測。重い描画の直後に、押す前のフレーム時間が加算される問題を修正。
- Windows向けシェーダー事前生成コレクションはD3Dでのみウォームアップし、Metalでは使用するパスを通常通りコンパイル。Windows用MRTバリアントのMetalエラーを回避。
- 夜景側のキーボード検証も、現行チュートリアルの実アイテム取得課題に追従。

## 画面と評価

Unity 6000.3.9f1 / macOS / Metal の実シーンを使用します。固定背景にUIを合成した画像ではありません。

### タイトル

[P1](../Artifacts/NightArcadeIntegration/Live/title-p1.png) / [P2](../Artifacts/NightArcadeIntegration/Live/title-p2.png)

青・ピンクと濃紺の組み合わせは夜景へ自然につながります。ロゴの白い輪郭も暗い背景で読めます。一方、現在の広い俯瞰は校舎と暗い余白が主役になり、桜・提灯・チューブの光が小さく見えます。新UIそのものより、タイトル背景の構図に改善余地があります。

改善案：夜景時はカメラをコース近くへ下げ、桜と提灯を中景に、校舎を遠景に置く。ロゴ左上・開始ボタン右下の余白を維持しながら、背景だけで「夜の学校の縁日」が伝わる構図を優先します。

### 走行HUD

[P1](../Artifacts/NightArcadeIntegration/Live/race-p1.png) / [P2](../Artifacts/NightArcadeIntegration/Live/race-p2.png)

濃紺の面・青／ピンク・ミントの目盛りは、チューブのシアンや桜のピンクと合っています。文字とメーターは暗い景色から判別しやすく、中央の進行方向も保たれます。旧UIより大きな計器で、アーケードゲームらしい読みやすさがあります。

ただし左右の大きなメーターが、並走する車の下部や近い縁石を隠す場面があります。明るい円盤と上の白いタイム面は、暗いコースより視線を強く引きます。色の不一致より、UIの占有面積と明るさの配分が調整候補です。

改善案：速度の大きな数字を保ちながら、外周だけ10〜15%縮めた案を比較する。タイム・周回の面には薄い青みを足すか濃紺＋白文字の案を用意し、数値の可読性を保ったまま背景との明暗差を少し抑える。外周や文字への強いネオン発光追加は、既存のコース発光と競合するため優先しません。

### 結果と実リプレイ

[P1](../Artifacts/NightArcadeIntegration/Live/result-p1.png) / [P2](../Artifacts/NightArcadeIntegration/Live/result-p2.png)

濃紺のカードと桜・夜空の組み合わせは馴染み、本人の順位・タイムをすぐ読めます。一方、この試験リプレイでは桜の枝がカメラと車の間に入り、車が隠れます。別角度の定点結果では車が見えるため常時ではなく、構図に依存します。夜景で増えた情景と、既存リプレイカメラの位置の相性に問題があります。

改善案：リプレイはコース内側へのオフセットや車を上から捉える角度を優先する。車との間に遮蔽物が続く場合は別のリプレイカメラへ切り替える。桜自体を減らすより、ハイライトで車が見える構図を確保する調整が有効です。

## 改善の優先順位

1. リプレイの遮蔽物対策：主役の車が見えなくなるため最優先。
2. 夜景タイトルのカメラ構図：桜・提灯・路面の発光を近くに入れ、縁日の世界観を伝える。
3. HUD外周の面積と白い面の明るさ：現行の数字の読みやすさを残して比較。

デザインを全面的に作り直す必要は感じません。配色の相性は良く、情景を見せる構図と情報量のバランスを調整する方が効果的です。この試験統合では配色やレイアウトの追加改変は行わず、上記を次のデザイン判断の候補として残しています。

### 車選択・練習・終了

[車選択P1](../Artifacts/NightArcadeIntegration/Live/car-p1.png) / [車選択P2](../Artifacts/NightArcadeIntegration/Live/car-p2.png) / [練習](../Artifacts/NightArcadeIntegration/Live/tutorial-p2.png) / [終了P1](../Artifacts/NightArcadeIntegration/Live/ending-p1.png) / [終了P2](../Artifacts/NightArcadeIntegration/Live/ending-p2.png)

車選択は独立したガレージとして成立し、車体色とカード・台座の色が揃っています。夜景からの連続感を強める場合も、床の縁にコースと同じシアン／ピンクの細線を足す程度で十分と感じます。練習・終了画面は同じ配色とロゴでつながり、文字と夜景の判別も良好です。

## コースと解像度の確認

[一覧](../Artifacts/NightArcadeIntegration/Course/contact-sheet.png) / [解像度比較](../Artifacts/NightArcadeIntegration/Course/resolution-sheet.png) / [撮影条件](../Artifacts/NightArcadeIntegration/Course/manifest.json)

| 場所 | 1080pでの実画像 |
| --- | --- |
| スタート・桜 | [表示](../Artifacts/NightArcadeIntegration/Course/01-start-grid-moving-1920x1080.png) |
| 校舎前 | [表示](../Artifacts/NightArcadeIntegration/Course/02-school-front-moving-1920x1080.png) |
| チューブ入口 | [表示](../Artifacts/NightArcadeIntegration/Course/03-tube-entry-moving-1920x1080.png) |
| チューブ内 | [表示](../Artifacts/NightArcadeIntegration/Course/04-tube-inner-moving-1920x1080.png) |
| チューブ出口 | [表示](../Artifacts/NightArcadeIntegration/Course/05-tube-exit-moving-1920x1080.png) |
| ジャンプ前 | [表示](../Artifacts/NightArcadeIntegration/Course/06-jump-approach-moving-1920x1080.png) |
| 急カーブ | [表示](../Artifacts/NightArcadeIntegration/Course/07-sharp-curve-moving-1920x1080.png) |

定点撮影は車の位置・速度を制御しています。静止／25m/sで同じ地点を撮影し、3Dのrender scaleを0.55、UIを等倍で描画しました。各状態を1280×720と1920×1080で出力し、元画像と原寸切り出しで文字と輪郭を確認します。定点の回転数・タイムは自然な全周走行の記録ではありません。`Live/` の進行検証もテスト入力・テスト用ゴール通知を使っており、結果のタイムは確認値です。

夜景が暗いチューブでも、数字と目盛りは背景から読み分けられます。UIだけを等倍にする構成により、3D縮小時も文字と計器の鮮明さを維持します。

## 検証と再実行

検証結果：[記録](../Artifacts/NightArcadeIntegration/validation-summary.json) / [EditMode詳細](../Artifacts/NightArcadeIntegration/editmode.xml) / [統合チェック](../Artifacts/NightArcadeIntegration/integration-checks.txt)

- EditMode：152 / 152 成功。
- UI／音声／通知／夜景素材の保持：成功。
- 車選択：P2先行・長押し・固定色・練習との並行・時間切れ・レース開始、成功。
- 描画：3Dが0.5で、UIカメラの描画中は1.0、描画後は0.5へ戻ることを実行時に確認。
- 終了：各プレイヤーの表示・ペダル踏みっぱなし防止・個別タイトル復帰、成功。
- 2人用進行：加速・カウントダウン・ゴール・観戦・実リプレイ・結果・リトライ・両者中断・ゴール演出中の中断と時間切れ、成功。
- コース撮影：7地点×静止／移動の14状態＋練習／結果、各720p・1080p。撮影の欠落なし。

既存の未保存Unityシーンと未コミット文書を保護するため、一時コピーの同一ソースで検証しました。実ディスプレイ2台・会場機のフレーム時間・4K・実ペダルの操作感は今回の確認対象に含みません。

```sh
# UNITY は Unity 6000.3.9f1 の実行ファイル、PROJECT は検証対象のプロジェクト。
"$UNITY" -batchmode -projectPath "$PROJECT" -executeMethod NightArcadeIntegrationValidation.Run -quit -logFile /tmp/night-ui-checks.log
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" -runTests -testPlatform EditMode -testResults /tmp/night-ui-tests.xml -logFile /tmp/night-ui-tests.log
"$UNITY" -batchmode -projectPath "$PROJECT" -executeMethod CarSelectionPlayModeValidation.Run -logFile /tmp/night-ui-car.log
"$UNITY" -batchmode -projectPath "$PROJECT" -executeMethod ArcadeEndingPlayModeValidation.Run -logFile /tmp/night-ui-ending.log
"$UNITY" -batchmode -projectPath "$PROJECT" -executeMethod MultiplayerPlayModeValidation.RunBatch -logFile /tmp/night-ui-flow.log

RACING_VISUAL_TOUR_MODE=fixed RACING_VISUAL_VARIANT=full \
RACING_VISUAL_RESOLUTIONS=1280x720,1920x1080 RACING_VISUAL_RENDER_SCALE=0.55 \
RACING_VISUAL_TOUR_OUTPUT=/tmp/night-ui-course \
"$UNITY" -batchmode -projectPath "$PROJECT" -executeMethod NightArcadeIntegrationValidation.CaptureCourse -logFile /tmp/night-ui-course.log
```

Play Modeの検証・撮影は完了時に自分でEditorを終了するため、`-quit`を付けません。グラフィックスを必要とする確認に`-nographics`を付けないでください。同じプロジェクトへの実行は順番に行います。
