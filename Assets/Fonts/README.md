# Racing UI font roles

- **MPLUS**: Japanese UI copy and labels.
- **Video**: English UI copy, headings, and non-instrument numeric values.
- **DSEG7**: Numeric values on vehicle instruments only (speedometer, race gauges, etc.).

Runtime-generated UI should resolve these through `RacingUIFontCatalog` rather than inheriting an arbitrary scene font.

The bilingual race HUD uses `RacingUIFontCatalog.GetHUD()` for its own M PLUS Medium (500)
and Bold (700) masters, including Latin captions and instrument digits. The static masters
are derived from the existing M PLUS variable font; their license is in `MPLUS-HUD-OFL.txt`.
The atlases are baked by `RacingHUDFontBuilder` and require no runtime character generation.
Reproduction instructions are in `Documentation/PlayScreenDesign.md`.
