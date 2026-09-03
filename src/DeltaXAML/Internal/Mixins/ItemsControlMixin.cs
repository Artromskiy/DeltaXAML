using Delta;

namespace DeltaXAML.Internal;

internal readonly struct ItemsControlMutationMixin
{
    public static void Apply<T>(
        IUiItemSource<T> source,
        IUiItemSource<T>? previous,
        IUiItemFactory<T> factory,
        List<UiElement> previousRealized,
        List<UiElement> nextRealized)
    {
        ArgumentNullException.ThrowIfNull(previousRealized);
        ArgumentNullException.ThrowIfNull(nextRealized);
        nextRealized.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            if (previous is not null && i < previousRealized.Count && source.Matches(previous, i))
            {
                nextRealized.Add(previousRealized[i]);
            }
            else
            {
                nextRealized.Add(factory.Create(source, i));
            }
        }
    }
}

internal readonly struct ItemsControlMeasureMixin : IMeasureMixin<ItemsControlState>
{
    public static void Measure(ref ItemsControlState state, in UiMeasureContext context)
    {
        if (!state.GridLayout)
        {
            StackPanelMeasureMixin.Measure(ref state.Layout, in context);
            return;
        }

        var children = context.Children;
        if (children is not null && context.MeasureChildren)
        {
            for (var i = 0; i < children.Count; i++)
            {
                UiMeasureQueue.Add(in context, children[i], context.Available);
            }
        }

        // A grid-backed items host fills its viewport; its children receive their
        // equal star slots during arrange.
        state.Layout.DesiredSize = context.Available;
    }
}

internal readonly struct ItemsControlArrangeMixin : IArrangeMixin<ItemsControlState>
{
    public static void Arrange(ref ItemsControlState state, in UiArrangeContext context)
    {
        if (!state.GridLayout)
        {
            StackPanelArrangeMixin.Arrange(ref state.Layout, in context);
            return;
        }

        state.Layout.Bounds = context.Bounds;
        state.Layout.Clip = context.Clip;
        var children = context.Children;
        if (children is null || children.Count == 0)
        {
            return;
        }

        var columns = Maths.Max(1, state.GridColumns);
        var rows = Maths.Max(1, state.GridRows);
        var cellWidth = context.Bounds.Width / columns;
        var cellHeight = context.Bounds.Height / rows;
        for (var i = 0; i < children.Count; i++)
        {
            var row = i / columns;
            var column = i % columns;
            if (row >= rows)
            {
                break;
            }

            UiArrangeQueue.Add(in context, children[i], new(
                context.Bounds.X + column * cellWidth,
                context.Bounds.Y + row * cellHeight,
                cellWidth,
                cellHeight));
        }
    }
}
