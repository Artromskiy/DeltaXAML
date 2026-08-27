using DeltaXAML.Internal;

internal static class ButtonArchitectureTests
{
    public static void Run()
    {
        var state = new ButtonState();
        var target = new UiElementId(10);
        var down = new UiRoutedEvent(target, UiRoutedEventPhase.Bubble, UiPointerEventKind.Down, default);
        var up = new UiRoutedEvent(target, UiRoutedEventPhase.Bubble, UiPointerEventKind.Up, default);

        Assert.True(!UiButtonGenerated.Process(ref state, in down), "button down does not raise click");
        Assert.True(state.IsPressed, "button input mixin enters pressed state");
        Assert.True(UiButtonGenerated.Process(ref state, in up), "button up reports click");
        Assert.True(!state.IsPressed, "button input mixin leaves pressed state");

        var toggleState = new ToggleButtonState();
        UiToggleButtonGenerated.Process(ref toggleState, in down);
        Assert.True(!toggleState.IsChecked, "toggle remains unchanged on pointer down");
        UiToggleButtonGenerated.Process(ref toggleState, in up);
        Assert.True(toggleState.IsChecked, "toggle changes on pointer up");
        UiToggleButtonGenerated.Process(ref toggleState, in up);
        Assert.True(!toggleState.IsChecked, "toggle can be changed back");

        Assert.True(UiButtonGenerated.Descriptor.IsValid, "Button descriptor has a valid compact identity");
        Assert.True(UiToggleButtonGenerated.Descriptor.IsValid, "ToggleButton descriptor has a valid compact identity");
        Assert.True(UiButtonGenerated.Descriptor.Supports(UiDescriptorCapabilities.Factory | UiDescriptorCapabilities.Input), "Button descriptor advertises factory and input");
        Assert.True(UiDescriptorCatalog.TryResolve(UiToggleButtonGenerated.Descriptor.Index, out var descriptor), "ToggleButton descriptor resolves through the compact catalog");
        Assert.Equal(UiToggleButtonGenerated.Descriptor, descriptor, "ToggleButton catalog preserves generated metadata");
        Assert.Equal((ushort)7, UiButtonGenerated.Descriptor.Index.Value, "Button has the seventh compact descriptor index");
        Assert.Equal((ushort)8, UiToggleButtonGenerated.Descriptor.Index.Value, "ToggleButton has the eighth compact descriptor index");

        var retained = UiButtonGenerated.Create();
        var clicks = 0;
        retained.Click += (_, _) => clicks++;
        retained.OnRoutedEvent(in down);
        Assert.True(retained.IsPressed, "retained Button forwards pressed state from generated input");
        retained.OnRoutedEvent(in up);
        Assert.True(!retained.IsPressed && clicks == 1, "retained Button forwards the generated click");
    }
}
