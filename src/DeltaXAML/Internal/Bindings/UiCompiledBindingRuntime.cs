using Delta.XAML;

namespace DeltaXAML.Internal;

internal interface IUiCompiledBindingRuntime : IDisposable
{
    void Attach();
    void ApplyPending();
    bool TryWrite(object? value, out string? diagnostic);
}

internal sealed class UiCompiledBindingRuntime<TSource, TValue> : IUiCompiledBindingRuntime
{
    private readonly UiElement _owner;
    private readonly UiCompiledBinding<TSource, TValue> _binding;
    private readonly string _propertyName;
    private readonly UiPropertyKey _property;
    private readonly UiDirtyMask _invalidation;
    private bool _pending;
    private bool _subscribed;
    private bool _disposed;

    internal UiCompiledBindingRuntime(
        UiElement owner,
        string propertyName,
        UiPropertyKey property,
        UiDirtyMask invalidation,
        UiCompiledBinding<TSource, TValue> binding,
        bool sourceNotificationsManaged)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(binding);
        _owner = owner;
        _propertyName = propertyName;
        _property = property;
        _invalidation = invalidation;
        _binding = binding;
        _sourceNotificationsManaged = sourceNotificationsManaged;
    }

    private readonly bool _sourceNotificationsManaged;

    public void Attach()
    {
        ApplyValue();
        if (!_sourceNotificationsManaged && _binding.Mode != Delta.XAML.UiBindingMode.OneTime)
        {
            _binding.Changed += OnBindingChanged;
            _subscribed = true;
        }
    }

    public void ApplyPending()
    {
        if (!_pending || _disposed)
        {
            return;
        }

        _pending = false;
        ApplyValue();
    }

    public bool TryWrite(object? value, out string? diagnostic)
    {
        if (value is not TValue typed)
        {
            diagnostic = $"Expected {typeof(TValue).Name}.";
            return false;
        }

        if (!_binding.TryWriteValue(typed, out var bindingDiagnostic))
        {
            diagnostic = bindingDiagnostic?.Message ?? "Binding write failed.";
            return false;
        }

        diagnostic = null;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_subscribed)
        {
            _binding.Changed -= OnBindingChanged;
            _subscribed = false;
        }
    }

    private void OnBindingChanged(object? sender, EventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _pending = true;
        _owner.InvalidateChanged(UiDirtyMask.Binding);
    }

    private void ApplyValue() => _owner.ApplyCompiledBinding(_propertyName, _property, _binding.ReadValue(), _invalidation);
}
