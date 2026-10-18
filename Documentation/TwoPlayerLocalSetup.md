# 2人ローカル対戦（フェーズ1〜5）

## 実行条件

- 1台のマイコンをUSB接続し、RGP/1のセッション確立・状態通知と、P1/P2の6項目TEL送信に対応していること（P1シリアル／P2キーボードの検証時も2人分すべての値が必要）
- macOS（Metal）版は1台のモニターでもP1/P2を別ウィンドウで表示できます。Windows版のP2は追加ディスプレイが必要です。
- `SampleScene` の `InputManager > Is Debug Mode` は通常プレイでは無効にすること

現在の保存済み `SampleScene` は両者をキーボードで操作する検証設定です（`Is Debug Mode` ON、`Is P1 Serial P2 Keyboard Debug Mode` OFF）。P1はEnterまたはWを0.25秒押し、P2はRight Shiftまたは上矢印を0.25秒押すと、それぞれ車種選択へ進みます。車種を確定して両者の練習が完了するとレースが始まります。P1/P2ともESP32で操作する実機対戦では、両方のデバッグ設定を無効にしてください。

## P1・P2を通常ウィンドウで表示する

StandaloneはP1/P2ともタイトルバー付きの移動・リサイズ可能なウィンドウで表示します。初期サイズは960×540（macOSでは画面の作業領域に収まるよう調整）です。以前のビルドで保存されたフルスクリーン設定も起動時にウィンドウへ切り替えます。フォーカスが外れてもゲーム進行とESP32入力を継続します。

- P1: Unityのメインウィンドウ（アプリ名 `Race`）
- P2: 独立ウィンドウ（`RACING — P2`）。タイトル・車種選択・練習・レース・リザルトまでP2のカメラとHUDを表示します。
- macOSではUnityの追加ディスプレイをActivateせず、AppKitの通常ウィンドウとMetalへ出力します。フルスクリーンの追加画面や専用Spaceを作りません。
- P2の初期配置は `GameManagers > GManager > Gmanager > Displays > Player Two Display` の画面番号を優先します。画面がなければP1と同じ画面へ開きます。macOSの画面列挙順はUnityのGameビューの番号と異なる場合があります。
- 起動後は両方のタイトルバーをドラッグし、好きなモニターへ移動できます。OSのメインディスプレイを変更して再ビルドする必要はありません。
- 各ウィンドウのサイズ・Retina倍率に応じて描画先とUI倍率を調整し、16:9以外の領域は余白になります。
- macOSのP2はキーボードフォーカスを奪わず、両者のキーボード操作はP1ウィンドウで受け付けます。キーボードで検証するときはP1をクリックしてください。
- macOSでP2の閉じるボタンを押すとゲーム全体を終了します。P1の終了時もP2ウィンドウを破棄します。

Unityでは `Racing > Build > Mac Player Windows` を実行すると `Builds/DisplayValidation/Race.app` へDevelopment Buildを作成します。旧メニュー `Mac Display Validation` も同じ処理です。既存のアプリには再ビルドが必要です。

macOSプラグインは `Native~/RaceWindows/RaceWindows.mm` がソースで、`sh Native~/RaceWindows/build-macos.sh` でarm64/x86_64のUniversal dylibを生成します。Unityの別バージョンを使う場合は `UNITY_PLUGIN_HEADERS` でそのEditorの `Contents/PluginAPI` を指定します。生成物 `Assets/Plugins/RaceWindows/macOS/libRaceWindows.dylib` をPlayerに同梱してください。検証時だけ環境変数 `RACING_WINDOW_CAPTURE` にPNGの保存先を指定すると、P2に転送した10フレーム目の描画画像をGPUから読み戻して保存します。通常起動時には画像保存やCPUへの読み戻しは行いません。Editorではロードせず、これまでどおりGameビューを2つ開いてDisplay 1と `Player Two Display` をプレビューします。

WindowsではUnityの追加ディスプレイを作成後に通常ウィンドウ枠へ変更し、クライアント領域に合わせて描画解像度を更新します。macOS版の実装はMetal専用です。Linuxや他の描画APIでのP2ウィンドウ化は対象外です。

2026-10-17、Unity 6000.3.9f1 / Apple M1でMacビルド、起動・レース描画・終了を確認しました。Editorの出力先／異なるサイズのターゲット再生成／UI倍率の検証と、Windows用コードのコンパイルも成功しています。Standaloneの検証レポートは `Logs/PlayerWindows/verified/report.json`（`errors: []`）、P1画像は `optimized.png`、P2へ転送した実描画画像は `p2-presented.png` です。P1/P2それぞれの車とHUD、上下方向を確認しました。外部モニターでのチラつき解消、OS操作によるウィンドウの連続リサイズ、Windows実行は未確認です。

## 異なる解像度の画面での描画修正（Unity 6000.3.9f1 / URP 17.3）

Macの実行ログで、1920×1080と2880×1800の描画先が混同されるMetalエラーを確認しました。URPの `UniversalRendererRenderGraph.ImportBackBuffers` が追加ディスプレイでも `Screen.width/height`（メイン画面のサイズ）を使うため、異なる解像度ではRenderPassの添付バッファサイズが一致しません。修正前のログにはRenderPass／SubPass関連エラーが計34,210行ありました。

このプロジェクトではRender Graphを有効にし、Desktopの `Scripting Define Symbols` から `URP_COMPATIBILITY_MODE` を取り除いています。定義だけが残っていると、EditorとPlayerのURPリソース型（`Renderer2DResources`）のフィールドが食い違い、Build And Runでシリアライズエラーになる場合があります。

P2は `RenderGraphDisplayOutput` が出力サイズに合った深度付きRenderTextureを作り、背景・レース・UIのカメラスタック全体をRender Graphで描画します。macOSではウィンドウのクライアント領域の物理ピクセル数を使い、描画後にUnityのMetalコマンドバッファでP2のCAMetalLayerへ転送・表示します。フレームごとのテクスチャ参照を保持し、リサイズや終了時も描画スレッドが参照しているテクスチャを保護します。Windowsの追加画面は `renderingWidth/Height` を使い、従来のネイティブcolorBufferへ転送します。これによりURP 17.3のメイン画面サイズによるバックバッファimportを通らず、異なる解像度の出力を扱えます。画面サイズの変更時はターゲットを作り直し、停止時に解放します。メイン画面とEditorのGameビューは通常のURP出力を使います。

追加画面への転送は、完成した背景・レース・UIスタックを確認した後、SRPの `CommandBuffer` で1フレームに1回だけ行います。色バッファを全面上書きし、Storeを明示して `ScriptableRenderContext.Submit()` で提出します。即時描画の `Graphics.DrawProceduralNow` は使用しません。背景だけの描画要求で未完成の画像を転送することも防ぎます。SRPの提出順序は [Unityの説明](https://docs.unity.cn/Packages/com.unity.render-pipelines.core%4017.0/manual/srp-using-scriptable-render-context.html) を参照してください。

PC品質設定のVSyncを有効にして無制限描画を避け、`Run In Background` も有効にしています。別画面や管理画面へフォーカスを移してもゲームの更新を継続します。物理演算の時間刻みや車の移動処理は変更していません。`Display Outputs` の検証には、不完全なスタック・同じフレーム内の重複転送の抑止と、交互の描画先への8フレーム分のGPU転送・全画素読み戻しを含めています。実際のDisplayLinkドライバー／USB接続でのちらつきは別途実機確認が必要です。

UI倍率は各リグの出力サイズと16:9の描画領域から計算します。別画面の描画中に変わりうるOverlayカメラのpixelWidth/Heightは参照せず、倍率が変わったときだけCanvasへ反映します。

Unityを開いたまま設定を更新する場合は `Racing > Validation > Enable Desktop Render Graph` を実行し、コンパイル完了後に `Racing > Validation > Display Outputs` でRender Graph設定・表示番号・異なる解像度の倍率・RenderTextureのリサイズと解放を検証します。`Racing > Build > Mac Display Validation` は `Builds/DisplayValidation/Race.app` へ検証用Development Buildを作成します。既存の `build.app` には修正は反映されないため、再ビルドが必要です。

2026-10-13の互換モードによる実機検証記録は `Logs/DisplayValidation/standalone.json` にあります。現在のRender Graph経路とは区別してください。

2026-10-14、Render Graphを有効にしたSampleSceneのMac Development Buildが成功し、Standaloneでタイトル・車種選択・練習・レースまでの描画を確認しました。検証環境は物理画面が1台のため、P2は1920×1080のRenderTextureを使った模擬出力で確認しています。物理2画面の同時出力は未検証です。条件・結果・P2画像は `Artifacts/RenderGraphBuild` に保存しています。

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
- P2: 矢印キー / /（スラッシュ、Right Ctrlも使用可） / Right Shift

P1はSpace、P2は/（スラッシュ、Right Ctrlも使用可）で、現在地から最寄りのコース路面へ復帰します。これらのリセットキーはESP32のシリアル操作時も使用できます。ジャンプ区間の空白を避け、路面の傾きとレースの進行方向に合わせて車を配置します。完了した周回・チェックポイントは維持します。中断メニューの `スタートから` は開始位置へ戻る操作です。

シリアル仕様は [ESP32SerialProtocol.md](ESP32SerialProtocol.md) を参照してください。

自動検証と実機チェックリストは [MultiplayerValidation.md](MultiplayerValidation.md) を参照してください。
