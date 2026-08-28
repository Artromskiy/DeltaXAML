using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

public sealed class UiRichTextBlock : UiElement
{
    private Retained.RichTextBlock StateOwner => (Retained.RichTextBlock)RetainedElement;

    public UiRichTextBlock() : base(Retained.UiRichTextGenerated.Create(), null) { }

    internal UiRichTextBlock(Retained.RichTextBlock element) : base(element, null) { }

    public ReadOnlyMemory<UiTextSpan> Spans
    {
        get => StateOwner.Spans;
        set => StateOwner.Spans = value;
    }
}
