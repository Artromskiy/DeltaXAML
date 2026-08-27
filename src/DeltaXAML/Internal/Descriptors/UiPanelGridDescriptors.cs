namespace DeltaXAML.Internal;

/// <summary>Typed companion for the migrated panel layout path.</summary>
internal static class UiPanelGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(5),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange);

    internal static Panel Create() => new();

    internal static void Measure(ref PanelState state, in UiMeasureContext context) =>
        PanelMeasureMixin.Measure(ref state, in context);

    internal static void Arrange(ref PanelState state, in UiArrangeContext context) =>
        PanelArrangeMixin.Arrange(ref state, in context);
}

/// <summary>Typed companion for the migrated fixed/auto/star grid path.</summary>
internal static class UiGridGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(6),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange |
        UiDescriptorCapabilities.PropertySetters);

    internal static Grid Create() => new();

    internal static void Measure(ref GridState state, in UiMeasureContext context) =>
        GridMeasureMixin.Measure(ref state, in context);

    internal static void Arrange(ref GridState state, in UiArrangeContext context) =>
        GridArrangeMixin.Arrange(ref state, in context);

    internal static bool TrySetColumns(ref GridState state, GridLength[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ReferenceEquals(state.Columns, value))
        {
            return false;
        }

        state.Columns = value;
        Ensure(ref state.MeasuredColumns, value.Length);
        Ensure(ref state.ResolvedColumns, Math.Max(1, value.Length));
        return true;
    }

    internal static bool TrySetRows(ref GridState state, GridLength[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ReferenceEquals(state.Rows, value))
        {
            return false;
        }

        state.Rows = value;
        Ensure(ref state.MeasuredRows, value.Length);
        Ensure(ref state.ResolvedRows, Math.Max(1, value.Length));
        return true;
    }

    private static void Ensure(ref float[] values, int count)
    {
        if (values.Length < count)
        {
            Array.Resize(ref values, Math.Max(count, Math.Max(1, values.Length * 2)));
        }
    }
}
