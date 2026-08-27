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
    public bool IsPressed { get; protected set; }
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
    public void SetPressed(bool value) { if (IsPressed != value) { IsPressed = value; Invalidate(UiDirtyFlags.Visual); } }
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
    public override string TypeName => "Panel";
    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        var w = 0f;
        var h = 0f;
        foreach (var child in Children)
        {
            child.Measure(available);
            w = MathF.Max(w, child.DesiredSize.Width);
            h = MathF.Max(h, child.DesiredSize.Height);
        }

        DesiredSize = RequestedSize(new(w, h));
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

        Bounds = bounds;
        Clip = bounds;
        foreach (var child in Children)
        {
            child.Arrange(bounds);
        }

        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
}

internal sealed class StackPanel : Panel
{
    public override string TypeName => "StackPanel"; public UiOrientation Orientation { get; set; } = UiOrientation.Vertical;
    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        var w = 0f;
        var h = 0f;
        foreach (var child in Children)
        {
            child.Measure(available);
            if (Orientation == UiOrientation.Horizontal)
            {
                w += child.DesiredSize.Width;
                h = MathF.Max(h, child.DesiredSize.Height);
            }
            else
            {
                w = MathF.Max(w, child.DesiredSize.Width);
                h += child.DesiredSize.Height;
            }
        }

        DesiredSize = RequestedSize(new(w, h));
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

        Bounds = bounds;
        Clip = bounds;
        var cursor = Orientation == UiOrientation.Horizontal ? bounds.X : bounds.Y;
        var fixedSize = 0f;
        var fillCount = 0;
        foreach (var child in Children)
        {
            if (child.Fill)
            {
                fillCount++;
            }
            else
            {
                fixedSize += Orientation == UiOrientation.Horizontal ? child.DesiredSize.Width : child.DesiredSize.Height;
            }
        }

        var available = Orientation == UiOrientation.Horizontal ? bounds.Width : bounds.Height;
        var remaining = MathF.Max(0, available - fixedSize);
        foreach (var child in Children)
        {
            var desired = child.DesiredSize;
            var main = child.Fill && fillCount > 0
                ? remaining / fillCount
                : Orientation == UiOrientation.Horizontal ? desired.Width : desired.Height;
            child.Arrange(Orientation == UiOrientation.Horizontal
                ? new UiRect(cursor, bounds.Y, main, bounds.Height)
                : new UiRect(bounds.X, cursor, bounds.Width, main));
            cursor += main;
        }

        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
}

internal sealed class ItemsControl : Panel
{
    private readonly List<object?> _items = new();
    private readonly List<UiElement> _realized = new();
    public override string TypeName => "ItemsControl";
    public IReadOnlyList<object?> Items => _items;
    public IReadOnlyList<UiElement> RealizedItems => _realized;

    public void SetItems(IReadOnlyList<object?> items, Func<object?, UiElement> factory)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(factory);
        var next = new List<UiElement>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            if (i < _items.Count && Equals(_items[i], items[i]))
            {
                next.Add(_realized[i]);
            }
            else
            {
                next.Add(factory(items[i]));
            }
        }

        ClearChildren();
        _items.Clear();
        _items.AddRange(items);
        _realized.Clear();
        _realized.AddRange(next);
        foreach (var child in _realized)
        {
            Add(child);
        }
    }
}

internal class Border : UiElement, IUiPanel
{
    public override string TypeName => "Border"; public IUiElement? Child => Children.Count == 0 ? null : Children[0];
    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        if (Child is not null)
        {
            Child.Measure(new(MathF.Max(0, available.Width - Padding.Horizontal), MathF.Max(0, available.Height - Padding.Vertical)));
            DesiredSize = RequestedSize(new(Child.DesiredSize.Width + Padding.Horizontal, Child.DesiredSize.Height + Padding.Vertical));
        }
        else
        {
            DesiredSize = RequestedSize(new(Padding.Horizontal, Padding.Vertical));
        }

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

        Bounds = bounds;
        Clip = bounds;
        Child?.Arrange(new(bounds.X + Padding.Left, bounds.Y + Padding.Top, MathF.Max(0, bounds.Width - Padding.Horizontal), MathF.Max(0, bounds.Height - Padding.Vertical)));
        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
}

internal readonly record struct GridLength(float Value, GridUnitType Type) { public static GridLength Fixed(float v) => new(v, GridUnitType.Pixel); public static GridLength Auto => new(1, GridUnitType.Auto); public static GridLength Star(float weight = 1) => new(weight, GridUnitType.Star); }
internal enum GridUnitType { Pixel, Auto, Star }

internal sealed class Grid : UiElement, IUiPanel
{
    private GridLength[] _columns = Array.Empty<GridLength>();
    private GridLength[] _rows = Array.Empty<GridLength>();
    private float[] _measuredColumns = Array.Empty<float>();
    private float[] _measuredRows = Array.Empty<float>();
    private float[] _resolvedColumns = Array.Empty<float>();
    private float[] _resolvedRows = Array.Empty<float>();
    public override string TypeName => "Grid"; public int ColumnCount => _columns.Length; public int RowCount => _rows.Length;
    public void SetColumns(params GridLength[] columns) { ArgumentNullException.ThrowIfNull(columns); _columns = columns; Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Arrange); }
    public void SetRows(params GridLength[] rows) { ArgumentNullException.ThrowIfNull(rows); _rows = rows; Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Arrange); }
    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        foreach (var child in Children)
        {
            child.Measure(available);
        }

        AutoSizes(_columns, true, ref _measuredColumns);
        AutoSizes(_rows, false, ref _measuredRows);
        DesiredSize = RequestedSize(new(Sum(_measuredColumns, _columns.Length), Sum(_measuredRows, _rows.Length)));
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

        Bounds = bounds;
        Clip = bounds;
        var columns = Resolve(_columns, bounds.Width, _measuredColumns, ref _resolvedColumns);
        var rows = Resolve(_rows, bounds.Height, _measuredRows, ref _resolvedRows);
        for (var i = 0; i < Children.Count; i++)
        {
            var column = i % Math.Max(1, columns.Length);
            var row = i / Math.Max(1, columns.Length);
            if (row >= rows.Length)
            {
                break;
            }

            var x = bounds.X + Sum(columns, column);
            var y = bounds.Y + Sum(rows, row);
            Children[i].Arrange(new(x, y, columns[column], rows[row]));
        }

        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
    private void AutoSizes(GridLength[] defs, bool columns, ref float[] result)
    {
        if (defs.Length == 0)
        {
            return;
        }

        Ensure(ref result, defs.Length); Array.Clear(result, 0, defs.Length); for (var i = 0; i < defs.Length; i++)
        {
            if (defs[i].Type == GridUnitType.Pixel)
            {
                result[i] = defs[i].Value;
            }
            else if (defs[i].Type == GridUnitType.Auto)
            {
                for (var child = 0; child < Children.Count; child++)
                {
                    if ((columns ? child % defs.Length : child / defs.Length) == i)
                    {
                        result[i] = MathF.Max(result[i], columns ? Children[child].DesiredSize.Width : Children[child].DesiredSize.Height);
                    }
                }
            }
        }
    }
    private static float[] Resolve(GridLength[] defs, float available, float[] measured, ref float[] result)
    {
        if (defs.Length == 0) { Ensure(ref result, 1); result[0] = available; return result; }
        Ensure(ref result, defs.Length); Array.Clear(result, 0, defs.Length); var rest = available; var stars = 0f; for (var i = 0; i < defs.Length; i++)
        {
            if (defs[i].Type == GridUnitType.Pixel)
            {
                result[i] = defs[i].Value;
            }
            else if (defs[i].Type == GridUnitType.Auto)
            {
                result[i] = measured.Length > i ? measured[i] : 0;
            }
            else
            {
                stars += defs[i].Value;
            }
            rest -= result[i];
        }
        if (stars > 0) { for (var i = 0; i < defs.Length; i++) { if (defs[i].Type == GridUnitType.Star) { result[i] = MathF.Max(0, rest) * defs[i].Value / stars; } } }
        return result;
    }
    private static void Ensure(ref float[] values, int count)
    {
        if (values.Length < count)
        {
            Array.Resize(ref values, Math.Max(count, Math.Max(4, values.Length * 2)));
        }
    }
    private static float Sum(float[] values, int count) { var total = 0f; for (var i = 0; i < count; i++) { total += values[i]; } return total; }
}

internal class ContentControl : UiElement
{
    public override string TypeName => "ContentControl"; public IUiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
    public override void Measure(UiSize available)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            DesiredSize = default;
            DirtyFlags &= ~UiDirtyFlags.Measure;
            return;
        }

        Content?.Measure(available);
        DesiredSize = RequestedSize(Content?.DesiredSize ?? new());
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

        Bounds = bounds;
        Clip = bounds;
        Content?.Arrange(bounds);
        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
}

internal class Button : ContentControl, IUiRoutedEventSink
{
    public override string TypeName => "Button"; public Button() { Focusable = true; AutomationRole = UiAutomationRole.Button; }
    public event EventHandler? Click;
    public virtual void OnRoutedEvent(in UiRoutedEvent routedEvent) { if (routedEvent.Phase == UiRoutedEventPhase.Bubble && routedEvent.Kind == UiPointerEventKind.Down) { SetPressed(true); } if (routedEvent.Phase == UiRoutedEventPhase.Bubble && routedEvent.Kind == UiPointerEventKind.Up) { SetPressed(false); Click?.Invoke(this, EventArgs.Empty); } }
    protected override string GetAutomationValueText() => Content is TextBlock t ? t.Text : string.Empty;
}

internal sealed class ToggleButton : Button
{
    public bool IsChecked { get; private set; }
    public override void OnRoutedEvent(in UiRoutedEvent routedEvent)
    {
        base.OnRoutedEvent(routedEvent); if (routedEvent.Phase == UiRoutedEventPhase.Bubble && routedEvent.Kind == UiPointerEventKind.Up)
        {
            IsChecked = !IsChecked;
        }
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
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    public override string TypeName => "TextBox"; public int CaretIndex { get; private set; }
    public int SelectionStart { get; private set; }
    public int SelectionLength { get; private set; }
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
        CaretIndex = Math.Min(CaretIndex, Text.Length);
        SelectionLength = 0;
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
        if (!input.IsDown)
        {
            return false;
        }

        if (input.Control)
        {
            return ApplyCommand(input.PhysicalKey);
        }

        if (input.PhysicalKey == 8 && CaretIndex > 0) { PushUndo(); DeleteRange(CaretIndex - 1, 1); return true; }
        if (input.PhysicalKey == 46 && CaretIndex < Text.Length) { PushUndo(); DeleteRange(CaretIndex, 1); return true; }
        if (input.PhysicalKey == 37 && CaretIndex > 0) { CaretIndex--; SelectionLength = 0; return true; }
        if (input.PhysicalKey == 39 && CaretIndex < Text.Length) { CaretIndex++; SelectionLength = 0; return true; }
        return false;
    }
    public void SelectAll() { SelectionStart = 0; SelectionLength = Text.Length; CaretIndex = Text.Length; }
    public void Copy()
    {
        if (Clipboard is not null && HasSelection())
        {
            Clipboard.SetText(GetSelection());
        }
    }
    public void Cut() { if (!HasSelection()) { return; } PushUndo(); if (Clipboard is not null) { Clipboard.SetText(GetSelection()); } DeleteRange(SelectionStart, SelectionLength); }
    public bool Paste() { if (Clipboard?.ReadText() is not { Length: > 0 } text) { return false; } ReplaceSelection(text); return true; }
    public bool Undo() { if (_undo.Count == 0) { return false; } _redo.Add(Text); Text = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); CaretIndex = Text.Length; SelectionLength = 0; Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual); NotifyBindingTargetChanged("Text", Text); TextChanged?.Invoke(this, new TextChangedEventArgs(Text)); return true; }
    public bool Redo() { if (_redo.Count == 0) { return false; } _undo.Add(Text); Text = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); CaretIndex = Text.Length; SelectionLength = 0; Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual); NotifyBindingTargetChanged("Text", Text); TextChanged?.Invoke(this, new TextChangedEventArgs(Text)); return true; }
    protected void ReplaceSelection(string inserted)
    {
        ArgumentNullException.ThrowIfNull(inserted);
        PushUndo();
        if (HasSelection())
        {
            DeleteRange(SelectionStart, SelectionLength, false);
        }

        Text = Text.Insert(CaretIndex, inserted);
        CaretIndex += inserted.Length;
        SelectionStart = CaretIndex; SelectionLength = 0;
        Diagnostic = null;
        SetInvalid(false);
        Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        NotifyBindingTargetChanged("Text", Text);
        TextChanged?.Invoke(this, new TextChangedEventArgs(Text));
    }
    private bool ApplyCommand(int physicalKey)
    {
        return physicalKey switch
        {
            65 => SelectAllCommand(),
            67 => CopyCommand(),
            88 => CutCommand(),
            86 => PasteCommand(),
            90 => UndoCommand(),
            89 => RedoCommand(),
            _ => false
        };
        bool SelectAllCommand() { SelectAll(); return true; }
        bool CopyCommand() { Copy(); return true; }
        bool CutCommand() { Cut(); return true; }
        bool PasteCommand() { return Paste(); }
        bool UndoCommand() { return Undo(); }
        bool RedoCommand() { return Redo(); }
    }
    private void PushUndo() { _undo.Add(Text); _redo.Clear(); }
    private void DeleteRange(int start, int length, bool record = true) { if (record) { PushUndo(); } Text = Text.Remove(start, length); CaretIndex = start; SelectionStart = start; SelectionLength = 0; Invalidate(UiDirtyFlags.Measure | UiDirtyFlags.Visual); NotifyBindingTargetChanged("Text", Text); TextChanged?.Invoke(this, new TextChangedEventArgs(Text)); }
    private bool HasSelection() => SelectionLength > 0;
    private string GetSelection() => Text.Substring(SelectionStart, SelectionLength);
    protected override string GetAutomationValueText() => Text;
    protected override string GetTextRunKey() => GlyphRunKey + ":" + Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class NumericEditor : TextBox
{
    private string _committedText = "";
    public override string TypeName => "NumericEditor"; public double Value { get; private set; }
    public double Min { get; set; } = double.MinValue; public double Max { get; set; } = double.MaxValue; public bool HasValidationError => Diagnostic is not null; public bool IsDirty => Text != _committedText;
    public void Initialize(double value) { Value = value; _committedText = Format(value); SetText(_committedText, false); }
    public bool TryCommit()
    {
        if (!double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v) || v < Min || v > Max) { Diagnostic = $"Value must be between {Min.ToString(CultureInfo.InvariantCulture)} and {Max.ToString(CultureInfo.InvariantCulture)}."; return false; }
        Value = v; _committedText = Format(v); SetText(_committedText, false); Diagnostic = null; return true;
    }
    public void CancelEdit() { SetText(_committedText, false); Diagnostic = null; }
    public bool TryCommitText(string text) { SetText(text); return TryCommit(); }
    public bool Increment(double step = 1) { return Adjust(step); }
    public bool Decrement(double step = 1) { return Adjust(-step); }
    public override bool ApplyKey(in UiKeyEvent input)
    {
        if (!input.IsDown)
        {
            return false;
        }

        if (input.PhysicalKey == 38) { return Increment(); }
        if (input.PhysicalKey == 40) { return Decrement(); }
        return base.ApplyKey(input);
    }
    public bool TryApplyValue(string text, out string? error)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) && v >= Min && v <= Max) { Value = v; _committedText = Format(v); SetText(_committedText, false); Diagnostic = null; SetInvalid(false); error = null; return true; }
        Diagnostic = $"Value must be between {Min.ToString(CultureInfo.InvariantCulture)} and {Max.ToString(CultureInfo.InvariantCulture)}."; SetInvalid(true); error = Diagnostic; return false;
    }
    private bool Adjust(double delta) { var next = Value + delta; if (next < Min || next > Max) { Diagnostic = $"Value must be between {Min.ToString(CultureInfo.InvariantCulture)} and {Max.ToString(CultureInfo.InvariantCulture)}."; SetInvalid(true); return false; } Value = next; _committedText = Format(next); SetText(_committedText, false); Diagnostic = null; SetInvalid(false); return true; }
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
    public override string TypeName => "ScrollViewer"; public UiPoint Offset { get; private set; }
    public void ScrollBy(float x, float y) { Offset = new(MathF.Max(0, Offset.X + x), MathF.Max(0, Offset.Y + y)); Invalidate(UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public override void Arrange(UiRect bounds)
    {
        if (!ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            Bounds = default;
            Clip = default;
            DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
            return;
        }

        Bounds = bounds;
        Clip = bounds;
        Content?.Arrange(new(bounds.X - Offset.X, bounds.Y - Offset.Y, Content.DesiredSize.Width, Content.DesiredSize.Height));
        DirtyFlags &= ~(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }
}
