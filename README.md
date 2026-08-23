# DeltaXAML

Platform-neutral retained UI and a Delta-owned XAML dialect shared by game and
editor. It has no SDL, Vulkan, Avalonia, DeltaEngine, DeltaRender, Roslyn or ECS
storage dependency.

```text
XAML -> retained tree -> properties/styles -> measure/arrange
  -> hit testing/input -> renderer-neutral primitives and positioned text
```

`DeltaXAML.Abstractions` owns neutral element/frame/draw contracts.
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

The dialect intentionally does not reproduce WPF/Avalonia's full dependency
property system. Unsupported elements/properties produce source diagnostics.
ECS/editor data enters through neutral schema/value/edit records rather than
storage handles or reflection objects.

See [WORKFLOW.md](WORKFLOW.md) for headless checks, [TODO.md](TODO.md) for
selected work, [IDEAS.md](IDEAS.md) for deferred tooling and
[../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md) for shared acceptance.
