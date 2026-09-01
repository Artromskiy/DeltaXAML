namespace DeltaXAML.Internal;

internal struct TextBoxState
{
    public int CaretIndex;
    public int SelectionStart;
    public int SelectionLength;
    public int CompositionStart;
    public int CompositionSelectionStart;
    public int CompositionSelectionLength;
    public string? CompositionText;
    public string? CompositionDisplayText;
    public string PlaceholderText;
    public bool IsReadOnly;
    public bool AcceptsReturn;
    public int MaxLength;
}
