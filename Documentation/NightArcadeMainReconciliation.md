# 夜の縁日ブランチとmainの統合確認

確認日：2026-10-28。作業ブランチ：`codex/night-ennichi-arcade-ui`。

統合前のこのブランチは `2f6264f`、取り込んだmainは `b21b790`（PR #61まで）。共通祖先は `6eb6360`。このブランチ固有の18コミットとmain側の96コミットを、履歴を残すマージで合わせた。mainへのマージ・pushは行っていない。

## 変更点の整理

| 分野 | このブランチで保持する内容 | mainから取り込む内容・調整 |
| --- | --- | --- |
| 学校と風景 | 夜空、校舎の窓明かり、淡い桜、提灯、ネオン行灯、反射プローブ、昼夜切り替え | 昼用キャンパス生成が夜のマテリアルと桜を置換しない保護を維持 |
| 路面とバリア | アスファルトの色むら・タイヤ跡、舗装端と草、発光縁石、バリアの支柱・レール | mainのジャンプ着地・チューブ入口・出口の接続と当たり判定を優先し、表示用の質感を重ねる |
| チューブ | 発光帯・矢印・区間番号・天井リブ、入口と出口の門 | mainの滑らかな走行面を使用。発光模様も同じ滑らかな格子から作り、旧FBX用のベイクへ戻らないよう調整 |
| 車と演出 | 車体の揺れ、夜間発光、参加者の札、衝突・着地・走行演出 | mainの操舵・コーナー・ロケット・ジャンプの改善を保持 |
| UIと描画 | 新UI、青／ピンクの固定色、3Dを縮小してもUIは等倍で表示、夜景タイトル | mainのUIアニメーション、同じ大きさの速度／回転数、結果選択、画面端の通知と効果残り時間を保持 |
| 運転通知 | 逆走・急カーブ警告、成功イベントを一度だけ表示する通知制御 | 共通Coreの通知キューに一本化。入力／復帰待ち > 安全 > アイテム結果 > 接近／運転案内。解決済みの接続待ちは即座に解除 |
| アイテムと練習 | 実アイテム取得課題、夜景での視認性 | mainの「？」付き発光ボックス、効果の説明、取得までの猶予・誘導、走行視界を空ける通知を採用 |
| 音 | 車両位置別の効果音、BGMフェード、通知時の音量低下、会場プリセット | mainの音声安定化・保守モードでの消音、Windows x64の自動ビルドを保持。ARM64の出力先も維持 |
| 運営・実機 | 画質プリセット、負荷に応じた3D解像度、描画区画分割、計測・単独デバッグ補助 | mainのダッシュボード、保守モード、入力デバイス診断、Windows起動・画面対応・運用手順を保持 |

## マージ時の修正

- 19ファイルのテキスト競合を内容に応じて解消。シーンは夜景の配置を保ち、mainのコース参照・旧壁の無効化・操舵設定・遷移時間を取り込んだ。
- 通知クラスは両ブランチに存在していたため、`Racing.Multiplayer.Core` 側を正本とし、夜景側の成功イベント用メールボックスをそこへ統合した。
- 長押しは押した時点を起点に、保守停止時間を除いた共通表示時計で測る。重い描画の直後や保守モードからの復帰だけで決定しない。
- チューブ模様は滑らかな走行面に合わせて再生成し、再生成時には前の表示メッシュと入口・出口の門を解放する。
- 日本語フォントは統合後の表示文言から静的アトラスを生成する。古い両側のアトラスをテキスト結合しない。

## 確認方法

この作業場所を Unity **6000.3.9f1** で開き、`Assets/Scenes/SampleScene.unity` を再生する。

- タイトル、車選択、練習、レース、結果、個別タイトル復帰を確認する。
- スタートの桜・行灯、校舎前、チューブ入口・内部・出口、ジャンプ前を比較する。
- F8で画質と演出、ダッシュボードで保守モードを切り替え、UIの鮮明さと停止・再開を確認する。
- Editorで一人で進める場合は、デバッグ入力を有効にして `ALL PLAYERS` の進行ボタンを使える。

自動の統合チェックは `Racing > UI > Validate Night Arcade With Main`、バッチでは `NightArcadeIntegrationValidation.RunWithMain`。再生中は実行せず、バッチでは描画を有効にする。

実機2画面、会場のスピーカー、ペダル／ハンドルの感触、本番Windows機でのフレーム時間は現地確認が必要。以前からのタイトル構図とリプレイの桜による遮蔽の検討は [NightArcadeIntegration.md](NightArcadeIntegration.md) に記録している。

## 今回の確認結果

[検証記録](../Artifacts/NightArcadeMainIntegration/verification.json) / [チェックの出力](../Artifacts/NightArcadeMainIntegration/checks.txt) / [EditMode詳細](../Artifacts/NightArcadeMainIntegration/editmode.xml)

- UnityのコンパイルとEditModeテスト：**170 / 170 成功**。
- ダッシュボード：**59件成功・1件スキップ・失敗0**。スキップは任意実行のブラウザー診断テスト。
- UI・音源・通知の優先度、通知クラスの一本化、入力復帰、アニメーション、操舵、チューブ：成功。高速コーナー31ケース、急ハンドル86ケースを含む。
- 車選択：P2先行、長押し、個別練習、時間切れ、同時レース開始を確認。
- 2人用進行：実入力の加速、カウントダウン、アイテム通知、観戦、リプレイ、結果選択、リトライ、個別タイトル復帰、両者離脱、ゴール演出中の離脱・時間切れを確認。
- 保守モード：タイトルから結果までの7段階、P1・P2・観戦の3画面、停止中の物理・時間・音・UI、再開、無効化／再有効化、再起動後の設定復元を確認。
- 描画データ：滑らかなチューブを含む6つの元Rendererを68区画へ再生成。模様用メッシュも同じ99,716頂点・1,028リングからベイク。再生成後の形状一致・表示の重複防止を確認。
- 定点撮影：7地点×静止／走行＋練習・結果の16状態、1920×1080。3D scale 0.55、UI等倍。撮影の欠落なし。

初回の読み込みで空き容量不足となったため、今回作成したキャッシュを整理し、既存キャッシュを独立コピーして再利用した。コピー由来のEditor検索索引エラーは索引を再作成して解消し、最終の統合チェックではプロジェクト例外・シェーダーエラー・検索索引エラーはいずれも0。モデルとフォントのLFS実データも展開済み。

## 統合後の確認画像

[コース全地点](../Artifacts/NightArcadeMainIntegration/Course/contact-sheet.png) / [スタートと桜](../Artifacts/NightArcadeMainIntegration/Course/01-start-grid-moving.png) / [チューブ内](../Artifacts/NightArcadeMainIntegration/Course/04-tube-inner-moving.png) / [チューブ出口](../Artifacts/NightArcadeMainIntegration/Course/05-tube-exit-moving.png)

[タイトル](../Artifacts/NightArcadeMainIntegration/Live/title-p1.png) / [車選択](../Artifacts/NightArcadeMainIntegration/Live/car-p1.png) / [走行P1](../Artifacts/NightArcadeMainIntegration/Live/race-p1.png) / [走行P2](../Artifacts/NightArcadeMainIntegration/Live/race-p2.png) / [アイテム](../Artifacts/NightArcadeMainIntegration/Live/item-p1.png) / [結果](../Artifacts/NightArcadeMainIntegration/Live/result-p1.png) / [終了](../Artifacts/NightArcadeMainIntegration/Live/ending-p1.png) / [保守モード](../Artifacts/NightArcadeMainIntegration/Maintenance/player-1.jpg)

画像はUnityの実シーン・カメラ・UIから撮影。進行試験はテスト入力とテスト用ゴール通知、定点撮影は位置・速度の制御を使っている。表示タイムは自然な全周走行の記録ではない。
