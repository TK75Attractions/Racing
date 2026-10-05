# Racing UI font roles

- **MPLUS**: Japanese UI copy and labels.
- **Video**: English UI copy, headings, and non-instrument numeric values.
- **DSEG7**: Numeric values on vehicle instruments only (speedometer, race gauges, etc.).

Runtime-generated UI should resolve these through `RacingUIFontCatalog` rather than inheriting an arbitrary scene font.

The driving tutorial uses a static M PLUS 1 ExtraBold (weight 800) master derived from the bundled variable font, stored at `Assets/Resources/UI/TutorialBold.ttf`. `DrivingTutorialTypography` builds its own SDF atlas and shadow materials, keeping the heavy reference typography separate from the other screens.
