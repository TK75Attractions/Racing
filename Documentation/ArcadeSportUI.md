# Arcade Sport UI（デザイン2）

`origin/main` の `23ec146` を基に、`codex/arcade-sport-ui` で実装。

2026-10-28: このブランチへ最新mainを取り込んだ内容と確認手順は [夜景とmainの統合確認](NightArcadeMainReconciliation.md) を参照。

2026-10-26: 夜景との試験統合は `codex/night-ennichi-arcade-ui`。[統合内容・実画面・改善案](NightArcadeIntegration.md) を参照。このブランチでは `NativeResolutionUI` による3D縮小＋UI等倍合成を使用します。

承認済みのデザイン2を共通の表示にしました。P1 は青、P2 はピンク。速度・回転数の主数字は同じ大きさの Barlow Condensed ExtraBold、その他の数字は Barlow Condensed Bold、日本語は M PLUS の静的 HUD フォントです。SVG のロゴは透過画像として景色へ直接重ね、輪郭だけ白く補って可読性を保ちます。

## 画面

| 画面 | 実装 |
| --- | --- |
| タイトル | 透過ロゴ、メーター型のアクセントを持つ開始ボタン、プレイヤー表示 |
| 車選択 | 台座と一緒に回る実車モデル、P1青／P2ピンク固定、性能バー、車種カード、ペダル長押し決定 |
| チュートリアル | レースと共通の速度・回転数／ブースト・ハンドル表示、現在の課題、残り時間。順位・レースタイムは非表示 |
| コース紹介 | 既存のコースカメラと実際のコース形状を使うプレビュー |
| カウントダウン | レース HUD の上へ信号と分割リング付き数字を表示 |
| レース | 速度、実回転数、ブースト、実際に適用されたハンドル角、順位、周回、タイム、LAP1/LAP2 |
| 注釈 | ブースト等は右メーター上の小さな斜めの帯と速度線。接近・与えたアイテム効果・ジャンプ予告はタイム下の小さな通知へ集約 |
| ESC・設定 | 同じ配色とボタン、音量と出力先の既存機能を維持 |
| 結果 | 左は既存の実リプレイ、右は自分の順位・タイムと全員の順位表。日次順位は「本日N位！」だけを表示 |
| 終了 | 「プレイありがとうございました！」を各プレイヤー個別に表示し、タイトルへ戻る |
| 実況・リプレイ・PV | 既存の映像・カメラを維持し、ヘッダーとテロップを共通の意匠に更新 |

## 表示と入力の接続

- 回転数は `CarSoundController.CurrentRpm`。速度からの推定値ではありません。
- 右メーターの外周はドリフトの蓄積とブーストの残量を表示します。ロケットの残量にも対応。
- ハンドルは既存の可動角インディケーターを使用し、要求角と制限後の実角を区別します。
- 2周目にLAP1の確定タイムとLAP2の進行中タイムを表示します。初周は非表示で、ゴール時は両周の確定値を保持します。リトライ・観戦先変更にも対応します。
- 走行中の案内は中央パネルを作らず、タイム下の通知に統一します。競合時は入力・復帰、ジャンプなどの安全案内、効果確認、後方接近、前方の相手、ドリフト等の案内の順で一つを表示します。
- 終了画面・車選択・結果からの決定は、ペダルを離してからの操作が必要です。ゴール時の踏みっぱなしで次の画面へ進みません。
- 順位・タイムは既存のセッション結果を利用します。未完走は `DNF`、表示ミリ秒とランキング比較の丸めを共通化しました。

## 2026-10-24 の調整

- タイトルの NEWS 行を削除。
- 車選択の色スウォッチを撤去し、過去の保存色も無視。車は目盛り付きの台座に載せ、台座と同じ親で回転。
- HUD に右下の硬い影を追加。速度を 154pt の ExtraBold に拡大し、速度に応じて指針と点灯部分を青／ピンクからミント、黄、オレンジへ変化。
- タコの警告帯はオレンジ＋白い斜線。P2のピンクと形・色の両方で区別。
- 結果の展開ボタンと日次一覧を撤去し、自分の「本日N位！」を常時表示。
- URP の共通縮小は HUD も低解像度にしていたため、`RaceRenderBudget` は等倍描画を維持するよう変更。図形には透明な縁を設けてアンチエイリアスを追加。720p、1080p、4K の描画画像を保存。

等倍描画では従来の縮小描画より GPU 負荷が増えます。特に 4K の実機フレーム時間は今回の Editor 検証では保証していません。旧性能測定と現在の描画方針の違いは [StandalonePerformance.md](StandalonePerformance.md) を参照。

## HUD の追加調整

- 速度と回転数の主数字を 130pt の ExtraBold に統一（以前は速度154pt、回転数108pt）。見出しと単位は20ptから24ptへ拡大。
- 周回の下線は2分割。1周目は左だけ、2周目は左右が点灯し、リトライで左だけに戻る。
- 順位にも共通の明るい背景と右下の影を追加。
- ブースト・アイテム発動・被弾の帯は右端のメーター上に縮小配置。中央の走路を空け、拡大演出を止めて1.6秒以内にフェード。効果の残り時間も同じ右端へまとめ、接近等の通知とは重ならないようにする。

## 中央の走行メッセージを廃止

最新main（54a7232）の通知キューを維持しつつ、`DriverNotice` の中央カード生成を撤去。「ドリフト加速」「曲がってためる」「この先ジャンプ」、着地・追走・復帰・接続の案内は `QuietRaceNotice` だけに表示する。入力再開と安全案内は画面端でも優先され、結果・ポーズで消去する。長い入力案内は同じ幅の中で折り返す。ブーストとアイテムは既存の右端の帯を維持する。

## DEBUG RACE CONTROLS の非表示

Hierarchy で `Gmanager` コンポーネントが付いたオブジェクトを選び、Inspector の **Debug UI > Show Debug Race Controls** をOFFにする。デバッグ用のキーボード入力は維持される。Play Mode中の変更は終了時に戻るため、保存する場合は停止中に変更してシーンを保存する。

この設定は周回加算・GOAL・PREVIEW RESULTがある小さなパネルを対象とする。別のF8機能設定パネルはF8（Macの設定によってはfn+F8）で閉じる。

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
