using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Delta.Diagnostics;
using PublicBinding = Delta.XAML.IUiBinding;
using PublicBindingChanged = Delta.XAML.IUiBindingChanged;
using PublicBindingExpression = Delta.XAML.UiBindingExpression;
using PublicBindingMode = Delta.XAML.UiBindingMode;
using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal sealed class UiBindingRuntime : IDisposable
{
    private readonly string[] _segments;
    private readonly PublicBindingExpression? _expression;
    private readonly PublicBinding? _external;
    private UiElement? _owner;
    private UiBindingValue? _binding;
    private object? _context;
    private INotifyPropertyChanged? _observable;
    private int _disposed;

    internal UiBindingRuntime(string propertyName, PublicBindingExpression expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(expression);
        PropertyName = propertyName;
        _expression = expression;
        _segments = expression.Path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_segments.Length == 0)
        {
            throw new ArgumentException("A binding path is required.", nameof(expression));
        }
    }

    internal UiBindingRuntime(string propertyName, PublicBinding external)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(external);
        PropertyName = propertyName;
        _external = external;
        _segments = Array.Empty<string>();
    }

    internal string PropertyName { get; }

    internal void Attach(UiElement owner, UiDirtyFlags invalidation)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        _binding = new UiBindingValue(Read, Write);
        owner.SetBinding(PropertyName, _binding, invalidation);
        if (_external is PublicBindingChanged changed && _external.Mode != PublicBindingMode.OneTime)
        {
            changed.Changed += OnExternalChanged;
        }

        Refresh();
    }

    internal void SetContext(object? context)
    {
        if (ReferenceEquals(_context, context))
        {
            Refresh();
            return;
        }

        if (_observable is not null)
        {
            _observable.PropertyChanged -= OnPropertyChanged;
        }

        _context = context;
        _observable = context as INotifyPropertyChanged;
        if (_observable is not null && _expression?.Mode != PublicBindingMode.OneTime)
        {
            _observable.PropertyChanged += OnPropertyChanged;
        }

        Refresh();
    }

    internal void Refresh()
    {
        if (_binding is null || _owner is null)
        {
            return;
        }

        _binding.NotifyChanged();
    }

    internal void WriteTarget(object? value)
    {
        if (_binding is null || (_expression is not null && _expression.Mode != PublicBindingMode.TwoWay))
        {
            return;
        }

        _binding.TryWrite(value, out _);
        _binding.NotifyChanged();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_observable is not null)
        {
            _observable.PropertyChanged -= OnPropertyChanged;
        }

        if (_external is PublicBindingChanged changed && _external.Mode != PublicBindingMode.OneTime)
        {
            changed.Changed -= OnExternalChanged;
        }

    }

    private object? Read()
    {
        if (_external is not null)
        {
            return _external.Read();
        }

        if (!TryReadPath(_context, out var value))
        {
            return _expression?.FallbackValue;
        }

        if (value is null && _expression?.TargetNullValue is not null)
        {
            value = _expression.TargetNullValue;
        }

        var converter = _expression?.Converter;
        if (converter is not null)
        {
            value = converter.Convert(value, _owner?.BindingTargetType(PropertyName) ?? typeof(object), null);
        }

        if (_expression?.StringFormat is { Length: > 0 } format)
        {
            value = string.Format(CultureInfo.InvariantCulture, format, value);
        }

        return value;
    }

    private (bool Success, string? Error) Write(object? value)
    {
        if (_external is not null)
        {
            var success = _external.TryWrite(value, out var diagnostic);
            return (success, diagnostic?.Message);
        }

        if (_expression?.Mode != PublicBindingMode.TwoWay)
        {
            return (false, "Binding is not two-way.");
        }

        if (!TryGetPathTarget(_context, out var target, out var property))
        {
            return (false, "The binding path could not be resolved.");
        }

        object? converted = value;
        if (_expression.Converter is not null)
        {
            if (!_expression.Converter.TryConvertBack(value, property.PropertyType, null, out converted, out var diagnostic))
            {
                return (false, diagnostic?.Message ?? "The binding converter rejected the value.");
            }
        }

        if (!TryConvert(converted, property.PropertyType, out converted))
        {
            return (false, $"Value cannot be converted to {property.PropertyType.Name}.");
        }

        try
        {
            property.SetValue(target, converted);
            return (true, null);
        }
        catch (Exception exception) when (exception is ArgumentException or TargetInvocationException)
        {
            return (false, exception.Message);
        }
    }

    private bool TryReadPath(object? source, out object? value)
    {
        value = source;
        for (var i = 0; i < _segments.Length; i++)
        {
            if (value is null)
            {
                return false;
            }

            var property = value.GetType().GetProperty(_segments[i], BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanRead)
            {
                return false;
            }

            value = property.GetValue(value);
        }

        return true;
    }

    private bool TryGetPathTarget(
        object? source,
        [NotNullWhen(true)] out object? target,
        [NotNullWhen(true)] out PropertyInfo? property)
    {
        target = source;
        property = null;
        for (var i = 0; i < _segments.Length; i++)
        {
            if (target is null)
            {
                return false;
            }

            var candidate = target.GetType().GetProperty(_segments[i], BindingFlags.Instance | BindingFlags.Public);
            if (candidate is null)
            {
                return false;
            }

            if (i == _segments.Length - 1)
            {
                property = candidate;
                return candidate.CanWrite;
            }

            target = candidate.GetValue(target);
        }

        return false;
    }

    private static bool TryConvert(object? value, Type targetType, out object? converted)
    {
        converted = value;
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

    private void OnExternalChanged(object? sender, EventArgs args) => Refresh();

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == _segments[0])
        {
            Refresh();
        }
    }
}
