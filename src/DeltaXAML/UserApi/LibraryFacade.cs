using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using Delta.Diagnostics;
using Delta.Maths;
using Delta.Text.Contract;
using Delta.XAML.Contract;
using Retained = DeltaXAML.Internal;
using RetainedContracts = DeltaXAML.Internal;
using RetainedDirty = DeltaXAML.Internal.UiDirtyMask;
using RetainedElement = DeltaXAML.Internal.UiElement;

namespace Delta.XAML;

public readonly record struct UiPropertyId(Guid Value) { public bool IsValid => Value != Guid.Empty; }
public readonly record struct UiTypeId(Guid Value) { public bool IsValid => Value != Guid.Empty; }
public readonly record struct XamlQualifiedName(string Namespace, string LocalName);

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

public enum UiBindingMode : byte { OneTime, OneWay, TwoWay }

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

public readonly record struct XamlLoadContext(IXamlTypeResolver Types, IUiResourceResolver Resources);

public abstract class UiElement
{
    private readonly RetainedElement _retained;
    private readonly IReadOnlyDictionary<RetainedElement, UiElement>? _views;
    private UiParticipation _participation = UiParticipation.All;

    protected UiElement() : this(new RetainedElement(), null) { }
    internal UiElement(RetainedElement retained, IReadOnlyDictionary<RetainedElement, UiElement>? views) { ArgumentNullException.ThrowIfNull(retained); _retained = retained; _views = views; }
    internal RetainedElement RetainedElement => _retained;

    public UiElement? Parent => _retained.Parent is RetainedElement parent ? Wrap(parent, _views) : null;
    public IReadOnlyList<UiElement> Children => new RetainedChildrenView(_retained, _views);
    protected IList<UiElement> MutableChildren => new RetainedChildrenEditor(_retained, _views);
    public UiParticipation Participation
    {
        get => _participation;
        set
        {
            if ((value & UiParticipation.Rendering) != 0 && (value & UiParticipation.Layout) == 0 ||
                (value & UiParticipation.HitTesting) != 0 && (value & UiParticipation.Layout) == 0)
            {
                throw new ArgumentException("Rendering and hit testing require layout participation.", nameof(value));
            }

            _participation = value;
        }
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

    internal static UiElement Wrap(RetainedElement element, IReadOnlyDictionary<RetainedElement, UiElement>? views = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return views is not null && views.TryGetValue(element, out var view) ? view : new RetainedElementView(element, views);
    }

    private sealed class RetainedElementView : UiElement
    {
        public RetainedElementView(RetainedElement element, IReadOnlyDictionary<RetainedElement, UiElement>? views) : base(element, views) { }
    }

    private sealed class RetainedChildrenView : IReadOnlyList<UiElement>
    {
        private readonly RetainedElement _owner;
        private readonly IReadOnlyDictionary<RetainedElement, UiElement>? _views;
        public RetainedChildrenView(RetainedElement owner, IReadOnlyDictionary<RetainedElement, UiElement>? views) { _owner = owner; _views = views; }
        public int Count => _owner.Children.Count;
        public UiElement this[int index] => Wrap((RetainedElement)_owner.Children[index], _views);
        public IEnumerator<UiElement> GetEnumerator() { for (var i = 0; i < Count; i++) { yield return this[i]; } }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RetainedChildrenEditor : IList<UiElement>
    {
        private readonly RetainedElement _owner;
        private readonly IReadOnlyDictionary<RetainedElement, UiElement>? _views;
        public RetainedChildrenEditor(RetainedElement owner, IReadOnlyDictionary<RetainedElement, UiElement>? views) { _owner = owner; _views = views; }
        public UiElement this[int index] { get => new RetainedChildrenView(_owner, _views)[index]; set => throw new NotSupportedException("Replace is not supported; remove and add the child."); }
        public int Count => _owner.Children.Count;
        public bool IsReadOnly => false;
        public void Add(UiElement item) { ArgumentNullException.ThrowIfNull(item); _owner.Add(item.RetainedElement); }
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
        var resources = ResolveResources(source, resourceResolver, contextDiagnostics);
        if (contextDiagnostics.Count != 0)
        {
            return new(null, contextDiagnostics.ToArray());
        }

        var retained = Retained.XamlLoader.LoadForAdapter(
            source,
            (namespaceUri, localName) => CreateCustomElement(namespaceUri, localName, typeResolver, views),
            resources);
        var diagnostics = ConvertDiagnostics(retained.Diagnostics);
        return new(retained.Root is null ? null : UiElement.Wrap(retained.Root, views), diagnostics);
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

    private static Retained.UiResourceStore? ResolveResources(string source, IUiResourceResolver resolver, List<Diagnostic> diagnostics)
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
                    if (!string.Equals(reader.LocalName, "ForegroundResource", StringComparison.Ordinal))
                    {
                        continue;
                    }

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
                    resources.Set(reader.Value, value);
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

public sealed class UiDocument
{
    private readonly Retained.UiFrame _retainedFrame;
    private readonly ITextService _textService;
    private UiVisualCommand[] _visuals = Array.Empty<UiVisualCommand>();
    private UiClip[] _clips = Array.Empty<UiClip>();
    private UiTextDraw[] _text = Array.Empty<UiTextDraw>();
    private int _visualCount;
    private int _clipCount;
    private int _textCount;

    public UiDocument(UiElement root, ITextService textService)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(textService);
        Root = root;
        _textService = textService;
        _retainedFrame = new Retained.UiFrame(root.RetainedElement);
    }

    public UiElement Root { get; }

    public void Dispatch(in UiInputEvent input)
    {
        switch (input.Kind)
        {
            case UiInputEventKind.PointingDevice:
                _retainedFrame.Input.RoutePointer(new RetainedContracts.UiPointerEvent(ToRetainedPointerKind(input.PointingDevice.Kind), new(input.PointingDevice.Position.x, input.PointingDevice.Position.y), (int)input.PointingDevice.ChangedButton.Value, input.PointingDevice.WheelDelta.y));
                break;
            case UiInputEventKind.Key:
                _retainedFrame.Input.RouteKey(new RetainedContracts.UiKeyEvent(checked((int)input.Key.PhysicalKey.Value), input.Key.Kind == UiKeyEventKind.Down, input.Key.IsRepeat));
                break;
            case UiInputEventKind.Text:
                _retainedFrame.Input.RouteText(new RetainedContracts.UiTextInput(input.Text.Text.ToString()));
                break;
            case UiInputEventKind.Composition:
                var composition = input.Composition;
                _retainedFrame.Input.RouteIme(new RetainedContracts.UiImeComposition(composition.Preedit.ToString(), composition.Selection.StartUtf16, composition.Selection.LengthUtf16, composition.Stage == UiCompositionStage.Finished));
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
        var retained = _retainedFrame.ExtractDrawList(new RetainedContracts.UiFrameContext(new(Root.RetainedElement.Bounds.Width, Root.RetainedElement.Bounds.Height), Root.RetainedElement.DpiScale, 0));
        if (retained.TextRuns.Length != 0)
        {
            displayList = default;
            diagnostic = new Diagnostic(new DiagnosticCode("XAML_DISPLAY_TEXT_UNSUPPORTED"), DiagnosticSeverity.Error, "The retained text run has no DeltaText font-instance mapping.", null);
            return false;
        }

        for (var i = 0; i < retained.Commands.Length; i++)
        {
            var source = retained.Commands.Span[i];
            if (source.Kind != RetainedContracts.UiDrawKind.Rectangle)
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
        _textCount = 0;
        EnsureCapacity(ref _visuals, _visualCount);
        EnsureCapacity(ref _clips, _clipCount);
        EnsureCapacity(ref _text, 0);
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

        displayList = new(_visuals.AsSpan(0, _visualCount), _clips.AsSpan(0, _clipCount), _text.AsSpan(0, _textCount));
        diagnostic = null;
        return true;
    }

    private static RetainedContracts.UiPointerEventKind ToRetainedPointerKind(UiPointerEventKind kind) => kind switch
    {
        UiPointerEventKind.ButtonDown => RetainedContracts.UiPointerEventKind.Down,
        UiPointerEventKind.ButtonUp => RetainedContracts.UiPointerEventKind.Up,
        UiPointerEventKind.Wheel => RetainedContracts.UiPointerEventKind.Wheel,
        _ => RetainedContracts.UiPointerEventKind.Move,
    };
    private static float4 ToFloat4(RetainedContracts.UiRect value) => new(value.X, value.Y, value.Width, value.Height);
    private static float4 ToColor(RetainedContracts.UiColor value) => new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
    private static void EnsureCapacity<T>(ref T[] storage, int count) { if (storage.Length < count) { Array.Resize(ref storage, Math.Max(8, count)); } }
}
