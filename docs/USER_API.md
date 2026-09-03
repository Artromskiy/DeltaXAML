# DeltaXAML user API

This file describes the user-facing retained UI library. It is not the
cross-project packet contract; that boundary is frozen in
[CONTRACT.md](CONTRACT.md). Exact selected public signatures are
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

For headless layout debugging, call `BuildLayoutDiagnosticsJson()` after
`Layout`. It returns a detached, deterministic JSON snapshot of the actual
retained hierarchy; it does not create another UI tree or affect rendering:

```csharp
string layoutJson = document.BuildLayoutDiagnosticsJson(indented: true);
```

The root object contains `schemaVersion`, `layoutCompleted`, `viewport`,
`dpiScale` and `root`. Each node contains `type`, `id`, `generation`,
`visibility`, `participation`, `bounds`, `clip`, `desiredSize`,
`requestedSize`, `margin`, `padding` and an ordered `children` array.
`requestedSize.width` and `.height` are `null` when the corresponding XAML
dimension is automatic/unset. `bounds` and `clip` are the final logical layout
values, so this snapshot is suitable for comparing authored dimensions with
the result produced by the layout stages. It is a cold diagnostic string, not
a borrowed frame buffer.

Layout bounds, clips, hit-test points and text placement use a top-left origin
with X increasing rightward and Y increasing downward. The corresponding
Vulkan consumer owns conversion to device pixels and clip space; this library
API does not expose backend coordinates.

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
artifacts never silently call it and never fall back to reflection. The cold
reader currently covers the built-in controls, literal colors/brushes,
qualified grid placement, rich-text spans, simple bindings and external
named/GUID resources. Resource/style/template/trigger/behavior declaration
blocks, collection item markup and typed collection properties (`ItemsSource`,
`ItemTemplate`, `ItemTemplateSelector`, `VirtualizationStart`,
`VirtualizationCount` and `ItemExtent`) remain compiler-only and return stable
`XAML020` diagnostics when passed to this reader; they do not produce a
partial fallback tree.
Generated-only relation bindings (`Source`, `RelativeSource`, `ElementName`,
`TemplateBinding` and `MultiBinding`) likewise return `XAML008` or `XAML020`
from the cold reader; they never fall back to string traversal.

### Unsupported cold syntax and its DeltaXAML alternative

The cold reader reports unsupported syntax instead of returning a partially
constructed document. The stable alternatives are:

| Cold syntax | Diagnostic | Generated/library alternative |
| --- | --- | --- |
| `Resource`, `Style`, `Setter`, `Template`, `Trigger`, `Behavior`, `VisualState` and `ResourceDictionary` declarations | `XAML020` | Compile the declaration in the same generated artifact; use `UiResourceCatalog`, `UiStyle`, `UiTemplate` and typed plans from code when constructing documents directly. |
| `TemplateBinding` and `MultiBinding` | `XAML020` | Use generated typed relation/multi-binding plans. |
| `Source`, `RelativeSource` and `ElementName` binding forms | `XAML008` | Use generated cached relation-source plans and stable namescope identities. |
| `ItemsSource`, `ItemTemplate`, `ItemTemplateSelector` and virtualization properties | `XAML020` | Use `IUiItemsSource<TItem>`, `IUiItemTemplatePlan<TPlan,TItem>` and the generated virtualizing presenter. |

These diagnostics are part of the loader boundary, not a claim of source
compatibility with MAUI, WPF or Avalonia.

## Elements and controls

`UiElement` is the retained identity node. It owns parent/children relations,
participation and property access. Controls are deliberately thin, normally
sealed types over the same retained tree:

- `UiPanel` and layout containers;
- `UiBorder` and content presenters;
- `UiButton`, `UiToggleButton` and command controls;
- `TextBlock`, `TextBox` and numeric editors;
- `UiScrollViewer` and item presentation controls.

Their public inheritance is not the behavior-reuse mechanism. Reusable
behavior is composed internally from typed state and mixins, so adding a
custom control does not require choosing a deep framework base class.

## Properties

Typed access is the normal application path:

```csharp
textBlock.SetValue(TextBlockProperties.Text, "Launch");
string text = textBlock.GetValue(TextBlockProperties.Text);
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

Text controls expose a compact layout and editing surface. `TextBlock` and the
text side of `TextBox`/`UiNumericEditor` support `HorizontalTextAlignment`,
`VerticalTextAlignment`, `TextWrapping`, `TextTrimming`, `MaxLines`,
`LineHeight`, `FontWeight`, `FontStyle` and `TextDecorations`. The controls
also expose `TextBoxProperties.PlaceholderText`, `IsReadOnly`,
`AcceptsReturn` and `MaxLength`, plus caret and selection accessors. Empty
editors publish their placeholder through the same renderer-neutral text-run
path; programmatic `SetText` remains available for host-controlled updates.

These values are retained in typed state and applied while DeltaXAML measures
and requests shaping. The frozen `UiTextDraw` carries the resulting shaped
text and paint, but does not expose each layout/style property as a separate
field; a renderer must not try to reconstruct wrapping, trimming or font
selection from the draw record. DeltaXAML does not rasterize glyphs, and
renderer effect support remains outside this library.

Common elements expose renderer-neutral paint values through `BorderColor`,
`BorderWidth` and four-corner `CornerRadius` values ordered top-left, top-right,
bottom-right, bottom-left. A scalar XAML value is expanded uniformly. A radius
changes the visual primitive to `RoundedRectangle` (or `Border` when a stroke is
present); it does not implicitly clip child content. Text controls expose `OutlineColor`,
`OutlineWidth` and an optional `TextEffect` resource identity. These values are
carried into `UiVisualPaint`/`UiTextPaint`; shaping, effect shader selection and
GPU resource resolution remain outside DeltaXAML.

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
`UiElementProperties`, `TextBlockProperties`, `UiNumericEditorProperties`,
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
emits already shaped `UiTextDraw` values. Each ordered draw has a stable
`UiElementIdentity` in `UiDisplayList.Identities`, aligned with
`UiDisplayList.Order`; its `Value` and lifetime `Generation` form the retained
cache identity and its `Version` identifies the current producer payload.
DeltaRender may use the identity and version to skip upload when unchanged.
DeltaRender owns atlas packing, texture uploads and Vulkan execution.

## Display-list lifetime

`BuildDisplayList` returns the borrowed `Delta.XAML.Contract.UiDisplayList`.
Its backing storage remains owned by the document and is valid only until the
next document mutation or display-list build. Consumers must add its contents
to their render work before that invalidation point and must not retain spans.
Iterate `UiDisplayList.Order` for the canonical mixed visual/text sequence;
each `UiDrawRef` indexes either `Visuals` or `Text` according to its `Kind`.

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
`ToggleButton`, `TextBlock`, `TextBox`, `NumericEditor`, `Slider`, `Picker`,
`CollectionView`, `Overlay`, `TabView`, `Menu`, `Image` and `RichTextBlock`
with `Span` declarations. The following source features compile to typed
artifacts:

`UiButton` and `UiToggleButton` are single-content controls and do not expose a
separate `Text` property. A text label is an explicit `TextBlock` content
child, so it uses the same text properties, bindings and renderer-neutral text
path as every other text element.

- common size, background, padding, fill, enabled/selected, style and template
  properties;
- text/font/foreground and numeric editor properties;
- stack orientation and fixed/`Auto`/star grid definitions;
- `x:Name`, `x:DataType`, typed `ItemsSource`, data templates, template
  selectors and viewport-driven realization;
- `{Binding ...}` with `OneTime`/`OneWay`/`TwoWay`, typed converters, `Self`,
  template-owner, element-name and cached ancestor sources;
- typed multi-source functions and explicitly cultured `StringFormat`;
- `TemplateBinding`, generated attached properties and direct grid row/column
  slots;
- scalar/static/dynamic resources, resource-backed brushes, styles, visual
  states, control/data/multi triggers and templates;
- descriptor-bound behaviors, semantic commands, keyboard routing and tap,
  multiple-tap, long-press, drag/pan, swipe and pinch declarations;
- image resource identities and fallback state, rich style/action spans,
  automation metadata and explicit localization inputs;
- custom controls and direct custom properties declared with
  `UiXamlTypeAttribute` and `UiXamlPropertyAttribute`.

Input is attached in code through neutral `UiInputEvent` packets and ordinary
control events. Platform work is published as semantic commands and adapted by
the host. The compiler intentionally rejects code-behind event-handler members,
`x:Reference` (use `ElementName`), undeclared binding paths, foreign control
names and unsupported markup extensions. These produce stable diagnostics that
name a Delta alternative where one exists; there is no reflection or
alternate-runtime fallback. DeltaXAML supplies equivalent retained
capabilities, not general MAUI/WPF/Avalonia source compatibility.

## Full-capability dialect target

The capabilities in this section are implemented by the generated artifact and
single retained pipeline described above. The milestone remains complete only
while a
sample expressible through ordinary retained UI concepts can be authored
without host code rebuilding layout, hit testing, binding or rendering logic.
DeltaXAML does not promise source compatibility with another XAML dialect, but
it must provide an intentional Delta equivalent for these capability families.

### Typed collection presentation

Generated XAML supports a typed `ItemsSource`, `DataTemplate` and deterministic
template selection. The primitive collection presenter provides realization,
recycling and virtualizing layout only. Selection, scrolling, keyboard policy
and item chrome are composable policies used by higher-level list, picker and
collection controls; they are not mandatory per-item wrappers.

Small static collections and large virtualized collections use the same typed
template artifact. Generated item binding does not box `TItem`, use reflection
or create a closure for every realized item. Collection changes preserve
retained identity for unaffected keys and update only the affected realization
range.

### Compiled relative and multi-source bindings

Generated bindings support `Self`, template owner, named element and cached
ancestor sources. Template-owner and named-element links resolve during
construction. Ancestor links resolve on attach/reparent and cache a
generation-safe node identity; they do not traverse parents every frame.

Multiple inputs and binding functions compile into one typed expression plan.
Formatting has an explicit culture and updates only when its inputs change.
There is no production reflection fallback. A source that cannot be resolved
through supported retained relations is a build diagnostic.

### Attached properties

An owner may declare a typed attached property with a stable `UiPropertyId`.
Generated artifacts assign a compact slot, and the owning layout or behavior
capability reads that slot directly. This covers layout metadata such as grid
row/column placement without a global `Dictionary<object, object>` or public
storage exposure.

### Conditions, states and actions

Property, data and multi-condition declarations compile to dependency plans.
Only a condition whose dependency changed is reevaluated, and setters write to
the existing `Style/Trigger` source in the one property-precedence system.
Visual states are named condition/setter groups over the same mechanism.

Actions do not execute arbitrary delegates during style/layout traversal.
They enqueue semantic commands that the document publishes after the current
stage. Host/application code owns command implementation.

### Behaviors, commands and gestures

Reusable behaviors compile to optional typed state plus stateless capability
operations. They do not allocate a behavior object, event subscription or
delegate per element in steady state. Attach/detach follows node lifetime.

The input pipeline recognizes tap, multiple tap, long press, drag/pan, swipe
and pinch through one document-owned active-pointer arena. Pointer timestamps
come from the host input packet; DeltaXAML does not own a clock. Commands carry
semantic typed identity and source identity and remain independent of renderer
commands.

### Collection and value controls

The library supplies reusable controls built from the primitives above:

- slider/range input with capture, keyboard increments and two-way binding;
- picker/drop-down through overlay, collection presentation and selection;
- virtualized list/collection presentation with optional selection;
- image with intrinsic size, stretch, tint and placeholder/error state;
- popup/overlay primitives, focus scopes, tabs and menu composition.

High-policy controls compose lower-level capabilities. They do not add an
alternate tree, input router, property engine or submission path.

### Rich text

Formatted text contains typed style runs over one paragraph. Layout preserves
paragraph-level line breaking, bidi ordering, shaping and baselines across run
boundaries. Link runs produce retained inline hit ranges and semantic command
invocations. Extraction may emit several `UiTextDraw` values for one retained
owner; those values carry the owner's `UiElementIdentity` in the corresponding
`UiDisplayList.Identities` entries while remaining separately addressable
through `UiDisplayList.Order`.

### Brushes and images

Solid and resource-backed brushes are typed values. Linear/radial gradients
are represented by stable resources and emitted through the existing custom
visual/resource boundary. Image decoding, Vulkan images, samplers and uploads
remain consumer-owned. DeltaXAML may request renderer-neutral intrinsic image
metadata for layout but never receives a GPU object.

### Platform and application services

Clipboard, drag/drop payload acquisition, navigation, file dialogs, external
URI activation and asset loading are host services. DeltaXAML may expose a
narrow semantic request/result boundary for a control that needs them, but it
does not implement platform policy or depend on DeltaEngine.

### Accessibility and localization

The retained document can produce a borrowed semantic snapshot containing
roles, names, values, bounds, actions, focus and collection-position metadata.
The platform host adapts that snapshot to its accessibility API. Localization,
text direction and formatting culture are explicit document/source inputs;
process-global culture is never an implicit production dependency.

### Unsupported feature rule

Every unsupported element, member or markup expression produces one stable
diagnostic naming the unsupported capability and the Delta alternative when
one exists. Silently ignoring markup, falling back to reflection or requiring
sample-specific host reconstruction is forbidden.

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
