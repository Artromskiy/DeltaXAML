using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal sealed class UiValue : IUiValue
{
    public UiValue(object? value, UiValueSource source, UiDirtyFlags invalidation) { UntypedValue = value; Source = source; Invalidation = invalidation; }
    public object? UntypedValue { get; }
    public UiValueSource Source { get; }
    public UiDirtyFlags Invalidation { get; }
}

internal sealed class UiBindingValue : IUiBinding
{
    private readonly Func<object?> _read; private readonly Func<object?, (bool Success, string? Error)> _write;
    public UiBindingValue(Func<object?> read, Func<object?, (bool Success, string? Error)> write) { ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(write); _read = read; _write = write; }
    public object? Read() => _read(); public bool TryWrite(object? value, [NotNullWhen(false)] out string? diagnostic) { var r = _write(value); diagnostic = r.Error ?? (r.Success ? null : "Binding write failed."); return r.Success; }
    public event EventHandler? Changed; public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class UiPropertyStore : IUiPropertyStore
{
    private readonly Dictionary<string, IUiValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SourceSlots> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IUiBinding> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventHandler> _bindingHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceBinding> _resourceBindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action<object?>> _valueApplications = new(StringComparer.Ordinal);
    private readonly UiElement _owner;
    private static readonly UiValueSource[] Precedence = [UiValueSource.Handle, UiValueSource.Local, UiValueSource.Binding, UiValueSource.Style, UiValueSource.Default];
    public UiPropertyStore(UiElement owner) { ArgumentNullException.ThrowIfNull(owner); _owner = owner; }
    void IUiPropertyStore.SetDefault(string name, object? value, UiDirtyFlags invalidation) => SetDefault(name, value, invalidation);
    void IUiPropertyStore.SetLocal(string name, object? value, UiDirtyFlags invalidation) => SetLocal(name, value, invalidation);
    void IUiPropertyStore.SetStyle(string name, object? value, UiDirtyFlags invalidation) => SetStyle(name, value, invalidation);
    void IUiPropertyStore.SetBinding(string name, IUiBinding binding, UiDirtyFlags invalidation) => SetBinding(name, binding, invalidation);
    public void SetDefault(string name, object? value, UiDirtyFlags invalidation, Action<object?>? apply = null) => SetSource(name, new(value, UiValueSource.Default, invalidation), apply);
    public void SetLocal(string name, object? value, UiDirtyFlags invalidation, Action<object?>? apply = null) => SetSource(name, new(value, UiValueSource.Local, invalidation), apply);
    public void SetStyle(string name, object? value, UiDirtyFlags invalidation, Action<object?>? apply = null)
    {
        RemoveResourceBinding(name);
        SetSource(name, new(value, UiValueSource.Style, invalidation), apply);
    }
    public void SetStyleResource(string name, UiResourceStore resources, UiResourceReference reference, UiDirtyFlags invalidation, Action<object?>? apply = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.Key);
        RemoveResourceBinding(name);
        var binding = new ResourceBinding(resources, reference, invalidation);
        _resourceBindings[name] = binding;
        binding.Handler = (_, _) =>
        {
            var slots = GetSlots(name);
            slots.StyleValue = new UiValue(ResolveResource(binding), UiValueSource.Style, invalidation);
            ApplyEffective(name, slots);
        };
        resources.Changed += binding.Handler;
        SetSource(name, new(ResolveResource(binding), UiValueSource.Style, invalidation), apply);
    }
    public void SetBinding(string name, IUiBinding binding, UiDirtyFlags invalidation, Action<object?>? apply = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(binding);
        RemoveBinding(name);
        if (apply is not null) { _valueApplications[name] = apply; }
        _bindings[name] = binding;
        var slots = GetSlots(name);
        slots.BindingValue = new UiValue(binding.Read(), UiValueSource.Binding, invalidation);
        ApplyEffective(name, slots);
        EventHandler handler = (_, _) =>
        {
            slots.BindingValue = new UiValue(binding.Read(), UiValueSource.Binding, invalidation);
            ApplyEffective(name, slots);
        };
        _bindingHandlers[name] = handler;
        binding.Changed += handler;
    }
    public void SetHandle(string name, object? value, UiDirtyFlags invalidation) => SetSource(name, new(value, UiValueSource.Handle, invalidation));
    public void Clear(string name, UiValueSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (source == UiValueSource.Default) { throw new ArgumentException("Default values cannot be cleared.", nameof(source)); }
        if (source == UiValueSource.Binding) { RemoveBinding(name); }
        if (source == UiValueSource.Style) { RemoveResourceBinding(name); }
        if (_slots.TryGetValue(name, out var slots))
        {
            SetValue(slots, source, null);
            ApplyEffective(name, slots);
        }
    }
    public bool TryGet(string name, [NotNullWhen(true)] out IUiValue? value) { ArgumentException.ThrowIfNullOrWhiteSpace(name); return _values.TryGetValue(name, out value); }
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
    private void SetSource(string name, UiValue value, Action<object?>? apply = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (apply is not null) { _valueApplications[name] = apply; }
        var slots = GetSlots(name);
        SetValue(slots, value.Source, value);
        ApplyEffective(name, slots);
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
        }
    }
    private SourceSlots GetSlots(string name) => _slots.TryGetValue(name, out var slots) ? slots : AddSlots(name);
    private SourceSlots AddSlots(string name) { var slots = new SourceSlots(); _slots.Add(name, slots); return slots; }
    private void ApplyEffective(string name, SourceSlots slots)
    {
        _values.TryGetValue(name, out var previous);
        UiValue? effective = null;
        foreach (var source in Precedence)
        {
            effective = source switch
            {
                UiValueSource.Handle => slots.HandleValue,
                UiValueSource.Local => slots.LocalValue,
                UiValueSource.Binding => slots.BindingValue,
                UiValueSource.Style => slots.StyleValue,
                UiValueSource.Default => slots.DefaultValue,
                _ => null
            };
            if (effective is not null) { break; }
        }
        if (effective is null) { _values.Remove(name); }
        else { _values[name] = effective; }
        if (Same(previous, effective)) { return; }
        var invalidation = effective?.Invalidation ?? previous?.Invalidation ?? UiDirtyFlags.Visual;
        _owner.Invalidate(invalidation);
        if (_valueApplications.TryGetValue(name, out var apply) && effective is not null)
        {
            apply(effective.UntypedValue);
        }
    }
    private object? ResolveResource(ResourceBinding binding) => binding.Resources.TryResolve(binding.Reference.Key, out var value, out _) ? value : null;
    private static bool Same(IUiValue? left, UiValue? right) => left?.Source == right?.Source && Equals(left?.UntypedValue, right?.UntypedValue);
    private sealed class SourceSlots
    {
        public UiValue? DefaultValue, StyleValue, BindingValue, LocalValue, HandleValue;
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

internal class UiElement : IUiElement, IUiPropertyStore
{
    private static uint _nextId;
    private static uint _nextGeneration;
    private readonly List<IUiElement> _children = new();
    private readonly List<UiBindingSpec> _bindingSpecs = new();
    private readonly Dictionary<string, UiBindingRuntime> _bindingRuntimes = new(StringComparer.Ordinal);
    private readonly UiPropertyStore _properties;
    private object? _bindingContext;
    private bool _hasExplicitBindingContext;
    private float _layoutScale = 1f;
    private float _dpiScale = 1f;
    private float _width = float.NaN;
    private float _height = float.NaN;
    private UiColor _background;
    private uint _layoutVersion;
    private uint _dpiVersion;
    private uint _textVersion;
    public UiElement() { Id = new(++_nextId); Generation = ++_nextGeneration; _properties = new(this); }
    public UiElementId Id { get; }
    public uint Generation { get; }
    public virtual string TypeName => "Element"; public IUiElement? Parent { get; private set; }
    public IReadOnlyList<IUiElement> Children => _children;
    public UiVisibility Visibility { get; set; } = UiVisibility.Visible; public bool Focusable { get; set; }
    public Delta.XAML.UiParticipation Participation { get; private set; } = Delta.XAML.UiParticipation.All;
    internal bool ParticipatesIn(Delta.XAML.UiParticipation participation) => (Participation & participation) == participation;
    public float Width { get => _width; set { if (!_width.Equals(value)) { SetLocalProperty("Width", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual, ApplyWidthValue); } } }
    public float Height { get => _height; set { if (!_height.Equals(value)) { SetLocalProperty("Height", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual, ApplyHeightValue); } } }
    public bool Fill { get; set; }
    public UiRect Bounds { get; protected set; }
    public UiRect Clip { get; protected set; }
    public UiSize DesiredSize { get; protected set; }
    public UiColor Background { get => _background; set { if (_background != value) { SetLocalProperty("Background", value, UiDirtyFlags.Visual, ApplyBackgroundValue); } } }
    public bool IsEnabled { get; set; } = true; public bool IsHovered { get; private set; }
    public virtual bool IsPressed => false;
    public bool IsSelected { get; set; }
    public bool IsInvalid { get; protected set; }
    public string? StyleKey { get; set; }
    public string? TemplateKey { get; set; }
    public string? AutomationName { get; set; }
    public UiAutomationRole AutomationRole { get; set; } = UiAutomationRole.Generic;
    public UiThickness Margin { get; set; }
    public UiThickness Padding { get; set; }
    internal UiDirtyFlags DirtyFlags { get; set; } = UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual;
    public UiAutomationMetadata Automation => new(AutomationName ?? TypeName, AutomationRole, GetAutomationValueText(), IsEnabled, IsInvalid);
    public UiStateSnapshot VisualState => new(IsEnabled ? IsInvalid ? UiVisualState.Invalid : IsPressed ? UiVisualState.Pressed : IsHovered ? UiVisualState.Hover : IsSelected ? UiVisualState.Selected : IsFocused ? UiVisualState.Focused : UiVisualState.Normal : UiVisualState.Disabled, IsEnabled, IsInvalid, IsSelected, IsFocused, IsHovered, IsPressed);
    public bool IsFocused { get; private set; }
    public void Add(IUiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child is not UiElement owned)
        {
            throw new ArgumentException("Child must be a DeltaXAML element.", nameof(child));
        }

        for (var ancestor = this; ancestor is not null; ancestor = ancestor.Parent as UiElement)
        {
            if (ReferenceEquals(ancestor, owned))
            {
                throw new ArgumentException("A UI element cannot be added below itself.", nameof(child));
            }
        }

        if (owned.Parent is UiElement parent)
        {
            parent.Remove(owned);
        }

        owned.Parent = this;
        if (_hasExplicitBindingContext && !owned._hasExplicitBindingContext)
        {
            owned.SetBindingContext(_bindingContext, false);
        }

        _children.Add(owned);
        Invalidate(UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
    }
    public bool Remove(IUiElement child) { if (!_children.Remove(child)) { return false; } if (child is UiElement owned) { owned.Parent = null; } Invalidate(UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual); return true; }
    public void ClearChildren()
    {
        for (var i = _children.Count - 1; i >= 0; i--)
        {
            Remove(_children[i]);
        }
    }
    public void Invalidate(UiDirtyFlags flags)
    {
        DirtyFlags |= flags;
        if ((flags & (UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Style | UiDirtyFlags.Resource)) != 0)
        {
            _layoutVersion++;
        }

        if ((flags & (UiDirtyFlags.Binding | UiDirtyFlags.Visual)) != 0) { _textVersion++; _layoutVersion++; }
        if ((flags & (UiDirtyFlags.Measure | UiDirtyFlags.Arrange)) != 0)
        {
            (Parent as UiElement)?.Invalidate(UiDirtyFlags.Measure);
        }
        else if ((flags & UiDirtyFlags.Visual) != 0)
        {
            (Parent as UiElement)?.Invalidate(UiDirtyFlags.Visual);
        }
    }
    public void SetHovered(bool value) { if (IsHovered != value) { IsHovered = value; Invalidate(UiDirtyFlags.Visual); } }
    public virtual void SetPressed(bool value) { if (IsPressed != value) { Invalidate(UiDirtyFlags.Visual); } }
    public void SetFocused(bool value) { if (IsFocused != value) { IsFocused = value; Invalidate(UiDirtyFlags.Visual); } }
    public void SetInvalid(bool value) { if (IsInvalid != value) { IsInvalid = value; Invalidate(UiDirtyFlags.Visual); } }
    public void SetParticipation(Delta.XAML.UiParticipation value)
    {
        if (Participation == value)
        {
            return;
        }

        Participation = value;
        Invalidate(UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.HitTest);
    }
    public virtual void Measure(UiSize available)
    {
        if ((Participation & Delta.XAML.UiParticipation.Layout) == 0)
        {
            DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        foreach (var child in _children)
        {
            if (child is UiElement element)
            {
                element._layoutScale = _layoutScale;
                element.Measure(available);
            }
            else
            {
                child.Measure(available);
            }
        }

        DesiredSize = RequestedSize(new(0, 0));
        DirtyFlags &= ~UiDirtyFlags.Measure;
    }
    public virtual void Arrange(UiRect bounds)
    {
        if ((Participation & Delta.XAML.UiParticipation.Layout) == 0)
        {
            Bounds = default;
            Clip = default;
            DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
            return;
        }

        Bounds = bounds;
        Clip = bounds;
        foreach (var child in _children)
        {
            if (child is UiElement element)
            {
                element._layoutScale = _layoutScale;
                element.Arrange(bounds);
            }
            else
            {
                child.Arrange(bounds);
            }
        }

        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
    protected UiSize RequestedSize(UiSize measured) => new(float.IsNaN(Width) ? measured.Width : Width, float.IsNaN(Height) ? measured.Height : Height);
    public IUiElement? HitTest(UiPoint point)
    {
        if (Visibility != UiVisibility.Visible || (Participation & Delta.XAML.UiParticipation.Layout) == 0 || !Clip.Contains(point))
        {
            return null;
        }

        for (var i = _children.Count - 1; i >= 0; i--)
        {
            if (_children[i] is UiElement child && child.HitTest(point) is { } hit)
            {
                return hit;
            }
        }

        return (Participation & Delta.XAML.UiParticipation.HitTesting) != 0 ? this : null;
    }
    public void SetDefault(string name, object? value, UiDirtyFlags invalidation) => _properties.SetDefault(name, value, invalidation); public void SetLocal(string name, object? value, UiDirtyFlags invalidation) => _properties.SetLocal(name, value, invalidation); public void SetStyle(string name, object? value, UiDirtyFlags invalidation) => _properties.SetStyle(name, value, invalidation, value => ApplyStyleValue(name, value)); public void SetBinding(string name, IUiBinding binding, UiDirtyFlags invalidation) => _properties.SetBinding(name, binding, invalidation, value => ApplyBindingValue(name, value)); public void SetHandle(string name, object? value, UiDirtyFlags invalidation) => _properties.SetHandle(name, value, invalidation); public void SetStyleResource(string name, UiResourceStore resources, UiResourceReference reference, UiDirtyFlags invalidation) => _properties.SetStyleResource(name, resources, reference, invalidation, value => ApplyStyleValue(name, value)); public void Clear(string name, UiValueSource source) => _properties.Clear(name, source); public bool TryGet(string name, [NotNullWhen(true)] out IUiValue? value) => _properties.TryGet(name, out value);
    protected virtual void ApplyStyleValue(string name, object? value) => ApplyStyleResourceValue(name, value);
    protected virtual void ApplyStyleResourceValue(string name, object? value)
    {
        switch (name)
        {
            case "Width" when value is float width: _width = width; break;
            case "Height" when value is float height: _height = height; break;
            case "Background" when value is UiColor background: _background = background; break;
            case "Fill" when value is bool fill: Fill = fill; break;
        }
    }
    private void ApplyWidthValue(object? value) { if (value is float width) { _width = width; } }
    private void ApplyHeightValue(object? value) { if (value is float height) { _height = height; } }
    private void ApplyBackgroundValue(object? value) { if (value is UiColor background) { _background = background; } }
    public UiPropertyHandle GetHandle(string name) => _properties.GetHandle(name);
    public bool TrySet(UiPropertyHandle handle, object? value, UiDirtyFlags invalidation, [NotNullWhen(false)] out string? diagnostic) => _properties.TrySet(handle, value, invalidation, out diagnostic);
    protected virtual string GetAutomationValueText() => string.Empty;
    protected virtual string GetTextRunKey() => TypeName;
    protected virtual bool HasTextRun => false;
    protected virtual string GetTextRunText() => string.Empty;
    protected virtual UiColor GetTextRunColor() => new(255, 255, 255);
    protected virtual float GetTextRunFontSize() => 14;
    protected virtual string GetTextRunFontKey() => "default";
    public uint TextVersion => _textVersion;
    public uint LayoutVersion => _layoutVersion;
    public float LayoutScale => _layoutScale;
    public float DpiScale => _dpiScale;
    public void SetLayoutScale(float scale)
    {
        if (Math.Abs(_dpiScale - scale) > float.Epsilon) { _dpiScale = scale; _dpiVersion++; }
        _layoutScale = scale; foreach (var child in _children)
        {
            if (child is UiElement element)
            {
                element.SetLayoutScale(scale);
            }
        }
    }
    protected uint TextRunVersion => _textVersion ^ (_dpiVersion << 1);
    internal virtual bool TryGetTextRun(out UiTextRun run)
    {
        if (!HasTextRun) { run = default; return false; }
        run = new UiTextRun(GetTextRunFontKey(), GetTextRunFontSize() * LayoutScale, GetTextRunText(), GetTextRunKey(), GetTextRunColor(), Bounds, Clip, Id, Generation, TextRunVersion);
        return true;
    }

    internal IReadOnlyList<UiBindingSpec> BindingSpecs => _bindingSpecs;
    internal object? BindingContext => _bindingContext;
    internal bool HasExplicitBindingContext => _hasExplicitBindingContext;

    internal void AddBindingSpec(in UiBindingSpec spec) => _bindingSpecs.Add(spec);

    internal void AttachBinding(UiBindingRuntime binding)
    {
        if (_bindingRuntimes.Remove(binding.PropertyName, out var previous))
        {
            previous.Dispose();
        }

        _bindingRuntimes.Add(binding.PropertyName, binding);
        binding.Attach(this, BindingInvalidation(binding.PropertyName));
        binding.SetContext(_bindingContext);
    }

    internal void AttachExternalBinding(string propertyName, Delta.XAML.IUiBinding binding) =>
        AttachBinding(new UiBindingRuntime(propertyName, binding));

    internal bool HasBinding(string propertyName) => _bindingRuntimes.ContainsKey(propertyName);

    internal void NotifyBindingTargetChanged(string propertyName, object? value)
    {
        if (_bindingRuntimes.TryGetValue(propertyName, out var binding))
        {
            binding.WriteTarget(value);
        }
    }

    internal virtual Type BindingTargetType(string propertyName) => propertyName switch
    {
        "Width" or "Height" or "FontSize" => typeof(float),
        "Fill" or "IsEnabled" or "IsSelected" => typeof(bool),
        "Background" or "Foreground" => typeof(UiColor),
        _ => typeof(object),
    };

    internal virtual void ApplyBindingValue(string propertyName, object? value)
    {
        switch (propertyName)
        {
            case "Width" when value is float width: _width = width; break;
            case "Height" when value is float height: _height = height; break;
            case "Fill" when value is bool fill: Fill = fill; break;
            case "Background" when value is UiColor background: _background = background; break;
            case "IsEnabled" when value is bool enabled: IsEnabled = enabled; break;
            case "IsSelected" when value is bool selected: IsSelected = selected; break;
        }
    }

    protected void SetDefaultProperty(string name, object? value, UiDirtyFlags invalidation, Action<object?> apply) =>
        _properties.SetDefault(name, value, invalidation, apply);
    protected void SetLocalProperty(string name, object? value, UiDirtyFlags invalidation, Action<object?> apply) =>
        _properties.SetLocal(name, value, invalidation, apply);

    private static UiDirtyFlags BindingInvalidation(string propertyName) => propertyName switch
    {
        "Text" or "FontKey" or "FontSize" or "Width" or "Height" or "Padding" => UiDirtyFlags.Measure | UiDirtyFlags.Visual,
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

internal class Panel : UiElement, IUiPanel
{
    private PanelState _state;

    internal ref PanelState State => ref _state;

    public override string TypeName => "Panel";
    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            _state.DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        UiPanelGenerated.Measure(ref _state, new(available, LayoutScale, Children));
        DesiredSize = RequestedSize(_state.DesiredSize);
        DirtyFlags &= ~UiDirtyFlags.Measure;
    }
    public override void Arrange(UiRect bounds)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            Bounds = default;
            Clip = default;
            _state.Bounds = default;
            _state.Clip = default;
            DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
            return;
        }

        UiPanelGenerated.Arrange(ref _state, new(bounds, bounds, Children));
        Bounds = _state.Bounds;
        Clip = _state.Clip;

        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
}

internal sealed class StackPanel : UiElement, IUiPanel
{
    private StackPanelState _state = new() { Orientation = UiOrientation.Vertical };

    internal ref StackPanelState State => ref _state;

    public override string TypeName => "StackPanel";
    public UiOrientation Orientation
    {
        get => _state.Orientation;
        set
        {
            if (UiStackPanelGenerated.TrySetOrientation(ref _state, value))
            {
                Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
            }
        }
    }

    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            _state.DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        UiStackPanelGenerated.Measure(ref _state, new(available, LayoutScale, Children));
        DesiredSize = RequestedSize(_state.DesiredSize);
        DirtyFlags &= ~UiDirtyFlags.Measure;
    }
    public override void Arrange(UiRect bounds)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            Bounds = default;
            Clip = default;
            _state.Bounds = default;
            _state.Clip = default;
            DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
            return;
        }

        UiStackPanelGenerated.Arrange(ref _state, new(bounds, bounds, Children));
        Bounds = _state.Bounds;
        Clip = _state.Clip;

        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
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

internal class Border : UiElement, IUiPanel
{
    private BorderState _state;

    internal ref BorderState State => ref _state;

    public override string TypeName => "Border";
    public IUiElement? Child => Children.Count == 0 ? null : Children[0];
    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        _state.Padding = Padding;
        UiBorderGenerated.Measure(ref _state, new(available, LayoutScale, Children));
        DesiredSize = RequestedSize(_state.DesiredSize);

        DirtyFlags &= ~UiDirtyFlags.Measure;
    }
    public override void Arrange(UiRect bounds)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            Bounds = default;
            Clip = default;
            DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
            return;
        }

        _state.Padding = Padding;
        UiBorderGenerated.Arrange(ref _state, new(bounds, bounds, Children));
        Bounds = _state.Bounds;
        Clip = _state.Clip;
        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
}

internal readonly record struct GridLength(float Value, GridUnitType Unit) { public static GridLength Fixed(float v) => new(v, GridUnitType.Pixel); public static GridLength Auto => new(1, GridUnitType.Auto); public static GridLength Star(float weight = 1) => new(weight, GridUnitType.Star); }
internal enum GridUnitType { Pixel, Auto, Star }

internal sealed class Grid : UiElement, IUiPanel
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
    }

    public override string TypeName => "Grid";
    public int ColumnCount => _state.Columns.Length;
    public int RowCount => _state.Rows.Length;
    public void SetColumns(params GridLength[] columns)
    {
        if (UiGridGenerated.TrySetColumns(ref _state, columns))
        {
            Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
        }
    }

    public void SetRows(params GridLength[] rows)
    {
        if (UiGridGenerated.TrySetRows(ref _state, rows))
        {
            Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
        }
    }

    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            _state.DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        UiGridGenerated.Measure(ref _state, new(available, LayoutScale, Children));
        DesiredSize = RequestedSize(_state.DesiredSize);
        DirtyFlags &= ~UiDirtyFlags.Measure;
    }
    public override void Arrange(UiRect bounds)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            Bounds = default;
            Clip = default;
            _state.Bounds = default;
            _state.Clip = default;
            DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
            return;
        }

        UiGridGenerated.Arrange(ref _state, new(bounds, bounds, Children));
        Bounds = _state.Bounds;
        Clip = _state.Clip;

        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
}

internal class ContentControl : UiElement
{
    private ContentControlState _state;

    internal ref ContentControlState State => ref _state;

    public override string TypeName => "ContentControl"; public IUiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        UiContentControlGenerated.Measure(ref _state, new(available, LayoutScale, Children));
        DesiredSize = RequestedSize(_state.DesiredSize);
        DirtyFlags &= ~UiDirtyFlags.Measure;
    }
    public override void Arrange(UiRect bounds)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            Bounds = default;
            Clip = default;
            DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
            return;
        }

        UiContentControlGenerated.Arrange(ref _state, new(bounds, bounds, Children));
        Bounds = _state.Bounds;
        Clip = _state.Clip;
        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
}

internal class Button : ContentControl, IUiRoutedEventSink
{
    private ButtonState _state;

    internal new ref ButtonState State => ref _state;

    public override bool IsPressed => _state.IsPressed;
    public override string TypeName => "Button";
    public Button() { Focusable = true; AutomationRole = UiAutomationRole.Button; }
    public event EventHandler? Click;
    public override void SetPressed(bool value)
    {
        if (_state.IsPressed != value)
        {
            _state.IsPressed = value;
            Invalidate(UiDirtyFlags.Visual);
        }
    }

    public virtual void OnRoutedEvent(in UiRoutedEvent routedEvent)
    {
        var clicked = UiButtonGenerated.Process(ref _state, in routedEvent);
        SetPressed(_state.IsPressed);
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
    public override void OnRoutedEvent(in UiRoutedEvent routedEvent)
    {
        base.OnRoutedEvent(routedEvent);
        UiToggleButtonGenerated.Process(ref _state, in routedEvent);
    }
}

internal class TextBlock : UiElement
{
    private TextBlockState _state;

    internal ref TextBlockState State => ref _state;

    public TextBlock()
    {
        _state.Text = string.Empty;
        _state.Visual.FontKey = "default";
        _state.Visual.GlyphRunKey = "default";
        _state.Visual.FontSize = 14;
        _state.Visual.Foreground = new(255, 255, 255);
        SetDefaultProperty("Text", _state.Text, UiDirtyFlags.Measure | UiDirtyFlags.Visual, ApplyTextValue);
        SetDefaultProperty("FontKey", _state.Visual.FontKey, UiDirtyFlags.Measure | UiDirtyFlags.Visual, ApplyFontKeyValue);
        SetDefaultProperty("FontSize", _state.Visual.FontSize, UiDirtyFlags.Measure | UiDirtyFlags.Visual, ApplyFontSizeValue);
        SetDefaultProperty("Foreground", _state.Visual.Foreground, UiDirtyFlags.Visual, ApplyForegroundValue);
    }

    public override string TypeName => "TextBlock";

    public string Text { get => _state.Text; set { ArgumentNullException.ThrowIfNull(value); if (_state.Text != value) { SetLocalProperty("Text", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual, ApplyTextValue); } } }
    public string FontKey { get => _state.Visual.FontKey; set { ArgumentNullException.ThrowIfNull(value); if (_state.Visual.FontKey != value) { SetLocalProperty("FontKey", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual, ApplyFontKeyValue); } } }
    public string GlyphRunKey { get => _state.Visual.GlyphRunKey; set { ArgumentNullException.ThrowIfNull(value); if (_state.Visual.GlyphRunKey == value) { return; } _state.Visual.GlyphRunKey = value; Invalidate(UiDirtyFlags.Visual); } }
    public float FontSize { get => _state.Visual.FontSize; set { if (!_state.Visual.FontSize.Equals(value)) { SetLocalProperty("FontSize", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual, ApplyFontSizeValue); } } }
    public UiColor Foreground { get => _state.Visual.Foreground; set { if (_state.Visual.Foreground != value) { SetLocalProperty("Foreground", value, UiDirtyFlags.Visual, ApplyForegroundValue); } } }

    protected override void ApplyStyleValue(string name, object? value)
    {
        switch (name)
        {
            case "Foreground": ApplyForegroundValue(value); break;
            case "FontSize": ApplyFontSizeValue(value); break;
            case "FontKey": ApplyFontKeyValue(value); break;
            default: base.ApplyStyleValue(name, value); break;
        }
    }

    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        UiTextBlockGenerated.Measure(ref _state, new(available, LayoutScale));
        DesiredSize = RequestedSize(_state.Layout.DesiredSize);
        DirtyFlags &= ~UiDirtyFlags.Measure;
    }

    public override void Arrange(UiRect bounds)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            Bounds = default;
            Clip = default;
            UiTextBlockGenerated.Arrange(ref _state, new(default, default));
            DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
            return;
        }

        Bounds = bounds;
        Clip = bounds;
        UiTextBlockGenerated.Arrange(ref _state, new(bounds, bounds));
        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }

    internal override bool TryGetTextRun(out UiTextRun run)
    {
        run = UiTextBlockGenerated.EmitVisual(ref _state, new(Id, Generation, LayoutScale, TextRunVersion));
        return true;
    }

    protected override string GetAutomationValueText() => _state.Text;
    protected override string GetTextRunKey() => _state.Visual.GlyphRunKey;

    internal override Type BindingTargetType(string propertyName) => propertyName switch
    {
        "Text" or "FontKey" => typeof(string),
        "FontSize" => typeof(float),
        "Foreground" => typeof(UiColor),
        _ => base.BindingTargetType(propertyName),
    };
    internal override void ApplyBindingValue(string propertyName, object? value)
    {
        switch (propertyName)
        {
            case "Text": ApplyTextValue(value); break;
            case "FontKey": ApplyFontKeyValue(value); break;
            case "FontSize": ApplyFontSizeValue(value); break;
            case "Foreground": ApplyForegroundValue(value); break;
            default: base.ApplyBindingValue(propertyName, value); break;
        }
    }

    private void ApplyTextValue(object? value) { if (value is string text && UiTextBlockGenerated.TrySetText(ref _state, text)) { Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual); } }
    private void ApplyFontKeyValue(object? value) { if (value is string fontKey && UiTextBlockGenerated.TrySetFontKey(ref _state, fontKey)) { Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual); } }
    private void ApplyFontSizeValue(object? value) { if (value is float fontSize && UiTextBlockGenerated.TrySetFontSize(ref _state, fontSize)) { Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual); } }
    private void ApplyForegroundValue(object? value) { if (value is UiColor foreground && UiTextBlockGenerated.TrySetForeground(ref _state, foreground)) { Invalidate(UiDirtyFlags.Visual); } }
}

internal class TextBox : TextBlock
{
    private TextBoxState _state;
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    internal new ref TextBoxState State => ref _state;

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

        if (HasBinding("Text"))
        {
            ApplyBindingValue("Text", text);
        }
        else
        {
            Text = text;
        }
        _state.CaretIndex = Math.Min(_state.CaretIndex, Text.Length);
        _state.SelectionStart = _state.CaretIndex;
        _state.SelectionLength = 0;
        Diagnostic = null;
        SetInvalid(false);
        Invalidate(UiDirtyFlags.Binding);
        Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        NotifyBindingTargetChanged("Text", Text);
        TextChanged?.Invoke(this, new TextChangedEventArgs(Text));
    }
    public bool ApplyText(in UiTextInput input) { ReplaceSelection(input.Text); return true; }
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
    public bool Undo() { if (_undo.Count == 0) { return false; } _redo.Add(Text); Text = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _state.CaretIndex = Text.Length; _state.SelectionStart = Text.Length; _state.SelectionLength = 0; Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual); NotifyBindingTargetChanged("Text", Text); TextChanged?.Invoke(this, new TextChangedEventArgs(Text)); return true; }
    public bool Redo() { if (_redo.Count == 0) { return false; } _undo.Add(Text); Text = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); _state.CaretIndex = Text.Length; _state.SelectionStart = Text.Length; _state.SelectionLength = 0; Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual); NotifyBindingTargetChanged("Text", Text); TextChanged?.Invoke(this, new TextChangedEventArgs(Text)); return true; }
    protected void ReplaceSelection(string inserted)
    {
        ArgumentNullException.ThrowIfNull(inserted);
        PushUndo();
        if (HasSelection())
        {
            DeleteRange(_state.SelectionStart, _state.SelectionLength, false);
        }

        Text = Text.Insert(_state.CaretIndex, inserted);
        _state.CaretIndex += inserted.Length;
        _state.SelectionStart = _state.CaretIndex; _state.SelectionLength = 0;
        Diagnostic = null;
        SetInvalid(false);
        Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        NotifyBindingTargetChanged("Text", Text);
        TextChanged?.Invoke(this, new TextChangedEventArgs(Text));
    }
    private void PushUndo() { _undo.Add(Text); _redo.Clear(); }
    private void DeleteRange(int start, int length, bool record = true) { if (record) { PushUndo(); } Text = Text.Remove(start, length); _state.CaretIndex = start; _state.SelectionStart = start; _state.SelectionLength = 0; Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual); NotifyBindingTargetChanged("Text", Text); TextChanged?.Invoke(this, new TextChangedEventArgs(Text)); }
    private bool HasSelection() => _state.SelectionLength > 0;
    private string GetSelection() => Text.Substring(_state.SelectionStart, _state.SelectionLength);
    protected override string GetAutomationValueText() => Text;
    protected override string GetTextRunKey() => GlyphRunKey + ":" + Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class NumericEditor : TextBox
{
    private NumericEditorState _state = new() { Min = double.MinValue, Max = double.MaxValue, CommittedText = string.Empty };

    internal new ref NumericEditorState State => ref _state;

    public override string TypeName => "NumericEditor";
    public double Value => _state.Value;
    public double Min { get => _state.Min; set => _state.Min = value; }
    public double Max { get => _state.Max; set => _state.Max = value; }
    public bool HasValidationError => Diagnostic is not null;
    public bool IsDirty => Text != _state.CommittedText;
    public void Initialize(double value) { _state.Value = value; _state.CommittedText = Format(value); SetText(_state.CommittedText, false); }
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
    internal override void ApplyBindingValue(string propertyName, object? value)
    {
        if (propertyName == "Value" && value is double numeric)
        {
            TryApplyValue(Format(numeric), out _);
            return;
        }

        base.ApplyBindingValue(propertyName, value);
    }
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
            Invalidate(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }

    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            _state.DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        UiScrollViewerGenerated.Measure(ref _state, new(available, LayoutScale, Children));
        DesiredSize = RequestedSize(_state.DesiredSize);
        DirtyFlags &= ~UiDirtyFlags.Measure;
    }

    public override void Arrange(UiRect bounds)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            Bounds = default;
            Clip = default;
            _state.Bounds = default;
            _state.Clip = default;
            DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
            return;
        }

        UiScrollViewerGenerated.Arrange(ref _state, new(bounds, bounds, Children));
        Bounds = _state.Bounds;
        Clip = _state.Clip;
        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
}
