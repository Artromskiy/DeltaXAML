# DeltaXAML.Contract

`DeltaXAML.Contract` is the authoritative producer-owned boundary between
platform input, the retained DeltaXAML library and DeltaRender. It is a small
contract assembly, not an abstract UI framework and not a second rendering API.

The ordinary loader/document/element/property API is specified separately in
[LIBRARY_CONTRACT.md](LIBRARY_CONTRACT.md). Do not move retained implementation
mechanics into this cross-project packet contract.

```text
DeltaEngine input -> DeltaXAML.Contract input packets -> DeltaXAML
DeltaXAML -> UiDisplayList -> DeltaRender adapter -> Vulkan render graph
DeltaText ShapedText ------------------^
```

## Ownership

The contract owns only platform-neutral packets and borrowed display-list
values. `DeltaXAML` owns XAML loading, the retained tree, properties,
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

## DeltaMaths and coordinates

Geometry and color reuse `DeltaMaths`: `float2` represents positions, sizes
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
build. It contains `UiVisualDraw` values for renderer-neutral visual requests,
`UiTextDraw` values for already shaped text requests and `UiClipRegion` values
for nested clipping. `UiClipId` is a frame-local list index and therefore
remains an integer.

`UiDisplayList.Order` is the canonical mixed draw sequence. Each
`UiDrawRef.Kind` selects either `Visuals` or `Text`, and its `Index` addresses
that span. Consumers must iterate `Order` to preserve retained traversal order;
they must not assume that all visuals precede all text. The span is borrowed with
the other display-list spans. A producer must emit one reference for every visual
or text payload and must not emit an invalid kind or index. The compatibility
boundary always supplies `Order`; there is no implicit visuals-first or
visuals-then-text fallback.

The top-level types are intentionally small:

```csharp
public enum UiDrawKind : byte { Unknown = 0, Visual = 1, Text = 2 }
public readonly record struct UiDrawRef(UiDrawKind Kind, int Index);
public readonly record struct UiClipRegion(
    float4 Bounds, UiClipId Parent, UiClipKind Kind, float4 CornerRadii);
public readonly record struct UiVisualPaint(
    float4 FillColor, float4 StrokeColor, float StrokeWidth, float4 CornerRadii);
public readonly record struct UiTextPaint(
    float4 FillColor, float4 OutlineColor, float OutlineWidth, UiResourceId Effect);
public ReadOnlySpan<UiDrawRef> Order { get; }
```

`UiDrawRef` is a payload reference, not a second command representation. Clips
remain in `Clips` and are reached through the selected visual or text payload.

`UiVisualDraw` describes a renderer-neutral primitive through its kind, logical
bounds, linear color, optional semantic `UiVisualTypeId`, optional
`UiResourceId` and clip reference. A custom visual carries a stable semantic
`UiVisualTypeId`; DeltaRender resolves that identity to its registered shader
and pipeline. DeltaXAML never exposes a Vulkan pipeline ID or a
`ShaderArtifact` in this boundary.

Text does not duplicate the DeltaText font contract. `UiTextDraw` transports an
already shaped `ShapedText` plus baseline placement, linear color and clip.
Exact font instances, variations, direction, script, language, OpenType
features, glyph IDs, advances and clusters remain owned by DeltaText.

`UiClipRegion` describes logical bounds, an optional parent region and a
semantic shape. Rectangles may use scissor; rounded regions may use an analytic
shader, stencil or mask selected by the renderer adapter. The contract carries
corner radii but does not prescribe the GPU implementation.

`UiVisualPaint` carries fixed-size fill, stroke and per-corner-radius values.
`UiTextPaint` carries fill, outline width/color and an optional effect resource
identity. Variable gradient stops, image data, shadow configuration and other
large values remain immutable resources addressed by `UiResourceId`.

The six-argument constructor of `UiVisualDraw` and the four-argument
constructor of `UiTextDraw` remain the fill-only convenience forms. The
paint-bearing constructors are the canonical form for effects; both forms
carry the same semantic payload and do not expose shader or pipeline handles.

## Resource identity rule

Durable resource and semantic identities crossing project boundaries are typed
wrappers over `Guid`. Runtime-local slots, generations, frame-local clip
indices, pointer IDs and GPU handles are not resources and remain compact
integer values.
