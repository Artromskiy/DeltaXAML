# DeltaXAML TODO

Public facade sequencing follows [API_REVIEW.md](API_REVIEW.md); the
cross-project handoff order is in
[../HIGH_PRIORITY_TODO.md](../HIGH_PRIORITY_TODO.md).

## XAML capability expansion

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
