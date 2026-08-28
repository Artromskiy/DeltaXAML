using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

public sealed class UiOverlay : UiElement
{
    private Retained.Overlay StateOwner => (Retained.Overlay)RetainedElement;

    public UiOverlay() : base(Retained.UiOverlayGenerated.Create(), null) { }

    internal UiOverlay(Retained.Overlay element) : base(element, null) { }

    public bool IsOpen { get => StateOwner.IsOpen; set => StateOwner.IsOpen = value; }

    public void Add(UiElement child) => this.AddChild(child);

    public bool Remove(UiElement child) => this.RemoveChild(child);
}
