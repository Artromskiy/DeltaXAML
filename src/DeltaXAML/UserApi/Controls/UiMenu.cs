using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

public sealed class UiMenu : UiElement
{
    public UiMenu() : base(Retained.UiMenuGenerated.Create(), null)
    {
        ItemsHost = new UiItemsControl();
        this.AddChild(ItemsHost);
    }

    internal UiMenu(Retained.Menu element) : base(element, null) => ItemsHost = (UiItemsControl)Children[0];

    public UiItemsControl ItemsHost { get; }
}
