using Delta.XAML;
using Delta.XAML.Contract;

internal static partial class Program
{
    private static void GeneratedToggleButtonTest()
    {
        using var textService = new CountingTextService();
        using var artifact = new DeltaXaml.Generated.ToggleButtonArtifact(textService);
        Assert.True(artifact.Document.Root is UiToggleButton, "declared ToggleButton dialect emits its public typed control");
        artifact.Document.Layout(new(120, 32), 1);
        Assert.True(artifact.TryFindName("Toggle", out var element) && element is UiToggleButton, "generated ToggleButton participates in the namescope");
        if (element is not UiToggleButton toggle)
        {
            throw new InvalidOperationException("Generated ToggleButton missing.");
        }

        artifact.Document.Dispatch(UiInputEvent.FromPointingDevice(Pointer(UiPointerEventKind.ButtonDown)));
        artifact.Document.Dispatch(UiInputEvent.FromPointingDevice(Pointer(UiPointerEventKind.ButtonUp)));
        artifact.Document.Layout(new(120, 32), 1);
        Assert.True(toggle.IsChecked, "generated ToggleButton uses canonical routed input");
    }

    private static UiPointerEvent Pointer(UiPointerEventKind kind) => new(
        kind,
        UiPointerDeviceKind.Mouse,
        1,
        new(1, 1),
        default,
        default,
        UiPointerButton.Primary,
        new(1),
        0,
        default);
}
