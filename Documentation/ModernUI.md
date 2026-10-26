# Racing UI

現在の共通デザインは [Arcade Sport UI（デザイン2）](ArcadeSportUI.md) です。タイトルから終了、実況まで P1 は青、P2 はピンクを使用します。

`ArcadeUI` が配色・文字・ロゴ、`RacingHUDBuilder` が共通メーター、`ArcadeNoticeRail` が控えめな通知を担当します。`NeonUI` / `ModernUIValidation` は既存の呼び出しとの互換入口として残り、旧デザインの画面は構築しません。シーン上の旧 HUD は初期化時に隠すため、シーン移行は不要です。

`Racing > UI > Validate Arcade Sport UI` で結合を検証し、`Capture Arcade Sport UI` で両プレイヤーの描画を保存できます。試走を含む検証方法と画像の説明は上記ドキュメントを参照してください。

## Live viewport changes

`FreezeAspectRate` keeps the scene and UI in the same 16:9 viewport, including after `MainCamera` moves under the camera-shake pivot. Primary-window sizing uses the current window/Game view dimensions. Each player's canvas scales its 1920 × 1080 reference layout to the UI camera's actual pixel viewport, excluding letterbox bars, and refreshes before rendering.

**Racing > Validation > Viewport Resize and Tire Marks** changes both players' render targets during Play Mode through 1920 × 1080, 1280 × 720, 1680 × 720, 1024 × 768, 720 × 1280 and back to 1920 × 1080. It checks title, HUD and result layout bounds, captures examples in `Logs/ViewportValidation`, and checks the production car's tire-mark output through straight/sliding/straight motion while retaining drift charge. Batch execution uses `-batchmode -executeMethod ViewportAndTireValidation.Run` without `-quit`; the validation exits on completion.

## Spectator and ESC screens

観戦中も既存のカメラ追従を使用します。HUD は観戦先の速度・回転数へ切り替わり、配色は画面の持ち主に合わせます。ESC メニューは既存の再開・走行方向・設定・中断の処理を維持しています。

## 夜景との統合（2026-10-26）

`codex/night-ennichi-arcade-ui` は `codex/arcade-sport-ui` (`6eb6360`) と `raymee/night-ennichi-visuals` (`c0d6158`) を統合します。実画面と評価は [NightArcadeIntegration.md](NightArcadeIntegration.md) を参照。

- 速度・回転数・順位・前周比は新UIの構成を使用します。旧ミニマップ・旧タイトル背景・旧結果カードは構築しません。
- `DriverNoticeQueue` のイベント消費・優先度・最短表示時間は夜景ブランチから維持しました。通常の案内・成功・道路警告はタイム下の `ArcadeNoticeRail` へ表示します。道路警告はアイテム効果・接近通知より優先し、接続断・復帰待ち・ペダル解放待ちは中央に表示します。
- 自動運転・操舵補助・操舵減速の動作中表示はメーターの上へ小さくまとめます。レースを抜けると通知キュー・各イベント・レールを消去します。
- タイトル・結果は `MenuSceneryDirector` の実景と新UIを使用します。夜の照明・空・提灯・道路・車両演出を保ち、昼用の追加環境光を夜へ重ねません。
- 練習は共通メーターと「加速・操舵・実アイテム取得」の3課題を使い、完了後は相手待ちを表示します。

## 2026-10-21: UI の等倍合成・解像度比較（35 / 40）

`PlayerDisplayRig` は `NativeResolutionUI` を追加し、BackImage → Main → Front の3Dスタックを出力サイズの中間RTへ描く。既存 UICamera は独立Baseにし、その描画中だけ共有URPのrenderScaleを1へ変更・直後に復元する。中間RTを背面のRawImageで表示し、その上に既存のScreen Space Camera Canvasを描く。P1はネイティブ出力、P2は従来の `RenderGraphDisplayOutput` のRT／別窓転送を維持する。HUDの内容・レイアウトと追従カメラは変更しない。UIへのポスト処理・AA・影・動的解像度は無効。既存Rendererの機能自体は保持するため、そのUIパスでの費用は計測が必要。

撮影APIは `PlayerDisplayRig.RenderToTarget`。3Dだけの `StandardRequest` にはUIが入らなくなったため、サムネイルと既存の `DrivingTutorialValidation` / `ViewportAndTireValidation` / `EngineAudioPlaythroughValidation` / `RocketJumpFixValidation` は2段合成へ更新。`MultiDisplayValidation` の構成前提も独立UIへ変更した。独自ツールでHUD込みの画像が必要なら、このAPIを使う（UIなしの `TitleBackgroundBuilder` は従来の3Dリクエストのまま）。

リードの固定撮影（グラフィックス有効、`-nographics` / `-quit` なし）:

```powershell
$env:RACING_VISUAL_TOUR_MODE = 'fixed'
$env:RACING_VISUAL_VARIANT = 'full' # 保存された夜の既定環境
$env:RACING_VISUAL_RESOLUTIONS = '1280x720,1920x1080,2560x1440'
$env:RACING_VISUAL_RENDER_SCALE = '0.55' # 別出力フォルダで 1.0 と比較
$env:RACING_VISUAL_TOUR_OUTPUT = '<absolute-output-directory>'
# Unity 6000.3.9f1 -batchmode -projectPath <project> -executeMethod CourseVisualTour.RunBatch -logFile <log>
# 終了後は上記 RACING_VISUAL_* 環境変数を解除する。
```

`RACING_VISUAL_RESOLUTIONS` は最大3個、各640〜3840 × 360〜2160。未指定は従来の1600×900。複数指定はfixed専用。`RACING_VISUAL_RENDER_SCALE` は0.5〜1、未指定は現在値。各地点の停止／移動・練習・結果で、物理やHUDの更新を挟まず各解像度を描く。カメラ追従は通常どおり整定させ、撮影時に変更しない。時系列ポスト処理の履歴まで同一になる保証はない。

出力は原寸PNG、`manifest.json`（各画像の寸法・3D scale）、従来の `contact-sheet.png` に加え、`resolution-sheet.png` と `resolution-index.html`。列は指定解像度順、行はmanifestのフレーム順（番号を画像にも記載）。各セルは384×216の全景と、上段左／中央／右・下段左／中央／右の128×128原寸切り出し。欠け・細線は切り出しと原寸リンクを100%表示で確認する。縮小した全景だけで鮮明さを判定しない。桁増加はHUDを書き換えないため、リードが実走の99→100 km/h・分繰上がり・大きい周回数も別途確認する。練習／結果が未成立の場合は `skipped` に記録されるので、`complete` だけで全画面撮影済みと判断しない。

Unity未起動。ローカルの既存Unity参照DLLとcsproj定義によるRuntime／Editorの外部C#コンパイル成功のみ確認済み。リードは既存のDisplay Outputs・Viewport Resize・Driving Tutorial・Modern UI検証、P1/P2の異なるサイズ／窓リサイズ／練習→レース→結果／サムネイル後の復帰、0.55と1.0の文字・色・縦反転・レターボックスを確認する。費用は [StandalonePerformance.md](StandalonePerformance.md) を参照。

実装根拠: 同梱URP 17.3の `UniversalRenderPipeline.RenderCameraStack` はBaseの `beginCameraRendering` 後にカメラデータを生成し、OverlayはBaseの縮小descriptorを継承する。`UniversalRendererRenderGraph.ImportBackBuffers` は独立Base出力の色保持を保証しないため、3Dを背面画像として明示合成した。[Unityのカメラ描画前イベント](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.RenderPipelineManager-beginCameraRendering.html)。URP更新時はこの順序を再確認する。
合成用の "NativeUIWorld" シェーダーは3D出力のRGBを不透明に描き、空のAlphaが0でも背景を欠落させない。実機の2画面出力とGPUフレーム時間は別途確認が必要です。

無人生成はグラフィックス有効で Unity.exe -batchmode -projectPath <project> -executeMethod TitleBackgroundBuilder.RunBatch -logFile <log> を実行する（-quit / -nographics は付けない）。完了時にツールが終了コード 0/1 で終了し、Assets/Resources/UI/Neon/TitleBackground.png を上書きする。
