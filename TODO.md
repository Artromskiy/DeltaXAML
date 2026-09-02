# DeltaXAML TODO

## Cross-project integration

Cross-project ownership, integration and shared acceptance remain tracked in
[../CONTRACTS.md](../CONTRACTS.md). Add future project work here only
after it has been explicitly selected; keep research and unselected alternatives
in [IDEAS.md](IDEAS.md).

## P0 — compiler and loader consistency

- [ ] Make the generated semantic artifact the single execution path for both
  compiled XAML and the interpreted `IXamlLoader`; do not introduce a second
  retained tree, property store or binding engine.
- [ ] Align `IXamlLoader` with the registered built-in element set, or emit a
  stable unsupported diagnostic for every built-in that is compiler-only:
  `Slider`, `Image`, `Overlay`, `CollectionView`, `Picker`, `TabView`, `Menu`,
  `RichTextBlock` and `ResourceDictionary`.
- [ ] Fix the `BackgroundBrush` cold-loader mapping: it is accepted by
  `SupportsProperty` but currently has no `Apply` implementation.
- [ ] Use the same color grammar in compiler and loader, including
  `#AARRGGBB`, or reject the form consistently with the same diagnostic.
- [ ] Align binding syntax between compiler and loader for `Source`,
  `RelativeSource`, `ElementName`, `TemplateBinding` and `MultiBinding`.
- [ ] Process declaration blocks (`Resource`, `Style`, `Template`, `Trigger`
  and `Behavior`) through the semantic model, or explicitly diagnose them as
  unavailable in interpreted mode. Do not silently create a partial tree.
- [ ] Align attached properties and collection syntax for `Grid.Row`,
  `Grid.Column`, spans, `ItemsSource`, `ItemTemplate`,
  `ItemTemplateSelector` and virtualization.
- [ ] Align `RichTextBlock`/`Span`, commands, gestures and `IsFocusScope`
  between generated and interpreted loading paths.

## P1 — user API and generated parity

- [ ] Extend custom XAML property metadata to cover the value forms already
  available to built-in properties: brushes, corner radii, resource IDs,
  resource references and binding/collection expressions where applicable.
- [ ] Add end-to-end tests that execute generated artifacts and compare the
  resulting retained tree, effective values, layout and display list with the
  supported interpreted path. Generator source snapshots alone are not enough.
- [ ] Add parity tests for resource/style updates, bindings, collections,
  templates, rich text and unsupported syntax diagnostics.

## P1 — documentation and boundary classification

- [ ] Correct `docs/USER_API.md` so it distinguishes generated production
  support from the current basic `IXamlLoader` subset.
- [ ] Document which capabilities are code-only (`BindingContext`, handles,
  custom visuals, editing state and generated plans) and which are host-owned
  (`IUiClipboard`, image/assets, navigation, URI, dialogs, drag-and-drop and
  fonts).
- [ ] Keep unsupported MAUI/WPF syntax on stable diagnostics with a documented
  DeltaXAML alternative; do not claim standard-XAML parity where it is absent.

## Cross-project handoff

- [ ] Complete the consumer-side logical-DIP to Vulkan device-pixel conversion
  and readback policy in DeltaRender. DeltaXAML provides only the logical
  display list and validated `UiDisplayList.DpiScale`.
