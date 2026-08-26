# DeltaXAML

Platform-neutral retained UI and a Delta-owned XAML dialect shared by game and
editor. It has no SDL, Vulkan, Avalonia, DeltaEngine, DeltaRender, Roslyn or ECS
storage dependency.

```text
XAML -> retained tree -> properties/styles -> measure/arrange
  -> hit testing/input -> shaped text + renderer-neutral UiDisplayList
```

`Delta.XAML.Contract` owns the canonical neutral input/display-list boundary.
`DeltaXAML.Abstractions` is the temporary legacy surface used while the current
implementation and consumers migrate to that smaller contract.
`DeltaXAML.Core` owns parsing, retained identity, invalidation, controls,
bindings, layout, clipping, focus and routed input. Font shaping/rasterization
and GPU atlases remain behind external contracts.

## Public layering decisions

`UiElementId` and `Generation` remain on the base `IUiElement` contract. The
concrete `UiElement` stores both values once; `UiPropertyHandle` only carries a
snapshot used to validate a generation-safe write and does not introduce a
second identity store. `HandleGeneration` is not a separate public property.

The retained dirty accumulator is internal to `UiElement` and is not exposed
through `IUiElement` or the public concrete element API. External consumers
observe updates through bindings, property values, layout, visual state and
frame extraction. `UiDirtyMask` remains only on the low-level property and
batched-mutation contracts until those contracts receive a typed facade.

The intended ownership split is:

- consumer-facing DeltaXAML: elements, controls, layout, bindings, styles,
  resources, input, clipboard and automation;
- retained implementation: `UiElement`, `UiPropertyStore`, `UiFrame`, stores,
  routers and reusable backing storage;
- XAML layer: `XamlLoader`, `XamlTypeRegistry` and load diagnostics;
- advanced engine/render adapters: handles, mutation batches, dirty masks,
  draw commands, clips, text-run identities and upload deltas;
- DeltaEditorShell: inspector-specific editor hints, schemas and theme data,
  mapped through the neutral property-source boundary.

The adapter split is additive. Existing frame, mutation and draw contracts stay
available for the game/editor integration while ordinary UI consumers can use
the retained controls without depending on ECS, DeltaEngine or Vulkan.

## Legacy draw producer and migration

`Delta.XAML.Contract.UiDisplayList` is the authoritative cross-project output.
The current implementation still produces the legacy `IUiDrawList` while its
consumers migrate. That legacy surface
publishes ordered rectangle commands, clip entries and `UiTextRun` requests.
`UiTextRun` contains content, font/style data, positioned bounds, clip,
`GlyphRunKey`, `Owner`, `OwnerGeneration` and dirty-data `Version`; it is not shaped glyph data,
atlas data or GPU state. DeltaText may consume the request, while DeltaRender
owns shaping results, atlases, batching and uploads.

Legacy `IUiDrawList.Version` changes only when commands, clips or text-request values
change. Unchanged extraction keeps the version, text owners, text versions and
backing arrays stable. `GetDeltaSince` reports precise command, clip and text
ranges for the immediately preceding version; an older version receives full
current ranges and must reread the current snapshot.

The `ReadOnlyMemory` properties are borrowed views into the producing
`UiFrame`. They remain valid only until that frame's next
`ExtractDrawList` call. Consumers must finish reading before the next
extraction, or copy the values when retaining them beyond the frame.

The dialect intentionally does not reproduce WPF/Avalonia's full dependency
property system. Unsupported elements/properties produce source diagnostics.
ECS/editor data enters through neutral schema/value/edit records rather than
storage handles or reflection objects.

See [WORKFLOW.md](WORKFLOW.md) for headless checks, [TODO.md](TODO.md) for
selected work, [API_REVIEW.md](API_REVIEW.md) for the additive facade plan,
[PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md) for the authoritative cross-project
contract,
[ARCHITECTURE.md](ARCHITECTURE.md) for the implemented boundary,
[IDEAS.md](IDEAS.md) for deferred tooling and
[../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md) for shared acceptance.
### Resource-backed value precedence

`UiPropertyStore` is the single retained value store. Its deterministic order
is `Default < Style < Binding < Local < Handle`; `Handle` is the transient,
generation-safe mutation layer and remains compatible with the existing
engine-neutral handle API. `SetDefault`, `SetStyle`, `SetBinding`,
`SetLocal`, and `SetHandle` update one effective value without replacing the
other source slots. `SetStyleResource` uses the typed
`UiResourceReference`; aliases report missing-resource and cycle diagnostics.
The minimal XAML form is `ForegroundResource="Color.Text"` together with
`XamlLoader.LoadFrame(source, resources)`; it binds the TextBlock foreground
without making resource syntax a general string metadata convention.
