using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Retained collection host that reuses rows when item identity is unchanged.</summary>
public sealed class UiItemsControl : UiElement
{
    private Retained.ItemsControl StateOwner => (Retained.ItemsControl)RetainedElement;

    public UiItemsControl() : base(Retained.UiItemsControlGenerated.Create(), null) { }

    internal UiItemsControl(Retained.ItemsControl element) : base(element, null) { }

    public IReadOnlyList<object?> Items => StateOwner.Items;

    /// <summary>Gets whether realized items use the equal-slot grid layout.</summary>
    public bool IsGridLayout => StateOwner.IsGridLayout;

    public void Add(UiElement child) => this.AddChild(child);

    public bool Remove(UiElement child) => this.RemoveChild(child);

    /// <summary>Uses equal star-sized slots for the realized items.</summary>
    public void SetGridDimensions(int columns, int rows) => StateOwner.SetGridDimensions(columns, rows);

    public void SetItems(IReadOnlyList<object?> items, Func<object?, UiElement> factory) =>
        this.SetItemsCore(StateOwner, items, factory);
}
