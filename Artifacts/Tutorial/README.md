# Tutorial visual and driving QA

Captured from Unity 6000.3.9f1 in the real SampleScene play mode at 1672 × 940.

- `05-accelerating.png`: live production car accelerating, accelerator at 65%, first lesson selected.
- `02-steer.png`: steering lesson selected.
- `03-stop.png`: stop lesson selected, prompting pedal release.
- `04-wait.png`: first player completed and waiting for the second.
- `01-accelerate-p1.png`, `01-accelerate-p2.png`: separate player views at the start of practice.

The automated drive completed accelerate → steer → stop independently for both players and reached the race countdown. 782 road centerline raycasts confirmed one flat drivable collider and no asphalt contact seams; the straight-to-curve tangent was also checked. Maximum vertical velocity on the straight after initial settling was 0.007 m/s.

Offscreen snapshots use the actual camera stack and temporarily freeze each screen-space canvas into the same world-space plane, preserving its camera, dimensions and layout. Normal gameplay keeps the original screen-space canvas mode.

Run `Racing > Tutorial > Validate and Capture` to repeat the checks, or `Racing > Tutorial > Play Preview` to inspect the practice flow with keyboard controls. P1: W/A/D; P2: arrow keys.
