namespace DeltaXAML.Internal;

internal static class UiOverlayGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(new(15), UiDescriptorCapabilities.Factory | UiDescriptorCapabilities.Measure | UiDescriptorCapabilities.Arrange | UiDescriptorCapabilities.Input);
    internal static Overlay Create() => new();
    internal static void Measure(ref OverlayState state, in UiMeasureContext context) => PanelMeasureMixin.Measure(ref state.Layout, in context);
    internal static void Arrange(ref OverlayState state, in UiArrangeContext context) => PanelArrangeMixin.Arrange(ref state.Layout, in context);
}

internal static class UiCollectionViewGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(new(16), UiDescriptorCapabilities.Factory | UiDescriptorCapabilities.Measure | UiDescriptorCapabilities.Arrange | UiDescriptorCapabilities.PropertySetters);
    internal static CollectionView Create() => new();
    internal static void Measure(ref CollectionViewState state, in UiMeasureContext context) => ContentControlMeasureMixin.Measure(ref state.Layout, in context);
    internal static void Arrange(ref CollectionViewState state, in UiArrangeContext context) => ContentControlArrangeMixin.Arrange(ref state.Layout, in context);
}

internal static class UiPickerGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(new(17), UiDescriptorCapabilities.Factory | UiDescriptorCapabilities.Measure | UiDescriptorCapabilities.Arrange | UiDescriptorCapabilities.Input | UiDescriptorCapabilities.PropertySetters);
    internal static Picker Create() => new();
    internal static void Measure(ref PickerState state, in UiMeasureContext context) => PanelMeasureMixin.Measure(ref state.Layout, in context);
    internal static void Arrange(ref PickerState state, in UiArrangeContext context) => PanelArrangeMixin.Arrange(ref state.Layout, in context);
}

internal static class UiTabViewGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(new(18), UiDescriptorCapabilities.Factory | UiDescriptorCapabilities.Measure | UiDescriptorCapabilities.Arrange | UiDescriptorCapabilities.PropertySetters);
    internal static TabView Create() => new();
    internal static void Measure(ref TabViewState state, in UiMeasureContext context) => PanelMeasureMixin.Measure(ref state.Layout, in context);
    internal static void Arrange(ref TabViewState state, in UiArrangeContext context) => PanelArrangeMixin.Arrange(ref state.Layout, in context);
}

internal static class UiMenuGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(new(19), UiDescriptorCapabilities.Factory | UiDescriptorCapabilities.Measure | UiDescriptorCapabilities.Arrange | UiDescriptorCapabilities.Input);
    internal static Menu Create() => new();
    internal static void Measure(ref MenuState state, in UiMeasureContext context) => StackPanelMeasureMixin.Measure(ref state.Layout, in context);
    internal static void Arrange(ref MenuState state, in UiArrangeContext context) => StackPanelArrangeMixin.Arrange(ref state.Layout, in context);
}
