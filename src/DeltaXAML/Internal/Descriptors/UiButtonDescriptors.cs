namespace DeltaXAML.Internal;

internal static class UiButtonGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(7),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Input);

    internal static Button Create() => new();

    internal static bool Process(ref ButtonState state, in UiRoutedEvent routedEvent) =>
        ButtonInputMixin.Process(ref state, in routedEvent);
}

internal static class UiToggleButtonGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(8),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Input);

    internal static ToggleButton Create() => new();

    internal static bool Process(ref ToggleButtonState state, in UiRoutedEvent routedEvent) =>
        ToggleButtonInputMixin.Process(ref state, in routedEvent);
}
