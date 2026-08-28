using System.Diagnostics.CodeAnalysis;

namespace DeltaXAML.Internal;

internal sealed class UiResourceChangedEventArgs(string key) : EventArgs
{
    public string Key { get; } = key;
}

/// <summary>Retained resource storage used by the public resource catalog and style slots.</summary>
internal sealed class UiResourceStore
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public event EventHandler<UiResourceChangedEventArgs>? Changed;

    public void Set(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_values.TryGetValue(key, out var current) && Equals(current, value))
        {
            return;
        }

        _values[key] = value;
        Changed?.Invoke(this, new(key));
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
        if (string.Equals(referenceKey, changedKey, StringComparison.Ordinal))
        {
            return true;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = referenceKey;
        while (_values.TryGetValue(current, out var value) && value is UiResourceReference reference)
        {
            if (!visited.Add(current))
            {
                return false;
            }

            current = reference.Key;
            if (string.Equals(current, changedKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryResolve(string key, out object? value, [NotNullWhen(false)] out string? diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_values.TryGetValue(key, out var candidate))
        {
            value = null;
            diagnostic = $"Resource '{key}' was not found.";
            return false;
        }

        if (candidate is not UiResourceReference firstReference)
        {
            value = candidate;
            diagnostic = null;
            return true;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { key };
        var current = firstReference.Key;
        while (true)
        {
            if (!visited.Add(current))
            {
                value = null;
                diagnostic = $"Resource cycle detected at '{current}'.";
                return false;
            }

            if (!_values.TryGetValue(current, out var nextValue))
            {
                value = null;
                diagnostic = $"Resource '{current}' was not found.";
                return false;
            }

            if (nextValue is not UiResourceReference reference)
            {
                value = nextValue;
                diagnostic = null;
                return true;
            }

            current = reference.Key;
        }
    }
}
