using Delta;
using Delta.XAML;
using Delta.XAML.Contract;

internal static class GestureCommandTests
{
    private static readonly UiCommandId Activate = new(new Guid("D066D24D-11D7-47A9-84BE-8146B39DD256"));

    public static void Run()
    {
        TimedGestureArenaPublishesSemanticCommands();
        GestureArenaCoversDragSwipePinchAndCancel();
        DetachingCapturedTargetCancelsGestureCandidate();
        PhysicalKeyRoutesCommandFromFocus();
    }

    private static void TimedGestureArenaPublishesSemanticCommands()
    {
        var target = new UiButton
        {
            Width = 100,
            Height = 40,
            Command = Activate,
            Gestures = UiGestureKind.Tap | UiGestureKind.MultipleTap | UiGestureKind.LongPress,
        };
        using var text = new EmptyTextService();
        using var document = new UiDocument(target, text);
        document.Layout(new float2(100, 40), 1);

        DispatchPointer(document, UiPointerEventKind.ButtonDown, TimeSpan.FromMilliseconds(10));
        DispatchPointer(document, UiPointerEventKind.ButtonUp, TimeSpan.FromMilliseconds(30));
        document.Layout(new float2(100, 40), 1);
        Assert.Equal(1, document.SemanticCommands.Length, "a timed tap publishes one semantic command");
        Assert.Equal(UiSemanticActionKind.Tap, document.SemanticCommands[0].Action, "the gesture arena classifies a tap");
        Assert.Equal(Activate, document.SemanticCommands[0].Command, "gesture output carries the application command identity");

        DispatchPointer(document, UiPointerEventKind.ButtonDown, TimeSpan.FromMilliseconds(100));
        DispatchPointer(document, UiPointerEventKind.ButtonUp, TimeSpan.FromMilliseconds(120));
        document.Layout(new float2(100, 40), 1);
        Assert.Equal(2, document.SemanticCommands.Length, "the second tap publishes tap and multiple-tap semantics");
        Assert.Equal(UiSemanticActionKind.MultipleTap, document.SemanticCommands[1].Action, "multiple-tap arbitration uses host timestamps");
        Assert.Equal(2, document.SemanticCommands[1].Gesture.TapCount, "multiple-tap output carries its count");

        DispatchPointer(document, UiPointerEventKind.ButtonDown, TimeSpan.FromMilliseconds(1000));
        DispatchPointer(document, UiPointerEventKind.ButtonUp, TimeSpan.FromMilliseconds(1600));
        document.Layout(new float2(100, 40), 1);
        Assert.Equal(UiSemanticActionKind.LongPress, document.SemanticCommands[0].Action, "long press uses the host timestamp instead of a UI-owned clock");
    }

    private static void PhysicalKeyRoutesCommandFromFocus()
    {
        var target = new UiButton
        {
            Width = 100,
            Height = 40,
            Command = Activate,
            CommandKey = new(new UiPhysicalKey(13), new UiModifierState(UiModifierBits.Control)),
        };
        using var text = new EmptyTextService();
        using var document = new UiDocument(target, text);
        document.Layout(new float2(100, 40), 1);
        DispatchPointer(document, UiPointerEventKind.ButtonDown, TimeSpan.Zero);
        document.Dispatch(UiInputEvent.FromKey(new(
            UiKeyEventKind.Down,
            new UiPhysicalKey(13),
            default,
            new UiModifierState(UiModifierBits.Control),
            false)));
        document.Layout(new float2(100, 40), 1);

        Assert.True(document.SemanticCommands.Length != 0, "focused keyboard chord publishes a semantic command");
        Assert.Equal(UiSemanticActionKind.Command, document.SemanticCommands[^1].Action, "keyboard routing remains separate from text input");
        Assert.Equal(Activate, document.SemanticCommands[^1].Command, "keyboard command preserves the typed command identity");
    }

    private static void GestureArenaCoversDragSwipePinchAndCancel()
    {
        var target = new UiButton
        {
            Width = 120,
            Height = 80,
            Command = Activate,
            Gestures = UiGestureKind.Drag | UiGestureKind.Pan,
        };
        using var text = new EmptyTextService();
        using var document = new UiDocument(target, text);
        document.Layout(new float2(120, 80), 1);

        DispatchPointer(document, UiPointerEventKind.ButtonDown, TimeSpan.Zero, 1, 10, 10);
        DispatchPointer(document, UiPointerEventKind.Move, TimeSpan.FromMilliseconds(10), 1, 30, 20);
        DispatchPointer(document, UiPointerEventKind.ButtonUp, TimeSpan.FromMilliseconds(20), 1, 40, 20);
        document.Layout(new float2(120, 80), 1);
        Assert.Equal(UiSemanticActionKind.DragStarted, document.SemanticCommands[0].Action, "drag threshold starts one arena gesture");
        Assert.Equal(UiSemanticActionKind.DragDelta, document.SemanticCommands[1].Action, "pan publishes a retained delta");
        Assert.Equal(UiSemanticActionKind.DragCompleted, document.SemanticCommands[2].Action, "pointer release completes drag state");

        target.Gestures = UiGestureKind.Swipe;
        DispatchPointer(document, UiPointerEventKind.ButtonDown, TimeSpan.FromMilliseconds(30), 2, 10, 10);
        DispatchPointer(document, UiPointerEventKind.ButtonUp, TimeSpan.FromMilliseconds(40), 2, 60, 10);
        document.Layout(new float2(120, 80), 1);
        Assert.Equal(UiSemanticActionKind.Swipe, document.SemanticCommands[0].Action, "swipe arbitration remains separate from drag candidates");

        target.Gestures = UiGestureKind.Pinch;
        DispatchPointer(document, UiPointerEventKind.ButtonDown, TimeSpan.FromMilliseconds(50), 3, 20, 20);
        DispatchPointer(document, UiPointerEventKind.ButtonDown, TimeSpan.FromMilliseconds(50), 4, 60, 20);
        DispatchPointer(document, UiPointerEventKind.Move, TimeSpan.FromMilliseconds(60), 4, 80, 20);
        document.Layout(new float2(120, 80), 1);
        Assert.Equal(UiSemanticActionKind.Pinch, document.SemanticCommands[0].Action, "two active pointers publish one scale gesture without recognizer objects");
        Assert.True(document.SemanticCommands[0].Gesture.Scale > 1, "pinch payload carries the renderer-neutral scale");

        DispatchPointer(document, UiPointerEventKind.Cancel, TimeSpan.FromMilliseconds(70), 3, 20, 20);
        DispatchPointer(document, UiPointerEventKind.Cancel, TimeSpan.FromMilliseconds(70), 4, 80, 20);
        document.Layout(new float2(120, 80), 1);
        Assert.Equal(0, document.SemanticCommands.Length, "pointer cancel removes active candidates without publishing a stale gesture");
    }

    private static void DetachingCapturedTargetCancelsGestureCandidate()
    {
        var root = new UiPanel { Width = 100, Height = 40 };
        var target = new UiButton
        {
            Width = 100,
            Height = 40,
            Command = Activate,
            Gestures = UiGestureKind.Tap,
        };
        root.Add(target);
        using var text = new EmptyTextService();
        using var document = new UiDocument(root, text);
        document.Layout(new float2(100, 40), 1);
        DispatchPointer(document, UiPointerEventKind.ButtonDown, TimeSpan.Zero);
        document.Layout(new float2(100, 40), 1);
        root.Remove(target);
        document.Layout(new float2(100, 40), 1);
        DispatchPointer(document, UiPointerEventKind.ButtonUp, TimeSpan.FromMilliseconds(10));
        document.Layout(new float2(100, 40), 1);

        Assert.Equal(0, document.SemanticCommands.Length, "detaching a captured target cancels its arena candidate before release");
    }

    private static void DispatchPointer(UiDocument document, UiPointerEventKind kind, TimeSpan timestamp)
        => DispatchPointer(document, kind, timestamp, 1, 10, 10);

    private static void DispatchPointer(
        UiDocument document,
        UiPointerEventKind kind,
        TimeSpan timestamp,
        ulong pointerId,
        float x,
        float y)
    {
        var pointer = new UiPointerEvent(
            kind,
            UiPointerDeviceKind.Mouse,
            pointerId,
            new float2(x, y),
            default,
            default,
            kind is UiPointerEventKind.ButtonDown or UiPointerEventKind.ButtonUp ? UiPointerButton.Primary : UiPointerButton.None,
            default,
            0,
            default);
        document.Dispatch(new UiInputSample(UiInputEvent.FromPointingDevice(pointer), timestamp));
    }
}
