using Delta;

namespace DeltaXAML.Internal;

internal readonly struct StackPanelMeasureMixin : IMeasureMixin<StackPanelState>
{
    public static void Measure(ref StackPanelState state, in UiMeasureContext context)
    {
        var width = 0f;
        var height = 0f;
        var children = context.Children;
        if (children is not null)
        {
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (context.MeasureChildren)
                {
                    UiMeasureQueue.Add(in context, child, context.Available);
                }

                if (state.Orientation == UiOrientation.Horizontal)
                {
                    width += child.DesiredSize.Width;
                    height = Maths.Max(height, child.DesiredSize.Height);
                }
                else
                {
                    width = Maths.Max(width, child.DesiredSize.Width);
                    height += child.DesiredSize.Height;
                }
            }
        }

        state.DesiredSize = new(width, height);
    }
}

internal readonly struct StackPanelArrangeMixin : IArrangeMixin<StackPanelState>
{
    public static void Arrange(ref StackPanelState state, in UiArrangeContext context)
    {
        state.Bounds = context.Bounds;
        state.Clip = context.Clip;
        var children = context.Children;
        if (children is null || children.Count == 0)
        {
            return;
        }

        var cursor = state.Orientation == UiOrientation.Horizontal
            ? context.Bounds.X
            : context.Bounds.Y;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var desired = child.DesiredSize;
            var main = state.Orientation == UiOrientation.Horizontal
                ? desired.Width
                : desired.Height;
            UiArrangeQueue.Add(in context, child, state.Orientation == UiOrientation.Horizontal
                ? new UiRect(cursor, context.Bounds.Y, main, context.Bounds.Height)
                : new UiRect(context.Bounds.X, cursor, context.Bounds.Width, main));
            cursor += main;
        }
    }
}
