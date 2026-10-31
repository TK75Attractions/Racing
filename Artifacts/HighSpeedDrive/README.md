# High-speed drive / no-tutorial mode verification

2026-11-01, macOS, Unity 6000.3.9f1. Editor checks ran in an isolated APFS clone; the working Editor and production Race were not stopped. No standalone player build was produced for this change.

## Real PhysX drivetrain measurement

`HighSpeedDriveValidation.Run` uses the production `car2.prefab`, actual tire contact rays, a flat road, full positive pedal input and 0.02 s Physics.Simulate steps for 20 seconds. The road material has zero collider friction, matching the car's custom tire/resistance model. No position/velocity injection supplies propulsion. This measures straight-road motor and resistance behavior; it does not claim a complete-course driving assessment.

| Measurement | Normal | High-speed ON |
| --- | ---: | ---: |
| Speed after 2 s | 126.18 km/h | 229.66 km/h |
| Speed after 20 s | 180.00 km/h | 300.00 km/h |
| Force per driven tire | 15 | 30 |
| Velocity resistance | 0.60 | 0.72 |

After switching OFF at 300 km/h: 298.56 km/h after one step and 199.62 km/h after three seconds. The input gate blocks propulsion in either configuration. The gauge reads 300 with a 360 range while enabled and restores its 240 range on disable. These assertions passed in Unity; raw measurements are in [physics.json](physics.json).

## Regression checks

- `Tools/Validate-PlayStatistics.sh`: production .NET build with zero warnings/errors; 11 statistics scenarios; 211 core tests; 86 dashboard/diagnostics/push tests passed, one existing optional browser test skipped.
- `Tools/OperatorUpdate/test.sh`: host/runtime checks, 23 updater scenarios, five bridge API cases (including independent NoTutorial/HighSpeedDrive selection), 71 dashboard cases passed before the two new UI cases were added. Those new cases passed in the final 86-test suite above.
- `QuickRacePlayModeValidation.Run`: Quick mode finishes one lap, Standard starts practice, and NoTutorial skips practice while retaining two laps. The final run passed sticky exclusion of both unfinished rankings after a speed toggle.

The cloned Editor logged an existing Unity Search index startup exception in SearchDatabase. The play-mode validator continued and exited with code 0 after its PASS marker. The graphics-disabled play-mode attempt was stopped after repeated unsupported render-target warnings; the successful final run used the normal graphics backend. The PhysX measurement did not require graphics.

No-tutorial course-intro behavior and independent option persistence are covered by the core rules tests. High-speed timing boards use an additional versioned hash input; normal-speed hash inputs remain identical to the previous version.

The administration helper was not redeployed because a separate operator build was actively importing assets. That running job targets origin/main (077cea0), which does not contain these branch changes.
