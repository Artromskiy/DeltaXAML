using Delta;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXAML.Internal;

internal static class CoordinateConventionTests
{
    internal static void Run()
    {
        var bounds = new UiRect(10, 20, 30, 40);

        Assert.True(bounds.Contains(new UiPoint(10, 20)), "top-left edge belongs to a UI rectangle");
        Assert.True(bounds.Contains(new UiPoint(39.999f, 59.999f)), "interior point belongs to a UI rectangle");
        Assert.True(!bounds.Contains(new UiPoint(40, 20)), "right edge is outside a half-open UI rectangle");
        Assert.True(!bounds.Contains(new UiPoint(10, 60)), "bottom edge is outside a half-open UI rectangle");
        Assert.True(!bounds.Contains(new UiPoint(9.999f, 20)), "left side is outside a UI rectangle");
        Assert.True(!bounds.Contains(new UiPoint(10, 19.999f)), "top side is outside a UI rectangle");

        var state = new RichTextState
        {
            HitRanges = [new(new float4(10, 20, 30, 40), new UiCommandId(Guid.NewGuid()), null)],
            HitRangeCount = 1,
        };
        Assert.True(
            RichTextHitTestMixin.TryGetLink(ref state, new(39.999f, 59.999f), out _, out _),
            "rich-text link hit testing accepts only interior points");
        Assert.True(
            !RichTextHitTestMixin.TryGetLink(ref state, new(40, 20), out _, out _),
            "rich-text link hit testing excludes the right edge");
        Assert.True(
            !RichTextHitTestMixin.TryGetLink(ref state, new(10, 60), out _, out _),
            "rich-text link hit testing excludes the bottom edge");
    }
}
