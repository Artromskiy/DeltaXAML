using Delta;

namespace DeltaXAML.Internal;

internal interface IRichTextLayoutCapability<TState>
    where TState : struct
{
    static abstract void Measure(ref TState state, in UiMeasureContext context);

    static abstract void Arrange(ref TState state, in UiArrangeContext context);
}

internal readonly struct RichTextLayoutMixin : IRichTextLayoutCapability<RichTextState>
{
    public static void Measure(ref RichTextState state, in UiMeasureContext context)
    {
        if (context.TextMetrics.IsValid)
        {
            state.DesiredSize = new(
                Maths.Min(context.TextMetrics.Width, context.Available.Width),
                Maths.Min(context.TextMetrics.Height, context.Available.Height));
            return;
        }

        var width = 0f;
        var height = 0f;
        for (var i = 0; i < state.Spans.Length; i++)
        {
            var span = state.Spans[i];
            width += span.Text.Length * Maths.Max(1, span.FontSize * UiTextMeasureFallback.AverageGlyphWidthFactor);
            height = Maths.Max(height, span.FontSize * UiTextMeasureFallback.LineHeightFactor);
        }

        state.DesiredSize = new(Maths.Min(width, context.Available.Width), Maths.Min(height, context.Available.Height));
    }

    public static void Arrange(ref RichTextState state, in UiArrangeContext context)
    {
    }
}

internal readonly struct RichTextHitTestMixin
{
    internal static bool TryGetLink(
        ref RichTextState state,
        UiPoint point,
        out Delta.XAML.UiCommandId command,
        out string? argument)
    {
        for (var i = 0; i < state.HitRangeCount; i++)
        {
            var bounds = state.HitRanges[i].Bounds;
            if (point.X >= bounds.x && point.Y >= bounds.y && point.X < bounds.x + bounds.z && point.Y < bounds.y + bounds.w)
            {
                command = state.HitRanges[i].Command;
                argument = state.HitRanges[i].Argument;
                return command.IsValid;
            }
        }

        command = default;
        argument = null;
        return false;
    }
}
