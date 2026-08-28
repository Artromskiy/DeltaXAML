using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Vertical or horizontal retained stack panel.</summary>
public sealed class UiStackPanel : UiElement
{
    private Retained.StackPanel StateOwner => (Retained.StackPanel)RetainedElement;

    public UiStackPanel() : base(Retained.UiStackPanelGenerated.Create(), null) { }

    internal UiStackPanel(Retained.StackPanel element) : base(element, null) { }

    public UiOrientation Orientation
    {
        get => (UiOrientation)StateOwner.Orientation;
        set => StateOwner.Orientation = (Retained.UiOrientation)value;
    }

    public void Add(UiElement child) => this.AddChild(child);

    public bool Remove(UiElement child) => this.RemoveChild(child);
}
