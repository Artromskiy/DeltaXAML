namespace DeltaXAML.Internal;

internal struct ImageState
{
    public Guid Resource;
    public Guid Placeholder;
    public Guid ErrorSource;
    public UiColor Tint;
    public float IntrinsicWidth;
    public float IntrinsicHeight;
    public byte Stretch;
    public byte Status;
    public UiSize DesiredSize;
    public UiRect Bounds;
    public UiRect Clip;
}
