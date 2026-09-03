using Delta;

namespace DeltaXAML.Internal;

internal readonly struct BorderMeasureMixin : IMeasureMixin<BorderState>
{
    public static void Measure(ref BorderState state, in UiMeasureContext context)
    {
        var child = FirstChild(context.Children);
        if (child is null)
        {
            state.DesiredSize = new(state.Padding.Horizontal, state.Padding.Vertical);
            return;
        }

        if (context.MeasureChildren)
        {
            UiMeasureQueue.Add(in context, child, new(
                Maths.Max(0, context.Available.Width - state.Padding.Horizontal),
                Maths.Max(0, context.Available.Height - state.Padding.Vertical)));
        }

        state.DesiredSize = new(
            child.DesiredSize.Width + state.Padding.Horizontal,
            child.DesiredSize.Height + state.Padding.Vertical);
    }

    private static UiElement? FirstChild(IReadOnlyList<UiElement>? children)
    {
        if (children is null || children.Count == 0)
        {
            return null;
        }

        return children[0];
    }
}

internal readonly struct BorderArrangeMixin : IArrangeMixin<BorderState>
{
    public static void Arrange(ref BorderState state, in UiArrangeContext context)
    {
        state.Bounds = context.Bounds;
        state.Clip = context.Clip;
        var children = context.Children;
        if (children is null || children.Count == 0)
        {
            return;
        }

        UiArrangeQueue.Add(in context, children[0], new(
            context.Bounds.X + state.Padding.Left,
            context.Bounds.Y + state.Padding.Top,
            Maths.Max(0, context.Bounds.Width - state.Padding.Horizontal),
            Maths.Max(0, context.Bounds.Height - state.Padding.Vertical)));
    }
}

internal readonly struct ContentControlMeasureMixin : IMeasureMixin<ContentControlState>
{
    public static void Measure(ref ContentControlState state, in UiMeasureContext context)
    {
        var children = context.Children;
        if (children is null || children.Count == 0)
        {
            state.DesiredSize = default;
            return;
        }

        var child = children[0];
        if (context.MeasureChildren)
        {
            UiMeasureQueue.Add(in context, child, context.Available);
        }

        state.DesiredSize = child.DesiredSize;
    }
}

internal readonly struct ContentControlArrangeMixin : IArrangeMixin<ContentControlState>
{
    public static void Arrange(ref ContentControlState state, in UiArrangeContext context)
    {
        state.Bounds = context.Bounds;
        state.Clip = context.Clip;
        var children = context.Children;
        if (children is not null && children.Count > 0)
        {
            UiArrangeQueue.Add(in context, children[0], context.Bounds);
        }
    }
}
