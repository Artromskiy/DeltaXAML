using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

public sealed class UiPicker : UiElement
{
    private Retained.Picker StateOwner => (Retained.Picker)RetainedElement;

    public UiPicker() : base(Retained.UiPickerGenerated.Create(), null)
    {
        Header = new UiButton();
        Overlay = new UiOverlay { IsOpen = false };
        Items = new UiCollectionView();
        Overlay.Add(Items);
        this.AddChild(Header);
        this.AddChild(Overlay);
    }

    internal UiPicker(Retained.Picker element) : base(element, null)
    {
        Header = (UiButton)Children[0];
        Overlay = (UiOverlay)Children[1];
        Items = (UiCollectionView)Overlay.Children[0];
    }

    public UiButton Header { get; }

    public UiOverlay Overlay { get; }

    public UiCollectionView Items { get; }

    public int SelectedIndex { get => StateOwner.SelectedIndex; set => StateOwner.SelectedIndex = value; }

    public bool IsOpen
    {
        get => StateOwner.IsOpen;
        set
        {
            StateOwner.IsOpen = value;
            Overlay.IsOpen = value;
        }
    }
}
