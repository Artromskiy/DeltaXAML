using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using Delta.Diagnostics;
using Delta.Maths;
using Delta.Text.Contract;
using Delta.XAML.Contract;
using Retained = DeltaXAML.Internal;
using RetainedDirty = DeltaXAML.Internal.UiDirtyMask;
using RetainedElement = DeltaXAML.Internal.UiElement;

namespace Delta.XAML;

public readonly record struct UiPropertyId(Guid Value) { public bool IsValid => Value != Guid.Empty; }
public readonly record struct UiTypeId(Guid Value) { public bool IsValid => Value != Guid.Empty; }
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

public abstract class UiElement
{
    private readonly RetainedElement _retained;
    private Dictionary<RetainedElement, UiElement> _views;
    private RetainedChildrenView _childrenView;
    private RetainedChildrenEditor _childrenEditor;
    protected UiElement() : this(new RetainedElement(), null) { }
    internal UiElement(RetainedElement retained, Dictionary<RetainedElement, UiElement>? views)
    {
        ArgumentNullException.ThrowIfNull(retained);
        _retained = retained;
        SetViewCache(views ?? new Dictionary<RetainedElement, UiElement>());
    }
    internal RetainedElement RetainedElement => _retained;

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
        if (!_retained.TrySet(handle.Retained, value, RetainedDirty.Binding | RetainedDirty.Visual, out var message))
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
        return _retained.TryGet(property.Name, out var value) ? value.UntypedValue : property.DefaultValue;
    }

    public bool TrySetValue(IUiProperty property, object? value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(property);
        _retained.SetLocal(property.Name, value, RetainedDirty.Binding | RetainedDirty.Visual);
        diagnostic = null;
        return true;
    }

    public T GetValue<T>(IUiProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var value = GetValue(property);
        return value is T typed ? typed : property.DefaultValue;
    }

    public void SetValue<T>(IUiProperty<T> property, T value)
    {
        ArgumentNullException.ThrowIfNull(property);
        _retained.SetLocal(property.Name, value, RetainedDirty.Binding | RetainedDirty.Visual);
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
        _retained.AttachBinding(new Retained.UiBindingRuntime(propertyName, expression));
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
        _retained.SetStyle(propertyName, ToRetainedValue(value), RetainedDirty.Measure | RetainedDirty.Visual);
    }

    internal void ApplyStyleResource(string propertyName, UiResourceCatalog resources, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(resources);
        _retained.SetStyleResource(propertyName, resources.Store, new(resourceKey), RetainedDirty.Measure | RetainedDirty.Visual);
    }

    internal void SetTemplateContent(UiElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        MutableChildren.Add(content);
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
        UiResourceReference reference => new Retained.UiResourceReference(reference.Key),
        _ => value,
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

    public UiItemsControl() : base(new Retained.ItemsControl(), null) { }
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

    public UiButton() : base(new Retained.Button(), null) { }
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

    public UiTextBox() : base(new Retained.TextBox()) { }

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

    public UiNumericEditor() : base(new Retained.NumericEditor(), null) { }
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

    public UiScrollViewer() : base(new Retained.ScrollViewer(), null) { }
    internal UiScrollViewer(Retained.ScrollViewer element, Dictionary<RetainedElement, UiElement>? views) : base(element, views) { }

    public float OffsetX => ScrollElement.Offset.X;
    public float OffsetY => ScrollElement.Offset.Y;
    public void ScrollBy(float x, float y) => ScrollElement.ScrollBy(x, y);
}

public sealed class XamlLoadResult
{
    internal XamlLoadResult(UiElement? root, ReadOnlyMemory<Diagnostic> diagnostics) { Root = root; Diagnostics = diagnostics; Success = root is not null && diagnostics.Length == 0; }
    public UiElement? Root { get; }
    public ReadOnlyMemory<Diagnostic> Diagnostics { get; }
    public bool Success { get; }
}

public interface IXamlLoader
{
    XamlLoadResult Load(string source, in XamlLoadContext context);
}

public sealed class XamlLoader : IXamlLoader
{
    public XamlLoadResult Load(string source, in XamlLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context.Types);
        ArgumentNullException.ThrowIfNull(context.Resources);

        var typeResolver = context.Types;
        var resourceResolver = context.Resources;
        var views = new Dictionary<RetainedElement, UiElement>();
        var contextDiagnostics = new List<Diagnostic>();
        var resources = resourceResolver is UiResourceCatalog catalog
            ? catalog.Store
            : ResolveResources(source, resourceResolver, resourceResolver as IUiNamedResourceResolver, contextDiagnostics);
        if (contextDiagnostics.Count != 0)
        {
            return new(null, contextDiagnostics.ToArray());
        }

        var retained = Retained.XamlLoader.LoadForAdapter(
            source,
            (namespaceUri, localName) => CreateCustomElement(namespaceUri, localName, typeResolver, views),
            resources);
        var diagnostics = new List<Diagnostic>(ConvertDiagnostics(retained.Diagnostics));
        if (retained.Root is not null)
        {
            AttachBindingSpecs(retained.Root, context.Bindings, diagnostics);
        }

        return new(diagnostics.Count == 0 && retained.Root is not null ? UiElement.Wrap(retained.Root, views) : null, diagnostics.ToArray());
    }

    private static void AttachBindingSpecs(
        RetainedElement element,
        IUiBindingResolver? resolver,
        List<Diagnostic> diagnostics)
    {
        foreach (var spec in element.BindingSpecs)
        {
            IUiValueConverter? converter = null;
            if (spec.ConverterKey is not null && (resolver is null || !resolver.TryResolveConverter(spec.ConverterKey, out converter)))
            {
                diagnostics.Add(CreateDiagnostic("XAML009", $"Binding converter '{spec.ConverterKey}' was not registered."));
                continue;
            }

            element.AttachBinding(new Retained.UiBindingRuntime(
                spec.Property,
                new UiBindingExpression(spec.Path, BindingModeMap.ToPublic(spec.Mode), converter, spec.StringFormat)));
        }

        foreach (var child in element.Children)
        {
            if (child is RetainedElement retainedChild)
            {
                AttachBindingSpecs(retainedChild, resolver, diagnostics);
            }
        }
    }

    private static RetainedElement? CreateCustomElement(string namespaceUri, string localName, IXamlTypeResolver resolver, Dictionary<RetainedElement, UiElement> views)
    {
        if (!resolver.TryResolveName(new XamlQualifiedName(namespaceUri, localName), out var type) ||
            !resolver.TryCreate(type, out var element) ||
            element is null)
        {
            return null;
        }

        views[element.RetainedElement] = element;
        return element.RetainedElement;
    }

    private static Retained.UiResourceStore? ResolveResources(string source, IUiResourceResolver resolver, IUiNamedResourceResolver? namedResolver, List<Diagnostic> diagnostics)
    {
        Retained.UiResourceStore? resources = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                while (reader.MoveToNextAttribute())
                {
                    if (string.Equals(reader.LocalName, "ForegroundResource", StringComparison.Ordinal))
                    {
                        if (!Guid.TryParse(reader.Value, out var resourceGuid))
                        {
                            diagnostics.Add(CreateDiagnostic("XAML005", $"Resource '{reader.Value}' is not a GUID identity."));
                            continue;
                        }

                        if (!resolver.TryResolve(new UiResourceId(resourceGuid), out var value))
                        {
                            diagnostics.Add(CreateDiagnostic("XAML006", $"Resource '{resourceGuid}' was not resolved."));
                            continue;
                        }

                        resources ??= new Retained.UiResourceStore();
                        resources.Set(resourceGuid.ToString("D"), value);
                        continue;
                    }

                    if (!Retained.XamlLoader.TryParseResourceReference(reader.Value, out var resourceKey))
                    {
                        continue;
                    }

                    if (namedResolver is null || !namedResolver.TryResolve(resourceKey, out var namedValue))
                    {
                        diagnostics.Add(CreateDiagnostic("XAML006", $"Resource '{resourceKey}' was not resolved."));
                        continue;
                    }

                    resources ??= new Retained.UiResourceStore();
                    resources.Set(resourceKey, namedValue);
                }

                reader.MoveToElement();
            }
        }
        catch (XmlException exception)
        {
            diagnostics.Add(CreateDiagnostic("XAML001", exception.Message));
        }

        return resources;
    }

    private static Diagnostic[] ConvertDiagnostics(IReadOnlyList<Retained.XamlDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return Array.Empty<Diagnostic>();
        }

        var converted = new Diagnostic[diagnostics.Count];
        for (var i = 0; i < diagnostics.Count; i++)
        {
            var diagnostic = diagnostics[i];
            var line = Math.Max(0, diagnostic.Line - 1);
            var column = Math.Max(0, diagnostic.Column - 1);
            converted[i] = CreateDiagnostic(diagnostic.Code, diagnostic.Message, line, column);
        }

        return converted;
    }

    private static Diagnostic CreateDiagnostic(string code, string message, int line = 0, int column = 0)
    {
        var location = line > 0 || column > 0
            ? new SourceRange(SourceId.Empty, new(Math.Max(0, line), Math.Max(0, column), 0), new(Math.Max(0, line), Math.Max(0, column), 0))
            : (SourceRange?)null;
        return new(new DiagnosticCode(code), DiagnosticSeverity.Error, message, location);
    }
}

public sealed class UiDocument : IDisposable
{
    private readonly Retained.UiFrame _retainedFrame;
    private readonly ITextService _textService;
    private readonly IUiFontResolver _fontResolver;
    private readonly Dictionary<string, FontInstanceId> _fontInstances = new(StringComparer.Ordinal);
    private readonly Dictionary<UiTextCacheKey, UiTextCacheEntry> _textCache = new();
    private readonly FontInstanceId[] _singleFontFallback = new FontInstanceId[1];
    private UiVisualCommand[] _visuals = Array.Empty<UiVisualCommand>();
    private UiClip[] _clips = Array.Empty<UiClip>();
    private UiTextDraw[] _text = Array.Empty<UiTextDraw>();
    private int _visualCount;
    private int _clipCount;
    private int _textCount;

    public UiDocument(UiElement root, ITextService textService)
        : this(root, textService, EmptyFontResolver.Instance)
    {
    }

    public UiDocument(UiElement root, ITextService textService, IUiFontResolver? fontResolver)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(textService);
        Root = root;
        _textService = textService;
        _fontResolver = fontResolver ?? EmptyFontResolver.Instance;
        _retainedFrame = new Retained.UiFrame(root.RetainedElement);
    }

    public UiElement Root { get; }

    public void Dispose()
    {
        if (_fontInstances.Count == 0)
        {
            return;
        }

        foreach (var font in _fontInstances.Values)
        {
            _textService.CloseFont(font);
        }

        _fontInstances.Clear();
        _textCache.Clear();
    }

    public void Dispatch(in UiInputEvent input)
    {
        switch (input.Kind)
        {
            case UiInputEventKind.PointingDevice:
                _retainedFrame.Input.RoutePointer(new Retained.UiPointerEvent(ToRetainedPointerKind(input.PointingDevice.Kind), new(input.PointingDevice.Position.x, input.PointingDevice.Position.y), (int)input.PointingDevice.ChangedButton.Value, input.PointingDevice.WheelDelta.y));
                break;
            case UiInputEventKind.Key:
                _retainedFrame.Input.RouteKey(new Retained.UiKeyEvent(checked((int)input.Key.PhysicalKey.Value), input.Key.Kind == UiKeyEventKind.Down, input.Key.IsRepeat));
                break;
            case UiInputEventKind.Text:
                _retainedFrame.Input.RouteText(new Retained.UiTextInput(input.Text.Text.ToString()));
                break;
            case UiInputEventKind.Composition:
                var composition = input.Composition;
                _retainedFrame.Input.RouteIme(new Retained.UiImeComposition(composition.Preedit.ToString(), composition.Selection.StartUtf16, composition.Selection.LengthUtf16, composition.Stage == UiCompositionStage.Finished));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }
    }

    public void Dispatch(ReadOnlySpan<UiInputEvent> input)
    {
        for (var i = 0; i < input.Length; i++) { Dispatch(input[i]); }
    }

    public void Layout(float2 viewport, float dpiScale) => _retainedFrame.Layout(new(viewport.x, viewport.y), dpiScale);

    public UiDisplayList BuildDisplayList()
    {
        if (!TryBuildDisplayList(out var displayList, out var diagnostic))
        {
            if (diagnostic is { } failure)
            {
                throw new InvalidOperationException($"{failure.Code.Value}: {failure.Message}");
            }

            throw new InvalidOperationException("The UI display list could not be built.");
        }

        return displayList;
    }

    public bool TryBuildDisplayList(out UiDisplayList displayList, out Diagnostic? diagnostic)
    {
        var retained = _retainedFrame.ExtractDrawList(new Retained.UiFrameContext(new(Root.RetainedElement.Bounds.Width, Root.RetainedElement.Bounds.Height), Root.RetainedElement.DpiScale, 0));

        for (var i = 0; i < retained.Commands.Length; i++)
        {
            var source = retained.Commands.Span[i];
            if (source.Kind != Retained.UiDrawKind.Rectangle)
            {
                displayList = default;
                diagnostic = new Diagnostic(new DiagnosticCode("XAML_DISPLAY_KIND_UNSUPPORTED"), DiagnosticSeverity.Error, $"The retained visual kind '{source.Kind}' has no canonical adapter mapping.", null);
                return false;
            }

            if (source.Resource.Value != 0 || source.Resource.Generation != 0)
            {
                displayList = default;
                diagnostic = new Diagnostic(new DiagnosticCode("XAML_DISPLAY_RESOURCE_UNSUPPORTED"), DiagnosticSeverity.Error, "The retained resource handle has no canonical GUID resource identity mapping.", null);
                return false;
            }

            if (source.Text is not null)
            {
                displayList = default;
                diagnostic = new Diagnostic(new DiagnosticCode("XAML_DISPLAY_TEXT_UNSUPPORTED"), DiagnosticSeverity.Error, "The retained visual text payload has no DeltaText shaping mapping.", null);
                return false;
            }
        }

        _visualCount = retained.Commands.Length;
        _clipCount = retained.Clips.Length;
        _textCount = retained.TextRuns.Length;
        EnsureCapacity(ref _visuals, _visualCount);
        EnsureCapacity(ref _clips, _clipCount);
        EnsureCapacity(ref _text, _textCount);
        for (var i = 0; i < _clipCount; i++)
        {
            var source = retained.Clips.Span[i];
            var parent = source.Parent.Value == 0 ? UiClipId.None : new UiClipId((int)source.Parent.Value - 1);
            _clips[i] = new(ToFloat4(source.Bounds), parent);
        }

        for (var i = 0; i < _visualCount; i++)
        {
            var source = retained.Commands.Span[i];
            var clip = source.ClipId.Value == 0 ? UiClipId.None : new UiClipId((int)source.ClipId.Value - 1);
            _visuals[i] = new(UiVisualKind.SolidRectangle, default, ToFloat4(source.Bounds), ToColor(source.Color), clip, UiResourceId.Empty);
        }

        for (var i = 0; i < _textCount; i++)
        {
            if (!TryBuildTextDraw(retained.TextRuns.Span[i], out _text[i], out diagnostic))
            {
                displayList = default;
                return false;
            }
        }

        displayList = new(_visuals.AsSpan(0, _visualCount), _clips.AsSpan(0, _clipCount), _text.AsSpan(0, _textCount));
        diagnostic = null;
        return true;
    }

    private bool TryBuildTextDraw(Retained.UiTextRun run, out UiTextDraw draw, out Diagnostic? diagnostic)
    {
        draw = default;
        if (!_fontResolver.TryResolve(run.FontKey, out var request))
        {
            diagnostic = TextDiagnostic("XAML_TEXT_FONT_NOT_FOUND", $"Font key '{run.FontKey}' was not registered.");
            return false;
        }

        if (!_fontInstances.TryGetValue(run.FontKey, out var font))
        {
            try
            {
                font = _textService.OpenFont(request);
            }
            catch (ArgumentException exception)
            {
                diagnostic = TextDiagnostic("XAML_TEXT_FONT_OPEN_FAILED", exception.Message);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                diagnostic = TextDiagnostic("XAML_TEXT_FONT_OPEN_FAILED", exception.Message);
                return false;
            }

            _fontInstances.Add(run.FontKey, font);
        }

        var cacheKey = new UiTextCacheKey(run.Owner, run.OwnerGeneration, run.GlyphRunKey);
        if (!_textCache.TryGetValue(cacheKey, out var cache) ||
            cache.FontKey != run.FontKey ||
            !cache.Text.Equals(run.Text, StringComparison.Ordinal) ||
            !cache.FontSize.Equals(run.FontSize))
        {
            try
            {
                _singleFontFallback[0] = font;
                var shaped = _textService.Shape(new TextShapeRequest(
                    run.Text.AsMemory(),
                    run.FontSize,
                    _singleFontFallback,
                    TextDirection.LeftToRight));
                cache = new UiTextCacheEntry(run.FontKey, run.Text, run.FontSize, shaped);
                _textCache[cacheKey] = cache;
            }
            catch (ArgumentException exception)
            {
                diagnostic = TextDiagnostic("XAML_TEXT_SHAPE_FAILED", exception.Message);
                return false;
            }
            catch (NotSupportedException exception)
            {
                diagnostic = TextDiagnostic("XAML_TEXT_SHAPING_UNSUPPORTED", exception.Message);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                diagnostic = TextDiagnostic("XAML_TEXT_SHAPE_FAILED", exception.Message);
                return false;
            }
        }

        var bounds = ShapedBounds(cache.Shaped);
        var baseline = new float2(run.Bounds.X - bounds.Left, run.Bounds.Y - bounds.Top);
        var clip = run.ClipId.Value == 0 ? UiClipId.None : new UiClipId(checked((int)run.ClipId.Value - 1));
        draw = new UiTextDraw(cache.Shaped, baseline, ToColor(run.Color), clip);
        diagnostic = null;
        return true;
    }

    private static TextBounds ShapedBounds(ShapedText shaped)
    {
        var runs = shaped.Runs.Span;
        if (runs.Length == 0)
        {
            return new TextBounds(0, 0, 0, 0);
        }

        var first = runs[0].Bounds;
        var left = first.Left;
        var top = first.Top;
        var right = first.Right;
        var bottom = first.Bottom;
        for (var i = 1; i < runs.Length; i++)
        {
            var bounds = runs[i].Bounds;
            left = MathF.Min(left, bounds.Left);
            top = MathF.Min(top, bounds.Top);
            right = MathF.Max(right, bounds.Right);
            bottom = MathF.Max(bottom, bounds.Bottom);
        }

        return new TextBounds(left, top, right, bottom);
    }

    private static Diagnostic TextDiagnostic(string code, string message) =>
        new(new DiagnosticCode(code), DiagnosticSeverity.Error, message, null);

    private readonly record struct UiTextCacheKey(Retained.UiElementId Owner, uint Generation, string GlyphRunKey);

    private sealed record UiTextCacheEntry(string FontKey, string Text, float FontSize, ShapedText Shaped);

    private sealed class EmptyFontResolver : IUiFontResolver
    {
        internal static EmptyFontResolver Instance { get; } = new();

        public bool TryResolve(string fontKey, out FontOpenRequest request)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
            request = default;
            return false;
        }
    }

    private static Retained.UiPointerEventKind ToRetainedPointerKind(UiPointerEventKind kind) => kind switch
    {
        UiPointerEventKind.ButtonDown => Retained.UiPointerEventKind.Down,
        UiPointerEventKind.ButtonUp => Retained.UiPointerEventKind.Up,
        UiPointerEventKind.Wheel => Retained.UiPointerEventKind.Wheel,
        _ => Retained.UiPointerEventKind.Move,
    };

    private static float4 ToFloat4(Retained.UiRect value) => new(value.X, value.Y, value.Width, value.Height);
    private static float4 ToColor(Retained.UiColor value) => new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
    private static void EnsureCapacity<T>(ref T[] storage, int count) { if (storage.Length < count) { Array.Resize(ref storage, Math.Max(8, count)); } }
}
