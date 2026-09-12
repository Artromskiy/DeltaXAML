using Retained = DeltaXAML.Internal;
using Delta.XAML.Contract;

namespace Delta.XAML;

/// <summary>Retained text element for code-authored composition.</summary>
public class UiTextBlock : UiElement
{
    private Retained.TextBlock TextElement => (Retained.TextBlock)RetainedElement;

    public UiTextBlock() : base(Retained.TextBlockGenerated.Create(), null) { }

    internal UiTextBlock(Retained.TextBlock element)
        : base(element, null) { }

    public string Text { get => TextElement.Text; set => TextElement.Text = value; }

    public string FontKey { get => TextElement.FontKey; set => TextElement.FontKey = value; }

    public float FontSize { get => TextElement.FontSize; set => TextElement.FontSize = value; }

    public UiColor Foreground
    {
        get => ToPublicColor(TextElement.Foreground);
        set => TextElement.Foreground = ToRetainedColor(value);
    }

    public UiBrush ForegroundBrush
    {
        get => TextElement.ForegroundBrush;
        set => TextElement.ForegroundBrush = value;
    }

    public UiColor StrokeColor { get => ToPublicColor(TextElement.StrokeColor); set => TextElement.StrokeColor = ToRetainedColor(value); }

    public float StrokeWidth { get => TextElement.StrokeWidth; set => TextElement.StrokeWidth = value; }

    public UiTextHorizontalAlignment HorizontalTextAlignment { get => TextElement.HorizontalTextAlignment; set => TextElement.HorizontalTextAlignment = value; }

    public UiTextVerticalAlignment VerticalTextAlignment { get => TextElement.VerticalTextAlignment; set => TextElement.VerticalTextAlignment = value; }

    public UiTextWrapping TextWrapping { get => TextElement.TextWrapping; set => TextElement.TextWrapping = value; }

    public UiTextTrimming TextTrimming { get => TextElement.TextTrimming; set => TextElement.TextTrimming = value; }

    public int MaxLines { get => TextElement.MaxLines; set => TextElement.MaxLines = value; }

    public float LineHeight { get => TextElement.LineHeight; set => TextElement.LineHeight = value; }

    public UiFontWeight FontWeight { get => TextElement.FontWeight; set => TextElement.FontWeight = value; }

    public UiFontStyle FontStyle { get => TextElement.FontStyle; set => TextElement.FontStyle = value; }

    public UiTextDecorations TextDecorations { get => TextElement.TextDecorations; set => TextElement.TextDecorations = value; }
}
