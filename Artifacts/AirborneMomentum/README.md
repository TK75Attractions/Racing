# Airborne momentum (2026-10-08)

Unity 6000.3.9f1, SampleScene, actual `car2` race prefab. Before uses commit `c977b57`; after reduces airborne horizontal resistance to 5% of ground resistance and excludes the ground drift resistance multiplier during flight.

The seven flight cases use initial horizontal speeds 8 / 20 / 40 m/s, with/without accelerator input, plus an active drift state at 20 m/s. Each flight starts 80 m above the test floor with 5 m/s upward velocity and lasts 2 s. The same motion and input are used before and after. Accelerator/no-accelerator cases produce equal results because tire propulsion requires contact.

| Initial speed (m/s) | Before final horizontal speed (m/s) | After final horizontal speed (m/s) | Before horizontal distance (m) | After horizontal distance (m) |
| --- | --- | --- | --- | --- |
| 8 | 2.392 | 7.534 | 9.235 | 15.525 |
| 20 | 5.980 | 18.835 | 23.085 | 38.811 |
| 40 | 11.961 | 37.670 | 46.172 | 77.623 |

After flight retains approximately 94.2% of takeoff speed; before retains approximately 29.9%. The drift case improves from 5.854 to 18.835 m/s. All flight cases have zero tire propulsion, zero grounded wheels and final vertical velocity -14.620 m/s, confirming that gravity and falling motion are unchanged.

All 37 after physics trials pass, including the existing jump/landing, inverted recovery, forward/reverse steering, matched drifts, full-lock spin protection and airborne yaw cases. All seven before flight cases fail the new momentum-preservation criteria. Edit-time checks confirm unchanged ground resistance, reduced airborne resistance, exclusion of drift drag in flight, the zero-resistance setting and restoration of ground drift resistance upon contact. Camera, minimap and existing visual regressions pass too.

These are automated physics measurements; manual flight feel has not been evaluated. Full measured reports are in `before.json` and `after.json`.

Reproduce:

```sh
RACING_STABILITY_OUTPUT=/tmp/racing-airglide Unity -batchmode -nographics -projectPath <project> -executeMethod DrivingFeelValidation.RunFullBatch -logFile /tmp/racing-airglide.log
```

Do not pass `-quit`; the asynchronous physics harness exits on completion.
