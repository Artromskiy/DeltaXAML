using Delta;
using Delta.XAML;

namespace DeltaXaml.Samples.UiLibraryDemo;

// Equal item slots over a rounded-up extent keep the grid square at any viewport size.
// Templates own the line controls; resize changes only counts and host dimensions.
internal sealed class GridOverlay(
    UiCollectionView vertical, UiCollectionView horizontal,
    GridLines columns, GridLines rows, int pitch)
{
    internal void Resize(float width, float height)
    {
        var columnCount = Maths.Max(1, (int)Maths.Ceil(width / pitch));
        var rowCount = Maths.Max(1, (int)Maths.Ceil(height / pitch));
        columns.Resize(columnCount);
        rows.Resize(rowCount);
        vertical.Width = columnCount * pitch;
        horizontal.Height = rowCount * pitch;
        vertical.ItemsHost.SetGridDimensions(columnCount, 1);
        horizontal.ItemsHost.SetGridDimensions(1, rowCount);
    }
}
