using Delta.XAML;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal static class GridOverlay
{
    private const int VerticalLineCount = 20;
    private const int HorizontalLineCount = 27;
    private static readonly UiColor LineColor = new(255, 255, 255, 13);

    internal static void Populate(UiItemsControl verticalLines, UiItemsControl horizontalLines)
    {
        ArgumentNullException.ThrowIfNull(verticalLines);
        ArgumentNullException.ThrowIfNull(horizontalLines);

        AddVerticalLines(verticalLines);
        AddHorizontalLines(horizontalLines);
    }

    private static void AddVerticalLines(UiItemsControl host)
    {
        for (var index = 0; index < VerticalLineCount; index++)
        {
            host.Add(new UiBorder
            {
                Width = 1,
                Background = LineColor,
                HorizontalAlignment = UiHorizontalAlignment.End,
            });
        }

        host.SetGridDimensions(VerticalLineCount, 1);
    }

    private static void AddHorizontalLines(UiItemsControl host)
    {
        for (var index = 0; index < HorizontalLineCount; index++)
        {
            host.Add(new UiBorder
            {
                Height = 1,
                Background = LineColor,
                VerticalAlignment = UiVerticalAlignment.End,
            });
        }

        host.SetGridDimensions(1, HorizontalLineCount);
    }
}
