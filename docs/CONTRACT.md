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
document the `(X, Y, Width, Height)` convention. Colors are linear RGBA.

The canonical UI coordinate convention is:

```text
origin:   top-left
X:        right
Y:        down
viewport: top-left
depth:    0..1
```

`UiVisualDraw`, `UiTextDraw`, `UiClipRegion`, hit-test points and layout JSON
use the same top-left logical coordinate space. Rectangles are half-open:
`[X, X + Width) x [Y, Y + Height)`. The Vulkan consumer uses a positive
viewport height and keeps these bounds/scissors in top-left framebuffer
coordinates; pixel-to-clip projection is a Vulkan adapter/shader detail, not
part of this contract. Logical-to-device-pixel DPI policy remains a separate
DeltaXAML backlog item; until it is specified, the producer values remain
logical and the consumer owns that boundary.

For 2D UI textures and atlases, normalized UVs use the same visual orientation:

```text
(0,0): top-left       (1,0): top-right
(0,1): bottom-left    (1,1): bottom-right
```

Texture upload must canonicalize source row order once. Vulkan does not infer
the orientation of a PNG, atlas or readback file from the UV values. Normal
maps are tangent-space data and their Y+/Y- green-channel convention is an
explicit asset/material property; it is not changed by the screen-space Y
direction.

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
or text payload and must not emit an invalid kind or index. `Identities` is
aligned with `Order`, so `Identities[i]` describes the payload selected by
`Order[i]`. Its length must always equal `Order.Length`. The compatibility
boundary always supplies `Order` and `Identities`; there is no implicit
visuals-first or visuals-then-text fallback.

The top-level types are intentionally small:

```csharp
public enum UiDrawKind : byte { Unknown = 0, Visual = 1, Text = 2 }
public readonly record struct UiDrawRef(UiDrawKind Kind, int Index);
public readonly record struct UiClipRegion(
    float4 Bounds, UiClipId Parent, UiClipKind Kind, float4 CornerRadii);
public readonly record struct UiVisualPaint(
    float4 FillColor, float4 CornerRadii, UiEffectSet EffectSet);
public readonly record struct UiTextPaint(
    float4 FillColor, UiEffectSet EffectSet);
public readonly record struct UiEffectSet(
    UiResourceId Resource,
    UiEffectTarget Target,
    UiEffectCapabilities Capabilities,
    UiEffectQuality Quality,
    float4 Outsets);
public readonly record struct UiEffectLayer(
    float4 Color, float2 Offset, float Width,
    float BlurRadius, float Spread, float Intensity);
public readonly record struct UiEffectParameters(
    UiEffectLayer Stroke,
    UiEffectLayer OuterShadow,
    UiEffectLayer InnerShadow,
    UiEffectLayer OuterGlow,
    UiEffectLayer InnerGlow,
    UiResourceId CachedMask)
{
    // All layer distances use Units (Logical by default).
    public PaintUnits Units { get; init; }
}
public readonly record struct UiEffectResource(
    UiEffectSet Set, UiEffectParameters Parameters);
public readonly record struct UiElementIdentity(uint Value, uint Generation, uint Version);
public readonly record struct UiTextDraw {
    public ShapedText Text { get; init; }
    public float2 BaselineOrigin { get; init; }
    public UiTextPaint Paint { get; init; }
    public UiClipId Clip { get; init; }
}
public UiDisplayList(
    ReadOnlySpan<UiVisualDraw> visuals,
    ReadOnlySpan<UiClipRegion> clips,
    ReadOnlySpan<UiTextDraw> text,
    ReadOnlySpan<UiDrawRef> order,
    ReadOnlySpan<UiElementIdentity> identities);
public ReadOnlySpan<UiDrawRef> Order { get; }
public ReadOnlySpan<UiElementIdentity> Identities { get; }
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
`UiElementIdentity` is the stable producer identity and version for each
ordered item: `Value` identifies the retained owner slot, `Generation` rejects
a stale occupant after slot reuse, and `Version` identifies the current
producer payload version. This identity is carried by `Identities`, not by the
text payload. Geometry-only changes are represented by the draw payload and
display-list delta; they do not require the text item's version to change.
Exact font instances, variations, direction, script, language, OpenType
features, glyph IDs, advances and clusters remain owned by DeltaText.

`UiClipRegion` describes logical bounds, an optional parent region and a
semantic shape. Rectangles may use scissor; rounded regions may use an analytic
shader, stencil or mask selected by the renderer adapter. The contract carries
corner radii but does not prescribe the GPU implementation.

`UiVisualPaint` and `UiTextPaint` carry only base fill, visual corner geometry
and the canonical `EffectSet` reference. Stroke, shadow and glow data are not
duplicated inline in draw payloads. The set
identifies one immutable, typed `UiEffectResource` containing the selected
effect parameters. `UiEffectParameters` has fixed named layers for the
canonical effect order; it is not a shader ABI and does not require a second
property store. Its target, capability flags, quality tier and left/top/right/
bottom outsets are renderer-neutral metadata; outsets affect paint bounds and
damage only, never layout, shaping or baseline.

Effect capabilities do not change `UiVisualDraw.Kind`: the producer selects
`SolidRectangle` or `RoundedRectangle` from base geometry, then the consumer
selects the prepared effect variant from `EffectSet`. A valid effect-only
visual is retained even when its base fill is transparent.

Visual and text resources use the same closed capability vocabulary: `Stroke`,
`OuterShadow`, `InnerShadow`, `OuterGlow` and `InnerGlow`. The target still
selects the primitive and shader family; it does not rename a layer. The removed
`Outline`, `InsetShadow` and ambiguous `Glow` names are invalid rather than
compatibility aliases. This is a versioned breaking mask revision: the five
capability bits are packed contiguously as `0x01`, `0x02`, `0x04`, `0x08` and
`0x10`; old serialized masks are not read.

Inner effects never add outsets. Outer shadow and outer glow add paint/damage
outsets; text stroke may also extend outside glyph geometry. None of these
outsets changes layout, shaping, font metrics or baseline. Variable gradient
stops, image data, shadow
parameters and other large values remain immutable typed resources addressed
by `UiResourceId`.

The six-argument constructor of `UiVisualDraw` and the four-argument
constructor of `UiTextDraw` remain fill-only convenience forms. Identity and
version are supplied once per ordered payload through `UiDisplayList`, keeping
the payload commands compact and allowing one consumer algorithm to process
visual and text entries together. The paint-bearing constructors are the
canonical form for effects; they do not expose shader or pipeline handles.

## Resource identity rule

Durable resource and semantic identities crossing project boundaries are typed
wrappers over `Guid`. Runtime-local slots, generations, frame-local clip
indices, pointer IDs and GPU handles are not resources and remain compact
integer values.
