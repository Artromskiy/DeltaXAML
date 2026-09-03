# Samples

Runnable DeltaXAML samples currently kept in this repository:

- [`Snake`](Snake/README.md) — a generated-XAML game sample with headless and
  SDL3/Vulkan paths.
- [`RoundedRectangle.Render`](RoundedRectangle.Render/README.md) — a native
  DeltaRender sample for a four-corner rounded rectangle.
- [`RoundedRectangle.Headless`](RoundedRectangle.Headless/README.md) — a
  headless retained/display-list sample with 100 rounded rectangles and
  bounded invalidation measurements.

The remaining samples are intentionally independent of `DeltaEngine` and ECS.
The headless sample validates DeltaXAML layout and display-list behavior;
renderer-backed samples own their DeltaRender, SDL3/Vulkan and shader
dependencies outside the DeltaXAML library.
