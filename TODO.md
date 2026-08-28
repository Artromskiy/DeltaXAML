# DeltaXAML TODO

Public facade sequencing follows [API_REVIEW.md](API_REVIEW.md); the
cross-project handoff order is in
[../HIGH_PRIORITY_TODO.md](../HIGH_PRIORITY_TODO.md).

The concrete implementation order, internal types, stage ownership and
compatibility-removal criteria for every unchecked item below are authoritative
in [INTERNAL.md#remaining-implementation-specification](INTERNAL.md#remaining-implementation-specification).

## XAML capability expansion

### P0 — typed descriptor runtime

- [x] `DXAML-MIXIN-1`: finish the executable architecture gate from
  [WORKFLOW.md](WORKFLOW.md). It must validate state-only structs, flat control
  classes, stateless readonly mixins, generated descriptor locations and the
  absence of object/type-keyed frame storage. Classify current retained files
  as migrated, obsolete compatibility or forbidden new work. The gate is
  active in the headless test entry point and reports designated-folder
  violations with exact source locations.
- [x] `DXAML-MIXIN-2`: introduce the minimal static generic capability set for
  measure, arrange, input and visual extraction. Move one representative leaf
  control (`UiTextBlock`) to composite state plus stateless mixins without
  creating a second retained identity/property store. The TextBlock exemplar
  is complete; the remaining controls are covered by `DXAML-MIXIN-4`.
- [x] `DXAML-MIXIN-3`: add immutable `UiTypeDescriptor` operations and generated
  typed thunks. Resolve control/state/mixin/property mappings at compile time;
  do not use `Dictionary<string, Action<...>>`, `Type` lookup, reflection or
  per-instance behavior delegates in frame stages. The current TextBlock
  descriptor is registered through a compact index and dispatches typed
  state-by-ref operations; all built-in controls are now registered in the
  compact descriptor catalog.
- [x] `DXAML-MIXIN-4`: migrate the remaining built-in controls in coherent
  groups: content/visual leaves, containers/layout, input/editing, then
  items/scrolling. Remove the old operation path after each group. Any old API,
  implementation, test or benchmark that must remain temporarily is marked
  `[Obsolete]` with its replacement and this removal milestone. The
  `TextBlock`, `StackPanel`, `Border`/`ContentControl`, `Panel`/`Grid`,
  `Button`/`ToggleButton`, `TextBox`/`NumericEditor` and
  `ItemsControl`/`ScrollViewer` operation paths now dispatch through typed
  state/mixin descriptors. The single retained owner hierarchy remains as a
  compatibility shell until the later stage split.
- [x] `DXAML-MIXIN-5`: make the canonical typed property path write effective
  values directly into concrete composite state. `UiPropertyStore` now has one
  explicit source-precedence table, no per-instance apply delegates, and routes
  changed effective values through typed setters in `UiElementState` and the
  control state descriptors. Untyped access remains limited to loader/editor
  cold paths; layout/input/visual extraction continues to use typed state.

### P0 — compiled XAML artifacts

- [x] `DXAML-COMPILE-1`: define one typed semantic model shared by production
  source generation and optional designer/hot-reload inflation. `DeltaXAML.Compiler`
  now parses a deterministic XML/XAML subset into typed object/member/value plans,
  preserves exact UTF-16 `SourceRange` offsets, resolves explicit stable GUID
  type/property/resource identities and recovers after local errors. Generation
  and designer integration remain in `DXAML-COMPILE-2`.
- [x] `DXAML-COMPILE-2`: `DeltaXAML.Generator` now supplies an incremental
  XAML input and deterministic companion emitter. Built-in plans generate
  direct `Ui*` construction, typed literal setters, child/content attachment
  and one compact generated namescope table; registered stable type/property
  identities are resolved by the compiler registry and missing factory
  metadata is a build diagnostic. No user control becomes `partial` and no
  reflection fallback is emitted. Resource emission remains in the dedicated
  later slice; typed binding emission is covered by `DXAML-COMPILE-3`.
- [x] `DXAML-COMPILE-3`: registered binding definitions now generate direct
  typed read/write lambdas for `OneTime`, `OneWay` and `TwoWay`, plus one
  explicit refresh/dispose batch on the companion. `OneTime` does not subscribe
  to source notifications. An undeclared XAML binding is a build diagnostic,
  never a string-path or reflection fallback.
- [x] `DXAML-COMPILE-4`: compile resource identities/slots, target-type style
  selectors, visual-state setters and template trees into deterministic plans.
  The companion emits direct resource/style/state/template registration; runtime
  state refresh uses the existing retained property store. Unsupported markup
  remains a diagnostic and never selects a reflection fallback.

### P1 — retained stages and game use

- [ ] Split retained execution into explicit mutation, binding, style, measure,
  arrange, input/focus and visual stages. Keep `UiDocument` as the small public
  owner; do not create a service-locator facade or allow stages to invoke one
  another recursively.
- [ ] Store logical and visual relations under one generation-safe `UiNodeId`.
  Template expansion may create a visual subtree but not a second logical
  document or per-frame translated tree.
- [ ] Add precise property invalidation and dirty-subtree processing. Layout
  must not invoke bindings, perform reflection, allocate child collections or
  reshape unchanged text.
- [ ] Complete the screen-space game HUD flow: host input -> `UiDocument` ->
  borrowed `UiDisplayList` -> DeltaRender graph target. Keep off-screen/world-
  space UI a consumer-side target choice, not a second DeltaXAML runtime.
- [ ] Complete custom visual registration through stable `UiVisualTypeId` and
  keep Vulkan resources, pipelines and shader artifacts outside DeltaXAML.

- [x] Implement the additive facade selected in
  [LIBRARY_CONTRACT.md](LIBRARY_CONTRACT.md): `IXamlLoader`, concrete
  `UiDocument`, the common typed/untyped property and binding surfaces,
  GUID-backed resource/type resolution, and validated `UiParticipation`.
  Reuse the current retained tree and stores; do not create a second UI engine.
- [ ] Complete migration of the remaining retained adapter path and direct
  consumers to the authoritative `Delta.XAML.Contract` display-list boundary;
  the former compatibility project and inspector source boundary are removed.

- [x] Add resource-backed style precedence and explicit style invalidation for
  local, style, binding, and transient handle values.
- [x] Add generation-safe `UiPropertyHandle` writes and a batched mutation
  surface for game/editor systems without an ECS dependency.
- [x] Add compiled-binding descriptors with typed read/write, validation,
  change notification, one-way/two-way modes, and retained invalidation.
- [x] Add a registered custom-type/factory catalog for XAML elements and
  diagnostics, without reflection-based discovery or MAUI coupling.
- [x] Add the first user-facing text path: `TextBlock`/`TextBox`/
  `NumericEditor` bindings and clipboard/edit commands through DeltaText and
  the canonical display-list adapter.
- [x] Add compact binding expressions, inherited `BindingContext`, explicit
  converters, typed compiled bindings and two-way source updates.
- [x] Add named resources, dynamic resource references, style precedence,
  templates and collection-row reuse on the single retained tree.
- [ ] Replace the current O(n) frame mutation lookup with a neutral retained
  index only after profiling shows that mutation volume justifies its upkeep.
- [ ] Expand the dialect toward practical XAML capability coverage in staged
  slices: resources/styles/templates, property systems, bindings, custom
  controls, input, and markup extensions. Track unsupported standard features
  explicitly; MAUI/WPF compatibility is not an acceptance criterion.

- [x] Connect real positioned DeltaText glyph runs to `TextBlock`, editors and
  diagnostics without giving DeltaXAML font rasterization ownership.
- Finish resize, focus, pointer/keyboard editing and scroll acceptance in the
  end-to-end editor shell.

Shared ownership and gates are in [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md).
