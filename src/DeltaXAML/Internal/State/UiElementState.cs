namespace DeltaXAML.Internal;

internal struct UiElementState
{
    public float Width;
    public float Height;
    public UiColor Background;
    public UiColor BorderColor;
    public float BorderWidth;
    public Delta.XAML.Contract.PaintUnits BorderWidthUnits;
    public Delta.XAML.UiCornerRadii CornerRadius;
    public UiThickness Padding;
    public bool Fill;
    public bool IsEnabled;
    public bool IsSelected;
    public Guid CustomVisualType;
    public Guid CustomVisualResource;
    public UiColor CustomVisualColor;
    public int GridRow;
    public int GridColumn;
    public int GridRowSpan;
    public int GridColumnSpan;
    public byte GridPlacementFlags;
    public ulong GestureBits;
    public Guid CommandId;
    public uint CommandPhysicalKey;
    public ulong CommandModifiers;
    public bool IsFocusScope;
    public int CollectionIndex;
    public bool HasCollectionIndex;
}
