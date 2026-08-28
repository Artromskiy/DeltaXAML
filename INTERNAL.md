# DeltaXAML internal architecture

This document is the authoritative implementation architecture for DeltaXAML.
It is explicitly internal: it does not replace the consumer-facing
[LIBRARY_CONTRACT.md](LIBRARY_CONTRACT.md) or the frozen cross-project
[PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md).

The selected target is a retained UI library with compiled XAML, typed state,
static generic mixins, generated descriptors and a renderer-neutral borrowed
display list. Migration work converges on this architecture without creating a
second retained tree or extending compatibility APIs.

## Design objective

DeltaXAML must provide a practical, extensible XAML UI while keeping runtime
code close to a game-engine data pipeline:

- element identity is retained and stable;
- state is explicit, typed and locally owned;
- behavior is composed instead of inherited;
- XAML, bindings, property access and operation dispatch are compiled;
- the steady-state path has no reflection, boxing or string lookup;
- invalidation limits work to affected subtrees and stages;
- the host owns input acquisition, scheduling and time;
- DeltaText owns shaping and glyph generation;
- DeltaRender owns Vulkan resources, pipelines and submission;
- DeltaXAML only produces the canonical `UiDisplayList`.

The code should read in execution order. A reader must be able to see what
state enters a stage, what it mutates, what memory it borrows and which project
owns the result.

## Lessons retained from other XAML systems

DeltaXAML deliberately adopts these proven ideas:

- property metadata for defaults, validation, precedence and invalidation;
- compiled bindings with compile-time path diagnostics;
- XAML source generation instead of production runtime inflation;
- generated property/command mapping instead of per-control subscriptions;
- separate logical and visual trees where templates require them;
- reusable templates, resources, styles and selector plans.

It deliberately does not reproduce:

- an `object`-valued `BindableObject` property bag;
- `Dictionary<string, Action<...>>` dispatch in the frame path;
- platform handler/renderer layers: DeltaRender has one Vulkan backend;
- deep control inheritance;
- global mutable mapper customization;
- reflection-based production loading or binding;
- framework ownership of the window, loop, clock or application lifecycle.

## Governing rules

### DeltaXAML is a library

`UiDocument` owns retained UI state. It does not own a window, thread, update
loop, renderer, global clock or application lifetime. A game and the editor use
the same flow:

```csharp
document.Dispatch(input);
document.Layout(viewport, dpiScale);

UiDisplayList displayList = document.BuildDisplayList();
xamlRenderer.AddToGraph(displayList, graph, target);
```

The host decides where the UI pass is inserted. A game may add it after the
world pass for HUD rendering, before post-processing, or into an off-screen
target for world-space UI. No DeltaXAML API requires DeltaEditor.

### Identity, state, capability and algorithm are separate

The precise rule is:

> An element class owns identity and its composite state. A generic interface
> describes one capability. A stateless `readonly struct` implements its
> algorithm. Generated code binds the concrete element, state and algorithms.

This is stricter than putting default algorithms directly into interfaces.
Static generic calls let the JIT see concrete state and algorithm types, while
the generated descriptor provides one uniform runtime entry point for a
heterogeneous retained tree.

### No runtime state lookup by `Type`

Generic state is not retrieved through `Dictionary<Type, object>`. The source
generator resolves `UiButton -> ButtonState -> ButtonMeasureMixin` at compile
time and emits a direct typed thunk. The hot path performs at most one
descriptor operation call per element and stage; the algorithm then receives
`ref ButtonState` directly.

### One retained model

Loader, public controls, styles, bindings, layout, hit testing and extraction
operate on the same element identity and property sources. An adapter must not
rebuild a parallel object graph, duplicate property values or translate the
entire tree every frame.

## Runtime overview

```text
XAML source
    -> parser
    -> typed semantic model + Delta.Diagnostics source ranges
    -> generated factories, setters, bindings and descriptors
    -> one retained UiDocument

host input / property writes
    -> mutations
    -> bindings
    -> style and effective-value resolution
    -> measure
    -> arrange
    -> hit-test/focus state
    -> visual extraction
    -> borrowed UiDisplayList
    -> DeltaRender Vulkan RenderGraph
```

Every phase has one owner and explicit inputs. There is no general event bus or
monolithic `ProcessEverything` facade.

## Core runtime types

The following declarations describe implementation shape. Public signatures
remain governed by `LIBRARY_CONTRACT.md`.

```csharp
internal readonly record struct UiNodeId(
    uint Index,
    uint Generation);

internal readonly record struct UiRuntimeTypeIndex(int Value);

internal struct UiNodeRecord
{
    public UiElement Element;
    public UiNodeId Parent;
    public UiNodeId FirstLogicalChild;
    public UiNodeId NextLogicalSibling;
    public UiNodeId FirstVisualChild;
    public UiNodeId NextVisualSibling;
    public UiRuntimeTypeIndex RuntimeType;
    public UiDirtyFlags Dirty;
}
```

`UiNodeId` and runtime indices are compact process-local values. Durable type,
property, resource and semantic visual identities remain typed `Guid` wrappers
as required by the public contracts.

## Composite state

State structs contain fields and constants only. They contain no layout,
validation, subscription, allocation or rendering behavior.

```csharp
internal struct LayoutState
{
    public float2 RequestedSize;
    public float2 DesiredSize;
    public float4 Bounds;
    public float4 Margin;
    public float4 Padding;
}

internal struct VisualState
{
    public float4 BackgroundColor;
    public float4 ForegroundColor;
    public UiVisualTypeId VisualType;
}

internal struct InteractionState
{
    public UiInteractionFlags Flags;
    public uint CapturedPointer;
}

internal struct ButtonState
{
    public LayoutState Layout;
    public VisualState Visual;
    public InteractionState Interaction;
    public string Text;
    public UiCommand Command;
}
```

Each concrete control has one aggregate state type. Common state components
are embedded by value. This keeps access direct and makes the state required by
each capability visible without introducing an entity-component store inside
the UI library.

## Static generic mixins

Capability interfaces contain no mutable instance state. Algorithms live in
stateless `readonly struct` providers and are invoked through static generic
constraints.

```csharp
internal interface IMeasureMixin<TState>
    where TState : struct
{
    static abstract void Measure(
        ref TState state,
        in UiMeasureContext context);
}

internal interface IArrangeMixin<TState>
    where TState : struct
{
    static abstract void Arrange(
        ref TState state,
        in UiArrangeContext context);
}

internal interface IInputMixin<TState>
    where TState : struct
{
    static abstract bool ProcessInput(
        ref TState state,
        in UiInputPacket input);
}

internal interface IVisualMixin<TState>
    where TState : struct
{
    static abstract UiTextRun EmitVisual(
        ref TState state,
        in UiTextVisualContext context);
}

internal readonly struct ButtonMeasureMixin : IMeasureMixin<ButtonState>
{
    public static void Measure(
        ref ButtonState state,
        ref UiMeasureContext context)
    {
        float2 contentSize = context.MeasureText(state.Text);
        state.Layout.DesiredSize =
            contentSize +
            state.Layout.Padding.XY +
            state.Layout.Padding.ZW;
    }
}
```

The first migrated leaf uses the same shape in
`Internal/State/TextBlockState.cs`: `TextBlockState` embeds
`TextBlockLayoutState` and `TextBlockVisualState` and keeps text content as a
field. `TextBlockMixin.cs` supplies the measure, arrange, input and visual
capabilities; `UiTextBlockDescriptor.cs` is the direct typed companion used by
the retained `TextBlock` path. The input capability intentionally returns
`false` because a text display leaf does not consume input.

The first migrated container uses the same capability path in
`Internal/State/StackPanelState.cs`, `Internal/Mixins/StackPanelMixin.cs` and
`Internal/Descriptors/UiStackPanelDescriptor.cs`. Its state owns orientation
and layout results; the stateless mixins perform child measure/arrange through
the one retained child list, and the generated companion supplies the typed
dispatch. `StackPanel` no longer inherits the legacy `Panel` algorithm. The
remaining built-in controls use the same descriptor path in the subsequent
content, layout, input/editing and items/scroll slices below.

The content/layout slice adds `BorderState` and `ContentControlState` with
their stateless measure/arrange mixins and typed companions. `Border` owns no
second child model: the mixins receive the existing retained child view, apply
padding and write the composite layout result. `ContentControl` uses the same
single-child view and its migrated layout path is inherited by the button
compatibility shell. Button input is dispatched through its generated
companion while Click remains the existing event boundary.

The panel/layout slice adds `PanelState` and `GridState` with the same typed
dispatch. `Panel` keeps only its composite geometry in state while
`PanelLayoutMixin` measures the largest child and arranges all children into
the panel slot. `GridState` owns reusable definition and resolved-size arrays;
`GridMeasureMixin` and `GridArrangeMixin` implement fixed/auto/star sizing
without per-layout replacement arrays. `UiPanelGridDescriptors.cs` is the
typed factory, layout and property-setter companion for these controls.

The button/input slice adds `ButtonState` and `ToggleButtonState`. The
`ButtonInputMixin` and `ToggleButtonInputMixin` keep pointer transitions
stateless; generated companions expose factory/input dispatch, and the
retained button classes only synchronize state with the existing routed-event
and Click boundary.

The editing slice adds `TextBoxState` for caret/selection and
`NumericEditorState` for value/range/commit state. `TextBoxEditingMixin` owns
physical-key mapping, while `NumericValidationMixin` owns range parsing and
step validation. Clipboard, undo/redo, text events and diagnostics remain on
the existing retained owner boundary.

The items/scroll slice adds `ItemsControlState` and `ScrollViewerState`.
`ItemsControlMutationMixin` reuses unchanged realized rows through typed
source/factory interfaces and a retained scratch list. `ScrollViewerMixin`
owns content measure and offset arrange; scrolling is a state mutation that
invalidates arrange/visual output only when the offset changes.

A mixin owns one coherent capability. It must not discover other capabilities
through service lookup. If two algorithms share data, that data belongs to an
explicit state component or a typed stage context.

## Control classes

Control classes are flat composition shells:

```csharp
public sealed class UiButton : UiElement
{
    private ButtonState _state;

    internal ref ButtonState State => ref _state;

    public string Text
    {
        get => _state.Text;
        set => SetValue(UiButtonProperties.Text, value);
    }

    public UiCommand Command
    {
        get => _state.Command;
        set => SetValue(UiButtonProperties.Command, value);
    }
}
```

A control may declare:

- state fields;
- constructors that establish valid initial state;
- property and event accessors;
- the internal `ref` state accessor required by generated code.

It may not declare layout, input, visual, binding or validation algorithms;
private domain helpers; per-instance behavior delegates; subscriptions; or
temporary allocation logic.

`UiElement` is the single identity base. Specialized control inheritance is
not an algorithm-composition mechanism. A reusable capability is a state
component plus mixin, not another base class.

## Generated descriptors

The source generator emits a companion type rather than requiring user
controls to be `partial`.

```csharp
internal static unsafe class UiButtonGenerated
{
    internal static readonly UiTypeDescriptor Descriptor =
        new(
            UiButtonType.Id,
            new UiTypeOperations(
                &Measure,
                &ProcessInput,
                &EmitVisual),
            UiButtonProperties.All);

    internal static UiElement Create() => new UiButton();

    private static void Measure(
        UiElement element,
        ref UiMeasureContext context)
    {
        UiButton button = (UiButton)element;
        MixinRunner.Measure<ButtonState, ButtonMeasureMixin>(
            ref button.State,
            ref context);
    }
}
```

`UiTypeDescriptor` is immutable after registration and contains:

- stable `UiTypeId`;
- factory entry point;
- operation thunks for supported capabilities;
- property descriptors and typed setter thunks;
- content/children metadata;
- template and selector metadata required by the runtime.

Descriptors use compact indices or generated function tables. They do not use
string dictionaries in the frame path. Human-readable names and `Type` values
are cold metadata for tooling and diagnostics only.

The current exemplar registers its immutable metadata in a compact generated
descriptor catalog indexed from one. Catalog lookup validates the index and
returns only metadata; it does not create a second tree or retain per-instance
delegates. The resolved `TextBlock` operations remain the companion's direct
typed thunks and receive `ref TextBlockState`.

## Property system

The public property system keeps typed and untyped views because the XAML
loader and editor need discovery, while compiled runtime access remains typed.

```csharp
public interface IUiProperty
{
    UiPropertyId Id { get; }
    string Name { get; }
    Type ValueType { get; }
}

public interface IUiProperty<T> : IUiProperty
{
    T DefaultValue { get; }
    UiInvalidation Invalidation { get; }
}
```

The canonical migration path still has one retained `UiPropertyStore`, but its
source slots are a cold mutation boundary. A source write resolves a compact
`UiPropertyKey` once and invokes the owning element's generated typed setter;
the setter writes directly into that element's composite state. The store does
not retain per-property apply delegates. The untyped path may box for loader,
editor inspection and runtime diagnostics because it is explicitly cold.

Effective-value precedence remains:

```text
Default < Style/Trigger < Binding < Local < Handle < Animation
```

The explicit resolver table is the only precedence source of truth. It includes
the internal animation layer above host handles, while public callers continue
to use the documented local/style/binding/handle sources. Resolving an
effective value writes the typed result into the concrete control state, so
layout, input and visual mixins do not query the property engine. Equal typed
values update source metadata without repeating invalidation or setter work;
the source slot remains reversible for a later `Clear`.

Property metadata declares exact invalidation:

```csharp
[Flags]
internal enum UiInvalidation : byte
{
    None = 0,
    Measure = 1 << 0,
    Arrange = 1 << 1,
    Visual = 1 << 2,
    HitTest = 1 << 3,
}
```

Validation and coercion run when a source value changes, not during layout or
visual extraction.

## Compiled XAML

Production XAML uses one compile-time pipeline:

```text
XML tokens
  -> namespace/type/property resolution
  -> typed semantic tree
  -> resources/styles/templates/selectors
  -> typed binding plans
  -> generated factories and setters
  -> source map and Delta.Diagnostics
```

The generated artifact contains:

- direct element factories;
- direct typed property setters;
- content and child attachment operations;
- compiled resource identities;
- compiled binding accessors;
- template factories and name-scope tables;
- selector/state-machine plans;
- source locations for every generated diagnostic.

Production loading performs no assembly scan, `Activator.CreateInstance`,
property-name reflection or string binding traversal. A separate runtime
inflation path may exist for designer/hot-reload tooling, but it must produce
the same semantic artifact and must not become a fallback silently used in a
shipping build.

## Bindings

Compiled bindings use generated typed read/write accessors. A simple binding
does not allocate a closure and does not resolve a dotted string path per
update. `OneTime` creates no subscription. `OneWay` and `TwoWay` register only
the source notifications required by their generated plan.

`INotifyPropertyChanged` remains a compatibility source mechanism. Engine and
editor hot paths should prefer generation-safe `UiPropertyHandle` writes and
batched mutations. Binding notifications are collected, deduplicated and
applied before layout; a callback must not recursively run layout.

## Trees, templates and storage

One `UiNodeId` identifies an element across the logical and visual trees.
Templates may create a different visual subtree while preserving the logical
owner. Parent/child relations are stored in compact tables rather than a
separate `List<T>` allocation per runtime operation.

Tree mutation validates:

- generation and document ownership;
- duplicate parentage;
- cycles;
- logical/visual lifetime consistency;
- name-scope ownership.

Templates are generated factories. Applying or replacing a template updates
only the affected visual subtree and corresponding selector/layout plans.

## Styles, resources and visual states

Styles compile into selector plans. Selector matching does not walk arbitrary
reflection metadata. Static and dynamic resource references resolve to stable
`Guid`-backed identities; runtime slots remain compact integers.

Interaction and user state are ordinary typed flags. Visual-state changes feed
the same effective-value resolver as styles and therefore reuse property
precedence and invalidation. They are not an event-driven second property
system.

## Input, focus and commands

The host supplies normalized `Delta.XAML.Contract.UiInputEvent` packets.
DeltaXAML owns hit testing, capture, focus, routing and control semantics.

The input stage performs:

```text
packet normalization already done by host
  -> hit-test candidate lookup
  -> capture/focus resolution
  -> route construction
  -> typed input mixin calls
  -> queued property/tree mutations
```

Commands are semantic actions, not renderer commands. Property changes and
commands remain separate in descriptors: a property represents retained
state, while a command requests an operation such as scroll, submit or focus.

## Layout

Measure and arrange are separate explicit stages. Dirty flags propagate only
as far as metadata requires. The steady-state stage binds a descriptor and
state once per element; inner maths operates on concrete structs and
`Delta.Maths` values.

Layout must not:

- inspect CLR property names;
- allocate temporary child collections;
- invoke user bindings;
- shape text repeatedly when the shaping key is unchanged;
- rebuild unaffected subtrees.

## Visual extraction and text

Visual mixins emit renderer-neutral commands into reused builders. Extraction
does not create Vulkan resources or renderer pipelines. Custom visuals use the
stable `UiVisualTypeId`; DeltaRender resolves that identity to its registered
implementation.

Text layout calls `DeltaText.Contract.ITextService`. DeltaText returns shaped
positions and glyph images; DeltaXAML places shaped text and emits
`UiTextDraw`; DeltaRender owns atlas packing, UV assignment, staging, shader
selection and GPU lifetime.

`UiDisplayList` is a borrowed view over document-owned storage and is invalid
after the next mutation or extraction, as defined by the frozen contract.

## Pipeline implementation

`UiDocument` is the small public ownership boundary. It delegates to focused
internal stages:

```csharp
internal sealed class UiPipeline
{
    private readonly UiMutationEngine _mutations;
    private readonly UiBindingEngine _bindings;
    private readonly UiStyleEngine _styles;
    private readonly UiLayoutEngine _layout;
    private readonly UiInputEngine _input;
    private readonly UiVisualEngine _visuals;

    public void Build(
        UiDocumentState state,
        float2 viewport,
        float dpiScale,
        ref UiDisplayListBuilder output)
    {
        _mutations.ApplyPending(state);
        _bindings.ApplyChanged(state);
        _styles.ResolveChanged(state);
        _layout.MeasureAndArrange(state, viewport, dpiScale);
        _visuals.Emit(state, ref output);
    }
}
```

The pipeline exposes order, not a general framework. Engines have narrow data
dependencies and do not call one another through a service locator.

## Cost zones

### Hot

Per node, child, glyph and visual-command operations use arrays/spans, compact
indices, typed state, `ref` access and generated thunks. No LINQ, reflection,
boxing, `Type` lookup, string lookup, closure allocation or unbounded delegate
chain is allowed.

### Warm

Per document build, dirty subtree, template application or mutation batch may
use cached plans and bounded dictionaries. Buffers are reused and capacity
growth happens before traversal when possible.

### Cold

Compilation, source diagnostics, editor inspection and optional runtime
designer inflation may use richer object graphs, reflection and boxing when it
makes the boundary clearer. Cold representations never cross into layout,
input or visual extraction.

## Required source layout

```text
src/DeltaXAML/
  UserApi/
    Controls/Ui*.cs            flat identity/state access shells
    Properties/                typed public property API
    Bindings/                  typed user binding API
  Internal/
    State/*State.cs            data only
    Mixins/*Mixin.cs           stateless algorithms
    Descriptors/               generated/runtime operation metadata
    Compilation/               semantic artifact consumption
    Properties/                precedence and typed value pools
    Bindings/                  generated plans and notification collection
    Tree/                      logical/visual retained tables
    Input/                     hit test, focus, capture and routing
    Layout/                    measure and arrange stages
    Visuals/                   display-list builders and extraction
```

Generated companions may live under `obj/Generated/DeltaXAML` or an explicit
generated source project. User-authored controls are never required to be
`partial` merely to receive generated runtime behavior.

## UML

```mermaid
classDiagram
    direction LR

    class UiDocument {
        +UiElement Root
        +Dispatch(UiInputEvent)
        +Layout(float2, float)
        +BuildDisplayList() UiDisplayList
    }

    class UiElement {
        +UiNodeId Id
        +UiParticipation Participation
        +UiElement Parent
        +Children
        +GetValue~T~(IUiProperty~T~) T
        +SetValue~T~(IUiProperty~T~, T)
    }

    class UiButton {
        -ButtonState state
        +string Text
        +UiCommand Command
        ~State ButtonState&
    }

    class ButtonState {
        +LayoutState Layout
        +VisualState Visual
        +InteractionState Interaction
    }

    class IMeasureMixin~TState~ {
        <<static capability>>
        +Measure(TState&, UiMeasureContext&)
    }

    class ButtonMeasureMixin {
        <<readonly struct>>
        +Measure(ButtonState&, UiMeasureContext&)
    }

    class UiTypeDescriptor {
        +UiTypeId Id
        +UiTypeOperations Operations
        +Properties
    }

    class UiButtonGenerated {
        <<generated>>
        +Descriptor
        +Create()
        +MeasureThunk()
        +InputThunk()
        +VisualThunk()
    }

    class CompiledXamlArtifact {
        <<generated>>
        +CreateDocument(UiServices) UiDocument
    }

    class UiPipeline
    class UiDisplayList {
        <<borrowed view>>
    }

    UiDocument *-- UiPipeline
    UiDocument *-- UiElement
    UiButton --|> UiElement
    UiButton *-- ButtonState
    ButtonMeasureMixin ..|> IMeasureMixin~ButtonState~
    UiButtonGenerated --> ButtonMeasureMixin
    UiButtonGenerated --> UiTypeDescriptor
    UiButton --> UiButtonGenerated
    CompiledXamlArtifact --> UiDocument
    UiPipeline --> UiTypeDescriptor
    UiPipeline --> UiDisplayList
```

## Architecture gate

The executable DeltaXAML architecture gate must reject:

- control classes with non-accessor domain methods;
- control inheritance used to share algorithms;
- state structs containing methods, reference-owned services or allocations;
- mixin structs containing instance fields;
- default interface algorithms that become runtime dispatch;
- `Dictionary<Type, object>` or object-valued storage in runtime stages;
- reflection, LINQ and string property lookup in layout/input/visual paths;
- per-instance behavior delegates or event subscriptions inside controls;
- generated companions that require user controls to be `partial`;
- new implementation references to compatibility APIs.

The gate applies to new and migrated types. Existing migration files are
tracked explicitly and may not receive new behavior.

## Current migration classification

The designated folders are populated by the migrated state, mixin and
descriptor types. The executable gate still reports any missing or empty
designated folder as `PENDING` and fails the headless command; an empty folder
is not an accepted architecture pass.

| Path | Classification | Boundary rule |
|---|---|---|
| `Internal/UiElement.cs` | migration compatibility implementation | Existing retained owner/traversal shell plus the single cold source resolver; typed effective values now enter composite state through `ApplyTypedProperty`; remove the virtual compatibility shell during the retained-stage split. |
| `Internal/Compatibility/UiBindingRuntime.cs` | migration-only cold compatibility implementation | Public `UiBindingExpression` bridge used by the explicit tooling/compatibility loader; generated typed binding artifacts do not call it. Remove after the loader has a generated construction mode. |
| `UserApi/LibraryFacade.cs` (`XamlLoader`) | migration-only cold compatibility implementation | Public XML inflation entry point; generated companions bypass it and use direct construction. Keep it bounded to the explicit loader contract until a generated loader mode replaces it. |
| `Internal/RetainedContracts.cs` | retained runtime vocabulary | Concrete records and the typed item source/factory and clipboard adapter boundaries still used by the retained compatibility shell; do not add new runtime algorithms here. |
| `UserApi/LibraryFacade.cs` | active public compatibility facade | Existing public callers remain supported; new controls and generated descriptors must live in the designated locations, not in this file. |
| `Internal/State/TextBlockState.cs` | migrated exemplar state | Composite text/layout state only; no algorithms, services or ownership. |
| `Internal/Mixins/TextBlockMixin.cs` | migrated exemplar capabilities | Static generic measure/arrange/input/visual capability shapes and stateless readonly algorithms. |
| `Internal/Descriptors/UiTextBlockDescriptor.cs` | migrated exemplar descriptor | Compact type index plus typed factory/measure/arrange/input/visual thunks and property setters for `TextBlock`; the catalog now covers all built-in control groups. |
| `Internal/State/StackPanelState.cs` | migrated layout state | Composite orientation/layout state only; child relations remain owned by the single retained element tree. |
| `Internal/Mixins/StackPanelMixin.cs` | migrated layout capabilities | Stateless child measure/arrange algorithms using the generic capability interfaces and an explicit child view. |
| `Internal/Descriptors/UiStackPanelDescriptor.cs` | migrated layout descriptor | Compact typed factory/measure/arrange/property dispatch for `StackPanel`; it does not create another tree or store. |
| `Internal/State/BorderState.cs` | migrated content state | Padding and geometry fields only; the retained child relation stays on `UiElement`. |
| `Internal/State/ContentControlState.cs` | migrated content state | Single-child geometry fields only; content remains the retained tree's first child. |
| `Internal/Mixins/ContentLayoutMixin.cs` | migrated content capabilities | Stateless padding/content measure and arrange algorithms over the existing child view. |
| `Internal/Descriptors/UiContentLayoutDescriptors.cs` | migrated content descriptors | Compact typed factories and layout dispatch for `Border` and `ContentControl`. |
| `Internal/State/PanelState.cs` | migrated container state | Panel geometry only; the retained child relation remains on `UiElement`. |
| `Internal/State/GridState.cs` | migrated grid state | Grid definitions and reusable measure/arrange buffers; no child ownership. |
| `Internal/Mixins/PanelLayoutMixin.cs` | migrated container capabilities | Stateless panel and fixed/auto/star grid measure/arrange algorithms. |
| `Internal/Descriptors/UiPanelGridDescriptors.cs` | migrated container descriptors | Compact typed factories, layout dispatch and grid definition setters. |
| `Internal/State/ButtonState.cs` | migrated input state | Pressed state only; event ownership remains on the existing retained button. |
| `Internal/State/ToggleButtonState.cs` | migrated input state | Checked state only; it composes with the button state through the existing class hierarchy. |
| `Internal/Mixins/ButtonInputMixin.cs` | migrated input capabilities | Stateless routed pointer transitions for Button and ToggleButton. |
| `Internal/Descriptors/UiButtonDescriptors.cs` | migrated input descriptors | Compact typed factories and routed-input dispatch for Button and ToggleButton. |
| `Internal/State/TextBoxState.cs` | migrated editing state | Caret and selection fields only; text storage remains the existing TextBlock state. |
| `Internal/State/NumericEditorState.cs` | migrated numeric state | Numeric value, bounds and committed text; validation has no external service dependency. |
| `Internal/Mixins/TextBoxEditingMixin.cs` | migrated editing capabilities | Stateless physical-key mapping, numeric parsing and numeric adjustment algorithms. |
| `Internal/Descriptors/UiEditingDescriptors.cs` | migrated editing descriptors | Compact typed factories, editing input dispatch and numeric validation thunks. |
| `Internal/State/ItemsControlState.cs` | migrated items state | Item count only; source and realized row collections remain on the retained owner. |
| `Internal/State/ScrollViewerState.cs` | migrated scrolling state | Offset and viewport/content geometry only. |
| `Internal/Mixins/ItemsControlMixin.cs` | migrated items capabilities | Stateless incremental row reuse algorithm over typed source/factory boundaries. |
| `Internal/Mixins/ScrollViewerMixin.cs` | migrated scrolling capabilities | Stateless content measure and offset arrange algorithms. |
| `Internal/Descriptors/UiItemsScrollDescriptors.cs` | migrated items/scroll descriptors | Compact typed factories, scroll layout/offset dispatch and items mutation dispatch. |
| `Internal/State/UiElementState.cs` | migrated common property state | Common width, height, visual, enabled/selected and padding fields only; no behavior or ownership. |
| `Internal/Descriptors/UiElementPropertyDescriptors.cs` | migrated common property descriptor | Stateless typed setters for common element state; source resolution remains outside the frame stages. |
| `UserApi/Controls/UiTextBlock.cs` | migrated exemplar user control | Thin public composition/accessor surface over the existing retained `TextBlock`; no second tree or property store. |
| `Internal/State`, `Internal/Mixins`, `Internal/Descriptors`, `UserApi/Controls` | migration targets | New state, capability, descriptor and control code is accepted only here and is checked by the architecture gate. |

The current retained files are not scanned as migrated controls by the gate.
This prevents a legacy implementation from making the new architecture appear
complete while keeping one retained tree/property model during migration.

## Migration and compatibility policy

Migration is complete only when the new path is the sole active path. If an
old API, benchmark, test or implementation cannot be removed in the same
change, it must be marked `[Obsolete]` with:

- the replacement API or removal destination;
- an error severity when no supported caller may remain;
- a TODO reference or removal milestone when temporary compilation is needed.

```csharp
[Obsolete(
    "Use UiTypeDescriptor generated operations; remove during DXAML-MIXIN-4.",
    error: true)]
internal void LegacyMeasure()
{
}
```

An unmarked compatibility surface is considered an accidentally supported API.
New code, tests and benchmarks must not call an obsolete path. Compatibility
adapters must not be optimized or extended; they exist only to keep a bounded
migration compiling until their removal milestone.

## Current migration blockers

The current retained implementation remains a migration surface. In
particular:

- the common `UiElement` owner and traversal shell still contains the
  compatibility operations that the final node-store stage must retire;
- the public XML loader and `Internal/Compatibility/UiBindingRuntime.cs` are
  intentionally cold compatibility paths; generated construction and typed
  binding artifacts bypass both;
- the canonical display-list producer is now `UiVisualStage` writing the
  document-owned `UiDisplayListStorage`; the returned view is borrowed, while
  final editor/game consumer integration remains outside this repository;
- style/resource application still uses the public cold store and string keys;
  the compiler plan has stable resource slots, but generated typed style plans
  are not yet the sole runtime path;
- generated templates now use the stateless `IUiTemplateFactory` boundary;
- source slots and loader/editor discovery still use the retained compatibility
  store, while effective values reach composite state through typed property
  descriptors.

These are migration tasks, not alternate architectures.

## Remaining implementation specification

This section is the execution plan after the completed `DXAML-MIXIN-1` through
`DXAML-MIXIN-5` baseline. It defines how the unchecked items in `TODO.md` are
implemented; it does not change `LIBRARY_CONTRACT.md`, `PUBLIC_CONTRACT.md` or
the types in `src/DeltaXAML.Contract`. If this section and a public contract
disagree, the public contract wins and implementation stops for a contract
decision instead of inventing an adapter.

The final implementation has one production path:

```text
XAML source
  -> typed compile-time artifact
  -> one retained UiDocumentState
  -> fixed stages over generation-safe nodes
  -> canonical borrowed UiDisplayList
```

There must not be a second object tree, a translated legacy draw list, a
reflection fallback selected in shipping code or a compatibility facade around
the old runtime.

### Delivery order

Implement these slices in order. A later slice may introduce declarations
needed by the current slice, but it must not keep two active implementations.

| Slice | TODO owner | Result |
|---|---|---|
| `DXAML-COMPILE-1` | compilation | One typed semantic model with exact source ranges and stable identities. |
| `DXAML-COMPILE-2` | generation | Direct factories, setters, attachment, namescopes and descriptor registration. |
| `DXAML-COMPILE-3` | binding | Generated typed read/write batches without steady-state path traversal or closures. |
| `DXAML-COMPILE-4` | styling | Compiled resources, selectors, styles, states and template factories. |
| `DXAML-RUNTIME-1` | retained storage | One generation-safe node store for logical and visual relations. |
| `DXAML-RUNTIME-2` | stages | Fixed input, mutation, binding, style, measure, arrange and visual stages. |
| `DXAML-RUNTIME-3` | extraction | Direct canonical `UiDisplayList` output and stable text caching. |
| `DXAML-RUNTIME-4` | removal/integration | Remove the obsolete retained runtime and finish editor/game input and resize paths. |

### Compile-time projects and ownership

Keep the runtime library small. Compile-time implementation belongs in two
supplementary projects only when `DXAML-COMPILE-1/2` begins:

```text
src/DeltaXAML.Compiler/
  Internal/Compilation/       XML reading, semantic model and diagnostics
  Internal/Plans/             immutable typed plans

src/DeltaXAML.Generator/
  IncrementalGenerator.cs     Roslyn AdditionalFiles adapter only
  Emission/                   C# source emission from typed plans
```

`DeltaXAML.Compiler` owns the source-to-plan pipeline and may be reused by an
explicit designer/hot-reload tool. `DeltaXAML.Generator` only supplies Roslyn
incremental inputs and emits C#; it must not contain a second parser or semantic
model. Neither project is a cross-project runtime contract. Runtime
`DeltaXAML` consumes generated C# and does not reference Roslyn.

Compilation is cold and may use immutable object graphs. Generated runtime
artifacts contain compact IDs, literal values and direct typed operations;
they do not retain XML nodes, `Type`, reflection metadata or property paths.

### `DXAML-COMPILE-1`: typed semantic model

The compiler pipeline is explicit and deterministic:

```text
XML token + source range
  -> namespace/name resolution
  -> UiTypeId / UiPropertyId resolution
  -> content-model validation
  -> typed literal/resource/binding value plan
  -> namescope/template/style validation
  -> immutable XamlDocumentPlan
```

The first implementation lives in the cold-only `DeltaXAML.Compiler` project.
`XamlSemanticRegistry` accepts stable, caller-assigned `UiTypeId`,
`UiPropertyId` and `UiResourceId` values; it never creates durable identities
from process-random values. `XamlCompiler` emits one immutable plan model with
typed literal/resource/binding values, exact UTF-16 `SourceRange` offsets and
diagnostics for unknown names, invalid values, duplicate names and unsupported
content. Its hand-written XML reader recovers at the next attribute or sibling
element so one compile can report multiple local errors. The compiler project
does not participate in runtime layout, input or visual extraction, and the
runtime `DeltaXAML` project does not reference it.

`XamlObjectPlan.ScopeName` carries an `x:Name` declaration for the generated
namescope. Built-in `XamlTypeDefinition` entries also carry explicit direct
factory expressions. The compiler indexes those definitions by stable
`UiTypeId`, so generation does not need to rediscover a CLR type.

Use one representation with these responsibilities (names may change only if
the responsibility remains one-to-one):

```csharp
internal sealed record XamlDocumentPlan(
    SourceId Source,
    XamlObjectPlan? Root,
    ImmutableArray<XamlResourcePlan> Resources,
    ImmutableArray<XamlStylePlan> Styles,
    ImmutableArray<XamlTemplatePlan> Templates,
    ImmutableArray<Diagnostic> Diagnostics);

internal sealed record XamlObjectPlan(
    UiTypeId Type,
    XamlQualifiedName Name,
    string? ScopeName,
    SourceRange Range,
    ImmutableArray<XamlMemberPlan> Members,
    ImmutableArray<XamlObjectPlan> Children);

internal readonly record struct XamlMemberPlan(
    UiPropertyId Property,
    XamlValuePlan Value,
    SourceRange Range);
```

Every syntax node that can fail resolution carries a `SourceRange`. Unknown
names, invalid content, duplicate names, incompatible values and unsupported
markup extensions produce `Delta.Diagnostics` entries and recovery continues
at the next attribute or sibling element so one build reports multiple errors.

Stable IDs come from registered metadata or explicit generated declarations.
Never derive a durable GUID from process-random hashes. Source order determines
generated local indices, and identical input plus identical registries must
emit byte-for-byte identical generated C#.

### `DXAML-COMPILE-2`: generated document construction

Generate a companion factory; do not modify a user control or require it to be
`partial`. The generated method constructs the final retained document
directly:

```text
create typed element
  -> register/resolve immutable descriptor
  -> apply typed literal and resource setters
  -> attach logical child/content
  -> record namescope slot
  -> instantiate template plan when required
  -> return UiDocument
```

`DeltaXAML.Generator` is the build-time companion. Its Roslyn incremental
entry point reads `.xaml` additional texts, invokes the one compiler plan, and
adds a deterministic `Xaml_<file>_<stable-hash>` artifact. The artifact owns
one final `UiDocument`, invokes direct public typed setters, and emits one
compact `_scopeElements` table plus a generated `TryFindName` switch. A
literal-only built-in document is therefore constructed without an XML reader
or runtime factory lookup. Custom registry entries must provide an explicit
factory expression; otherwise generation reports `DXAMLGEN001`.

The compiler registry validates and indexes the immutable descriptor metadata
once per compilation. Existing built-in runtime descriptor companions remain
the only runtime operation catalog; this slice does not invent a second
descriptor/property store. Generated code calls direct typed companion
operations and must not call
`Activator.CreateInstance`, set properties by name, use `dynamic`, enumerate
assemblies or create a dictionary per element. A namescope uses one generated
compact table per scope; source names are retained only because name lookup is
a user feature, not as runtime identities.

For this slice, descriptor registration is the compiler-side
`XamlSemanticRegistry` validation and stable-ID index. Built-in runtime
operation descriptors remain registered by the existing static catalog; a
generated custom runtime descriptor table is not emitted until the later
runtime descriptor slice.

`IXamlLoader.Load(string, ...)` remains an explicit cold/tooling path. Shipping
generated construction never silently calls it. If the build cannot generate
an artifact, it reports a build diagnostic instead of producing reflection
fallback code.

The current generator accepts literal values, the existing typed child/content
operations, and binding plans that have an explicit
`XamlBindingDefinition` in the registry. Binding definitions supply source and
value type names plus direct accessor expressions; the artifact emits static
typed lambdas and one direct refresh/dispose batch. `OneTime` has no source
subscription. Resource references and bindings without a typed definition
produce `DXAMLGEN002` rather than a string-path or reflection fallback.

### `DXAML-COMPILE-3`: compiled binding batches

Each binding is resolved against a declared source type during compilation.
The artifact generates direct member reads and, for `TwoWay`, direct writes.
The generated document groups its concrete bindings into a typed batch so one
batch dispatch may contain many property updates; there is no heterogeneous
`object`/delegate call per property in the steady state.

```text
OneTime  : read once during construction; register no notification
OneWay   : source notification -> deduplicated binding slot -> typed read
TwoWay   : OneWay path + typed target commit -> source write
```

The current implementation represents the declared source contract as an
`XamlBindingDefinition` in `XamlSemanticRegistry`. `CSharpArtifactEmitter`
requires all bindings in one artifact to use that source type, emits static
typed lambdas, attaches them through the source-managed `SetCompiledBinding`
overload, and exposes typed `RefreshBindings`/`Dispose` calls over the binding
fields. The artifact installs one source notification boundary; it only queues
the affected target runtimes, and the next binding stage reads them. Generated
bindings do not subscribe per property. `OneTime` still registers no
notification. The legacy string-path `UiBindingRuntime` remains
tooling/compatibility-only until the runtime stage migration.

Binding attachment may allocate notification infrastructure once. A changed
notification only records a compact binding slot. The binding stage later
reads all queued slots and writes through the normal typed property/source
resolver; callbacks never run layout recursively.

Production binding rules:

- no dotted string traversal after compilation;
- no reflection, `dynamic`, boxing or captured lambda in an update;
- nullable segments are validated and generate explicit propagation behavior;
- source conversion and validation are generated typed calls;
- `INotifyPropertyChanged` is a compatibility notification source, not the
  binding execution engine;
- an unsupported source shape is a compile diagnostic, not a runtime fallback.

### `DXAML-COMPILE-4`: resources, styles and templates

Compile names to stable GUID-backed identities and then to artifact-local
indices. Runtime arrays are indexed by those local values; dictionaries are
per artifact or scope and are not consulted per node during layout or visual
extraction.

Styles compile into:

```text
selector predicate over compact type/state/class indices
  -> ordered typed property writes
  -> exact invalidation metadata
```

Static resources resolve during artifact construction. Dynamic resources keep
a compact dependency list from resource slot to affected property slots; a
resource change queues only those writes. Visual states use the same property
precedence resolver as styles and do not form a second property engine.

Templates are generated factories. A template creates visual nodes in the
same `UiDocumentState`, assigns their visual parent and templated owner, and
does not create another `UiDocument`. Replacing a template destroys only that
visual subtree and invalidates the affected layout/visual queues.

### `DXAML-RUNTIME-1`: one node store

Replace recursive identity lookup and per-object relation ownership with one
document-owned store. Public `UiElement` instances remain stable identity
shells; internal relations and generations live in indexed records:

```csharp
internal readonly record struct UiNodeId(uint Index, uint Generation);

internal struct UiNodeRecord
{
    internal UiElement Element;
    internal UiRuntimeTypeIndex RuntimeType;
    internal UiNodeId LogicalParent;
    internal UiNodeId VisualParent;
    internal UiNodeId FirstLogicalChild;
    internal UiNodeId FirstVisualChild;
    internal UiNodeId NextLogicalSibling;
    internal UiNodeId NextVisualSibling;
    internal UiDirtyMask Dirty;
}
```

The exact link encoding may use first-child/next-sibling or compact ranges, but
there is one authoritative record per element. `UiNodeId.Index` resolves in
O(1) and generation validation rejects stale handles. Do not add a parallel
`Dictionary<UiElement, ...>` or translate between separate logical and visual
documents. The node store naturally replaces `UiFrame.Find`; no separate
mutation index is justified.

All tree mutation is queued. Applying a mutation validates ownership, stale
generation, duplicate parentage, cycles and logical/visual lifetime before
changing links. Destroying a logical owner also destroys its template-owned
visual subtree and increments released generations.

### `DXAML-RUNTIME-2`: fixed stage pipeline

`UiDocument` owns data and sequences stages; it is not a service locator.
Stages are stateless algorithms over `UiDocumentState`. They do not invoke one
another and do not expose interfaces without a second implementation.

```text
Dispatch()           queues normalized input only

Layout():
  1. input/focus     route queued input against last committed geometry
  2. mutation        apply input and host tree/property mutations
  3. binding         apply deduplicated changed binding slots
  4. style/resource  resolve queued selectors, states and resources
  5. measure         post-order dirty subtrees
  6. arrange         pre-order dirty subtrees and refresh hit geometry
  7. focus repair    release detached/disabled focus and capture

BuildDisplayList():
  8. visual/text     update dirty output ranges and return borrowed spans
```

The first document layout establishes hit-test geometry before input is
accepted. A property write during input enters the mutation stage of the same
`Layout` call. A stage may enqueue work for a later stage, never call it
directly. An illegal earlier-stage invalidation discovered after its barrier
is retained for the next `Layout`; it is not solved through recursion.

Each stage has a reusable dense `UiNodeId[]` queue and a generation/stamp array
for O(1) deduplication. Do not use per-frame `HashSet`, LINQ, recursive list
construction or one heap allocation per node.

### Invalidation and traversal rules

Generated property metadata is the only source of invalidation flags. Compare
the new typed effective value before enqueueing work.

| Flag | Work queued |
|---|---|
| `Binding` | Binding slot only; its resulting typed write adds later flags. |
| `Style` / `Resource` | Affected selector/resource dependency slots only. |
| `Measure` | Node and logical ancestors until an already-dirty boundary. |
| `Arrange` | Node; arranging a parent visits only children whose geometry can change. |
| `HitTest` | Node geometry/capture state after arrange. |
| `Visual` | Node output range and dependent clip/text records. |
| `Tree` | Changed relation, affected ancestors and removed subtree lifetime. |

Measure is post-order; arrange and visual extraction are pre-order in stable
child/Z order. Hidden subtrees obey `UiParticipation` and are skipped without
destroying their retained state. Layout never invokes a binding, resolves a
resource by name or shapes text.

### `DXAML-RUNTIME-3`: canonical extraction and text cache

The visual stage writes `UiVisualCommand`, `UiClip` and `UiTextDraw` directly
into reusable document-owned arrays. `UiDocument.TryBuildDisplayList` returns
spans over those arrays. It does not first build `IUiDrawList`, compare a full
duplicate previous list and translate every item into the contract types.

Only dirty node ranges are rewritten; a structural or ordering change may
compact the affected suffix. The returned view is invalid after the next
mutation or extraction exactly as stated by `Delta.XAML.Contract`.

Text shaping is cached by all inputs that can change shaping: retained node
generation, text version, resolved font instance/fallback set, size, direction,
script/language/features and layout constraint. Position/color/clip changes do
not reshape glyphs. Replacing or destroying a cache entry releases the owned
`ShapedText` lifetime according to the DeltaText contract.

Custom visuals emit `UiVisualKind.Custom` with a stable `UiVisualTypeId` and
resource identities. DeltaXAML does not resolve shaders, pipelines, textures
or Vulkan handles.

### `DXAML-RUNTIME-4`: removal and final integration

Remove or split the following compatibility implementation as its replacement
lands; do not wrap it in a new facade:

- `Internal/UiElement.cs`: compatibility owner/relation fields and remaining
  cold property/source operations; the public retained `Measure`/`Arrange`
  operation path has been removed, and runtime layout now enters through the
  queued stage methods;
- `UserApi/LibraryFacade.cs` (`XamlLoader`): runtime parsing and string
  property application;
- `Internal/Compatibility/UiBindingRuntime.cs`: reflection path walking and
  per-binding closure execution;
- `UserApi/StyleApi.cs`: the obsolete delegate template constructor and
  string-key style application;
- obsolete interfaces and packet types in `Internal/RetainedContracts.cs`;
- retained compatibility accessors that still live in `UserApi/LibraryFacade.cs`.

Keep the public library contract while moving its implementation into focused
files. If a compatibility symbol cannot be removed in the same slice, mark it
`[Obsolete(..., error: true)]` with the exact replacement and the next slice;
no new caller may use it.

Final integration acceptance is one path for both editor and game:

```text
platform/engine normalizes input
  -> UiDocument.Dispatch
  -> UiDocument.Layout
  -> UiDocument.BuildDisplayList
  -> consumer adds the borrowed display list to DeltaRender
```

Resize changes viewport/DPI and invalidates only the required measure, arrange,
hit-test and text-cache entries. Pointer capture, focus traversal, keyboard and
IME editing, scrolling and clipping operate through the fixed stages. A game
HUD uses the same document and display-list path; screen, texture or world-space
placement is the renderer consumer's target choice.

### Completion gate

The remaining implementation is complete only when all of these are true:

- every production XAML file has a generated typed artifact;
- production bindings and style/template application have no reflection or
  string traversal;
- `UiDocument` owns one node/property/state model and one fixed stage pipeline;
- public `UiElement` shells contain accessors/state composition, not algorithms;
- canonical contract commands are emitted directly into reused storage;
- unchanged text is not reshaped and unchanged subtrees are not remeasured;
- the compatibility files listed above have been removed or are compile-error
  obsolete with no active caller;
- editor and game HUD paths consume the same `UiDisplayList` boundary;
- Vulkan, SDL, ECS, application clocks and renderer pipelines remain outside
  DeltaXAML.

## Non-goals

DeltaXAML does not promise byte-for-byte WPF, Avalonia, Xamarin or MAUI
compatibility. It does not own ECS data, SDL events, native windows, Vulkan
submission, font rasterization, a global clock or an application framework.
Feature parity is pursued through the DeltaXAML dialect and compiled semantic
model, not by inheriting another framework's runtime costs.
