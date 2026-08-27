namespace DeltaXAML.Internal;

internal static class UiTextBoxGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(9),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Input |
        UiDescriptorCapabilities.PropertySetters);

    internal static TextBox Create() => new();

    internal static UiTextEditAction ProcessKey(ref TextBoxState state, in UiKeyEvent input, int textLength) =>
        TextBoxEditingMixin.ProcessKey(ref state, in input, textLength);
}

internal static class UiNumericEditorGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(10),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Input |
        UiDescriptorCapabilities.PropertySetters);

    internal static NumericEditor Create() => new();

    internal static UiTextEditAction ProcessKey(ref NumericEditorState state, in UiKeyEvent input, int textLength) =>
        NumericEditorInputMixin.ProcessKey(ref state, in input, textLength);

    internal static bool TryParse(
        string text,
        double min,
        double max,
        out double value,
        out string? diagnostic) =>
        NumericValidationMixin.TryParse(text, min, max, out value, out diagnostic);

    internal static bool TryAdjust(ref NumericEditorState state, double delta, out string? diagnostic) =>
        NumericValidationMixin.TryAdjust(ref state, delta, out diagnostic);
}
