# Modern racing UI

The title, race HUD, countdown, finish warning, spectator overlay, ESC menu and results share `RacingUITheme`.
The palette uses navy surfaces, cyan selection and a gold pedal-progress/confirmation state.

- `RacingPanelGraphic` draws rounded panels and slanted primary buttons with gradients and screen-pixel edge feathering. It does not enlarge button textures.
- `RacingHUDBuilder` replaces the legacy HUD bitmaps at runtime. Position, lap count, both clocks and speed remain bound to the existing race state. The lap denominator comes from `LapManager.GoalLap`.
- The race HUD uses its own charcoal, mint and amber palette via `RacingHUDStyle`. Japanese headings are primary, with English captions and explicit number units. `RacingHUDPlateGraphic` draws chamfered surfaces and a circular speed dial. See [PlayScreenDesign.md](PlayScreenDesign.md) for the design and preview workflow.
- `RacingSpeedGauge` and `RacingIconGraphic` draw scalable geometry. Each custom graphic requires its own `CanvasRenderer`.
- Text uses the font catalog. The HUD uses static M PLUS Medium and Bold atlases; other screens use the existing catalog roles and Japanese SDF material.
- The title uses live TSUKUKOMA CIRCUIT lettering. `ScreenTransitionController.useArtworkLogo` can restore the original artwork. The artwork import also disables block compression and mipmaps.

## Check in Unity

1. Run **Racing > UI > Validate Modern UI**. This checks fonts and backgrounds, renderer dependencies and both players' HUD bindings, including fractional times and configurable total laps.
2. Enter Play Mode to inspect the title and pedal feedback.
3. Use **Racing > UI > Preview HUD** or **Racing > Preview Result UI** for the other screens. HUD preview is only a visual preview; exit Play Mode to reset it.
4. Check at 1920 × 1080 and 3840 × 2160. In the Game view resolution menu, turn off **Low Resolution Aspect Ratios** when using Free Aspect on a Retina display. A magnified low-resolution Game view cannot show the final edge quality.

The legacy scene objects are retained and hidden during initialization; no scene migration is required.

## Live viewport changes

`FreezeAspectRate` keeps the scene and UI in the same 16:9 viewport, including after `MainCamera` moves under the camera-shake pivot. Primary-window sizing uses the current window/Game view dimensions. Each player's canvas scales its 1920 × 1080 reference layout to the UI camera's actual pixel viewport, excluding letterbox bars, and refreshes before rendering.

**Racing > Validation > Viewport Resize and Tire Marks** changes both players' render targets during Play Mode through 1920 × 1080, 1280 × 720, 1680 × 720, 1024 × 768, 720 × 1280 and back to 1920 × 1080. It checks title, HUD and result layout bounds, captures examples in `Logs/ViewportValidation`, and checks the production car's tire-mark output through straight/sliding/straight motion while retaining drift charge. Batch execution uses `-batchmode -executeMethod ViewportAndTireValidation.Run` without `-quit`; the validation exits on completion.

## Spectator and ESC screens

- Spectator status sits between the minimap and timer. Thin corner marks and a compact finish-status panel preserve the camera view. All spectator graphics ignore raycasts.
- The ESC menu uses `RacingMenuButton`, a standard Unity Button with the same vector surface as pedal buttons. Pointer hover and keyboard selection light cyan; pressing lights gold. Transparent Image hit targets stay enabled, while decorative surfaces ignore raycasts.
- Restart, direction change and interruption keep the existing callbacks. Forward/reverse is shown beside the direction action. ESC still closes the menu through the game manager.
- **Racing > UI > Preview Spectator** and **Preview ESC Menu** are visual previews in Play Mode. Exit Play Mode to reset preview state. **Validate Modern UI** also checks both spectator labels, nonblocking overlays, hit targets, action callbacks and repeated menu initialization.

## HUD hierarchy and notices (2026-10-21, backlog 33/34/36/37/38/39/46 HUD)

- `RacingHUDStyle` owns reference text sizes and ratios (Japanese / English / numbers / units), primary-number-only italics and secondary opacity. Position/map/warnings remain prominent; clocks, gear and steering telemetry are subordinate. `RacingUIFontCatalog.GetHUDForText` falls back to the Japanese catalog when a new notice is absent from the static HUD atlas.
- Each `OnPlayUIManager` owns one `DriverNoticeQueue`: connection/recovery/pedal gate > wrong way > jump/sharp bend > guidance > success. Safety/guidance appear at the upper center; success appears below center, with geometric warning/check icons and category labels. Minimum duration is 0.8 s; higher safety priority may interrupt immediately. Active-condition notices linger for 0.8 s after the last active sample to absorb short dropouts. Identical event text has a 3 s cooldown; pending events expire after 4 s. Race exit clears the queue. Landing/tube/drift controllers publish mailboxes without independent display deadlines; the HUD drains them once. Their `Notice` properties now describe undrained messages (tube also exposes ongoing guidance).
- Sharp bends use successive `RaceCourse` forward samples every 12 m over the next 48 m in race direction (over 1.5 degrees/m); reverse driving compares car forward with the directed course tangent while moving above 2 m/s. These thresholds require driving confirmation.
- Minimap markers use outlined large self triangles, medium rival triangles and small CPU circles. Self remains foremost; off-map markers are inset at the viewport edge with a white base mark. AUTO, steering assist and steering braking have compact labels only while active. Acceleration/deceleration use upward/downward arrow icons alongside their labels.
- Queue-only checks passed outside Unity (legacy out-variable syntax lowered; Mathf.Max substituted): safety preemption, minimum duration, deferred success, duplicate suppression, stale expiry and clear. Unity was not launched for this change. Lead: run existing `SkillGameplayValidation`, `ModernUIValidation`, `MiniMapValidation` and feature validations; use `CourseVisualTour` fixed captures on both displays at 720p/1080p/Retina. Check overlapping markers/edges, Japanese fallback glyphs, warning plus success overlap, reconnect/recovery, reversed course direction, assistance labels and sharp-bend timing.
## 2026-10-21: タイトル・準備・練習・結果・観戦（41/42/43/45/48）

- タイトルは `Racing > UI > Build Title Background from P1 Start View (Play Mode)` で、夜の実レースの P1 カメラスタックを UI なしで1920×1080撮影し、`Assets/Resources/UI/Neon/TitleBackground.png` を差し替える。夜のレースを開始してスタート地点で一時停止し、ゲームカメラにスタートゲート・提灯・車が入ることを確認して実行する。既存のポスト処理を保持し、カメラ出力先と Canvas の状態は処理後に戻す。終了して再生し直すと新背景を確認できる。画像の撮影・差し替え自体はリード担当で未実施（現時点のPNGは旧画像）。実行時の背景描画カメラ・ライト追加なし。
- 練習の操作計は下左右、現在の説明カードは下中央へ縮小。右カーブ入口（練習道路の80mサンプル）を各プレイヤーの実カメラへ投影し、操舵段階のみ右矢印と「ハンドルを右へ」を表示する。画面外・背後の矢印は隠す。準備はペダル、完了待ちはチェック、接続待ちは警告アイコンで、誰が操作できるかを短く表示する。
- 結果は本人ラベル・順位・総タイム／DNF・実際の達成を右側に集約。メダルとタイトル装飾を縮小し、下部の再挑戦・メニュー操作から間隔を取る。記録なしでは完走扱いにしない。
- `SpectatorOverlayUI` は観戦対象に加え、`Gmanager.SecondPlaceTimeRemaining` の切り上げ秒数、操作不要・終了後に結果へ自動遷移することを表示する。猶予が未確定なら残り秒数を作らず「終了を確認中」とする。
- リード確認: まず **Racing > UI > Build HUD Fonts** と **Validate and Capture Car Selection** で新しい日本語の静的アトラスを更新する。その後 **Validate Modern UI**、**RacingPolishValidation**、**DrivingTutorialValidation**、**CarSelectionPlayModeValidation**、**ViewportAndTireValidation** を実行する。既存の練習文言検証は新しい右カーブ案内と完了待ち文へ更新済み。`CourseVisualTour` の固定撮影と両画面720p/1080p/Retinaで、矢印位置、前方視界、文字欠け、長いDNF進捗・達成文、片側接続断、相手待ちから結果への遷移を確認する。Unity実行・撮影・性能計測は未実施。通常のdotnet buildはWindows SDKのアクセス制限で停止したが、既存csprojの参照DLLと定義を用いたRoslyn直接コンパイルでRuntime／Editor（新ビルダーを含む）はエラー0を確認した。

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
合成用の "NativeUIWorld" シェーダーは3D出力のRGBを不透明に描き、空のAlphaが0でも背景を欠落させない。新規シェーダーのUnityインポート／実機コンパイルは未確認。
