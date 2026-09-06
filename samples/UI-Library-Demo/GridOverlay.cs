using Delta.XAML;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal static class GridOverlay
{
    private const int GridColumns = 24;
    private const int GridRows = 16;
    private static readonly UiColor _lineColor = new(30, 33, 38, 255);

    internal static void Update(UiItemsControl verticalLines, UiItemsControl horizontalLines)
    {
        EnsureLines(verticalLines, GridColumns, true);
        EnsureLines(horizontalLines, GridRows, false);
    }

    private static void EnsureLines(UiItemsControl host, int count, bool vertical)
    {
        while (host.Children.Count > count)
        {
            host.Remove(host.Children[^1]);
        }

        while (host.Children.Count < count)
        {
            host.Add(vertical
                ? new UiBorder
                {
                    Width = 1,
                    Background = _lineColor,
                    HorizontalAlignment = UiHorizontalAlignment.Center,
                }
                : new UiBorder
                {
                    Height = 1,
                    Background = _lineColor,
                    VerticalAlignment = UiVerticalAlignment.Center,
                });
        }

        host.SetGridDimensions(vertical ? count : 1, vertical ? 1 : count);
    }
}
