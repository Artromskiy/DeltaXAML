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

/// <summary>Payload kind referenced by a <see cref="UiDrawRef"/>.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1028:Enum Storage should be Int32",
    Justification = "Display-list references use a compact byte kind at the renderer-neutral boundary.")]
public enum UiDrawKind : byte
{
    Unknown = 0,
    Visual = 1,
    Text = 2,
}

/// <summary>Compact reference to one payload in a <see cref="UiDisplayList"/>.</summary>
/// <param name="Kind">Payload span selected by <see cref="Kind"/>.</param>
/// <param name="Index">Zero-based index into the selected payload span.</param>
public readonly record struct UiDrawRef(UiDrawKind Kind, int Index)
{
    /// <summary>
    /// Gets whether the fields form a supported non-negative reference. The selected display
    /// list still owns range validation because only it knows the payload span length.
    /// </summary>
    public bool IsValid => (Kind is UiDrawKind.Visual or UiDrawKind.Text) && Index >= 0;
}

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
    /// <summary>Creates a borrowed display list with its canonical mixed payload order.</summary>
    public UiDisplayList(
        ReadOnlySpan<UiVisualDraw> visuals,
        ReadOnlySpan<UiClipRegion> clips,
        ReadOnlySpan<UiTextDraw> text,
        ReadOnlySpan<UiDrawRef> order)
    {
        Visuals = visuals;
        Clips = clips;
        Text = text;
        Order = order;
    }

    /// <summary>Gets renderer-neutral visual payloads indexed by visual draw references.</summary>
    public ReadOnlySpan<UiVisualDraw> Visuals { get; }

    /// <summary>Gets nested clip regions referenced by visual and text payloads.</summary>
    public ReadOnlySpan<UiClipRegion> Clips { get; }

    /// <summary>Gets renderer-neutral shaped text payloads indexed by text draw references.</summary>
    public ReadOnlySpan<UiTextDraw> Text { get; }

    /// <summary>
    /// Gets the canonical draw sequence. Each entry selects one item from <see cref="Visuals"/>
    /// or <see cref="Text"/>; the span is borrowed with the rest of this display list.
    /// </summary>
    public ReadOnlySpan<UiDrawRef> Order { get; }
}
