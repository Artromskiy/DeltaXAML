using Delta.XAML.Contract;

namespace DeltaXAML.Internal;

internal static class UiSliderGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(13),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange |
        UiDescriptorCapabilities.Input |
        UiDescriptorCapabilities.PropertySetters);

    internal static Slider Create() => new();

    internal static void Measure(ref SliderState state, in UiMeasureContext context) =>
        SliderMeasureMixin.Measure(ref state, in context);

    internal static void Arrange(ref SliderState state, in UiArrangeContext context) =>
        SliderArrangeMixin.Arrange(ref state, in context);

    internal static bool TryGetPointerValue(ref SliderState state, in UiRoutedEvent input, out double value) =>
        SliderInputMixin.TryGetPointerValue(ref state, in input, out value);

    internal static bool TryGetKeyValue(ref SliderState state, in UiKeyEvent input, out double value) =>
        SliderInputMixin.TryGetKeyValue(ref state, in input, out value);
}

internal static class UiImageGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(14),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange |
        UiDescriptorCapabilities.Visual |
        UiDescriptorCapabilities.PropertySetters);

    internal static Image Create() => new();

    internal static void Measure(ref ImageState state, in UiMeasureContext context) =>
        ImageMeasureMixin.Measure(ref state, in context);

    internal static void Arrange(ref ImageState state, in UiArrangeContext context) =>
        ImageArrangeMixin.Arrange(ref state, in context);
}
