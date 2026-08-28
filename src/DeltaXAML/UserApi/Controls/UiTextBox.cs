using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Convenience retained text editor for code-authored composition.</summary>
public class UiTextBox : UiElement
{
    private Retained.TextBox StateOwner => (Retained.TextBox)RetainedElement;

    public UiTextBox() : base(Retained.UiTextBoxGenerated.Create(), null) { }

    internal UiTextBox(Retained.TextBox element) : base(element, null) { }

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

    public void SetText(string text) => StateOwner.SetText(text);

    public void SelectAll() => StateOwner.SelectAll();

    public void Copy() => StateOwner.Copy();

    public void Cut() => StateOwner.Cut();

    public bool Paste() => StateOwner.Paste();

    public bool Undo() => StateOwner.Undo();

    public bool Redo() => StateOwner.Redo();
}
