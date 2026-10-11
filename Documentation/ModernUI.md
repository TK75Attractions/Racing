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
