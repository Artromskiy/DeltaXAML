using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Retained text element for code-authored composition.</summary>
public class UiTextBlock : UiElement
{
    private Retained.TextBlock TextElement => (Retained.TextBlock)RetainedElement;

    public UiTextBlock() : base(Retained.UiTextBlockGenerated.Create(), null) { }

    internal UiTextBlock(Retained.TextBlock element)
        : base(element, null) { }

    public string Text { get => TextElement.Text; set => TextElement.Text = value; }

    public string FontKey { get => TextElement.FontKey; set => TextElement.FontKey = value; }

    public float FontSize { get => TextElement.FontSize; set => TextElement.FontSize = value; }

    public UiColor Foreground
    {
        get
        {
            var value = TextElement.Foreground;
            return new UiColor(value.R, value.G, value.B, value.A);
        }
        set => TextElement.Foreground = new Retained.UiColor(value.R, value.G, value.B, value.A);
    }
}
