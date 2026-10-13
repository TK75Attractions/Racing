# 車種選択 / Car selection

`codex/car-selection` adds a full-screen garage sheet to each player's title screen. Open **車の選択**, choose a vehicle and paint, then press **この車で決定**. **戻る** or Escape discards the draft. Confirming returns to the title; the usual two-player readiness flow starts the tutorial and race.

The center is a live 3D mesh preview in a procedural neon garage. Drag to orbit the vehicle. The right panel shows speed, acceleration, handling and drift charge ratings, plus the actual front-wheel-drive layout and paint samples. The bottom cards use rendered 3D thumbnails.

## Vehicles

The catalog uses the project's existing assets. These are game vehicle names, not licensed representations of the real-world cars in the reference image.

| Vehicle | Model | Speed | Acceleration | Handling | Drift charge |
| --- | --- | --- | --- | --- | --- |
| SPORT GT | SportCar_1 coupe | 8.0 | 8.0 | 8.0 | 8.0 |
| STREET RS | RMCar26 sedan | 7.2 | 8.8 | 9.2 | 7.2 |
| SPORT GT-R | SportCar_1 coupe, speed grade | 9.2 | 7.6 | 7.2 | 9.2 |

Ratings are ten-point game balance values. The catalog's multipliers change the production `DebugMover` settings: acceleration changes drive force; speed changes the drive-force/resistance ratio; handling changes steering angle and lateral grip; drift changes charge rate. The baseline grade retains the prefab's settings. These multipliers are applied relative to the original settings and never compound.

White, silver, blue, pink and black paint are available. Each player has an independent committed vehicle and color, saved in `PlayerPrefs`. Browsing and cancelling do not update the saved choice. The selected body, paint and handling are used in both the driving tutorial and the race, including race restarts.

The sedan replaces visible chassis meshes and keeps the production physics, tires and input rig. It uses the existing wheel positions and collider; it does not simulate a different wheelbase, mass or drivetrain. SPORT GT-R is a different performance grade of the coupe model.

## Controls

| Action | Player 1 | Player 2 |
| --- | --- | --- |
| Select vehicle | A / D or steering wheel | Left / Right or steering wheel |
| Change paint | C or paint sample | M or paint sample |
| Confirm | Enter, hold pedal for 0.65 s, or button | Right Shift, hold pedal for 0.65 s, or button |
| Rotate preview | Drag or Q / E | Drag |
| Reset preview | R | Reopen sheet |
| Cancel | Escape or Back | Escape or Back |

Steering selection requires returning to neutral between changes. Pedal confirmation is armed only after releasing the pedal; the title start gate also requires release after leaving the sheet. A ready player retains readiness while the other player chooses a vehicle.

## Configuration and validation

Edit `Assets/Resources/UI/CarSelectionCatalog.asset` in the Inspector to change model references, names, descriptions and performance multipliers. `PlayerCarPaint` uses private material instances; shared asset materials remain intact.

Run **Racing > UI > Validate and Capture Car Selection**. It checks draft/commit/cancel behavior, player independence, wraparound, body replacement, preservation of the physics rig, noncompounding tuning and preview isolation, then checks font coverage and text layout at 1920×1080, 1280×720 and 2560×1440. It restores the player's saved choices after validation. The command also builds the dedicated static Japanese font from the existing M PLUS Bold source when new text is added.

Batch invocation:

```sh
Unity -batchmode -projectPath /path/to/Racing -executeMethod CarSelectionValidation.Run -quit -logFile /tmp/car-selection.log
```

1920×1080 screenshots are written to `Artifacts/CarSelection`; other resolution captures go to the temporary `RacingCarSelectionCaptures` directory. The large preview renders on opening and when the vehicle, paint or viewing angle changes; thumbnails are captured once per screen. The world is inactive while the sheet is closed. It contains no driving scripts or colliders, and its private textures/materials are released on teardown.

**Racing > UI > Validate Car Selection in Play Mode** additionally opens the production scene and checks the two-player title gate, the selected tutorial cars and the selected race cars. It skips lesson completion through the existing private transition callbacks; it is a spawn/integration smoke test, not a driving playthrough. It preserves saved selections and leaves scene edits unsaved. Batch invocation uses `-executeMethod CarSelectionPlayModeValidation.Run` **without `-quit`**; the validation exits on completion.

The sedan's legacy materials are copied to URP-compatible materials at runtime. The coupe preview uses the project's complete wheel prefab rather than the incomplete embedded wheel references in the standalone body asset.
