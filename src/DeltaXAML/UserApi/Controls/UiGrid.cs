using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Retained grid facade with fixed, auto and star definitions.</summary>
public sealed class UiGrid : UiElement
{
    private Retained.Grid StateOwner => (Retained.Grid)RetainedElement;

    public UiGrid() : base(Retained.UiGridGenerated.Create(), null) { }

    internal UiGrid(Retained.Grid element) : base(element, null) { }

    public void Add(UiElement child) => this.AddChild(child);

    public bool Remove(UiElement child) => this.RemoveChild(child);

    public void SetColumns(params UiGridLength[] columns) => StateOwner.SetColumns(ConvertGridLengths(columns));

    public void SetRows(params UiGridLength[] rows) => StateOwner.SetRows(ConvertGridLengths(rows));
}
