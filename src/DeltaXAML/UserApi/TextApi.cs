using Delta.Text.Contract;

namespace Delta.XAML;

/// <summary>Resolves a stable XAML font key to immutable font bytes for DeltaText.</summary>
public interface IUiFontResolver
{
    bool TryResolve(string fontKey, out FontOpenRequest request);
}

/// <summary>
/// Small explicit font catalog for the text adapter. It owns registrations, while
/// DeltaText owns its private copy of the bytes after <c>OpenFont</c>.
/// </summary>
public sealed class UiFontCatalog : IUiFontResolver
{
    private readonly Dictionary<string, FontOpenRequest> _fonts = new(StringComparer.Ordinal);

    public void Register(
        string fontKey,
        FontSourceId source,
        ReadOnlyMemory<byte> data,
        uint faceIndex = 0,
        ReadOnlyMemory<FontVariation> variations = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        if (!source.IsValid)
        {
            throw new ArgumentException("A valid font source identity is required.", nameof(source));
        }

        if (data.IsEmpty)
        {
            throw new ArgumentException("Font data cannot be empty.", nameof(data));
        }

        _fonts[fontKey] = new FontOpenRequest(source, data, faceIndex, variations);
    }

    public bool TryResolve(string fontKey, out FontOpenRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        return _fonts.TryGetValue(fontKey, out request);
    }
}
