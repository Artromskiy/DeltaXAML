using Delta;
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

/// <summary>Shape used to clip a renderer-neutral region.</summary>
public enum UiClipKind : byte
{
    None = 0,
    Unknown = 1,
    Rectangle = 2,
    RoundedRectangle = 3,
}

/// <summary>Renderer-neutral clipping region in logical coordinates.</summary>
public readonly record struct UiClipRegion
{
    /// <summary>Creates a rectangular region with an optional parent region.</summary>
    public UiClipRegion(float4 bounds, UiClipId parent)
        : this(bounds, parent, UiClipKind.Rectangle, default)
    {
    }

    /// <summary>Creates a region with an explicit shape and per-corner radii.</summary>
    public UiClipRegion(float4 bounds, UiClipId parent, UiClipKind kind, float4 cornerRadii)
    {
        Bounds = bounds;
        Parent = parent;
        Kind = kind;
        CornerRadii = cornerRadii;
    }

    /// <summary>Logical X, Y, Width and Height.</summary>
    public float4 Bounds { get; init; }

    /// <summary>Parent region for nested clipping, or <see cref="UiClipId.None"/>.</summary>
    public UiClipId Parent { get; init; }

    /// <summary>Gets the semantic clip shape selected by the producer.</summary>
    public UiClipKind Kind { get; init; }

    /// <summary>Per-corner radii in logical units, ordered top-left, top-right, bottom-right, bottom-left.</summary>
    public float4 CornerRadii { get; init; }
}

/// <summary>Payload kind referenced by a <see cref="UiDrawRef"/>.</summary>
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

/// <summary>Unit system for renderer paint metrics such as thickness and effect distances.</summary>
public enum PaintUnits : byte
{
    /// <summary>The metric is expressed in logical UI units and is scaled by the frame DPI.</summary>
    Logical,

    /// <summary>The metric is expressed in physical device pixels and is not DPI-scaled.</summary>
    Device,

    /// <summary>The metric is expressed as a fraction of the arranged bounds.</summary>
    Percent,
}

/// <summary>Renderer-neutral compositing mode selected for one paint payload.</summary>
public enum UiBlendMode : byte
{
    Unknown = 0,
    Opaque = 1,
    Alpha = 2,
    PremultipliedAlpha = 3,
    Additive = 4,
    Multiply = 5,
}

/// <summary>Fixed-size renderer-neutral paint data for a visual primitive.</summary>
/// <param name="FillColor">Linear RGBA fill or tint.</param>
/// <param name="CornerRadii">Per-corner radii in logical units.</param>
/// <param name="EffectSet">Prepared immutable set of visual paint effects, if any.</param>
/// <param name="BlendMode">Renderer-neutral compositing mode.</param>
public readonly record struct UiVisualPaint(
    float4 FillColor,
    float4 CornerRadii,
    UiEffectSet EffectSet,
    UiBlendMode BlendMode = UiBlendMode.PremultipliedAlpha)
{
    /// <summary>Creates a fill-only paint.</summary>
    public static UiVisualPaint Solid(float4 color) => new(color, default, UiEffectSet.None);
}

/// <summary>Renderer-neutral visual command with optional fixed paint parameters.</summary>
public readonly record struct UiVisualDraw
{
    /// <summary>Creates a visual using a fill-only paint.</summary>
    public UiVisualDraw(
        UiVisualKind kind,
        UiVisualTypeId visualType,
        float4 bounds,
        float4 color,
        UiClipId clip,
        UiResourceId resource)
        : this(kind, visualType, bounds, UiVisualPaint.Solid(color), clip, resource)
    {
    }

    /// <summary>Creates a visual with explicit fill, corner-radius and effect data.</summary>
    private UiVisualDraw(
        UiVisualKind kind,
        UiVisualTypeId visualType,
        float4 bounds,
        UiVisualPaint paint,
        UiClipId clip,
        UiResourceId resource)
    {
        Kind = kind;
        VisualType = visualType;
        Bounds = bounds;
        Paint = paint;
        Clip = clip;
        Resource = resource;
    }

    /// <summary>Creates a visual with explicit fill, corner-radius and effect data.</summary>
    public static UiVisualDraw WithPaint(
        UiVisualKind kind,
        UiVisualTypeId visualType,
        float4 bounds,
        UiVisualPaint paint,
        UiClipId clip,
        UiResourceId resource) =>
        new()
        {
            Kind = kind,
            VisualType = visualType,
            Bounds = bounds,
            Paint = paint,
            Clip = clip,
            Resource = resource,
        };

    /// <summary>Gets the primitive kind.</summary>
    public UiVisualKind Kind { get; init; }

    /// <summary>Gets the semantic custom-visual identity; empty for built-in kinds.</summary>
    public UiVisualTypeId VisualType { get; init; }

    /// <summary>Gets logical X, Y, Width and Height.</summary>
    public float4 Bounds { get; init; }

    /// <summary>Gets fixed-size fill, corner-radius and effect data.</summary>
    public UiVisualPaint Paint { get; init; }

    /// <summary>Gets or initializes the fill color for the simple visual path.</summary>
    public float4 Color
    {
        get => Paint.FillColor;
        init => Paint = Paint with { FillColor = value };
    }

    /// <summary>Gets the clip reference.</summary>
    public UiClipId Clip { get; init; }

    /// <summary>Gets the stable image, brush or custom-visual resource identity.</summary>
    public UiResourceId Resource { get; init; }
}

/// <summary>Fixed-size renderer-neutral paint data for shaped text.</summary>
/// <param name="FillColor">Linear RGBA glyph fill color.</param>
/// <param name="EffectSet">Prepared immutable set of text paint effects, if any.</param>
/// <param name="BlendMode">Renderer-neutral compositing mode.</param>
public readonly record struct UiTextPaint(
    float4 FillColor,
    UiEffectSet EffectSet,
    UiBlendMode BlendMode = UiBlendMode.PremultipliedAlpha)
{
    /// <summary>Creates fill-only text paint.</summary>
    public static UiTextPaint Solid(float4 color) => new(color, UiEffectSet.None);
}

/// <summary>Stable identity and dirty version of one ordered retained UI item.</summary>
/// <remarks>
/// <see cref="Value"/> identifies the producer's retained slot,
/// <see cref="Generation"/> distinguishes a later occupant of that slot, and
/// <see cref="Version"/> identifies the current producer payload version. The identity is
/// aligned with <see cref="UiDisplayList.Order"/> rather than a visual or text payload array.
/// </remarks>
public readonly record struct UiElementIdentity(
    uint Value,
    uint Generation,
    uint Version);

/// <summary>Positioned shaped text plus UI-only paint and clipping data.</summary>
public readonly record struct UiTextDraw
{
    /// <summary>Creates a text draw using fill-only paint.</summary>
    public UiTextDraw(ShapedText text, float2 baselineOrigin, float4 color, UiClipId clip)
        : this(text, baselineOrigin, UiTextPaint.Solid(color), clip)
    {
    }

    /// <summary>Creates a text draw with explicit fill and effect-set data.</summary>
    public static UiTextDraw WithPaint(
        ShapedText text,
        float2 baselineOrigin,
        UiTextPaint paint,
        UiClipId clip) =>
        new(text, baselineOrigin, paint, clip);

    private UiTextDraw(
        ShapedText text,
        float2 baselineOrigin,
        UiTextPaint paint,
        UiClipId clip)
    {
        Text = text;
        BaselineOrigin = baselineOrigin;
        Paint = paint;
        Clip = clip;
    }

    /// <summary>Gets the already shaped text value.</summary>
    public ShapedText Text { get; init; }

    /// <summary>Gets the baseline origin in logical coordinates.</summary>
    public float2 BaselineOrigin { get; init; }

    /// <summary>Gets the fixed-size fill and canonical effect-set reference.</summary>
    public UiTextPaint Paint { get; init; }

    /// <summary>Gets or initializes the fill color for the simple text path.</summary>
    public float4 Color
    {
        get => Paint.FillColor;
        init => Paint = Paint with { FillColor = value };
    }

    /// <summary>Gets the clip reference.</summary>
    public UiClipId Clip { get; init; }
}

/// <summary>
/// Borrowed renderer-neutral output of one retained UI document. The spans are valid only
/// until the producer next mutates or rebuilds its display list.
/// </summary>
public readonly ref struct UiDisplayList
{
    /// <summary>Creates a borrowed display list with its canonical mixed payload order and identities.</summary>
    /// <remarks><paramref name="identities"/> must contain exactly one identity for every entry in <paramref name="order"/>.</remarks>
    public UiDisplayList(
        ReadOnlySpan<UiVisualDraw> visuals,
        ReadOnlySpan<UiClipRegion> clips,
        ReadOnlySpan<UiTextDraw> text,
        ReadOnlySpan<UiDrawRef> order,
        ReadOnlySpan<UiElementIdentity> identities,
        float dpiScale = 1f)
    {
        if (identities.Length != order.Length)
        {
            throw new ArgumentException("Identities length must equal Order length.", nameof(identities));
        }

        if (!float.IsFinite(dpiScale) || dpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale), "DPI scale must be finite and positive.");
        }

        Visuals = visuals;
        Clips = clips;
        Text = text;
        Order = order;
        Identities = identities;
        DpiScale = dpiScale;
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

    /// <summary>
    /// Gets the retained identity and version aligned with <see cref="Order"/>. Entry <c>i</c>
    /// describes the ordered payload selected by entry <c>i</c> in <see cref="Order"/>.
    /// </summary>
    public ReadOnlySpan<UiElementIdentity> Identities { get; }

    /// <summary>Gets the logical-to-device scale for paint metrics in this frame.</summary>
    public float DpiScale { get; }
}
