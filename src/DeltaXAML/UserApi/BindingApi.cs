using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Delta.Diagnostics;

namespace Delta.XAML;

/// <summary>Converts a binding value at the target boundary.</summary>
public interface IUiValueConverter
{
    object? Convert(object? value, Type targetType, object? parameter);

    bool TryConvertBack(
        object? value,
        Type sourceType,
        object? parameter,
        out object? result,
        [NotNullWhen(false)] out Diagnostic? diagnostic);
}

/// <summary>Resolves explicitly registered converter names used by XAML binding markup.</summary>
public interface IUiBindingResolver
{
    bool TryResolveConverter(string key, [NotNullWhen(true)] out IUiValueConverter? converter);
}

/// <summary>Optional change notification implemented by programmatic bindings.</summary>
public interface IUiBindingChanged
{
    event EventHandler? Changed;
}

/// <summary>Closed generated relation-source vocabulary.</summary>
public enum UiBindingSourceKind
{
    Context,
    Self,
    TemplateOwner,
    Name,
    Ancestor,
}

/// <summary>Construction-time or generation-cached relation used by compiled bindings.</summary>
public readonly record struct UiBindingSource
{
    private UiBindingSource(UiBindingSourceKind kind, UiElement? fixedElement, UiTypeId ancestorType)
    {
        Kind = kind;
        FixedElement = fixedElement;
        AncestorType = ancestorType;
    }

    public UiBindingSourceKind Kind { get; }

    public UiTypeId AncestorType { get; }

    internal UiElement? FixedElement { get; }

    public static UiBindingSource Self => new(UiBindingSourceKind.Self, null, default);

    public static UiBindingSource Context => new(UiBindingSourceKind.Context, null, default);

    public static UiBindingSource TemplateOwner(UiElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return new(UiBindingSourceKind.TemplateOwner, owner, default);
    }

    public static UiBindingSource NamedElement(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new(UiBindingSourceKind.Name, element, default);
    }

    public static UiBindingSource Ancestor(UiTypeId type)
    {
        if (!type.IsValid)
        {
            throw new ArgumentException("A stable ancestor type identity is required.", nameof(type));
        }

        return new(UiBindingSourceKind.Ancestor, null, type);
    }
}

/// <summary>Static typed read/write operations emitted for one relation binding.</summary>
public interface IUiRelationBindingPlan<TPlan, TValue>
    where TPlan : IUiRelationBindingPlan<TPlan, TValue>
{
    static abstract TValue Read(UiElement source);

    static abstract bool TryWrite(UiElement source, TValue value);
}

/// <summary>Static typed function over several cached retained relation sources.</summary>
public interface IUiMultiBindingPlan<TPlan, TValue>
    where TPlan : IUiMultiBindingPlan<TPlan, TValue>
{
    static abstract TValue Read(ReadOnlySpan<UiElement> sources);
}

/// <summary>Static typed function over a generated context and cached retained relation sources.</summary>
public interface IUiContextMultiBindingPlan<TPlan, TContext, TValue>
    where TPlan : IUiContextMultiBindingPlan<TPlan, TContext, TValue>
{
    static abstract TValue Read(TContext context, ReadOnlySpan<UiElement> sources);
}

/// <summary>Generated relation binding with lazy re-resolution only after structural change.</summary>
public sealed class UiRelationBinding<TPlan, TValue> : IUiBinding<TValue>, IUiBindingChanged, DeltaXAML.Internal.IUiRelationBindingMetadata, IDisposable
    where TPlan : IUiRelationBindingPlan<TPlan, TValue>
{
    private readonly UiElement _target;
    private readonly UiBindingSource _source;
    private UiElement? _resolved;
    private uint _relationVersion = uint.MaxValue;
    private TValue? _lastValue;
    private bool _hasLastValue;
    private UiElement? _observed;
    private bool _disposed;

    public UiRelationBinding(UiElement target, UiBindingSource source, UiBindingMode mode = UiBindingMode.OneWay)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (source.Kind == UiBindingSourceKind.Context)
        {
            throw new ArgumentException("Context bindings use UiCompiledBinding<TSource,TValue>.", nameof(source));
        }

        _target = target;
        _source = source;
        Mode = mode;
        if (source.Kind is UiBindingSourceKind.Name or UiBindingSourceKind.TemplateOwner && source.FixedElement is { } fixedElement)
        {
            Observe(fixedElement);
        }
        else if (source.Kind == UiBindingSourceKind.Self)
        {
            Observe(target);
        }
    }

    public Type ValueType => typeof(TValue);

    public UiBindingMode Mode { get; }

    UiBindingSourceKind DeltaXAML.Internal.IUiRelationBindingMetadata.SourceKind => _source.Kind;

    public event EventHandler? Changed;

    public TValue ReadValue()
    {
        var value = TPlan.Read(ResolveSource());
        _lastValue = value;
        _hasLastValue = true;
        return value;
    }

    public object? Read() => ReadValue();

    public bool TryWriteValue(TValue value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        if (Mode != UiBindingMode.TwoWay || !TPlan.TryWrite(ResolveSource(), value))
        {
            diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, "The relation binding is read-only or rejected the value.", null);
            return false;
        }

        diagnostic = null;
        return true;
    }

    public bool TryWrite(object? value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        if (value is not TValue typed)
        {
            diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, $"Expected {typeof(TValue).Name}.", null);
            return false;
        }

        return TryWriteValue(typed, out diagnostic);
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_observed is { } observed)
        {
            observed.RetainedElement.EffectivePropertyChanged -= OnSourcePropertyChanged;
            _observed = null;
        }
    }

    /// <summary>Refreshes a generated relation slot only when its typed value changed.</summary>
    public bool Refresh()
    {
        if (_source.Kind != UiBindingSourceKind.Ancestor ||
            (_hasLastValue && _relationVersion == _target.RetainedElement.RelationVersion))
        {
            return false;
        }

        var previous = _lastValue;
        var hadValue = _hasLastValue;
        var next = ReadValue();
        if (hadValue && EqualityComparer<TValue>.Default.Equals(previous, next))
        {
            return false;
        }

        _lastValue = next;
        _hasLastValue = true;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal UiElement ResolveSource()
    {
        if (_source.Kind is UiBindingSourceKind.Name or UiBindingSourceKind.TemplateOwner)
        {
            return _source.FixedElement ?? throw new InvalidOperationException("The generated fixed relation source is missing.");
        }

        if (_source.Kind == UiBindingSourceKind.Self)
        {
            return _target;
        }

        var currentVersion = _target.RetainedElement.RelationVersion;
        if (_resolved is not null && _relationVersion == currentVersion)
        {
            return _resolved;
        }

        for (var candidate = _target.Parent; candidate is not null; candidate = candidate.Parent)
        {
            if (DeltaXAML.Internal.UiDescriptorCatalog.MatchesType(candidate.RetainedElement, _source.AncestorType))
            {
                _resolved = candidate;
                _relationVersion = currentVersion;
                Observe(candidate);
                return candidate;
            }
        }

        _resolved = null;
        _relationVersion = currentVersion;
        throw new InvalidOperationException($"No attached ancestor matches '{_source.AncestorType.Value:D}'.");
    }

    private void Observe(UiElement element)
    {
        if (ReferenceEquals(_observed, element))
        {
            return;
        }

        if (_observed is { } previous)
        {
            previous.RetainedElement.EffectivePropertyChanged -= OnSourcePropertyChanged;
        }

        _observed = element;
        element.RetainedElement.EffectivePropertyChanged += OnSourcePropertyChanged;
    }

    private void OnSourcePropertyChanged(string propertyName)
    {
        _ = propertyName;
        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>Generated typed function over a fixed set of relation bindings.</summary>
public sealed class UiMultiBinding<TPlan, TValue> : IUiBinding<TValue>, IUiBindingChanged, IDisposable
    where TPlan : IUiMultiBindingPlan<TPlan, TValue>
{
    private readonly UiElement[] _sources;

    public UiMultiBinding(params UiElement[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Length == 0)
        {
            throw new ArgumentException("A multi binding requires at least one typed source.", nameof(sources));
        }

        _sources = (UiElement[])sources.Clone();
        for (var i = 0; i < _sources.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(_sources[i], nameof(sources));
            _sources[i].RetainedElement.EffectivePropertyChanged += OnSourcePropertyChanged;
        }
    }

    public Type ValueType => typeof(TValue);

    public UiBindingMode Mode => UiBindingMode.OneWay;

    public event EventHandler? Changed;

    public TValue ReadValue() => TPlan.Read(_sources);

    public object? Read() => ReadValue();

    public bool TryWriteValue(TValue value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, "Multi bindings are read-only.", null);
        return false;
    }

    public bool TryWrite(object? value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, "Multi bindings are read-only.", null);
        return false;
    }

    public void Dispose()
    {
        for (var i = 0; i < _sources.Length; i++)
        {
            _sources[i].RetainedElement.EffectivePropertyChanged -= OnSourcePropertyChanged;
        }
    }

    private void OnSourcePropertyChanged(string propertyName)
    {
        _ = propertyName;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Generated multi-binding over fixed or generation-cached retained relation sources.</summary>
public sealed class UiRelationMultiBinding<TPlan, TValue> : IUiBinding<TValue>, IUiBindingChanged, IDisposable
    where TPlan : IUiMultiBindingPlan<TPlan, TValue>
{
    private readonly UiElement _target;
    private readonly UiBindingSource[] _sources;
    private readonly UiElement[] _resolved;
    private uint _relationVersion = uint.MaxValue;
    private TValue? _lastValue;
    private bool _hasLastValue;

    public UiRelationMultiBinding(UiElement target, params UiBindingSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Length == 0)
        {
            throw new ArgumentException("A relation multi-binding requires at least one source.", nameof(sources));
        }

        _target = target;
        _sources = (UiBindingSource[])sources.Clone();
        _resolved = new UiElement[sources.Length];
        ResolveSources();
    }

    public Type ValueType => typeof(TValue);

    public UiBindingMode Mode => UiBindingMode.OneWay;

    public event EventHandler? Changed;

    public TValue ReadValue()
    {
        ResolveSources();
        var value = TPlan.Read(_resolved);
        _lastValue = value;
        _hasLastValue = true;
        return value;
    }

    public object? Read() => ReadValue();

    public bool TryWriteValue(TValue value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, "Multi bindings are read-only.", null);
        return false;
    }

    public bool TryWrite(object? value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, "Multi bindings are read-only.", null);
        return false;
    }

    public bool Refresh()
    {
        if (_hasLastValue && _relationVersion == _target.RetainedElement.RelationVersion)
        {
            return false;
        }

        var previous = _lastValue;
        var hadValue = _hasLastValue;
        var next = ReadValue();
        if (hadValue && EqualityComparer<TValue>.Default.Equals(previous, next))
        {
            return false;
        }

        _lastValue = next;
        _hasLastValue = true;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
        for (var i = 0; i < _resolved.Length; i++)
        {
            if (_resolved[i] is { } source)
            {
                source.RetainedElement.EffectivePropertyChanged -= OnSourcePropertyChanged;
            }
        }
    }

    private void ResolveSources()
    {
        var currentVersion = _target.RetainedElement.RelationVersion;
        if (_relationVersion == currentVersion)
        {
            return;
        }

        for (var i = 0; i < _sources.Length; i++)
        {
            var previous = _resolved[i];
            var resolved = Resolve(_sources[i]);
            if (ReferenceEquals(previous, resolved))
            {
                continue;
            }

            if (previous is not null)
            {
                previous.RetainedElement.EffectivePropertyChanged -= OnSourcePropertyChanged;
            }

            _resolved[i] = resolved;
            resolved.RetainedElement.EffectivePropertyChanged += OnSourcePropertyChanged;
        }

        _relationVersion = currentVersion;
    }

    private UiElement Resolve(UiBindingSource source)
    {
        if (source.Kind == UiBindingSourceKind.Self)
        {
            return _target;
        }

        if (source.Kind is UiBindingSourceKind.Name or UiBindingSourceKind.TemplateOwner)
        {
            return source.FixedElement ?? throw new InvalidOperationException("A fixed generated relation source is missing.");
        }

        if (source.Kind != UiBindingSourceKind.Ancestor)
        {
            throw new InvalidOperationException("Context sources use a typed generated context plan.");
        }

        for (var candidate = _target.Parent; candidate is not null; candidate = candidate.Parent)
        {
            if (DeltaXAML.Internal.UiDescriptorCatalog.MatchesType(candidate.RetainedElement, source.AncestorType))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No attached ancestor matches '{source.AncestorType.Value:D}'.");
    }

    private void OnSourcePropertyChanged(string propertyName)
    {
        _ = propertyName;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Generated multi-binding combining one typed data context with fixed or generation-cached
/// retained relation sources. Context slots never enter the retained element array.
/// </summary>
public sealed class UiContextMultiBinding<TPlan, TContext, TValue> : IUiBinding<TValue>, IUiBindingChanged, IDisposable
    where TPlan : IUiContextMultiBindingPlan<TPlan, TContext, TValue>
{
    private readonly TContext _context;
    private readonly UiElement _target;
    private readonly UiBindingSource[] _sources;
    private readonly UiElement[] _resolved;
    private readonly INotifyPropertyChanged? _observable;
    private uint _relationVersion = uint.MaxValue;
    private TValue? _lastValue;
    private bool _hasLastValue;
    private bool _disposed;

    public UiContextMultiBinding(TContext context, UiElement target, params UiBindingSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Length == 0)
        {
            throw new ArgumentException("A context multi-binding requires at least one source.", nameof(sources));
        }

        _context = context;
        _target = target;
        _sources = (UiBindingSource[])sources.Clone();
        _resolved = new UiElement[sources.Length];
        if (context is INotifyPropertyChanged observable)
        {
            _observable = observable;
            _observable.PropertyChanged += OnContextPropertyChanged;
        }

        ResolveSources();
    }

    public Type ValueType => typeof(TValue);

    public UiBindingMode Mode => UiBindingMode.OneWay;

    public event EventHandler? Changed;

    public TValue ReadValue()
    {
        ResolveSources();
        var value = TPlan.Read(_context, _resolved);
        _lastValue = value;
        _hasLastValue = true;
        return value;
    }

    public object? Read() => ReadValue();

    public bool TryWriteValue(TValue value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, "Multi bindings are read-only.", null);
        return false;
    }

    public bool TryWrite(object? value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, "Multi bindings are read-only.", null);
        return false;
    }

    public bool Refresh()
    {
        if (_hasLastValue && _relationVersion == _target.RetainedElement.RelationVersion)
        {
            return false;
        }

        var previous = _lastValue;
        var hadValue = _hasLastValue;
        var next = ReadValue();
        if (hadValue && EqualityComparer<TValue>.Default.Equals(previous, next))
        {
            return false;
        }

        _lastValue = next;
        _hasLastValue = true;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_observable is not null)
        {
            _observable.PropertyChanged -= OnContextPropertyChanged;
        }

        for (var i = 0; i < _resolved.Length; i++)
        {
            if (_sources[i].Kind != UiBindingSourceKind.Context && _resolved[i] is { } source)
            {
                source.RetainedElement.EffectivePropertyChanged -= OnSourcePropertyChanged;
            }
        }
    }

    private void ResolveSources()
    {
        var currentVersion = _target.RetainedElement.RelationVersion;
        if (_relationVersion == currentVersion)
        {
            return;
        }

        for (var i = 0; i < _sources.Length; i++)
        {
            if (_sources[i].Kind == UiBindingSourceKind.Context)
            {
                _resolved[i] = _target;
                continue;
            }

            var previous = _resolved[i];
            var resolved = Resolve(_sources[i]);
            if (ReferenceEquals(previous, resolved))
            {
                continue;
            }

            if (previous is not null)
            {
                previous.RetainedElement.EffectivePropertyChanged -= OnSourcePropertyChanged;
            }

            _resolved[i] = resolved;
            resolved.RetainedElement.EffectivePropertyChanged += OnSourcePropertyChanged;
        }

        _relationVersion = currentVersion;
    }

    private UiElement Resolve(UiBindingSource source)
    {
        if (source.Kind == UiBindingSourceKind.Self)
        {
            return _target;
        }

        if (source.Kind is UiBindingSourceKind.Name or UiBindingSourceKind.TemplateOwner)
        {
            return source.FixedElement ?? throw new InvalidOperationException("A fixed generated relation source is missing.");
        }

        if (source.Kind != UiBindingSourceKind.Ancestor)
        {
            throw new InvalidOperationException("Only generated context and retained relation sources are valid here.");
        }

        for (var candidate = _target.Parent; candidate is not null; candidate = candidate.Parent)
        {
            if (DeltaXAML.Internal.UiDescriptorCatalog.MatchesType(candidate.RetainedElement, source.AncestorType))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No attached ancestor matches '{source.AncestorType.Value:D}'.");
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSourcePropertyChanged(string propertyName)
    {
        _ = propertyName;
        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>Compact binding expression used by XAML and code.</summary>
public sealed class UiBindingExpression
{
    public UiBindingExpression(
        string path,
        UiBindingMode mode = UiBindingMode.OneWay,
        IUiValueConverter? converter = null,
        string? stringFormat = null,
        CultureInfo? culture = null,
        object? fallbackValue = null,
        object? targetNullValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (stringFormat is not null && culture is null)
        {
            throw new ArgumentException("StringFormat requires an explicit culture.", nameof(culture));
        }

        if (stringFormat is not null && mode == UiBindingMode.TwoWay)
        {
            throw new ArgumentException("A formatted binding cannot be TwoWay; bind the editable value separately.", nameof(mode));
        }

        Path = path;
        Mode = mode;
        Converter = converter;
        StringFormat = stringFormat;
        Culture = culture;
        FallbackValue = fallbackValue;
        TargetNullValue = targetNullValue;
    }

    public string Path { get; }

    public UiBindingMode Mode { get; }

    public IUiValueConverter? Converter { get; }

    public string? StringFormat { get; }

    public CultureInfo? Culture { get; }

    public object? FallbackValue { get; }

    public object? TargetNullValue { get; }

    public static UiBindingExpression Parse(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        if (!DeltaXAML.Internal.XamlBindingParser.TryParse(markup, out var spec, out var error))
        {
            throw new FormatException(error);
        }

        var mode = spec.Mode switch
        {
            DeltaXAML.Internal.UiBindingMode.OneTime => UiBindingMode.OneTime,
            DeltaXAML.Internal.UiBindingMode.OneWay => UiBindingMode.OneWay,
            DeltaXAML.Internal.UiBindingMode.TwoWay => UiBindingMode.TwoWay,
            _ => throw new FormatException($"Unsupported binding mode '{spec.Mode}'."),
        };
        var culture = spec.CultureName is null ? null : CultureInfo.GetCultureInfo(spec.CultureName);
        return new UiBindingExpression(spec.Path, mode, null, spec.StringFormat, culture);
    }
}

/// <summary>Explicit converter registry; no reflection discovery is performed.</summary>
public sealed class UiBindingResolver : IUiBindingResolver
{
    private readonly Dictionary<string, IUiValueConverter> _converters = new(StringComparer.Ordinal);

    public void Register(string key, IUiValueConverter converter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(converter);
        _converters[key] = converter;
    }

    public bool TryResolveConverter(string key, [NotNullWhen(true)] out IUiValueConverter? converter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _converters.TryGetValue(key, out converter);
    }
}

/// <summary>Typed code binding that can be refreshed by a source notification.</summary>
public sealed class UiCompiledBinding<TSource, TValue> : IUiBinding<TValue>, IUiBindingChanged, IDisposable
{
    private readonly TSource _source;
    private readonly Func<TSource, TValue> _read;
    private readonly Action<TSource, TValue>? _write;
    private readonly INotifyPropertyChanged? _observable;
    private int _disposed;

    public UiCompiledBinding(
        TSource source,
        Func<TSource, TValue> read,
        Action<TSource, TValue>? write = null,
        UiBindingMode mode = UiBindingMode.OneWay,
        bool subscribeToSource = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(read);
        if (mode == UiBindingMode.TwoWay && write is null)
        {
            throw new ArgumentException("Two-way bindings require a write delegate.", nameof(write));
        }

        _source = source;
        _read = read;
        _write = write;
        Mode = mode;
        if (subscribeToSource && mode != UiBindingMode.OneTime && source is INotifyPropertyChanged observable)
        {
            _observable = observable;
            _observable.PropertyChanged += OnPropertyChanged;
        }
    }

    public Type ValueType => typeof(TValue);

    public UiBindingMode Mode { get; }

    public event EventHandler? Changed;

    public TValue ReadValue() => _read(_source);

    public object? Read() => ReadValue();

    public bool TryWriteValue(TValue value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        if (Mode != UiBindingMode.TwoWay || _write is null)
        {
            diagnostic = BindingDiagnostic("Binding is read-only.");
            return false;
        }

        _write(_source, value);
        diagnostic = null;
        return true;
    }

    public bool TryWrite(object? value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        if (value is not TValue typed)
        {
            diagnostic = BindingDiagnostic($"Expected {typeof(TValue).Name}.");
            return false;
        }

        return TryWriteValue(typed, out diagnostic);
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _observable is not null)
        {
            _observable.PropertyChanged -= OnPropertyChanged;
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) => NotifyChanged();

    private static Diagnostic BindingDiagnostic(string message) =>
        new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, message, null);
}
