namespace Delta.XAML;

/// <summary>Platform-neutral clipboard boundary; the host owns OS integration.</summary>
public interface IUiClipboard
{
    string? ReadText();
    void SetText(string? text);
    bool HasText { get; }
}

/// <summary>Small in-memory clipboard useful for tests and headless hosts.</summary>
public sealed class UiClipboard : IUiClipboard
{
    public string? Text { get; private set; }
    public bool HasText => !string.IsNullOrEmpty(Text);
    public string? ReadText() => Text;
    public void SetText(string? text) => Text = text;
}
