namespace DeltaXAML.Internal;

/// <summary>Typed companion for the migrated StackPanel layout path.</summary>
internal static class UiStackPanelGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(2),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange |
        UiDescriptorCapabilities.PropertySetters);

    internal static StackPanel Create() => new();

    internal static void Measure(
        ref StackPanelState state,
        in UiMeasureContext context) =>
        StackPanelMeasureMixin.Measure(ref state, in context);

    internal static void Arrange(
        ref StackPanelState state,
        in UiArrangeContext context) =>
        StackPanelArrangeMixin.Arrange(ref state, in context);

    internal static bool TrySetOrientation(
        ref StackPanelState state,
        UiOrientation value)
    {
        if (state.Orientation == value)
        {
            return false;
        }

        state.Orientation = value;
        return true;
    }
}
