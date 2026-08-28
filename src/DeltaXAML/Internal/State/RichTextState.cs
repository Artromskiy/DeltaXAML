namespace DeltaXAML.Internal;

internal struct RichTextState
{
    internal Delta.XAML.UiTextSpan[] Spans;
    internal Delta.XAML.UiInlineHitRange[] HitRanges;
    internal int HitRangeCount;
    internal UiSize DesiredSize;
}
