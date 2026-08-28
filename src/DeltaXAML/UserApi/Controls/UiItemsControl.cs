using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Retained collection host that reuses rows when item identity is unchanged.</summary>
public sealed class UiItemsControl : UiElement
{
    private Retained.ItemsControl StateOwner => (Retained.ItemsControl)RetainedElement;

    public UiItemsControl() : base(Retained.UiItemsControlGenerated.Create(), null) { }

    internal UiItemsControl(Retained.ItemsControl element) : base(element, null) { }

    public IReadOnlyList<object?> Items => StateOwner.Items;

    public void Add(UiElement child) => this.AddChild(child);

    public bool Remove(UiElement child) => this.RemoveChild(child);

    public void SetItems(IReadOnlyList<object?> items, Func<object?, UiElement> factory) =>
        this.SetItemsCore(StateOwner, items, factory);
}
