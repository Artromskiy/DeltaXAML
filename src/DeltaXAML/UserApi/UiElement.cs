using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Delta.Diagnostics;
using Delta;
using Delta.Text.Contract;
using Delta.XAML.Contract;
using Retained = DeltaXAML.Internal;
using RetainedDirty = DeltaXAML.Internal.UiDirtyMask;
using RetainedElement = DeltaXAML.Internal.UiElement;

namespace Delta.XAML;

public readonly record struct UiPropertyId(Guid Value) { public bool IsValid => Value != Guid.Empty; }
public readonly record struct UiTypeId(Guid Value) { public bool IsValid => Value != Guid.Empty; }
public readonly record struct UiTemplateId(Guid Value)
{
    public static UiTemplateId Empty => default;

    public bool IsValid => Value != Guid.Empty;
}
public readonly record struct UiStyleId(Guid Value)
{
    public static UiStyleId Empty => default;

    public bool IsValid => Value != Guid.Empty;
}
public readonly record struct XamlQualifiedName(string Namespace, string LocalName);
public readonly record struct UiColor(byte R, byte G, byte B, byte A = 255);
public readonly record struct UiThickness(float Left, float Top, float Right, float Bottom)
{
    public static UiThickness Zero => default;
}

/// <summary>Four logical corner radii in top-left, top-right, bottom-right, bottom-left order.</summary>
public readonly record struct UiCornerRadii(
    float TopLeft,
    float TopRight,
    float BottomRight,
    float BottomLeft)
{
    public static UiCornerRadii Zero => default;

    public static UiCornerRadii Uniform(float radius) => new(radius, radius, radius, radius);

    public static UiCornerRadii FromSingle(float radius) => Uniform(radius);

    public bool IsFiniteNonNegative =>
        float.IsFinite(TopLeft) && TopLeft >= 0 &&
        float.IsFinite(TopRight) && TopRight >= 0 &&
        float.IsFinite(BottomRight) && BottomRight >= 0 &&
        float.IsFinite(BottomLeft) && BottomLeft >= 0;

    public static implicit operator UiCornerRadii(float radius) => Uniform(radius);
}

/// <summary>Opaque generation-safe property target for low-boilerplate host writes.</summary>
public readonly struct UiPropertyHandle : IEquatable<UiPropertyHandle>
{
    private readonly Retained.UiPropertyHandle _retained;

    internal UiPropertyHandle(Retained.UiPropertyHandle retained) => _retained = retained;

    public string Name => _retained.Name ?? string.Empty;
    public bool IsValid => _retained.Element.IsValid && _retained.Generation != 0 && !string.IsNullOrWhiteSpace(_retained.Name);
    internal Retained.UiPropertyHandle Retained => _retained;
    public bool Equals(UiPropertyHandle other) => _retained == other._retained;
    public override bool Equals(object? obj) => obj is UiPropertyHandle other && Equals(other);
    public override int GetHashCode() => _retained.GetHashCode();
    public static bool operator ==(UiPropertyHandle left, UiPropertyHandle right) => left.Equals(right);
    public static bool operator !=(UiPropertyHandle left, UiPropertyHandle right) => !left.Equals(right);
}

internal static class BindingModeMap
{
    internal static UiBindingMode ToPublic(Retained.UiBindingMode mode) => mode switch
    {
        Retained.UiBindingMode.OneTime => UiBindingMode.OneTime,
        Retained.UiBindingMode.OneWay => UiBindingMode.OneWay,
        Retained.UiBindingMode.TwoWay => UiBindingMode.TwoWay,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported binding mode."),
    };
}

[Flags]
public enum UiParticipation
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

public enum UiBindingMode { OneTime, OneWay, TwoWay }

public interface IUiBinding
{
    Type ValueType { get; }
    UiBindingMode Mode { get; }
    object? Read();
    bool TryWrite(object? value, [NotNullWhen(false)] out Diagnostic? diagnostic);
}

public interface IUiBinding<T> : IUiBinding
{
    T ReadValue();
    bool TryWriteValue(T value, [NotNullWhen(false)] out Diagnostic? diagnostic);
}

public interface IUiResourceResolver
{
    bool TryResolve(UiResourceId resource, out object? value);
}

public interface IXamlTypeResolver
{
    bool TryResolveName(in XamlQualifiedName name, out UiTypeId type);
    bool TryCreate(UiTypeId type, [NotNullWhen(true)] out UiElement? element);
}

public readonly record struct XamlLoadContext(
    IXamlTypeResolver Types,
    IUiResourceResolver Resources,
    IUiBindingResolver? Bindings = null);

/// <summary>Stable user-facing identity and accessor shell for one retained node.</summary>
public abstract class UiElement
{
    private readonly RetainedElement _retained;
    private Dictionary<RetainedElement, UiElement> _views;
    private RetainedChildrenView _childrenView;
    private RetainedChildrenEditor _childrenEditor;
    private UiElement? _templateContent;
    private UiTemplate? _appliedTemplate;
    protected UiElement() : this(new RetainedElement(), null) { }
    internal UiElement(RetainedElement retained, Dictionary<RetainedElement, UiElement>? views)
    {
        ArgumentNullException.ThrowIfNull(retained);
        _retained = retained;
        SetViewCache(views ?? new Dictionary<RetainedElement, UiElement>());
    }
    internal RetainedElement RetainedElement => _retained;

    public uint ElementId => _retained.Id.Value;

    public uint Generation => _retained.Generation;

    public float4 Bounds => new(_retained.Bounds.X, _retained.Bounds.Y, _retained.Bounds.Width, _retained.Bounds.Height);

    public bool IsFocused => _retained.IsFocused;

    public bool IsInvalid => _retained.IsInvalid;

    internal UiElement WrapRetained(RetainedElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Wrap(element, _views);
    }

    public UiElement? Parent => _retained.Parent is RetainedElement parent ? Wrap(parent, _views) : null;
    public IReadOnlyList<UiElement> Children => _childrenView;
    protected IList<UiElement> MutableChildren => _childrenEditor;

    /// <summary>Explicit binding source; descendants inherit it until they set their own context.</summary>
    public object? BindingContext
    {
        get => _retained.BindingContext;
        set => _retained.SetBindingContext(value, true);
    }

    public float Width
    {
        get => _retained.Width;
        set => _retained.Width = value;
    }

    public float Height
    {
        get => _retained.Height;
        set => _retained.Height = value;
    }

    /// <summary>Logical space outside this element's arranged bounds.</summary>
    public UiThickness Margin
    {
        get
        {
            var value = _retained.Margin;
            return new UiThickness(value.Left, value.Top, value.Right, value.Bottom);
        }
        set => _retained.Margin = new Retained.UiThickness(value.Left, value.Top, value.Right, value.Bottom);
    }

    /// <summary>Horizontal placement of this element in its parent slot.</summary>
    public UiHorizontalAlignment HorizontalAlignment
    {
        get => _retained.HorizontalAlignment;
        set => _retained.HorizontalAlignment = value;
    }

    /// <summary>Vertical placement of this element in its parent slot.</summary>
    public UiVerticalAlignment VerticalAlignment
    {
        get => _retained.VerticalAlignment;
        set => _retained.VerticalAlignment = value;
    }

    public string TypeName => _retained.TypeName;

    public string? StyleKey
    {
        get => _retained.StyleKey;
        set => _retained.StyleKey = value;
    }

    /// <summary>Optional semantic style variant selected together with StyleKey.</summary>
    public string? Variant
    {
        get => _retained.Variant;
        set => _retained.Variant = value;
    }

    public string? TemplateKey
    {
        get => _retained.TemplateKey;
        set => _retained.TemplateKey = value;
    }

    /// <summary>Attaches a generated template by stable identity.</summary>
    public void SetCompiledTemplate(UiTemplateId template)
    {
        if (!template.IsValid)
        {
            throw new ArgumentException("A stable template identity is required.", nameof(template));
        }

        _retained.SetCompiledTemplate(template);
    }

    /// <summary>Attaches a generated style by its stable compiled identity.</summary>
    public void SetCompiledStyle(UiStyleId style)
    {
        if (!style.IsValid)
        {
            throw new ArgumentException("A stable style identity is required.", nameof(style));
        }

        _retained.SetCompiledStyle(style.Value);
    }

    public bool IsEnabled
    {
        get => _retained.IsEnabled;
        set => _retained.IsEnabled = value;
    }

    public bool IsSelected
    {
        get => _retained.IsSelected;
        set => _retained.IsSelected = value;
    }

    public UiColor Background
    {
        get => ToPublicColor(_retained.Background);
        set => _retained.Background = ToRetainedColor(value);
    }

    /// <summary>Renderer-neutral border color used with <see cref="BorderWidth"/>.</summary>
    public UiColor BorderColor
    {
        get => ToPublicColor(_retained.BorderColor);
        set => _retained.BorderColor = ToRetainedColor(value);
    }

    /// <summary>Gets or sets the immutable prepared effect set for this element.</summary>
    /// <remarks>Effect outsets affect paint bounds and damage, not layout size.</remarks>
    public UiEffectSet EffectSet
    {
        get => _retained.EffectSet;
        set
        {
            if (value != UiEffectSet.None && !value.IsValid)
            {
                throw new ArgumentException("EffectSet must be empty or a valid prepared effect resource.", nameof(value));
            }

            _retained.EffectSet = value;
        }
    }

    /// <summary>Gets or sets the renderer-neutral compositing mode for this element's paint.</summary>
    public UiBlendMode BlendMode
    {
        get => _retained.BlendMode;
        set
        {
            if (value is not (UiBlendMode.Opaque or UiBlendMode.Alpha or UiBlendMode.PremultipliedAlpha or UiBlendMode.Additive or UiBlendMode.Multiply))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _retained.BlendMode = value;
        }
    }

    /// <summary>Uniform border width; zero disables the stroke.</summary>
    /// <remarks>Use <see cref="BorderWidthUnits"/> to keep the width in device pixels for hairlines.</remarks>
    public float BorderWidth
    {
        get => _retained.BorderWidth;
        set
        {
            ValidatePaintDimension(value, nameof(value));
            _retained.BorderWidth = value;
        }
    }

    /// <summary>Per-side border widths in left, top, right, bottom order.</summary>
    /// <remarks>Zero leaves the legacy uniform <see cref="BorderWidth"/> value in effect.</remarks>
    public UiThickness BorderThickness
    {
        get
        {
            var thickness = _retained.BorderThickness;
            return new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
        }
        set
        {
            ValidateNonNegativeThickness(value, nameof(value));
            _retained.BorderThickness = new(value.Left, value.Top, value.Right, value.Bottom);
        }
    }

    /// <summary>Gets or sets whether <see cref="BorderWidth"/> is logical or device-pixel sized.</summary>
    public PaintUnits BorderWidthUnits
    {
        get => _retained.BorderWidthUnits;
        set
        {
            if (value is not (PaintUnits.Logical or PaintUnits.Device))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _retained.BorderWidthUnits = value;
        }
    }

    /// <summary>Four corner radii in logical units; they do not implicitly clip children.</summary>
    /// <remarks>The XAML property name remains <c>CornerRadius</c>; a scalar value is expanded uniformly.</remarks>
    public UiCornerRadii CornerRadius
    {
        get => _retained.CornerRadius;
        set
        {
            ValidateCornerRadii(value, nameof(value));
            _retained.CornerRadius = value;
        }
    }

    /// <summary>Renderer-neutral paint. Gradients and images remain stable resource references.</summary>
    public UiBrush BackgroundBrush
    {
        get
        {
            if (!_retained.HasCustomVisual)
            {
                return UiBrush.Solid(Background);
            }

            var kind = _retained.CustomVisualTypeId == UiKnownVisuals.LinearGradient.Value
                ? UiBrushKind.LinearGradient
                : _retained.CustomVisualTypeId == UiKnownVisuals.RadialGradient.Value
                    ? UiBrushKind.RadialGradient
                    : UiBrushKind.Image;
            return new(kind, ToPublicColor(_retained.CustomVisualColor), new UiResourceId(_retained.CustomVisualResourceId));
        }
        set
        {
            switch (value.Kind)
            {
                case UiBrushKind.None:
                    _retained.ClearCustomVisual();
                    Background = default;
                    break;
                case UiBrushKind.Solid:
                    _retained.ClearCustomVisual();
                    Background = value.Color;
                    break;
                case UiBrushKind.LinearGradient:
                    SetCustomVisual(UiKnownVisuals.LinearGradient, value.Resource, value.Color);
                    break;
                case UiBrushKind.RadialGradient:
                    SetCustomVisual(UiKnownVisuals.RadialGradient, value.Resource, value.Color);
                    break;
                case UiBrushKind.Image:
                    SetCustomVisual(new UiVisualTypeId(new Guid("3419D85F-C401-4DD8-86DD-D2A68359D303")), value.Resource, value.Color);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), "Unknown brush kinds cannot enter retained state.");
            }
        }
    }

    public string? AutomationName
    {
        get => _retained.AutomationName;
        set => _retained.AutomationName = value;
    }

    public UiSemanticRole AutomationRole
    {
        get => ToPublicRole(_retained.AutomationRole);
        set => _retained.AutomationRole = ToRetainedRole(value);
    }

    /// <summary>Stable semantic custom-visual identity consumed by a renderer adapter.</summary>
    public UiVisualTypeId CustomVisualType => new(_retained.CustomVisualTypeId);

    /// <summary>Stable resource identity carried by the custom-visual command.</summary>
    public UiResourceId CustomVisualResource => new(_retained.CustomVisualResourceId);

    /// <summary>Sets renderer-neutral custom visual data; shader and pipeline resolution stay external.</summary>
    public void SetCustomVisual(UiVisualTypeId visualType, UiResourceId resource, UiColor color)
    {
        if (!visualType.IsValid)
        {
            throw new ArgumentException("A custom visual identity is required.", nameof(visualType));
        }

        _retained.SetCustomVisual(visualType.Value, resource.Value, ToRetainedColor(color));
    }

    /// <summary>Removes the custom visual and restores the normal visual participation path.</summary>
    public void ClearCustomVisual() => _retained.ClearCustomVisual();

    public UiThickness Padding
    {
        get
        {
            var value = _retained.Padding;
            return new UiThickness(value.Left, value.Top, value.Right, value.Bottom);
        }
        set => _retained.Padding = new Retained.UiThickness(value.Left, value.Top, value.Right, value.Bottom);
    }

    /// <summary>Descriptor capability bits consumed by the document-owned gesture arena.</summary>
    public UiGestureKind Gestures
    {
        get => _retained.Gestures;
        set
        {
            if ((value & ~(UiGestureKind.Tap | UiGestureKind.MultipleTap | UiGestureKind.LongPress |
                UiGestureKind.Drag | UiGestureKind.Pan | UiGestureKind.Swipe | UiGestureKind.Pinch)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _retained.Gestures = value;
        }
    }

    /// <summary>Application-owned command emitted by gestures and keyboard routing.</summary>
    public UiCommandId Command
    {
        get => _retained.Command;
        set => _retained.Command = value;
    }

    public UiKeyGesture CommandKey
    {
        get => _retained.CommandKey;
        set => _retained.CommandKey = value;
    }

    /// <summary>Constrains keyboard traversal to this retained subtree while focus is inside it.</summary>
    public bool IsFocusScope
    {
        get => _retained.IsFocusScope;
        set => _retained.IsFocusScope = value;
    }
    /// <summary>Controls whether this element participates in layout, rendering and hit testing.</summary>
    public UiParticipation Participation
    {
        get => _retained.Participation;
        set
        {
            if ((value & ~UiParticipation.All) != 0 ||
                ((value & UiParticipation.Rendering) != 0 && (value & UiParticipation.Layout) == 0) ||
                ((value & UiParticipation.HitTesting) != 0 && (value & UiParticipation.Layout) == 0))
            {
                throw new ArgumentException("Participation contains unsupported flags or requires layout participation.", nameof(value));
            }

            _retained.SetParticipation(value);
        }
    }

    public UiPropertyHandle GetHandle(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return new(_retained.GetHandle(propertyName));
    }

    public bool TrySet(UiPropertyHandle handle, object? value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        if (!_retained.TrySet(handle.Retained, ToRetainedValue(value), PropertyInvalidation(handle.Name), out var message))
        {
            diagnostic = new Diagnostic(new DiagnosticCode("XAML_PROPERTY"), DiagnosticSeverity.Error, message ?? "The property write was rejected.", null);
            return false;
        }

        diagnostic = null;
        return true;
    }

    public object? GetValue(IUiProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return _retained.TryGet(property.Name, out var value) ? ToPublicValue(value.UntypedValue) : property.DefaultValue;
    }

    public bool TrySetValue(IUiProperty property, object? value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!IsCompatibleValue(property.ValueType, value))
        {
            diagnostic = new Diagnostic(new DiagnosticCode("XAML_PROPERTY"), DiagnosticSeverity.Error, $"Value is not compatible with property '{property.Name}'.", null);
            return false;
        }

        _retained.SetLocal(property.Name, ToRetainedValue(value), PropertyInvalidation(property.Name));
        diagnostic = null;
        return true;
    }

    public T GetValue<T>(IUiProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var value = GetValue((IUiProperty)property);
        return value is T typed ? typed : property.DefaultValue;
    }

    public void SetValue<T>(IUiProperty<T> property, T value)
    {
        ArgumentNullException.ThrowIfNull(property);
        _retained.SetLocal(property.Name, ToRetainedValue(value), PropertyInvalidation(property.Name));
    }

    /// <summary>Reads a typed attached-property slot without exposing retained storage.</summary>
    public T GetAttachedValue<T>(UiAttachedProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return property.Read(_retained);
    }

    /// <summary>Writes a typed attached-property slot through its generated owner descriptor.</summary>
    public void SetAttachedValue<T>(UiAttachedProperty<T> property, T value)
    {
        ArgumentNullException.ThrowIfNull(property);
        property.Write(_retained, value);
    }

    /// <summary>Attaches a programmatic binding without exposing retained invalidation flags.</summary>
    public void SetBinding(string propertyName, IUiBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(binding);
        _retained.AttachExternalBinding(propertyName, binding);
    }

    /// <summary>Attaches a compact path binding to the element's inherited context.</summary>
    public void SetBinding(string propertyName, UiBindingExpression expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(expression);
        _retained.AttachBinding(new Retained.UiInterpretedBinding(propertyName, expression));
    }

    /// <summary>Attaches a generated typed binding without entering the cold interpreted binding path.</summary>
    public void SetCompiledBinding<TSource, TValue>(
        UiProperty<TValue> property,
        UiCompiledBinding<TSource, TValue> binding,
        bool sourceNotificationsManaged = false)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(binding);
        _retained.AttachCompiledBinding(
            property.Name,
            Retained.UiPropertyKeys.Resolve(property.Name),
            PropertyInvalidation(property.Name),
            binding,
            sourceNotificationsManaged);
    }

    /// <summary>Refreshes one generated binding target with its current typed source value.</summary>
    /// <remarks>Generated companions call this from their single source-notification batch.</remarks>
    public void RefreshCompiledBinding<TSource, TValue>(
        UiProperty<TValue> property,
        UiCompiledBinding<TSource, TValue> binding)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(binding);
        _retained.ApplyCompiledBinding(
            property.Name,
            Retained.UiPropertyKeys.Resolve(property.Name),
            ToRetainedValue(binding.ReadValue()),
            PropertyInvalidation(property.Name));
    }

    /// <summary>Queues a generated typed binding refresh for the next document binding stage.</summary>
    /// <remarks>Generated companions use this after their single source-notification boundary.</remarks>
    public void QueueCompiledBindingRefresh<TSource, TValue>(
        UiProperty<TValue> property,
        UiCompiledBinding<TSource, TValue> binding)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(binding);
        _retained.QueueCompiledBindingRefresh(property.Name);
    }

    internal static UiElement Wrap(RetainedElement element, Dictionary<RetainedElement, UiElement>? views = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        var cache = views ?? new Dictionary<RetainedElement, UiElement>();
        if (cache.TryGetValue(element, out var view))
        {
            return view;
        }

        UiElement created = element switch
        {
            Retained.NumericEditor numericEditor => new UiNumericEditor(numericEditor),
            Retained.TextBox textBox => new UiTextBox(textBox),
            Retained.TextBlock textBlock => new UiTextBlock(textBlock),
            Retained.StackPanel stackPanel => new UiStackPanel(stackPanel),
            Retained.ItemsControl itemsControl => new UiItemsControl(itemsControl),
            Retained.Panel panel => new UiPanel(panel),
            Retained.Border border => new UiBorder(border),
            Retained.Grid grid => new UiGrid(grid),
            Retained.ToggleButton toggleButton => new UiToggleButton(toggleButton),
            Retained.Button button => new UiButton(button),
            Retained.ScrollViewer scrollViewer => new UiScrollViewer(scrollViewer),
            Retained.Slider slider => new UiSlider(slider),
            Retained.Image image => new UiImage(image),
            Retained.Overlay overlay => new UiOverlay(overlay),
            Retained.CollectionView collection => new UiCollectionView(collection),
            Retained.Picker picker => new UiPicker(picker),
            Retained.TabView tabs => new UiTabView(tabs),
            Retained.Menu menu => new UiMenu(menu),
            Retained.RichTextBlock richText => new UiRichTextBlock(richText),
            Retained.ContentControl contentControl => new UiContentControl(contentControl),
            _ => new RetainedElementView(element, cache),
        };
        created.AdoptViewCache(cache);
        return created;
    }

    private sealed class RetainedElementView : UiElement
    {
        public RetainedElementView(RetainedElement element, Dictionary<RetainedElement, UiElement> views) : base(element, views) { }
    }

    private sealed class RetainedChildrenView : IReadOnlyList<UiElement>
    {
        private readonly RetainedElement _owner;
        private readonly Dictionary<RetainedElement, UiElement> _views;
        public RetainedChildrenView(RetainedElement owner, Dictionary<RetainedElement, UiElement> views) { _owner = owner; _views = views; }
        public int Count => _owner.Children.Count;
        public UiElement this[int index] => Wrap((RetainedElement)_owner.Children[index], _views);
        public IEnumerator<UiElement> GetEnumerator() { for (var i = 0; i < Count; i++) { yield return this[i]; } }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RetainedChildrenEditor : IList<UiElement>
    {
        private readonly RetainedElement _owner;
        private readonly Dictionary<RetainedElement, UiElement> _views;
        public RetainedChildrenEditor(RetainedElement owner, Dictionary<RetainedElement, UiElement> views) { _owner = owner; _views = views; }
        public UiElement this[int index] { get => Wrap((RetainedElement)_owner.Children[index], _views); set => throw new NotSupportedException("Replace is not supported; remove and add the child."); }
        public int Count => _owner.Children.Count;
        public bool IsReadOnly => false;
        public void Add(UiElement item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _owner.Add(item.RetainedElement);
            item.AdoptViewCache(_views);
        }
        public void Clear() => _owner.ClearChildren();
        public bool Contains(UiElement item) => item is not null && _owner.Children.Contains(item.RetainedElement);
        public void CopyTo(UiElement[] array, int arrayIndex) { for (var i = 0; i < Count; i++) { array[arrayIndex + i] = this[i]; } }
        public IEnumerator<UiElement> GetEnumerator() => new RetainedChildrenView(_owner, _views).GetEnumerator();
        public int IndexOf(UiElement item)
        {
            if (item is null) { return -1; }
            for (var i = 0; i < Count; i++) { if (ReferenceEquals(_owner.Children[i], item.RetainedElement)) { return i; } }
            return -1;
        }
        public void Insert(int index, UiElement item) { if (index != Count) { throw new NotSupportedException("Only append is supported by the retained tree."); } Add(item); }
        public bool Remove(UiElement item) => item is not null && _owner.Remove(item.RetainedElement);
        public void RemoveAt(int index) => _owner.Remove(_owner.Children[index]);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal void ApplyStyleValue(string propertyName, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        _retained.SetStyle(propertyName, ToRetainedValue(value), PropertyInvalidation(propertyName));
    }

    internal void ApplyStyleValue(IUiProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ApplyStyleValue(property.Name, value);
    }

    internal void ApplyTriggerValue<T>(UiProperty<T> property, T value) =>
        _retained.SetTrigger(property.Name, ToRetainedValue(value), PropertyInvalidation(property.Name));

    internal void ClearTriggerValue<T>(UiProperty<T> property) =>
        _retained.Clear(property.Name, Retained.UiValueSource.Trigger);

    internal void ApplyStyleResource(string propertyName, UiResourceCatalog resources, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(resources);
        _retained.SetStyleResource(propertyName, resources.Store, new(resourceKey), PropertyInvalidation(propertyName));
    }

    internal void ApplyStyleResource(string propertyName, UiResourceCatalog resources, UiResourceId resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(resources);
        if (!resource.IsValid)
        {
            throw new ArgumentException("A resource identity is required.", nameof(resource));
        }

        _retained.SetStyleResource(propertyName, resources.Store, new(resource.Value), PropertyInvalidation(propertyName));
    }

    /// <summary>Assigns a resource-backed style value that follows later catalog changes.</summary>
    public void SetDynamicResource(string propertyName, UiResourceCatalog resources, string resourceKey)
    {
        ApplyStyleResource(propertyName, resources, resourceKey);
    }

    /// <summary>Assigns a dynamic resource by its stable compiled identity.</summary>
    public void SetDynamicResource(string propertyName, UiResourceCatalog resources, UiResourceId resource)
    {
        ApplyStyleResource(propertyName, resources, resource);
    }

    /// <summary>Assigns a dynamic resource through a generated typed property descriptor.</summary>
    public void SetDynamicResource<T>(UiProperty<T> property, UiResourceCatalog resources, UiResourceId resource)
    {
        ArgumentNullException.ThrowIfNull(property);
        SetDynamicResource(property.Name, resources, resource);
    }

    /// <summary>Assigns the current resource value once without subscribing to later changes.</summary>
    public void SetStaticResource(string propertyName, UiResourceCatalog resources, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (!resources.TryResolve(resourceKey, out var value))
        {
            throw new KeyNotFoundException($"Resource '{resourceKey}' was not found.");
        }

        _retained.SetLocal(propertyName, ToRetainedStaticResourceValue(propertyName, value), PropertyInvalidation(propertyName));
    }

    /// <summary>Assigns the current value of a stable compiled resource identity.</summary>
    public void SetStaticResource(string propertyName, UiResourceCatalog resources, UiResourceId resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(resources);
        if (!resource.IsValid)
        {
            throw new ArgumentException("A resource identity is required.", nameof(resource));
        }

        if (!resources.TryResolve(resource, out var value))
        {
            throw new KeyNotFoundException($"Resource '{resource.Value:D}' was not found.");
        }

        _retained.SetLocal(propertyName, ToRetainedStaticResourceValue(propertyName, value), PropertyInvalidation(propertyName));
    }

    /// <summary>Assigns a static resource through a generated typed property descriptor.</summary>
    public void SetStaticResource<T>(UiProperty<T> property, UiResourceCatalog resources, UiResourceId resource)
    {
        ArgumentNullException.ThrowIfNull(property);
        SetStaticResource(property.Name, resources, resource);
    }

    internal bool HasTemplateContent
    {
        get
        {
            if (_templateContent is not { } content)
            {
                return false;
            }

            for (var i = 0; i < _retained.Children.Count; i++)
            {
                if (ReferenceEquals(_retained.Children[i], content.RetainedElement))
                {
                    return true;
                }
            }

            return false;
        }
    }
    internal UiTemplate? AppliedTemplate => _appliedTemplate;

    internal void SetTemplateContent(UiElement content, UiTemplate template)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(template);
        MutableChildren.Add(content);
        _templateContent = content;
        _appliedTemplate = template;
    }

    internal void ClearTemplateContent()
    {
        if (_templateContent is not { } content)
        {
            _appliedTemplate = null;
            return;
        }

        _templateContent = null;
        _appliedTemplate = null;
        MutableChildren.Remove(content);
    }

    internal void RegisterView(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.AdoptViewCache(_views);
    }

    internal void AddChild(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        MutableChildren.Add(child);
    }

    internal bool RemoveChild(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return MutableChildren.Remove(child);
    }

    internal void SetCollectionIndex(int index) => _retained.SetCollectionIndex(index);

    internal void SetSingleChild(UiElement? child)
    {
        MutableChildren.Clear();
        if (child is not null)
        {
            MutableChildren.Add(child);
        }
    }

    internal void SetItemsCore(
        Retained.ItemsControl owner,
        IReadOnlyList<object?> items,
        Func<object?, UiElement> factory)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(factory);
        owner.SetItems(items, item =>
        {
            var created = factory(item);
            ArgumentNullException.ThrowIfNull(created, nameof(item));
            RegisterView(created);
            return created.RetainedElement;
        });
    }

    internal static Retained.GridLength[] ConvertGridLengths(UiGridLength[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var converted = new Retained.GridLength[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            converted[i] = new(values[i].Value, (Retained.GridUnitType)values[i].Unit);
        }

        return converted;
    }

    internal static UiColor ToPublicColor(Retained.UiColor value) =>
        new(value.R, value.G, value.B, value.A);

    private static UiSemanticRole ToPublicRole(Retained.UiAutomationRole value) => value switch
    {
        Retained.UiAutomationRole.None => UiSemanticRole.None,
        Retained.UiAutomationRole.Unknown => UiSemanticRole.Unknown,
        Retained.UiAutomationRole.Generic => UiSemanticRole.Generic,
        Retained.UiAutomationRole.Button => UiSemanticRole.Button,
        Retained.UiAutomationRole.Window => UiSemanticRole.Window,
        Retained.UiAutomationRole.Text => UiSemanticRole.Text,
        Retained.UiAutomationRole.TextBox => UiSemanticRole.TextBox,
        Retained.UiAutomationRole.NumericEditor => UiSemanticRole.NumericEditor,
        Retained.UiAutomationRole.Slider => UiSemanticRole.Slider,
        Retained.UiAutomationRole.Image => UiSemanticRole.Image,
        Retained.UiAutomationRole.List => UiSemanticRole.List,
        Retained.UiAutomationRole.ListItem => UiSemanticRole.ListItem,
        Retained.UiAutomationRole.Menu => UiSemanticRole.Menu,
        Retained.UiAutomationRole.Tab => UiSemanticRole.Tab,
        Retained.UiAutomationRole.Link => UiSemanticRole.Link,
        _ => UiSemanticRole.Unknown,
    };

    private static Retained.UiAutomationRole ToRetainedRole(UiSemanticRole value) => value switch
    {
        UiSemanticRole.None => Retained.UiAutomationRole.None,
        UiSemanticRole.Unknown => Retained.UiAutomationRole.Unknown,
        UiSemanticRole.Generic => Retained.UiAutomationRole.Generic,
        UiSemanticRole.Button => Retained.UiAutomationRole.Button,
        UiSemanticRole.Window => Retained.UiAutomationRole.Window,
        UiSemanticRole.Text => Retained.UiAutomationRole.Text,
        UiSemanticRole.TextBox => Retained.UiAutomationRole.TextBox,
        UiSemanticRole.NumericEditor => Retained.UiAutomationRole.NumericEditor,
        UiSemanticRole.Slider => Retained.UiAutomationRole.Slider,
        UiSemanticRole.Image => Retained.UiAutomationRole.Image,
        UiSemanticRole.List => Retained.UiAutomationRole.List,
        UiSemanticRole.ListItem => Retained.UiAutomationRole.ListItem,
        UiSemanticRole.Menu => Retained.UiAutomationRole.Menu,
        UiSemanticRole.Tab => Retained.UiAutomationRole.Tab,
        UiSemanticRole.Link => Retained.UiAutomationRole.Link,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    internal static Retained.UiColor ToRetainedColor(UiColor value) =>
        new(value.R, value.G, value.B, value.A);

    internal static IUiClipboard? GetClipboard(Retained.TextBox owner) =>
        owner.Clipboard is UiClipboardBridge bridge ? bridge.Source : null;

    internal static void SetClipboard(Retained.TextBox owner, IUiClipboard? clipboard)
    {
        ArgumentNullException.ThrowIfNull(owner);
        owner.Clipboard = clipboard is null ? null : new UiClipboardBridge(clipboard);
    }

    internal static IUiClipboard? GetClipboard(Retained.NumericEditor owner) =>
        owner.Clipboard is UiClipboardBridge bridge ? bridge.Source : null;

    internal static void SetClipboard(Retained.NumericEditor owner, IUiClipboard? clipboard)
    {
        ArgumentNullException.ThrowIfNull(owner);
        owner.Clipboard = clipboard is null ? null : new UiClipboardBridge(clipboard);
    }

    internal void AdoptViewCache(Dictionary<RetainedElement, UiElement> views)
    {
        if (ReferenceEquals(_views, views))
        {
            return;
        }

        var previousViews = _views;
        SetViewCache(views);
        foreach (var child in _retained.Children)
        {
            if (child is RetainedElement retainedChild && previousViews.TryGetValue(retainedChild, out var childView))
            {
                childView.AdoptViewCache(views);
            }
        }
    }

    [MemberNotNull(nameof(_views), nameof(_childrenView), nameof(_childrenEditor))]
    private void SetViewCache(Dictionary<RetainedElement, UiElement> views)
    {
        _views = views;
        _views[_retained] = this;
        _childrenView = new RetainedChildrenView(_retained, _views);
        _childrenEditor = new RetainedChildrenEditor(_retained, _views);
    }

    internal static object? ToRetainedValue(object? value) => value switch
    {
        UiColor color => new Retained.UiColor(color.R, color.G, color.B, color.A),
        UiThickness thickness => new Retained.UiThickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom),
        UiResourceReference reference => reference.Resource.IsValid
            ? new Retained.UiResourceReference(reference.Resource.Value)
            : new Retained.UiResourceReference(reference.Key),
        _ => value,
    };

    private static object? ToRetainedStaticResourceValue(string propertyName, object? value) =>
        propertyName == "EffectSet" && value is UiEffectResource effectResource
            ? effectResource.Set
            : ToRetainedValue(value);

    private static object? ToPublicValue(object? value) => value switch
    {
        Retained.UiColor color => new UiColor(color.R, color.G, color.B, color.A),
        Retained.UiThickness thickness => new UiThickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom),
        Retained.UiResourceReference reference => reference.HasResourceId
            ? new UiResourceReference(new UiResourceId(reference.ResourceId))
            : new UiResourceReference(reference.Key),
        _ => value,
    };

    private static bool IsCompatibleValue(Type valueType, object? value) =>
        value is not null
            ? valueType.IsInstanceOfType(value)
            : !valueType.IsValueType || Nullable.GetUnderlyingType(valueType) is not null;

    private static RetainedDirty PropertyInvalidation(string propertyName) => propertyName switch
    {
        "Text" or "FontKey" or "FontSize" => RetainedDirty.Measure | RetainedDirty.Visual | RetainedDirty.Text,
        "FontWeight" or "FontStyle" => RetainedDirty.Measure | RetainedDirty.Visual | RetainedDirty.Text,
        "TextDecorations" => RetainedDirty.Visual | RetainedDirty.Text,
        "TextWrapping" or "MaxLines" or "LineHeight" => RetainedDirty.Measure | RetainedDirty.Arrange | RetainedDirty.Visual,
        "HorizontalTextAlignment" or "VerticalTextAlignment" or "TextTrimming" => RetainedDirty.Arrange | RetainedDirty.Visual,
        "PlaceholderText" => RetainedDirty.Visual | RetainedDirty.Text,
        "IsReadOnly" or "AcceptsReturn" or "MaxLength" => RetainedDirty.Visual,
        "Foreground" or "ForegroundBrush" or "StrokeColor" or "StrokeWidth" => RetainedDirty.Visual | RetainedDirty.Text,
        "EffectSet" => RetainedDirty.Visual | RetainedDirty.Text,
        "BlendMode" => RetainedDirty.Visual | RetainedDirty.Text,
        "BackgroundBrush" or "Tint" or "Placeholder" or "ErrorSource" or "Stretch" => RetainedDirty.Visual,
        "BorderColor" or "BorderWidth" or "BorderThickness" or "BorderWidthUnits" or "CornerRadius" => RetainedDirty.Visual,
        "Variant" => RetainedDirty.Style | RetainedDirty.Visual,
        "Width" or "Height" or "Margin" or "Padding" or
        "Minimum" or "Maximum" or "Value" or "Orientation" or "Columns" or "Rows" => RetainedDirty.Measure | RetainedDirty.Arrange | RetainedDirty.Visual,
        "HorizontalAlignment" or "VerticalAlignment" => RetainedDirty.Arrange | RetainedDirty.Visual,
        _ => RetainedDirty.Visual,
    };

    private static void ValidatePaintDimension(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Paint dimensions must be finite and non-negative.");
        }
    }

    private static void ValidateNonNegativeThickness(UiThickness value, string parameterName)
    {
        if (!float.IsFinite(value.Left) || value.Left < 0 ||
            !float.IsFinite(value.Top) || value.Top < 0 ||
            !float.IsFinite(value.Right) || value.Right < 0 ||
            !float.IsFinite(value.Bottom) || value.Bottom < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Thickness values must be finite and non-negative.");
        }
    }

    private static void ValidateCornerRadii(UiCornerRadii value, string parameterName)
    {
        if (!value.IsFiniteNonNegative)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Corner radii must be finite and non-negative.");
        }
    }

    private sealed class UiClipboardBridge(IUiClipboard source) : Retained.IUiClipboard
    {
        internal IUiClipboard Source { get; } = source;

        public string? ReadText() => Source.ReadText();

        public void SetText(string? text) => Source.SetText(text);

        public bool HasText => Source.HasText;
    }

}

public enum UiGridUnitType { Pixel, Auto, Star }

public enum UiOrientation { Horizontal, Vertical }

public readonly record struct UiGridLength(float Value, UiGridUnitType Unit)
{
    public static UiGridLength Pixel(float value) => new(value, UiGridUnitType.Pixel);
    public static UiGridLength Auto => new(1, UiGridUnitType.Auto);
    public static UiGridLength Star(float weight = 1) => new(weight, UiGridUnitType.Star);
}
