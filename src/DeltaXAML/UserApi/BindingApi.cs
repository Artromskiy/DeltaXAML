using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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

/// <summary>Compact binding expression used by XAML and code.</summary>
public sealed class UiBindingExpression
{
    public UiBindingExpression(
        string path,
        UiBindingMode mode = UiBindingMode.OneWay,
        IUiValueConverter? converter = null,
        string? stringFormat = null,
        object? fallbackValue = null,
        object? targetNullValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        Mode = mode;
        Converter = converter;
        StringFormat = stringFormat;
        FallbackValue = fallbackValue;
        TargetNullValue = targetNullValue;
    }

    public string Path { get; }

    public UiBindingMode Mode { get; }

    public IUiValueConverter? Converter { get; }

    public string? StringFormat { get; }

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
        return new UiBindingExpression(spec.Path, mode, null, spec.StringFormat);
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
        UiBindingMode mode = UiBindingMode.OneWay)
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
        if (mode != UiBindingMode.OneTime && source is INotifyPropertyChanged observable)
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
        if (value is null)
        {
            diagnostic = BindingDiagnostic($"Expected {typeof(TValue).Name}.");
            return false;
        }

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
