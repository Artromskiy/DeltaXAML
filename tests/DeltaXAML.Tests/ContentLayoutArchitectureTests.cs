using DeltaXAML.Internal;

internal static class ContentLayoutArchitectureTests
{
    public static void Run()
    {
        var child = new TextBlock { Width = 20, Height = 10 };
        UiElement[] children = [child];
        var borderState = new BorderState
        {
            Padding = new UiThickness(4, 5, 6, 7),
        };

        UiBorderGenerated.Measure(ref borderState, new(new(100, 50), 1, children));
        Assert.Equal(new UiSize(30, 22), borderState.DesiredSize, "typed Border measure includes padding");
        UiBorderGenerated.Arrange(ref borderState, new(new(0, 0, 100, 50), new(0, 0, 100, 50), children));
        Assert.Equal(new UiRect(4, 5, 90, 38), child.Bounds, "typed Border arranges the child inside padding");
        Assert.Equal(new UiRect(0, 0, 100, 50), borderState.Bounds, "typed Border stores arranged bounds");

        var emptyBorderState = new BorderState
        {
            Padding = new UiThickness(2, 3, 4, 5),
        };
        UiBorderGenerated.Measure(ref emptyBorderState, new(new(100, 50), 1));
        Assert.Equal(new UiSize(6, 8), emptyBorderState.DesiredSize, "empty Border measures its padding");

        var contentState = new ContentControlState();
        UiContentControlGenerated.Measure(ref contentState, new(new(100, 50), 1, children));
        Assert.Equal(new UiSize(20, 10), contentState.DesiredSize, "typed ContentControl measures its content");
        UiContentControlGenerated.Arrange(ref contentState, new(new(1, 2, 30, 40), new(1, 2, 30, 40), children));
        Assert.Equal(new UiRect(1, 2, 30, 40), child.Bounds, "typed ContentControl fills its content bounds");

        Assert.True(UiBorderGenerated.Descriptor.IsValid, "Border descriptor has a valid compact identity");
        Assert.True(UiContentControlGenerated.Descriptor.IsValid, "ContentControl descriptor has a valid compact identity");
        Assert.True(UiDescriptorCatalog.TryResolve(UiBorderGenerated.Descriptor.Index, out var borderDescriptor), "Border descriptor resolves through the compact catalog");
        Assert.Equal(UiBorderGenerated.Descriptor, borderDescriptor, "Border catalog preserves generated metadata");
        Assert.True(UiDescriptorCatalog.TryResolve(UiContentControlGenerated.Descriptor.Index, out var contentDescriptor), "ContentControl descriptor resolves through the compact catalog");
        Assert.Equal(UiContentControlGenerated.Descriptor, contentDescriptor, "ContentControl catalog preserves generated metadata");
        Assert.Equal((ushort)3, UiBorderGenerated.Descriptor.Index.Value, "Border has the third compact descriptor index");
        Assert.Equal((ushort)4, UiContentControlGenerated.Descriptor.Index.Value, "ContentControl has the fourth compact descriptor index");

        var retainedBorder = UiBorderGenerated.Create();
        retainedBorder.Padding = new UiThickness(4, 5, 6, 7);
        retainedBorder.Add(child);
        retainedBorder.Measure(new(100, 50));
        retainedBorder.Arrange(new(0, 0, 100, 50));
        Assert.Equal(new UiRect(4, 5, 90, 38), child.Bounds, "retained Border dispatches through generated layout");

        var retainedContent = UiContentControlGenerated.Create();
        retainedContent.Add(child);
        retainedContent.Measure(new(100, 50));
        retainedContent.Arrange(new(1, 2, 30, 40));
        Assert.Equal(new UiRect(1, 2, 30, 40), child.Bounds, "retained ContentControl dispatches through generated layout");
    }
}
