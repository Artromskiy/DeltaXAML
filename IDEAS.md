# DeltaXAML ideas

Not active work:

- Schema-driven VS Code completion, diagnostics, preview and hot reload.
- Theme resources/templates after primitive controls and invalidation stabilize.
- Accessibility and full IME composition after neutral input is proven.
- A Grid/design dialect compatible in spirit with XAML tooling; native Blend
compatibility is not assumed.
- Compiler-only text-content sugar for single-content controls, for example
  `<Button>Save</Button>` (and, if selected, `<Button Content="Save" />`). The
  typed artifact would lower the string to a generated `TextBlock` child with
  stable identity and source mapping. This must not add `UiButton.Text`, an
  object-valued runtime content slot, implicit runtime conversion or a second
  text path; whitespace, localization, binding and inherited-style semantics
  must be specified before selection.

## Renderer-neutral geometry, effects and custom visuals

The current display contract already names `RoundedRectangle` and `Border`, but
does not carry corner radii, stroke parameters or paint references. The current
`UiClip` is rectangular, and `UiTextDraw` carries a solid color rather than a
text paint. Record the following as one future contract revision; do not add
renderer or shader objects to the DeltaXAML user API.

- Add typed neutral geometry for rectangles, rounded rectangles and compiled
  path/resource geometry. A rounded rectangle should support four corner
  radii, with normalization against the arranged bounds.
- Add separate stroke data: paint/resource identity, thickness and later, if
  needed, line join/cap. Stroke and background remain independent properties.
- Add resource-backed paint for solid, linear/radial gradient, image and
  pattern fills. Keep resource data immutable and use resource dependency
  invalidation rather than rebuilding the retained tree.
- Add a compact shadow/glow effect descriptor. The first version should cover
  offset, blur, spread and color; multiple effects, backdrop blur and complex
  filters remain renderer-specific until their ordering and cost are proven.
- Extend clip data with a geometry kind and parent relationship. Rectangular
  clips stay the scissor fast path; rounded/path clips are expressed as
  semantic masks and executed by the renderer with stencil, mask or an
  off-screen pass.
- Allow text paint references for gradient or effect-painted glyphs. Changing
  text paint must preserve shaped text and layout identity, and must invalidate
  only paint/visual output.
- Keep animation host-driven through typed mutations or a renderer parameter;
  DeltaXAML owns no clock. Radius, paint and effect changes must preserve stable
  element identity and avoid per-frame replacement arrays.

Clipping ownership is deliberately split: DeltaXAML computes declarative clip
  geometry, clip hierarchy, hit-test semantics and invalidation; DeltaRender
  maps rectangular clips to scissor and non-rectangular clips to GPU masks or
  stencil. `CornerRadius` must not implicitly clip child content; an explicit
  clip-to-bounds/geometry decision is required.

The existing semantic custom-visual hook (`UiVisualTypeId` plus
`UiResourceId`) is sufficient for a renderer registry, but it is not yet an
arbitrary shader API. A future adapter may map a stable visual identity to a
validated DeltaShader artifact, pipeline and typed parameter layout. XAML may
select that identity and provide neutral typed resources/parameters, but must
not accept GLSL, SPIR-V, Vulkan handles or renderer pipeline objects. Define
the parameter payload and lifetime in a separate contract revision before
implementing custom effects.

Candidate acceptance coverage:

- rounded background/stroke rendering with zero, uniform and per-corner radii;
- separate rounded child clipping and nested clip hierarchy;
- gradient fill, gradient text and shadow/glow paint-only invalidation;
- unchanged shaped text across color, gradient and effect animation changes;
- deterministic resize/DPI normalization and no-allocation warm frames;
- unsupported geometry/effect diagnostics without a solid-rectangle fallback;
- semantic custom visual registration and typed-parameter validation at the
  DeltaRender adapter boundary.

## Development-host XAML hot reload

Provide a development-only hot-reload session for `.dxaml` hosts. This is an
IDE/sample-host capability, not part of the retained UI kernel, renderer
contract or Release runtime.

The first implementation can use a debounced file watcher and a full reload:

```text
source save -> parse/load -> diagnostics -> frame-boundary swap
            -> Layout(viewport, dpi) -> BuildDisplayList -> render
```

Keep the last valid document active when a save is temporarily incomplete or
invalid, and report source-range diagnostics without stopping the render loop.
Reuse the host's text, font, image, resource and binding services; a shader or
renderer rebuild is not needed for layout-only edits. The watcher must use an
explicit source root and must not search for assets through hard-coded probe
paths.

The follow-up implementation should compile a semantic reload patch and apply
it to the existing document at a frame boundary. Match unchanged nodes through
stable generated identity/name-scope entries, update only affected properties,
resources, styles and child order, and preserve binding context, focus,
selection, scroll and input state where identity remains valid. A temporary
candidate document is acceptable during validation for the initial host-only
full-reload mode, but there must be one active retained document and one
canonical `Dispatch -> Layout -> BuildDisplayList` path after the swap.

Supported edits should include property values, text, layout, styles, resources
and child add/remove/reorder. Changes that introduce new C# types, handlers,
packages, root types or generated-only declarations should produce a clear
reload diagnostic and require a rebuild/restart. As-you-type and on-save modes
can share the same session; on-save is the safer default for incomplete XML.

Acceptance should cover: valid edit, invalid edit with last-good-frame
retention, resource/style invalidation, child reorder, stable identity/state
preservation, resize/DPI after reload, no duplicate active document/tree, and a
renderer-independent headless host test. The existing `RoundedRectangle.Render
--watch` behavior is a prototype reference, not the final shared API.
