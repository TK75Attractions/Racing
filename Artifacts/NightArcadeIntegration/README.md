# 新UI × 夜の縁日：確認画像

統合ブランチ：`codex/night-ennichi-arcade-ui`。Unity 6000.3.9f1 / macOS / Metal。

[評価・改善案・再実行方法](../../Documentation/NightArcadeIntegration.md)

全画像は統合後のSampleScene、実際の車・カメラ・Canvas・URPで撮影しています。固定背景へのUI合成ではありません。テスト入力・テスト用のゴール通知を使用し、結果タイムは確認値です。定点撮影では位置・速度を制御しています。

## 画面

| 画面 | P1 | P2 |
| --- | --- | --- |
| タイトル | [表示](Live/title-p1.png) | [表示](Live/title-p2.png) |
| 車選択 | [表示](Live/car-p1.png) | [表示](Live/car-p2.png) |
| 練習 | — | [表示](Live/tutorial-p2.png) |
| コース紹介 | [表示](Live/course-preview-p1.png) | — |
| カウントダウン | [表示](Live/countdown-p1.png) | [表示](Live/countdown-p2.png) |
| 走行 | [表示](Live/race-p1.png) | [表示](Live/race-p2.png) |
| 観戦 | [表示](Live/spectator-p1.png) | — |
| 設定 | [表示](Live/settings-p1.png) | — |
| 結果・実リプレイ | [表示](Live/result-p1.png) | [表示](Live/result-p2.png) |
| 両者DNF | [表示](Live/dnf-p1.png) | — |
| 終了 | [表示](Live/ending-p1.png) | [表示](Live/ending-p2.png) |

## コースと解像度

[全地点](Course/contact-sheet.png) / [解像度比較](Course/resolution-sheet.png) / [元画像への索引](Course/resolution-index.html) / [撮影条件](Course/manifest.json)

スタート、校舎前、チューブ入口・内部・出口、ジャンプ前、急カーブを静止／移動で撮影し、練習・結果も追加。各720p・1080p、3D scale 0.55、UI 1.0。

## 検証

[結果](validation-summary.json) / [152件のEditModeテスト](editmode.xml) / [UI・音声・通知の統合チェック](integration-checks.txt)

車選択・終了・2人用進行も成功。3D縮小中にUIだけが等倍になり、描画後に3D縮小率が復元されることを実行時に検証しました。実機2画面と会場機のGPU性能は未測定です。
