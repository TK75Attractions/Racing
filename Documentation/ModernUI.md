# Modern racing UI

The title, race HUD, countdown, finish warning, spectator overlay, ESC menu and results share `RacingUITheme`.
The palette uses navy surfaces, cyan selection and a gold pedal-progress/confirmation state.

- `RacingPanelGraphic` draws rounded panels and slanted primary buttons with gradients and screen-pixel edge feathering. It does not enlarge button textures.
- `RacingHUDBuilder` replaces the legacy HUD bitmaps at runtime. Position, lap count, both clocks and speed remain bound to the existing race state. The lap denominator comes from `LapManager.GoalLap`.
- `RacingSpeedGauge` and `RacingIconGraphic` draw scalable geometry. Each custom graphic requires its own `CanvasRenderer`.
- All text uses the font catalog. A shared SDF material adds weight to the existing M PLUS Thin atlas for readable Japanese labels without modifying the font asset.
- The title uses live TSUKUKOMA CIRCUIT lettering. `ScreenTransitionController.useArtworkLogo` can restore the original artwork. The artwork import also disables block compression and mipmaps.

## Check in Unity

1. Run **Racing > UI > Validate Modern UI**. This checks fonts and backgrounds, renderer dependencies and both players' HUD bindings, including fractional times and configurable total laps.
2. Enter Play Mode to inspect the title and pedal feedback.
3. Use **Racing > UI > Preview HUD** or **Racing > Preview Result UI** for the other screens. HUD preview is only a visual preview; exit Play Mode to reset it.
4. Check at 1920 × 1080 and 3840 × 2160. In the Game view resolution menu, turn off **Low Resolution Aspect Ratios** when using Free Aspect on a Retina display. A magnified low-resolution Game view cannot show the final edge quality.

The legacy scene objects are retained and hidden during initialization; no scene migration is required.

## Spectator and ESC screens

- Spectator status sits between the minimap and timer. Thin corner marks and a compact finish-status panel preserve the camera view. All spectator graphics ignore raycasts.
- The ESC menu uses `RacingMenuButton`, a standard Unity Button with the same vector surface as pedal buttons. Pointer hover and keyboard selection light cyan; pressing lights gold. Transparent Image hit targets stay enabled, while decorative surfaces ignore raycasts.
- Restart, direction change and interruption keep the existing callbacks. Forward/reverse is shown beside the direction action. ESC still closes the menu through the game manager.
- **Racing > UI > Preview Spectator** and **Preview ESC Menu** are visual previews in Play Mode. Exit Play Mode to reset preview state. **Validate Modern UI** also checks both spectator labels, nonblocking overlays, hit targets, action callbacks and repeated menu initialization.
