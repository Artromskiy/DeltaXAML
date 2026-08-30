# Generated sample parity

Runnable sample projects:

- [`TipCalc`](TipCalc/README.md) — a generated-XAML, typed-binding adaptation
  that runs headlessly through `UiDocument` and the canonical display list.
- [`Game2048`](Game2048/README.md) — a generated-XAML 4×4 game sample with
  deterministic moves, themed rounded tiles and retained display-list checks.
- [`Game2048.Render`](Game2048.Render/README.md) — the same sample in a real
  SDL3/Vulkan window with DeltaRender text and rounded-rectangle passes.
- [`RoundedRectangle.Render`](RoundedRectangle.Render/README.md) — a minimal
  native DeltaRender sample for a four-corner rounded rectangle.
- [`RoundedRectangle.Headless`](RoundedRectangle.Headless/README.md) — a
  headless retained/display-list sample with 100 rounded rectangles and
  bounded invalidation measurements.

The executable sample acceptance lives in
`tests/DeltaXAML.Tests/Fixtures/Samples`. All twenty fixtures enter the same
`AdditionalFiles` compiler/generator path as application XAML and are created,
laid out and extracted by `GeneratedSampleParityTests`. The runner does not use
`XamlLoader` or construct substitute retained trees in host code.

The scenarios are adaptations of the MIT-licensed
[`dotnet/maui-samples`](https://github.com/dotnet/maui-samples/tree/e78b47511ac706bb179abc4a09f182355ae75178/10.0)
snapshot `e78b47511ac706bb179abc4a09f182355ae75178`:

| Delta fixture | Upstream scenario | DeltaXAML capability |
|---|---|---|
| `XamlFundamentals` | XAML/Fundamentals | retained composition |
| `DataBinding` | Fundamentals/DataBindingDemos | generated converter and two-way binding |
| `DataTemplates` | Fundamentals/DataTemplateDemos | typed templates and virtualized collection |
| `ControlTemplates` | Fundamentals/ControlTemplateDemos | template factory and `TemplateBinding` |
| `Triggers` | Fundamentals/TriggersDemos | generated data condition and semantic action |
| `Behaviors` | Fundamentals/BehaviorsDemos | descriptor-bound typed behavior state |
| `ControlGallery` | UserInterface/ControlGallery | shared value/input controls |
| `Theming` | UserInterface/ThemingDemo | resources and typed styles |
| `Brushes` | UserInterface/BrushesDemos | resource-backed gradient visual |
| `WorkingWithColors` | UserInterface/WorkingWithColors | typed colors |
| `WorkingWithFonts` | UserInterface/WorkingWithFonts | rich style runs |
| `Picker` | UserInterface/PickerDemo | typed picker composition |
| `TwoPaneView` | UserInterface/Views/TwoPaneView | grid attached layout slots |
| `PassingData` | Navigation/PassingData | semantic command boundary |
| `ModalNavigation` | Navigation/ModalNavigation | same-document overlay |
| `TabbedPage` | Navigation/TabbedPage | tab composition |
| `Popups` | Navigation/Pop-ups | focus-scope overlay |
| `Hyperlink` | UserInterface/HyperlinkDemo | inline hit range and command |
| `WorkingWithFiles` | UserInterface/WorkingWithFiles | host-owned file request command |
| `TipCalc` | Apps/TipCalc | typed converters, two-way inputs and slider |

These are DeltaXAML equivalents, not MAUI source compatibility claims.
Platform navigation, dialogs, URI activation, drag/drop and asset I/O remain
host services; the retained controls publish semantic requests and never own
platform objects.
