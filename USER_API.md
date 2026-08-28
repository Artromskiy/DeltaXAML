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
<StackPanel xmlns:x="urn:delta-xaml"
            x:DataType="Game.HudModel"
            Fill="true">
    <Resource x:Key="Accent" Type="Color" Value="#304860" />
    <Style x:Key="ActionStyle" TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource Accent}" />
    </Style>
    <TextBlock Text="{Binding Title}" FontKey="default" FontSize="18" />
    <Grid Columns="240,*" Rows="*">
        <ItemsControl>
            <TextBlock Text="Inventory" />
        </ItemsControl>
        <Button StyleKey="ActionStyle">
            <TextBlock Text="Use" />
        </Button>
    </Grid>
</StackPanel>
```

`IXamlLoader` is the explicit cold source-loading entry point selected by the
library contract. It constructs the same retained elements, descriptors,
property store and node store as generated code. Generated production
artifacts never silently call it and never fall back to reflection.

## Elements and controls

`UiElement` is the retained identity node. It owns parent/children relations,
participation and property access. Controls are deliberately thin, normally
sealed types over the same retained tree:

- `UiPanel` and layout containers;
- `UiBorder` and content presenters;
- `UiButton`, `UiToggleButton` and command controls;
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
host writes through `UiElement.TrySet`. A host may group several writes before
the next `Layout`; each write still resolves through the one retained property
store and its typed invalidation metadata. Handles never expose internal arrays
or dirty masks.

## Bindings

`UiCompiledBinding<TSource,TValue>` is the default no-reflection path. XAML
compilation emits typed read/write accessors and reports invalid paths at build
time.

Generated artifacts attach these bindings with `SetCompiledBinding` and a
typed property descriptor. Their source notifications are collected at the
artifact boundary and applied by the document binding stage; string-based
`SetBinding` is an explicit cold source/tooling API and is not used by generated
artifacts.

- `OneTime` reads once and registers no notification;
- `OneWay` updates the target when the source changes;
- `TwoWay` additionally writes validated target changes back to the source.

Generated artifacts use one source notification boundary for all compiled
bindings and expose `RefreshBindings()` for sources without notifications.

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

Compiled artifacts use `UiTemplateId` with the typed
`UiTheme.RegisterTemplate(UiTemplateId, UiTemplate)` and
`UiElement.SetCompiledTemplate(UiTemplateId)` methods. The string template
key overloads remain for cold loader/tooling composition.

Templates receive an `IUiTemplateFactory`; its `Create` method returns the
template subtree for the existing owner and resource catalog. This keeps
compiled template construction typed and avoids a per-template delegate in the
generated artifact. A generated template binding reads the owner's typed
`BindingContext`; an incompatible context is a load-time construction error.

Generated style artifacts use the typed overloads on `UiStyle` with the
`UiElementProperties`, `UiTextBlockProperties`, `UiNumericEditorProperties`,
`UiStackPanelProperties` and `UiGridProperties` descriptors. The existing
string overloads remain a cold loader/tooling surface. Compiled
resource setters use the registered `UiResourceId` directly; name-based
resource keys remain available for cold markup loading.

Generated element members use the typed `UiElement.SetDynamicResource` and
`SetStaticResource` overloads with `UiProperty<T>` and `UiResourceId`; the
string-property overloads remain the cold loader/tooling path.

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

An attributed custom XAML type is a concrete `UiElement` with an accessible
parameterless constructor. `UiXamlTypeAttribute` supplies its stable type
identity and content kind; `UiXamlPropertyAttribute` supplies stable typed
literal setters. The generator emits direct construction, common element
setters, custom member setters and optional `Add(UiElement)`/`SetContent`
attachment without requiring the user type to be `partial`.

A custom type receives the common size/background/padding/participation state
of `UiElement`. It can compose built-in controls and can emit a registered
semantic visual through `SetCustomVisual`. The current public authoring API
does not register arbitrary user layout/input/visual mixins into the frame
pipeline; custom behavior is composed from built-ins and host code. Bindings,
resources and styles on a custom property require a public typed
`UiProperty<T>` descriptor and are otherwise rejected at generation time.

`XamlTypeCatalog.Register(name, factory)` assigns a deterministic identity from
the qualified XAML name for local catalog use. Generated or shared artifacts
should use `Register(name, UiTypeId, factory)` with a caller-assigned stable ID;
the catalog indexes both name and identity and rejects one identity registered
for different names.

## Supported production dialect

Generated XAML supports the built-in elements `Panel`, `StackPanel`, `Grid`,
`ItemsControl`, `Border`, `ContentControl`, `ScrollViewer`, `Button`,
`ToggleButton`, `TextBlock`, `TextBox` and `NumericEditor`. The following source
features compile to typed artifacts:

- common size, background, padding, fill, enabled/selected, style and template
  properties;
- text/font/foreground and numeric editor properties;
- stack orientation and fixed/`Auto`/star grid definitions;
- `x:Name`, `x:DataType`, `{Binding ...}`, typed converters and
  `OneTime`/`OneWay`/`TwoWay` modes;
- scalar/static/dynamic resources, styles, visual states and templates;
- custom controls and direct custom properties declared with
  `UiXamlTypeAttribute` and `UiXamlPropertyAttribute`.

Input is attached in code through neutral `UiInputEvent` packets and ordinary
control events. The compiler intentionally rejects XAML event-handler members,
attached properties such as `Grid.Row`, `x:Reference`, `RelativeSource`,
`TemplateBinding`, undeclared binding paths, unsupported markup extensions and
automation markup. These produce stable compiler/generator diagnostics; there
is no reflection or alternate-runtime fallback. Collection source generation,
data templates, triggers beyond the declared visual-state setters and general
MAUI/WPF/Avalonia syntax are not part of the current dialect.

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

## Implementation status

Generated artifacts and the explicit cold loader converge on one retained
identity, property store, generation-safe node store and fixed stage pipeline.
Generated construction, bindings, resources, styles, states and templates are
the production execution path. The cold loader and untyped property access are
bounded public authoring/tooling entry points, not a second runtime.
