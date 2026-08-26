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

`UiElement` is the retained public node. `Parent`, `Children`,
`UiParticipation`, typed/untyped property access and typed bindings are the
consumer surface. Stable `UiPropertyId`, `UiTypeId` and `UiResourceId` values
are GUID-backed semantic identities. The public surface does not expose dirty
masks, retained arrays, frame-local clip indices, ECS storage or renderer
objects.

`IUiBinding` and `IUiBinding<T>` provide read and optional write operations;
failures are returned as `Delta.Diagnostics.Diagnostic?`. Compiled binding
delegates and validation remain implementation details of the library.
`IXamlTypeResolver` is the explicit custom factory boundary. It does not
perform reflection discovery or infer types from arbitrary assemblies.

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

The repository is migrating from its temporary implementation surface to this
library shape. Existing compatibility names are implementation-only and must
not be used for new cross-project integration. The migration is intentionally
additive until direct consumers move to `Delta.XAML.Contract`.

The current public library contract does not include a control-specific
compatibility promise. Controls, templates, dirty propagation, focus,
routing, concrete resource stores and caches remain internal implementation
choices behind the facade.

## Boundary ownership

- DeltaXAML owns loading, retained elements, properties, bindings, layout and
  display-list production.
- DeltaEngine owns platform event acquisition and dispatch scheduling.
- DeltaText owns font instances and shaping.
- DeltaRender owns atlas/GPU/pipeline work and consumes `UiDisplayList`.

No Avalonia, SDL, Vulkan, DeltaEngine or ECS model is part of this library API.
