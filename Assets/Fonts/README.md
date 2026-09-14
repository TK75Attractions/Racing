# Racing UI font roles

- **MPLUS**: Japanese UI copy and labels.
- **Video**: English UI copy, headings, and non-instrument numeric values.
- **DSEG7**: Numeric values on vehicle instruments only (speedometer, race gauges, etc.).

Runtime-generated UI should resolve these through `RacingUIFontCatalog` rather than inheriting an arbitrary scene font.
