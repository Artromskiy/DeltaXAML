# Delta.XAML.Contract

`Delta.XAML.Contract` is the authoritative producer-owned boundary between
platform input, the retained DeltaXAML library and DeltaRender. It is a small
contract assembly, not an abstract UI framework and not a second rendering API.

```text
DeltaEngine input -> Delta.XAML.Contract input packets -> DeltaXAML.Core
DeltaXAML.Core -> UiDisplayList -> DeltaRender adapter -> Vulkan render graph
DeltaText ShapedText ------------------^
```

## Ownership

The contract owns only platform-neutral packets and borrowed display-list
values. `DeltaXAML.Core` owns XAML loading, the retained tree, properties,
controls, bindings, layout, focus, hit testing and input semantics. DeltaText
owns font instances and shaping. DeltaRender owns Vulkan resources, shaders,
pipelines, batching and submission. DeltaEngine owns the event loop and call
order.

The normal library flow is explicit:

```csharp
document.Dispatch(input);
document.Layout(viewport, dpiScale);

UiDisplayList displayList = document.BuildDisplayList();
xamlRenderer.AddToGraph(displayList, graph, target);
```

There is no `Update`, frame clock, delta time, command buffer, renderer service
or application lifecycle in this contract.

## Maths and coordinates

Geometry and color reuse `Delta.Maths`: `float2` represents positions, sizes
and deltas; `float4` represents rectangles and colors. Rectangle properties
document the `(X, Y, Width, Height)` convention. Colors are linear RGBA. UI
coordinates use logical units until the consumer applies its DPI transform.

## Input

Pointer packets cover mouse, touch and pen, including boundary, cancellation
and capture-loss events. Device button values are extensible rather than a
closed enum. Physical and logical key identities are opaque normalized values;
the platform adapter defines their mapping. Modifier bits are a convenience
snapshot only. Arbitrary chords and key sequences are implemented from the
pressed physical-key state.

Committed UTF-16 text is separate from key events. IME composition is an
uncommitted pre-edit stream for complex input, dead keys and composition;
committed text arrives through `UiTextInput`.

## Drawing and text

`UiDisplayList` is a stack-only borrowed view. Its storage remains owned by the
producing document and is valid only until the next mutation or display-list
build. `UiClipId` is a frame-local list index and therefore remains an integer.

Built-in visuals describe renderer-neutral primitives. A custom visual carries
a stable semantic `UiVisualTypeId`; DeltaRender resolves that identity to its
registered shader and pipeline. DeltaXAML never exposes a Vulkan pipeline ID or
a `ShaderArtifact` in this boundary.

Text does not duplicate the DeltaText font contract. `UiTextDraw` transports an
already shaped `ShapedText` plus baseline placement, linear color and clip.
Exact font instances, variations, direction, script, language, OpenType
features, glyph IDs, advances and clusters remain owned by DeltaText.

## Resource identity rule

Durable resource and semantic identities crossing project boundaries are typed
wrappers over `Guid`. Runtime-local slots, generations, frame-local clip
indices, pointer IDs and GPU handles are not resources and remain compact
integer values.

The existing `DeltaXAML.Abstractions` assembly is a temporary migration surface
for the current implementation and consumers. It is not the source of truth for
new cross-project API. Migrate consumers to `Delta.XAML.Contract` before
removing the legacy assembly.
