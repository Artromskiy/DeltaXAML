# UI rendering design

This document defines the intended cross-project design for rendering a
DeltaXAML display list. It does not add a renderer dependency to DeltaXAML and
does not claim that the future adapter or effect payloads are implemented.

## Ownership

| Area | Owner | Responsibility |
|---|---|---|
| XAML and retained state | DeltaXAML | Load, style, layout, invalidation, clip hierarchy, draw order and neutral `UiDisplayList` production |
| Font processing | DeltaText | Font instances, Unicode, shaping and Coverage/SDF/MSDF/color glyph images |
| Shader authoring | DeltaShader | Standard text/UI shader source, validation, final SPIR-V and binary `ShaderAbi` artifacts |
| GPU adapter | DeltaRender.XAML | Consume `UiDisplayList`, resolve resources, build atlas/instance buffers, batch and add RenderGraph passes |
| GPU execution | DeltaRender | RenderGraph, Vulkan resources, synchronization, raster commands and pipeline cache |
| Host | DeltaEngine or game/editor | Window/target, DPI, input acquisition, frame order and animation time |

DeltaXAML must not reference DeltaRender, DeltaShader, Vulkan, SDL, DeltaText
implementation types or Engine/ECS storage. DeltaRender.XAML is a consumer-side
adapter and must not become a second UI tree or property system.

## Frame path

```mermaid
flowchart LR
    XAML[DeltaXAML retained document]
    LIST[Borrowed UiDisplayList<br/>Visuals + Text + Clips + Order]
    ADAPTER[DeltaRender.XAML<br/>UiRenderFeature]
    TEXT[DeltaRender.Text<br/>atlas and text batching]
    SHAPE[DeltaText<br/>shape and glyph images]
    SHADER[DeltaShader<br/>source + final artifacts]
    GRAPH[DeltaRender RenderGraph]
    GPU[Vulkan]

    XAML --> LIST
    LIST --> ADAPTER
    ADAPTER --> TEXT
    TEXT --> SHAPE
    ADAPTER --> SHADER
    TEXT --> SHADER
    ADAPTER --> GRAPH
    TEXT --> GRAPH
    SHADER --> GRAPH
    GRAPH --> GPU
```

The adapter iterates only `UiDisplayList.Order`. It must preserve an arbitrary
sequence such as `visual, text, visual`; it may not draw all visuals first and
all text later. The display list is borrowed. The adapter either consumes it
while the list is valid or copies the small payload values into its own reused
staging storage before the producer mutates the document.

## Current contract and next extension

The current `DeltaXAML.Contract` carries solid rectangles, images, text fill,
rectangular or rounded clip semantics and canonical ordering. `UiVisualDraw`
has a semantic `UiVisualTypeId`, a `UiResourceId` and fixed-size
`UiVisualPaint`; `UiTextDraw` has shaped text, baseline, fixed-size
`UiTextPaint` and clip. These are identities and renderer-neutral values, not
shader or Vulkan handles.

The paint-bearing form is intentionally compact for hot, fixed-size parameters;
variable immutable data is addressed by resource identity:

```csharp
public readonly record struct UiTextPaint(
    float4 FillColor,
    float4 OutlineColor,
    float OutlineWidth,
    UiResourceId Effect);

public readonly record struct UiVisualPaint(
    float4 FillColor,
    float4 StrokeColor,
    float StrokeWidth,
    float4 CornerRadii);
```

Gradient stops, image data, shadows and other variable payloads should be
immutable resources addressed by `UiResourceId`, with resolution performed by
the renderer adapter. Animation time remains host/render state and never
enters DeltaXAML retained state.

Rounded clipping is a separate semantic capability from rounded drawing. The
current `UiClipRegion` carries bounds, parent links, a semantic clip kind and
corner radii. The choice between scissor, stencil, mask or shader discard
belongs to DeltaRender.XAML/DeltaRender, while DeltaXAML owns only the logical
clip description.

## Adapter API shape

The convenient consumer entry point should be a concrete adapter in
`DeltaRender.XAML`, not a new frozen RenderGraph contract:

```csharp
uiFeature.AddToGraph(in displayList, graph, viewport);
```

The adapter is responsible for:

1. validating the order references and clip indices;
2. resolving semantic resources through an adapter-owned registry;
3. routing `UiTextDraw` values to the existing text adapter;
4. using DeltaText shaped values without reshaping unchanged runs;
5. producing renderer-owned atlas, instance and upload ranges;
6. adding ordinary transfer and raster passes to the RenderGraph.

No adapter API may expose native handles, atlas page IDs, UV allocation,
pipeline IDs or retained XAML nodes to a game or editor.

## Standard shader placement

Shader source belongs to DeltaShader:

```text
DeltaShader/src/DeltaShader.Text/
DeltaShader/src/DeltaShader.Ui/
```

`DeltaShader.Text` owns Coverage/SDF/MSDF text entry points and their ABI.
`DeltaShader.Ui` should own solid/rounded rectangles, borders, gradients,
image tinting and clip/mask algorithms. `DeltaShader` publishes validated
SPIR-V plus binary `ShaderAbi` artifacts. DeltaRender consumes those artifacts
and owns pipeline/cache construction. Generated artifacts are build/package
outputs owned by DeltaShader; they are not hand-authored files in DeltaXAML.

The current text shader already has `TextColor`, `OutlineColor` and
`OutlineWidth` in its shader-visible parameters, and the MSDF fragment applies
outline coverage. The next text pass must carry equivalent paint data from
the UI draw request and make SDF/MSDF behavior consistent. DeltaText itself
does not need to know about outline shaders; it only needs to produce a valid
distance field with the requested range.

## Clipping and effects

Layout should avoid accidental overflow, but clipping remains required for
ScrollViewer content, nested panels, text glyph bounds, shadows, animation and
intentional masks. Ownership is split as follows:

- DeltaXAML computes logical nested clip relations and emits them in the list;
- DeltaRender.XAML intersects rectangular clips and forms scissor batches;
- DeltaShader supplies rounded/mask distance functions where needed;
- DeltaRender chooses scissor, stencil, mask pass and synchronization;
- the host chooses the screen-space or off-screen target.

The same display list can therefore serve a HUD, editor overlay or world-space
render target. World-space projection is a consumer transform, not a second
XAML runtime.

## Implementation order

1. Add a `DeltaRender.XAML` adapter for the existing list: order, solid/image,
   text fill and rectangular clips.
2. Add headless order/clip/resource/lifetime tests and a RenderGraph adapter
   smoke without a native window.
3. Add producer extraction for the new paint/clip values and verify that style
   changes do not reshape unchanged text.
4. Add `DeltaShader.Ui` and complete common SDF/MSDF text fill-plus-outline
   artifacts.
5. Add rounded rectangles, borders and gradients, then rounded clip/mask.
6. Add custom visual registration keyed by `UiVisualTypeId`, with explicit
   unsupported diagnostics and no fallback rectangle.
7. Measure dirty uploads, atlas reuse, batching and unchanged-frame behavior
   at realistic UI sizes before changing storage or sort policy.

The first adapter must preserve semantic order even when that creates several
draw batches. Sorting by material is legal only inside a contiguous order range
that cannot cross another draw reference or clip boundary.

## Performance principles adapted from browser renderers

Browser compositing is a useful reference for the cost model, but DeltaXAML
does not reproduce a browser render tree or CSS property system. The equivalent
rules for this stack are:

- Keep layout, invalidation and visibility decisions in the retained document;
  keep rasterization and composition in Render.
- Treat a promoted layer as a measured Render-owned cache for an expensive,
  independently transformed subtree. Do not make every control a GPU layer.
  A transform/opacity-only update should be able to reuse the existing visual
  and text payloads, but promotion must have an explicit memory and upload cost.
- For large rounded rectangles, prefer a nine-region/analytic representation:
  ordinary center and edge quads plus small corner regions or an SDF fragment.
  The exact segmentation is an adapter optimization and must not change the
  `UiDisplayList` semantic order.
- Cache a rounded clip mask or stencil result by resource identity, size,
  radii, DPI and clip generation. Scrolling content may reuse a stable mask;
  resizing or changing radii invalidates it.
- Use bounds and clip intersection for early culling. A subtree fully outside
  the target can skip measure/visual extraction only when its retained layout
  contract permits that decision; it must remain available for hit testing and
  later viewport changes.
- Form upload ranges from dirty owners and contiguous order runs. Never sort
  across an intervening draw reference merely to reduce pipeline changes.

This gives the useful browser ideas to the right owners without leaking layer,
mask, atlas or GPU handles into XAML. The ECS consultation should validate the
data-oriented storage and dirty-range details before implementation.
