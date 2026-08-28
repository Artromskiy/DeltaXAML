using DeltaXAML.Internal;

internal static class PanelGridArchitectureTests
{
    public static void Run()
    {
        PanelUsesGeneratedLayout();
        GridUsesGeneratedLayoutAndReusableState();
    }

    private static void PanelUsesGeneratedLayout()
    {
        var first = new TextBlock { Width = 40, Height = 10 };
        var second = new TextBlock { Width = 60, Height = 20 };
        UiElement[] children = [first, second];
        var state = new PanelState();

        UiPanelGenerated.Measure(ref state, new(new(200, 100), 1, children));
        Assert.Equal(new UiSize(60, 20), state.DesiredSize, "typed Panel measures the largest child");
        UiPanelGenerated.Arrange(ref state, new(new(3, 4, 200, 100), new(3, 4, 200, 100), children));
        Assert.Equal(new UiRect(3, 4, 200, 100), state.Bounds, "typed Panel stores its arranged bounds");
        Assert.Equal(state.Bounds, first.Bounds, "typed Panel gives each child the panel bounds");
        Assert.Equal(state.Bounds, second.Bounds, "typed Panel gives every child the same slot");

        Assert.True(UiPanelGenerated.Descriptor.IsValid, "Panel descriptor has a valid compact identity");
        Assert.True(UiDescriptorCatalog.TryResolve(UiPanelGenerated.Descriptor.Index, out var descriptor), "Panel descriptor resolves through the compact catalog");
        Assert.Equal(UiPanelGenerated.Descriptor, descriptor, "Panel catalog preserves generated metadata");
        Assert.Equal((ushort)5, UiPanelGenerated.Descriptor.Index.Value, "Panel has the fifth compact descriptor index");

        var retained = UiPanelGenerated.Create();
        retained.Add(first);
        retained.Add(second);
        retained.Measure(new(200, 100));
        retained.Arrange(new(3, 4, 200, 100));
        Assert.Equal(new UiRect(3, 4, 200, 100), first.Bounds, "retained Panel dispatches layout through generated mixins");
    }

    private static void GridUsesGeneratedLayoutAndReusableState()
    {
        var columns = new[] { GridLength.Fixed(40), GridLength.Auto, GridLength.Star() };
        var rows = new[] { GridLength.Fixed(20) };
        var first = new TextBlock { Width = 30, Height = 20 };
        var second = new TextBlock { Width = 50, Height = 20 };
        var third = new TextBlock { Fill = true };
        UiElement[] children = [first, second, third];
        var state = NewGridState(columns, rows);

        UiGridGenerated.Measure(ref state, new(new(200, 100), 1, children));
        Assert.Equal(new UiSize(90, 20), state.DesiredSize, "typed Grid measures fixed and auto definitions");
        UiGridGenerated.Arrange(ref state, new(new(0, 0, 200, 20), new(0, 0, 200, 20), children));
        Assert.Equal(new UiRect(0, 0, 40, 20), first.Bounds, "Grid arranges the fixed column");
        Assert.Equal(new UiRect(40, 0, 50, 20), second.Bounds, "Grid arranges the auto column");
        Assert.Equal(new UiRect(90, 0, 110, 20), third.Bounds, "Grid distributes the remaining star space");

        Assert.True(UiGridGenerated.Descriptor.IsValid, "Grid descriptor has a valid compact identity");
        Assert.True(UiDescriptorCatalog.TryResolve(UiGridGenerated.Descriptor.Index, out var descriptor), "Grid descriptor resolves through the compact catalog");
        Assert.Equal(UiGridGenerated.Descriptor, descriptor, "Grid catalog preserves generated metadata");
        Assert.Equal((ushort)6, UiGridGenerated.Descriptor.Index.Value, "Grid has the sixth compact descriptor index");

        var retained = UiGridGenerated.Create();
        retained.SetColumns(columns);
        retained.SetRows(rows);
        var before = retained.LayoutVersion;
        retained.SetColumns(columns);
        Assert.Equal(before, retained.LayoutVersion, "unchanged Grid definitions do not invalidate layout");
        retained.SetColumns(new[] { GridLength.Fixed(60), GridLength.Auto, GridLength.Star() });
        Assert.True(retained.LayoutVersion > before, "changed Grid definitions invalidate layout");
    }

    private static GridState NewGridState(GridLength[] columns, GridLength[] rows) => new()
    {
        Columns = columns,
        Rows = rows,
        MeasuredColumns = new float[columns.Length],
        MeasuredRows = new float[rows.Length],
        ResolvedColumns = new float[Math.Max(1, columns.Length)],
        ResolvedRows = new float[Math.Max(1, rows.Length)],
    };
}
