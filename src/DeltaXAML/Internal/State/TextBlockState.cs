using Delta.XAML;

namespace DeltaXAML.Internal;

internal struct TextBlockLayoutState
{
    public UiSize DesiredSize;
    public UiRect Bounds;
    public UiRect Clip;
    public UiRect TextBounds;
    public UiTextHorizontalAlignment HorizontalAlignment;
    public UiTextVerticalAlignment VerticalAlignment;
    public UiTextWrapping Wrapping;
    public UiTextTrimming Trimming;
    public int MaxLines;
    public float LineHeight;
}

internal struct TextBlockVisualState
{
    public string FontKey;
    public string GlyphRunKey;
    public float FontSize;
    public UiColor Foreground;
    public UiColor OutlineColor;
    public float OutlineWidth;
    public Guid TextEffectResource;
    public UiFontWeight Weight;
    public UiFontStyle Style;
    public UiTextDecorations Decorations;
}

internal struct TextBlockState
{
    public TextBlockLayoutState Layout;
    public TextBlockVisualState Visual;
    public string Text;
}
