using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Numeric retained editor with explicit validation and commit state.</summary>
public sealed class UiNumericEditor : UiElement
{
    private Retained.NumericEditor StateOwner => (Retained.NumericEditor)RetainedElement;

    public UiNumericEditor() : base(Retained.UiNumericEditorGenerated.Create(), null) { }

    internal UiNumericEditor(Retained.NumericEditor element) : base(element, null) { }

    public string Text { get => StateOwner.Text; set => StateOwner.Text = value; }

    public string FontKey { get => StateOwner.FontKey; set => StateOwner.FontKey = value; }

    public float FontSize { get => StateOwner.FontSize; set => StateOwner.FontSize = value; }

    public UiColor Foreground
    {
        get => ToPublicColor(StateOwner.Foreground);
        set => StateOwner.Foreground = ToRetainedColor(value);
    }

    public IUiClipboard? Clipboard
    {
        get => GetClipboard(StateOwner);
        set => SetClipboard(StateOwner, value);
    }

    public double CurrentValue => StateOwner.Value;

    public double Minimum { get => StateOwner.Min; set => StateOwner.Min = value; }

    public double Maximum { get => StateOwner.Max; set => StateOwner.Max = value; }

    public bool HasValidationError => StateOwner.HasValidationError;

    public bool IsDirty => StateOwner.IsDirty;

    public string? Diagnostic => StateOwner.Diagnostic;

    public void SetText(string text) => StateOwner.SetText(text);

    public void SelectAll() => StateOwner.SelectAll();

    public void Copy() => StateOwner.Copy();

    public void Cut() => StateOwner.Cut();

    public bool Paste() => StateOwner.Paste();

    public bool Undo() => StateOwner.Undo();

    public bool Redo() => StateOwner.Redo();

    public void Initialize(double value) => StateOwner.Initialize(value);

    public bool TryCommit() => StateOwner.TryCommit();

    public bool TryCommitText(string text) => StateOwner.TryCommitText(text);

    public void CancelEdit() => StateOwner.CancelEdit();

    public bool Increment(double step = 1) => StateOwner.Increment(step);

    public bool Decrement(double step = 1) => StateOwner.Decrement(step);
}
