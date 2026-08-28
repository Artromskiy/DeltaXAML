using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Retained button control with a neutral click callback.</summary>
public class UiButton : UiElement
{
    private Retained.Button StateOwner => (Retained.Button)RetainedElement;

    public UiButton() : base(Retained.UiButtonGenerated.Create(), null) { }

    internal UiButton(Retained.Button element) : base(element, null) { }

    public UiElement? Content => Children.Count == 0 ? null : Children[0];

    public event EventHandler? Click
    {
        add => StateOwner.Click += value;
        remove => StateOwner.Click -= value;
    }

    public void SetContent(UiElement? content) => this.SetSingleChild(content);
}
