# Broadcast / result highlight validation

Implementation: da5d985, b05e63a, 94b3131. Unity 6000.3.9f1, macOS, Apple M1.

## Recorded footage

- `broadcast-live.mp4`: about 8 seconds of the final LIVE camera implementation, including the fix that hides CPU identity labels only during broadcast rendering.
- `broadcast-replay.mp4`: about 10 seconds of actual recorded race motion replayed at 0.85 speed. The same feed is shown in the third window and the results panels.
- `result-p1.jpg`, `result-p2.jpg`: actual result layouts from a completed two-lap race.
- `native-pv.png`, `native-replay.png`: actual Metal drawable captures from the built standalone application, fresh startup and startup with two saved races respectively.

The input-driven test bot reads car/course state and supplies steering and pedal input. It does not place cars, set laps, or fabricate overtakes. Non-filmed parts use 4x game time; filmed LIVE and result playback use normal game time. Movies are silent, encoded from captured frames with their recorded time intervals. They are visual evidence, not rendering performance benchmarks.

## Checks

- `full-playthrough-checks.json`: full real two-lap race; four cars, eight selected clips (four overtakes and four corners), binary archive round-trip, invalid archive rejection, mesh-only replay actors, and unchanged physical vehicle state during rendering. The second full run loaded history, hence `pvSeen: false`; fresh PV startup is independently verified below. All four cars had zero recoveries in the full-run CSV.
- `live-camera-checks.json`: separate final LIVE camera sample; verifies CPU labels are hidden during broadcast rendering and restored afterward. Archive fields are unused in this scope (`liveOnly: true`).
- `native-fresh-report.json`, `native-history-report.json`: built app starts in COURSE PV / REPLAY respectively; 3360x2100 broadcast output on the built-in display, full screen bounds, no collected rendering errors.
- `native-tests.txt`: twelve display-selection configurations plus production AppKit window tests. Broadcast does not become the keyboard input window, can be moved by its background, and its manually moved frame survives layout refresh. The final borderless window fills the complete screen rather than the smaller visible work area.
- `core-test-results.xml`: 140 core tests pass.
- `dashboard-ui.txt`: 37 UI tests pass.
- `dashboard-tests.txt`, `dashboard-integration.json`: 27 relay tests pass; one environment-dependent PTY test skipped. The integration test uses temporary ports 19765/19766, leaving the running dashboard service alone.

Native universal arm64/x86_64 build succeeded (`MULTI_DISPLAY_BUILD_PASS`). The standalone smoke test used the built application and an owned ESP32 simulator process; those processes were stopped afterward. These checks had one physical display. Three physical displays connected simultaneously were not available; automatic selection for two external displays is covered by the native matrix, while the movable third window was tested on the actual built-in display.

Replays store vehicle mesh poses, appearance, and visibility in the current course; transient weather/item effects are not a complete historical world recording. User controls and reproducible commands are in `Documentation/RaceBroadcast.md`.
