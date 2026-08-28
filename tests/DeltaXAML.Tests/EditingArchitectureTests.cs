using DeltaXAML.Internal;

internal static class EditingArchitectureTests
{
    public static void Run()
    {
        TextBoxEditingUsesTypedState();
        NumericEditorUsesTypedValidation();
    }

    private static void TextBoxEditingUsesTypedState()
    {
        var state = new TextBoxState { CaretIndex = 3, SelectionStart = 3 };
        var selectAll = new UiKeyEvent(65, true, Control: true);
        var action = UiTextBoxGenerated.ProcessKey(ref state, in selectAll, 3);
        Assert.Equal(UiTextEditAction.SelectAll, action, "TextBox descriptor dispatches select-all to the typed editing mixin");
        Assert.Equal(0, state.SelectionStart, "select-all starts at the first character");
        Assert.Equal(3, state.SelectionLength, "select-all covers the text");

        var backspace = new UiKeyEvent(8, true);
        action = UiTextBoxGenerated.ProcessKey(ref state, in backspace, 3);
        Assert.Equal(UiTextEditAction.DeleteSelection, action, "TextBox editing reports a typed delete operation");
        Assert.Equal(0, state.SelectionStart, "delete operation keeps the selected range start");
        Assert.Equal(3, state.SelectionLength, "delete operation keeps the selected range for the owner");

        var retained = UiTextBoxGenerated.Create();
        retained.SetText("abc", false);
        Assert.True(retained.ApplyKey(in selectAll), "retained TextBox handles select-all through the generated path");
        Assert.Equal(3, retained.SelectionLength, "retained TextBox stores selection in TextBoxState");
        retained.ApplyText(new UiTextInput("x".AsMemory()));
        Assert.Equal("x", retained.Text, "retained TextBox edits through the existing text event boundary");
        Assert.Equal(1, retained.CaretIndex, "retained TextBox updates the typed caret state");

        Assert.True(UiTextBoxGenerated.Descriptor.IsValid, "TextBox descriptor has a valid compact identity");
        Assert.True(UiDescriptorCatalog.TryResolve(UiTextBoxGenerated.Descriptor.Index, out var descriptor), "TextBox descriptor resolves through the compact catalog");
        Assert.Equal(UiTextBoxGenerated.Descriptor, descriptor, "TextBox catalog preserves generated metadata");
        Assert.Equal((ushort)9, UiTextBoxGenerated.Descriptor.Index.Value, "TextBox has the ninth compact descriptor index");
    }

    private static void NumericEditorUsesTypedValidation()
    {
        Assert.True(UiNumericEditorGenerated.TryParse("4.5", 0, 10, out var value, out var diagnostic), "numeric descriptor accepts a value within range");
        Assert.Equal(4.5, value, "numeric descriptor returns the parsed value");
        Assert.True(diagnostic is null, "valid numeric input has no diagnostic");
        Assert.True(!UiNumericEditorGenerated.TryParse("11", 0, 10, out _, out diagnostic), "numeric descriptor rejects a value outside range");
        Assert.True(diagnostic is not null, "invalid numeric input has an inline diagnostic");

        var committed = new NumericEditorState { Value = 2, Min = 0, Max = 10, CommittedText = "2" };
        Assert.True(UiNumericEditorGenerated.TryCommit(ref committed, "4.5", 0, 10, out var formatted, out diagnostic), "numeric descriptor commits valid text through typed state");
        Assert.Equal(4.5, committed.Value, "numeric commit updates the typed value");
        Assert.Equal(formatted, committed.CommittedText, "numeric commit keeps formatted text in state");
        Assert.True(!UiNumericEditorGenerated.TryCommit(ref committed, "11", 0, 10, out _, out diagnostic), "numeric descriptor rejects invalid commit text");
        Assert.Equal(4.5, committed.Value, "invalid numeric commit preserves the prior typed value");

        var state = new NumericEditorState { Value = 4, Min = 0, Max = 5 };
        Assert.True(UiNumericEditorGenerated.TryAdjust(ref state, 1, out diagnostic), "numeric descriptor accepts an in-range increment");
        Assert.Equal(5, state.Value, "numeric increment updates typed state");
        Assert.True(!UiNumericEditorGenerated.TryAdjust(ref state, 1, out diagnostic), "numeric descriptor rejects an out-of-range increment");
        Assert.Equal(5, state.Value, "failed numeric increment preserves the prior value");

        var retained = UiNumericEditorGenerated.Create();
        retained.Min = 0;
        retained.Max = 5;
        retained.Initialize(2);
        Assert.True(!retained.TryCommitText("8"), "retained NumericEditor rejects invalid text");
        Assert.Equal(2, retained.Value, "invalid NumericEditor text preserves the committed value");
        Assert.True(retained.HasValidationError, "invalid NumericEditor text exposes validation state");
        Assert.True(retained.ApplyKey(new UiKeyEvent(38, true)), "retained NumericEditor handles keyboard increment");
        Assert.Equal(3, retained.Value, "keyboard increment uses the generated numeric path");

        Assert.True(UiNumericEditorGenerated.Descriptor.IsValid, "NumericEditor descriptor has a valid compact identity");
        Assert.True(UiDescriptorCatalog.TryResolve(UiNumericEditorGenerated.Descriptor.Index, out var descriptor), "NumericEditor descriptor resolves through the compact catalog");
        Assert.Equal(UiNumericEditorGenerated.Descriptor, descriptor, "NumericEditor catalog preserves generated metadata");
        Assert.Equal((ushort)10, UiNumericEditorGenerated.Descriptor.Index.Value, "NumericEditor has the tenth compact descriptor index");
    }
}
