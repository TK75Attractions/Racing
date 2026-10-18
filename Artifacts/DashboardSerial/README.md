# Japanese serial monitor and error history — 2026-10-15

The serial monitor now displays the original wire data together with Japanese explanations for RGP/1 telemetry, state/calibration/recovery events, system/configuration messages, ACK outcomes, device errors, and diagnostic text. Invalid or unapplied telemetry is explicitly identified. Diagnostics are inserted as text, never interpreted as HTML.

The bridge retains the newest eight alerts independently of the high-rate TEL log, including device warnings/errors, malformed frames, command timeouts, and serial transport errors. Alerts remain visible as history after recovery. The remote view prioritizes alerts within its existing ten-line, 120-character, 4 KiB schema; no Firebase rules changes are required. Truncated remote ERR data is labeled as incomplete.

Validation:

- `node --test Tests~/Dashboard/dashboard-ui.test.cjs`: **11 passed**, including all 50 supplied host acceptance vectors, original frame preservation, separate pedal values, error history, truncated messages, safe diagnostic text, and remote/keyboard behavior.
- `NpcDashboardValidation.Run` in Unity 6000.3.9f1: **passed**, including alert retention after 200 normal TEL records, newest-eight retention, local 80-line limit, remote 4 KiB limit, and the existing NPC lifecycle checks.
- Unity EditMode: **37 passed, 0 failed** (`../Rgp/unity-editmode-results.xml`).
- `./Tools/Validate-Rgp.sh`: **37 passed**, including the command-timeout notification regression and protocol vectors.
- Actual Chrome with mocked local snapshots: **passed** for desktop and 390px mobile layouts, no horizontal overflow and no JavaScript exceptions. Screenshots use sample data: [desktop](desktop.png), [mobile](mobile.png).

The source changes have not been published to GitHub Pages or deployed as a Unity build. Apply the Dashboard files through the existing main-branch publishing workflow and update the Unity runtime to include persistent alert forwarding. Physical ESP32/USB testing was not performed.


## Operator controls update — 2026-10-18

- Operators can confirm car selections, advance remaining players to the tutorial, and skip the tutorial once both players have entered it.
- Six health cards show USB, both discovered IMUs, ADS1115, and P1/P2 pedals. Pedals report valid current input, stale/invalid input, sensor faults, or a paused stream. Zero is valid input; physical wire continuity is not inferred.
- The command panel shows send/accept/complete stages and structured results. Calibration and I2C scans wait for a matching completion event; failures and unknown results have explicit states.
- Resolved alerts remain for ten seconds, then disappear without being recreated from the retained log. Cumulative parser counts no longer create permanent warnings.

Validation: 55 C# tests, 25 dashboard/live-client tests, and an actual Chrome fixture covering both progression buttons, six health cards, inline status results, ten-second recovery, 1440px/390px layouts, and no browser exceptions. The Unity C# project compiled with zero errors. Hardware was represented by sample data; no command was sent to a physical ESP32.

Preview images: [hardware desktop](hardware-desktop.png), [hardware mobile](hardware-mobile.png), [command desktop](command-desktop.png), [command mobile](command-mobile.png), [flow mobile](flow-mobile.png).
