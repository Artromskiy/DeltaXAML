# DeltaXAML user API

This file describes the user-facing retained UI library. It is not the
cross-project packet contract; that boundary is frozen in
[PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md). Exact selected public signatures are
defined by [LIBRARY_CONTRACT.md](LIBRARY_CONTRACT.md). Runtime implementation
mechanics are documented only in [INTERNAL.md](INTERNAL.md).

## Library model

DeltaXAML is loaded and driven by the host. It does not create a window, own an
application loop, advance a global clock or submit GPU work.

```csharp
XamlLoadResult loaded = loader.Load(source, in loadContext);
if (!loaded.Success || loaded.Root is null)
{
    Report(loaded.Diagnostics.Span);
    return;
}

UiDocument document = new(loaded.Root, textService);

document.Dispatch(input);
document.Layout(viewport, dpiScale);

UiDisplayList displayList = document.BuildDisplayList();
xamlRenderer.AddToGraph(displayList, graph, target);
```

The document remains retained between frames. Only changed bindings,
properties, styles, layout subtrees and visuals are recomputed.

## Game and editor use

The same `UiDocument` can render an editor shell or an in-game UI. DeltaEditor
is not required.

```csharp
void AddGameUi(
    GameFrame frame,
    UiDocument hud,
    IXamlRenderGraphProducer xamlRenderer)
{
    hud.Dispatch(frame.Input);
    hud.Layout(frame.ViewportSize, frame.DpiScale);

    UiDisplayList displayList = hud.BuildDisplayList();

    frame.Graph.AddWorldPasses(frame.Scene);
    xamlRenderer.AddToGraph(
        displayList,
        frame.Graph,
        frame.SurfaceColor);
}
```

Screen-space HUD, menus, inventory, consoles and overlays use the surface
target directly. World-space UI renders the same display list into an
off-screen render target which the game then samples on a mesh. This does not
require a second XAML runtime.

## XAML authoring

Production XAML is compiled. Type names, property names, resource references,
template content and binding paths are validated during the build. Generated
factories and typed setters construct the retained document without reflection.

```xml
<Grid Rows="Auto,*" Columns="240,*">
    <TextBlock
        Grid.Row="0"
        Grid.ColumnSpan="2"
        Text="{Binding Title}" />

    <ItemsControl
        Grid.Row="1"
        Items="{Binding Inventory}" />

    <ContentControl
        Grid.Row="1"
        Grid.Column="1"
        Content="{Binding SelectedItem}" />
</Grid>
```

A runtime loader may remain available for designer and hot-reload tooling, but
shipping code must not silently fall back to reflection-based inflation.

## Elements and controls

`UiElement` is the retained identity node. It owns parent/children relations,
participation and property access. Controls are deliberately thin, normally
sealed types over the same retained tree:

- `UiPanel` and layout containers;
- `UiBorder` and content presenters;
- `UiButton` and command controls;
- `UiTextBlock`, `UiTextBox` and numeric editors;
- `UiScrollViewer` and item presentation controls.

Their public inheritance is not the behavior-reuse mechanism. Reusable
behavior is composed internally from typed state and mixins, so adding a
custom control does not require choosing a deep framework base class.

## Properties

Typed access is the normal application path:

```csharp
textBlock.SetValue(UiTextBlockProperties.Text, "Launch");
string text = textBlock.GetValue(UiTextBlockProperties.Text);
```

The untyped `IUiProperty` view exists for XAML loading, diagnostics and editor
inspection. It is a cold tooling boundary; layout, input and visual extraction
do not route typed values through `object`.

Effective value precedence is:

```text
Default < Style/Trigger < Binding < Local < Handle < Animation
```

Property metadata determines whether a change affects measure, arrange,
visual extraction or hit testing.

`UiElement.GetHandle` returns a generation-safe `UiPropertyHandle` for direct
host writes. Batched handle writes are the preferred integration path for
frequently changing game/editor state. Handles never expose internal arrays or
dirty masks.

## Bindings

`UiCompiledBinding<TSource,TValue>` is the default no-reflection path. XAML
compilation emits typed read/write accessors and reports invalid paths at build
time.

- `OneTime` reads once and registers no notification;
- `OneWay` updates the target when the source changes;
- `TwoWay` additionally writes validated target changes back to the source.

`INotifyPropertyChanged` is supported for ordinary view models. High-frequency
engine state should use typed property handles or an explicit adapter instead
of producing one managed event per value update.

## Resources, styles and templates

Durable resource, property, type and semantic visual IDs are typed wrappers
over `Guid`. Human-readable XAML keys are source aliases, not runtime identity.

`UiResourceCatalog`, `UiTheme`, `UiStyle` and `UiTemplate` provide:

- static and dynamic resources;
- inherited and selector-driven style values;
- reusable visual templates;
- compiled selector/state plans;
- exact invalidation of dependent retained values.

Generated style artifacts use the typed overloads on `UiStyle` with the
`UiElementProperties`, `UiTextBlockProperties`, `UiNumericEditorProperties`,
`UiStackPanelProperties` and `UiGridProperties` descriptors. The existing
string overloads remain a cold loader/tooling compatibility surface.

Resources do not carry Vulkan objects. Images, custom visuals and other GPU
assets cross the display-list boundary through stable semantic IDs resolved by
DeltaRender.

Controls can set a renderer-neutral custom visual with
`SetCustomVisual(UiVisualTypeId, UiResourceId, UiColor)` and remove it with
`ClearCustomVisual()`. The next display list contains a `UiVisualKind.Custom`
command carrying those identities and the element bounds. DeltaXAML does not
register or resolve the renderer implementation; the consumer owns that
mapping and the associated GPU resources.

## Input and commands

`UiDocument.Dispatch` consumes neutral `UiInputEvent` packets. Pointer, key,
committed text and IME composition are distinct payloads. DeltaXAML owns hit
testing, focus, pointer capture and routing; DeltaEngine only acquires and
schedules platform events.

Commands represent semantic actions such as submit, scroll or focus. They are
not render commands and are not stored in `UiDisplayList`.

## Text

`UiDocument` receives `DeltaText.Contract.ITextService`. DeltaText owns font
instances, shaping and glyph image generation. DeltaXAML owns placement and
emits already shaped `UiTextDraw` values. DeltaRender owns atlas packing,
texture uploads and Vulkan execution.

## Display-list lifetime

`BuildDisplayList` returns the borrowed `Delta.XAML.Contract.UiDisplayList`.
Its backing storage remains owned by the document and is valid only until the
next document mutation or display-list build. Consumers must add its contents
to their render work before that invalidation point and must not retain spans.

## Custom controls

A custom control contributes:

1. a flat element class containing identity-facing properties and state;
2. plain state structs;
3. stateless capability mixins for layout, input and visuals;
4. compile-time registration consumed by the DeltaXAML generator.

Generated companion code supplies factories, property metadata and runtime
operation thunks. User controls are not required to be `partial`, do not store
algorithms in the class, and do not register runtime property dictionaries.

`XamlTypeCatalog.Register(name, factory)` assigns a deterministic identity from
the qualified XAML name for local catalog use. Generated or shared artifacts
should use `Register(name, UiTypeId, factory)` with a caller-assigned stable ID;
the catalog indexes both name and identity and rejects one identity registered
for different names.

## Ownership summary

```text
Application/game
  owns view models, property mutations and call order

DeltaEngine
  owns platform input acquisition and frame scheduling

DeltaXAML
  owns retained identity, properties, bindings, styles, layout, input
  semantics and borrowed display-list production

DeltaText
  owns font instances, shaping and CPU glyph images

DeltaRender
  owns Vulkan resources, passes, batching and submission
```

DeltaXAML has no SDL, Vulkan, DeltaEngine or ECS storage dependency. It never
owns a global delta-time value.

## Current migration status

The public library contract is selected, but parts of the repository still use
the compatibility retained implementation. New features target the compiled
descriptor architecture from `INTERNAL.md`. Compatibility APIs must either be
removed during migration or be marked `[Obsolete]` with their replacement and
removal milestone; new code must not extend them.
