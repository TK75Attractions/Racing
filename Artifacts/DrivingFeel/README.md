# Driving feel validation (2026-10-08)

Unity 6000.3.9f1, SampleScene and the race car prefab. `DrivingFeelValidation.RunFullBatch` passed projection/UI, steering, drift, visual effects and camera checks, followed by all 17 Play Mode physics trials.

- Camera: alternating ±0.12 m height changes at 60 Hz produced a maximum aim height deviation of 0.0029 m; horizontal follow, level horizon and respawn snapping passed.
- Body motion: the prefab exposes one visual-only branch; no colliders or tire force components are moved.
- Both left/right drifts entered and released a boost. Maximum lateral speed was 0.884 / 0.888 m/s, maximum angular speed 1.125 / 1.116 rad/s and maximum tilt 0°. Final speed was 44.037 / 44.031 m/s and signed heading change was -87.247° / +86.518°.
- Remaining physics trials cover airborne input, six jump/landing combinations, inverted input and recovery, forward/reverse steering, and airborne yaw preservation.

`physics.json` contains the measured results. These are automated physics measurements; driving feel has not been evaluated through a manual playthrough.

Reproduce with:

```sh
RACING_STABILITY_OUTPUT=/tmp/racing-driving-physics Unity -batchmode -nographics -projectPath <project> -executeMethod DrivingFeelValidation.RunFullBatch -logFile /tmp/racing-driving.log
```

Do not pass `-quit`: the asynchronous Play Mode harness exits when complete. The practice lesson is advanced using the same race setup methods as the existing multiplayer smoke test. The remote publisher is disabled during the physics harness.
