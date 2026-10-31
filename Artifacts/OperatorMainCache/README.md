# Main-project import cache reuse

2026-11-01, macOS / Unity 6000.3.9f1.

The updater now seeds its first private Library from the working project's Library when the source project's imported Editor version matches the fetched main project's version. A dedicated previous-build Library takes precedence. APFS cloning (with timestamp preservation) is attempted first; ordinary copying is the fallback. The working Library is never moved, linked or written by the updater.

SHA-256 hashes of ArtifactDB, SourceAssetDB, Editor version/instance metadata and package definitions must stay identical across the copy. Both copied import databases are also compared byte for byte. If the working Editor updates them while copying, the private copy is discarded and Unity performs an ordinary import. Linked Library trees are not accepted. Private copies have inherited ILPP PID, DB locks, Editor-instance metadata, search indices, temporary artifacts and live ShaderCache SQLite files removed before Unity opens them.

## Actual Unity validation

A temporary project under /private/tmp was populated with independent APFS copies of Assets, Packages and ProjectSettings. The production seed function copied the working Library in 20.179 seconds, including database hashing/comparison. The working Editor remained open.

A separate Unity batch Editor loaded `Assets/Scenes/1016こうしゃ.fbx` and verified that it contains 550 MeshFilters. The file is approximately 102 MB. There were **zero** `Start importing Assets/Scenes/1016こうしゃ.fbx` lines and the validator exited successfully. The Unity log's creation-to-last-write interval was approximately 33.2 seconds, including script import/compilation and shutdown. This measures an import-cache check, **not** total player-build time or a guaranteed speedup across all assets/platform changes. Earlier production logs recorded a 347.64-second import of this model.

The QA clone was deleted after Unity exited. No production Race was stopped or launched; the already-running operator job retained its original script.

## Regression verification

- `python3 Tests~/OperatorUpdate/test_update.py`: 29 passed. New coverage includes first-job main seeding, independent writes in a dirty feature checkout, warm-cache precedence, Unity-version mismatch, malformed Editor metadata, symlinks, a database changing during copying, and cancelling a partially copied cache.
- `node --test Tests~/Dashboard/dashboard-ui.test.cjs`: 74 passed. The progress UI distinguishes main-project seeding from cold and dedicated warm-cache builds.
- Shell syntax and Git whitespace checks passed. The source-copy validation was repeated after enabling timestamp preservation. Windows's existing dedicated-cache path was not changed.

Initial unprivileged integration attempts could not inspect/stop the test-owned fake process tree in the sandbox. The complete suite passed with the required process permissions; real game discovery remains replaced by the test fixture.
