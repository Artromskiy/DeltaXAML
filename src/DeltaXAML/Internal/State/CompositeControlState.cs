namespace DeltaXAML.Internal;

internal struct OverlayState
{
    public PanelState Layout;
    public bool IsOpen;
}

internal struct CollectionViewState
{
    public ContentControlState Layout;
    public int SelectedIndex;
}

internal struct PickerState
{
    public PanelState Layout;
    public int SelectedIndex;
    public bool IsOpen;
}

internal struct TabViewState
{
    public PanelState Layout;
    public int SelectedIndex;
}

internal struct MenuState
{
    public StackPanelState Layout;
}
