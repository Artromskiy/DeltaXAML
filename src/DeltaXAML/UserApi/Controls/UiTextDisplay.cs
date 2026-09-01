using Retained = DeltaXAML.Internal;
using Delta.XAML.Contract;

namespace Delta.XAML;

/// <summary>Retained text element for code-authored composition.</summary>
public class TextBlock : UiElement
{
    private Retained.TextBlock TextElement => (Retained.TextBlock)RetainedElement;

    public TextBlock() : base(Retained.TextBlockGenerated.Create(), null) { }

    internal TextBlock(Retained.TextBlock element)
        : base(element, null) { }

    public string Text { get => TextElement.Text; set => TextElement.Text = value; }

    public string FontKey { get => TextElement.FontKey; set => TextElement.FontKey = value; }

    public float FontSize { get => TextElement.FontSize; set => TextElement.FontSize = value; }

    public UiColor Foreground
    {
        get => ToPublicColor(TextElement.Foreground);
        set => TextElement.Foreground = ToRetainedColor(value);
    }

    public UiColor OutlineColor { get => ToPublicColor(TextElement.OutlineColor); set => TextElement.OutlineColor = ToRetainedColor(value); }

    public float OutlineWidth { get => TextElement.OutlineWidth; set => TextElement.OutlineWidth = value; }

    public UiResourceId TextEffect { get => new(TextElement.TextEffectResource); set => TextElement.TextEffect = value; }

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
