namespace DeltaXAML.Internal;

/// <summary>Typed companion for the migrated Border layout path.</summary>
internal static class UiBorderGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(3),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange);

    internal static Border Create() => new();

    internal static void Measure(ref BorderState state, in UiMeasureContext context) =>
        BorderMeasureMixin.Measure(ref state, in context);

    internal static void Arrange(ref BorderState state, in UiArrangeContext context) =>
        BorderArrangeMixin.Arrange(ref state, in context);
}

/// <summary>Typed companion for the migrated single-child content path.</summary>
internal static class UiContentControlGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(4),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange);

    internal static ContentControl Create() => new();

    internal static void Measure(ref ContentControlState state, in UiMeasureContext context) =>
        ContentControlMeasureMixin.Measure(ref state, in context);

    internal static void Arrange(ref ContentControlState state, in UiArrangeContext context) =>
        ContentControlArrangeMixin.Arrange(ref state, in context);
}
