namespace DeltaXAML.Internal;

internal struct GridState
{
    public GridLength[] Columns;
    public GridLength[] Rows;
    public float[] MeasuredColumns;
    public float[] MeasuredRows;
    public float[] ResolvedColumns;
    public float[] ResolvedRows;
    public UiSize DesiredSize;
    public UiRect Bounds;
    public UiRect Clip;
}
