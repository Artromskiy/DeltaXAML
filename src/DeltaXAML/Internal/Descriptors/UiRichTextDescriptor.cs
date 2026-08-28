namespace DeltaXAML.Internal;

internal static class UiRichTextGenerated
{
    internal static UiTypeDescriptor Descriptor { get; } = new(
        new UiRuntimeTypeIndex(20),
        UiDescriptorCapabilities.Factory | UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange | UiDescriptorCapabilities.Visual);

    internal static RichTextBlock Create() => new();

    internal static void Measure(ref RichTextState state, in UiMeasureContext context) =>
        RichTextLayoutMixin.Measure(ref state, in context);

    internal static void Arrange(ref RichTextState state, in UiArrangeContext context) =>
        RichTextLayoutMixin.Arrange(ref state, in context);
}
