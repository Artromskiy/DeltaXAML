namespace DeltaXAML.Internal;

internal struct UiElementState
{
    public float Width;
    public float Height;
    public UiThickness Margin;
    public Delta.XAML.UiHorizontalAlignment HorizontalAlignment;
    public Delta.XAML.UiVerticalAlignment VerticalAlignment;
    public UiColor Background;
    public UiColor BorderColor;
    public Delta.XAML.Contract.UiEffectSet EffectSet;
    public float BorderWidth;
    public Delta.XAML.Contract.PaintUnits BorderWidthUnits;
    public Delta.XAML.UiCornerRadii CornerRadius;
    public UiThickness Padding;
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
