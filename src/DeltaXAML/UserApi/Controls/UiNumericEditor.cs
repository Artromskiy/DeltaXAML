using Retained = DeltaXAML.Internal;
using Delta.XAML.Contract;

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

    public UiColor OutlineColor { get => ToPublicColor(StateOwner.OutlineColor); set => StateOwner.OutlineColor = ToRetainedColor(value); }

    public float OutlineWidth { get => StateOwner.OutlineWidth; set => StateOwner.OutlineWidth = value; }

    public UiResourceId TextEffect { get => new(StateOwner.TextEffectResource); set => StateOwner.TextEffect = value; }

    public UiTextHorizontalAlignment HorizontalTextAlignment { get => StateOwner.HorizontalTextAlignment; set => StateOwner.HorizontalTextAlignment = value; }

    public UiTextVerticalAlignment VerticalTextAlignment { get => StateOwner.VerticalTextAlignment; set => StateOwner.VerticalTextAlignment = value; }

    public UiTextWrapping TextWrapping { get => StateOwner.TextWrapping; set => StateOwner.TextWrapping = value; }

    public UiTextTrimming TextTrimming { get => StateOwner.TextTrimming; set => StateOwner.TextTrimming = value; }

    public int MaxLines { get => StateOwner.MaxLines; set => StateOwner.MaxLines = value; }

    public float LineHeight { get => StateOwner.LineHeight; set => StateOwner.LineHeight = value; }

    public UiFontWeight FontWeight { get => StateOwner.FontWeight; set => StateOwner.FontWeight = value; }

    public UiFontStyle FontStyle { get => StateOwner.FontStyle; set => StateOwner.FontStyle = value; }

    public UiTextDecorations TextDecorations { get => StateOwner.TextDecorations; set => StateOwner.TextDecorations = value; }

    public string PlaceholderText { get => StateOwner.PlaceholderText; set => StateOwner.PlaceholderText = value; }

    public bool IsReadOnly { get => StateOwner.IsReadOnly; set => StateOwner.IsReadOnly = value; }

    public bool AcceptsReturn { get => StateOwner.AcceptsReturn; set => StateOwner.AcceptsReturn = value; }

    public int MaxLength { get => StateOwner.MaxLength; set => StateOwner.MaxLength = value; }

    public int CaretIndex => StateOwner.CaretIndex;

    public int SelectionStart => StateOwner.SelectionStart;

    public int SelectionLength => StateOwner.SelectionLength;

    public int SelectionEnd => StateOwner.SelectionEnd;

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

    public void SetSelection(int start, int length) => StateOwner.SetSelection(start, length);

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
