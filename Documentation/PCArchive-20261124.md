# PC cleanup archive — 2026-11-24

The `codex/archive-pc-20261124/` branches preserve local work before removing the primary checkout. Existing branches and `main` were not overwritten.

## Restore the game

Install Git LFS and Unity 6000.3.9f1, then run:

```sh
git clone https://github.com/TK75Attractions/Racing.git
cd Racing
git lfs pull
git switch main
```

Open the project in Unity and allow package resolution and asset import. Scene: `Assets/Scenes/SampleScene.unity`. macOS release build entry point: `StandalonePerformanceValidation.BuildRelease`.

## Recover archived work

`Archive/PC-20261124/manifest.json` maps archived branches and stash commits. Check out a corresponding branch to inspect a saved state. To restore a stash, fetch its branch and run `git stash apply <commit-sha>` on a suitable working branch; the saved commits retain all original stash parents.

`RetrospectiveAndHandover.md` and recovered Unity scenes are saved on this archive branch. Recovered worktree changes have their own `snapshots/*` branches based on the original commits. Personal signing settings are retained locally only.

## Private local backup

The sibling directory `Racing-backup-20261124` retains private monitor settings, iOS signing settings, editor preferences, original recovery archives, untracked playtest evidence, a previously built app, and file checksums. Do not upload the whole directory. It is still on this PC; copy it to a private backup destination if it needs off-device protection.

This archive records source recovery. Fresh-clone and build verification results are recorded in the local backup after testing.
