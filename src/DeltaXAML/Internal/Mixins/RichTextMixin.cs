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
        var width = 0f;
        var height = 0f;
        for (var i = 0; i < state.Spans.Length; i++)
        {
            var span = state.Spans[i];
            width += span.Text.Length * MathF.Max(1, span.FontSize * context.DpiScale * 0.55f);
            height = MathF.Max(height, span.FontSize * context.DpiScale * 1.25f);
        }

        state.DesiredSize = new(MathF.Min(width, context.Available.Width), MathF.Min(height, context.Available.Height));
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
