using Delta;

namespace Delta.XAML.Contract;

/// <summary>Semantic target of an immutable UI effect set.</summary>
public enum UiEffectTarget : byte
{
    None,
    Visual,
    Text,
}

/// <summary>Effect capabilities represented by one prepared effect resource.</summary>
[Flags]
public enum UiEffectCapabilities : byte
{
    None = 0,
    Stroke = 1 << 0,
    Outline = 1 << 1,
    OuterShadow = 1 << 2,
    InsetShadow = 1 << 3,
    Glow = 1 << 4,
}

/// <summary>Quality tier selected when an effect resource is prepared.</summary>
public enum UiEffectQuality : byte
{
    Analytic,
    CachedMask,
}

/// <summary>Typed parameters for one semantic effect layer.</summary>
public readonly record struct UiEffectLayer(
    float4 Color,
    float2 Offset,
    float Width,
    float BlurRadius,
    float Spread,
    float Intensity)
{
    /// <summary>Gets whether this layer contains no paint data.</summary>
    public bool IsEmpty => Color == default && Offset == default &&
        Width == 0 && BlurRadius == 0 && Spread == 0 && Intensity == 0;

    internal bool IsFiniteNonNegative()
        => IsFinite(Color) && IsFinite(Offset) &&
            IsFiniteNonNegative(Width) && IsFiniteNonNegative(BlurRadius) &&
            IsFiniteNonNegative(Spread) && IsFiniteNonNegative(Intensity);

    private static bool IsFinite(float2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);

    private static bool IsFinite(float4 value) => float.IsFinite(value.x) && float.IsFinite(value.y) &&
        float.IsFinite(value.z) && float.IsFinite(value.w);

    private static bool IsFiniteNonNegative(float value) => float.IsFinite(value) && value >= 0;
}

/// <summary>
/// Fixed typed payload for one immutable effect resource. The named layers are
/// selected by <see cref="UiEffectSet.Capabilities"/> and are not shader ABI types.
/// </summary>
public readonly record struct UiEffectParameters(
    UiEffectLayer StrokeOrOutline,
    UiEffectLayer OuterShadow,
    UiEffectLayer InsetShadow,
    UiEffectLayer Glow,
    UiResourceId CachedMask)
{
    /// <summary>Gets an empty parameter payload.</summary>
    public static UiEffectParameters Empty => default;

    /// <summary>Gets the unit system used by all distance values in this payload.</summary>
    /// <remarks>Logical values are converted once by the renderer adapter; device values are already physical pixels.</remarks>
    public PaintUnits Units { get; init; } = PaintUnits.Logical;

    internal bool IsValidFor(UiEffectSet effectSet)
    {
        if (!StrokeOrOutline.IsFiniteNonNegative() || !OuterShadow.IsFiniteNonNegative() ||
            !InsetShadow.IsFiniteNonNegative() || !Glow.IsFiniteNonNegative() ||
            Units is not (PaintUnits.Logical or PaintUnits.Device))
        {
            return false;
        }

        if (effectSet.Quality == UiEffectQuality.CachedMask && !CachedMask.IsValid)
        {
            return false;
        }

        if (effectSet.Quality == UiEffectQuality.Analytic && CachedMask.IsValid)
        {
            return false;
        }

        return Matches(UiEffectCapabilities.Stroke | UiEffectCapabilities.Outline, StrokeOrOutline, effectSet) &&
            Matches(UiEffectCapabilities.OuterShadow, OuterShadow, effectSet) &&
            Matches(UiEffectCapabilities.InsetShadow, InsetShadow, effectSet) &&
            Matches(UiEffectCapabilities.Glow, Glow, effectSet);
    }

    private static bool Matches(UiEffectCapabilities capabilities, UiEffectLayer layer, UiEffectSet effectSet)
    {
        var selected = (effectSet.Capabilities & capabilities) != UiEffectCapabilities.None;
        return selected ? !layer.IsEmpty : layer.IsEmpty;
    }
}

/// <summary>Immutable typed effect resource resolved from a stable resource identity.</summary>
public readonly record struct UiEffectResource(
    UiEffectSet Set,
    UiEffectParameters Parameters)
{
    /// <summary>Gets whether metadata and typed parameters are internally consistent.</summary>
    public bool IsValid => Set.IsValid && Parameters.IsValidFor(Set);

    /// <summary>Creates a visual analytic stroke resource from XAML paint sugar.</summary>
    public static UiEffectResource CreateVisualStroke(
        UiResourceId resource,
        float4 color,
        float width,
        PaintUnits units = PaintUnits.Logical)
    {
        var set = new UiEffectSet(
            resource,
            UiEffectTarget.Visual,
            UiEffectCapabilities.Stroke,
            UiEffectQuality.Analytic,
            default);
        return new(
            set,
            new UiEffectParameters(
                new UiEffectLayer(color, default, width, 0, 0, 1),
                default,
                default,
                default,
                default)
            {
                Units = units,
            });
    }

    /// <summary>Creates a text analytic outline resource from XAML paint sugar.</summary>
    public static UiEffectResource CreateTextOutline(
        UiResourceId resource,
        float4 color,
        float width,
        PaintUnits units = PaintUnits.Logical)
    {
        var set = new UiEffectSet(
            resource,
            UiEffectTarget.Text,
            UiEffectCapabilities.Outline,
            UiEffectQuality.Analytic,
            default);
        return new(
            set,
            new UiEffectParameters(
                new UiEffectLayer(color, default, width, 0, 0, 1),
                default,
                default,
                default,
                default)
            {
                Units = units,
            });
    }
}

/// <summary>
/// Renderer-neutral reference to one immutable set of paint effects.
/// </summary>
/// <remarks>
/// The resource identity resolves typed effect parameters owned by the renderer-side
/// preparation path. <see cref="Outsets"/> is left, top, right and bottom paint extent
/// in the same units as the owning draw. It affects damage and paint bounds, never layout.
/// </remarks>
public readonly record struct UiEffectSet(
    UiResourceId Resource,
    UiEffectTarget Target,
    UiEffectCapabilities Capabilities,
    UiEffectQuality Quality,
    float4 Outsets)
{
    private const UiEffectCapabilities KnownCapabilities =
        UiEffectCapabilities.Stroke | UiEffectCapabilities.Outline |
        UiEffectCapabilities.OuterShadow | UiEffectCapabilities.InsetShadow |
        UiEffectCapabilities.Glow;

    /// <summary>Gets an empty effect set.</summary>
    public static UiEffectSet None => default;

    /// <summary>Gets whether this set references a usable effect resource.</summary>
    public bool IsValid
        => Resource.IsValid && (Target is UiEffectTarget.Visual or UiEffectTarget.Text) &&
            Capabilities != UiEffectCapabilities.None &&
            (Capabilities & ~KnownCapabilities) == UiEffectCapabilities.None &&
            (Capabilities & ~AllowedCapabilities(Target)) == UiEffectCapabilities.None &&
            (Quality is UiEffectQuality.Analytic or UiEffectQuality.CachedMask) &&
            IsFiniteNonNegative(Outsets);

    /// <summary>Gets whether the set contains every requested capability.</summary>
    public bool Has(UiEffectCapabilities capabilities)
        => (Capabilities & capabilities) == capabilities;

    private static bool IsFiniteNonNegative(float4 value)
        => IsFiniteNonNegative(value.x) && IsFiniteNonNegative(value.y) &&
            IsFiniteNonNegative(value.z) && IsFiniteNonNegative(value.w);

    private static UiEffectCapabilities AllowedCapabilities(UiEffectTarget target) => target switch
    {
        UiEffectTarget.Visual => UiEffectCapabilities.Stroke | UiEffectCapabilities.OuterShadow |
            UiEffectCapabilities.InsetShadow | UiEffectCapabilities.Glow,
        UiEffectTarget.Text => UiEffectCapabilities.Outline | UiEffectCapabilities.OuterShadow |
            UiEffectCapabilities.Glow,
        _ => UiEffectCapabilities.None,
    };

    private static bool IsFiniteNonNegative(float value)
        => float.IsFinite(value) && value >= 0;
}
