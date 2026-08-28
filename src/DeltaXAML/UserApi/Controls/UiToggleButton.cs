using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Retained toggle button with checked state and a neutral click callback.</summary>
public sealed class UiToggleButton : UiElement
{
    private Retained.ToggleButton StateOwner => (Retained.ToggleButton)RetainedElement;

    public UiToggleButton() : base(Retained.UiToggleButtonGenerated.Create(), null) { }

    internal UiToggleButton(Retained.ToggleButton element) : base(element, null) { }

    public UiElement? Content => Children.Count == 0 ? null : Children[0];

    public bool IsChecked => StateOwner.IsChecked;

    public event EventHandler? Click
    {
        add => StateOwner.Click += value;
        remove => StateOwner.Click -= value;
    }

    public void SetContent(UiElement? content) => this.SetSingleChild(content);
}
