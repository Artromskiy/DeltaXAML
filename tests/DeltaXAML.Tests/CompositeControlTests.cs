using Delta;
using Delta.XAML;
using Delta.XAML.Contract;

internal static class CompositeControlTests
{
    public static void Run()
    {
        HighPolicyControlsSharePrimitiveTree();
        FocusTraversalStaysInsideNearestScope();
    }

    private static void HighPolicyControlsSharePrimitiveTree()
    {
        var picker = new UiPicker();
        Assert.True(picker.Children.Count == 2 && ReferenceEquals(picker.Children[1], picker.Overlay), "picker composes header and overlay in the canonical retained tree");
        Assert.True(ReferenceEquals(picker.Items.ItemsHost, picker.Items.Viewport.Content), "collection view composes one ScrollViewer and ItemsControl");
        picker.IsOpen = true;
        Assert.True(picker.Overlay.IsOpen && picker.Overlay.Participation == UiParticipation.All, "opening a picker enables its same-document overlay subtree");
        picker.IsOpen = false;
        Assert.Equal(UiParticipation.None, picker.Overlay.Participation, "closing a picker removes overlay participation without creating another document");

        var tabs = new UiTabView();
        Assert.Equal(2, tabs.Children.Count, "tabs compose a headers presenter and content host");
        var menu = new UiMenu();
        Assert.True(menu.IsFocusScope && ReferenceEquals(menu.Children[0], menu.ItemsHost), "menu composition reuses items presentation and focus scope primitives");
    }

    private static void FocusTraversalStaysInsideNearestScope()
    {
        var root = new UiPanel { Width = 100, Height = 100 };
        var outside = new UiButton { Width = 100, Height = 100 };
        var overlay = new UiOverlay { Width = 100, Height = 100 };
        var first = new UiButton { Width = 100, Height = 100 };
        var second = new UiButton { Width = 100, Height = 100 };
        overlay.Add(first);
        overlay.Add(second);
        root.Add(outside);
        root.Add(overlay);
        using var text = new EmptyTextService();
        using var document = new UiDocument(root, text);
        document.Layout(new float2(100, 100), 1);
        document.Dispatch(UiInputEvent.FromPointingDevice(new(
            UiPointerEventKind.ButtonDown,
            UiPointerDeviceKind.Mouse,
            1,
            new float2(10, 10),
            default,
            default,
            UiPointerButton.Primary,
            default,
            0,
            default)));
        document.Layout(new float2(100, 100), 1);
        Assert.True(second.RetainedElement.IsFocused, "topmost overlay child receives focus");

        document.Dispatch(UiInputEvent.FromKey(new(UiKeyEventKind.Down, new UiPhysicalKey(9), default, default, false)));
        document.Layout(new float2(100, 100), 1);
        Assert.True(first.RetainedElement.IsFocused, "Tab wraps within the nearest focus scope");
        Assert.True(!outside.RetainedElement.IsFocused, "focus traversal does not escape the active overlay scope");
    }
}
