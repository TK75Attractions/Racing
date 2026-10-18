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

## iPad operator layout — 2026-10-18

The race controls are now a compact toolbar above the two player feeds. Only actions applicable to the current game state appear; viewer controls remain disabled. Per-player preparation details sit with the feeds, so progression no longer stretches the camera panel. The top statistics include a linked device overview with six status chips and a fault count. A healthy overview requires current responses, all six checks, READY, calibration and a valid angle reference.

Device health and ESP32 commands share two columns at widths of 840px and above. The serial monitor uses Japanese status badges, direction arrows, rotating steering-wheel graphics with a fixed neutral marker and separate accelerator/brake meters. Unconfirmed or old input is explicitly labeled. Original frames are available in a modal with pause/resume, Escape/backdrop dismissal and keyboard focus handling. Reception and the newest-500 buffer continue while the dialog is closed; raw rows are only rendered when opened. Firebase's existing remote feed does not carry raw frames, which the empty dialog explains.

Validation:

- `node --test Tests~/Dashboard/dashboard-ui.test.cjs Tests~/DashboardRelay/live-client.test.cjs`: **31 passed**.
- `Tests~/Dashboard/serial-preview.cjs` in Chrome: **passed**, including 1024×768, 1180×820, 1366×1024 landscape layouts, 1440px desktop, 390px mobile, paired device panels, compact controls, signed steering-wheel rotation, no horizontal overflow, modal background-control isolation and focus restoration, dismissal, pause/resume, raw CRLF preservation and safe display of device text. No JavaScript exceptions.
- Visual review: [iPad 1024](overview-1024.png), [iPad 1180](overview-1180.png), [iPad 1366](overview-1366.png), [serial monitor](desktop.png), [raw dialog](raw-dialog-ipad.png), [mobile monitor](mobile.png), [mobile dialog](raw-dialog-mobile.png).

All previews use sample data. Physical iPad/Safari and ESP32 hardware were not tested; these dashboard changes have not been deployed.
