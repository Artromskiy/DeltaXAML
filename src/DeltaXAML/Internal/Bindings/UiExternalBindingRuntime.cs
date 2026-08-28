using DeltaBinding = Delta.XAML.IUiBinding;
using DeltaBindingChanged = Delta.XAML.IUiBindingChanged;
using DeltaBindingMode = Delta.XAML.UiBindingMode;

namespace DeltaXAML.Internal;

/// <summary>Stage-managed adapter for an explicitly supplied, already typed binding.</summary>
/// <remarks>
/// This path never parses or walks a string path. It owns only the notification
/// boundary; value resolution and source writes remain in the supplied binding.
/// </remarks>
internal sealed class UiExternalBindingRuntime : IDisposable
{
    private readonly UiElement _owner;
    private readonly DeltaBinding _binding;
    private readonly string _propertyName;
    private readonly UiPropertyKey _property;
    private readonly UiDirtyMask _invalidation;
    private bool _pending;
    private bool _subscribed;
    private bool _stageManaged;
    private bool _disposed;

    internal UiExternalBindingRuntime(
        UiElement owner,
        string propertyName,
        UiPropertyKey property,
        UiDirtyMask invalidation,
        DeltaBinding binding)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(binding);
        _owner = owner;
        _propertyName = propertyName;
        _property = property;
        _invalidation = invalidation;
        _binding = binding;
    }

    internal string PropertyName => _propertyName;

    internal void Attach()
    {
        ApplyValue();
        if (_binding.Mode != DeltaBindingMode.OneTime && _binding is DeltaBindingChanged changed)
        {
            changed.Changed += OnBindingChanged;
            _subscribed = true;
        }
    }

    internal void EnableStageManagement() => _stageManaged = true;

    internal void ApplyPending()
    {
        if (!_pending || _disposed)
        {
            return;
        }

        _pending = false;
        ApplyValue();
    }

    internal void WriteTarget(object? value)
    {
        if (_disposed || _binding.Mode != DeltaBindingMode.TwoWay)
        {
            return;
        }

        if (_binding.TryWrite(value, out _))
        {
            QueueRefresh();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_subscribed && _binding is DeltaBindingChanged changed)
        {
            changed.Changed -= OnBindingChanged;
            _subscribed = false;
        }
    }

    private void OnBindingChanged(object? sender, EventArgs args)
        => QueueRefresh();

    private void QueueRefresh()
    {
        if (_disposed)
        {
            return;
        }

        if (!_stageManaged)
        {
            ApplyValue();
            return;
        }

        if (_pending)
        {
            return;
        }

        _pending = true;
        _owner.Invalidate(UiDirtyMask.Binding);
    }

    private void ApplyValue() =>
        _owner.ApplyExternalBinding(_propertyName, _property, _binding.Read(), _invalidation);
}
