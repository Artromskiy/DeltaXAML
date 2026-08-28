using Delta.XAML.Contract;

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
        if (input.Kind != UiKeyEventKind.Down)
        {
            return UiTextEditAction.None;
        }

        if (input.Modifiers.Contains(UiModifierBits.Control))
        {
            return input.PhysicalKey.Value switch
            {
                65 => SelectAllAndReturn(ref state, textLength),
                67 => UiTextEditAction.Copy,
                88 => UiTextEditAction.Cut,
                86 => UiTextEditAction.Paste,
                90 => UiTextEditAction.Undo,
                89 => UiTextEditAction.Redo,
                _ => UiTextEditAction.None,
            };
        }

        if (input.PhysicalKey.Value == 8)
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

        if (input.PhysicalKey.Value == 46 && state.CaretIndex < textLength)
        {
            state.SelectionStart = state.CaretIndex;
            state.SelectionLength = 1;
            return UiTextEditAction.DeleteSelection;
        }

        if (input.PhysicalKey.Value == 37 && state.CaretIndex > 0)
        {
            state.CaretIndex--;
            state.SelectionStart = state.CaretIndex;
            state.SelectionLength = 0;
            return UiTextEditAction.None;
        }

        if (input.PhysicalKey.Value == 39 && state.CaretIndex < textLength)
        {
            state.CaretIndex++;
            state.SelectionStart = state.CaretIndex;
            state.SelectionLength = 0;
        }

        return UiTextEditAction.None;
    }

    internal static void SelectAll(ref TextBoxState state, int textLength)
    {
        state.SelectionStart = 0;
        state.SelectionLength = textLength;
        state.CaretIndex = textLength;
    }

    private static UiTextEditAction SelectAllAndReturn(ref TextBoxState state, int textLength)
    {
        SelectAll(ref state, textLength);
        return UiTextEditAction.SelectAll;
    }

    internal static bool HasSelection(in TextBoxState state) => state.SelectionLength > 0;

    internal static bool ApplyComposition(
        ref TextBoxState state,
        string current,
        in UiCompositionEvent input)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (input.Stage is UiCompositionStage.Finished or UiCompositionStage.Cancelled)
        {
            return ClearComposition(ref state);
        }

        var start = state.CompositionDisplayText is null
            ? state.SelectionLength > 0 ? state.SelectionStart : state.CaretIndex
            : state.CompositionStart;
        var replacedLength = state.CompositionDisplayText is null ? state.SelectionLength : state.SelectionLength;
        if ((uint)start > (uint)current.Length || replacedLength < 0 || start + replacedLength > current.Length)
        {
            return false;
        }

        var preedit = input.Preedit.Span;
        var display = string.Concat(current.AsSpan(0, start), preedit, current.AsSpan(start + replacedLength));
        var selectionStart = Math.Clamp(input.Selection.StartUtf16, 0, preedit.Length);
        var selectionLength = Math.Clamp(input.Selection.LengthUtf16, 0, preedit.Length - selectionStart);
        var changed = !string.Equals(state.CompositionDisplayText, display, StringComparison.Ordinal) ||
                      state.CompositionSelectionStart != selectionStart ||
                      state.CompositionSelectionLength != selectionLength;
        state.CompositionStart = start;
        state.CompositionText = input.Preedit.ToString();
        state.CompositionDisplayText = display;
        state.CompositionSelectionStart = selectionStart;
        state.CompositionSelectionLength = selectionLength;
        return changed;
    }

    internal static bool ClearComposition(ref TextBoxState state)
    {
        if (state.CompositionDisplayText is null)
        {
            return false;
        }

        state.CompositionStart = 0;
        state.CompositionSelectionStart = 0;
        state.CompositionSelectionLength = 0;
        state.CompositionText = null;
        state.CompositionDisplayText = null;
        return true;
    }

    internal static string GetSelection(string text, in TextBoxState state) =>
        text.Substring(state.SelectionStart, state.SelectionLength);

    internal static string ReplaceSelection(
        ref TextBoxState state,
        List<string> undo,
        List<string> redo,
        string current,
        ReadOnlySpan<char> inserted)
    {
        RecordUndo(undo, redo, current);
        var start = state.CaretIndex;
        if (HasSelection(in state))
        {
            start = state.SelectionStart;
            current = current.Remove(start, state.SelectionLength);
        }

        var result = InsertText(current, start, inserted);
        state.CaretIndex = start + inserted.Length;
        state.SelectionStart = state.CaretIndex;
        state.SelectionLength = 0;
        return result;
    }

    internal static string DeleteRange(
        ref TextBoxState state,
        List<string> undo,
        List<string> redo,
        string current,
        int start,
        int length,
        bool recordUndo)
    {
        if (recordUndo)
        {
            RecordUndo(undo, redo, current);
        }

        var result = current.Remove(start, length);
        state.CaretIndex = start;
        state.SelectionStart = start;
        state.SelectionLength = 0;
        return result;
    }

    internal static bool TryUndo(
        ref TextBoxState state,
        List<string> undo,
        List<string> redo,
        string current,
        out string result)
    {
        if (undo.Count == 0)
        {
            result = current;
            return false;
        }

        redo.Add(current);
        result = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        MoveCaretToEnd(ref state, result.Length);
        return true;
    }

    internal static bool TryRedo(
        ref TextBoxState state,
        List<string> undo,
        List<string> redo,
        string current,
        out string result)
    {
        if (redo.Count == 0)
        {
            result = current;
            return false;
        }

        undo.Add(current);
        result = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        MoveCaretToEnd(ref state, result.Length);
        return true;
    }

    internal static void RecordUndo(List<string> undo, List<string> redo, string current)
    {
        undo.Add(current);
        redo.Clear();
    }

    private static void MoveCaretToEnd(ref TextBoxState state, int textLength)
    {
        state.CaretIndex = textLength;
        state.SelectionStart = textLength;
        state.SelectionLength = 0;
    }

    private static string InsertText(string value, int index, ReadOnlySpan<char> inserted)
    {
        return string.Concat(value.AsSpan(0, index), inserted, value.AsSpan(index));
    }
}

internal readonly struct NumericEditorInputMixin : ITextBoxInputMixin<NumericEditorState>
{
    public static UiTextEditAction ProcessKey(ref NumericEditorState state, in UiKeyEvent input, int textLength)
    {
        if (input.Kind == UiKeyEventKind.Down && input.PhysicalKey.Value == 38)
        {
            return UiTextEditAction.Increment;
        }

        if (input.Kind == UiKeyEventKind.Down && input.PhysicalKey.Value == 40)
        {
            return UiTextEditAction.Decrement;
        }

        return UiTextEditAction.None;
    }
}

internal readonly struct NumericValidationMixin
{
    internal static bool TryCommit(
        ref NumericEditorState state,
        string text,
        double min,
        double max,
        out string formatted,
        out string? diagnostic)
    {
        if (!TryParse(text, min, max, out var value, out diagnostic))
        {
            formatted = string.Empty;
            return false;
        }

        state.Value = value;
        formatted = Format(value);
        state.CommittedText = formatted;
        return true;
    }

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

    internal static string Format(double value) =>
        value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);

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
