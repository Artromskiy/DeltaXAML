using DeltaXAML.Internal;
using Delta.XAML.Contract;

internal static class TextBlockArchitectureTests
{
    public static void Run()
    {
        var state = new TextBlockState
        {
            Text = "Hello",
            Visual = new TextBlockVisualState
            {
                FontKey = "default",
                GlyphRunKey = "hello-run",
                FontSize = 14,
                Foreground = new UiColor(1, 2, 3),
            },
        };

        TextBlockGenerated.Measure(ref state, new(new(320, 100), 1));
        Assert.True(state.Layout.DesiredSize.Width > 0 && state.Layout.DesiredSize.Height > 0, "typed TextBlock descriptor measures composite state");
        TextBlockGenerated.Arrange(ref state, new(new(10, 20, 100, 30), new(10, 20, 100, 30)));
        Assert.Equal(new UiRect(10, 20, 100, 30), state.Layout.Bounds, "typed TextBlock descriptor arranges composite state");

        var run = TextBlockGenerated.EmitVisual(
            ref state,
            new(new(7), 3, 1, 11));
        Assert.Equal(new UiElementId(7), run.Owner, "typed visual thunk preserves owner identity");
        Assert.Equal((uint)3, run.OwnerGeneration, "typed visual thunk preserves owner generation");
        Assert.Equal((uint)11, run.Version, "typed visual thunk preserves text version");
        Assert.Equal(state.Text, run.Text, "typed visual thunk emits state text");
        var noInput = new UiInputEvent();
        Assert.True(!TextBlockGenerated.ProcessInput(ref state, in noInput), "text block input capability is a stateless no-op");
        Assert.True(TextBlockGenerated.Descriptor.IsValid, "TextBlock descriptor has a valid compact identity");
        Assert.True(TextBlockGenerated.Descriptor.Supports(UiDescriptorCapabilities.Measure | UiDescriptorCapabilities.Arrange | UiDescriptorCapabilities.Input | UiDescriptorCapabilities.Visual), "TextBlock descriptor advertises all leaf capabilities");
        Assert.True(UiDescriptorCatalog.TryResolve(TextBlockGenerated.Descriptor.Index, out var resolved), "generated descriptor resolves through the compact catalog");
        Assert.Equal(TextBlockGenerated.Descriptor, resolved, "descriptor catalog preserves generated metadata");
        Assert.True(!UiDescriptorCatalog.TryResolve(new UiRuntimeTypeIndex(0), out _), "descriptor catalog rejects an invalid index");
        Assert.Equal((ushort)1, TextBlockGenerated.Descriptor.Index.Value, "TextBlock has a compact descriptor index");
        Assert.True((TextBlockGenerated.Descriptor.Capabilities & UiDescriptorCapabilities.PropertySetters) != 0, "TextBlock descriptor exposes typed property setters");
        Assert.True((TextBlockGenerated.Descriptor.Capabilities & UiDescriptorCapabilities.Arrange) != 0, "TextBlock descriptor exposes typed arrange capability");
        Assert.True((TextBlockGenerated.Descriptor.Capabilities & UiDescriptorCapabilities.Input) != 0, "TextBlock descriptor exposes typed input capability");
        Assert.True(TextBlockGenerated.TrySetText(ref state, "Updated"), "typed text setter changes state");
        Assert.True(!TextBlockGenerated.TrySetText(ref state, "Updated"), "typed text setter skips unchanged state");

        var retained = new TextBlock { Text = "Retained" };
        RetainedLayoutTest.Layout(retained, new(320, 100), new(4, 5, 120, 24));
        Assert.Equal(new UiRect(4, 5, 120, 24), retained.Bounds, "retained TextBlock uses the typed arrange thunk");
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                new UiRuntimeTypeIndex(1),
                retained,
                new(retained.Id, retained.Generation, retained.LayoutScale, retained.TextRunVersion),
                out var retainedRun),
            "retained TextBlock uses the typed visual thunk");
        Assert.Equal(retained.Bounds, retainedRun.Bounds, "typed visual state carries arranged bounds");
    }
}
