using DeltaXAML.Internal;

internal static class TextBlockArchitectureTests
{
    public static void Run()
    {
        var state = new TextBlockState
        {
            Text = "Hello",
            FontKey = "default",
            GlyphRunKey = "hello-run",
            FontSize = 14,
            Foreground = new UiColor(1, 2, 3),
        };

        UiTextBlockGenerated.Measure(ref state, new(new(320, 100), 1));
        Assert.True(state.DesiredSize.Width > 0 && state.DesiredSize.Height > 0, "typed TextBlock descriptor measures composite state");

        var run = UiTextBlockGenerated.EmitVisual(
            ref state,
            new(new(7), 3, new(10, 20, 100, 30), new(10, 20, 100, 30), 1, state.GlyphRunKey, 11));
        Assert.Equal(new UiElementId(7), run.Owner, "typed visual thunk preserves owner identity");
        Assert.Equal((uint)3, run.OwnerGeneration, "typed visual thunk preserves owner generation");
        Assert.Equal((uint)11, run.Version, "typed visual thunk preserves text version");
        Assert.Equal(state.Text, run.Text, "typed visual thunk emits state text");
        Assert.Equal((ushort)1, UiTextBlockGenerated.Descriptor.Index.Value, "TextBlock has a compact descriptor index");
    }
}
