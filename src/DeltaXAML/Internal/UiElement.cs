using System.Diagnostics.CodeAnalysis;
using System.Globalization;

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
            if (!resources.DependsOn(binding.Reference.Key, args.Key))
            {
                return;
            }

            var slots = GetSlots(name);
            slots.StyleValue = new UiValue(ResolveResource(binding), UiValueSource.Style, invalidation);
            ApplyEffective(name, slots, UiPropertyKeys.Resolve(name));
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
        while (effective is not null && !_owner.TryApplyTypedProperty(property, effective.UntypedValue))
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
    private object? ResolveResource(ResourceBinding binding) => binding.Resources.TryResolve(binding.Reference.Key, out var value, out _) ? value : null;
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
    }
}

internal class UiElement
{
    private static uint _nextId;
    private static uint _nextGeneration;
    private readonly List<UiElement> _children = new();
    private readonly List<UiBindingSpec> _bindingSpecs = new();
    private readonly Dictionary<string, UiBindingRuntime> _bindingRuntimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IUiCompiledBindingRuntime> _compiledBindingRuntimes = new(StringComparer.Ordinal);
    private readonly UiPropertyStore _properties;
    private List<UiNodeStore>? _nodeStores;
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
    private string? _styleKey;
    private string? _templateKey;
    private UiSize _measuredAvailable;
    private UiRect _arrangedBounds;
    private bool _hasMeasured;
    private bool _hasArranged;
    private bool _runtimeDisposed;
    private int _displayClipIndex = -1;
    private int _displayVisualIndex = -1;
    private int _displayTextIndex = -1;
    private int _displayClipCount;
    private int _displayVisualCount;
    private int _displayTextCount;
    public UiElement()
    {
        Id = new(++_nextId);
        Generation = ++_nextGeneration;
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
    public virtual string TypeName => "Element"; public UiElement? Parent { get; private set; }
    public IReadOnlyList<UiElement> Children => _children;
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
    public virtual bool IsPressed => false;
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

        if (child.Parent is { } parent)
        {
            parent.Remove(child);
        }

        child.Parent = this;
        if (_hasExplicitBindingContext && !child._hasExplicitBindingContext)
        {
            child.SetBindingContext(_bindingContext, false);
        }

        _children.Add(child);
        var invalidation = UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual;
        if (child.IsStyleDirty)
        {
            invalidation |= UiDirtyFlags.Style;
        }

        InvalidateChanged(invalidation);
        RootElement.NotifyNodeStoresChildAdded(this, child);
    }
    public bool Remove(UiElement child)
    {
        var index = _children.IndexOf(child);
        if (index < 0)
        {
            return false;
        }

        _children.RemoveAt(index);
        child.Parent = null;
        InvalidateChanged(UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        RootElement.NotifyNodeStoresChildRemoved(this, child, index);
        return true;
    }
    public void ClearChildren()
    {
        for (var i = _children.Count - 1; i >= 0; i--)
        {
            Remove(_children[i]);
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
    public virtual void SetPressed(bool value) { if (IsPressed != value) { InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual); } }
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
    internal void MeasureStage(UiSize available, UiNodeStore? nodes = null, List<UiMeasureRequest>? requests = null)
    {
        if (CanSkipMeasure(available))
        {
            return;
        }

        if ((Participation & Delta.XAML.UiParticipation.Layout) == 0)
        {
            DesiredSize = default;
            CompleteMeasure(available);
            return;
        }

        var children = nodes is null
            ? Children
            : nodes.GetLogicalChildren(new(Id.Value, Generation));
        DesiredSize = RequestedSize(UiDescriptorCatalog.Measure(this, new(available, LayoutScale, children, requests is null, requests, nodes)));
        CompleteMeasure(available);
    }

    internal void ArrangeStage(UiRect bounds, List<UiArrangeRequest>? requests, UiNodeStore? nodes)
    {
        if (CanSkipArrange(bounds))
        {
            return;
        }

        if ((Participation & Delta.XAML.UiParticipation.Layout) == 0)
        {
            Bounds = default;
            Clip = default;
            CompleteArrange(bounds);
            return;
        }

        Bounds = bounds;
        Clip = bounds;
        var children = nodes is null
            ? Children
            : nodes.GetLogicalChildren(new(Id.Value, Generation));
        if (UiDescriptorCatalog.Arrange(this, new(bounds, bounds, children, requests, nodes)))
        {
            CompleteArrange(bounds);
            return;
        }

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (requests is not null && child is UiElement element)
            {
                if (nodes is null)
                {
                    if (element.NeedsArrange(bounds))
                    {
                        requests.Add(new(new(element.Id.Value, element.Generation), bounds));
                    }
                }
                else if (nodes.TryGetNode(new(element.Id.Value, element.Generation), out var node) &&
                         element.NeedsArrange(bounds))
                {
                    requests.Add(new(node.Id, bounds));
                }
            }
            else
            {
                child.ArrangeStage(bounds, null, null);
            }
        }

        CompleteArrange(bounds);
    }
    protected bool CanSkipMeasure(UiSize available) =>
        _hasMeasured && (DirtyFlags & UiDirtyFlags.Measure) == 0 && _measuredAvailable == available;

    internal bool NeedsMeasure(UiSize available) => !CanSkipMeasure(available);

    protected void CompleteMeasure(UiSize available)
    {
        _measuredAvailable = available;
        _hasMeasured = true;
        DirtyFlags &= ~UiDirtyFlags.Measure;
        DirtyFlags |= UiDirtyFlags.Arrange;
    }

    protected bool CanSkipArrange(UiRect bounds) =>
        _hasArranged && (DirtyFlags & UiDirtyFlags.Arrange) == 0 && _arrangedBounds == bounds;

    internal bool NeedsArrange(UiRect bounds) => !CanSkipArrange(bounds);

    protected void CompleteArrange(UiRect bounds)
    {
        _arrangedBounds = bounds;
        _hasArranged = true;
        DirtyFlags &= ~UiDirtyFlags.Arrange;
    }

    internal void CompleteVisualExtraction() => DirtyFlags &= ~(UiDirtyFlags.Tree | UiDirtyFlags.Visual | UiDirtyFlags.Text);
    internal bool IsStyleDirty => (DirtyFlags & UiDirtyFlags.Style) != 0;
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
    protected UiSize RequestedSize(UiSize measured) => new(float.IsNaN(Width) ? measured.Width : Width, float.IsNaN(Height) ? measured.Height : Height);
    public void SetDefault(string name, object? value, UiDirtyFlags invalidation) => _properties.SetDefault(name, value, invalidation); public void SetLocal(string name, object? value, UiDirtyFlags invalidation) => _properties.SetLocal(name, value, invalidation); public void SetStyle(string name, object? value, UiDirtyFlags invalidation) => _properties.SetStyle(name, value, invalidation); public void SetBinding(string name, UiBindingValue binding, UiDirtyFlags invalidation) => _properties.SetBinding(name, binding, invalidation); public void SetHandle(string name, object? value, UiDirtyFlags invalidation) => _properties.SetHandle(name, value, invalidation); public void SetAnimation(string name, object? value, UiDirtyFlags invalidation) => _properties.SetAnimation(name, value, invalidation); public void SetStyleResource(string name, UiResourceStore resources, UiResourceReference reference, UiDirtyFlags invalidation) => _properties.SetStyleResource(name, resources, reference, invalidation); public void Clear(string name, UiValueSource source) => _properties.Clear(name, source); public bool TryGet(string name, [NotNullWhen(true)] out UiValue? value) => _properties.TryGet(name, out value);
    internal virtual bool TryApplyTypedProperty(UiPropertyKey key, object? value)
    {
        switch (key)
        {
            case UiPropertyKey.Width when value is float width: UiElementPropertiesGenerated.TrySetWidth(ref _state, width); return true;
            case UiPropertyKey.Height when value is float height: UiElementPropertiesGenerated.TrySetHeight(ref _state, height); return true;
            case UiPropertyKey.Background when value is UiColor background: UiElementPropertiesGenerated.TrySetBackground(ref _state, background); return true;
            case UiPropertyKey.Padding when value is UiThickness padding: UiElementPropertiesGenerated.TrySetPadding(ref _state, padding); return true;
            case UiPropertyKey.Fill when value is bool fill: UiElementPropertiesGenerated.TrySetFill(ref _state, fill); return true;
            case UiPropertyKey.IsEnabled when value is bool enabled: UiElementPropertiesGenerated.TrySetEnabled(ref _state, enabled); return true;
            case UiPropertyKey.IsSelected when value is bool selected: UiElementPropertiesGenerated.TrySetSelected(ref _state, selected); return true;
            case UiPropertyKey.Width or UiPropertyKey.Height or UiPropertyKey.Background or UiPropertyKey.Padding or UiPropertyKey.Fill or UiPropertyKey.IsEnabled or UiPropertyKey.IsSelected:
                return false;
        }

        return true;
    }
    public UiPropertyHandle GetHandle(string name) => _properties.GetHandle(name);
    public bool TrySet(UiPropertyHandle handle, object? value, UiDirtyFlags invalidation, [NotNullWhen(false)] out string? diagnostic) => _properties.TrySet(handle, value, invalidation, out diagnostic);
    protected virtual string GetAutomationValueText() => string.Empty;
    public uint TextVersion => _textVersion;
    public uint LayoutVersion => _layoutVersion;
    internal uint OutputVersion => _outputVersion;
    internal Delta.XAML.UiStyle? AppliedStyle => _appliedStyle;
    internal Delta.XAML.UiStyleState AppliedStyleState => _appliedStyleState;
    internal int AppliedStyleVersion => _appliedStyleVersion;
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

    internal void AttachBinding(UiBindingRuntime binding)
    {
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

    internal void AttachExternalBinding(string propertyName, Delta.XAML.IUiBinding binding) =>
        AttachBinding(new UiBindingRuntime(propertyName, binding));

    internal void AttachCompiledBinding<TSource, TValue>(
        string propertyName,
        UiPropertyKey property,
        UiDirtyFlags invalidation,
        Delta.XAML.UiCompiledBinding<TSource, TValue> binding,
        bool sourceNotificationsManaged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(binding);
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

    internal bool HasBinding(string propertyName) =>
        _bindingRuntimes.ContainsKey(propertyName) || _compiledBindingRuntimes.ContainsKey(propertyName);

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
        _nodeStores ??= new List<UiNodeStore>();
        if (!_nodeStores.Contains(store))
        {
            _nodeStores.Add(store);
        }
    }

    internal void DetachNodeStore(UiNodeStore store)
    {
        if (_nodeStores is not null && _nodeStores.Remove(store) && _nodeStores.Count == 0)
        {
            _nodeStores = null;
        }
    }

    private void NotifyNodeStoresChildAdded(UiElement parent, UiElement child)
    {
        if (_nodeStores is not { Count: > 0 } stores)
        {
            return;
        }

        for (var i = 0; i < stores.Count; i++)
        {
            stores[i].ApplyChildAdded(parent, child);
        }
    }

    private void NotifyNodeStoresChildRemoved(UiElement parent, UiElement child, int index)
    {
        if (_nodeStores is not { Count: > 0 } stores)
        {
            return;
        }

        for (var i = 0; i < stores.Count; i++)
        {
            stores[i].ApplyChildRemoved(parent, child, index);
        }
    }

    internal void ApplyBindingStage()
    {
        foreach (var binding in _bindingRuntimes.Values)
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
    }

    internal void NotifyBindingTargetChanged(string propertyName, object? value)
    {
        if (_bindingRuntimes.TryGetValue(propertyName, out var binding))
        {
            binding.WriteTarget(value);
        }

        if (_compiledBindingRuntimes.TryGetValue(propertyName, out var compiledBinding))
        {
            compiledBinding.TryWrite(value, out _);
        }
    }

    internal virtual Type BindingTargetType(string propertyName) => propertyName switch
    {
        "Width" or "Height" or "FontSize" => typeof(float),
        "Fill" or "IsEnabled" or "IsSelected" => typeof(bool),
        "Background" or "Foreground" => typeof(UiColor),
        _ => typeof(object),
    };

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

            foreach (var binding in element._compiledBindingRuntimes.Values)
            {
                binding.Dispose();
            }

            element._bindingRuntimes.Clear();
            element._compiledBindingRuntimes.Clear();
            for (var childIndex = 0; childIndex < element._children.Count; childIndex++)
            {
                if (element._children[childIndex] is UiElement child)
                {
                    traversal.Add(child);
                }
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

        foreach (var child in _children)
        {
            if (child is UiElement element && !element._hasExplicitBindingContext)
            {
                element.SetBindingContext(value, false);
            }
        }
    }
}

internal class Panel : UiElement
{
    private PanelState _state;

    internal ref PanelState State => ref _state;

    public override string TypeName => "Panel";
}

internal sealed class StackPanel : UiElement
{
    private StackPanelState _state = new() { Orientation = UiOrientation.Vertical };

    internal ref StackPanelState State => ref _state;

    public StackPanel() => SetDefaultProperty("Orientation", _state.Orientation, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);

    public override string TypeName => "StackPanel";
    public UiOrientation Orientation
    {
        get => _state.Orientation;
        set => SetLocalProperty("Orientation", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

    internal override bool TryApplyTypedProperty(UiPropertyKey key, object? value)
    {
        if (key == UiPropertyKey.Orientation && value is UiOrientation orientation)
        {
            UiStackPanelGenerated.TrySetOrientation(ref _state, orientation);
            return true;
        }

        return key == UiPropertyKey.Orientation ? false : base.TryApplyTypedProperty(key, value);
    }

}

internal sealed class ItemsControl : Panel
{
    private ItemsControlState _state;
    private readonly List<object?> _items = new();
    private readonly List<UiElement> _realized = new();
    private readonly List<UiElement> _nextRealized = new();

    internal new ref ItemsControlState State => ref _state;

    public override string TypeName => "ItemsControl";
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

    private sealed class ItemSource(IReadOnlyList<object?> values) : IUiItemSource
    {
        public int Count => values.Count;
        public object? GetValue(int index) => values[index];
        public bool Matches(IUiItemSource previous, int index) => Equals(values[index], previous.GetValue(index));
    }

    private sealed class ItemFactoryAdapter(Func<object?, UiElement> factory) : IUiItemFactory
    {
        public UiElement Create(IUiItemSource source, int index)
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

    public override string TypeName => "Border";
    public UiElement? Child => Children.Count == 0 ? null : Children[0];
}

internal readonly record struct GridLength(float Value, GridUnitType Unit) { public static GridLength Fixed(float v) => new(v, GridUnitType.Pixel); public static GridLength Auto => new(1, GridUnitType.Auto); public static GridLength Star(float weight = 1) => new(weight, GridUnitType.Star); }
internal enum GridUnitType { Pixel, Auto, Star }

internal sealed class Grid : UiElement
{
    private GridState _state;

    internal ref GridState State => ref _state;

    public Grid()
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

    public override string TypeName => "Grid";
    public int ColumnCount => _state.Columns.Length;
    public int RowCount => _state.Rows.Length;
    public void SetColumns(params GridLength[] columns)
        => SetLocalProperty("Columns", columns, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);

    public void SetRows(params GridLength[] rows)
        => SetLocalProperty("Rows", rows, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);

    internal override bool TryApplyTypedProperty(UiPropertyKey key, object? value)
    {
        if (key == UiPropertyKey.Columns && value is GridLength[] columns)
        {
            UiGridGenerated.TrySetColumns(ref _state, columns);
            return true;
        }

        if (key == UiPropertyKey.Rows && value is GridLength[] rows)
        {
            UiGridGenerated.TrySetRows(ref _state, rows);
            return true;
        }

        return key is UiPropertyKey.Columns or UiPropertyKey.Rows ? false : base.TryApplyTypedProperty(key, value);
    }

}

internal class ContentControl : UiElement
{
    private ContentControlState _state;

    internal ref ContentControlState State => ref _state;

    public override string TypeName => "ContentControl"; public UiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
}

internal class Button : ContentControl
{
    private ButtonState _state;

    internal new ref ButtonState State => ref _state;
    internal ref ButtonState InputState => ref _state;

    public override bool IsPressed => _state.IsPressed;
    public override string TypeName => "Button";
    public Button() { Focusable = true; AutomationRole = UiAutomationRole.Button; }
    public event EventHandler? Click;
    public override void SetPressed(bool value)
    {
        if (_state.IsPressed != value)
        {
            _state.IsPressed = value;
            InvalidateChanged(UiDirtyFlags.Visual);
        }
    }

    internal void ApplyInputResult(bool wasPressed, bool clicked, bool toggled)
    {
        if (wasPressed != _state.IsPressed || toggled)
        {
            InvalidateChanged(UiDirtyFlags.Visual);
        }

        if (clicked)
        {
            Click?.Invoke(this, EventArgs.Empty);
        }
    }
    protected override string GetAutomationValueText() => Content is TextBlock t ? t.Text : string.Empty;
}

internal sealed class ToggleButton : Button
{
    private ToggleButtonState _state;

    internal new ref ToggleButtonState State => ref _state;

    public bool IsChecked => _state.IsChecked;
}

internal class TextBlock : UiElement
{
    private TextBlockState _state;

    internal ref TextBlockState State => ref _state;

    public TextBlock()
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

    public override string TypeName => "TextBlock";

    public string Text { get => _state.Text; set { ArgumentNullException.ThrowIfNull(value); SetLocalProperty("Text", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public string FontKey { get => _state.Visual.FontKey; set { ArgumentNullException.ThrowIfNull(value); SetLocalProperty("FontKey", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public string GlyphRunKey { get => _state.Visual.GlyphRunKey; set { ArgumentNullException.ThrowIfNull(value); if (_state.Visual.GlyphRunKey == value) { return; } _state.Visual.GlyphRunKey = value; InvalidateChanged(UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public float FontSize { get => _state.Visual.FontSize; set => SetLocalProperty("FontSize", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor Foreground { get => _state.Visual.Foreground; set => SetLocalProperty("Foreground", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }

    internal override bool TryApplyTypedProperty(UiPropertyKey key, object? value)
    {
        switch (key)
        {
            case UiPropertyKey.Text when value is string text: UiTextBlockGenerated.TrySetText(ref _state, text); return true;
            case UiPropertyKey.FontKey when value is string fontKey: UiTextBlockGenerated.TrySetFontKey(ref _state, fontKey); return true;
            case UiPropertyKey.FontSize when value is float fontSize: UiTextBlockGenerated.TrySetFontSize(ref _state, fontSize); return true;
            case UiPropertyKey.Foreground when value is UiColor foreground: UiTextBlockGenerated.TrySetForeground(ref _state, foreground); return true;
            case UiPropertyKey.Text or UiPropertyKey.FontKey or UiPropertyKey.FontSize or UiPropertyKey.Foreground:
                return false;
        }

        return base.TryApplyTypedProperty(key, value);
    }

    protected override string GetAutomationValueText() => _state.Text;

    internal override Type BindingTargetType(string propertyName) => propertyName switch
    {
        "Text" or "FontKey" => typeof(string),
        "FontSize" => typeof(float),
        "Foreground" => typeof(UiColor),
        _ => base.BindingTargetType(propertyName),
    };
}

internal class TextBox : TextBlock
{
    private TextBoxState _state;
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    internal new ref TextBoxState State => ref _state;

    public TextBox()
    {
        Focusable = true;
        AutomationRole = UiAutomationRole.TextBox;
    }

    public override string TypeName => "TextBox";
    public int CaretIndex => _state.CaretIndex;
    public int SelectionStart => _state.SelectionStart;
    public int SelectionLength => _state.SelectionLength;
    public string? Diagnostic { get; protected set; }
    public IUiClipboard? Clipboard { get; set; }
    public event EventHandler<TextChangedEventArgs>? TextChanged;
    public void SetText(string text, bool recordUndo = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (recordUndo)
        {
            PushUndo();
        }

        var bound = HasBinding("Text");
        var changed = SetTextValue(text, bound);
        _state.CaretIndex = Math.Min(_state.CaretIndex, Text.Length);
        _state.SelectionStart = _state.CaretIndex;
        _state.SelectionLength = 0;
        Diagnostic = null;
        SetInvalid(false);
        if (bound && changed)
        {
            InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text);
        }

        NotifyBindingTargetChanged("Text", Text);
        TextChanged?.Invoke(this, new TextChangedEventArgs(Text));
    }
    public bool ApplyText(in UiTextInput input) { ReplaceSelection(input.Text.Span); return true; }
    public virtual bool ApplyKey(in UiKeyEvent input)
    {
        return UiTextBoxGenerated.ProcessKey(ref _state, in input, Text.Length) switch
        {
            UiTextEditAction.SelectAll => true,
            UiTextEditAction.Copy => CopyAndConsume(),
            UiTextEditAction.Cut => CutAndConsume(),
            UiTextEditAction.Paste => Paste(),
            UiTextEditAction.Undo => Undo(),
            UiTextEditAction.Redo => Redo(),
            UiTextEditAction.DeleteSelection => DeleteSelectionAndConsume(),
            _ => false,
        };

        bool CopyAndConsume() { Copy(); return true; }
        bool CutAndConsume() { Cut(); return true; }
        bool DeleteSelectionAndConsume()
        {
            if (!HasSelection())
            {
                return false;
            }

            DeleteRange(_state.SelectionStart, _state.SelectionLength);
            return true;
        }
    }
    public void SelectAll() { _state.SelectionStart = 0; _state.SelectionLength = Text.Length; _state.CaretIndex = Text.Length; }
    public void Copy()
    {
        if (Clipboard is not null && HasSelection())
        {
            Clipboard.SetText(GetSelection());
        }
    }
    public void Cut() { if (!HasSelection()) { return; } if (Clipboard is not null) { Clipboard.SetText(GetSelection()); } DeleteRange(_state.SelectionStart, _state.SelectionLength); }
    public bool Paste() { if (Clipboard?.ReadText() is not { Length: > 0 } text) { return false; } ReplaceSelection(text); return true; }
    public bool Undo() { if (_undo.Count == 0) { return false; } _redo.Add(Text); var bound = HasBinding("Text"); var changed = SetTextValue(_undo[^1], bound); _undo.RemoveAt(_undo.Count - 1); _state.CaretIndex = Text.Length; _state.SelectionStart = Text.Length; _state.SelectionLength = 0; if (bound && changed) { InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } NotifyBindingTargetChanged("Text", Text); TextChanged?.Invoke(this, new TextChangedEventArgs(Text)); return true; }
    public bool Redo() { if (_redo.Count == 0) { return false; } _undo.Add(Text); var bound = HasBinding("Text"); var changed = SetTextValue(_redo[^1], bound); _redo.RemoveAt(_redo.Count - 1); _state.CaretIndex = Text.Length; _state.SelectionStart = Text.Length; _state.SelectionLength = 0; if (bound && changed) { InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } NotifyBindingTargetChanged("Text", Text); TextChanged?.Invoke(this, new TextChangedEventArgs(Text)); return true; }
    protected void ReplaceSelection(ReadOnlySpan<char> inserted)
    {
        PushUndo();
        if (HasSelection())
        {
            DeleteRange(_state.SelectionStart, _state.SelectionLength, false);
        }

        var bound = HasBinding("Text");
        var changed = SetTextValue(InsertText(Text, _state.CaretIndex, inserted), bound);
        _state.CaretIndex += inserted.Length;
        _state.SelectionStart = _state.CaretIndex; _state.SelectionLength = 0;
        Diagnostic = null;
        SetInvalid(false);
        if (bound && changed)
        {
            InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text);
        }

        NotifyBindingTargetChanged("Text", Text);
        TextChanged?.Invoke(this, new TextChangedEventArgs(Text));
    }
    private void PushUndo() { _undo.Add(Text); _redo.Clear(); }
    private void DeleteRange(int start, int length, bool record = true) { if (record) { PushUndo(); } var bound = HasBinding("Text"); var changed = SetTextValue(Text.Remove(start, length), bound); _state.CaretIndex = start; _state.SelectionStart = start; _state.SelectionLength = 0; if (bound && changed) { InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } NotifyBindingTargetChanged("Text", Text); TextChanged?.Invoke(this, new TextChangedEventArgs(Text)); }
    private bool SetTextValue(string text, bool bound)
    {
        if (bound)
        {
            return UiTextBlockGenerated.TrySetText(ref ((TextBlock)this).State, text);
        }

        if (Text == text)
        {
            return false;
        }

        Text = text;
        return true;
    }
    private bool HasSelection() => _state.SelectionLength > 0;
    private string GetSelection() => Text.Substring(_state.SelectionStart, _state.SelectionLength);
    private static string InsertText(string value, int index, ReadOnlySpan<char> inserted)
    {
        return string.Concat(value.AsSpan(0, index), inserted, value.AsSpan(index));
    }
    protected override string GetAutomationValueText() => Text;
}

internal sealed class NumericEditor : TextBox
{
    private NumericEditorState _state = new() { Min = double.MinValue, Max = double.MaxValue, CommittedText = string.Empty };

    internal new ref NumericEditorState State => ref _state;

    public NumericEditor()
    {
        AutomationRole = UiAutomationRole.NumericEditor;
        SetDefaultProperty("Value", _state.Value, UiDirtyFlags.Binding | UiDirtyFlags.Visual);
        SetDefaultProperty("Minimum", _state.Min, UiDirtyFlags.Visual);
        SetDefaultProperty("Maximum", _state.Max, UiDirtyFlags.Visual);
    }

    public override string TypeName => "NumericEditor";
    public double Value => _state.Value;
    public double Min { get => _state.Min; set => SetLocalProperty("Minimum", value, UiDirtyFlags.Visual); }
    public double Max { get => _state.Max; set => SetLocalProperty("Maximum", value, UiDirtyFlags.Visual); }
    public bool HasValidationError => Diagnostic is not null;
    public bool IsDirty => Text != _state.CommittedText;
    public void Initialize(double value) { SetLocalProperty("Value", value, UiDirtyFlags.Binding | UiDirtyFlags.Visual); }
    public bool TryCommit()
    {
        if (!UiNumericEditorGenerated.TryParse(Text, Min, Max, out var value, out var diagnostic))
        {
            Diagnostic = diagnostic;
            return false;
        }

        _state.Value = value;
        _state.CommittedText = Format(value);
        SetText(_state.CommittedText, false);
        Diagnostic = null;
        return true;
    }
    public void CancelEdit() { SetText(_state.CommittedText ?? string.Empty, false); Diagnostic = null; }
    public bool TryCommitText(string text) { SetText(text); return TryCommit(); }
    public bool Increment(double step = 1) { return Adjust(step); }
    public bool Decrement(double step = 1) { return Adjust(-step); }
    public override bool ApplyKey(in UiKeyEvent input)
    {
        var action = UiNumericEditorGenerated.ProcessKey(ref _state, in input, Text.Length);
        if (action == UiTextEditAction.Increment)
        {
            return Increment();
        }

        if (action == UiTextEditAction.Decrement)
        {
            return Decrement();
        }

        return base.ApplyKey(input);
    }
    public bool TryApplyValue(string text, out string? error)
    {
        if (UiNumericEditorGenerated.TryParse(text, Min, Max, out var value, out var diagnostic))
        {
            _state.Value = value;
            _state.CommittedText = Format(value);
            SetText(_state.CommittedText, false);
            Diagnostic = null;
            SetInvalid(false);
            error = null;
            return true;
        }

        Diagnostic = diagnostic;
        SetInvalid(true);
        error = Diagnostic;
        return false;
    }

    internal override bool TryApplyTypedProperty(UiPropertyKey key, object? value)
    {
        switch (key)
        {
            case UiPropertyKey.Value when value is double numeric:
                return TryApplyValue(Format(numeric), out _);
            case UiPropertyKey.Minimum when value is double minimum:
                UiNumericEditorGenerated.TrySetMinimum(ref _state, minimum);
                return true;
            case UiPropertyKey.Maximum when value is double maximum:
                UiNumericEditorGenerated.TrySetMaximum(ref _state, maximum);
                return true;
            case UiPropertyKey.Value or UiPropertyKey.Minimum or UiPropertyKey.Maximum:
                return false;
        }

        return base.TryApplyTypedProperty(key, value);
    }
    private bool Adjust(double delta)
    {
        if (!UiNumericEditorGenerated.TryAdjust(ref _state, delta, out var diagnostic))
        {
            Diagnostic = diagnostic;
            SetInvalid(true);
            return false;
        }

        _state.CommittedText = Format(_state.Value);
        SetText(_state.CommittedText, false);
        Diagnostic = null;
        SetInvalid(false);
        return true;
    }
    private static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    protected override string GetAutomationValueText() => Value.ToString(CultureInfo.InvariantCulture);
    internal override Type BindingTargetType(string propertyName) => propertyName == "Value" ? typeof(double) : base.BindingTargetType(propertyName);
}

internal class ScrollViewer : ContentControl
{
    private ScrollViewerState _state;

    internal new ref ScrollViewerState State => ref _state;

    public override string TypeName => "ScrollViewer";
    public UiPoint Offset => _state.Offset;
    public void ScrollBy(float x, float y)
    {
        if (UiScrollViewerGenerated.TryScrollBy(ref _state, x, y))
        {
            InvalidateChanged(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }

    internal void ApplyWheelInput(float delta)
    {
        if (UiScrollViewerGenerated.TryScrollBy(ref _state, 0, delta))
        {
            InvalidateChanged(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }
}
