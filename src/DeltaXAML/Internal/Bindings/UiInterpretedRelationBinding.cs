using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Delta.Diagnostics;

namespace DeltaXAML.Internal;

/// <summary>Cold relation binding used when a source explicitly selects the interpreted loader.</summary>
internal sealed class UiInterpretedRelationBinding : Delta.XAML.IUiBinding, Delta.XAML.IUiBindingChanged, IUiRelationBindingMetadata, IUiBindingContextAware, IDisposable
{
    private readonly UiElement _target;
    private readonly Delta.XAML.UiBindingSourceKind _sourceKind;
    private readonly string? _sourceArgument;
    private readonly string[] _segments;
    private readonly Delta.XAML.UiBindingMode _mode;
    private readonly Delta.XAML.IUiValueConverter? _converter;
    private readonly string? _format;
    private readonly CultureInfo? _culture;
    private readonly IReadOnlyDictionary<string, UiElement>? _names;
    private readonly UiElement? _templateOwner;
    private object? _context;
    private INotifyPropertyChanged? _observable;
    private UiElement? _observed;
    private bool _disposed;

    internal UiInterpretedRelationBinding(
        UiElement target,
        Delta.XAML.UiBindingSourceKind sourceKind,
        string? sourceArgument,
        string path,
        Delta.XAML.UiBindingMode mode,
        Delta.XAML.IUiValueConverter? converter,
        string? format,
        CultureInfo? culture,
        IReadOnlyDictionary<string, UiElement>? names,
        UiElement? templateOwner)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _target = target;
        _sourceKind = sourceKind;
        _sourceArgument = sourceArgument;
        _segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _mode = mode;
        _converter = converter;
        _format = format;
        _culture = culture;
        _names = names;
        _templateOwner = templateOwner;
        _context = target.BindingContext;
        Observe(ResolveSource());
    }

    public Type ValueType => typeof(object);
    public Delta.XAML.UiBindingMode Mode => _mode;
    Delta.XAML.UiBindingSourceKind IUiRelationBindingMetadata.SourceKind => _sourceKind;
    public event EventHandler? Changed;

    public void SetContext(object? context)
    {
        if (ReferenceEquals(_context, context))
        {
            return;
        }

        _context = context;
        Observe(ResolveSource());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public object? Read()
    {
        var source = ResolveSource();
        Observe(source);
        if (!TryReadPath(source, out var value))
        {
            return null;
        }

        if (_converter is not null)
        {
            value = _converter.Convert(value, typeof(object), null);
        }

        return _format is { Length: > 0 }
            ? string.Format(_culture ?? CultureInfo.InvariantCulture, _format, value)
            : value;
    }

    public bool TryWrite(object? value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        diagnostic = null;
        if (_mode != Delta.XAML.UiBindingMode.TwoWay || !TryGetPathTarget(ResolveSource(), out var target, out var property) || target is null || property is null)
        {
            diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, "The interpreted relation binding is read-only or has no writable source.", null);
            return false;
        }

        if (_converter is not null && !_converter.TryConvertBack(value, property.PropertyType, null, out value, out diagnostic))
        {
            return false;
        }

        if (!TryConvert(value, property.PropertyType, out value))
        {
            diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, $"Value cannot be converted to {property.PropertyType.Name}.", null);
            return false;
        }

        try
        {
            property.SetValue(target, value);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or TargetInvocationException)
        {
            diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, exception.Message, null);
            return false;
        }
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
            _observable.PropertyChanged -= OnPropertyChanged;
        }

        if (_observed is not null)
        {
            _observed.EffectivePropertyChanged -= OnRetainedPropertyChanged;
        }
    }

    private object? ResolveSource() => _sourceKind switch
    {
        Delta.XAML.UiBindingSourceKind.Self => _target,
        Delta.XAML.UiBindingSourceKind.TemplateOwner => _templateOwner,
        Delta.XAML.UiBindingSourceKind.Name => _sourceArgument is not null && _names is not null && _names.TryGetValue(_sourceArgument, out var named) ? named : null,
        Delta.XAML.UiBindingSourceKind.Ancestor => FindAncestor(_target, _sourceArgument),
        _ => _context,
    };

    private static UiElement? FindAncestor(UiElement target, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var normalized = NormalizeTypeName(typeName);
        for (var candidate = target.Parent; candidate is not null; candidate = candidate.Parent)
        {
            if (string.Equals(NormalizeTypeName(candidate.TypeName), normalized, StringComparison.Ordinal) ||
                string.Equals(candidate.TypeName, normalized, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string NormalizeTypeName(string value)
    {
        var separator = value.LastIndexOf('.');
        return separator < 0 ? value.TrimStart(':') : value[(separator + 1)..];
    }

    private bool TryReadPath(object? source, out object? value)
    {
        value = source;
        for (var index = 0; index < _segments.Length; index++)
        {
            if (value is null)
            {
                return false;
            }

            var property = value.GetType().GetProperty(_segments[index], BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanRead)
            {
                return false;
            }

            value = property.GetValue(value);
        }

        return true;
    }

    private bool TryGetPathTarget(object? source, out object? target, out PropertyInfo? property)
    {
        target = source;
        property = null;
        for (var index = 0; index < _segments.Length; index++)
        {
            if (target is null)
            {
                return false;
            }

            var candidate = target.GetType().GetProperty(_segments[index], BindingFlags.Instance | BindingFlags.Public);
            if (candidate is null || !candidate.CanRead)
            {
                return false;
            }

            if (index == _segments.Length - 1)
            {
                property = candidate;
                return candidate.CanWrite;
            }

            target = candidate.GetValue(target);
        }

        return false;
    }

    private void Observe(object? source)
    {
        if (source is INotifyPropertyChanged observable && !ReferenceEquals(observable, _observable))
        {
            if (_observable is not null)
            {
                _observable.PropertyChanged -= OnPropertyChanged;
            }

            _observable = observable;
            if (_mode != Delta.XAML.UiBindingMode.OneTime)
            {
                _observable.PropertyChanged += OnPropertyChanged;
            }
        }
        else if (source is not INotifyPropertyChanged && _observable is not null)
        {
            _observable.PropertyChanged -= OnPropertyChanged;
            _observable = null;
        }

        if (source is UiElement retained && !ReferenceEquals(retained, _observed))
        {
            if (_observed is not null)
            {
                _observed.EffectivePropertyChanged -= OnRetainedPropertyChanged;
            }

            _observed = retained;
            if (_mode != Delta.XAML.UiBindingMode.OneTime)
            {
                _observed.EffectivePropertyChanged += OnRetainedPropertyChanged;
            }
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        _ = sender;
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == _segments[0])
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRetainedPropertyChanged(string propertyName)
    {
        _ = propertyName;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryConvert(object? value, Type targetType, out object? converted)
    {
        converted = value;
        if (value is Delta.XAML.UiColor publicColor && targetType == typeof(UiColor))
        {
            converted = new UiColor(publicColor.R, publicColor.G, publicColor.B, publicColor.A);
            return true;
        }

        if (value is null)
        {
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null;
        }

        var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullable.IsInstanceOfType(value))
        {
            return true;
        }

        try
        {
            converted = Convert.ChangeType(value, nonNullable, CultureInfo.InvariantCulture);
            return true;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Cold multi binding over relation paths; values are formatted or resolved by a registered function.</summary>
internal sealed class UiInterpretedMultiBinding : Delta.XAML.IUiBinding, Delta.XAML.IUiBindingChanged, IUiBindingContextAware, IDisposable
{
    private readonly UiElement _target;
    private readonly UiMultiBindingSpec _spec;
    private readonly CultureInfo? _culture;
    private readonly IReadOnlyDictionary<string, UiElement>? _names;
    private readonly UiElement? _templateOwner;
    private readonly Delta.XAML.IUiBindingFunctionResolver? _functions;
    private readonly List<INotifyPropertyChanged> _observables = new();
    private readonly List<UiElement> _retainedSources = new();
    private bool _disposed;

    internal UiInterpretedMultiBinding(
        UiElement target,
        IReadOnlyList<UiMultiBindingSourceSpec> sources,
        string? functionKey,
        string? format,
        CultureInfo? culture,
        IReadOnlyDictionary<string, UiElement>? names,
        UiElement? templateOwner,
        Delta.XAML.IUiBindingFunctionResolver? functions)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _spec = new UiMultiBindingSpec(string.Empty, sources.ToArray(), functionKey, format, culture?.Name);
        _culture = culture;
        _names = names;
        _templateOwner = templateOwner;
        _functions = functions;
        RefreshObservers();
    }

    public Type ValueType => typeof(object);
    public Delta.XAML.UiBindingMode Mode => Delta.XAML.UiBindingMode.OneWay;
    public event EventHandler? Changed;

    public void SetContext(object? context)
    {
        _ = context;
        RefreshObservers();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public object? Read()
    {
        var values = new object?[_spec.Sources.Length];
        RefreshObservers();
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = ReadPath(ResolveSource(_spec.Sources[index]), _spec.Sources[index].Path);
        }

        if (_spec.FunctionKey is { } function)
        {
            return _functions is not null && _functions.TryInvokeFunction(function, values, out var result) ? result : null;
        }

        return _spec.StringFormat is { Length: > 0 }
            ? string.Format(_culture ?? CultureInfo.InvariantCulture, _spec.StringFormat, values)
            : values.Length == 0 ? null : values[0];
    }

    public bool TryWrite(object? value, [NotNullWhen(false)] out Diagnostic? diagnostic)
    {
        diagnostic = new(new DiagnosticCode("XAML_BINDING"), DiagnosticSeverity.Error, "Multi bindings are read-only.", null);
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var index = 0; index < _observables.Count; index++)
        {
            _observables[index].PropertyChanged -= OnPropertyChanged;
        }

        for (var index = 0; index < _retainedSources.Count; index++)
        {
            _retainedSources[index].EffectivePropertyChanged -= OnRetainedPropertyChanged;
        }
    }

    private object? ResolveSource(UiMultiBindingSourceSpec source) => source.SourceKind switch
    {
        Delta.XAML.UiBindingSourceKind.Self => _target,
        Delta.XAML.UiBindingSourceKind.TemplateOwner => _templateOwner,
        Delta.XAML.UiBindingSourceKind.Name => source.SourceArgument is not null && _names is not null && _names.TryGetValue(source.SourceArgument, out var named) ? named : null,
        Delta.XAML.UiBindingSourceKind.Ancestor => FindAncestor(_target, source.SourceArgument),
        _ => _target.BindingContext,
    };

    private static object? ReadPath(object? source, string path)
    {
        var value = source;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            if (value is null)
            {
                return null;
            }

            var property = value.GetType().GetProperty(segments[index], BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanRead)
            {
                return null;
            }

            value = property.GetValue(value);
        }

        return value;
    }

    private void RefreshObservers()
    {
        for (var index = 0; index < _observables.Count; index++)
        {
            _observables[index].PropertyChanged -= OnPropertyChanged;
        }

        _observables.Clear();
        for (var index = 0; index < _retainedSources.Count; index++)
        {
            _retainedSources[index].EffectivePropertyChanged -= OnRetainedPropertyChanged;
        }

        _retainedSources.Clear();
        for (var index = 0; index < _spec.Sources.Length; index++)
        {
            var source = ResolveSource(_spec.Sources[index]);
            if (source is INotifyPropertyChanged observable)
            {
                _observables.Add(observable);
                observable.PropertyChanged += OnPropertyChanged;
            }

            if (source is UiElement retained)
            {
                _retainedSources.Add(retained);
                retained.EffectivePropertyChanged += OnRetainedPropertyChanged;
            }
        }
    }

    private static UiElement? FindAncestor(UiElement target, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var normalized = typeName[(typeName.LastIndexOf('.') + 1)..];
        for (var candidate = target.Parent; candidate is not null; candidate = candidate.Parent)
        {
            if (string.Equals(candidate.TypeName, normalized, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRetainedPropertyChanged(string propertyName)
    {
        _ = propertyName;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
