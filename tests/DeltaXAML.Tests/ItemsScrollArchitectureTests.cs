using DeltaXAML.Internal;

internal static class ItemsScrollArchitectureTests
{
    public static void Run()
    {
        ItemsControlReusesRows();
        ScrollViewerUsesGeneratedLayout();
    }

    private static void ItemsControlReusesRows()
    {
        var items = new ItemsControl();
        items.SetItems(new object?[] { "one", "two" }, value => value is string text ? new TextBlock { Text = text } : throw new InvalidOperationException());
        var first = items.RealizedItems[0];
        var second = items.RealizedItems[1];
        items.SetItems(new object?[] { "one", "three" }, value => value is string text ? new TextBlock { Text = text } : throw new InvalidOperationException());

        Assert.True(ReferenceEquals(first, items.RealizedItems[0]), "ItemsControl preserves row identity for an unchanged item");
        Assert.True(!ReferenceEquals(second, items.RealizedItems[1]), "ItemsControl replaces only a changed row");
        Assert.Equal(2, items.RealizedItems.Count, "ItemsControl retains the changed collection size");
        Assert.Equal(2, items.Items.Count, "ItemsControl updates its source snapshot");
        Assert.True(UiItemsControlGenerated.Descriptor.IsValid, "ItemsControl descriptor has a valid compact identity");
        Assert.True(UiDescriptorCatalog.TryResolve(UiItemsControlGenerated.Descriptor.Index, out var descriptor), "ItemsControl descriptor resolves through the compact catalog");
        Assert.Equal(UiItemsControlGenerated.Descriptor, descriptor, "ItemsControl catalog preserves generated metadata");
        Assert.Equal((ushort)11, UiItemsControlGenerated.Descriptor.Index.Value, "ItemsControl has the eleventh compact descriptor index");
    }

    private static void ScrollViewerUsesGeneratedLayout()
    {
        var child = new Panel { Width = 200, Height = 80 };
        UiElement[] children = [child];
        var state = new ScrollViewerState { Offset = new UiPoint(10, 5) };
        RetainedLayoutTest.Measure(child, new(100, 50));
        UiScrollViewerGenerated.Measure(ref state, new(new(100, 50), 1, children));
        Assert.Equal(new UiSize(200, 80), state.DesiredSize, "typed ScrollViewer measures its content size");
        UiScrollViewerGenerated.Arrange(ref state, new(new(0, 0, 100, 50), new(0, 0, 100, 50), children));
        Assert.Equal(new UiRect(0, 0, 100, 50), state.Clip, "typed ScrollViewer keeps the viewport clip");

        var retained = UiScrollViewerGenerated.Create();
        retained.Content = child;
        RetainedLayoutTest.Layout(retained, new(100, 50), new(0, 0, 100, 50));
        retained.ScrollBy(10, 5);
        RetainedLayoutTest.Layout(retained, new(100, 50), new(0, 0, 100, 50));
        Assert.Equal(new UiPoint(10, 5), retained.Offset, "retained ScrollViewer updates offset through a generated setter");
        Assert.Equal(new UiRect(-10, -5, 200, 80), child.Bounds, "retained ScrollViewer dispatches offset layout through the generated path");
        Assert.True(UiScrollViewerGenerated.Descriptor.IsValid, "ScrollViewer descriptor has a valid compact identity");
        Assert.True(UiDescriptorCatalog.TryResolve(UiScrollViewerGenerated.Descriptor.Index, out var descriptor), "ScrollViewer descriptor resolves through the compact catalog");
        Assert.Equal(UiScrollViewerGenerated.Descriptor, descriptor, "ScrollViewer catalog preserves generated metadata");
        Assert.Equal((ushort)12, UiScrollViewerGenerated.Descriptor.Index.Value, "ScrollViewer has the twelfth compact descriptor index");
    }
}
