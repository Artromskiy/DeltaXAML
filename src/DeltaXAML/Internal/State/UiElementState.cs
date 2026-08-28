namespace DeltaXAML.Internal;

internal struct UiElementState
{
    public float Width;
    public float Height;
    public UiColor Background;
    public UiThickness Padding;
    public bool Fill;
    public bool IsEnabled;
    public bool IsSelected;
    public Guid CustomVisualType;
    public Guid CustomVisualResource;
    public UiColor CustomVisualColor;
}
