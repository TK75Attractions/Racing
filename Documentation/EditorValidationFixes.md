# Editor validation fixes (2026-10-21)

Compared against `b2df7d9` on `raymee/night-ennichi-visuals`. No font assets were regenerated.

| Check | Attribution and change |
| --- | --- |
| MainIntegrationValidation | New UI copy exceeded the existing single-page HUD bake. The original UI string scanner already includes the new copy; the 375-character current corpus is covered by all three source TTFs (baseline corpus: 342). Enable TMP multi-page baking, retaining static population afterward. Include Gmanager strings for its direct HUD usage. `build_hud_font_sources.py` creates full font masters, not subsets, and needs no character-list update. |
| BoostVolumeValidation | Pre-existing: baseline calls CreateStack before ensuring the render pipeline has initialized VolumeManager. Reuse the initialization camera already used by baseline LightningValidation. Apply the same guard to DriftChargeValidation. Explicitly invoke VManager disposal in edit mode, as LightningValidation already does. |
| SoundEffectsValidation | New BGM fade caused the failure in nested PlayerAudioValidation; race/countdown clip paths and cue assignments did not change. Keep the fade and advance its real implementation with a deterministic timestep in validation, including a check that the old clip remains before fade-out. Existing final-lap, per-player, spectator, retry and single-lap assertions remain. |
| EngineAudioValidation | Pre-existing Editor lifecycle issue: TubeDriveSurface lacks ExecuteAlways in baseline, but Ensure gates registration through isActiveAndEnabled. Enable its existing register/unregister callbacks in edit mode. Collision mesh, collider configuration, tube reference and TunnelAcoustics are unchanged from baseline. Rendering chunks only disable MeshRenderers; interpolation changes render settings, not acoustic collision. |
| RaceBarrierPathValidation / CourseBarrierValidation | New solid frame adds a second MeshFilter. The old one-filter assertion mistakes that frame for a stale wall. Require exactly one wall mesh and one frame mesh; retain duplicate-root destruction, one-collider and disable-cleanup checks. CourseBarrierValidation invokes this same check first. |
| LightningValidation / premature exit | Supplied log actually contains both barrier failures, Lightning PASS and TMPVAL DONE failed=6. None of the seven requested synchronous validation entry points or their directly invoked validation dependencies calls EditorApplication.Exit. Separate play-mode drivers have explicit exit calls; these are not on this execution path. Static inspection found no additional issue in the camera-scoped visibility protection. |

`isActiveAndEnabled` requires OnEnable to have run: [Unity 6000.3 Behaviour documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Behaviour-isActiveAndEnabled.html).

## Font audit and regeneration

The existing static HUD assets lack newly used characters. All source font cmaps cover the current HUD corpus. CarSelectionFont has a separate scanner: include PreparationTimerUI because that screen uses the same font, and allow multiple baked pages there too. The general M PLUS Japanese SDF covers the current corpus except U+2212 (minus), which is already used by baseline HUD/tutorial copy and covered by their own fonts. Instrument fonts retain their specialized numeric roles.

Run from Unity 6000.3.9f1, with the project path supplied as usual:

```text
-batchmode -projectPath <Racing> -executeMethod RacingHUDFontBuilder.Build -quit -logFile <hud-font.log>
-batchmode -projectPath <Racing> -executeMethod CarSelectionValidation.BuildFont -quit -logFile <selection-font.log>
```

No Python font-master regeneration is required. Check all resulting atlas pages into the same existing font assets. Then rerun MainIntegrationValidation.Run, BoostVolumeValidation.Run, SoundEffectsValidation.Run, EngineAudioValidation.Run, RaceBarrierPathValidation.Run, CourseBarrierValidation.Run and LightningValidation.Run. The synchronous checks can be caught and reported individually by the lead's existing batch harness without intermediate exits.

## Validation performed

- Static comparison with b2df7d9, source-TTF cmap coverage, serialized SDF character coverage, installed TMP multi-atlas implementation and synchronous-validation exit scan.
- `git diff --check` passed.
- Attempted Unity batch validation; the process produced no log and never reached the validation runner. Stopped only that process and removed the temporary runner. No Unity compile/test pass is claimed; runtime checks and atlas regeneration remain for the lead.
