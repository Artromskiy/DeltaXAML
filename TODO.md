# DeltaXAML TODO

## DXAML-RENDER - neutral UI paint handoff

This is the selected producer slice for the shared UI rendering path. The
contract revision is recorded in `v0.0.8`; implementation must not add a
renderer dependency to DeltaXAML or move shaping/GPU ownership into this
project.

- [x] Emit `UiVisualPaint` and `UiClipRegion` shape data from retained visual
  state where the corresponding XAML properties are present; preserve old
  fill-only behavior for controls that do not use effects. Current producer
  coverage is explicit fill paint plus rectangular clip data; rounded/stroke
  data remains pending until those properties exist in retained state.
- [x] Emit `UiTextPaint` for text fill/outline/effect-resource values without
  reshaping when only paint changes. Current producer coverage is fill paint;
  text outline/effect properties remain pending. Text shaping remains delegated to
  `DeltaText.Contract.ITextService`.
- [x] Preserve `UiDisplayList.Order`, clip parent links, resource identities
  and borrowed lifetime in producer tests, including `visual -> text -> visual`.
- [ ] Add explicit diagnostics for unsupported effect/resource combinations;
  never replace them with a solid rectangle or silently discard paint data.
- [ ] Keep the retained tree, property store, invalidation and frame storage
  as the only XAML runtime path. No Vulkan, SDL, DeltaRender, DeltaEngine or
  ECS reference is allowed.

Cross-project integration and consumer cleanup remain tracked in
[../HIGH_PRIORITY_TODO.md](../HIGH_PRIORITY_TODO.md). Shared editor acceptance
remains tracked in [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md). Add future
project work here only after it has been explicitly selected; keep research and
unselected alternatives in [IDEAS.md](IDEAS.md).
