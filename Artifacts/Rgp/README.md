# RGP/1 validation — 2026-10-15

Implemented against the supplied RGP/1 1.0 release candidate, with the two-player six-value TEL revision.

- Unity 6000.3.9f1: project compilation succeeded; EditMode tests **37 passed, 0 failed** (`unity-editmode-results.xml`).
- Offline Mono runner: the same core and EditMode test sources, **37 passed, 0 failed** (`test-results.xml`). Reproduce with `./Tools/Validate-Rgp.sh`.
- Unmodified supplied vectors: **144 applicable records passed** — 19 CRC, 50 ESP_TO_HOST valid, 57 ESP_TO_HOST invalid, 18 shared malformed framing records. Device-side command argument/execution vectors require ESP32 firmware and are not claimed as host validation.
- Regression coverage includes session setup without boot events, ACK correlation and exact retransmissions, HELLO reset, delayed old-session ACK, incompatible major version, independent pedals and atomic TEL application, duplicate/old/wrapped SEQ, receive-time staleness and rate changes, non-READY states/errors/disconnection, resynchronization and partial-frame expiry, and legacy rejection.

Physical ESP32/USB testing and sensor calibration were not performed. The device must run the matching RGP/1 revision. Existing unrelated Unity warnings about obsolete APIs and headless rendering were present; no compilation errors or failing EditMode tests occurred.
