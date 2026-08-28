using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Retained border with one optional child.</summary>
public sealed class UiBorder : UiElement
{
    public UiBorder() : base(Retained.UiBorderGenerated.Create(), null) { }

    internal UiBorder(Retained.Border element) : base(element, null) { }

    public UiElement? Child => Children.Count == 0 ? null : Children[0];

    public void SetChild(UiElement? child) => this.SetSingleChild(child);
}
