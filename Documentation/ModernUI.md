# Racing UI

現在の共通デザインは [Arcade Sport UI（デザイン2）](ArcadeSportUI.md) です。タイトルから終了、実況まで P1 は青、P2 はピンクを使用します。

`ArcadeUI` が配色・文字・ロゴ、`RacingHUDBuilder` が共通メーター、`ArcadeNoticeRail` が控えめな通知を担当します。`NeonUI` / `ModernUIValidation` は既存の呼び出しとの互換入口として残り、旧デザインの画面は構築しません。シーン上の旧 HUD は初期化時に隠すため、シーン移行は不要です。

`Racing > UI > Validate Arcade Sport UI` で結合を検証し、`Capture Arcade Sport UI` で両プレイヤーの描画を保存できます。試走を含む検証方法と画像の説明は上記ドキュメントを参照してください。

## Live viewport changes

`FreezeAspectRate` keeps the scene and UI in the same 16:9 viewport, including after `MainCamera` moves under the camera-shake pivot. Primary-window sizing uses the current window/Game view dimensions. Each player's canvas scales its 1920 × 1080 reference layout to the UI camera's actual pixel viewport, excluding letterbox bars, and refreshes before rendering.

**Racing > Validation > Viewport Resize and Tire Marks** changes both players' render targets during Play Mode through 1920 × 1080, 1280 × 720, 1680 × 720, 1024 × 768, 720 × 1280 and back to 1920 × 1080. It checks title, HUD and result layout bounds, captures examples in `Logs/ViewportValidation`, and checks the production car's tire-mark output through straight/sliding/straight motion while retaining drift charge. Batch execution uses `-batchmode -executeMethod ViewportAndTireValidation.Run` without `-quit`; the validation exits on completion.

## Spectator and ESC screens

観戦中も既存のカメラ追従を使用します。HUD は観戦先の速度・回転数へ切り替わり、配色は画面の持ち主に合わせます。ESC メニューは既存の再開・走行方向・設定・中断の処理を維持しています。
