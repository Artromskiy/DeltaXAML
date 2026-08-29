using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Delta.Diagnostics;
using Delta.XAML.Contract;

using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal readonly struct UiValue
{
    public UiValue(object? value, UiValueSource source, UiDirtyFlags invalidation)
    {
        UntypedValue = value;
        Source = source;
        Invalidation = invalidation;
    }
    public object? UntypedValue { get; }
    public UiValueSource Source { get; }
    public UiDirtyFlags Invalidation { get; }
}

internal sealed class UiBindingValue
{
    private readonly Func<object?>? _read;
    private readonly Func<object?, (bool Success, string? Error)>? _write;
    private readonly Delta.XAML.IUiBinding? _external;

    public UiBindingValue(Func<object?> read, Func<object?, (bool Success, string? Error)> write)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        _read = read;
        _write = write;
    }

    internal UiBindingValue(Delta.XAML.IUiBinding external)
    {
        ArgumentNullException.ThrowIfNull(external);
        _external = external;
    }

    public object? Read()
    {
        if (_external is { } external)
        {
            return external.Read();
        }

        if (_read is not { } read)
        {
            throw new InvalidOperationException("The binding read operation is not initialized.");
        }

        return read();
    }

    public bool TryWrite(object? value, [NotNullWhen(false)] out string? diagnostic)
    {
        if (_external is { } external)
        {
            var success = external.TryWrite(value, out var externalDiagnostic);
            diagnostic = externalDiagnostic?.Message ?? (success ? null : "Binding write failed.");
            return success;
        }

        if (_write is not { } write)
        {
            diagnostic = "The binding write operation is not initialized.";
            return false;
        }

        var result = write(value);
        diagnostic = result.Error ?? (result.Success ? null : "Binding write failed.");
        return result.Success;
    }

    public event EventHandler? Changed; public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class UiPropertyStore
{
    private readonly Dictionary<string, UiValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SourceSlots> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiBindingValue> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventHandler> _bindingHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceBinding> _resourceBindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceDiagnostic> _resourceDiagnostics = new(StringComparer.Ordinal);
    private readonly UiElement _owner;
    private bool _disposed;
    private static readonly UiValueSource[] Precedence =
    [
        UiValueSource.Animation,
        UiValueSource.Handle,
        UiValueSource.Local,
        UiValueSource.Binding,
        UiValueSource.Trigger,
        UiValueSource.Style,
        UiValueSource.Default,
    ];
    public UiPropertyStore(UiElement owner) { ArgumentNullException.ThrowIfNull(owner); _owner = owner; }
    internal void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var pair in _bindings)
        {
            if (_bindingHandlers.TryGetValue(pair.Key, out var handler))
            {
                pair.Value.Changed -= handler;
            }
        }

        foreach (var binding in _resourceBindings.Values)
        {
            if (binding.Handler is { } handler)
            {
                binding.Resources.Changed -= handler;
            }
        }

        _bindingHandlers.Clear();
        _resourceBindings.Clear();
        _resourceDiagnostics.Clear();
        _bindings.Clear();
    }
    public void InitializeDefault(string name, object? value, UiDirtyFlags invalidation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var slots = GetSlots(name);
        var defaultValue = new UiValue(value, UiValueSource.Default, invalidation);
        SetValue(slots, UiValueSource.Default, defaultValue);
        _values[name] = defaultValue;
    }
    public void SetDefault(string name, object? value, UiDirtyFlags invalidation) => SetSource(name, new(value, UiValueSource.Default, invalidation));
    public void SetLocal(string name, object? value, UiDirtyFlags invalidation) => SetSource(name, new(value, UiValueSource.Local, invalidation));
    public void SetStyle(string name, object? value, UiDirtyFlags invalidation)
    {
        RemoveResourceBinding(name);
        SetSource(name, new(value, UiValueSource.Style, invalidation));
    }
    public void SetTrigger(string name, object? value, UiDirtyFlags invalidation) =>
        SetSource(name, new(value, UiValueSource.Trigger, invalidation));
    public void SetStyleResource(string name, UiResourceStore resources, UiResourceReference reference, UiDirtyFlags invalidation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.Key);
        RemoveResourceBinding(name);
        var binding = new ResourceBinding(resources, reference, invalidation);
        _resourceBindings[name] = binding;
        binding.Handler = (_, args) =>
        {
            if (!resources.DependsOn(binding.Reference, args))
            {
                return;
            }

            if (_owner.NodeStore is null)
            {
                ApplyResourceBinding(name, binding);
                return;
            }

            binding.Pending = true;
            _owner.InvalidateChanged(UiDirtyFlags.Resource | UiDirtyFlags.Style);
        };
        resources.Changed += binding.Handler;
        ApplyResourceBinding(name, binding);
    }
    public void SetBinding(string name, UiBindingValue binding, UiDirtyFlags invalidation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(binding);
        RemoveBinding(name);
        _bindings[name] = binding;
        var slots = GetSlots(name);
        SetValue(slots, UiValueSource.Binding, new UiValue(binding.Read(), UiValueSource.Binding, invalidation));
        ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
        EventHandler handler = (_, _) =>
        {
            SetValue(slots, UiValueSource.Binding, new UiValue(binding.Read(), UiValueSource.Binding, invalidation));
            ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
        };
        _bindingHandlers[name] = handler;
        binding.Changed += handler;
    }

    public void SetBindingValue(string name, UiPropertyKey property, object? value, UiDirtyFlags invalidation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var slots = GetSlots(name);
        SetValue(slots, UiValueSource.Binding, new UiValue(value, UiValueSource.Binding, invalidation));
        ApplyEffective(name, slots, property);
    }
    public void SetHandle(string name, object? value, UiDirtyFlags invalidation) => SetSource(name, new(value, UiValueSource.Handle, invalidation));
    public void SetAnimation(string name, object? value, UiDirtyFlags invalidation) => SetSource(name, new(value, UiValueSource.Animation, invalidation));
    public void Clear(string name, UiValueSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (source == UiValueSource.Default) { throw new ArgumentException("Default values cannot be cleared.", nameof(source)); }
        if (source == UiValueSource.Binding) { RemoveBinding(name); }
        if (source == UiValueSource.Style) { RemoveResourceBinding(name); }
        if (_slots.TryGetValue(name, out var slots))
        {
            ClearValue(slots, source);
            ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
        }
    }
    public bool TryGet(string name, out UiValue value) { ArgumentException.ThrowIfNullOrWhiteSpace(name); return _values.TryGetValue(name, out value); }
    public UiPropertyHandle GetHandle(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A UI property name is required.", nameof(name));
        }

        return new(_owner.Id, _owner.Generation, name);
    }
    public bool TrySet(UiPropertyHandle handle, object? value, UiDirtyFlags invalidation, [NotNullWhen(false)] out string? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(handle.Name)) { diagnostic = "A UI property name is required."; return false; }
        if (handle.Element != _owner.Id || handle.Generation != _owner.Generation) { diagnostic = "The UI property handle is stale."; return false; }
        SetHandle(handle.Name, value, invalidation); diagnostic = null; return true;
    }
    internal void ApplyPendingResources()
    {
        foreach (var pair in _resourceBindings)
        {
            if (!pair.Value.Pending)
            {
                continue;
            }

            pair.Value.Pending = false;
            ApplyResourceBinding(pair.Key, pair.Value);
        }
    }
    internal bool TryGetResourceDiagnostic(
        [NotNullWhen(true)] out string? code,
        [NotNullWhen(true)] out string? message)
    {
        foreach (var diagnostic in _resourceDiagnostics.Values)
        {
            code = diagnostic.Code;
            message = diagnostic.Message;
            return true;
        }

        code = null;
        message = null;
        return false;
    }
    private void RemoveBinding(string name)
    {
        if (_bindings.Remove(name, out var binding) && _bindingHandlers.Remove(name, out var handler))
        {
            binding.Changed -= handler;
        }
        if (_slots.TryGetValue(name, out var slots)) { ClearValue(slots, UiValueSource.Binding); }
    }
    private void RemoveResourceBinding(string name)
    {
        if (_resourceBindings.Remove(name, out var binding))
        {
            binding.Resources.Changed -= binding.Handler;
        }
    }
    private void SetSource(string name, UiValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var slots = GetSlots(name);
        SetValue(slots, value.Source, value);
        if (_values.TryGetValue(name, out var current) && Same(in current, in value))
        {
            return;
        }

        ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
    }
    private static void SetValue(SourceSlots slots, UiValueSource source, UiValue value)
    {
        switch (source)
        {
            case UiValueSource.Default: slots.DefaultValue = value.UntypedValue; slots.DefaultInvalidation = value.Invalidation; break;
            case UiValueSource.Style: slots.StyleValue = value.UntypedValue; slots.StyleInvalidation = value.Invalidation; break;
            case UiValueSource.Trigger: slots.TriggerValue = value.UntypedValue; slots.TriggerInvalidation = value.Invalidation; break;
            case UiValueSource.Binding: slots.BindingValue = value.UntypedValue; slots.BindingInvalidation = value.Invalidation; break;
            case UiValueSource.Local: slots.LocalValue = value.UntypedValue; slots.LocalInvalidation = value.Invalidation; break;
            case UiValueSource.Handle: slots.HandleValue = value.UntypedValue; slots.HandleInvalidation = value.Invalidation; break;
            case UiValueSource.Animation: slots.AnimationValue = value.UntypedValue; slots.AnimationInvalidation = value.Invalidation; break;
        }

        slots.Present |= SourceBit(source);
    }
    private static void ClearValue(SourceSlots slots, UiValueSource source)
    {
        slots.Present &= (byte)~SourceBit(source);
    }
    private SourceSlots GetSlots(string name) => _slots.TryGetValue(name, out var slots) ? slots : AddSlots(name);
    private SourceSlots AddSlots(string name) { var slots = new SourceSlots(); _slots.Add(name, slots); return slots; }
    private void ApplyEffective(string name, SourceSlots slots, UiPropertyKey property)
    {
        var hasPrevious = _values.TryGetValue(name, out var previous);
        var hasEffective = TryResolve(slots, out var effective);
        if (hasPrevious == hasEffective && (!hasEffective || Same(in previous, in effective)))
        {
            if (!hasEffective)
            {
                _values.Remove(name);
            }
            else
            {
                _values[name] = effective;
            }

            return;
        }
        while (hasEffective && !UiDescriptorCatalog.TrySetProperty(_owner, property, effective))
        {
            ClearValue(slots, effective.Source);
            hasEffective = TryResolve(slots, out effective);
        }

        if (hasPrevious == hasEffective && (!hasEffective || Same(in previous, in effective)))
        {
            return;
        }

        var invalidation = hasEffective
            ? effective.Invalidation
            : hasPrevious
                ? previous.Invalidation
                : UiDirtyFlags.Visual;
        _owner.InvalidateChanged(invalidation);
        if (!hasEffective)
        {
            _values.Remove(name);
        }
        else
        {
            _values[name] = effective;
        }

        _owner.NotifyEffectivePropertyChanged(name);
    }
    private static bool TryResolve(SourceSlots slots, out UiValue value)
    {
        foreach (var source in Precedence)
        {
            if (TryGetValue(slots, source, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }
    private static bool TryGetValue(SourceSlots slots, UiValueSource source, out UiValue value)
    {
        if ((slots.Present & SourceBit(source)) == 0)
        {
            value = default;
            return false;
        }

        value = source switch
        {
            UiValueSource.Animation => new UiValue(slots.AnimationValue, source, slots.AnimationInvalidation),
            UiValueSource.Handle => new UiValue(slots.HandleValue, source, slots.HandleInvalidation),
            UiValueSource.Local => new UiValue(slots.LocalValue, source, slots.LocalInvalidation),
            UiValueSource.Binding => new UiValue(slots.BindingValue, source, slots.BindingInvalidation),
            UiValueSource.Trigger => new UiValue(slots.TriggerValue, source, slots.TriggerInvalidation),
            UiValueSource.Style => new UiValue(slots.StyleValue, source, slots.StyleInvalidation),
            UiValueSource.Default => new UiValue(slots.DefaultValue, source, slots.DefaultInvalidation),
            _ => default,
        };
        return true;
    }
    private void ApplyResourceBinding(string name, ResourceBinding binding)
    {
        var slots = GetSlots(name);
        if (!binding.Resources.TryResolve(binding.Reference, out var value, out var resourceDiagnostic))
        {
            _resourceDiagnostics[name] = new(
                "XAML006",
                resourceDiagnostic ?? $"Resource '{ResourceIdentity(binding.Reference)}' was not resolved.");
            SetValue(slots, UiValueSource.Style, new(null, UiValueSource.Style, binding.Invalidation));
            ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
            return;
        }

        if (!IsCompatibleValue(name, value))
        {
            _resourceDiagnostics[name] = new(
                "XAML010",
                $"Resource '{ResourceIdentity(binding.Reference)}' is not compatible with property '{name}'.");
            SetValue(slots, UiValueSource.Style, new(null, UiValueSource.Style, binding.Invalidation));
            ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
            return;
        }

        _resourceDiagnostics.Remove(name);
        SetValue(slots, UiValueSource.Style, new(value, UiValueSource.Style, binding.Invalidation));
        ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
    }
    private bool IsCompatibleValue(string name, object? value)
    {
        var valueType = UiPropertyKeys.ValueType(_owner, name);
        return value is not null
            ? valueType.IsInstanceOfType(value)
            : !valueType.IsValueType || Nullable.GetUnderlyingType(valueType) is not null;
    }
    private static string ResourceIdentity(UiResourceReference reference) =>
        reference.HasResourceId ? reference.ResourceId.ToString("D") : reference.Key;
    private static bool Same(in UiValue left, in UiValue right) =>
        left.Source == right.Source && Equals(left.UntypedValue, right.UntypedValue);
    private static byte SourceBit(UiValueSource source) => (byte)(1 << (int)source);
    private sealed class SourceSlots
    {
        public object? DefaultValue, StyleValue, TriggerValue, BindingValue, LocalValue, HandleValue, AnimationValue;
        public UiDirtyFlags DefaultInvalidation, StyleInvalidation, TriggerInvalidation, BindingInvalidation, LocalInvalidation, HandleInvalidation, AnimationInvalidation;
        public byte Present;
    }
    private sealed class ResourceBinding
    {
        public ResourceBinding(UiResourceStore resources, UiResourceReference reference, UiDirtyFlags invalidation)
        {
            Resources = resources; Reference = reference; Invalidation = invalidation;
        }
        public UiResourceStore Resources { get; }
        public UiResourceReference Reference { get; }
        public UiDirtyFlags Invalidation { get; }
        public EventHandler<UiResourceChangedEventArgs>? Handler { get; set; }
        public bool Pending { get; set; }
    }

    private readonly record struct ResourceDiagnostic(string Code, string Message);
}

/// <summary>Canonical retained state owner addressed by the document node store.</summary>
internal class UiElement
{
    private static uint _nextId;
    private static uint _nextGeneration;
    private readonly List<UiElement> _detachedChildren = new();
    private readonly UiElementChildrenView _children;
    private readonly string _typeName;
    private readonly List<UiBindingSpec> _bindingSpecs = new();
    private readonly Dictionary<string, UiInterpretedBinding> _bindingRuntimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiExternalBindingRuntime> _externalBindingRuntimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IUiCompiledBindingRuntime> _compiledBindingRuntimes = new(StringComparer.Ordinal);
    private readonly UiPropertyStore _properties;
    private UiNodeStore? _nodeStore;
    private UiElementState _state = new()
    {
        Width = float.NaN,
        Height = float.NaN,
        IsEnabled = true,
        GridRowSpan = 1,
        GridColumnSpan = 1,
    };
    private object? _bindingContext;
    private bool _hasExplicitBindingContext;
    private bool _bindingStageManaged;
    private float _layoutScale = 1f;
    private float _dpiScale = 1f;
    private uint _layoutVersion;
    private uint _dpiVersion;
    private uint _textVersion;
    private uint _treeVersion;
    private uint _relationVersion;
    private uint _outputVersion;
    private Delta.XAML.UiStyle? _appliedStyle;
    private Delta.XAML.UiStyleState _appliedStyleState;
    private int _appliedStyleVersion = -1;
    private Guid _compiledStyleId;
    private Delta.XAML.UiTemplateId _compiledTemplateId;
    private string? _styleKey;
    private string? _templateKey;
    private UiSize _measuredAvailable;
    private UiRect _arrangedBounds;
    private UiRect _arrangedClip;
    private bool _hasMeasured;
    private bool _hasArranged;
    private bool _runtimeDisposed;
    private int _displayClipIndex = -1;
    private int _displayVisualIndex = -1;
    private int _displayTextIndex = -1;
    private int _displayOwnTextCount;
    private int _displayClipCount;
    private int _displayVisualCount;
    private int _displayTextCount;
    internal ref UiElementState CommonState => ref _state;
    public UiElement(string typeName = "Element")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        _typeName = typeName;
        Id = new(++_nextId);
        Generation = ++_nextGeneration;
        _children = new(this);
        _properties = new(this);
        _properties.InitializeDefault("Width", _state.Width, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        _properties.InitializeDefault("Height", _state.Height, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        _properties.InitializeDefault("Background", _state.Background, UiDirtyFlags.Visual);
        _properties.InitializeDefault("BorderColor", _state.BorderColor, UiDirtyFlags.Visual);
        _properties.InitializeDefault("BorderWidth", _state.BorderWidth, UiDirtyFlags.Visual);
        _properties.InitializeDefault("CornerRadius", _state.CornerRadius, UiDirtyFlags.Visual);
        _properties.InitializeDefault("Padding", _state.Padding, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        _properties.InitializeDefault("Fill", _state.Fill, UiDirtyFlags.Visual);
        _properties.InitializeDefault("IsEnabled", _state.IsEnabled, UiDirtyFlags.Visual);
        _properties.InitializeDefault("IsSelected", _state.IsSelected, UiDirtyFlags.Visual);
    }
    public UiElementId Id { get; }
    public uint Generation { get; }
    internal uint TreeVersion => _treeVersion;
    internal uint RelationVersion => _relationVersion;
    public string TypeName => _typeName;
    public UiElement? Parent => _nodeStore is { } store ? store.GetLogicalParent(this) : _detachedParent;
    public IReadOnlyList<UiElement> Children => _children;
    private UiElement? _detachedParent;
    internal UiNodeStore? NodeStore => _nodeStore;
    internal List<UiElement> DetachedChildren => _detachedChildren;
    public UiVisibility Visibility { get; set; } = UiVisibility.Visible; public bool Focusable { get; set; }
    public Delta.XAML.UiParticipation Participation { get; private set; } = Delta.XAML.UiParticipation.All;
    internal bool ParticipatesIn(Delta.XAML.UiParticipation participation) => (Participation & participation) == participation;
    public float Width { get => _state.Width; set => SetLocalProperty("Width", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual); }
    public float Height { get => _state.Height; set => SetLocalProperty("Height", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual); }
    public bool Fill { get => _state.Fill; set => SetLocalProperty("Fill", value, UiDirtyFlags.Visual); }
    public UiRect Bounds { get; protected set; }
    public UiRect Clip { get; protected set; }
    public UiSize DesiredSize { get; protected set; }
    public UiColor Background { get => _state.Background; set => SetLocalProperty("Background", value, UiDirtyFlags.Visual); }
    public UiColor BorderColor { get => _state.BorderColor; set => SetLocalProperty("BorderColor", value, UiDirtyFlags.Visual); }
    public float BorderWidth
    {
        get => _state.BorderWidth;
        set
        {
            if (!float.IsFinite(value) || value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            SetLocalProperty("BorderWidth", value, UiDirtyFlags.Visual);
        }
    }
    public Delta.XAML.UiCornerRadii CornerRadius
    {
        get => _state.CornerRadius;
        set
        {
            if (!value.IsFiniteNonNegative) { throw new ArgumentOutOfRangeException(nameof(value)); }
            SetLocalProperty("CornerRadius", value, UiDirtyFlags.Visual);
        }
    }
    internal bool HasCustomVisual => _state.CustomVisualType != Guid.Empty;
    internal Guid CustomVisualTypeId => _state.CustomVisualType;
    internal Guid CustomVisualResourceId => _state.CustomVisualResource;
    internal UiColor CustomVisualColor => _state.CustomVisualColor;
    public bool IsEnabled { get => _state.IsEnabled; set => SetLocalProperty("IsEnabled", value, UiDirtyFlags.Visual); }
    public bool IsHovered { get; private set; }
    public bool IsPressed => UiDescriptorCatalog.IsPressed(this);
    public bool IsSelected
    {
        get => _state.IsSelected;
        set
        {
            if (_state.IsSelected != value)
            {
                SetLocalProperty("IsSelected", value, UiDirtyFlags.Visual);
            }
        }
    }
    public bool IsInvalid { get; protected set; }
    public string? StyleKey
    {
        get => _styleKey;
        set
        {
            if (string.Equals(_styleKey, value, StringComparison.Ordinal))
            {
                return;
            }

            _styleKey = value;
            InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        }
    }
    public string? TemplateKey
    {
        get => _templateKey;
        set
        {
            if (string.Equals(_templateKey, value, StringComparison.Ordinal))
            {
                return;
            }

            _templateKey = value;
            _compiledTemplateId = default;
            InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        }
    }
    public string? AutomationName { get; set; }
    public UiAutomationRole AutomationRole { get; set; } = UiAutomationRole.Generic;
    public UiThickness Margin { get; set; }
    public UiThickness Padding { get => _state.Padding; set => SetLocalProperty("Padding", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual); }
    internal Delta.XAML.UiGestureKind Gestures
    {
        get => (Delta.XAML.UiGestureKind)_state.GestureBits;
        set => _state.GestureBits = (ulong)value;
    }
    internal Delta.XAML.UiCommandId Command
    {
        get => new(_state.CommandId);
        set => _state.CommandId = value.Value;
    }
    internal Delta.XAML.UiKeyGesture CommandKey
    {
        get => new(new(_state.CommandPhysicalKey), new(_state.CommandModifiers));
        set
        {
            _state.CommandPhysicalKey = value.Key.Value;
            _state.CommandModifiers = value.Modifiers.Bits;
        }
    }
    internal bool IsFocusScope
    {
        get => _state.IsFocusScope;
        set => _state.IsFocusScope = value;
    }
    internal int GridRow => _state.GridRow;
    internal int GridColumn => _state.GridColumn;
    internal int GridRowSpan => _state.GridRowSpan;
    internal int GridColumnSpan => _state.GridColumnSpan;
    internal bool HasGridRow => (_state.GridPlacementFlags & 1) != 0;
    internal bool HasGridColumn => (_state.GridPlacementFlags & 2) != 0;
    internal int CollectionIndex => _state.HasCollectionIndex ? _state.CollectionIndex : -1;
    internal void SetGridRow(int value) => SetGridSlot(ref _state.GridRow, value, 1, nameof(GridRow));
    internal void SetGridColumn(int value) => SetGridSlot(ref _state.GridColumn, value, 2, nameof(GridColumn));
    internal void SetGridRowSpan(int value) => SetGridSpan(ref _state.GridRowSpan, value, nameof(GridRowSpan));
    internal void SetGridColumnSpan(int value) => SetGridSpan(ref _state.GridColumnSpan, value, nameof(GridColumnSpan));
    internal void SetCollectionIndex(int value)
    {
        if (value < 0)
        {
            _state.CollectionIndex = 0;
            _state.HasCollectionIndex = false;
            return;
        }

        _state.CollectionIndex = value;
        _state.HasCollectionIndex = true;
    }
    internal UiDirtyFlags DirtyFlags { get; set; } = UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual;
    public UiAutomationMetadata Automation => new(AutomationName ?? TypeName, AutomationRole, GetAutomationValueText(), IsEnabled, IsInvalid);
    public UiStateSnapshot VisualState => new(IsEnabled ? IsInvalid ? UiVisualState.Invalid : IsPressed ? UiVisualState.Pressed : IsHovered ? UiVisualState.Hover : IsSelected ? UiVisualState.Selected : IsFocused ? UiVisualState.Focused : UiVisualState.Normal : UiVisualState.Disabled, IsEnabled, IsInvalid, IsSelected, IsFocused, IsHovered, IsPressed);
    public bool IsFocused { get; private set; }
    public void Add(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);

        for (var ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, child))
            {
                throw new ArgumentException("A UI element cannot be added below itself.", nameof(child));
            }
        }

        if (child._nodeStore is not null && !ReferenceEquals(child._nodeStore, _nodeStore))
        {
            throw new InvalidOperationException("An element cannot move between attached documents without being detached first.");
        }

        if (_nodeStore is null && child._nodeStore is not null)
        {
            throw new InvalidOperationException("A retained document root cannot be reparented while its node store is attached.");
        }

        if (child.Parent is { } parent)
        {
            parent.Remove(child);
        }

        if (_hasExplicitBindingContext && !child._hasExplicitBindingContext)
        {
            child.SetBindingContext(_bindingContext, false);
        }

        if (_nodeStore is { } store)
        {
            store.AddLogicalChild(this, child);
        }
        else
        {
            child._detachedParent = this;
            _detachedChildren.Add(child);
            child.AdvanceDetachedRelationVersion();
        }

        var invalidation = UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual;
        if (child.IsStyleDirty)
        {
            invalidation |= UiDirtyFlags.Style;
        }

        InvalidateChanged(invalidation);
        _nodeStore?.CommitTreeVersion();
    }
    public bool Remove(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_nodeStore is { } store)
        {
            if (!store.RemoveLogicalChild(this, child))
            {
                return false;
            }

            InvalidateChanged(UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
            store.CommitTreeVersion();
            return true;
        }

        var index = _detachedChildren.IndexOf(child);
        if (index < 0)
        {
            return false;
        }

        _detachedChildren.RemoveAt(index);
        child._detachedParent = null;
        child.AdvanceDetachedRelationVersion();
        InvalidateChanged(UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        return true;
    }
    public void ClearChildren()
    {
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            Remove(Children[i]);
        }
    }
    public void Invalidate(UiDirtyFlags flags)
    {
        InvalidateCore(flags, false);
    }

    private void SetGridSlot(ref int field, int value, byte flag, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, name);
        if (field == value && (_state.GridPlacementFlags & flag) != 0)
        {
            return;
        }

        field = value;
        _state.GridPlacementFlags |= flag;
        InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

    private void SetGridSpan(ref int field, int value, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, name);
        if (field == value)
        {
            return;
        }

        field = value;
        InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

    private void AdvanceDetachedRelationVersion()
    {
        _relationVersion++;
        for (var i = 0; i < _detachedChildren.Count; i++)
        {
            _detachedChildren[i].AdvanceDetachedRelationVersion();
        }
    }

    internal void InvalidateChanged(UiDirtyFlags flags)
    {
        InvalidateCore(flags, true);
    }

    private void InvalidateCore(UiDirtyFlags flags, bool changed)
    {
        if (flags == UiDirtyFlags.None)
        {
            return;
        }

        var current = this;
        var propagatedFlags = flags;
        while (propagatedFlags != UiDirtyFlags.None)
        {
            var newFlags = propagatedFlags & ~current.DirtyFlags;
            current.DirtyFlags |= propagatedFlags;
            if (!changed && newFlags == UiDirtyFlags.None)
            {
                return;
            }

            var versionFlags = changed ? propagatedFlags : newFlags;
            var versionedFlags = versionFlags & ~UiDirtyFlags.BindingSubtree;
            if (versionedFlags != UiDirtyFlags.None)
            {
                current._outputVersion++;
                if ((versionedFlags & UiDirtyFlags.Tree) != 0)
                {
                    current._treeVersion++;
                }

                if ((versionedFlags & (UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Style | UiDirtyFlags.Resource)) != 0)
                {
                    current._layoutVersion++;
                }

                if ((versionedFlags & UiDirtyFlags.Text) != 0)
                {
                    current._textVersion++;
                }
            }

            var parentFlags = UiDirtyFlags.None;
            if ((versionedFlags & (UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Style | UiDirtyFlags.Resource)) != 0)
            {
                parentFlags |= UiDirtyFlags.Measure;
            }

            if ((versionedFlags & UiDirtyFlags.Tree) != 0)
            {
                parentFlags |= UiDirtyFlags.Tree;
            }

            if ((versionedFlags & UiDirtyFlags.Visual) != 0)
            {
                parentFlags |= UiDirtyFlags.Visual;
            }

            if ((versionedFlags & UiDirtyFlags.Text) != 0)
            {
                parentFlags |= UiDirtyFlags.Visual;
            }

            if ((versionedFlags & UiDirtyFlags.HitTest) != 0)
            {
                parentFlags |= UiDirtyFlags.HitTest;
            }

            if ((versionedFlags & UiDirtyFlags.Style) != 0)
            {
                parentFlags |= UiDirtyFlags.Style;
            }

            if ((versionFlags & (UiDirtyFlags.Binding | UiDirtyFlags.BindingSubtree)) != 0)
            {
                parentFlags |= UiDirtyFlags.BindingSubtree;
            }

            current = current.Parent as UiElement;
            propagatedFlags = parentFlags;
            if (current is null)
            {
                return;
            }
        }
    }
    public void SetHovered(bool value) { if (IsHovered != value) { IsHovered = value; InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual); } }
    public void SetPressed(bool value) => UiDescriptorCatalog.SetPressed(this, value);
    public void SetFocused(bool value) { if (IsFocused != value) { IsFocused = value; InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual); } }
    public void SetInvalid(bool value) { if (IsInvalid != value) { IsInvalid = value; InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual); } }
    internal void SetCustomVisual(Guid visualType, Guid resource, UiColor color)
    {
        if (visualType == Guid.Empty)
        {
            throw new ArgumentException("A custom visual identity is required.", nameof(visualType));
        }

        if (_state.CustomVisualType == visualType && _state.CustomVisualResource == resource && _state.CustomVisualColor == color)
        {
            return;
        }

        _state.CustomVisualType = visualType;
        _state.CustomVisualResource = resource;
        _state.CustomVisualColor = color;
        InvalidateChanged(UiDirtyFlags.Visual);
    }

    internal void ClearCustomVisual()
    {
        if (!HasCustomVisual)
        {
            return;
        }

        _state.CustomVisualType = Guid.Empty;
        _state.CustomVisualResource = Guid.Empty;
        _state.CustomVisualColor = default;
        InvalidateChanged(UiDirtyFlags.Visual);
    }
    public void SetParticipation(Delta.XAML.UiParticipation value)
    {
        if (Participation == value)
        {
            return;
        }

        Participation = value;
        InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.HitTest);
    }
    internal bool CanSkipMeasure(UiSize available) =>
        _hasMeasured && (DirtyFlags & UiDirtyFlags.Measure) == 0 && _measuredAvailable == available;

    internal bool NeedsMeasure(UiSize available) => !CanSkipMeasure(available);

    internal void CompleteMeasure(UiSize available, UiSize desired)
    {
        DesiredSize = desired;
        _measuredAvailable = available;
        _hasMeasured = true;
        DirtyFlags &= ~UiDirtyFlags.Measure;
        DirtyFlags |= UiDirtyFlags.Arrange;
    }

    internal bool CanSkipArrange(UiRect bounds, UiRect clip) =>
        _hasArranged && (DirtyFlags & UiDirtyFlags.Arrange) == 0 && _arrangedBounds == bounds && _arrangedClip == clip;

    internal bool NeedsArrange(UiRect bounds, UiRect clip) => !CanSkipArrange(bounds, clip);

    internal void SetArrangeFrame(UiRect bounds, UiRect clip)
    {
        Bounds = bounds;
        Clip = clip;
    }

    internal void SetNonParticipatingArrange(UiRect clip)
    {
        Bounds = default;
        Clip = clip;
    }

    internal void CompleteArrange(UiRect bounds, UiRect clip)
    {
        _arrangedBounds = bounds;
        _arrangedClip = clip;
        _hasArranged = true;
        DirtyFlags &= ~UiDirtyFlags.Arrange;
    }

    internal void CompleteVisualExtraction() => DirtyFlags &= ~(UiDirtyFlags.Tree | UiDirtyFlags.Visual | UiDirtyFlags.Text);
    internal bool IsStyleDirty => (DirtyFlags & UiDirtyFlags.Style) != 0;
    internal bool NeedsResourceStage => (DirtyFlags & UiDirtyFlags.Resource) != 0;
    internal void ApplyResourceStage()
    {
        _properties.ApplyPendingResources();
        DirtyFlags &= ~UiDirtyFlags.Resource;
    }
    internal void CompleteStyleStage() => DirtyFlags &= ~UiDirtyFlags.Style;
    internal bool NeedsVisualExtraction => (DirtyFlags & (UiDirtyFlags.Tree | UiDirtyFlags.Style | UiDirtyFlags.Binding | UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual | UiDirtyFlags.Resource | UiDirtyFlags.Text)) != 0;
    internal int DisplayClipIndex => _displayClipIndex;
    internal int DisplayVisualIndex => _displayVisualIndex;
    internal int DisplayTextIndex => _displayTextIndex;
    internal int DisplayOwnTextCount => _displayOwnTextCount;
    internal int DisplayClipCount => _displayClipCount;
    internal int DisplayVisualCount => _displayVisualCount;
    internal int DisplayTextCount => _displayTextCount;
    internal void SetDisplayRange(int clipIndex, int visualIndex, int textIndex, int ownTextCount = 0)
    {
        _displayClipIndex = clipIndex;
        _displayVisualIndex = visualIndex;
        _displayTextIndex = textIndex;
        _displayOwnTextCount = ownTextCount;
    }
    internal void SetDisplaySubtreeCounts(int clips, int visuals, int text)
    {
        _displayClipCount = clips;
        _displayVisualCount = visuals;
        _displayTextCount = text;
    }
    internal void ClearDisplayRange()
    {
        SetDisplayRange(-1, -1, -1, 0);
        SetDisplaySubtreeCounts(0, 0, 0);
    }
    public void SetDefault(string name, object? value, UiDirtyFlags invalidation) => _properties.SetDefault(name, value, invalidation); public void SetLocal(string name, object? value, UiDirtyFlags invalidation) => _properties.SetLocal(name, value, invalidation); public void SetStyle(string name, object? value, UiDirtyFlags invalidation) => _properties.SetStyle(name, value, invalidation); public void SetTrigger(string name, object? value, UiDirtyFlags invalidation) => _properties.SetTrigger(name, value, invalidation); public void SetBinding(string name, UiBindingValue binding, UiDirtyFlags invalidation) => _properties.SetBinding(name, binding, invalidation); public void SetHandle(string name, object? value, UiDirtyFlags invalidation) => _properties.SetHandle(name, value, invalidation); public void SetAnimation(string name, object? value, UiDirtyFlags invalidation) => _properties.SetAnimation(name, value, invalidation); public void SetStyleResource(string name, UiResourceStore resources, UiResourceReference reference, UiDirtyFlags invalidation) => _properties.SetStyleResource(name, resources, reference, invalidation); public void Clear(string name, UiValueSource source) => _properties.Clear(name, source); public bool TryGet(string name, out UiValue value) => _properties.TryGet(name, out value);
    internal bool TryGetResourceDiagnostic(
        [NotNullWhen(true)] out string? code,
        [NotNullWhen(true)] out string? message) => _properties.TryGetResourceDiagnostic(out code, out message);
    public UiPropertyHandle GetHandle(string name) => _properties.GetHandle(name);
    public bool TrySet(UiPropertyHandle handle, object? value, UiDirtyFlags invalidation, [NotNullWhen(false)] out string? diagnostic) => _properties.TrySet(handle, value, invalidation, out diagnostic);
    private string GetAutomationValueText() => UiDescriptorCatalog.GetAutomationValueText(this);
    public uint TextVersion => _textVersion;
    public uint LayoutVersion => _layoutVersion;
    internal uint OutputVersion => _outputVersion;
    internal event Action<string>? EffectivePropertyChanged;
    internal Delta.XAML.UiStyle? AppliedStyle => _appliedStyle;
    internal Delta.XAML.UiStyleState AppliedStyleState => _appliedStyleState;
    internal int AppliedStyleVersion => _appliedStyleVersion;
    internal Guid CompiledStyleId => _compiledStyleId;
    internal Delta.XAML.UiTemplateId CompiledTemplateId => _compiledTemplateId;
    public float LayoutScale => _layoutScale;
    public float DpiScale => _dpiScale;
    public void SetLayoutScale(float scale) => ApplyLayoutScale(scale);

    internal void ApplyLayoutScale(float scale)
    {
        if (Math.Abs(_dpiScale - scale) > float.Epsilon)
        {
            _dpiScale = scale;
            _dpiVersion++;
            _outputVersion++;
            _layoutVersion++;
            DirtyFlags |= UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual;
        }

        _layoutScale = scale;
    }
    internal uint TextRunVersion => _textVersion ^ (_dpiVersion << 1);

    internal IReadOnlyList<UiBindingSpec> BindingSpecs => _bindingSpecs;
    internal object? BindingContext => _bindingContext;
    internal bool HasExplicitBindingContext => _hasExplicitBindingContext;

    internal void AddBindingSpec(in UiBindingSpec spec) => _bindingSpecs.Add(spec);

    internal void AttachBinding(UiInterpretedBinding binding)
    {
        if (_externalBindingRuntimes.Remove(binding.PropertyName, out var externalPrevious))
        {
            externalPrevious.Dispose();
            _properties.Clear(binding.PropertyName, UiValueSource.Binding);
        }

        if (_compiledBindingRuntimes.Remove(binding.PropertyName, out var compiledPrevious))
        {
            compiledPrevious.Dispose();
            _properties.Clear(binding.PropertyName, UiValueSource.Binding);
        }

        if (_bindingRuntimes.Remove(binding.PropertyName, out var previous))
        {
            previous.Dispose();
        }

        _bindingRuntimes.Add(binding.PropertyName, binding);
        if (_bindingStageManaged)
        {
            binding.EnableStageManagement();
        }

        binding.Attach(this, BindingInvalidation(binding.PropertyName));
        binding.SetContext(_bindingContext);
    }

    internal void AttachExternalBinding(string propertyName, Delta.XAML.IUiBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(binding);
        if (_bindingRuntimes.Remove(propertyName, out var interpretedPrevious))
        {
            interpretedPrevious.Dispose();
            _properties.Clear(propertyName, UiValueSource.Binding);
        }

        if (_externalBindingRuntimes.Remove(propertyName, out var externalPrevious))
        {
            externalPrevious.Dispose();
            _properties.Clear(propertyName, UiValueSource.Binding);
        }

        if (_compiledBindingRuntimes.Remove(propertyName, out var compiledPrevious))
        {
            compiledPrevious.Dispose();
            _properties.Clear(propertyName, UiValueSource.Binding);
        }

        var runtime = new UiExternalBindingRuntime(
            this,
            propertyName,
            UiPropertyKeys.Resolve(propertyName),
            BindingInvalidation(propertyName),
            binding);
        _externalBindingRuntimes.Add(propertyName, runtime);
        if (_bindingStageManaged)
        {
            runtime.EnableStageManagement();
        }

        runtime.Attach();
    }

    internal void AttachCompiledBinding<TSource, TValue>(
        string propertyName,
        UiPropertyKey property,
        UiDirtyFlags invalidation,
        Delta.XAML.UiCompiledBinding<TSource, TValue> binding,
        bool sourceNotificationsManaged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(binding);
        if (_externalBindingRuntimes.Remove(propertyName, out var externalPrevious))
        {
            externalPrevious.Dispose();
            _properties.Clear(propertyName, UiValueSource.Binding);
        }

        if (_bindingRuntimes.Remove(propertyName, out var interpretedPrevious))
        {
            interpretedPrevious.Dispose();
            _properties.Clear(propertyName, UiValueSource.Binding);
        }

        if (_compiledBindingRuntimes.Remove(propertyName, out var previous))
        {
            previous.Dispose();
        }

        var runtime = new UiCompiledBindingRuntime<TSource, TValue>(
            this,
            propertyName,
            property,
            invalidation,
            binding,
            sourceNotificationsManaged);
        if (binding.Mode == Delta.XAML.UiBindingMode.OneTime)
        {
            runtime.Attach();
            runtime.Dispose();
            return;
        }

        _compiledBindingRuntimes.Add(propertyName, runtime);
        runtime.Attach();
    }

    internal void ApplyCompiledBinding(
        string propertyName,
        UiPropertyKey property,
        object? value,
        UiDirtyFlags invalidation) =>
        _properties.SetBindingValue(propertyName, property, value, invalidation);

    internal void ApplyExternalBinding(
        string propertyName,
        UiPropertyKey property,
        object? value,
        UiDirtyFlags invalidation) =>
        _properties.SetBindingValue(propertyName, property, Delta.XAML.UiElement.ToRetainedValue(value), invalidation);

    internal void ApplyTemplateOwnerBindings()
    {
        foreach (var binding in _externalBindingRuntimes.Values)
        {
            if (binding.IsTemplateOwnerRelation)
            {
                binding.ApplyPending();
            }
        }
    }

    internal void QueueCompiledBindingRefresh(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (_compiledBindingRuntimes.TryGetValue(propertyName, out var binding))
        {
            binding.QueueRefresh();
        }
    }

    internal bool HasBinding(string propertyName) =>
        _bindingRuntimes.ContainsKey(propertyName) ||
        _externalBindingRuntimes.ContainsKey(propertyName) ||
        _compiledBindingRuntimes.ContainsKey(propertyName);

    internal UiElement RootElement
    {
        get
        {
            var root = this;
            while (root.Parent is { } parent)
            {
                root = parent;
            }

            return root;
        }
    }

    internal void AttachNodeStore(UiNodeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (_nodeStore is { } current && !ReferenceEquals(current, store))
        {
            throw new InvalidOperationException("A retained root can have only one authoritative node store.");
        }

        _nodeStore = store;
        _detachedParent = null;
        _detachedChildren.Clear();
        _relationVersion++;
    }

    internal void PrepareNodeStoreDetachment(UiNodeStore store)
    {
        if (!ReferenceEquals(_nodeStore, store))
        {
            throw new InvalidOperationException("The element is not owned by this node store.");
        }

        _detachedParent = null;
        _detachedChildren.Clear();
    }

    internal void AddDetachedChild(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _detachedChildren.Add(child);
        child._detachedParent = this;
    }

    internal void CompleteNodeStoreDetachment(UiNodeStore store)
    {
        if (ReferenceEquals(_nodeStore, store))
        {
            _nodeStore = null;
            _relationVersion++;
        }
    }

    internal void ApplyBindingStage()
    {
        foreach (var binding in _bindingRuntimes.Values)
        {
            binding.ApplyPending();
        }

        foreach (var binding in _externalBindingRuntimes.Values)
        {
            binding.ApplyPending();
        }

        foreach (var binding in _compiledBindingRuntimes.Values)
        {
            binding.ApplyPending();
        }
    }

    internal bool NeedsBindingStage => !_bindingStageManaged || (DirtyFlags & (UiDirtyFlags.Binding | UiDirtyFlags.BindingSubtree)) != 0;
    internal void CompleteBindingStage() => DirtyFlags &= ~(UiDirtyFlags.Binding | UiDirtyFlags.BindingSubtree);

    internal void EnableBindingStage()
    {
        _bindingStageManaged = true;
        foreach (var binding in _bindingRuntimes.Values)
        {
            binding.EnableStageManagement();
        }

        foreach (var binding in _externalBindingRuntimes.Values)
        {
            binding.EnableStageManagement();
        }
    }

    internal void NotifyBindingTargetChanged(string propertyName, object? value)
    {
        if (_bindingRuntimes.TryGetValue(propertyName, out var binding))
        {
            binding.WriteTarget(value);
        }

        if (_externalBindingRuntimes.TryGetValue(propertyName, out var externalBinding))
        {
            externalBinding.WriteTarget(value);
        }

        if (_compiledBindingRuntimes.TryGetValue(propertyName, out var compiledBinding))
        {
            compiledBinding.TryWrite(value, out _);
        }
    }

    internal void NotifyEffectivePropertyChanged(string propertyName) => EffectivePropertyChanged?.Invoke(propertyName);

    internal Type BindingTargetType(string propertyName) => UiPropertyKeys.ValueType(this, propertyName);

    protected void SetDefaultProperty(string name, object? value, UiDirtyFlags invalidation) =>
        _properties.InitializeDefault(name, value, invalidation);
    protected void SetLocalProperty(string name, object? value, UiDirtyFlags invalidation) =>
        _properties.SetLocal(name, value, invalidation);

    internal void SetAppliedStyle(Delta.XAML.UiStyle style, Delta.XAML.UiStyleState state, int version)
    {
        ArgumentNullException.ThrowIfNull(style);
        _appliedStyle = style;
        _appliedStyleState = state;
        _appliedStyleVersion = version;
    }

    internal void ClearAppliedStyle()
    {
        _appliedStyle = null;
        _appliedStyleState = default;
        _appliedStyleVersion = -1;
    }

    internal void ClearStyleValue(string name) => _properties.Clear(name, UiValueSource.Style);

    internal void SetCompiledTemplate(Delta.XAML.UiTemplateId template)
    {
        if (!template.IsValid || _compiledTemplateId == template)
        {
            return;
        }

        _compiledTemplateId = template;
        InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
    }

    internal void SetCompiledStyle(Guid style)
    {
        if (style == Guid.Empty || _compiledStyleId == style)
        {
            return;
        }

        _compiledStyleId = style;
        InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
    }

    internal void DisposeRuntime()
    {
        if (_runtimeDisposed)
        {
            return;
        }

        _runtimeDisposed = true;
        var traversal = new List<UiElement> { this };
        for (var i = 0; i < traversal.Count; i++)
        {
            var element = traversal[i];
            element._properties.Dispose();
            foreach (var binding in element._bindingRuntimes.Values)
            {
                binding.Dispose();
            }

            foreach (var binding in element._externalBindingRuntimes.Values)
            {
                binding.Dispose();
            }

            foreach (var binding in element._compiledBindingRuntimes.Values)
            {
                binding.Dispose();
            }

            element._bindingRuntimes.Clear();
            element._externalBindingRuntimes.Clear();
            element._compiledBindingRuntimes.Clear();
            for (var childIndex = 0; childIndex < element.Children.Count; childIndex++)
            {
                traversal.Add(element.Children[childIndex]);
            }
        }
    }

    private static UiDirtyFlags BindingInvalidation(string propertyName) => propertyName switch
    {
        "Text" or "FontKey" or "FontSize" => UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "Width" or "Height" or "Padding" => UiDirtyFlags.Measure | UiDirtyFlags.Visual,
        "Foreground" or "OutlineColor" or "OutlineWidth" or "TextEffect" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "BorderColor" or "BorderWidth" or "CornerRadius" => UiDirtyFlags.Visual,
        _ => UiDirtyFlags.Visual,
    };

    internal void SetBindingContext(object? value, bool explicitValue)
    {
        if (_nodeStore is { } store)
        {
            store.SetBindingContext(this, value, explicitValue);
            return;
        }

        var traversal = new List<UiElement> { this };
        for (var i = 0; i < traversal.Count; i++)
        {
            var element = traversal[i];
            var isOwner = i == 0;
            if (!isOwner && element._hasExplicitBindingContext)
            {
                continue;
            }

            element.ApplyBindingContext(value, isOwner && explicitValue);
            for (var childIndex = 0; childIndex < element._detachedChildren.Count; childIndex++)
            {
                traversal.Add(element._detachedChildren[childIndex]);
            }
        }
    }

    internal void ApplyBindingContext(object? value, bool explicitValue)
    {
        _bindingContext = value;
        if (explicitValue)
        {
            _hasExplicitBindingContext = true;
        }

        foreach (var binding in _bindingRuntimes.Values)
        {
            binding.SetContext(value);
        }
    }
}

internal class Panel : UiElement
{
    private PanelState _state;

    internal ref PanelState State => ref _state;

    public Panel(string typeName = "Panel") : base(typeName) { }
}

internal sealed class StackPanel : UiElement
{
    private StackPanelState _state = new() { Orientation = UiOrientation.Vertical };

    internal ref StackPanelState State => ref _state;

    public StackPanel() : base("StackPanel") => SetDefaultProperty("Orientation", _state.Orientation, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    public UiOrientation Orientation
    {
        get => _state.Orientation;
        set => SetLocalProperty("Orientation", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

}

internal sealed class ItemsControl : UiElement
{
    private StackPanelState _layoutState = new() { Orientation = UiOrientation.Vertical };
    private ItemsControlState _state;
    private readonly List<object?> _items = new();
    private readonly List<UiElement> _realized = new();
    private readonly List<UiElement> _nextRealized = new();

    internal ref StackPanelState LayoutState => ref _layoutState;
    internal ref ItemsControlState State => ref _state;

    public ItemsControl() : base("ItemsControl") { }
    public IReadOnlyList<object?> Items => _items;
    public IReadOnlyList<UiElement> RealizedItems => _realized;

    public void SetItems(IReadOnlyList<object?> items, Func<object?, UiElement> factory)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(factory);

        var source = new ItemSource(items);
        var previous = new ItemSource(_items);
        var adapter = new ItemFactoryAdapter(factory);
        UiItemsControlGenerated.ApplyItems(ref _state, source, previous, adapter, _realized, _nextRealized);

        ClearChildren();
        _items.Clear();
        _items.AddRange(items);
        _realized.Clear();
        _realized.AddRange(_nextRealized);
        foreach (var child in _realized)
        {
            Add(child);
        }
    }

    private sealed class ItemSource(IReadOnlyList<object?> values) : IUiItemSource<object?>
    {
        public int Count => values.Count;
        public object? GetValue(int index) => values[index];
        public bool Matches(IUiItemSource<object?> previous, int index) => Equals(values[index], previous.GetValue(index));
    }

    private sealed class ItemFactoryAdapter(Func<object?, UiElement> factory) : IUiItemFactory<object?>
    {
        public UiElement Create(IUiItemSource<object?> source, int index)
        {
            var element = factory(source.GetValue(index));
            return element ?? throw new InvalidOperationException("The item factory returned a null UI element.");
        }
    }
}

internal sealed class Slider : UiElement
{
    private SliderState _state = new()
    {
        Maximum = 1,
        Step = 0.1,
        Orientation = UiOrientation.Horizontal,
    };

    internal ref SliderState State => ref _state;

    internal Slider() : base("Slider")
    {
        Focusable = true;
        AutomationRole = UiAutomationRole.Slider;
        SetDefaultProperty("Minimum", _state.Minimum, UiDirtyFlags.Visual);
        SetDefaultProperty("Maximum", _state.Maximum, UiDirtyFlags.Visual);
        SetDefaultProperty("Value", _state.Value, UiDirtyFlags.Binding | UiDirtyFlags.Visual);
        SetDefaultProperty("Step", _state.Step, UiDirtyFlags.Visual);
        SetDefaultProperty("Orientation", _state.Orientation, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

    internal double Minimum { get => _state.Minimum; set => SetLocalProperty("Minimum", value, UiDirtyFlags.Visual); }
    internal double Maximum { get => _state.Maximum; set => SetLocalProperty("Maximum", value, UiDirtyFlags.Visual); }
    internal double Value { get => _state.Value; set => SetLocalProperty("Value", value, UiDirtyFlags.Binding | UiDirtyFlags.Visual); }
    internal double Step { get => _state.Step; set => SetLocalProperty("Step", value, UiDirtyFlags.Visual); }
    internal UiOrientation Orientation { get => _state.Orientation; set => SetLocalProperty("Orientation", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange); }

    internal void SetUserValue(double value)
    {
        value = Math.Clamp(value, _state.Minimum, _state.Maximum);
        if (HasBinding(nameof(Value)))
        {
            if (_state.Value.Equals(value))
            {
                return;
            }

            _state.Value = value;
            InvalidateChanged(UiDirtyFlags.Binding | UiDirtyFlags.Visual);
        }
        else
        {
            Value = value;
        }

        NotifyBindingTargetChanged(nameof(Value), value);
    }
}

internal sealed class Image : UiElement
{
    private ImageState _state = new() { Tint = new(255, 255, 255, 255), Stretch = (byte)Delta.XAML.UiImageStretch.Uniform };

    internal ref ImageState State => ref _state;

    internal Image() : base("Image") => AutomationRole = UiAutomationRole.Image;

    internal Guid Source
    {
        get => _state.Resource;
        set
        {
            if (_state.Resource == value)
            {
                return;
            }

            _state.Resource = value;
            InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        }
    }

    internal UiColor Tint
    {
        get => _state.Tint;
        set
        {
            if (_state.Tint == value)
            {
                return;
            }

            _state.Tint = value;
            InvalidateChanged(UiDirtyFlags.Visual);
        }
    }

    internal byte Stretch
    {
        get => _state.Stretch;
        set
        {
            if (_state.Stretch == value)
            {
                return;
            }

            _state.Stretch = value;
            InvalidateChanged(UiDirtyFlags.Visual);
        }
    }

    internal Guid Placeholder
    {
        get => _state.Placeholder;
        set
        {
            if (_state.Placeholder == value)
            {
                return;
            }

            _state.Placeholder = value;
            InvalidateChanged(UiDirtyFlags.Visual);
        }
    }

    internal Guid ErrorSource
    {
        get => _state.ErrorSource;
        set
        {
            if (_state.ErrorSource == value)
            {
                return;
            }

            _state.ErrorSource = value;
            InvalidateChanged(UiDirtyFlags.Visual);
        }
    }

    internal Guid DisplaySource => _state.Status switch
    {
        (byte)Delta.XAML.UiImageStatus.Error when _state.ErrorSource != Guid.Empty => _state.ErrorSource,
        (byte)Delta.XAML.UiImageStatus.Loading when _state.Placeholder != Guid.Empty => _state.Placeholder,
        _ => _state.Resource,
    };

    internal UiRect ImageBounds
    {
        get
        {
            var width = MathF.Max(0, _state.IntrinsicWidth);
            var height = MathF.Max(0, _state.IntrinsicHeight);
            if ((Delta.XAML.UiImageStretch)_state.Stretch == Delta.XAML.UiImageStretch.Fill || width <= 0 || height <= 0)
            {
                return Bounds;
            }

            var scale = (Delta.XAML.UiImageStretch)_state.Stretch switch
            {
                Delta.XAML.UiImageStretch.None => 1,
                Delta.XAML.UiImageStretch.UniformToFill => MathF.Max(Bounds.Width / width, Bounds.Height / height),
                _ => MathF.Min(Bounds.Width / width, Bounds.Height / height),
            };
            var renderedWidth = width * scale;
            var renderedHeight = height * scale;
            return new(
                Bounds.X + (Bounds.Width - renderedWidth) * 0.5f,
                Bounds.Y + (Bounds.Height - renderedHeight) * 0.5f,
                renderedWidth,
                renderedHeight);
        }
    }

    internal void SetMetadata(float width, float height, byte status)
    {
        if (_state.IntrinsicWidth.Equals(width) && _state.IntrinsicHeight.Equals(height) && _state.Status == status)
        {
            return;
        }

        _state.IntrinsicWidth = MathF.Max(0, width);
        _state.IntrinsicHeight = MathF.Max(0, height);
        _state.Status = status;
        InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual);
    }
}

internal sealed class RichTextBlock : UiElement
{
    private RichTextState _state = new()
    {
        Spans = Array.Empty<Delta.XAML.UiTextSpan>(),
        HitRanges = Array.Empty<Delta.XAML.UiInlineHitRange>(),
    };

    internal RichTextBlock() : base("RichTextBlock") => AutomationRole = UiAutomationRole.Text;

    internal ref RichTextState State => ref _state;

    internal ReadOnlyMemory<Delta.XAML.UiTextSpan> Spans
    {
        get => _state.Spans;
        set
        {
            var source = value.Span;
            for (var i = 0; i < source.Length; i++)
            {
                ArgumentNullException.ThrowIfNull(source[i].Text);
                ArgumentException.ThrowIfNullOrWhiteSpace(source[i].FontKey);
                if (!float.IsFinite(source[i].FontSize) || source[i].FontSize <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Rich-text font sizes must be finite and positive.");
                }
            }

            if (source.SequenceEqual(_state.Spans))
            {
                return;
            }

            var shapingChanged = source.Length != _state.Spans.Length;
            if (!shapingChanged)
            {
                for (var i = 0; i < source.Length; i++)
                {
                    var previous = _state.Spans[i];
                    if (!string.Equals(source[i].Text, previous.Text, StringComparison.Ordinal) ||
                        !string.Equals(source[i].FontKey, previous.FontKey, StringComparison.Ordinal) ||
                        !source[i].FontSize.Equals(previous.FontSize))
                    {
                        shapingChanged = true;
                        break;
                    }
                }
            }

            _state.Spans = source.ToArray();
            InvalidateChanged(shapingChanged
                ? UiDirtyFlags.Measure | UiDirtyFlags.Text | UiDirtyFlags.Visual
                : UiDirtyFlags.Text | UiDirtyFlags.Visual);
        }
    }

    internal void SetHitRanges(Delta.XAML.UiInlineHitRange[] ranges, int count)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, ranges.Length);
        _state.HitRanges = ranges;
        _state.HitRangeCount = count;
    }

    internal string AutomationText
    {
        get
        {
            var builder = new StringBuilder();
            for (var i = 0; i < _state.Spans.Length; i++)
            {
                builder.Append(_state.Spans[i].Text);
            }

            return builder.ToString();
        }
    }

    internal bool TryGetLink(UiPoint point, out Delta.XAML.UiCommandId command, out string? argument)
        => RichTextHitTestMixin.TryGetLink(ref _state, point, out command, out argument);
}

internal sealed class Overlay : UiElement
{
    private OverlayState _state = new() { IsOpen = true };
    internal ref OverlayState State => ref _state;
    internal Overlay() : base("Overlay") => IsFocusScope = true;
    internal bool IsOpen
    {
        get => _state.IsOpen;
        set
        {
            if (_state.IsOpen == value)
            {
                return;
            }

            _state.IsOpen = value;
            SetParticipation(value ? Delta.XAML.UiParticipation.All : Delta.XAML.UiParticipation.None);
        }
    }
}

internal sealed class CollectionView : UiElement
{
    private CollectionViewState _state = new() { SelectedIndex = -1 };
    internal ref CollectionViewState State => ref _state;
    internal CollectionView() : base("CollectionView")
    {
        AutomationRole = UiAutomationRole.List;
        Focusable = true;
    }
    internal int SelectedIndex
    {
        get => _state.SelectedIndex;
        set
        {
            if (_state.SelectedIndex == value)
            {
                return;
            }

            _state.SelectedIndex = value;
            ApplySelection();
            InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual);
        }
    }

    internal bool SelectTarget(UiElement? target)
    {
        var items = ItemsHost;
        if (items is null || target is null)
        {
            return false;
        }

        var current = target;
        while (current.Parent is { } parent && !ReferenceEquals(parent, items))
        {
            current = parent;
        }

        if (!ReferenceEquals(current.Parent, items) || current.CollectionIndex < 0)
        {
            return false;
        }

        SelectedIndex = current.CollectionIndex;
        return true;
    }

    internal bool MoveSelection(int delta)
    {
        var items = ItemsHost;
        if (items is null || items.Children.Count == 0)
        {
            return false;
        }

        var first = items.Children[0].CollectionIndex;
        var last = items.Children[^1].CollectionIndex;
        var next = SelectedIndex < first || SelectedIndex > last
            ? delta < 0 ? last : first
            : Math.Clamp(SelectedIndex + delta, first, last);
        if (next == SelectedIndex)
        {
            return false;
        }

        SelectedIndex = next;
        return true;
    }

    private ItemsControl? ItemsHost =>
        Children.Count > 0 && Children[0] is ScrollViewer { Content: ItemsControl items } ? items : null;

    private void ApplySelection()
    {
        if (ItemsHost is not { } items)
        {
            return;
        }

        for (var i = 0; i < items.Children.Count; i++)
        {
            items.Children[i].IsSelected = items.Children[i].CollectionIndex == _state.SelectedIndex;
        }
    }
}

internal sealed class Picker : UiElement
{
    private PickerState _state = new() { SelectedIndex = -1 };
    internal ref PickerState State => ref _state;
    internal Picker() : base("Picker") => Focusable = true;
    internal int SelectedIndex
    {
        get => _state.SelectedIndex;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);

            if (_state.SelectedIndex == value)
            {
                return;
            }

            _state.SelectedIndex = value;
            if (ItemsCollection is { } items)
            {
                items.SelectedIndex = value;
            }

            InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual);
        }
    }
    internal bool IsOpen
    {
        get => _state.IsOpen;
        set
        {
            if (_state.IsOpen == value)
            {
                return;
            }

            _state.IsOpen = value;
            if (Children.Count > 1 && Children[1] is Overlay overlay)
            {
                overlay.IsOpen = value;
            }

            InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.HitTest);
        }
    }

    internal bool ProcessPointer(UiElement? target)
    {
        if (target is null)
        {
            return false;
        }

        if (Children.Count > 0 && IsWithin(target, Children[0]))
        {
            IsOpen = !IsOpen;
            return true;
        }

        if (ItemsCollection is { } items && items.SelectTarget(target))
        {
            SelectedIndex = items.SelectedIndex;
            IsOpen = false;
            return true;
        }

        return false;
    }

    internal bool MoveSelection(int delta)
    {
        if (ItemsCollection is not { } items || !items.MoveSelection(delta))
        {
            return false;
        }

        SelectedIndex = items.SelectedIndex;
        return true;
    }

    internal bool SynchronizeSelection()
    {
        if (ItemsCollection is not { } items || items.SelectedIndex == _state.SelectedIndex)
        {
            return false;
        }

        SelectedIndex = items.SelectedIndex;
        return true;
    }

    private CollectionView? ItemsCollection =>
        Children.Count > 1 && Children[1] is Overlay overlay && overlay.Children.Count > 0
            ? overlay.Children[0] as CollectionView
            : null;

    private static bool IsWithin(UiElement candidate, UiElement ancestor)
    {
        for (var current = candidate; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class TabView : UiElement
{
    private TabViewState _state = new() { SelectedIndex = -1 };
    internal ref TabViewState State => ref _state;
    internal TabView() : base("TabView") => AutomationRole = UiAutomationRole.Tab;
    internal int SelectedIndex { get => _state.SelectedIndex; set => _state.SelectedIndex = value; }
}

internal sealed class Menu : UiElement
{
    private MenuState _state = new() { Layout = new StackPanelState { Orientation = UiOrientation.Vertical } };
    internal ref MenuState State => ref _state;
    internal Menu() : base("Menu")
    {
        IsFocusScope = true;
        AutomationRole = UiAutomationRole.Menu;
    }
}

internal class Border : UiElement
{
    private BorderState _state;

    internal ref BorderState State => ref _state;

    public Border() : base("Border") { }
    public UiElement? Child => Children.Count == 0 ? null : Children[0];
}

internal readonly record struct GridLength(float Value, GridUnitType Unit) { public static GridLength Fixed(float v) => new(v, GridUnitType.Pixel); public static GridLength Auto => new(1, GridUnitType.Auto); public static GridLength Star(float weight = 1) => new(weight, GridUnitType.Star); }
internal enum GridUnitType { Pixel, Auto, Star }

internal sealed class Grid : UiElement
{
    private GridState _state;

    internal ref GridState State => ref _state;

    public Grid() : base("Grid")
    {
        _state.Columns = Array.Empty<GridLength>();
        _state.Rows = Array.Empty<GridLength>();
        _state.MeasuredColumns = Array.Empty<float>();
        _state.MeasuredRows = Array.Empty<float>();
        _state.ResolvedColumns = new float[1];
        _state.ResolvedRows = new float[1];
        SetDefaultProperty("Columns", _state.Columns, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
        SetDefaultProperty("Rows", _state.Rows, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

    public int ColumnCount => _state.Columns.Length;
    public int RowCount => _state.Rows.Length;
    public void SetColumns(params GridLength[] columns)
        => SetLocalProperty("Columns", columns, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);

    public void SetRows(params GridLength[] rows)
        => SetLocalProperty("Rows", rows, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);

}

internal class ContentControl : UiElement
{
    private ContentControlState _state;

    internal ref ContentControlState State => ref _state;

    public ContentControl(string typeName = "ContentControl") : base(typeName) { }
    public UiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
}

internal class Button : UiElement
{
    private ContentControlState _contentState;
    private ButtonState _state;

    internal ref ContentControlState ContentState => ref _contentState;
    internal ref ButtonState State => ref _state;
    internal ref ButtonState InputState => ref _state;

    public Button(string typeName = "Button") : base(typeName) { Focusable = true; AutomationRole = UiAutomationRole.Button; }
    public UiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
    public event EventHandler? Click;
    internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);
}

internal sealed class ToggleButton : UiElement
{
    private ContentControlState _contentState;
    private ButtonState _buttonState;
    private ToggleButtonState _state;

    internal ref ContentControlState ContentState => ref _contentState;
    internal ref ButtonState InputState => ref _buttonState;
    internal ref ToggleButtonState State => ref _state;

    public ToggleButton() : base("ToggleButton") { Focusable = true; AutomationRole = UiAutomationRole.Button; }

    public bool IsChecked => _state.IsChecked;
    public UiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
    public event EventHandler? Click;
    internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);
}

internal class TextBlock : UiElement
{
    private TextBlockState _state;

    internal ref TextBlockState State => ref _state;

    public TextBlock(string typeName = "TextBlock") : base(typeName)
    {
        AutomationRole = UiAutomationRole.Text;
        _state.Text = string.Empty;
        _state.Visual.FontKey = "default";
        _state.Visual.GlyphRunKey = "default";
        _state.Visual.FontSize = 14;
        _state.Visual.Foreground = new(255, 255, 255);
        SetDefaultProperty("Text", _state.Text, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text);
        SetDefaultProperty("FontKey", _state.Visual.FontKey, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text);
        SetDefaultProperty("FontSize", _state.Visual.FontSize, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text);
        SetDefaultProperty("Foreground", _state.Visual.Foreground, UiDirtyFlags.Visual | UiDirtyFlags.Text);
    }

    public string Text { get => _state.Text; set { ArgumentNullException.ThrowIfNull(value); SetLocalProperty("Text", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public string FontKey { get => _state.Visual.FontKey; set { ArgumentNullException.ThrowIfNull(value); SetLocalProperty("FontKey", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public string GlyphRunKey { get => _state.Visual.GlyphRunKey; set { ArgumentNullException.ThrowIfNull(value); if (_state.Visual.GlyphRunKey == value) { return; } _state.Visual.GlyphRunKey = value; InvalidateChanged(UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public float FontSize { get => _state.Visual.FontSize; set => SetLocalProperty("FontSize", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor Foreground { get => _state.Visual.Foreground; set => SetLocalProperty("Foreground", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor OutlineColor { get => _state.Visual.OutlineColor; set => SetLocalProperty("OutlineColor", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public float OutlineWidth
    {
        get => _state.Visual.OutlineWidth;
        set
        {
            if (!float.IsFinite(value) || value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            SetLocalProperty("OutlineWidth", value, UiDirtyFlags.Visual | UiDirtyFlags.Text);
        }
    }
    public Guid TextEffectResource => _state.Visual.TextEffectResource;
    public UiResourceId TextEffect
    {
        get => new(_state.Visual.TextEffectResource);
        set => SetLocalProperty("TextEffect", value, UiDirtyFlags.Visual | UiDirtyFlags.Text);
    }

}

internal class TextBox : UiElement, ITextEditorStateOwner
{
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private TextBlockState _textState;
    private TextBoxState _state;
    private string? _diagnostic;

    internal ref TextBlockState TextState => ref _textState;
    internal ref TextBoxState State => ref _state;
    ref TextBlockState ITextEditorStateOwner.TextState => ref _textState;
    ref TextBoxState ITextEditorStateOwner.EditorState => ref _state;
    UiElement ITextEditorStateOwner.Element => this;
    List<string> ITextEditorStateOwner.UndoHistory => _undo;
    List<string> ITextEditorStateOwner.RedoHistory => _redo;
    string? ITextEditorStateOwner.ValidationDiagnostic { get => _diagnostic; set => _diagnostic = value; }

    public TextBox() : base("TextBox")
    {
        Focusable = true;
        AutomationRole = UiAutomationRole.TextBox;
        TextEditorBehaviorMixin.Initialize(this);
    }

    public string Text { get => _textState.Text; set => SetText(value, false); }
    public string FontKey { get => _textState.Visual.FontKey; set { ArgumentNullException.ThrowIfNull(value); SetLocalProperty("FontKey", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public string GlyphRunKey { get => _textState.Visual.GlyphRunKey; set { ArgumentNullException.ThrowIfNull(value); if (_textState.Visual.GlyphRunKey == value) { return; } _textState.Visual.GlyphRunKey = value; InvalidateChanged(UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public float FontSize { get => _textState.Visual.FontSize; set => SetLocalProperty("FontSize", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor Foreground { get => _textState.Visual.Foreground; set => SetLocalProperty("Foreground", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor OutlineColor { get => _textState.Visual.OutlineColor; set => SetLocalProperty("OutlineColor", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public float OutlineWidth
    {
        get => _textState.Visual.OutlineWidth;
        set
        {
            if (!float.IsFinite(value) || value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            SetLocalProperty("OutlineWidth", value, UiDirtyFlags.Visual | UiDirtyFlags.Text);
        }
    }
    public Guid TextEffectResource => _textState.Visual.TextEffectResource;
    public UiResourceId TextEffect
    {
        get => new(_textState.Visual.TextEffectResource);
        set => SetLocalProperty("TextEffect", value, UiDirtyFlags.Visual | UiDirtyFlags.Text);
    }
    public int CaretIndex => _state.CaretIndex;
    public int SelectionStart => _state.SelectionStart;
    public int SelectionLength => _state.SelectionLength;
    public string? Diagnostic => _diagnostic;
    internal string VisualText => _state.CompositionDisplayText ?? Text;
    internal bool IsComposing => _state.CompositionDisplayText is not null;
    internal string? CompositionText => _state.CompositionText;
    internal int CompositionSelectionStart => _state.CompositionSelectionStart;
    internal int CompositionSelectionLength => _state.CompositionSelectionLength;
    public IUiClipboard? Clipboard { get; set; }
    public event EventHandler<TextChangedEventArgs>? TextChanged;

    public void SetText(string text, bool recordUndo = true) => TextEditorBehaviorMixin.SetText(this, text, recordUndo);
    public bool ApplyText(in UiTextInput input) => TextEditorBehaviorMixin.ApplyText(this, in input);
    public bool ApplyComposition(in UiCompositionEvent input) => TextEditorBehaviorMixin.ApplyComposition(this, in input);
    public bool ApplyKey(in UiKeyEvent input) => TextEditorBehaviorMixin.ApplyKey(this, in input);
    public void SelectAll() => TextEditorBehaviorMixin.SelectAll(this);
    public void Copy() => TextEditorBehaviorMixin.Copy(this);
    public void Cut() => TextEditorBehaviorMixin.Cut(this);
    public bool Paste() => TextEditorBehaviorMixin.Paste(this);
    public bool Undo() => TextEditorBehaviorMixin.Undo(this);
    public bool Redo() => TextEditorBehaviorMixin.Redo(this);
    void ITextEditorStateOwner.RaiseTextChanged(string text) => TextChanged?.Invoke(this, new TextChangedEventArgs(text));
}

internal sealed class NumericEditor : UiElement, ITextEditorStateOwner
{
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private TextBlockState _textState;
    private TextBoxState _editorState;
    private NumericEditorState _state = new() { Min = double.MinValue, Max = double.MaxValue, CommittedText = string.Empty };
    private string? _diagnostic;

    internal ref TextBlockState TextState => ref _textState;
    internal ref TextBoxState EditorState => ref _editorState;
    internal ref NumericEditorState State => ref _state;
    ref TextBlockState ITextEditorStateOwner.TextState => ref _textState;
    ref TextBoxState ITextEditorStateOwner.EditorState => ref _editorState;
    UiElement ITextEditorStateOwner.Element => this;
    List<string> ITextEditorStateOwner.UndoHistory => _undo;
    List<string> ITextEditorStateOwner.RedoHistory => _redo;
    string? ITextEditorStateOwner.ValidationDiagnostic { get => _diagnostic; set => _diagnostic = value; }

    public NumericEditor() : base("NumericEditor")
    {
        Focusable = true;
        AutomationRole = UiAutomationRole.NumericEditor;
        TextEditorBehaviorMixin.Initialize(this);
        SetDefaultProperty("Value", _state.Value, UiDirtyFlags.Binding | UiDirtyFlags.Visual);
        SetDefaultProperty("Minimum", _state.Min, UiDirtyFlags.Visual);
        SetDefaultProperty("Maximum", _state.Max, UiDirtyFlags.Visual);
    }

    public string Text { get => _textState.Text; set => SetText(value, false); }
    public string FontKey { get => _textState.Visual.FontKey; set { ArgumentNullException.ThrowIfNull(value); SetLocalProperty("FontKey", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public string GlyphRunKey { get => _textState.Visual.GlyphRunKey; set { ArgumentNullException.ThrowIfNull(value); if (_textState.Visual.GlyphRunKey == value) { return; } _textState.Visual.GlyphRunKey = value; InvalidateChanged(UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public float FontSize { get => _textState.Visual.FontSize; set => SetLocalProperty("FontSize", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor Foreground { get => _textState.Visual.Foreground; set => SetLocalProperty("Foreground", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor OutlineColor { get => _textState.Visual.OutlineColor; set => SetLocalProperty("OutlineColor", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public float OutlineWidth
    {
        get => _textState.Visual.OutlineWidth;
        set
        {
            if (!float.IsFinite(value) || value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            SetLocalProperty("OutlineWidth", value, UiDirtyFlags.Visual | UiDirtyFlags.Text);
        }
    }
    public Guid TextEffectResource => _textState.Visual.TextEffectResource;
    public UiResourceId TextEffect
    {
        get => new(_textState.Visual.TextEffectResource);
        set => SetLocalProperty("TextEffect", value, UiDirtyFlags.Visual | UiDirtyFlags.Text);
    }
    public int CaretIndex => _editorState.CaretIndex;
    public int SelectionStart => _editorState.SelectionStart;
    public int SelectionLength => _editorState.SelectionLength;
    public string? Diagnostic => _diagnostic;
    internal string VisualText => _editorState.CompositionDisplayText ?? Text;
    internal bool IsComposing => _editorState.CompositionDisplayText is not null;
    internal string? CompositionText => _editorState.CompositionText;
    internal int CompositionSelectionStart => _editorState.CompositionSelectionStart;
    internal int CompositionSelectionLength => _editorState.CompositionSelectionLength;
    public IUiClipboard? Clipboard { get; set; }
    public event EventHandler<TextChangedEventArgs>? TextChanged;
    public double Value => _state.Value;
    public double Min { get => _state.Min; set => SetLocalProperty("Minimum", value, UiDirtyFlags.Visual); }
    public double Max { get => _state.Max; set => SetLocalProperty("Maximum", value, UiDirtyFlags.Visual); }
    public bool HasValidationError => _diagnostic is not null;
    public bool IsDirty => Text != _state.CommittedText;

    public void SetText(string text, bool recordUndo = true) => TextEditorBehaviorMixin.SetText(this, text, recordUndo);
    public bool ApplyText(in UiTextInput input) => TextEditorBehaviorMixin.ApplyText(this, in input);
    public bool ApplyComposition(in UiCompositionEvent input) => TextEditorBehaviorMixin.ApplyComposition(this, in input);
    public void SelectAll() => TextEditorBehaviorMixin.SelectAll(this);
    public void Copy() => TextEditorBehaviorMixin.Copy(this);
    public void Cut() => TextEditorBehaviorMixin.Cut(this);
    public bool Paste() => TextEditorBehaviorMixin.Paste(this);
    public bool Undo() => TextEditorBehaviorMixin.Undo(this);
    public bool Redo() => TextEditorBehaviorMixin.Redo(this);
    public void Initialize(double value) => SetLocalProperty("Value", value, UiDirtyFlags.Binding | UiDirtyFlags.Visual);
    public bool TryCommitText(string text) { SetText(text); return TryCommit(); }
    public bool Increment(double step = 1) => Adjust(step);
    public bool Decrement(double step = 1) => Adjust(-step);

    public bool ApplyKey(in UiKeyEvent input)
    {
        return UiNumericEditorGenerated.ProcessKey(ref _state, in input, Text.Length) switch
        {
            UiTextEditAction.Increment => Increment(),
            UiTextEditAction.Decrement => Decrement(),
            _ => TextEditorBehaviorMixin.ApplyKey(this, in input),
        };
    }

    public bool TryCommit()
    {
        if (!UiNumericEditorGenerated.TryCommit(ref _state, Text, Min, Max, out var formatted, out _diagnostic))
        {
            SetInvalid(true);
            return false;
        }

        SetText(formatted, false);
        _diagnostic = null;
        SetInvalid(false);
        return true;
    }

    public void CancelEdit()
    {
        SetText(_state.CommittedText ?? string.Empty, false);
        _diagnostic = null;
        SetInvalid(false);
    }

    public bool TryApplyValue(string text, out string? error)
    {
        if (UiNumericEditorGenerated.TryCommit(ref _state, text, Min, Max, out var formatted, out _diagnostic))
        {
            SetText(formatted, false);
            _diagnostic = null;
            SetInvalid(false);
            error = null;
            return true;
        }

        SetInvalid(true);
        error = _diagnostic;
        return false;
    }

    private bool Adjust(double delta)
    {
        if (!UiNumericEditorGenerated.TryAdjust(ref _state, delta, out _diagnostic))
        {
            SetInvalid(true);
            return false;
        }

        _state.CommittedText = UiNumericEditorGenerated.Format(_state.Value);
        SetText(_state.CommittedText, false);
        _diagnostic = null;
        SetInvalid(false);
        return true;
    }

    void ITextEditorStateOwner.RaiseTextChanged(string text) => TextChanged?.Invoke(this, new TextChangedEventArgs(text));
}

internal class ScrollViewer : UiElement
{
    private ScrollViewerState _state;

    internal ref ScrollViewerState State => ref _state;

    public ScrollViewer() : base("ScrollViewer") { }
    public UiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
    public UiPoint Offset => _state.Offset;
    public void ScrollBy(float x, float y)
    {
        if (UiScrollViewerGenerated.TryScrollBy(ref _state, x, y))
        {
            InvalidateChanged(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }

}
