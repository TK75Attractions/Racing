# RGP/1 regression vectors

`Vectors/` is an unchanged copy of the supplied RGP `test-vectors/` directory, for the 1.0 release candidate with the six-value, two-player TEL revision (2026-10-15).

The host tests run all 19 CRC records, all 50 ESP_TO_HOST acceptance records, all 57 ESP_TO_HOST rejection records, and 18 shared malformed-framing records from HOST_TO_ESP. The remaining HOST_TO_ESP command execution/argument vectors require ESP32 firmware and are outside this Unity host implementation.

`Assets/Tests/EditMode/SerialInputProtocolTests.cs` uses the production decoder, parser, and host session. It also covers handshake, independent pedals, atomic input, retries, old/duplicate/wrapped sequences, receipt-time staleness, state changes, disconnects, and session/major-version changes. Existing gameplay core tests run alongside them.

Run in Unity's EditMode Test Runner, or use `./Tools/Validate-Rgp.sh` on macOS with Mono and Unity installed. The offline runner compiles the production core and the same EditMode test sources against Unity's NUnit library, without restoring packages. Results are written to `Artifacts/Rgp/test-results.xml`.
