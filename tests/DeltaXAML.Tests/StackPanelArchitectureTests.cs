using DeltaXAML.Internal;

internal static class StackPanelArchitectureTests
{
    public static void Run()
    {
        var state = new StackPanelState { Orientation = UiOrientation.Vertical };
        var first = new TextBlock { Width = 40, Height = 10 };
        var second = new TextBlock { Width = 60, Height = 20 };
        var fill = new TextBlock { Fill = true };
        IUiElement[] children = [first, second, fill];

        UiStackPanelGenerated.Measure(ref state, new(new(200, 100), 1, children));
        Assert.Equal(new UiSize(60, 47.5f), state.DesiredSize, "typed StackPanel measure uses child desired sizes");
        UiStackPanelGenerated.Arrange(ref state, new(new(0, 0, 200, 100), new(0, 0, 200, 100), children));
        Assert.Equal(new UiRect(0, 0, 200, 10), first.Bounds, "vertical StackPanel keeps the first child size");
        Assert.Equal(new UiRect(0, 10, 200, 20), second.Bounds, "vertical StackPanel advances the cursor");
        Assert.Equal(new UiRect(0, 30, 200, 70), fill.Bounds, "vertical StackPanel distributes remaining space");
        Assert.Equal(state.Bounds, new UiRect(0, 0, 200, 100), "typed arrange stores the parent bounds");

        Assert.True(UiStackPanelGenerated.Descriptor.IsValid, "StackPanel descriptor has a valid compact identity");
        Assert.True(UiStackPanelGenerated.Descriptor.Supports(UiDescriptorCapabilities.Factory | UiDescriptorCapabilities.Measure | UiDescriptorCapabilities.Arrange), "StackPanel descriptor advertises layout capabilities");
        Assert.True(UiDescriptorCatalog.TryResolve(UiStackPanelGenerated.Descriptor.Index, out var resolved), "StackPanel descriptor resolves through the compact catalog");
        Assert.Equal(UiStackPanelGenerated.Descriptor, resolved, "StackPanel catalog preserves generated metadata");
        Assert.Equal((ushort)2, UiStackPanelGenerated.Descriptor.Index.Value, "StackPanel has the second compact descriptor index");

        var retained = UiStackPanelGenerated.Create();
        retained.Add(first);
        retained.Add(second);
        retained.Add(fill);
        retained.Measure(new(200, 100));
        retained.Arrange(new(0, 0, 200, 100));
        Assert.Equal(fill.Bounds, new UiRect(0, 30, 200, 70), "retained StackPanel dispatches layout through generated mixins");

        var before = retained.LayoutVersion;
        retained.Orientation = UiOrientation.Vertical;
        Assert.Equal(before, retained.LayoutVersion, "unchanged StackPanel orientation does not invalidate layout");
        retained.Orientation = UiOrientation.Horizontal;
        Assert.True(retained.LayoutVersion > before, "StackPanel orientation invalidates layout");
        retained.Measure(new(200, 100));
        retained.Arrange(new(0, 0, 200, 100));
        Assert.Equal(new UiRect(0, 0, 40, 100), first.Bounds, "horizontal StackPanel preserves the first child width");
        Assert.Equal(new UiRect(40, 0, 60, 100), second.Bounds, "horizontal StackPanel advances by child width");
        Assert.Equal(new UiRect(100, 0, 100, 100), fill.Bounds, "horizontal StackPanel distributes remaining width");
    }
}
