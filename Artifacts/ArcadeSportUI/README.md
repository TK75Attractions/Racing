# デザイン2 / 実装確認画像

Unity 6000.3.9f1 の製品用 UI コードを使用した確認画像です。実装・再検証の手順は [ArcadeSportUI.md](../../Documentation/ArcadeSportUI.md) を参照。

## 実シーン（Live）

SampleScene を Play Mode で動かして撮影しました。固定背景の合成ではありません。テスト用入力とゴール通知で画面を進めています。レースのタイムはテスト値です。

| 画面 | P1 | P2 |
| --- | --- | --- |
| タイトル | [表示](Live/title-p1.png) | — |
| 車選択 | [表示](Live/car-p1.png) | [表示](Live/car-p2.png) |
| チュートリアル | — | [表示](Live/tutorial-p2.png) |
| コース紹介 | [表示](Live/course-preview-p1.png) | — |
| カウントダウン | [表示](Live/countdown-p1.png) | — |
| 走行 HUD | [表示](Live/race-p1.png) | — |
| 音量・出力設定 | [表示](Live/settings-p1.png) | — |
| 結果・実リプレイ | [表示](Live/result-p1.png) | — |
| 両者未完走 | [表示](Live/dnf-p1.png) | — |
| 終了画面 | [表示](Live/ending-p1.png) | [表示](Live/ending-p2.png) |

## レイアウト・状態別（固定背景）

ルートの PNG は Unity の Canvas で描画したレイアウト確認用です。背景は main の実画面を固定し、速度・回転数等の値も固定しています。リプレイがない状態も確認できます。

| 状態 | P1 | P2 |
| --- | --- | --- |
| タイトル | [表示](title-p1.png) | [表示](title-p2.png) |
| 練習 | [表示](tutorial-p1.png) | [表示](tutorial-p2.png) |
| カウントダウン | [表示](countdown-p1.png) | [表示](countdown-p2.png) |
| レース | [表示](race-p1.png) | [表示](race-p2.png) |
| 観戦 | [表示](spectator-p1.png) | [表示](spectator-p2.png) |
| 控えめな接近通知 | [表示](quiet-p1.png) | [表示](quiet-p2.png) |
| ブースト発動 | [表示](boost-p1.png) | [表示](boost-p2.png) |
| ESC | [表示](pause-p1.png) | [表示](pause-p2.png) |
| 設定 | [表示](settings-p1.png) | [表示](settings-p2.png) |
| 結果 | [表示](result-p1.png) | [表示](result-p2.png) |
| 終了 | [表示](ending-p1.png) | [表示](ending-p2.png) |
| 実況 LIVE | [表示](broadcast-p1.png) | [表示](broadcast-p2.png) |
| 実況 REPLAY | [表示](replay-p1.png) | [表示](replay-p2.png) |
| コース PV | [表示](pv-p1.png) | [表示](pv-p2.png) |

## 確認内容

- EditMode: 152 / 152 件成功。
- 実車選択: P2 先行、長押し決定、練習との並行、車種・性能・塗装の引継ぎ、練習の時間切れ、レース開始まで成功。
- 実レース: 加速、カウントダウン、ゴール、観戦、実リプレイ、結果、リトライ、各自のタイトル復帰、両者中断、ゴール演出中の中断・時間切れを確認。
- 終了画面: 描画後の P1/P2 撮影、踏みっぱなし防止、相手の結果を残したまま個別にタイトルへ戻る動作を確認。
- 見た目: 透過ロゴ、メーターの透け、タイム下の短い通知、ボタンの目盛り・斜線、文字欠け、重なりを画像で確認。
