# Samples

Runnable DeltaXAML samples currently kept in this repository:

- [`Xaml.Render`](Xaml.Render/README.md) — the shared renderer host for static
  `.dxaml` documents.
- [`Snake`](Snake/README.md) — a generated-XAML game sample using the same host
  in headless and SDL3/Vulkan modes.
- [`RoundedRectangle.Render`](RoundedRectangle.Render/README.md) — a
  `DeltaRender.UI` host sample for a four-corner rounded rectangle.
- [`UI-Library-Demo`](UI-Library-Demo/README.md) — a generated `.dxaml`
  editor-style UI library demonstration using the same host.

The remaining samples are intentionally independent of `DeltaEngine` and ECS.
renderer-backed samples consume the reusable `DeltaRender.UI` package; their
XAML and application content remain local to the sample.
