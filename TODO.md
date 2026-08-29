# DeltaXAML TODO

## DXAML-RENDER - neutral UI paint handoff

This is the selected producer slice for the shared UI rendering path. The
contract revision is recorded in `v0.0.8`; implementation must not add a
renderer dependency to DeltaXAML or move shaping/GPU ownership into this
project.

- [x] Emit `UiVisualPaint` and `UiClipRegion` shape data from retained visual
  state where the corresponding XAML properties are present; preserve old
  fill-only behavior for controls that do not use effects. Producer coverage
  includes fill, uniform stroke and per-corner radius data plus the
  existing rectangular clip hierarchy. Corner radius does not implicitly clip
  child content; rounded clip masks remain a consumer concern.
- [x] Emit `UiTextPaint` for text fill/outline/effect-resource values without
  reshaping when only paint changes. Producer coverage includes fill, outline
  and optional effect resource identity. DeltaRender may still report outline
  or effect as unsupported until its text effect shader path is registered;
  text shaping remains delegated to `DeltaText.Contract.ITextService`.
- [x] Preserve `UiDisplayList.Order`, clip parent links, resource identities
  and borrowed lifetime in producer tests, including `visual -> text -> visual`.
- [x] Add explicit `XAML010` diagnostics for incompatible resource values in
  the interpreted loader and runtime dynamic-resource stage; a brush used as a
  color is rejected instead of being silently discarded. `UiDocument` reports
  the same diagnostic from `TryBuildDisplayList` until the resource is fixed.
- [x] Keep the retained tree, property store, invalidation and frame storage
  as the only XAML runtime path. `UiRuntime` owns the single generation-safe
  node store and `UiDocument` owns the reusable display storage; architecture,
  node-store and display-list tests prove the boundary. No Vulkan, SDL,
  DeltaRender, DeltaEngine or ECS reference is allowed.

Cross-project integration and consumer cleanup remain tracked in
[../HIGH_PRIORITY_TODO.md](../HIGH_PRIORITY_TODO.md). Shared editor acceptance
remains tracked in [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md). Add future
project work here only after it has been explicitly selected; keep research and
unselected alternatives in [IDEAS.md](IDEAS.md).
