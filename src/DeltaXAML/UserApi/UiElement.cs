using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Delta.Diagnostics;
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

    public string TypeName => _retained.TypeName;

    public string? StyleKey
    {
        get => _retained.StyleKey;
        set => _retained.StyleKey = value;
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

    public bool Fill
    {
        get => _retained.Fill;
        set => _retained.Fill = value;
    }

    public UiColor Background
    {
        get
        {
            var value = _retained.Background;
            return new UiColor(value.R, value.G, value.B, value.A);
        }
        set => _retained.Background = new Retained.UiColor(value.R, value.G, value.B, value.A);
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

        _retained.SetCustomVisual(visualType.Value, resource.Value, new Retained.UiColor(color.R, color.G, color.B, color.A));
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

    /// <summary>Attaches a generated typed binding without the reflection-based compatibility bridge.</summary>
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

        return element switch
        {
            Retained.NumericEditor numericEditor => new UiNumericEditor(numericEditor, cache),
            Retained.TextBox textBox => new UiTextBox(textBox, cache),
            Retained.TextBlock textBlock => AdoptViewCache(new UiTextBlock(textBlock), cache),
            Retained.StackPanel stackPanel => new UiStackPanel(stackPanel, cache),
            Retained.ItemsControl itemsControl => new UiItemsControl(itemsControl, cache),
            Retained.Panel panel => new UiPanel(panel, cache),
            Retained.Border border => new UiBorder(border, cache),
            Retained.Grid grid => new UiGrid(grid, cache),
            Retained.Button button => new UiButton(button, cache),
            Retained.ScrollViewer scrollViewer => new UiScrollViewer(scrollViewer, cache),
            Retained.ContentControl contentControl => new UiContentControl(contentControl, cache),
            _ => new RetainedElementView(element, cache),
        };
    }

    private sealed class RetainedElementView : UiElement
    {
        public RetainedElementView(RetainedElement element, Dictionary<RetainedElement, UiElement> views) : base(element, views) { }
    }

    private static UiElement AdoptViewCache(UiElement view, Dictionary<RetainedElement, UiElement> views)
    {
        view.AdoptViewCache(views);
        return view;
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

        ApplyStyleValue(propertyName, value);
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

        ApplyStyleValue(propertyName, value);
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

    internal void AdoptViewCache(Dictionary<RetainedElement, UiElement> views)
    {
        if (ReferenceEquals(_views, views))
        {
            return;
        }

        SetViewCache(views);
        foreach (var child in _retained.Children)
        {
            if (child is RetainedElement retainedChild && _views.TryGetValue(retainedChild, out var childView))
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

    private static object? ToRetainedValue(object? value) => value switch
    {
        UiColor color => new Retained.UiColor(color.R, color.G, color.B, color.A),
        UiThickness thickness => new Retained.UiThickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom),
        UiResourceReference reference => reference.Resource.IsValid
            ? new Retained.UiResourceReference(reference.Resource.Value)
            : new Retained.UiResourceReference(reference.Key),
        _ => value,
    };

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
        "Foreground" => RetainedDirty.Visual | RetainedDirty.Text,
        "Width" or "Height" or "Padding" or
        "Minimum" or "Maximum" or "Value" or "Orientation" or "Columns" or "Rows" => RetainedDirty.Measure | RetainedDirty.Visual,
        _ => RetainedDirty.Visual,
    };

}

/// <summary>Convenience retained panel for code-authored composition.</summary>
public class UiPanel : UiElement
{
    public UiPanel() : base(Retained.UiPanelGenerated.Create(), null) { }
    internal UiPanel(RetainedElement element, Dictionary<RetainedElement, UiElement>? views) : base(element, views) { }

    public void Add(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        MutableChildren.Add(child);
    }

    public bool Remove(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return MutableChildren.Remove(child);
    }
}

/// <summary>Vertical or horizontal retained stack panel.</summary>
public sealed class UiStackPanel : UiPanel
{
    private Retained.StackPanel StackElement => (Retained.StackPanel)RetainedElement;

    public UiStackPanel() : base(Retained.UiStackPanelGenerated.Create(), null) { }
    internal UiStackPanel(Retained.StackPanel element, Dictionary<RetainedElement, UiElement>? views) : base(element, views) { }

    public UiOrientation Orientation
    {
        get => (UiOrientation)StackElement.Orientation;
        set => StackElement.Orientation = (Retained.UiOrientation)value;
    }
}

/// <summary>Retained collection host that reuses rows when item identity is unchanged.</summary>
public sealed class UiItemsControl : UiPanel
{
    private Retained.ItemsControl ItemsElement => (Retained.ItemsControl)RetainedElement;

    public UiItemsControl() : base(Retained.UiItemsControlGenerated.Create(), null) { }
    internal UiItemsControl(Retained.ItemsControl element, Dictionary<RetainedElement, UiElement>? views) : base(element, views) { }

    public IReadOnlyList<object?> Items => ItemsElement.Items;

    public void SetItems(IReadOnlyList<object?> items, Func<object?, UiElement> factory)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(factory);
        ItemsElement.SetItems(items, item =>
        {
            var created = factory(item);
            ArgumentNullException.ThrowIfNull(created, nameof(item));
            RegisterView(created);
            return created.RetainedElement;
        });
    }
}

/// <summary>Retained border with one optional child.</summary>
public sealed class UiBorder : UiElement
{
    private Retained.Border BorderElement => (Retained.Border)RetainedElement;

    public UiBorder() : base(Retained.UiBorderGenerated.Create(), null) { }
    internal UiBorder(Retained.Border element, Dictionary<RetainedElement, UiElement>? views) : base(element, views) { }

    public UiElement? Child => Children.Count == 0 ? null : Children[0];

    public void SetChild(UiElement? child)
    {
        BorderElement.ClearChildren();
        if (child is null)
        {
            return;
        }

        BorderElement.Add(child.RetainedElement);
        RegisterView(child);
    }
}

/// <summary>Retained content host with one optional child.</summary>
public class UiContentControl : UiElement
{
    private Retained.ContentControl ContentElement => (Retained.ContentControl)RetainedElement;

    public UiContentControl() : base(Retained.UiContentControlGenerated.Create(), null) { }
    internal UiContentControl(Retained.ContentControl element, Dictionary<RetainedElement, UiElement>? views) : base(element, views) { }

    public UiElement? Content => Children.Count == 0 ? null : Children[0];

    public void SetContent(UiElement? content)
    {
        if (content is not null)
        {
            RegisterView(content);
        }

        ContentElement.Content = content?.RetainedElement;
    }
}

/// <summary>Retained button control with a neutral click callback.</summary>
public class UiButton : UiContentControl
{
    private Retained.Button ButtonElement => (Retained.Button)RetainedElement;

    public UiButton() : base(Retained.UiButtonGenerated.Create(), null) { }
    internal UiButton(Retained.Button element, Dictionary<RetainedElement, UiElement>? views) : base(element, views) { }

    public event EventHandler? Click
    {
        add => ButtonElement.Click += value;
        remove => ButtonElement.Click -= value;
    }
}

/// <summary>Retained grid facade with fixed, auto and star definitions.</summary>
public sealed class UiGrid : UiElement
{
    private Retained.Grid GridElement => (Retained.Grid)RetainedElement;

    public UiGrid() : base(Retained.UiGridGenerated.Create(), null) { }
    internal UiGrid(Retained.Grid element, Dictionary<RetainedElement, UiElement>? views) : base(element, views) { }

    public void Add(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        MutableChildren.Add(child);
    }

    public bool Remove(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return MutableChildren.Remove(child);
    }

    public void SetColumns(params UiGridLength[] columns) => GridElement.SetColumns(columns.Select(ToRetained).ToArray());

    public void SetRows(params UiGridLength[] rows) => GridElement.SetRows(rows.Select(ToRetained).ToArray());

    private static Retained.GridLength ToRetained(UiGridLength value) => new(value.Value, (Retained.GridUnitType)value.Unit);
}

public enum UiGridUnitType { Pixel, Auto, Star }

public enum UiOrientation { Horizontal, Vertical }

public readonly record struct UiGridLength(float Value, UiGridUnitType Unit)
{
    public static UiGridLength Pixel(float value) => new(value, UiGridUnitType.Pixel);
    public static UiGridLength Auto => new(1, UiGridUnitType.Auto);
    public static UiGridLength Star(float weight = 1) => new(weight, UiGridUnitType.Star);
}

/// <summary>Convenience retained text editor for code-authored composition.</summary>
public class UiTextBox : UiTextBlock
{
    private Retained.TextBox TextBoxElement => (Retained.TextBox)RetainedElement;
    private ClipboardBridge? _clipboardBridge;

    public UiTextBox() : base(Retained.UiTextBoxGenerated.Create()) { }

    internal UiTextBox(Retained.TextBox element, Dictionary<RetainedElement, UiElement>? views)
        : base(element)
    {
        if (views is not null)
        {
            AdoptViewCache(views);
        }
    }

    public void SetText(string text) => TextBoxElement.SetText(text);

    public IUiClipboard? Clipboard
    {
        get => _clipboardBridge?.Source;
        set
        {
            _clipboardBridge = value is null ? null : new ClipboardBridge(value);
            TextBoxElement.Clipboard = _clipboardBridge;
        }
    }

    public void SelectAll() => TextBoxElement.SelectAll();
    public void Copy() => TextBoxElement.Copy();
    public void Cut() => TextBoxElement.Cut();
    public bool Paste() => TextBoxElement.Paste();
    public bool Undo() => TextBoxElement.Undo();
    public bool Redo() => TextBoxElement.Redo();

    private sealed class ClipboardBridge(IUiClipboard clipboard) : Retained.IUiClipboard
    {
        public IUiClipboard Source => clipboard;
        public string? ReadText() => clipboard.ReadText();
        public void SetText(string? text) => clipboard.SetText(text);
        public bool HasText => clipboard.HasText;
    }
}

/// <summary>Numeric retained editor with explicit validation and commit state.</summary>
public sealed class UiNumericEditor : UiTextBox
{
    private Retained.NumericEditor NumericElement => (Retained.NumericEditor)RetainedElement;

    public UiNumericEditor() : base(Retained.UiNumericEditorGenerated.Create(), null) { }
    internal UiNumericEditor(Retained.NumericEditor element, Dictionary<RetainedElement, UiElement>? views) : base(element, views) { }

    public double CurrentValue => NumericElement.Value;
    public double Minimum { get => NumericElement.Min; set => NumericElement.Min = value; }
    public double Maximum { get => NumericElement.Max; set => NumericElement.Max = value; }
    public bool HasValidationError => NumericElement.HasValidationError;
    public bool IsDirty => NumericElement.IsDirty;
    public string? Diagnostic => NumericElement.Diagnostic;
    public void Initialize(double value) => NumericElement.Initialize(value);
    public bool TryCommit() => NumericElement.TryCommit();
    public bool TryCommitText(string text) => NumericElement.TryCommitText(text);
    public void CancelEdit() => NumericElement.CancelEdit();
    public bool Increment(double step = 1) => NumericElement.Increment(step);
    public bool Decrement(double step = 1) => NumericElement.Decrement(step);
}

/// <summary>Retained content viewport with platform-neutral scrolling.</summary>
public sealed class UiScrollViewer : UiContentControl
{
    private Retained.ScrollViewer ScrollElement => (Retained.ScrollViewer)RetainedElement;

    public UiScrollViewer() : base(Retained.UiScrollViewerGenerated.Create(), null) { }
    internal UiScrollViewer(Retained.ScrollViewer element, Dictionary<RetainedElement, UiElement>? views) : base(element, views) { }

    public float OffsetX => ScrollElement.Offset.X;
    public float OffsetY => ScrollElement.Offset.Y;
    public void ScrollBy(float x, float y) => ScrollElement.ScrollBy(x, y);
}
