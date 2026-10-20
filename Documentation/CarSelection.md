# 車種選択 / Car selection

Each player's **はじめる / START** button (or pedal hold on the title) enters that player's car selection screen. Filling the selection's confirmation gauge starts that player's tutorial immediately. P1 and P2 can start and finish selecting at different times: one can practice while the other is still at the title or choosing a car. The race countdown starts after both tutorials are complete.

Each player's screen transition and camera switch are independent during preparation. Selection never creates the other player's practice car or resets an already-running practice area. Holding the start pedal cannot accidentally confirm a car, and holding the confirmation pedal cannot accelerate the practice car until it has been released.

## Preparation time limits

Each player receives a separate **30-second car selection** countdown and **60-second tutorial** countdown on entering those screens. Both show a `MM:SS` timer and remaining-time bar, turning pink for the last ten seconds. The timers use real elapsed time and continue when a controller disconnects.

Selection timeout commits the currently displayed vehicle and paint through the normal confirmation path and opens that player's tutorial. Returning to the title discards the draft; reopening selection starts a fresh countdown. Tutorial timeout ends that player's practice, stops the car and shows `TIME UP` while waiting for the other player. Completing the lessons early stops the timer. The race countdown begins once both players have completed or timed out of practice, after the existing one-second ready delay. Players still at the title or choosing a car keep their own preparation flow.

Change **Preparation Time Limits > Car Selection Time Limit Seconds / Tutorial Time Limit Seconds** on `Gmanager` in the Inspector to configure the two durations.

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

選択した車種は車体性能に加えて、エンジン排気の共鳴プロファイルと小さな排気パフの色を切り替えます。3種類の差は控えめで、ギア・回転数・出力ゲインは共通です。パフはアクセル入力中だけ少量を発生させます。

White, silver, blue, pink and black are available. The committed vehicle and paint are independently saved for each player and used in both tutorial and race, including restarts. Browsing and cancelling do not change the saved selection. The sedan replaces chassis meshes while retaining the production physics rig, wheel positions, collider and FWD drivetrain. SPORT GT-R shares the coupe model with a different performance grade.

The preview uses mesh copies without driving scripts or colliders. Its local point lights are isolated by range, layer and distance so they do not affect the other player's practice area. The large preview renders at up to 30 frames per second while rotating; thumbnails are captured once per screen. The showroom is inactive while closed, and its private textures/materials are released on teardown. The sedan's legacy materials are copied to URP-compatible materials. Coupe preview wheels come from the complete wheel prefab.

Car body paint, dark plastic/interior, metal wheel parts, glass and rubber use the shared surface recipe. Run **Racing > Visuals > Build Car Material Tuning** after changing source car materials; showroom copies receive the same recipe without modifying their source assets.

## Configuration and validation

Edit `Assets/Resources/UI/CarSelectionCatalog.asset` for vehicle models, descriptions and performance multipliers. Shared asset materials remain intact.

**Racing > UI > Validate and Capture Car Selection** checks draft/commit/cancel, player independence, wraparound, body replacement, preservation of the physics rig, noncompounding tuning, automatic rotation, pedal gauge progress, short taps through analog release, steering-only paint selection and Back operation. It checks font coverage and untruncated text at 1920×1080, 1280×720 and 2560×1440, restoring saved choices afterward.

```sh
Unity -batchmode -projectPath /path/to/Racing -executeMethod CarSelectionValidation.Run -quit -logFile /tmp/car-selection.log
```

1920×1080 screenshots go to `Artifacts/CarSelection`; other sizes go to the temporary `RacingCarSelectionCaptures` directory. The command updates the dedicated static M PLUS Bold atlas when new text is added.

**Racing > UI > Validate Car Selection in Play Mode** tests P2-first start, a held start pedal, visible hold progress, independent tutorial entry, driving while P1 selects, confirmation pedal release, tutorial timeout stopping the car, and synchronized race spawning. It simulates one completed lesson and times out the other for the final race gate; it is an integration smoke test, not a full driving playthrough. Use `-executeMethod CarSelectionPlayModeValidation.Run` without `-quit` in batch mode. **Racing > Tutorial > Validate and Capture** additionally exercises the real driving lessons; its editor driver chooses the saved/default cars automatically to reach practice.

## 2026-10-21: 車の特徴と構図（44）

性能説明は設定済みの最高速・ハンドリング倍率の比較から「曲がりやすい / カーブが得意」「直線で伸びる / カーブは早めに減速」「バランス型 / 速さと曲がりやすさを両立」を表示する。数値評価と併記し、倍率そのものは変更しない。プレビューは注視点を車体の高さへ戻して画角を38度へ広げ、回転中も車が収まりやすくした（サムネイルは26度を維持）。接続待ちは係員への案内を表示する。新文言は既存の **Validate and Capture Car Selection** のフォント収集対象。リードは同コマンドでアトラスを再生成し、全車・全塗装・回転中・両プレイヤーで文字と車体の重なりを確認する。Unityでの実画面確認は未実施。
