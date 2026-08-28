using Delta.XAML.Contract;

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

    internal static bool TryCommit(
        ref NumericEditorState state,
        string text,
        double min,
        double max,
        out string formatted,
        out string? diagnostic) =>
        NumericValidationMixin.TryCommit(ref state, text, min, max, out formatted, out diagnostic);

    internal static string Format(double value) => NumericValidationMixin.Format(value);

    internal static bool TryAdjust(ref NumericEditorState state, double delta, out string? diagnostic) =>
        NumericValidationMixin.TryAdjust(ref state, delta, out diagnostic);

    internal static bool TrySetMinimum(ref NumericEditorState state, double value)
    {
        if (state.Min.Equals(value))
        {
            return false;
        }

        state.Min = value;
        return true;
    }

    internal static bool TrySetMaximum(ref NumericEditorState state, double value)
    {
        if (state.Max.Equals(value))
        {
            return false;
        }

        state.Max = value;
        return true;
    }

    internal static bool TrySetValue(NumericEditor element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        ref NumericEditorState state = ref element.State;
        var text = Format(value);
        if (!NumericValidationMixin.TryCommit(ref state, text, state.Min, state.Max, out var formatted, out _))
        {
            return false;
        }

        UiTextBlockGenerated.TrySetText(ref ((TextBlock)element).State, formatted);
        return true;
    }
}
