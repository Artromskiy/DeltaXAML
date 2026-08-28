using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Delta.XAML;

/// <summary>Explicit XAML name-to-factory catalog; it performs no reflection discovery.</summary>
public sealed class XamlTypeCatalog : IXamlTypeResolver
{
    private readonly Dictionary<XamlQualifiedName, Entry> _entries = new();
    private readonly Dictionary<UiTypeId, Entry> _entriesByType = new();

    public void Register(XamlQualifiedName name, Func<UiElement> factory)
    {
        Register(name, CreateStableTypeId(name), factory);
    }

    public void Register(XamlQualifiedName name, UiTypeId type, Func<UiElement> factory)
    {
        ArgumentNullException.ThrowIfNull(name.Namespace);
        if (string.IsNullOrWhiteSpace(name.LocalName))
        {
            throw new ArgumentException("A local XAML type name is required.", nameof(name));
        }

        if (!type.IsValid)
        {
            throw new ArgumentException("A stable XAML type identity is required.", nameof(type));
        }

        ArgumentNullException.ThrowIfNull(factory);
        if (_entriesByType.TryGetValue(type, out var existing) && !existing.Name.Equals(name))
        {
            throw new ArgumentException($"The XAML type identity '{type.Value}' is already registered for '{existing.Name.LocalName}'.", nameof(type));
        }

        if (_entries.TryGetValue(name, out var previous))
        {
            _entriesByType.Remove(previous.Type);
        }

        var entry = new Entry(name, type, factory);
        _entries[name] = entry;
        _entriesByType[type] = entry;
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
        if (_entriesByType.TryGetValue(type, out var entry))
        {
            element = entry.Factory();
            return true;
        }

        element = null;
        return false;
    }

    private static UiTypeId CreateStableTypeId(XamlQualifiedName name)
    {
        ArgumentNullException.ThrowIfNull(name.Namespace);
        if (string.IsNullOrWhiteSpace(name.LocalName))
        {
            throw new ArgumentException("A local XAML type name is required.", nameof(name));
        }

        var identity = string.Concat("DeltaXAML.TypeCatalog\0", name.Namespace, '\0', name.LocalName);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(identity), digest);
        digest[6] = (byte)((digest[6] & 0x0F) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3F) | 0x80);
        return new UiTypeId(new Guid(digest[..16]));
    }

    private sealed record Entry(XamlQualifiedName Name, UiTypeId Type, Func<UiElement> Factory);
}
