using Delta;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Generated;
using DeltaXaml.Tests;

internal static class FullCapabilityGeneratedTests
{
    internal static void Run()
    {
        var model = new FullCapabilityModel { Armed = true, Volume = 4 };
        using var text = new EmptyTextService();
        using var artifact = new FullCapabilityArtifact(model, text);
        artifact.Document.Layout(new float2(220, 240), 1);

        var surface = Find<UiPanel>(artifact, "Surface");
        Assert.Equal(UiBrushKind.LinearGradient, surface.BackgroundBrush.Kind, "generated brush resources use the canonical custom-visual path");
        var condition = Find<UiTextBlock>(artifact, "ConditionTarget");
        Assert.Equal(new UiColor(51, 204, 102), condition.Foreground, "a generated data condition writes the trigger precedence slot");
        Assert.Equal(1, artifact.Document.SemanticCommands.Length, "the data-condition action is deferred as one semantic command");
        var behavior = Find<UiTextBlock>(artifact, "BehaviorTarget");
        Assert.True(behavior.IsSelected, "the descriptor-bound behavior runs from generated inline state");
        var list = Find<UiCollectionView>(artifact, "List");
        Assert.Equal(5, list.ItemsHost.Children.Count, "viewport-driven realization uses visible rows plus bounded overscan");
        var firstRow = list.ItemsHost.Children[0];
        var picker = Find<UiPicker>(artifact, "Choice");
        Assert.Equal(3, picker.Items.ItemsHost.Children.Count, "picker reuses the same typed template presenter");
        Assert.True(picker.Header.Content is UiTextBlock { Text: "One" }, "generated picker presents its selected typed item without host composition");
        var action = Find<UiButton>(artifact, "Action");
        Assert.Equal(UiGestureKind.Tap | UiGestureKind.LongPress, action.Gestures, "gesture capabilities compile as descriptor data");
        Assert.True(action.Command.IsValid && action.IsFocusScope, "command identity and focus-scope policy compile without delegates");
        var contextMulti = Find<UiTextBlock>(artifact, "ContextMulti");
        Assert.Equal("True:4", contextMulti.Text, "multi bindings combine typed context and retained relation sources");
        model.Armed = false;
        artifact.Document.Layout(new float2(220, 240), 1);
        Assert.Equal("False:4", contextMulti.Text, "context notifications refresh a generated multi binding without string traversal");
        Assert.True(ReferenceEquals(firstRow, list.ItemsHost.Children[0]), "unrelated context updates preserve realized row identity");
        var volume = Find<UiSlider>(artifact, "Volume");
        var sliderBounds = volume.RetainedElement.Bounds;
        artifact.Document.Dispatch(UiInputEvent.FromPointingDevice(new(
            UiPointerEventKind.ButtonDown,
            UiPointerDeviceKind.Mouse,
            1,
            new float2(sliderBounds.X + sliderBounds.Width * 0.8f, sliderBounds.Y + sliderBounds.Height * 0.5f),
            default,
            default,
            UiPointerButton.Primary,
            default,
            0,
            default)));
        artifact.Document.Layout(new float2(220, 240), 1);
        Assert.True(Maths.Abs(model.Volume - 8d) < 0.001d, "generated slider two-way binding writes its typed source without a command adapter");
        var image = Find<UiImage>(artifact, "Picture");
        Assert.Equal(UiImageStretch.UniformToFill, image.Stretch, "image stretch compiles to typed retained state");
        Assert.True(image.Placeholder.IsValid && image.ErrorSource.IsValid, "image fallback resources remain renderer-neutral identities");

        for (var i = 0; i < 10; i++)
        {
            artifact.Document.Layout(new float2(220, 240), 1);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            artifact.Document.Layout(new float2(220, 240), 1);
        }

        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before, "unchanged full-capability stage pipeline allocates no replacement frame state");
    }

    private static T Find<T>(FullCapabilityArtifact artifact, string name)
        where T : UiElement
    {
        if (!artifact.TryFindName(name, out var element) || element is not T typed)
        {
            throw new InvalidOperationException($"Generated element '{name}' was not found as {typeof(T).Name}.");
        }

        return typed;
    }
}
