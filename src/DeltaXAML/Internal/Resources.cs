using System.Diagnostics.CodeAnalysis;
using Delta.XAML.Contract;

namespace DeltaXAML.Internal;

internal sealed class UiResourceChangedEventArgs : EventArgs
{
    internal UiResourceChangedEventArgs(string key, Guid resourceId = default)
    {
        Key = key;
        ResourceId = resourceId;
    }

    public string Key { get; }
    public Guid ResourceId { get; }
}

/// <summary>Retained resource storage used by the public resource catalog and style slots.</summary>
internal sealed class UiResourceStore
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, int> _resourceSlots = new();
    private readonly List<ResourceSlot> _slots = new();

    public event EventHandler<UiResourceChangedEventArgs>? Changed;

    public void Set(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        SetCore(key, Guid.Empty, value);
    }

    internal void Set(Guid resource, object? value)
    {
        if (resource == Guid.Empty)
        {
            throw new ArgumentException("A resource identity is required.", nameof(resource));
        }

        SetCore(resource.ToString("D"), resource, value);
    }

    private void SetCore(string key, Guid resourceId, object? value)
    {
        if (_values.TryGetValue(key, out var current) && Equals(current, value))
        {
            if (resourceId != Guid.Empty)
            {
                EnsureResourceSlot(key, resourceId, value);
            }

            return;
        }

        _values[key] = value;
        if (resourceId != Guid.Empty)
        {
            EnsureResourceSlot(key, resourceId, value);
        }

        Changed?.Invoke(this, new(key, resourceId));
    }

    public bool TryGet(string key, out object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _values.TryGetValue(key, out value);
    }

    internal bool DependsOn(string referenceKey, string changedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(changedKey);
        return DependsOn(new UiResourceReference(referenceKey), new(changedKey));
    }

    internal bool DependsOn(UiResourceReference reference, UiResourceChangedEventArgs changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.Key);
        ArgumentNullException.ThrowIfNull(changed);
        if (ReferenceMatches(reference, changed))
        {
            return true;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = reference;
        while (TryGetReferenceValue(current, out var value) && value is UiResourceReference next)
        {
            var identity = Identity(next);
            if (!visited.Add(identity))
            {
                return false;
            }

            current = next;
            if (ReferenceMatches(current, changed))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryResolve(string key, out object? value, [NotNullWhen(false)] out string? diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return TryResolve(new UiResourceReference(key), out value, out diagnostic);
    }

    internal bool TryResolve(Guid resource, out object? value, [NotNullWhen(false)] out string? diagnostic)
    {
        if (resource == Guid.Empty)
        {
            value = null;
            diagnostic = "A resource identity is required.";
            return false;
        }

        return TryResolve(new UiResourceReference(resource), out value, out diagnostic);
    }

    internal bool TryResolve(UiResourceReference reference, out object? value, [NotNullWhen(false)] out string? diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.Key);
        if (!TryGetReferenceValue(reference, out var candidate))
        {
            value = null;
            diagnostic = $"Resource '{Identity(reference)}' was not found.";
            return false;
        }

        if (candidate is not UiResourceReference firstReference)
        {
            value = candidate;
            diagnostic = null;
            return true;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { Identity(reference) };
        var current = firstReference;
        while (true)
        {
            if (!visited.Add(Identity(current)))
            {
                value = null;
                diagnostic = $"Resource cycle detected at '{Identity(current)}'.";
                return false;
            }

            if (!TryGetReferenceValue(current, out var nextValue))
            {
                value = null;
                diagnostic = $"Resource '{Identity(current)}' was not found.";
                return false;
            }

            if (nextValue is not UiResourceReference nextReference)
            {
                value = nextValue;
                diagnostic = null;
                return true;
            }

            current = nextReference;
        }
    }

    internal int ResourceSlotCount => _slots.Count;

    internal UiEffectResource[] SnapshotEffectResources()
    {
        var resources = new List<UiEffectResource>();
        var identities = new HashSet<Guid>();
        foreach (var value in _values.Values)
        {
            if (value is UiEffectResource effect && identities.Add(effect.Set.Resource.Value))
            {
                resources.Add(effect);
            }
        }

        return resources.ToArray();
    }

    private void EnsureResourceSlot(string key, Guid resourceId, object? value)
    {
        if (_resourceSlots.TryGetValue(resourceId, out var slot))
        {
            _slots[slot] = new(key, value);
            return;
        }

        _resourceSlots.Add(resourceId, _slots.Count);
        _slots.Add(new(key, value));
    }

    private bool TryGetReferenceValue(UiResourceReference reference, out object? value)
    {
        if (reference.HasResourceId && _resourceSlots.TryGetValue(reference.ResourceId, out var slot))
        {
            value = _slots[slot].Value;
            return true;
        }

        return _values.TryGetValue(reference.Key, out value);
    }

    private static bool ReferenceMatches(UiResourceReference reference, UiResourceChangedEventArgs changed) =>
        (reference.HasResourceId && reference.ResourceId == changed.ResourceId) ||
        string.Equals(reference.Key, changed.Key, StringComparison.Ordinal);

    private static string Identity(UiResourceReference reference) =>
        reference.HasResourceId ? reference.ResourceId.ToString("D") : reference.Key;

    private readonly record struct ResourceSlot(string Key, object? Value);
}
