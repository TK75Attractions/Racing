# Arcade Sport UI（デザイン2）

`origin/main` の `23ec146` を基に、`codex/arcade-sport-ui` で実装。

承認済みのデザイン2を共通の表示にしました。P1 は青、P2 はピンク。数字は Barlow Condensed Bold、日本語は M PLUS の静的 HUD フォントです。SVG のロゴは透過画像として景色へ直接重ね、輪郭だけ白く補って可読性を保ちます。

## 画面

| 画面 | 実装 |
| --- | --- |
| タイトル | 透過ロゴ、メーター型のアクセントを持つ開始ボタン、プレイヤー表示 |
| 車選択 | 実際の車モデルと塗装、性能バー、車種カード、ペダル長押し決定 |
| チュートリアル | レースと共通の速度・回転数／ブースト・ハンドル表示、現在の課題、残り時間。順位・レースタイムは非表示 |
| コース紹介 | 既存のコースカメラと実際のコース形状を使うプレビュー |
| カウントダウン | レース HUD の上へ信号と分割リング付き数字を表示 |
| レース | 速度、実回転数、ブースト、実際に適用されたハンドル角、順位、周回、タイム、前周比 |
| 注釈 | ブースト等は斜めの帯と速度線。接近・与えたアイテム効果・ジャンプ予告はタイム下の小さな通知へ集約 |
| ESC・設定 | 同じ配色とボタン、音量と出力先の既存機能を維持 |
| 結果 | 左は既存の実リプレイ、右は自分の順位・タイムと全員の順位表。本日ランキングは開いたときだけ表示 |
| 終了 | 「プレイありがとうございました！」を各プレイヤー個別に表示し、タイトルへ戻る |
| 実況・リプレイ・PV | 既存の映像・カメラを維持し、ヘッダーとテロップを共通の意匠に更新 |

## 表示と入力の接続

- 回転数は `CarSoundController.CurrentRpm`。速度からの推定値ではありません。
- 右メーターの外周はドリフトの蓄積とブーストの残量を表示します。ロケットの残量にも対応。
- ハンドルは既存の可動角インディケーターを使用し、要求角と制限後の実角を区別します。
- 前周比は同じコース位置で前の周の経過時間と比較します。初周は「—」、リトライ・観戦先変更で比較をリセットします。
- 控えめな通知が競合した場合は、効果確認、後方接近、前方の相手、ジャンプ予告の順で一つを表示します。
- 終了画面・車選択・結果からの決定は、ペダルを離してからの操作が必要です。ゴール時の踏みっぱなしで次の画面へ進みません。
- 順位・タイムは既存のセッション結果を利用します。未完走は `DNF`、表示ミリ秒とランキング比較の丸めを共通化しました。

## 確認用画像

[Artifacts/ArcadeSportUI](../Artifacts/ArcadeSportUI) に両プレイヤーの画像を保存しています。

ルートの PNG は製品用の UI 構築コードを Unity の Canvas で描画したレイアウト確認画像です。背景には main の実画面を固定画像として使い、値も確認用に固定しています。動作中のゲーム映像のキャプチャとは区別してください。結果のリプレイは、この単独の確認シーンに実セッションがないため非表示になります。

`Live/` 内の画像は SampleScene の Play Mode を実行し、本番の車・カメラ・Canvas・URP で撮影したものです。

実際の画面一覧は [画像の索引](../Artifacts/ArcadeSportUI/README.md) を参照。

## 再確認

Unity 6000.3.9f1 で実行します。Play Mode の検証はそれぞれが終了時に Editor を終了するため、CLI では `-quit` を付けません。同じプロジェクトに対して順番に実行してください。

```sh
Unity -batchmode -nographics -projectPath "$PROJECT" -runTests -testPlatform EditMode -testResults /tmp/arcade-editmode.xml -logFile /tmp/arcade-editmode.log
Unity -batchmode -projectPath "$PROJECT" -executeMethod ArcadeUIValidation.RunAndCapture -quit -logFile /tmp/arcade-ui.log
Unity -batchmode -projectPath "$PROJECT" -executeMethod CarSelectionPlayModeValidation.Run -logFile /tmp/arcade-car.log
Unity -batchmode -projectPath "$PROJECT" -executeMethod MultiplayerPlayModeValidation.RunBatch -logFile /tmp/arcade-flow.log
Unity -batchmode -projectPath "$PROJECT" -executeMethod ArcadeEndingPlayModeValidation.Run -logFile /tmp/arcade-ending.log
Unity -batchmode -projectPath "$PROJECT" -executeMethod ArcadeUIValidation.RunRegressionChecks -quit -logFile /tmp/arcade-regression.log
```

Editor の `Racing > UI` から個別のレイアウト検証・画像出力も実行できます。既存の Modern UI / Design Preview の入口は新しい検証への互換入口に統一しています。

Barlow のライセンスは `Assets/Resources/UI/Arcade/Barlow-OFL.txt`。添付ロゴの元 SVG は同ディレクトリに保持しています。
