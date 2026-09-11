using Delta.XAML.Contract;

namespace Delta.XAML;

public enum UiBrushKind
{
    None,
    Solid,
    LinearGradient,
    RadialGradient,
    Image,
    Unknown,
}

/// <summary>One normalized stop in a renderer-neutral gradient resource.</summary>
public readonly record struct UiGradientStop(float Offset, UiColor Color);

/// <summary>Renderer-neutral linear-gradient resource data stored in a resource catalog.</summary>
public readonly record struct UiLinearGradient
{
    public UiLinearGradient(float startX, float startY, float endX, float endY, ReadOnlyMemory<UiGradientStop> stops)
    {
        if (!float.IsFinite(startX) || !float.IsFinite(startY) || !float.IsFinite(endX) || !float.IsFinite(endY))
        {
            throw new ArgumentOutOfRangeException(nameof(startX), "Gradient coordinates must be finite.");
        }

        StartX = startX;
        StartY = startY;
        EndX = endX;
        EndY = endY;
        Stops = CopyStops(stops);
    }

    public float StartX { get; }

    public float StartY { get; }

    public float EndX { get; }

    public float EndY { get; }

    public ReadOnlyMemory<UiGradientStop> Stops { get; }

    /// <summary>Gets whether the gradient line is resolved against each visual's arranged bounds.</summary>
    public bool IsRelativeToBounds { get; init; }

    /// <summary>Gets the CSS-compatible clockwise angle used for relative gradients.</summary>
    public float AngleDegrees { get; init; }

    /// <summary>Gets the optional solid outline color drawn around the gradient bounds.</summary>
    public UiColor OutlineColor { get; init; }

    /// <summary>Gets the optional solid outline width in the gradient's logical units.</summary>
    public float OutlineWidth { get; init; }

    /// <summary>Creates a CSS-style linear gradient whose line follows each visual's bounds.</summary>
    public static UiLinearGradient Relative(float angleDegrees, ReadOnlyMemory<UiGradientStop> stops)
    {
        if (!float.IsFinite(angleDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(angleDegrees));
        }

        return new UiLinearGradient(0, 0, 0, 0, stops)
        {
            IsRelativeToBounds = true,
            AngleDegrees = angleDegrees,
        };
    }

    /// <summary>Returns this gradient with a solid outline around its arranged bounds.</summary>
    public UiLinearGradient WithOutline(UiColor color, float width = 1)
    {
        if (!float.IsFinite(width) || width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Gradient outline width must be finite and non-negative.");
        }

        return this with
        {
            OutlineColor = color,
            OutlineWidth = width,
        };
    }

    private static ReadOnlyMemory<UiGradientStop> CopyStops(ReadOnlyMemory<UiGradientStop> stops)
    {
        if (stops.Length < 2)
        {
            throw new ArgumentException("A gradient requires at least two stops.", nameof(stops));
        }

        var copy = stops.ToArray();
        var previous = -1f;
        for (var i = 0; i < copy.Length; i++)
        {
            var offset = copy[i].Offset;
            if (!float.IsFinite(offset) || offset < 0 || offset > 1 || offset < previous)
            {
                throw new ArgumentException("Gradient stop offsets must be finite, normalized and ordered.", nameof(stops));
            }

            previous = offset;
        }

        return copy;
    }
}

/// <summary>Renderer-neutral radial-gradient resource data stored in a resource catalog.</summary>
public readonly record struct UiRadialGradient
{
    public UiRadialGradient(float centerX, float centerY, float radius, ReadOnlyMemory<UiGradientStop> stops)
    {
        if (!float.IsFinite(centerX) || !float.IsFinite(centerY) || !float.IsFinite(radius) || radius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Gradient center coordinates must be finite and radius must be positive.");
        }

        CenterX = centerX;
        CenterY = centerY;
        Radius = radius;
        Stops = CopyStops(stops);
    }

    public float CenterX { get; }

    public float CenterY { get; }

    public float Radius { get; }

    public ReadOnlyMemory<UiGradientStop> Stops { get; }

    private static ReadOnlyMemory<UiGradientStop> CopyStops(ReadOnlyMemory<UiGradientStop> stops)
    {
        if (stops.Length < 2)
        {
            throw new ArgumentException("A gradient requires at least two stops.", nameof(stops));
        }

        var copy = stops.ToArray();
        var previous = -1f;
        for (var i = 0; i < copy.Length; i++)
        {
            var offset = copy[i].Offset;
            if (!float.IsFinite(offset) || offset < 0 || offset > 1 || offset < previous)
            {
                throw new ArgumentException("Gradient stop offsets must be finite, normalized and ordered.", nameof(stops));
            }

            previous = offset;
        }

        return copy;
    }
}

/// <summary>Compact paint value; non-solid paint is identified by a renderer-neutral resource.</summary>
public readonly record struct UiBrush(UiBrushKind Kind, UiColor Color, UiResourceId Resource)
{
    public static UiBrush None => default;

    public static UiBrush Solid(UiColor color) => new(UiBrushKind.Solid, color, UiResourceId.Empty);

    public static UiBrush LinearGradient(UiResourceId resource, UiColor tint = default) =>
        ResourceBacked(UiBrushKind.LinearGradient, resource, tint);

    public static UiBrush RadialGradient(UiResourceId resource, UiColor tint = default) =>
        ResourceBacked(UiBrushKind.RadialGradient, resource, tint);

    public static UiBrush Image(UiResourceId resource, UiColor tint = default) =>
        ResourceBacked(UiBrushKind.Image, resource, tint);

    private static UiBrush ResourceBacked(UiBrushKind kind, UiResourceId resource, UiColor tint)
    {
        if (!resource.IsValid)
        {
            throw new ArgumentException("A resource-backed brush requires a stable resource identity.", nameof(resource));
        }

        return new(kind, tint == default ? new UiColor(255, 255, 255) : tint, resource);
    }
}

/// <summary>Stable semantic visual identities understood by renderer adapters.</summary>
public static class UiKnownVisuals
{
    public static UiVisualTypeId LinearGradient { get; } = new(new Guid("3419D85F-C401-4DD8-86DD-D2A68359D301"));

    public static UiVisualTypeId RadialGradient { get; } = new(new Guid("3419D85F-C401-4DD8-86DD-D2A68359D302"));
}
