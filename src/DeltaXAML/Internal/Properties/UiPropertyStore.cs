using System.Diagnostics.CodeAnalysis;
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
