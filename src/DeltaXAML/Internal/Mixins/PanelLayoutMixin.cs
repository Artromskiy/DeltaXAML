namespace DeltaXAML.Internal;

internal readonly struct PanelMeasureMixin : IMeasureMixin<PanelState>
{
    public static void Measure(ref PanelState state, in UiMeasureContext context)
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

                width = MathF.Max(width, child.DesiredSize.Width);
                height = MathF.Max(height, child.DesiredSize.Height);
            }
        }

        state.DesiredSize = new(width, height);
    }
}

internal readonly struct PanelArrangeMixin : IArrangeMixin<PanelState>
{
    public static void Arrange(ref PanelState state, in UiArrangeContext context)
    {
        state.Bounds = context.Bounds;
        state.Clip = context.Clip;
        var children = context.Children;
        if (children is not null)
        {
            for (var i = 0; i < children.Count; i++)
            {
                UiArrangeQueue.Add(in context, children[i], context.Bounds);
            }
        }
    }
}

internal readonly struct GridMeasureMixin : IMeasureMixin<GridState>
{
    public static void Measure(ref GridState state, in UiMeasureContext context)
    {
        var children = context.Children;
        Array.Clear(state.MeasuredColumns, 0, state.Columns.Length);
        Array.Clear(state.MeasuredRows, 0, state.Rows.Length);
        if (children is not null)
        {
            if (context.MeasureChildren)
            {
                for (var i = 0; i < children.Count; i++)
                {
                    UiMeasureQueue.Add(in context, children[i], context.Available);
                }
            }

            var columnCount = Math.Max(1, state.Columns.Length);
            MeasureAuto(state.Columns, state.MeasuredColumns, children, true, columnCount);
            MeasureAuto(state.Rows, state.MeasuredRows, children, false, columnCount);
        }

        state.DesiredSize = new(
            Sum(state.MeasuredColumns, state.Columns.Length),
            Sum(state.MeasuredRows, state.Rows.Length));
    }

    private static void MeasureAuto(
        GridLength[] definitions,
        float[] measured,
        IReadOnlyList<UiElement> children,
        bool columns,
        int gridColumnCount)
    {
        for (var definition = 0; definition < definitions.Length; definition++)
        {
            if (definitions[definition].Unit == GridUnitType.Pixel)
            {
                measured[definition] = definitions[definition].Value;
                continue;
            }

            if (definitions[definition].Unit != GridUnitType.Auto)
            {
                continue;
            }

            for (var child = 0; child < children.Count; child++)
            {
                var element = children[child];
                var slot = columns
                    ? element.HasGridColumn ? element.GridColumn : child % Math.Max(1, definitions.Length)
                    : element.HasGridRow ? element.GridRow : child / gridColumnCount;
                if (slot != definition)
                {
                    continue;
                }

                measured[definition] = MathF.Max(
                    measured[definition],
                    columns ? element.DesiredSize.Width : element.DesiredSize.Height);
            }
        }
    }

    private static float Sum(float[] values, int count)
    {
        var total = 0f;
        for (var i = 0; i < count; i++)
        {
            total += values[i];
        }

        return total;
    }
}

internal readonly struct GridArrangeMixin : IArrangeMixin<GridState>
{
    public static void Arrange(ref GridState state, in UiArrangeContext context)
    {
        state.Bounds = context.Bounds;
        state.Clip = context.Clip;
        Resolve(
            state.Columns,
            state.MeasuredColumns,
            context.Bounds.Width,
            state.ResolvedColumns);
        Resolve(
            state.Rows,
            state.MeasuredRows,
            context.Bounds.Height,
            state.ResolvedRows);

        var children = context.Children;
        if (children is null)
        {
            return;
        }

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var columnCount = Math.Max(1, state.Columns.Length);
            var rowCount = Math.Max(1, state.Rows.Length);
            var column = Math.Min(child.HasGridColumn ? child.GridColumn : i % columnCount, columnCount - 1);
            var row = Math.Min(child.HasGridRow ? child.GridRow : i / columnCount, rowCount - 1);
            if (column < 0 || row < 0)
            {
                continue;
            }

            var columnSpan = Math.Min(child.GridColumnSpan, columnCount - column);
            var rowSpan = Math.Min(child.GridRowSpan, rowCount - row);

            UiArrangeQueue.Add(in context, child, new(
                context.Bounds.X + Sum(state.ResolvedColumns, column),
                context.Bounds.Y + Sum(state.ResolvedRows, row),
                Sum(state.ResolvedColumns, column, columnSpan),
                Sum(state.ResolvedRows, row, rowSpan)));
        }
    }

    private static void Resolve(
        GridLength[] definitions,
        float[] measured,
        float available,
        float[] result)
    {
        if (definitions.Length == 0)
        {
            result[0] = available;
            return;
        }

        Array.Clear(result, 0, definitions.Length);
        var remaining = available;
        var stars = 0f;
        for (var i = 0; i < definitions.Length; i++)
        {
            var definition = definitions[i];
            if (definition.Unit == GridUnitType.Pixel)
            {
                result[i] = definition.Value;
            }
            else if (definition.Unit == GridUnitType.Auto)
            {
                result[i] = measured[i];
            }
            else
            {
                stars += definition.Value;
            }

            remaining -= result[i];
        }

        if (stars <= 0)
        {
            return;
        }

        for (var i = 0; i < definitions.Length; i++)
        {
            if (definitions[i].Unit == GridUnitType.Star)
            {
                result[i] = MathF.Max(0, remaining) * definitions[i].Value / stars;
            }
        }
    }

    private static float Sum(float[] values, int count)
    {
        var total = 0f;
        for (var i = 0; i < count; i++)
        {
            total += values[i];
        }

        return total;
    }

    private static float Sum(float[] values, int start, int count)
    {
        var total = 0f;
        for (var i = start; i < start + count; i++)
        {
            total += values[i];
        }

        return total;
    }
}
