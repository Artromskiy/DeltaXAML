using Delta;
using Delta.XAML.Contract;

namespace Delta.XAML;

/// <summary>One typed style/action range in a retained paragraph.</summary>
public readonly record struct UiTextSpan(
    string Text,
    string FontKey,
    float FontSize,
    UiColor Color,
    UiCommandId Link = default,
    string? LinkArgument = null,
    UiTextDecorations Decorations = UiTextDecorations.None)
{
    public UiTextSpan(string text, UiColor color)
        : this(text, "default", 14, color)
    {
    }
}

/// <summary>Retained inline action bounds expressed in logical document coordinates.</summary>
public readonly record struct UiInlineHitRange(float4 Bounds, UiCommandId Command, string? Argument);
