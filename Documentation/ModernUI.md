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