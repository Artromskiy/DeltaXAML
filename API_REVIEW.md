# DeltaXAML public API review

Reviewed against the current [Avalonia API reference](https://docs.avaloniaui.net/api)
on 2026-08-23. This is a compatibility and layering review, not a proposal to
copy Avalonia's dependency-property or control model. No runtime API is
changed by this document.

## Baseline

Avalonia presents a consumer-facing control hierarchy above several distinct
services: `AvaloniaObject` owns property and binding operations,
`Layoutable` owns measure/arrange and layout invalidation, `InputElement` owns
focus and routed input, and the controls layer supplies panels, content
controls, text controls and scrolling. The reference also exposes XAML
loading/type resolution, selectors/setters/templates, and a larger media/text
stack:

- [AvaloniaObject](https://docs.avaloniaui.net/api/avalonia/avaloniaobject)
  provides `GetValue`/`SetValue`, binding, and value-source behavior.
- [Layoutable](https://docs.avaloniaui.net/api/avalonia/layout/layoutable)
  provides `Measure`, `Arrange`, `InvalidateMeasure`, `InvalidateArrange`,
  bounds and layout properties.
- [InputElement](https://docs.avaloniaui.net/api/avalonia/input/inputelement)
  provides focus, routed-event handlers and event raising.
- [Controls](https://docs.avaloniaui.net/api/avalonia/controls) contains the
  ordinary control vocabulary, including `Panel`, `StackPanel`, `Grid`,
  `ContentControl`, `TextBox` and `ScrollViewer`.
- [Markup.Xaml](https://docs.avaloniaui.net/api/avalonia/markup/xaml) and
  [Styling](https://docs.avaloniaui.net/api/avalonia/styling) expose XAML
  loading/type-resolution and styles, selectors, setters and templates.

DeltaXAML intentionally has a much smaller surface. `UiElement` and the
controls are retained classes; `IUiFrame` owns layout and produces an
`IUiDrawList`; properties use local/style/binding/handle values; and input is
represented by platform-neutral pointer, physical-key, UTF-text and IME
packets. That is suitable for a shared game/editor renderer, but the two
levels are currently visible in the same public assemblies.

## Where implementation details leak

These are layering findings, not immediate breaking-change requests.

1. `IUiElement` exposes `UiElementId`, `Generation`, `DirtyFlags`,
   `DesiredSize`, `Bounds`, `Clip` and `VisualState`. Stable identity and
   invalidation are valuable to a retained implementation and to an engine
   adapter, but a normal control consumer should mainly need hierarchy,
   properties, layout and input behavior.
2. `IUiPropertyStore` and `UiMutation` expose string names, `object?` values,
   `UiPropertyHandle`, `UiDirtyMask` and generation validation. This is a good
   ECS/engine fast path, not an ergonomic everyday property API. It also makes
   invalidation policy part of every caller's write operation.
3. `IUiFrame.ExtractDrawList`, `IUiDrawList`, `UiDrawCommand`, clip entries,
   `UiDrawDelta` and `UiTextRun` make the renderer submission model public.
   `GlyphRunKey`, owner/version identity and clip IDs are renderer-adapter
   inputs even though they do not mention Vulkan or an atlas. They should stay
   available for DeltaRender, but ordinary UI code should not have to reason
   about command ranges or upload deltas.
4. `UiResourceHandle` and `UiDrawKind.Image`/`Viewport` are resource and
   submission concepts. They are neutral at the current boundary, but belong
   to the render adapter layer if the high-level API is split later.
5. `IUiPropertySource` is engine-neutral and correctly lives outside ECS, but
   `ComponentKey`, `FieldKey` and the closed `UiEditorKind` vocabulary make it
   inspector-shaped. That is acceptable for the current DeltaEditorShell
   handoff; it should not become the only general-purpose data/template API.
6. `XamlTypeRegistry.Register(string, Func<UiElement>)` is a deliberately
   narrow factory hook. Compared with Avalonia's XAML type resolver and
   markup/styling surfaces, it does not provide namespaces, attached
   properties, markup extensions, converters, event attributes or compiled
   XAML. This is a dialect limit rather than an accidental renderer leak.

The main conclusion is that DeltaXAML is not ECS-dependent, but its public
core currently combines a high-level retained tree with an intentionally
low-level, allocation-aware submission contract. The latter should be named
and documented as an advanced adapter boundary, not removed from the current
smoke/API.

## Neutral facades to simplify

The following should be additive facades over the existing implementation:

1. Add an ordinary-control facade for tree ownership, properties, layout and
   input. It should hide ID/generation/dirty details by default while allowing
   an advanced adapter to obtain the retained identity explicitly.
2. Add a typed property-key/value facade (`UiPropertyKey<T>` or an equivalent
   non-reflection key) for common consumers. Keep `UiPropertyHandle` and
   `UiMutation` as the generation-safe fast path for engine adapters.
3. Separate a high-level frame/layout boundary from a render submission
   boundary. A future `IUiRenderSnapshot`/adapter can consume the existing
   draw list without forcing ordinary consumers to inspect command storage.
4. Keep `IUiPropertySource` as the current neutral bridge, but introduce a
   generic schema/value source before adding more inspector terminology. The
   DeltaEditorShell adapter can map component/field metadata to that generic
   source.
5. Keep `UiTextRun` renderer-neutral, but place glyph identity, clip identity
   and versioned upload deltas behind the future render adapter facade. The
   text owner remains responsible for text, style, layout identity and
   invalidation; no shaping or rasterization belongs in DeltaXAML.

None of these facades should duplicate the retained tree or create a second
binding system. They should delegate to `UiElement`, `UiPropertyStore` and
`UiFrame`, preserving stable identity and warm-frame storage.

## Details that should remain engine-specific

Keep the following in the advanced/adapter-facing layer:

- `UiElementId` generation, `UiPropertyHandle`, `UiMutation`, and
  `UiDirtyMask`, because they provide safe batched writes and precise
  invalidation for engine-owned systems.
- `IUiDrawList`, command/clip ranges, `UiDrawDelta`, `UiResourceHandle` and
  `UiTextRun` submission identity, because the renderer needs stable data and
  upload deltas. DeltaXAML must remain ignorant of Vulkan, GPU buffers,
  texture atlases and glyph rasterization.
- Recursive retained-tree ownership, reusable backing arrays and frame-local
  extraction state. These are implementation ownership, not ECS storage.

Keep pointer/key/text/IME records, focus/capture and routed phases neutral.
SDL, OS clipboard, OS accessibility and IME translation belong in platform or
engine adapters. `IUiPropertySource` should remain free of ECS and DeltaEngine
references even while its inspector-oriented records are being generalized.

## Type-policy audit (REVIEW_PLAYBOOK)

The current internal choices satisfy the identity/ownership/value-semantics
rules without a speculative conversion:

| Shape | Current examples | Decision and reason |
| --- | --- | --- |
| `sealed class` / class | `UiElement` and controls, `UiFrame`, internal `DrawList`, `UiInputRouter`, property/resource stores, styles, templates, themes, bindings | Retained identity, mutable state, ownership of children/arrays, event subscriptions or lifecycle. These must not become structs. |
| `readonly record struct` | `UiSize`, `UiPoint`, `UiRect`, `UiColor`, IDs/handles, `GridLength`, input packets, draw commands/ranges and deltas | Immutable boundary values with value semantics. They are stored in reused arrays or passed by `in` at hot boundaries. |
| `record class` | `XamlLoadResult`, `XamlFrameLoadResult` | Immutable result objects containing nullable roots and diagnostic lists; reference identity avoids copying the list-bearing result. |
| larger immutable records | `UiPropertySchema`, `UiSchemaValue`, `UiTextRun`, `UiDrawCommand` | Retained as record structs for contiguous backing-array transport and API compatibility. They exceed the usual small-value guideline, so this is a measured exception, not a general rule; do not copy them individually in hot paths. Revisit only with allocation/copy measurements. |
| `ref struct` / `readonly ref struct` | None | No public borrowed-memory cursor/view contract exists. `ReadOnlyMemory` is the correct lifetime-safe boundary for draw-list storage today. |

`UiValue` remains a class because the property store holds it through
`IUiValue`; changing it to a struct would introduce interface boxing and would
not improve the retained ownership model. No class-to-struct or public record
shape change is justified by this audit. The result is a policy application to
the existing internal code without changing runtime API or allocation
behavior.

## Additive simplification plan

1. **Inventory and freeze (now).** Keep all current public types and the
   existing smoke/API. Treat the IDs, mutation masks and draw-list contracts
   as advanced adapter contracts and keep their nullable annotations intact.
2. **Add facades.** Introduce high-level typed property and render-snapshot
   facades that delegate to the current stores/frame. Do not add a second tree,
   binding engine or renderer branch.
3. **Move consumers to facades.** Update ordinary game/editor composition to
   use the facades; keep DeltaRender and the engine adapter on the existing
   stable draw/mutation contracts until migration is measured.
4. **Generalize data source metadata.** Add a generic schema/value source and
   let DeltaEditorShell map its inspector records to it. Keep the current
   `IUiPropertySource` as a compatibility adapter during the migration.
5. **Partition assemblies/namespaces.** If usage confirms the split, place
   high-level controls/layout/input in the consumer-facing namespace and draw,
   mutation and upload-delta contracts in an adapter namespace/assembly. This
   is a packaging step, not a type rewrite.
6. **Deprecate only after evidence.** Add deprecation or advanced-editor
   annotations only after DeltaEditorShell, game UI and the smoke harness use
   the new facades. Every removal requires a compatibility decision and a
   before/after allocation/layout check.

## Scope of this commit

Only this review document is added. No public API, runtime type, test, engine
reference or renderer dependency is changed; no Avalonia dependency is added.
