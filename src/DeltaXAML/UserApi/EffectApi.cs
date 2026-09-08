using Delta;
using Delta.XAML.Contract;

namespace Delta.XAML;

/// <summary>Convenient authoring value for a visual stroke.</summary>
public readonly record struct UiStrokeEffect(UiColor Color, float Width);

/// <summary>Convenient authoring value for an outer or inner shadow.</summary>
public readonly record struct UiShadowEffect(
    UiColor Color,
    float Radius,
    float2 Offset = default,
    float Spread = 0,
    float Intensity = 1);

/// <summary>Convenient authoring value for an outer or inner glow.</summary>
public readonly record struct UiGlowEffect(
    UiColor Color,
    float Radius,
    float Spread = 0,
    float Intensity = 1,
    float2 Offset = default);

/// <summary>Cold authoring description for one visual effect set.</summary>
public sealed class UiVisualEffects
{
    public UiStrokeEffect? Stroke { get; init; }

    public UiShadowEffect? OuterShadow { get; init; }

    public UiShadowEffect? InnerShadow { get; init; }

    public UiGlowEffect? OuterGlow { get; init; }

    public UiGlowEffect? InnerGlow { get; init; }

    public PaintUnits Units { get; init; } = PaintUnits.Logical;

    public UiEffectQuality Quality { get; init; } = UiEffectQuality.Analytic;

    public UiResourceId CachedMask { get; init; }

    /// <summary>
    /// Optional logical paint outsets. Logical effects derive this automatically;
    /// outward device-unit effects require an explicit value because DPI is not
    /// available while the immutable resource is prepared.
    /// </summary>
    public UiThickness? Outsets { get; init; }
}

/// <summary>Cold authoring description for one text effect set.</summary>
public sealed class UiTextEffects
{
    public UiStrokeEffect? Stroke { get; init; }

    public UiShadowEffect? OuterShadow { get; init; }

    public UiShadowEffect? InnerShadow { get; init; }

    public UiGlowEffect? OuterGlow { get; init; }

    public UiGlowEffect? InnerGlow { get; init; }

    public PaintUnits Units { get; init; } = PaintUnits.Logical;

    public UiEffectQuality Quality { get; init; } = UiEffectQuality.Analytic;

    public UiResourceId CachedMask { get; init; }

    /// <inheritdoc cref="UiVisualEffects.Outsets"/>
    public UiThickness? Outsets { get; init; }
}

/// <summary>Creates canonical immutable effect resources from concise authoring values.</summary>
public static class UiEffects
{
    public static UiEffectResource Create(UiResourceId resource, UiVisualEffects effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return CreateVisual(
            resource,
            Capabilities(
                effects.Stroke.HasValue,
                effects.OuterShadow.HasValue,
                effects.InnerShadow.HasValue,
                effects.OuterGlow.HasValue,
                effects.InnerGlow.HasValue),
            effects.Stroke is { } stroke ? Layer(stroke.Color, width: stroke.Width) : default,
            effects.OuterShadow is { } outer ? Layer(outer) : default,
            effects.InnerShadow is { } innerShadow ? Layer(innerShadow) : default,
            effects.OuterGlow is { } outerGlow ? Layer(outerGlow) : default,
            effects.InnerGlow is { } innerGlow ? Layer(innerGlow) : default,
            effects.Units,
            effects.Quality,
            effects.CachedMask,
            ToVector(effects.Outsets));
    }

    public static UiEffectResource Create(UiResourceId resource, UiTextEffects effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return CreateText(
            resource,
            Capabilities(
                effects.Stroke.HasValue,
                effects.OuterShadow.HasValue,
                effects.InnerShadow.HasValue,
                effects.OuterGlow.HasValue,
                effects.InnerGlow.HasValue),
            effects.Stroke is { } stroke ? Layer(stroke.Color, width: stroke.Width) : default,
            effects.OuterShadow is { } outer ? Layer(outer) : default,
            effects.InnerShadow is { } innerShadow ? Layer(innerShadow) : default,
            effects.OuterGlow is { } outerGlow ? Layer(outerGlow) : default,
            effects.InnerGlow is { } innerGlow ? Layer(innerGlow) : default,
            effects.Units,
            effects.Quality,
            effects.CachedMask,
            ToVector(effects.Outsets));
    }

    /// <summary>
    /// Creates a canonical visual resource. Generated DXAML companions call this
    /// overload directly so changing a binding does not allocate an authoring object.
    /// </summary>
    public static UiEffectResource CreateVisual(
        UiResourceId resource,
        UiEffectCapabilities capabilities,
        UiEffectLayer stroke = default,
        UiEffectLayer outerShadow = default,
        UiEffectLayer innerShadow = default,
        UiEffectLayer outerGlow = default,
        UiEffectLayer innerGlow = default,
        PaintUnits units = PaintUnits.Logical,
        UiEffectQuality quality = UiEffectQuality.Analytic,
        UiResourceId cachedMask = default,
        float4? outsets = null) =>
        CreateCore(
            resource,
            UiEffectTarget.Visual,
            capabilities,
            stroke,
            outerShadow,
            innerShadow,
            outerGlow,
            innerGlow,
            units,
            quality,
            cachedMask,
            outsets);

    /// <summary>
    /// Creates a canonical text resource. Generated DXAML companions call this
    /// overload directly so changing a binding does not allocate an authoring object.
    /// </summary>
    public static UiEffectResource CreateText(
        UiResourceId resource,
        UiEffectCapabilities capabilities,
        UiEffectLayer stroke = default,
        UiEffectLayer outerShadow = default,
        UiEffectLayer innerShadow = default,
        UiEffectLayer outerGlow = default,
        UiEffectLayer innerGlow = default,
        PaintUnits units = PaintUnits.Logical,
        UiEffectQuality quality = UiEffectQuality.Analytic,
        UiResourceId cachedMask = default,
        float4? outsets = null) =>
        CreateCore(
            resource,
            UiEffectTarget.Text,
            capabilities,
            stroke,
            outerShadow,
            innerShadow,
            outerGlow,
            innerGlow,
            units,
            quality,
            cachedMask,
            outsets);

    /// <summary>Converts an authoring color to the renderer-neutral normalized representation.</summary>
    public static float4 Color(UiColor color) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        color.A / 255f);

    private static UiEffectResource CreateCore(
        UiResourceId resource,
        UiEffectTarget target,
        UiEffectCapabilities capabilities,
        UiEffectLayer stroke,
        UiEffectLayer outerShadow,
        UiEffectLayer innerShadow,
        UiEffectLayer outerGlow,
        UiEffectLayer innerGlow,
        PaintUnits units,
        UiEffectQuality quality,
        UiResourceId cachedMask,
        float4? declaredOutsets)
    {
        if (!resource.IsValid)
        {
            throw new ArgumentException("A stable effect resource identity is required.", nameof(resource));
        }

        var parameters = new UiEffectParameters(
            stroke,
            outerShadow,
            innerShadow,
            outerGlow,
            innerGlow,
            cachedMask)
        {
            Units = units,
        };
        var outsets = declaredOutsets ?? DeriveOutsets(target, parameters);
        var result = new UiEffectResource(
            new UiEffectSet(resource, target, capabilities, quality, outsets),
            parameters);
        if (!result.IsValid)
        {
            throw new ArgumentException("Effect layers must be finite, non-negative and valid for the selected target and quality.");
        }

        return result;
    }

    private static UiEffectCapabilities Capabilities(
        bool stroke,
        bool outerShadow,
        bool innerShadow,
        bool outerGlow,
        bool innerGlow)
    {
        var result = UiEffectCapabilities.None;
        if (stroke)
        {
            result |= UiEffectCapabilities.Stroke;
        }

        if (outerShadow)
        {
            result |= UiEffectCapabilities.OuterShadow;
        }

        if (innerShadow)
        {
            result |= UiEffectCapabilities.InnerShadow;
        }

        if (outerGlow)
        {
            result |= UiEffectCapabilities.OuterGlow;
        }

        if (innerGlow)
        {
            result |= UiEffectCapabilities.InnerGlow;
        }

        return result;
    }

    private static float4 DeriveOutsets(UiEffectTarget target, UiEffectParameters parameters)
    {
        var hasOutwardPaint = !parameters.OuterShadow.IsEmpty || !parameters.OuterGlow.IsEmpty ||
            (target == UiEffectTarget.Text && !parameters.Stroke.IsEmpty);
        if (parameters.Units == PaintUnits.Device && hasOutwardPaint)
        {
            throw new ArgumentException("Outward device-unit effects require explicit logical Outsets because preparation has no DPI context.");
        }

        var result = default(float4);
        if (target == UiEffectTarget.Text && !parameters.Stroke.IsEmpty)
        {
            Expand(ref result, parameters.Stroke.Width, default);
        }

        if (!parameters.OuterShadow.IsEmpty)
        {
            Expand(ref result, parameters.OuterShadow.BlurRadius + parameters.OuterShadow.Spread, parameters.OuterShadow.Offset);
        }

        if (!parameters.OuterGlow.IsEmpty)
        {
            Expand(ref result, parameters.OuterGlow.BlurRadius + parameters.OuterGlow.Spread, parameters.OuterGlow.Offset);
        }

        return result;
    }

    private static void Expand(ref float4 value, float extent, float2 offset)
    {
        value.x = Maths.Max(value.x, extent - offset.x);
        value.y = Maths.Max(value.y, extent - offset.y);
        value.z = Maths.Max(value.z, extent + offset.x);
        value.w = Maths.Max(value.w, extent + offset.y);
    }

    private static UiEffectLayer Layer(UiColor color, float width) =>
        new(Color(color), default, width, 0, 0, 1);

    private static UiEffectLayer Layer(UiShadowEffect effect) =>
        new(Color(effect.Color), effect.Offset, 0, effect.Radius, effect.Spread, effect.Intensity);

    private static UiEffectLayer Layer(UiGlowEffect effect) =>
        new(Color(effect.Color), effect.Offset, 0, effect.Radius, effect.Spread, effect.Intensity);

    private static float4? ToVector(UiThickness? value) => value is { } outsets
        ? new(outsets.Left, outsets.Top, outsets.Right, outsets.Bottom)
        : null;
}
