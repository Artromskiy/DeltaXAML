using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Delta.XAML.Contract;

using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal sealed class UiValue
{
    public UiValue(object? value, UiValueSource source, UiDirtyFlags invalidation) { UntypedValue = value; Source = source; Invalidation = invalidation; }
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
    private readonly UiElement _owner;
    private bool _disposed;
    private static readonly UiValueSource[] Precedence =
    [
        UiValueSource.Animation,
        UiValueSource.Handle,
        UiValueSource.Local,
        UiValueSource.Binding,
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
        _bindings.Clear();
    }
    public void InitializeDefault(string name, object? value, UiDirtyFlags invalidation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var slots = GetSlots(name);
        var defaultValue = new UiValue(value, UiValueSource.Default, invalidation);
        slots.DefaultValue = defaultValue;
        _values[name] = defaultValue;
    }
    public void SetDefault(string name, object? value, UiDirtyFlags invalidation) => SetSource(name, new(value, UiValueSource.Default, invalidation));
    public void SetLocal(string name, object? value, UiDirtyFlags invalidation) => SetSource(name, new(value, UiValueSource.Local, invalidation));
    public void SetStyle(string name, object? value, UiDirtyFlags invalidation)
    {
        RemoveResourceBinding(name);
        SetSource(name, new(value, UiValueSource.Style, invalidation));
    }
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
        SetSource(name, new(ResolveResource(binding), UiValueSource.Style, invalidation));
    }
    public void SetBinding(string name, UiBindingValue binding, UiDirtyFlags invalidation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(binding);
        RemoveBinding(name);
        _bindings[name] = binding;
        var slots = GetSlots(name);
        slots.BindingValue = new UiValue(binding.Read(), UiValueSource.Binding, invalidation);
        ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
        EventHandler handler = (_, _) =>
        {
            slots.BindingValue = new UiValue(binding.Read(), UiValueSource.Binding, invalidation);
            ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
        };
        _bindingHandlers[name] = handler;
        binding.Changed += handler;
    }

    public void SetBindingValue(string name, UiPropertyKey property, object? value, UiDirtyFlags invalidation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var slots = GetSlots(name);
        slots.BindingValue = new UiValue(value, UiValueSource.Binding, invalidation);
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
            SetValue(slots, source, null);
            ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
        }
    }
    public bool TryGet(string name, [NotNullWhen(true)] out UiValue? value) { ArgumentException.ThrowIfNullOrWhiteSpace(name); return _values.TryGetValue(name, out value); }
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
    private void RemoveBinding(string name)
    {
        if (_bindings.Remove(name, out var binding) && _bindingHandlers.Remove(name, out var handler))
        {
            binding.Changed -= handler;
        }
        if (_slots.TryGetValue(name, out var slots)) { slots.BindingValue = null; }
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
        ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
    }
    private static void SetValue(SourceSlots slots, UiValueSource source, UiValue? value)
    {
        switch (source)
        {
            case UiValueSource.Default: slots.DefaultValue = value; break;
            case UiValueSource.Style: slots.StyleValue = value; break;
            case UiValueSource.Binding: slots.BindingValue = value; break;
            case UiValueSource.Local: slots.LocalValue = value; break;
            case UiValueSource.Handle: slots.HandleValue = value; break;
            case UiValueSource.Animation: slots.AnimationValue = value; break;
        }
    }
    private SourceSlots GetSlots(string name) => _slots.TryGetValue(name, out var slots) ? slots : AddSlots(name);
    private SourceSlots AddSlots(string name) { var slots = new SourceSlots(); _slots.Add(name, slots); return slots; }
    private void ApplyEffective(string name, SourceSlots slots, UiPropertyKey property)
    {
        _values.TryGetValue(name, out var previous);
        var effective = Resolve(slots);
        if (Same(previous, effective))
        {
            if (effective is null)
            {
                _values.Remove(name);
            }
            else
            {
                _values[name] = effective;
            }

            return;
        }
        while (effective is not null && !UiDescriptorCatalog.TrySetProperty(_owner, property, effective))
        {
            SetValue(slots, effective.Source, null);
            effective = Resolve(slots);
        }

        if (Same(previous, effective))
        {
            return;
        }

        var invalidation = effective?.Invalidation ?? previous?.Invalidation ?? UiDirtyFlags.Visual;
        _owner.InvalidateChanged(invalidation);
        if (effective is null)
        {
            _values.Remove(name);
        }
        else
        {
            _values[name] = effective;
        }
    }
    private static UiValue? Resolve(SourceSlots slots)
    {
        foreach (var source in Precedence)
        {
            var value = source switch
            {
                UiValueSource.Animation => slots.AnimationValue,
                UiValueSource.Handle => slots.HandleValue,
                UiValueSource.Local => slots.LocalValue,
                UiValueSource.Binding => slots.BindingValue,
                UiValueSource.Style => slots.StyleValue,
                UiValueSource.Default => slots.DefaultValue,
                _ => null,
            };
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }
    private object? ResolveResource(ResourceBinding binding) => binding.Resources.TryResolve(binding.Reference, out var value, out _) ? value : null;
    private void ApplyResourceBinding(string name, ResourceBinding binding)
    {
        var slots = GetSlots(name);
        slots.StyleValue = new UiValue(ResolveResource(binding), UiValueSource.Style, binding.Invalidation);
        ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
    }
    private static bool Same(UiValue? left, UiValue? right) =>
        (left is null && right is null) ||
        (left is not null && right is not null &&
         left.Source == right.Source && Equals(left.UntypedValue, right.UntypedValue));
    private sealed class SourceSlots
    {
        public UiValue? DefaultValue, StyleValue, BindingValue, LocalValue, HandleValue, AnimationValue;
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
    private UiElementState _state = new() { Width = float.NaN, Height = float.NaN, IsEnabled = true };
    private object? _bindingContext;
    private bool _hasExplicitBindingContext;
    private bool _bindingStageManaged;
    private float _layoutScale = 1f;
    private float _dpiScale = 1f;
    private uint _layoutVersion;
    private uint _dpiVersion;
    private uint _textVersion;
    private uint _treeVersion;
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
        _properties.InitializeDefault("Padding", _state.Padding, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        _properties.InitializeDefault("Fill", _state.Fill, UiDirtyFlags.Visual);
        _properties.InitializeDefault("IsEnabled", _state.IsEnabled, UiDirtyFlags.Visual);
        _properties.InitializeDefault("IsSelected", _state.IsSelected, UiDirtyFlags.Visual);
    }
    public UiElementId Id { get; }
    public uint Generation { get; }
    internal uint TreeVersion => _treeVersion;
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
    internal bool HasCustomVisual => _state.CustomVisualType != Guid.Empty;
    internal Guid CustomVisualTypeId => _state.CustomVisualType;
    internal Guid CustomVisualResourceId => _state.CustomVisualResource;
    internal UiColor CustomVisualColor => _state.CustomVisualColor;
    public bool IsEnabled { get => _state.IsEnabled; set => SetLocalProperty("IsEnabled", value, UiDirtyFlags.Visual); }
    public bool IsHovered { get; private set; }
    public bool IsPressed => UiDescriptorCatalog.IsPressed(this);
    public bool IsSelected { get => _state.IsSelected; set => SetLocalProperty("IsSelected", value, UiDirtyFlags.Visual); }
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
    internal int DisplayClipCount => _displayClipCount;
    internal int DisplayVisualCount => _displayVisualCount;
    internal int DisplayTextCount => _displayTextCount;
    internal void SetDisplayRange(int clipIndex, int visualIndex, int textIndex)
    {
        _displayClipIndex = clipIndex;
        _displayVisualIndex = visualIndex;
        _displayTextIndex = textIndex;
    }
    internal void SetDisplaySubtreeCounts(int clips, int visuals, int text)
    {
        _displayClipCount = clips;
        _displayVisualCount = visuals;
        _displayTextCount = text;
    }
    internal void ClearDisplayRange()
    {
        SetDisplayRange(-1, -1, -1);
        SetDisplaySubtreeCounts(0, 0, 0);
    }
    public void SetDefault(string name, object? value, UiDirtyFlags invalidation) => _properties.SetDefault(name, value, invalidation); public void SetLocal(string name, object? value, UiDirtyFlags invalidation) => _properties.SetLocal(name, value, invalidation); public void SetStyle(string name, object? value, UiDirtyFlags invalidation) => _properties.SetStyle(name, value, invalidation); public void SetBinding(string name, UiBindingValue binding, UiDirtyFlags invalidation) => _properties.SetBinding(name, binding, invalidation); public void SetHandle(string name, object? value, UiDirtyFlags invalidation) => _properties.SetHandle(name, value, invalidation); public void SetAnimation(string name, object? value, UiDirtyFlags invalidation) => _properties.SetAnimation(name, value, invalidation); public void SetStyleResource(string name, UiResourceStore resources, UiResourceReference reference, UiDirtyFlags invalidation) => _properties.SetStyleResource(name, resources, reference, invalidation); public void Clear(string name, UiValueSource source) => _properties.Clear(name, source); public bool TryGet(string name, [NotNullWhen(true)] out UiValue? value) => _properties.TryGet(name, out value);
    public UiPropertyHandle GetHandle(string name) => _properties.GetHandle(name);
    public bool TrySet(UiPropertyHandle handle, object? value, UiDirtyFlags invalidation, [NotNullWhen(false)] out string? diagnostic) => _properties.TrySet(handle, value, invalidation, out diagnostic);
    private string GetAutomationValueText() => UiDescriptorCatalog.GetAutomationValueText(this);
    public uint TextVersion => _textVersion;
    public uint LayoutVersion => _layoutVersion;
    internal uint OutputVersion => _outputVersion;
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
        if (_bindingRuntimes.Remove(propertyName, out var compatibilityPrevious))
        {
            compatibilityPrevious.Dispose();
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

        if (_bindingRuntimes.Remove(propertyName, out var compatibilityPrevious))
        {
            compatibilityPrevious.Dispose();
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
        _properties.SetBindingValue(propertyName, property, value, invalidation);

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
        "Foreground" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        _ => UiDirtyFlags.Visual,
    };

    internal void SetBindingContext(object? value, bool explicitValue)
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

        foreach (var child in Children)
        {
            if (!child._hasExplicitBindingContext)
            {
                child.SetBindingContext(value, false);
            }
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
    private PanelState _panelState;
    private ItemsControlState _state;
    private readonly List<object?> _items = new();
    private readonly List<UiElement> _realized = new();
    private readonly List<UiElement> _nextRealized = new();

    internal ref PanelState PanelState => ref _panelState;
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
