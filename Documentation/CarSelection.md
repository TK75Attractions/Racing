# 車種選択 / Car selection

Each player's **はじめる / START** button (or pedal hold on the title) enters that player's car selection screen. Filling the selection's confirmation gauge starts that player's tutorial immediately. P1 and P2 can start and finish selecting at different times: one can practice while the other is still at the title or choosing a car. The race countdown starts after both tutorials are complete.

Each player's screen transition and camera switch are independent during preparation. Selection never creates the other player's practice car or resets an already-running practice area. Holding the start pedal cannot accidentally confirm a car, and holding the confirmation pedal cannot accelerate the practice car until it has been released.

## Pedal and steering controls

The screen needs only the steering wheel and pedal. The vehicle automatically rotates at 18 degrees per second; keyboard/mouse orbit controls are unnecessary.

| Item | Steering wheel | Pedal |
| --- | --- | --- |
| Vehicle (initial focus) | Left/right chooses the previous/next vehicle | Short tap moves focus to paint; hold confirms the car |
| Paint | Left/right chooses the previous/next color | Short tap moves focus to actions; hold confirms the car |
| Actions | Left selects Back; right selects Confirm | Hold executes the selected action; short tap returns focus to vehicle |

Return the wheel to neutral between changes. A short tap means press and release before the 0.65-second confirmation gauge fills. An analog release through intermediate pedal values still counts as a tap. A full hold executes once. Changing selection while pressing or losing the controller clears the hold and requires a new pedal release.

Both action buttons show a segmented hold gauge and percentage. **この車で決定** saves the draft and starts the tutorial. **戻る** discards the draft and returns only that player to the title. The other player's tutorial keeps running. Mouse holds are retained for editor convenience; the on-screen instructions describe pedal/steering operation. Keyboard debug mode simulates them with P1's W/A/D and P2's Up/Left/Right keys.

## Vehicles and gameplay

The catalog uses existing project assets and fictional game vehicle names.

| Vehicle | Model | Speed | Acceleration | Handling | Drift charge |
| --- | --- | --- | --- | --- | --- |
| SPORT GT | SportCar_1 coupe | 8.0 | 8.0 | 8.0 | 8.0 |
| STREET RS | RMCar26 sedan | 7.2 | 8.8 | 9.2 | 7.2 |
| SPORT GT-R | SportCar_1 coupe, speed grade | 9.2 | 7.6 | 7.2 | 9.2 |

Ratings are ten-point game balance values. Acceleration changes drive force; speed changes the drive-force/resistance ratio; handling changes steering angle and lateral grip; drift changes charge rate. Multipliers apply relative to the original prefab settings and do not compound.

White, silver, blue, pink and black are available. The committed vehicle and paint are independently saved for each player and used in both tutorial and race, including restarts. Browsing and cancelling do not change the saved selection. The sedan replaces chassis meshes while retaining the production physics rig, wheel positions, collider and FWD drivetrain. SPORT GT-R shares the coupe model with a different performance grade.

The preview uses mesh copies without driving scripts or colliders. Its local point lights are isolated by range, layer and distance so they do not affect the other player's practice area. The large preview renders at up to 30 frames per second while rotating; thumbnails are captured once per screen. The showroom is inactive while closed, and its private textures/materials are released on teardown. The sedan's legacy materials are copied to URP-compatible materials. Coupe preview wheels come from the complete wheel prefab.

## Configuration and validation

Edit `Assets/Resources/UI/CarSelectionCatalog.asset` for vehicle models, descriptions and performance multipliers. Shared asset materials remain intact.

**Racing > UI > Validate and Capture Car Selection** checks draft/commit/cancel, player independence, wraparound, body replacement, preservation of the physics rig, noncompounding tuning, automatic rotation, pedal gauge progress, short taps through analog release, steering-only paint selection and Back operation. It checks font coverage and untruncated text at 1920×1080, 1280×720 and 2560×1440, restoring saved choices afterward.

```sh
Unity -batchmode -projectPath /path/to/Racing -executeMethod CarSelectionValidation.Run -quit -logFile /tmp/car-selection.log
```

1920×1080 screenshots go to `Artifacts/CarSelection`; other sizes go to the temporary `RacingCarSelectionCaptures` directory. The command updates the dedicated static M PLUS Bold atlas when new text is added.

**Racing > UI > Validate Car Selection in Play Mode** tests P2-first start, a held start pedal, visible hold progress, independent tutorial entry, driving while P1 selects, confirmation pedal release, and synchronized race spawning. It simulates lesson completion for the final race gate; it is an integration smoke test, not a full driving playthrough. Use `-executeMethod CarSelectionPlayModeValidation.Run` without `-quit` in batch mode. **Racing > Tutorial > Validate and Capture** additionally exercises the real driving lessons; its editor driver chooses the saved/default cars automatically to reach practice.
