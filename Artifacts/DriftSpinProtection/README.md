# Drift spin protection (2026-10-08)

Unity 6000.3.9f1, SampleScene, the actual `car2` race prefab and manual inputs through the existing physics harness. The before run uses commit `08438eb` driving code; the after run includes drift spin protection.

The 12 stress trials use initial speeds 8 / 20 / 40 m/s, steering -30 / +30, and clean/disturbed starts. The disturbance adds sideways velocity equal to 25% of forward speed (14.04° initial slip) and 1.8 rad/s of yaw. Each run holds full steering for 1.5 s, countersteers for 0.14 s, then centers for 1.2 s. The table aggregates the worst measurement across left/right and clean/disturbed starts.

| Initial speed (m/s) | Before peak slip | After peak slip | Before minimum speed (m/s) | After minimum speed (m/s) |
| --- | --- | --- | --- | --- |
| 8 | 90.14° | 14.04° | 3.35 | 8.00 |
| 20 | 84.29° | 14.04° | 3.35 | 20.00 |
| 40 | 176.89° | 14.04° | 0.69 | 34.97 |

Slip is the angle between the car's forward direction and its horizontal velocity, rather than total cornering angle. All 12 before trials exceed the acceptance criteria. All 12 after trials keep peak slip below 20°, final slip below 5°, yaw below 2 rad/s, minimum speed above 80% of initial forward speed, and final forward speed above 90% of initial speed. Mild slip up to a target of 12° is permitted by the controller; the peak disturbed measurement includes its deliberately injected initial slide. Recovery redirects excess horizontal slip without reducing its speed magnitude or changing vertical velocity.

The full after run passes 30 Play Mode cases: these 12 stress cases, the existing 17 driving/stability cases, and an additional airborne yaw test with active drift state. Projection/UI, camera, steering, drift and visual effect regression checks also pass. Steering protection checks cover normal driving, both turn directions, reverse, airborne and the disable toggle.

The before run has 29 cases because the extra airborne drift test was added for the after run. The shared 12 stress cases are identical. These are automated physics measurements; manual driving feel is unverified.

Reproduce the after run:

```sh
RACING_STABILITY_OUTPUT=/tmp/racing-antispin Unity -batchmode -nographics -projectPath <project> -executeMethod DrivingFeelValidation.RunFullBatch -logFile /tmp/racing-antispin.log
```

Do not pass `-quit`; the harness exits when completed. `before.json` and `after.json` contain the recorded per-trial values.
