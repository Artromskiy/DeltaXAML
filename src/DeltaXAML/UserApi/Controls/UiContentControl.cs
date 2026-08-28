using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Retained content host with one optional child.</summary>
public class UiContentControl : UiElement
{
    public UiContentControl() : base(Retained.UiContentControlGenerated.Create(), null) { }

    internal UiContentControl(Retained.ContentControl element) : base(element, null) { }

    public UiElement? Content => Children.Count == 0 ? null : Children[0];

    public void SetContent(UiElement? content) => this.SetSingleChild(content);
}
