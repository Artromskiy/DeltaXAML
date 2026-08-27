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
remaining built-in controls are migrated in the subsequent content, layout,
input/editing and items/scroll slices below.

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

The generated path never calls an `object` getter or setter. It resolves the
property once and emits the typed operation directly. The untyped path may box
for editor inspection and runtime diagnostics because it is explicitly cold.

Effective-value precedence remains:

```text
Default < Style/Trigger < Binding < Local < Handle < Animation
```

Only non-default sources require retained override storage. Override storage is
split into typed pools (`UiValuePool<float>`, `UiValuePool<float4>`, reference
value pools and so on), indexed by compact property/node handles. Resolving an
effective value writes the typed result into the concrete control state, so
layout, input and visual mixins do not query the property engine.

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

The designated folders are populated by the first `UiTextBlock` exemplar. The
executable gate still reports any missing or empty designated folder as
`PENDING` and fails the headless command; an empty folder is not an accepted
architecture pass.

| Path | Classification | Boundary rule |
|---|---|---|
| `Internal/UiElement.cs` | obsolete compatibility implementation | Existing retained controls, property store and virtual operations; no new behavior; remove during `DXAML-MIXIN-4/5`. |
| `Internal/UiFrame.cs` | obsolete compatibility implementation | Existing recursive frame, input and draw traversal; keep only for current callers until descriptor stages replace it. |
| `Internal/XamlLoader.cs` | obsolete compatibility implementation | Runtime XML inflation bridge; replace with the compiled artifact path during `DXAML-COMPILE-1/2`. |
| `Internal/BindingRuntime.cs` and `Internal/Theme.cs` | obsolete compatibility implementation | Existing cold binding/resource/style bridge; replace with typed plans during `DXAML-COMPILE-3/4` and `DXAML-MIXIN-5`. |
| `Internal/RetainedContracts.cs` | obsolete compatibility contracts | Current internal adapter vocabulary; do not extend it; remove each operation with its migrated control group. |
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

- compatibility retained control behavior has not yet moved fully into state
  components and typed mixins;
- runtime XAML loading still performs work that belongs in generated artifacts;
- the legacy draw representation is not the canonical `UiDisplayList`;
- retained text requests are not a substitute for canonical shaped
  `UiTextDraw`;
- compatibility type/resource factories do not yet map losslessly to stable
  GUID identities;
- the public typed property API still delegates through parts of the old
  retained store.

These are migration tasks, not alternate architectures.

## Non-goals

DeltaXAML does not promise byte-for-byte WPF, Avalonia, Xamarin or MAUI
compatibility. It does not own ECS data, SDL events, native windows, Vulkan
submission, font rasterization, a global clock or an application framework.
Feature parity is pursued through the DeltaXAML dialect and compiled semantic
model, not by inheriting another framework's runtime costs.
