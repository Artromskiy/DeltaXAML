# DeltaXAML user API

This file is the user-facing library API guide. It is not the
cross-project packet contract; that boundary is [PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md).
The selected library shape is specified by [LIBRARY_CONTRACT.md](LIBRARY_CONTRACT.md).

## Consumer boundary

Application code should compose a retained document through `IXamlLoader`,
`XamlLoadContext` and `UiDocument`:

```csharp
XamlLoadResult loaded = loader.Load(source, in context);
var document = new UiDocument(loaded.Root, textService);
document.Dispatch(input);
document.Layout(viewport, dpiScale);
UiDisplayList display = document.BuildDisplayList();
```

`UiElement` is the retained public node. `Parent`, `Children`, `BindingContext`,
`UiParticipation`, typed/untyped property access and path or compiled bindings
are the consumer surface. `UiPanel`, `UiStackPanel`, `UiBorder`, `UiGrid`,
`UiContentControl`, `UiButton`, `UiTextBlock`, `UiTextBox`,
`UiNumericEditor`, `UiScrollViewer` and `UiItemsControl` are thin facades over
the same retained tree. The public surface does not expose dirty masks,
retained arrays, frame-local clip indices, ECS storage or renderer objects.

`IUiBinding` and `IUiBinding<T>` provide read and optional write operations;
failures are returned as `Delta.Diagnostics.Diagnostic?`. `UiBindingExpression`
handles compact `{Binding Path=..., Mode=..., Converter=..., StringFormat=...}`
markup. `UiCompiledBinding<TSource,TValue>` is the no-reflection code path for
typed source access and optional two-way writes; it observes
`INotifyPropertyChanged` when available. `IUiValueConverter` and
`IUiBindingResolver` are explicit converter boundaries.

`UiResourceCatalog`, `UiStyle`, `UiTheme` and `UiTemplate` provide the small
resource/style/template layer. `{DynamicResource Key}` and
`{StaticResource Key}` are explicit loader forms; the current implementation
uses the same live catalog subscription for both. Style values use the
retained precedence `Default < Style < Binding < Local < Handle`; resource
updates invalidate only consuming properties.

`UiElement.GetHandle` returns an opaque generation-safe `UiPropertyHandle`;
`UiElement.TrySet` is the direct host-write path. The handle does not expose
retained storage or dirty flags and becomes invalid when its retained lifetime
changes.

`XamlTypeCatalog` is the explicit custom factory boundary. It does not perform
reflection discovery or infer types from arbitrary assemblies. Built-in `Grid`
attributes accept fixed pixels, `Auto` and star lengths (for example
`Columns="160,*,Auto"`).

`XamlLoader` passes unknown element names to `IXamlTypeResolver`. The current
adapter supports GUID-valued `ForegroundResource` references through
`IUiResourceResolver`; an invalid or missing resource is returned as a
`Diagnostic` and does not produce a partially resolved document.

`UiDocument.Dispatch` accepts the neutral `UiInputEvent` packets. Physical
keys, committed UTF-16 text and IME composition are separate payloads.
`UiDocument.Layout` is deterministic for a viewport and DPI. `BuildDisplayList`
returns the borrowed `UiDisplayList` described by `PUBLIC_CONTRACT.md`.
`TryBuildDisplayList` is the diagnostic form for adapter data that cannot be
represented by the canonical display list; `BuildDisplayList` throws an
`InvalidOperationException` carrying the same diagnostic code in that case.

## Current implementation status

The repository's retained implementation names are internal implementation
details and must not be used for new cross-project integration. Cross-project
integration uses `Delta.XAML.Contract`; the library-shaped API above is the
consumer entry point.

The public controls are intentionally compact and do not promise WPF,
Avalonia or MAUI parity. Text shaping and glyph generation remain owned by
DeltaText through `ITextService`; DeltaXAML only creates text requests and
adapts shaped output to the canonical display list. Clipboard access is
platform-neutral (`IUiClipboard`), while the host supplies any OS bridge.

## Boundary ownership

- DeltaXAML owns loading, retained elements, properties, bindings, layout and
  display-list production.
- DeltaEngine owns platform event acquisition and dispatch scheduling.
- DeltaText owns font instances and shaping.
- DeltaRender owns atlas/GPU/pipeline work and consumes `UiDisplayList`.

No Avalonia, SDL, Vulkan, DeltaEngine or ECS model is part of this library API.
Binding markup currently supports dotted public property paths, the three
binding modes, registered converters and string formatting; it does not yet
implement compiled XAML code generation, triggers, validation rules or full
markup-extension parity.
