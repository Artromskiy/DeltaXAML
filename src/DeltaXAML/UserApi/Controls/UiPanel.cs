using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Convenience retained panel for code-authored composition.</summary>
public class UiPanel : UiElement
{
    public UiPanel() : base(Retained.UiPanelGenerated.Create(), null) { }

    internal UiPanel(Retained.Panel element) : base(element, null) { }

    public void Add(UiElement child) => this.AddChild(child);

    public bool Remove(UiElement child) => this.RemoveChild(child);
}
