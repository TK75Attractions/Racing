# Car stability Play Mode measurements

Measured on 2026-10-07 with Unity 6000.3.9f1. The current `codex/fix-course-crest-collisions` scene, FBX assets, car prefab and project settings were copied to a temporary Unity project because the original project was open in another Editor instance. The working scene and existing uncommitted settings were preserved.

- `before.json`: final validation harness with the original stability, ground check and tire force scripts.
- `after.json`: same scene, assets and harness with the corrected scripts.

The branch measurements use its existing gravity of -15 m/s². main retains -9.81 m/s². All trials use real Play Mode physics and the spawned player car. The twelve ramp trials use the actual new course colliders. Controlled landing trials use a temporary flat BoxCollider and disable `LapManager` to prevent off-course respawns from contaminating the measurement. No test scene is saved.

`maxTilt`, `maxAirTilt`, `finalTilt`, `landingTilt` and `yawChange` are degrees. `maxAngularSpeed` is rad/s; speeds are m/s. `groundedWheels` is the final count. The trace CSV can be regenerated with `CarStabilityValidation.RunFullBatch`; the harness also checks steering, drift and visual effects. See [CarStability.md](../../Documentation/CarStability.md) for commands and criteria.
