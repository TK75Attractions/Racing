# Operator exit and build retention validation

2026-11-01, macOS / Unity 6000.3.9f1.

`OperatorExitPlayModeValidation.Run` ran in a separate APFS project clone. It exercised a real Quick race: P1 finishes and spectates, P2 finishes, P1 completes the thank-you screen and enters local maintenance while P2's result/thank-you presentation continues, then P2 enters maintenance. Both maintenance sheets and the broadcast sheet are visible before `UpdateExitReady`. Readiness requires a later frame and no unfinished transitions. Cancelling restores Title after both exits and preserves operator-owned manual maintenance. The real Race and working Unity Editor were not stopped. No replacement application was launched by QA.

The first validator attempt waited for **all** transitions before observing either thank-you screen; that missed P1's five-second screen while P2's longer goal celebration continued and timed out. The validator now observes those concurrent phases independently and passes. This was a validation-observation issue. A Unity Search startup-index exception and an occupied production dashboard port also appeared in the clone; validation continued. The final Unity process exited with code 0. Manual maintenance was tested without changing persistent preferences.

Regression checks:

- Storage tests: 10 assertions passed, including active/running/latest protection, symlink boundaries, partial outputs, registered worktrees, unregistered generated leftovers, and preserving logs/cache/unrelated files.
- Host and runtime integration passed. New cases cover one player still presenting results/thanks, cancelling a one-sided hold, both ready screens, strict graceful release, old-player Title-only compatibility and clearing an updater-owned persisted pause before release.
- External updater pipeline: 29 cases passed; production bridge request guards on a private port: six cases passed.
- Dashboard UI: 75 tests passed. .NET production build: zero warnings/errors. Shell syntax and Git whitespace checks passed.

Actual cleanup removed nine obsolete application outputs and one old generated source tree (previously approximately 3.5 GB). The latest two apps plus a separately running older app were retained. Git registration was already gone for the source tree, so the cleanup first proved it was unregistered. A remaining Finder .DS_Store caused a first deletion warning; bounded retries removed the residue. The final cleanup reported no warnings. Relative paths are recorded in cleanup.json. Reusable Library and diagnostic logs were retained.

The installed Dashboard helper was updated while no operator build was running. The per-player screen behavior still requires a Race built from this branch; the older production player uses the Title-only compatibility path. No main merge/push or full player rebuild was performed here.
