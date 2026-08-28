namespace DeltaXAML.Internal;

internal static class UiItemsControlGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(11),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange |
        UiDescriptorCapabilities.PropertySetters);

    internal static ItemsControl Create() => new();

    internal static void ApplyItems<T>(
        ref ItemsControlState state,
        IUiItemSource<T> source,
        IUiItemSource<T>? previous,
        IUiItemFactory<T> factory,
        List<UiElement> previousRealized,
        List<UiElement> nextRealized)
    {
        ItemsControlMutationMixin.Apply(source, previous, factory, previousRealized, nextRealized);
        state.ItemCount = source.Count;
    }
}

internal static class UiScrollViewerGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(12),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange |
        UiDescriptorCapabilities.PropertySetters);

    internal static ScrollViewer Create() => new();

    internal static void Measure(ref ScrollViewerState state, in UiMeasureContext context) =>
        ScrollViewerMeasureMixin.Measure(ref state, in context);

    internal static void Arrange(ref ScrollViewerState state, in UiArrangeContext context) =>
        ScrollViewerArrangeMixin.Arrange(ref state, in context);

    internal static bool TryScrollBy(ref ScrollViewerState state, float x, float y)
    {
        var next = new UiPoint(
            MathF.Max(0, state.Offset.X + x),
            MathF.Max(0, state.Offset.Y + y));
        if (next == state.Offset)
        {
            return false;
        }

        state.Offset = next;
        return true;
    }
}
