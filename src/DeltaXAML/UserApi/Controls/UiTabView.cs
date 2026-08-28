using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

public sealed class UiTabView : UiElement
{
    private Retained.TabView StateOwner => (Retained.TabView)RetainedElement;

    public UiTabView() : base(Retained.UiTabViewGenerated.Create(), null)
    {
        Headers = new UiItemsControl();
        ContentHost = new UiContentControl();
        this.AddChild(Headers);
        this.AddChild(ContentHost);
    }

    internal UiTabView(Retained.TabView element) : base(element, null)
    {
        Headers = (UiItemsControl)Children[0];
        ContentHost = (UiContentControl)Children[1];
    }

    public UiItemsControl Headers { get; }

    public UiContentControl ContentHost { get; }

    public int SelectedIndex { get => StateOwner.SelectedIndex; set => StateOwner.SelectedIndex = value; }
}
