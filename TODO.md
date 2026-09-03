# DeltaXAML TODO

## Cross-project integration

Cross-project ownership, integration and shared acceptance remain tracked in
[../CONTRACTS.md](../CONTRACTS.md). Add future project work here only
after it has been explicitly selected; keep research and unselected alternatives
in [IDEAS.md](IDEAS.md).

## P0 — compiler and loader consistency

- [x] Make the generated semantic artifact the single execution path for both
  compiled XAML and the interpreted `IXamlLoader`; `XamlCompiler` now emits
  one plan consumed by generated emission and the cold plan materializer, with
  no second retained tree, property store or binding engine.
- [x] Align `IXamlLoader` with the registered built-in element set for
  `Slider`, `Image`, `Overlay`, `CollectionView`, `Picker`, `TabView`, `Menu`
  and `RichTextBlock`; `ResourceDictionary` remains an explicit unsupported
  declaration until interpreted resource declarations are implemented.
- [x] Fix the `BackgroundBrush` cold-loader mapping: literal solid,
  linear/radial/image resource-backed brushes now use the descriptor path.
- [x] Use the same color grammar in compiler and loader, including
  `#AARRGGBB`.
- [x] Align binding syntax between compiler and loader for `Source`,
  `RelativeSource`, `ElementName`, `TemplateBinding` and `MultiBinding`: the
  generated path supports the typed forms, while the cold reader returns
  stable `XAML008`/`XAML020` diagnostics instead of building a partial tree.
- [x] Process declaration blocks (`Resource`, `Style`, `Template`, `Trigger`
  and `Behavior`) through the semantic model, or explicitly diagnose them as
  unavailable in interpreted mode. The interpreted reader now returns stable
  `XAML020` diagnostics instead of creating a partial tree.
- [x] Align attached properties and collection syntax for `ItemsSource`,
  `ItemTemplate`, `ItemTemplateSelector` and virtualization. `Grid.Row`,
  `Grid.Column` and spans are supported by the interpreted reader; collection
  properties have an explicit generated-only `XAML020` boundary rather than a
  partial fallback tree.
- [x] Align `RichTextBlock`/`Span`, commands, gestures and `IsFocusScope`
  between generated and interpreted loading paths.

## P1 — user API and generated parity

- [x] Extend custom XAML property metadata to cover the literal value forms
  already available to built-in properties: brushes, corner radii and resource
  IDs. Resource references and binding/collection expressions remain semantic
  plans rather than custom CLR property literals.
- [x] Add end-to-end tests that execute generated artifacts and compare the
  resulting retained tree, effective values, layout and display list with the
  supported interpreted path. `ToggleButton.dxaml` now exercises this boundary;
  broader resource/collection/template parity remains tracked below.
- [x] Add parity and boundary tests for resource/style updates, bindings,
  collections, templates, rich text and unsupported syntax diagnostics. The
  supported cold surface is compared with generated output; compiler-only
  collections/templates are covered by generated tests and stable cold-reader
  diagnostics rather than a fabricated interpreted tree.

## P1 — documentation and boundary classification

- [x] Correct `docs/USER_API.md` so it distinguishes generated production
  support from the current basic `IXamlLoader` subset.
- [x] Document which capabilities are code-only (`BindingContext`, handles,
  custom visuals, editing state and generated plans) and which are host-owned
  (`IUiClipboard`, image/assets, navigation, URI, dialogs, drag-and-drop and
  fonts).
- [x] Keep unsupported MAUI/WPF syntax on stable diagnostics with a documented
  DeltaXAML alternative; do not claim standard-XAML parity where it is absent.
  `docs/USER_API.md` now maps the cold-reader diagnostic to the generated or
  typed library alternative.

## Cross-project handoff

- [ ] Complete the consumer-side logical-DIP to Vulkan device-pixel conversion
  and readback policy in DeltaRender. DeltaXAML provides only the logical
  display list and validated `UiDisplayList.DpiScale`.
