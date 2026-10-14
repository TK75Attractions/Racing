# Standalone performance evidence

See [validation and methodology](../../Documentation/StandalonePerformance.md).

- `diagnosis.json`: original Render Graph configuration, selective feature/geometry/simulation ablations.
- `before-1080.json` / `after-1080.json`: original and final grid configurations. Focus differs; do not treat frame-time ratio as a controlled comparison. Median triangles decrease from about 8.7M to 1.5M.
- `driving-1080.json`: same-run original geometry versus tree LOD, followed by actual 60-second driving.
- `driving-retina.json`: final code, 2880×1800 native output plus simulated 1920×1080 P2; 60-second physical driving, both humans exceed 1.2km; errors empty; median 11.76ms, p95 14.74ms.
- `retina-race.png` / `retina-driving.png`: game/HUD/geometry and letterbox checks.
- `validation.txt`: static rendering/LOD/display validation result.

One physical monitor was available. Development benchmark simulates the second output; a two-monitor presentation/synchronization test remains outstanding. Release build succeeded and initialized; final release UI inspection was prevented by the Mac locking.
