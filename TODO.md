# DeltaXAML TODO

## XAML capability expansion

- [ ] Add resource-backed style precedence and explicit style invalidation for
  local, style, binding, and transient handle values.
- [x] Add generation-safe `UiPropertyHandle` writes and a batched mutation
  surface for game/editor systems without an ECS dependency.
- [x] Add compiled-binding descriptors with typed read/write, validation,
  change notification, one-way/two-way modes, and retained invalidation.
- [x] Add a registered custom-type/factory catalog for XAML elements and
  diagnostics, without reflection-based discovery or MAUI coupling.
- [ ] Replace the current O(n) frame mutation lookup with a neutral retained
  index only after profiling shows that mutation volume justifies its upkeep.
- [ ] Expand the dialect toward practical XAML capability coverage in staged
  slices: resources/styles/templates, property systems, bindings, custom
  controls, input, and markup extensions. Track unsupported standard features
  explicitly; MAUI/WPF compatibility is not an acceptance criterion.

- Connect real positioned DeltaText glyph runs to `TextBlock`, editors and
  diagnostics without giving DeltaXAML font rasterization ownership.
- Keep the neutral `IUiPropertySource` schema/value boundary stable for the
  external DeltaEditorShell inspector, including retained row identity across
  value-only updates.
- Finish resize, focus, pointer/keyboard editing and scroll acceptance in the
  end-to-end editor shell.

Shared ownership and gates are in [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md).
