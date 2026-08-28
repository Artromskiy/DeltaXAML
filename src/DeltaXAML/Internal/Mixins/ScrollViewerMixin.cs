namespace DeltaXAML.Internal;

internal readonly struct ScrollViewerMeasureMixin : IMeasureMixin<ScrollViewerState>
{
    public static void Measure(ref ScrollViewerState state, in UiMeasureContext context)
    {
        var children = context.Children;
        if (children is null || children.Count == 0)
        {
            state.DesiredSize = default;
            return;
        }

        if (context.MeasureChildren)
        {
            UiMeasureQueue.Add(in context, children[0], context.Available);
        }

        state.DesiredSize = children[0].DesiredSize;
    }
}

internal readonly struct ScrollViewerArrangeMixin : IArrangeMixin<ScrollViewerState>
{
    public static void Arrange(ref ScrollViewerState state, in UiArrangeContext context)
    {
        state.Bounds = context.Bounds;
        state.Clip = context.Clip;
        var children = context.Children;
        if (children is not null && children.Count > 0)
        {
            var child = children[0];
            var maxOffsetX = MathF.Max(0, child.DesiredSize.Width - context.Bounds.Width);
            var maxOffsetY = MathF.Max(0, child.DesiredSize.Height - context.Bounds.Height);
            state.Offset = new(
                MathF.Min(state.Offset.X, maxOffsetX),
                MathF.Min(state.Offset.Y, maxOffsetY));
            UiArrangeQueue.Add(in context, child, new(
                context.Bounds.X - state.Offset.X,
                context.Bounds.Y - state.Offset.Y,
                child.DesiredSize.Width,
                child.DesiredSize.Height),
                UiRect.Intersect(context.Clip, context.Bounds));
        }
    }
}
