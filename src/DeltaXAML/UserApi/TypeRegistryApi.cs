using System.Diagnostics.CodeAnalysis;

namespace Delta.XAML;

/// <summary>Explicit XAML name-to-factory catalog; it performs no reflection discovery.</summary>
public sealed class XamlTypeCatalog : IXamlTypeResolver
{
    private readonly Dictionary<XamlQualifiedName, Entry> _entries = new();

    public void Register(XamlQualifiedName name, Func<UiElement> factory)
    {
        if (string.IsNullOrWhiteSpace(name.LocalName))
        {
            throw new ArgumentException("A local XAML type name is required.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(factory);
        var type = new UiTypeId(Guid.NewGuid());
        _entries[name] = new(type, factory);
    }

    public bool TryResolveName(in XamlQualifiedName name, out UiTypeId type)
    {
        if (string.IsNullOrWhiteSpace(name.LocalName))
        {
            type = default;
            return false;
        }

        if (_entries.TryGetValue(name, out var entry))
        {
            type = entry.Type;
            return true;
        }

        type = default;
        return false;
    }

    public bool TryCreate(UiTypeId type, [NotNullWhen(true)] out UiElement? element)
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.Type == type)
            {
                element = entry.Factory();
                return true;
            }
        }

        element = null;
        return false;
    }

    private sealed record Entry(UiTypeId Type, Func<UiElement> Factory);
}
