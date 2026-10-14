# 2人ローカル対戦（フェーズ1〜5）

## 実行条件

- 1台のマイコンをUSB接続し、P1/P2の4列入力を改行区切りで送信できること（P1シリアル／P2キーボードの検証時はP1の先頭2列だけ有効でよい）
- PCにモニターを2台接続してからゲームを起動すること
- `SampleScene` の `InputManager > Is Debug Mode` は通常プレイでは無効にすること

現在の保存済み `SampleScene` は両者をキーボードで操作する検証設定です（`Is Debug Mode` ON、`Is P1 Serial P2 Keyboard Debug Mode` OFF）。P1はEnterまたはWを0.25秒押し、P2はRight Shiftまたは上矢印を0.25秒押すと、それぞれ車種選択へ進みます。車種を確定して両者の練習が完了するとレースが始まります。P1/P2ともESP32で操作する実機対戦では、両方のデバッグ設定を無効にしてください。

ゲーム起動時にDisplay 1（2台目）を有効化し、次の表示を割り当てます。

- Display 0: Player 1の追従カメラとHUD
- Display 1: Player 2の追従カメラとHUD
- タイトルとリザルト: 両Displayに同じ内容

## Macの内蔵画面を使わず、外部2枚に出力する

このPCは `MacBookPro17,1`（13インチMacBook Pro / M1 / 2020）で、標準のDisplayPort/HDMI出力は外部1枚までです。通常のUSB-C/HDMI変換やドックだけでは、外部2枚を別映像で使うことはできません。外部2枚にはDisplayLink対応の追加出力機器・ドライバーなど、または外部2枚へのネイティブ出力をサポートするPCが必要です。Appleの仕様: https://support.apple.com/en-us/111893 。以下の設定は、OSが外部2枚と内蔵画面を別々の拡張ディスプレイとして認識した場合に使用します。

UnityはP1のメインウィンドウと、追加で有効化したP2のディスプレイへ描画します。外部モニターを接続しただけでは、メインウィンドウが内蔵画面で起動することがあります。

1. 3画面を接続し、macOSの「システム設定 > ディスプレイ」でミラーリングを解除します。
2. P1に使う外部画面を選択し、「使用形態 / Use as」を「メインディスプレイ / Main display」にします。OSのメイン設定はゲーム起動前に変更してください。
3. Unityで `SampleScene` を開き、`GameManagers > GManager > Gmanager > Displays > Player Two Display` を、P2に使う外部画面の番号にします。番号はUnityのGameビューと同じ1始まりです（2または3）。OSの画面配置とUnityの番号は必ずしも同じ順序ではありません。
4. シーンを保存して Build And Run を行います。P2が内蔵画面へ出た場合は `Player Two Display` を2から3（または3から2）へ変更して保存・再ビルドします。P1はDisplay 1のメインウィンドウ、P2だけが指定した追加ディスプレイを有効化します。
5. 内蔵画面をEditorや管理画面用に使い、外部2枚にそれぞれP1/P2が出ることを確認します。音声機器は映像の番号とは独立して選択します。

EditorではGameビューを2つ開き、P1をDisplay 1、P2を `Player Two Display` の番号にします。各Gameウィンドウは好きな物理画面へ移動できます。Editorで仮想ディスプレイをプレビューする際は、接続台数不足のエラーを出しません。

## 異なる解像度の画面での描画修正（Unity 6000.3.9f1 / URP 17.3）

Macの実行ログで、1920×1080と2880×1800の描画先が混同されるMetalエラーを確認しました。URPの `UniversalRendererRenderGraph.ImportBackBuffers` が追加ディスプレイでも `Screen.width/height`（メイン画面のサイズ）を使うため、異なる解像度ではRenderPassの添付バッファサイズが一致しません。修正前のログにはRenderPass／SubPass関連エラーが計34,210行ありました。

このプロジェクトでは以下の回避を適用しています。

- Desktopの `Scripting Define Symbols` に `URP_COMPATIBILITY_MODE` を設定し、URP Global Settingsの `Compatibility Mode (Render Graph Disabled)` を有効化。
- `PC_Renderer` のネイティブレンダーパスを無効化して、互換モードでも画面ごとの描画先を扱える経路を使用。
- UI倍率は各リグの出力サイズと16:9の描画領域から計算。別画面の描画中に変わりうるOverlayカメラのpixelWidth/Heightは参照せず、倍率が変わったときだけCanvasへ反映。

Unityを開いたままファイルを更新した場合は、`Racing > Validation > Enable Desktop Display Compatibility` を実行し、コンパイル完了後に `Racing > Validation > Display Outputs` で設定・表示番号・解像度の異なる2出力の倍率安定性を検証できます。`Racing > Build > Mac Display Validation` は `Builds/DisplayValidation/Race.app` へ検証用Development Buildを作成します。

互換モードはUnity 6.3で非推奨のため、この回避は現在のUnity版向けです。Unity/URP更新時は追加画面のバックバッファサイズ処理を確認し、異なる解像度の実機2画面で表示・ログを検証してから解除してください。既存の `build.app` には修正は反映されないため、再ビルドが必要です。

2026-10-13の検証では、Apple M1 / Metalで2880×1800と1920×1080へ同時出力するDevelopment Buildを起動し、タイトル・車種選択・練習への遷移を確認しました。RenderPassのサイズ不一致、無効なSubPass、managed例外はいずれも0件でした。4組の異なる出力サイズで倍率安定性を検証し、Display 1/3の割り当てもEditorで確認済みです。結果は `Logs/DisplayValidation/standalone.json` に保存しています。FPSの比較測定と外部2枚の物理接続による確認は未実施です。

## レース進行

1. 両プレイヤーがペダルを離すと開始入力を受け付けます。
2. P1、P2がそれぞれペダルを押す（またはプロトコルのReadyを送る）とレースを開始します。
3. 2台の車両はスタート地点の左右に4m間隔で生成されます。
4. 両Displayに同じ3秒カウントダウンを表示し、その間は車両入力を無効化します。
5. プレイヤーが完走すると、そのプレイヤーのDisplayだけにゴール画面を表示し、車両を停止して衝突判定を無効化します。
6. ゴール画面の後、完走済みプレイヤーのDisplayは未完走プレイヤーのカメラとHUDへ切り替わります。画面外周の枠と `WATCHING P2` のようなラベルで観戦対象を示します。
7. 未完走プレイヤーのDisplayだけに残り時間を表示し、完走を最大40秒待ちます。時間切れの場合は未完走車をDNFとして確定します。
8. 全員の完走または時間切れ後、両Displayに同じ順位・タイム（またはDNF）を表示します。

レース中のメニューで「中断する」を選ぶと、そのプレイヤーは即座にDNFになり、車両はコース上の障害物にならないよう衝突判定を無効化します。もう一人が走行中なら観戦へ切り替え、両者が中断した場合はリザルトへ進みます。ゴール演出中の中断では、演出が終わってから両画面にリザルトを表示します。

リザルトのペダル操作は、画面表示後に一度ペダルを離してから受け付けます。ハンドルで選択し、ペダルを1秒踏んで確定します。「リトライ」は練習を繰り返さず、3秒カウントダウンから次のレースを開始します。両者がタイトルへ戻った場合は、次の開始時に練習を実施します。

`Gmanager` の `Start Grid Spacing` と `Second Place Timeout Seconds` で、それぞれスタート間隔と待ち時間を調整できます。待ち時間の標準値は40秒です。

## キーボード確認

Unity Editorで `InputManager > Is Debug Mode` を有効にすると、ESP32なしでも確認できます。

- P1: WASD / Space / Enter
- P2: 矢印キー / Right Ctrl / Right Shift

P1はSpace、P2はRight Ctrlで、現在地から最寄りのコース路面へ復帰します。これらのリセットキーはESP32のシリアル操作時も使用できます。ジャンプ区間の空白を避け、路面の傾きとレースの進行方向に合わせて車を配置します。完了した周回・チェックポイントは維持します。中断メニューの `スタートから` は開始位置へ戻る操作です。

シリアル仕様は [ESP32SerialProtocol.md](ESP32SerialProtocol.md) を参照してください。

自動検証と実機チェックリストは [MultiplayerValidation.md](MultiplayerValidation.md) を参照してください。
