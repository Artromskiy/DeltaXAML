namespace DeltaXAML.Internal;

internal interface ITextBoxInputMixin<TState>
    where TState : struct
{
    static abstract UiTextEditAction ProcessKey(ref TState state, in UiKeyEvent input, int textLength);
}

internal readonly struct TextBoxEditingMixin : ITextBoxInputMixin<TextBoxState>
{
    public static UiTextEditAction ProcessKey(ref TextBoxState state, in UiKeyEvent input, int textLength)
    {
        if (!input.IsDown)
        {
            return UiTextEditAction.None;
        }

        if (input.Control)
        {
            return input.PhysicalKey switch
            {
                65 => SelectAll(ref state, textLength),
                67 => UiTextEditAction.Copy,
                88 => UiTextEditAction.Cut,
                86 => UiTextEditAction.Paste,
                90 => UiTextEditAction.Undo,
                89 => UiTextEditAction.Redo,
                _ => UiTextEditAction.None,
            };
        }

        if (input.PhysicalKey == 8)
        {
            if (state.SelectionLength > 0)
            {
                return UiTextEditAction.DeleteSelection;
            }

            if (state.CaretIndex > 0)
            {
                state.SelectionStart = state.CaretIndex - 1;
                state.SelectionLength = 1;
                return UiTextEditAction.DeleteSelection;
            }
        }

        if (input.PhysicalKey == 46 && state.CaretIndex < textLength)
        {
            state.SelectionStart = state.CaretIndex;
            state.SelectionLength = 1;
            return UiTextEditAction.DeleteSelection;
        }

        if (input.PhysicalKey == 37 && state.CaretIndex > 0)
        {
            state.CaretIndex--;
            state.SelectionStart = state.CaretIndex;
            state.SelectionLength = 0;
            return UiTextEditAction.None;
        }

        if (input.PhysicalKey == 39 && state.CaretIndex < textLength)
        {
            state.CaretIndex++;
            state.SelectionStart = state.CaretIndex;
            state.SelectionLength = 0;
        }

        return UiTextEditAction.None;
    }

    private static UiTextEditAction SelectAll(ref TextBoxState state, int textLength)
    {
        state.SelectionStart = 0;
        state.SelectionLength = textLength;
        state.CaretIndex = textLength;
        return UiTextEditAction.SelectAll;
    }
}

internal readonly struct NumericEditorInputMixin : ITextBoxInputMixin<NumericEditorState>
{
    public static UiTextEditAction ProcessKey(ref NumericEditorState state, in UiKeyEvent input, int textLength)
    {
        if (input.IsDown && input.PhysicalKey == 38)
        {
            return UiTextEditAction.Increment;
        }

        if (input.IsDown && input.PhysicalKey == 40)
        {
            return UiTextEditAction.Decrement;
        }

        return UiTextEditAction.None;
    }
}

internal readonly struct NumericValidationMixin
{
    public static bool TryParse(
        string text,
        double min,
        double max,
        out double value,
        out string? diagnostic)
    {
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) &&
            value >= min &&
            value <= max)
        {
            diagnostic = null;
            return true;
        }

        diagnostic = "Value must be between " +
            min.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " and " +
            max.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ".";
        return false;
    }

    public static bool TryAdjust(
        ref NumericEditorState state,
        double delta,
        out string? diagnostic)
    {
        var next = state.Value + delta;
        if (next < state.Min || next > state.Max)
        {
            diagnostic = "Value must be between " +
                state.Min.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " and " +
                state.Max.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ".";
            return false;
        }

        state.Value = next;
        diagnostic = null;
        return true;
    }
}
