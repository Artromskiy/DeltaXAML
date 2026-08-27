namespace DeltaXAML.Internal;

internal struct TextBlockLayoutState
{
    public UiSize DesiredSize;
    public UiRect Bounds;
    public UiRect Clip;
}

internal struct TextBlockVisualState
{
    public string FontKey;
    public string GlyphRunKey;
    public float FontSize;
    public UiColor Foreground;
}

internal struct TextBlockState
{
    public TextBlockLayoutState Layout;
    public TextBlockVisualState Visual;
    public string Text;
}
