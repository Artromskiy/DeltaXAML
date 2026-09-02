# DeltaXAML TODO

## Cross-project integration

Cross-project ownership and integration remain tracked in
[../CONTRACTS.md](../CONTRACTS.md). Shared editor acceptance remains tracked in
[../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md). Add future project work here only
after it has been explicitly selected; keep research and unselected alternatives
in [IDEAS.md](IDEAS.md).

## Deferred rendering follow-up

- [x] Implement the DeltaXAML producer half of the logical-DIP boundary:
  `UiDocument.Layout(viewport, dpiScale)` validates one finite positive scale,
  keeps viewport, layout and clip geometry in logical coordinates, applies the
  scale once to text metrics, and exports the scale through
  `UiDisplayList.DpiScale`. Repeating the same scale reuses the retained
  layout/text state; `DpiInvalidatesLayoutWithoutCompoundingScale` covers the
  invalidation and shaped-text cache behavior.
- [ ] Complete the consumer-side logical-DIP to Vulkan device-pixel conversion
  and readback policy. This is owned by DeltaRender; DeltaXAML must only
  provide the logical display list and its validated `DpiScale`.
