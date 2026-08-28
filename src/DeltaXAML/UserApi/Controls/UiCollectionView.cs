using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

public sealed class UiCollectionView : UiElement
{
    private Retained.CollectionView StateOwner => (Retained.CollectionView)RetainedElement;

    public UiCollectionView() : base(Retained.UiCollectionViewGenerated.Create(), null)
    {
        ItemsHost = new UiItemsControl();
        Viewport = new UiScrollViewer();
        Viewport.SetContent(ItemsHost);
        this.AddChild(Viewport);
    }

    internal UiCollectionView(Retained.CollectionView element) : base(element, null)
    {
        Viewport = (UiScrollViewer)Children[0];
        ItemsHost = Viewport.Content as UiItemsControl ??
            throw new InvalidOperationException("The retained collection view is missing its generated items host.");
    }

    public UiScrollViewer Viewport { get; }

    public UiItemsControl ItemsHost { get; }

    public int SelectedIndex { get => StateOwner.SelectedIndex; set => StateOwner.SelectedIndex = value; }
}
