using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Retained content viewport with platform-neutral scrolling.</summary>
public sealed class UiScrollViewer : UiElement
{
    private Retained.ScrollViewer StateOwner => (Retained.ScrollViewer)RetainedElement;

    public UiScrollViewer() : base(Retained.UiScrollViewerGenerated.Create(), null) { }

    internal UiScrollViewer(Retained.ScrollViewer element) : base(element, null) { }

    public UiElement? Content => Children.Count == 0 ? null : Children[0];

    public float OffsetX => StateOwner.Offset.X;

    public float OffsetY => StateOwner.Offset.Y;

    public void SetContent(UiElement? content) => this.SetSingleChild(content);

    public void ScrollBy(float x, float y) => StateOwner.ScrollBy(x, y);
}
