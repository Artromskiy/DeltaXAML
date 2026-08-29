using Delta.Maths;
using Delta.Text.Contract;

namespace Delta.XAML.Contract;

/// <summary>Stable cross-project identity of a UI resource.</summary>
public readonly record struct UiResourceId(Guid Value)
{
    public static UiResourceId Empty => default;

    public bool IsValid => Value != Guid.Empty;
}

/// <summary>Frame-local index into <see cref="UiDisplayList.Clips"/>.</summary>
public readonly record struct UiClipId(int Value)
{
    public static UiClipId None => new(-1);

    public bool IsValid => Value >= 0;
}

/// <summary>Renderer-neutral clipping region in logical coordinates.</summary>
/// <param name="Bounds">Logical X, Y, Width and Height.</param>
/// <param name="Parent">Parent region for nested clipping, or <see cref="UiClipId.None"/>.</param>
public readonly record struct UiClipRegion(float4 Bounds, UiClipId Parent);

/// <summary>Stable semantic identity of a custom visual, not a pipeline or shader handle.</summary>
public readonly record struct UiVisualTypeId(Guid Value)
{
    public bool IsValid => Value != Guid.Empty;
}

public enum UiVisualKind : byte
{
    SolidRectangle,
    RoundedRectangle,
    Border,
    Image,
    Custom,
}

/// <param name="Bounds">Logical X, Y, Width and Height.</param>
/// <param name="Color">Linear RGBA tint.</param>
/// <param name="VisualType">Semantic custom-visual identity; empty for built-in kinds.</param>
/// <param name="Resource">Stable image or visual resource identity; empty when unused.</param>
public readonly record struct UiVisualDraw(
    UiVisualKind Kind,
    UiVisualTypeId VisualType,
    float4 Bounds,
    float4 Color,
    UiClipId Clip,
    UiResourceId Resource);

/// <summary>Positioned shaped text plus UI-only paint and clipping data.</summary>
public readonly record struct UiTextDraw(
    ShapedText Text,
    float2 BaselineOrigin,
    float4 Color,
    UiClipId Clip);

/// <summary>
/// Borrowed renderer-neutral output of one retained UI document. The spans are valid only
/// until the producer next mutates or rebuilds its display list.
/// </summary>
public readonly ref struct UiDisplayList
{
    public UiDisplayList(
        ReadOnlySpan<UiVisualDraw> visuals,
        ReadOnlySpan<UiClipRegion> clips,
        ReadOnlySpan<UiTextDraw> text)
    {
        Visuals = visuals;
        Clips = clips;
        Text = text;
    }

    public ReadOnlySpan<UiVisualDraw> Visuals { get; }

    public ReadOnlySpan<UiClipRegion> Clips { get; }

    public ReadOnlySpan<UiTextDraw> Text { get; }
}
