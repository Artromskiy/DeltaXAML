# DeltaXAML library contract

This document is the authoritative consumer-facing API shape for the retained
DeltaXAML library. It complements [PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md): the
library contract owns loading, the retained document, element/property access,
bindings and resource/type resolution; the cross-project contract owns neutral
input packets and the borrowed renderer-facing display list.

The current `DeltaXAML.Core` and `DeltaXAML.Abstractions` types are migration
surfaces. New API work converges on this contract instead of extending those
legacy surfaces.

## Selected public shape

```csharp
using System.Diagnostics.CodeAnalysis;
using Delta.Diagnostics;
using DeltaMaths;
using DeltaText.Contract;
using DeltaXAML.Contract;

namespace DeltaXAML;

public readonly record struct UiPropertyId(Guid Value)
{
    public bool IsValid => Value != Guid.Empty;
}

public readonly record struct UiTypeId(Guid Value)
{
    public bool IsValid => Value != Guid.Empty;
}

public readonly record struct XamlQualifiedName(
    string Namespace,
    string LocalName);

[Flags]
public enum UiParticipation : byte
{
    None = 0,
    Layout = 1 << 0,
    Rendering = 1 << 1,
    HitTesting = 1 << 2,
    All = Layout | Rendering | HitTesting,
}

public interface IUiProperty
{
    UiPropertyId Id { get; }
    string Name { get; }
    Type ValueType { get; }
    object? DefaultValue { get; }
}

public interface IUiProperty<T> : IUiProperty
{
    new T DefaultValue { get; }
}

public enum UiBindingMode : byte
{
    OneTime,
    OneWay,
    TwoWay,
}

public interface IUiBinding
{
    Type ValueType { get; }
    UiBindingMode Mode { get; }
    object? Read();
    bool TryWrite(object? value, out Diagnostic? diagnostic);
}

public interface IUiBinding<T> : IUiBinding
{
    T ReadValue();
    bool TryWriteValue(T value, out Diagnostic? diagnostic);
}

public abstract class UiElement
{
    public UiElement? Parent { get; }
    public IReadOnlyList<UiElement> Children { get; }
    protected IList<UiElement> MutableChildren { get; }

    public UiParticipation Participation { get; set; }

    public object? GetValue(IUiProperty property);
    public bool TrySetValue(
        IUiProperty property,
        object? value,
        out Diagnostic? diagnostic);

    public T GetValue<T>(IUiProperty<T> property);
    public void SetValue<T>(IUiProperty<T> property, T value);
}

public interface IUiResourceResolver
{
    bool TryResolve(
        UiResourceId resource,
        out object? value);
}

public interface IXamlTypeResolver
{
    bool TryResolveName(
        in XamlQualifiedName name,
        out UiTypeId type);

    bool TryCreate(
        UiTypeId type,
        [NotNullWhen(true)] out UiElement? element);
}

public readonly record struct XamlLoadContext(
    IXamlTypeResolver Types,
    IUiResourceResolver Resources);

public sealed class XamlLoadResult
{
    public UiElement? Root { get; }
    public ReadOnlyMemory<Diagnostic> Diagnostics { get; }
    public bool Success { get; }
}

public interface IXamlLoader
{
    XamlLoadResult Load(
        string source,
        in XamlLoadContext context);
}

public sealed class UiDocument
{
    public UiDocument(
        UiElement root,
        ITextService textService);

    public UiElement Root { get; }

    public void Dispatch(in UiInputEvent input);
    public void Dispatch(ReadOnlySpan<UiInputEvent> input);

    public void Layout(float2 viewport, float dpiScale);

    public UiDisplayList BuildDisplayList();
    public bool TryBuildDisplayList(
        out UiDisplayList displayList,
        out Diagnostic? diagnostic);
}
```

The declarations above specify public shape and ownership; they are not a
second implementation. `DeltaXAML.Core` supplies the concrete retained storage,
loader, document and property/binding implementations.

`TryBuildDisplayList` is the diagnostic form for a retained value that cannot
be represented by the canonical display-list contract during migration.

## Participation invariant

`Rendering` and `HitTesting` require `Layout`. Therefore the only valid values
are `None`, `Layout`, `Layout | Rendering`, `Layout | HitTesting`, and `All`.
Assigning another combination is a programmer contract violation and throws
`ArgumentException`. When an element does not participate in layout, its entire
subtree is excluded from layout, rendering and hit testing.

`Opacity`, `IsEnabled` and `IsHitTestVisible` are deliberately absent from the
base class. Opacity is a visual capability only when the implementation defines
correct primitive or group-composition semantics. Enabled/read-only/command
state belongs to the control that can define its exact behavior. Hit testing is
already represented by `UiParticipation`.

## Tree ownership

Every element exposes its logical children, so a special `Panel` contract is
not required. Consumers receive `IReadOnlyList<UiElement>`. Derived custom
elements mutate their tree through the protected `IList<UiElement>`; its
implementation validates cycles, duplicate parents and updates `Parent`.
Publicly exposing an `IList` that rejects all writes would advertise a false
capability and is not part of the contract.

## Identity and resolution

Stable property, type and resource identities are typed wrappers over `Guid`.
The library reuses `DeltaXAML.Contract.UiResourceId` instead of declaring a
second resource identity. XAML names are source aliases, not identities.
`IXamlTypeResolver` remains a
DeltaXAML-owned resolver because it creates retained UI elements. Do not add a
shared `Guid -> System.Type` registry: that would erase typed identity and bind
the contract to reflection. A generic cross-project resolver is deferred until
at least three real consumers need exactly the same synchronous `TryResolve`
semantics.

## Diagnostics and services

Loading, binding and property failures use `Delta.Diagnostics.Contract`.
Project-specific diagnostic codes remain extensible strings such as `XAML001`.
Programmer contract violations use exceptions; expected absence uses `Try...`.

`ITextService` is `DeltaText.Contract.ITextService`. It is required by
`UiDocument` for text layout and is not a loader option. Clipboard access is a
platform-host concern and is not part of this minimal library contract.

## Excluded implementation surface

Controls, styles, templates, dirty masks, layout nodes, routing, focus,
clipboard adapters, concrete dictionaries, compiled-binding implementations
and caches remain DeltaXAML implementation. The public contract does not expose
renderer services, Vulkan objects, engine lifecycle, ECS storage or delta time.
