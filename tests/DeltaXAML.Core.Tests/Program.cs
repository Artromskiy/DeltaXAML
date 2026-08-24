using System.Runtime.InteropServices;
using DeltaXAML.Abstractions;
using DeltaXAML.Core;

using UiDirtyFlags = DeltaXAML.Abstractions.UiDirtyMask;

static class Assert
{
    public static void True(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: {expected} != {actual}");
        }
    }
}

sealed class FakeClipboard : IUiClipboard
{
    public string? Text { get; private set; }
    public string? ReadText() => Text;
    public void SetText(string? text) => Text = text;
    public bool HasText => !string.IsNullOrEmpty(Text);
}

sealed class EventProbe : UiElement, IUiRoutedEventSink
{
    public List<UiRoutedEvent> Events { get; } = [];
    public void OnRoutedEvent(in UiRoutedEvent routedEvent) => Events.Add(routedEvent);
}

sealed class CustomBadge : Border
{
    public override string TypeName => "CustomBadge";
}

internal static class Program
{
    public static void Main()
    {
        PropertyInvalidation();
        GridSizing();
        TextClipboardUndoAndValidation();
        PointerFocusAndDispatch();
        ScrollAndClips();
        TextRunStabilityAndDelta();
        StorageReuse();
        HandlesCompiledBindingsAndCustomTypes();
        ResourceLookupDiagnostics();
        ResourceBackedPrecedenceAndXaml();
        FrameContractAndBatchedMutations();
    }

    private static void PropertyInvalidation()
    {
        Assert.True(typeof(IUiElement).GetProperty("Id") is not null, "element identity remains in the base contract");
        Assert.True(typeof(IUiElement).GetProperty("Generation") is not null, "element generation remains in the base contract");
        Assert.True(typeof(IUiElement).GetProperty("DirtyFlags") is null, "dirty flags are not part of the external element contract");
        Assert.True(typeof(UiElement).GetProperty("DirtyFlags") is null, "dirty flags are internal to the retained implementation");
        var element = new UiElement();
        element.Measure(new(100, 100));
        element.Arrange(new(0, 0, 100, 100));
        element.SetLocal("Width", 42, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        element.Measure(new(100, 100));
        element.Arrange(new(0, 0, 100, 100));
        element.SetStyle("Color", new UiColor(1, 2, 3), UiDirtyFlags.Visual);
        element.SetDefault("Priority", "default", UiDirtyFlags.Visual);
        element.SetStyle("Priority", "style", UiDirtyFlags.Visual);
        element.SetBinding("Priority", new UiBindingValue(() => "binding", _ => (true, null)), UiDirtyFlags.Visual);
        element.SetLocal("Priority", "local", UiDirtyFlags.Visual);
        Assert.True(element.TryGet("Priority", out var priority) && Equals(priority.UntypedValue, "local"), "local value wins precedence");
        element.SetHandle("Priority", "transient", UiDirtyFlags.Visual);
        Assert.True(element.TryGet("Priority", out priority) && Equals(priority.UntypedValue, "transient"), "transient handle wins precedence");
        var binding = new UiBindingValue(() => 17, _ => (true, null));
        element.SetBinding("Value", binding, UiDirtyFlags.Visual);
        element.Measure(new(100, 100));
        element.Arrange(new(0, 0, 100, 100));
        binding.NotifyChanged();
        Assert.True(element.TryGet("Value", out var value) && Equals(value.UntypedValue, 17), "binding refreshes the retained value");
        var button = new Button();
        button.SetStyle("Button", null, UiDirtyFlags.Visual);
        Assert.True(button.VisualState.State == UiVisualState.Normal, "button starts normal");
        button.SetHovered(true);
        Assert.True(button.VisualState.State == UiVisualState.Hover, "hover state");
        button.SetPressed(true);
        Assert.True(button.VisualState.State == UiVisualState.Pressed, "pressed state");
        button.SetFocused(true);
        Assert.True(button.VisualState.IsFocused, "focused state data");
    }

    private static void HandlesCompiledBindingsAndCustomTypes()
    {
        var element = new UiElement();
        var handle = element.GetHandle("Value");
        Assert.True(element.TrySet(handle, 42, UiDirtyFlags.Binding, out var error) && error is null, "property handle writes value");
        Assert.True(element.TryGet("Value", out var value) && Equals(value.UntypedValue, 42), "handle value is retained");
        var stale = new UiPropertyHandle(element.Id, handle.Generation + 1, "Value");
        Assert.True(!element.TrySet(stale, 7, UiDirtyFlags.Binding, out error) && error is not null, "stale handle is rejected");
        var backing = 3;
        var binding = new UiCompiledBinding<int>(() => backing, v => { backing = v; return (true, null); }, UiBindingMode.TwoWay);
        element.SetBinding("Count", binding, UiDirtyFlags.Binding);
        Assert.True(binding.TryWrite(9, out error) && backing == 9, "compiled binding writes typed value");
        var registry = new XamlTypeRegistry();
        registry.Register("CustomBadge", () => new CustomBadge());
        var loaded = XamlLoader.Load("<CustomBadge Padding=\"1,1,1,1\" />", registry);
        Assert.True(loaded.Success && loaded.Root is CustomBadge, "registered custom type loads");
        var roleLoaded = XamlLoader.Load("<Button AutomationRole=\"button\" />");
        if (!roleLoaded.Success || roleLoaded.Root is not Button roleButton)
        {
            throw new InvalidOperationException("Closed automation role did not parse.");
        }

        Assert.Equal(UiAutomationRole.Button, roleButton.AutomationRole, "automation role is typed");
        var unknownKind = new UiPropertySchema("Transform", "Unknown", "Unknown", "System.Object", UiEditorKind.Unknown);
        Assert.Equal(UiEditorKind.Unknown, unknownKind.EditorKind, "unknown editor kind is explicit");
    }

    private static void GridSizing()
    {
        var grid = new Grid();
        grid.SetColumns(GridLength.Fixed(40), GridLength.Auto, GridLength.Star());
        grid.SetRows(GridLength.Fixed(20));
        var a = new Panel { Width = 30, Height = 20 };
        var b = new Panel { Width = 50, Height = 20 };
        var c = new Panel { Fill = true };
        grid.Add(a);
        grid.Add(b);
        grid.Add(c);
        grid.Measure(new(200, 20));
        grid.Arrange(new(0, 0, 200, 20));
        Assert.Equal(new UiRect(0, 0, 40, 20), a.Bounds, "grid fixed");
        Assert.Equal(new UiRect(40, 0, 50, 20), b.Bounds, "grid auto");
        Assert.Equal(new UiRect(90, 0, 110, 20), c.Bounds, "grid star");
    }

    private static void TextClipboardUndoAndValidation()
    {
        var clipboard = new FakeClipboard();
        var box = new TextBox { Width = 100, Height = 30, Focusable = true, Clipboard = clipboard };
        box.SetText("abc", false);
        box.SelectAll();
        box.Copy();
        Assert.Equal("abc", clipboard.Text, "copy");
        box.SetText("z", false);
        box.Paste();
        Assert.Equal("zabc", box.Text, "paste");
        box.Undo();
        Assert.Equal("z", box.Text, "undo");
        box.Redo();
        Assert.Equal("zabc", box.Text, "redo");
        box.SelectAll();
        box.Cut();
        Assert.Equal("", box.Text, "cut");

        var numeric = new NumericEditor { Min = 1, Max = 10 };
        numeric.Initialize(3);
        numeric.SetText("99", false);
        Assert.True(!numeric.TryCommit(), "numeric validation fails");
        Assert.Equal("99", numeric.Text, "invalid text retained");
        Assert.Equal(3d, numeric.Value, "invalid commit preserves value");
        Assert.True(numeric.HasValidationError, "inline diagnostic set");
        numeric.CancelEdit();
        Assert.Equal("3", numeric.Text, "cancel restores commit");
        numeric.Increment();
        Assert.Equal(4d, numeric.Value, "increment");
        numeric.Decrement();
        Assert.Equal(3d, numeric.Value, "decrement");
    }

    private static void PointerFocusAndDispatch()
    {
        var root = new Panel();
        var probe = new EventProbe { Focusable = true, Width = 100, Height = 30 };
        var box = new TextBox { Width = 100, Height = 30, Focusable = true };
        root.Add(probe);
        root.Add(box);
        var button = new Button { Width = 100, Height = 30 };
        root.Add(button);
        var frame = new UiFrame(root);
        frame.Layout(new(100, 90), 1);
        var clicked = false;
        button.Click += (_, _) => clicked = true;
        frame.Input.Focus(box.Id);
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiTextInput("12")));
        Assert.Equal("12", box.Text, "UTF text is separate input");
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiKeyEvent(8, true)));
        Assert.Equal("1", box.Text, "physical backspace");
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiPointerEvent(UiPointerEventKind.Down, new(10, 65), 1)));
        if (frame.Input.Captured is not { } captured)
        {
            throw new InvalidOperationException("Pointer capture was not set.");
        }

        Assert.Equal(button.Id, captured, "pointer capture");
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiPointerEvent(UiPointerEventKind.Up, new(10, 65), 1)));
        Assert.True(clicked, "button bubble click");
        Assert.True(frame.Input.Focused == button.Id, "pointer focuses control");
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiKeyEvent(9, true)));
        Assert.True(frame.Input.Focused == probe.Id || frame.Input.Focused == box.Id, "tab focus traversal");
    }

    private static void ScrollAndClips()
    {
        var scroll = new ScrollViewer { Width = 100, Height = 40 };
        var content = new StackPanel { Height = 120 };
        content.Add(new Panel { Width = 100, Height = 60, Background = new(255, 0, 0) });
        content.Add(new Panel { Width = 100, Height = 60, Background = new(0, 255, 0) });
        scroll.Content = content;
        var frame = new UiFrame(scroll);
        frame.Layout(new(100, 40), 1);
        scroll.ScrollBy(0, 20);
        frame.Layout(new(100, 40), 1);
        var list = frame.ExtractDrawList(new UiFrameContext(new(100, 40), 1, 1));
        Assert.Equal(2, list.Commands.Length, "scroll retains both commands");
        Assert.Equal(0, list.TextRuns.Length, "plain scrolling content has no text runs");
        foreach (var command in list.Commands.Span)
        {
            Assert.True(command.Clip.IsInside(new UiRect(0, 0, 100, 40)), "scroll clips to viewport");
        }
    }

    private static void TextRunStabilityAndDelta()
    {
        var root = new Panel { Width = 200, Height = 40 };
        var first = new TextBlock { Text = "first", Width = 100, Height = 20 };
        var second = new TextBlock { Text = "second", Width = 100, Height = 20 };
        root.Add(first);
        root.Add(second);
        var frame = new UiFrame(root);
        frame.Layout(new(200, 40), 1);
        var initial = frame.ExtractDrawList(new UiFrameContext(new(200, 40), 1, 1));
        var initialRuns = initial.TextRuns.ToArray();
        var initialVersion = initial.Version;
        Assert.True(MemoryMarshal.TryGetArray(initial.TextRuns, out ArraySegment<UiTextRun> initialTextStorage), "text request backing array");
        Assert.Equal("default", initialRuns[0].FontKey, "text request font key");
        Assert.Equal("first", initialRuns[0].Text, "text request content");
        Assert.Equal(first.Id, initialRuns[0].Owner, "text request owner");
        for (var index = 0; index < 20; index++)
        {
            frame.Layout(new(200, 40), 1);
            var next = frame.ExtractDrawList(new UiFrameContext(new(200, 40), 1, (uint)(index + 2)));
            Assert.Equal(initialVersion, next.Version, "unchanged frame keeps draw-list version");
            Assert.Equal(initialRuns.Length, next.TextRuns.Length, "unchanged frame keeps text run count");
            Assert.True(MemoryMarshal.TryGetArray(next.TextRuns, out ArraySegment<UiTextRun> nextTextStorage) && ReferenceEquals(initialTextStorage.Array, nextTextStorage.Array), "unchanged frame reuses text backing array");
            var unchangedDelta = next.GetDeltaSince(initialVersion);
            Assert.Equal(0, unchangedDelta.Commands.Count, "unchanged frame has no command delta");
            Assert.Equal(0, unchangedDelta.Clips.Count, "unchanged frame has no clip delta");
            Assert.Equal(0, unchangedDelta.TextRuns.Count, "unchanged frame has no text delta");
            for (var runIndex = 0; runIndex < next.TextRuns.Length; runIndex++)
            {
                Assert.Equal(initialRuns[runIndex].Version, next.TextRuns.Span[runIndex].Version, "unchanged frame keeps text version");
                Assert.Equal(initialRuns[runIndex].Owner, next.TextRuns.Span[runIndex].Owner, "unchanged frame keeps text owner");
            }
        }

        first.Foreground = new UiColor(200, 210, 220);
        frame.Layout(new(200, 40), 1);
        var styled = frame.ExtractDrawList(new UiFrameContext(new(200, 40), 1, 22));
        var styleDelta = styled.GetDeltaSince(initialVersion);
        Assert.Equal(new UiDrawRange(0, 1), styleDelta.TextRuns, "style update changes one text range");
        Assert.True(styled.TextRuns.Span[0].Version != initialRuns[0].Version, "style update changes the owning text version");
        Assert.Equal(initialRuns[1].Version, styled.TextRuns.Span[1].Version, "style update keeps the other text version");
        initialRuns = styled.TextRuns.ToArray();
        initialVersion = styled.Version;

        first.Text = "changed";
        frame.Layout(new(200, 40), 1);
        var changed = frame.ExtractDrawList(new UiFrameContext(new(200, 40), 1, 23));
        var changedVersion = changed.Version;
        Assert.True(changed.TextRuns.Span[0].Version != initialRuns[0].Version, "changed text updates its version");
        Assert.Equal(initialRuns[1].Version, changed.TextRuns.Span[1].Version, "unchanged text keeps its version");
        var valueDelta = changed.GetDeltaSince(initialVersion);
        Assert.Equal(new UiDrawRange(0, 1), valueDelta.TextRuns, "value update changes one text range");
        Assert.Equal(new UiDrawRange(0, 0), valueDelta.Clips, "value update does not change clips");

        root.Width = 240;
        frame.Layout(new(240, 40), 1);
        var layoutChanged = frame.ExtractDrawList(new UiFrameContext(new(240, 40), 1, 24));
        var layoutChangedVersion = layoutChanged.Version;
        var layoutDelta = layoutChanged.GetDeltaSince(changedVersion);
        Assert.Equal(new UiDrawRange(0, layoutChanged.Clips.Length), layoutDelta.Clips, "layout update changes the clip ranges");
        Assert.Equal(new UiDrawRange(0, 2), layoutDelta.TextRuns, "layout update changes positioned text ranges");
        Assert.Equal(changed.TextRuns.Span[0].Version, layoutChanged.TextRuns.Span[0].Version, "layout update preserves first text identity");
        Assert.Equal(changed.TextRuns.Span[1].Version, layoutChanged.TextRuns.Span[1].Version, "layout update preserves second text identity");

        frame.Layout(new(240, 40), 2);
        var dpiChanged = frame.ExtractDrawList(new UiFrameContext(new(240, 40), 2, 25));
        var dpiDelta = dpiChanged.GetDeltaSince(layoutChangedVersion);
        Assert.Equal(new UiDrawRange(0, 2), dpiDelta.TextRuns, "DPI update changes both text layout requests");
    }

    private static void StorageReuse()
    {
        var root = new Panel { Width = 20, Height = 20, Background = new(1, 2, 3) };
        var frame = new UiFrame(root);
        frame.Layout(new(20, 20), 1);
        var list = frame.ExtractDrawList(new UiFrameContext(new(20, 20), 1, 1));
        var commands = list.Commands;
        Assert.True(MemoryMarshal.TryGetArray(commands, out ArraySegment<UiDrawCommand> first), "command backing array");
        frame.ExtractDrawList(new UiFrameContext(new(20, 20), 1, 2));
        Assert.True(MemoryMarshal.TryGetArray(list.Commands, out ArraySegment<UiDrawCommand> second), "second command backing array");
        Assert.True(ReferenceEquals(first.Array, second.Array), "draw storage stable after warmup");
        Assert.Equal(1, list.Commands.Length, "draw count stable");
        for (var index = 0; index < 3; index++)
        {
            frame.Layout(new(20, 20), 1);
            frame.ExtractDrawList(new UiFrameContext(new(20, 20), 1, (uint)index));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 20; index++)
        {
            frame.Layout(new(20, 20), 1);
            frame.ExtractDrawList(new UiFrameContext(new(20, 20), 1, (uint)index));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"warm frame allocated {allocated} bytes");
    }

    private static void ResourceLookupDiagnostics()
    {
        var resources = new UiResourceStore();
        resources.Set("Color.Text", new UiColor(1, 2, 3));
        resources.Set("Color.Alias", new UiResourceReference("Color.Text"));
        Assert.True(resources.TryResolve("Color.Alias", out var resolved, out var diagnostic) && resolved is UiColor && diagnostic is null, "resource alias resolves");
        Assert.True(!resources.TryResolve("Color.Missing", out _, out diagnostic) && diagnostic?.Contains("not found", StringComparison.Ordinal) == true, "missing resource has diagnostic");
        resources.Set("Cycle.A", new UiResourceReference("Cycle.B"));
        resources.Set("Cycle.B", new UiResourceReference("Cycle.A"));
        Assert.True(!resources.TryResolve("Cycle.A", out _, out diagnostic) && diagnostic?.Contains("cycle", StringComparison.OrdinalIgnoreCase) == true, "resource cycle has diagnostic");
    }

    private static void ResourceBackedPrecedenceAndXaml()
    {
        var resources = new UiResourceStore();
        resources.Set("Color.Text", new UiColor(10, 20, 30));
        resources.Set("Color.Alias", new UiResourceReference("Color.Text"));
        var element = new TextBlock { Text = "Value" };
        element.SetDefault("Value", "default", UiDirtyFlags.Visual);
        element.SetStyleResource("Value", resources, new("Color.Alias"), UiDirtyFlags.Visual);
        var bindingValue = "binding";
        var binding = new UiBindingValue(() => bindingValue, _ => (true, null));
        element.SetBinding("Value", binding, UiDirtyFlags.Visual);
        element.SetLocal("Value", "local", UiDirtyFlags.Visual);
        element.SetHandle("Value", "handle", UiDirtyFlags.Visual);
        Assert.True(element.TryGet("Value", out var value) && Equals(value.UntypedValue, "handle"), "precedence reaches handle");
        bindingValue = "latest binding";
        binding.NotifyChanged();
        element.Clear("Value", UiValueSource.Handle);
        Assert.True(element.TryGet("Value", out value) && Equals(value.UntypedValue, "local"), "clear handle reveals local");
        element.Clear("Value", UiValueSource.Local);
        Assert.True(element.TryGet("Value", out value) && Equals(value.UntypedValue, "latest binding"), "clear local reveals latest hidden binding");
        element.Clear("Value", UiValueSource.Binding);
        Assert.True(element.TryGet("Value", out value) && value.Source == UiValueSource.Style, "clear binding reveals resource style");
        element.Clear("Value", UiValueSource.Style);
        Assert.True(element.TryGet("Value", out value) && Equals(value.UntypedValue, "default"), "clear style reveals default");

        var loaded = XamlLoader.LoadFrame("<Panel><TextBlock Text=\"Label\" ForegroundResource=\"Color.Text\" /></Panel>", resources);
        Assert.True(loaded.Success && loaded.Frame?.Root.Children[0] is TextBlock, "XAML resource fixture loads");
        var root = (Panel)loaded.Frame!.Root;
        var first = (TextBlock)root.Children[0];
        var second = new TextBlock { Text = "Other" };
        root.Add(second);
        var frame = loaded.Frame;
        frame.Layout(new(200, 40), 1);
        var before = frame.ExtractDrawList(new UiFrameContext(new(200, 40), 1, 1));
        var beforeVersion = before.Version;
        resources.Set("Color.Text", new UiColor(40, 50, 60));
        var unchangedAssignment = frame.ExtractDrawList(new UiFrameContext(new(200, 40), 1, 2));
        Assert.True(unchangedAssignment.Version != beforeVersion, "dependent resource change invalidates output");
        Assert.Equal(new UiDrawRange(0, 1), unchangedAssignment.GetDeltaSince(beforeVersion).TextRuns, "resource change has bounded text delta");
        resources.Set("Color.Text", new UiColor(40, 50, 60));
        var unchanged = frame.ExtractDrawList(new UiFrameContext(new(200, 40), 1, 3));
        Assert.Equal(unchangedAssignment.Version, unchanged.Version, "unchanged resource assignment has no draw delta");
        Assert.Equal(new UiColor(40, 50, 60), first.Foreground, "resource style updates the consuming property");
        Assert.Equal(new UiColor(255, 255, 255), second.Foreground, "unrelated property remains unchanged");
    }

    private static void FrameContractAndBatchedMutations()
    {
        var text = new TextBlock { Width = 100, Height = 20, Focusable = true };
        var other = new TextBlock { Width = 100, Height = 20 };
        var root = new Panel();
        root.Add(text);
        root.Add(other);
        var frame = new UiFrame(root);
        var handle = text.GetHandle("Text");
        var otherHandle = other.GetHandle("Text");
        var schema = new UiPropertySchema("Transform", "X", "X", "System.Single", UiEditorKind.Numeric);
        var schemaValue = new UiSchemaValue(schema, 3f, UiValueSource.Binding, 1);
        Assert.Equal(UiEditorKind.Numeric, schemaValue.Schema.EditorKind, "schema/value record");
        frame.Enqueue(new UiMutation(handle, "value", UiDirtyFlags.Binding | UiDirtyFlags.Visual));
        frame.Enqueue(new UiMutation(otherHandle, "other", UiDirtyFlags.Binding | UiDirtyFlags.Visual));
        Assert.Equal(2, frame.PendingMutationCount, "multiple mutations are queued");
        frame.ApplyMutations();
        Assert.Equal(2, frame.AppliedMutationCount, "queued mutations applied");
        Assert.Equal(0, frame.RejectedMutationCount, "valid mutation accepted");
        Assert.True(text.TryGet("Text", out var value) && Equals(value.UntypedValue, "value") && value.Source == UiValueSource.Handle, "handle source retained");
        Assert.True(other.TryGet("Text", out var otherValue) && Equals(otherValue.UntypedValue, "other"), "second mutation targets second element");
        var stale = new UiPropertyHandle(text.Id, text.Generation + 1, "Text");
        var empty = new UiPropertyHandle(text.Id, text.Generation, string.Empty);
        Assert.True(!text.TrySet(otherHandle, "foreign", UiDirtyFlags.Visual, out var error) && error is not null, "foreign handle is rejected");
        Assert.True(!text.TrySet(empty, "empty", UiDirtyFlags.Visual, out error) && error is not null, "empty property name is rejected");
        var emptyFactoryRejected = false;
        try
        {
            text.GetHandle(string.Empty);
        }
        catch (ArgumentException)
        {
            emptyFactoryRejected = true;
        }

        Assert.True(emptyFactoryRejected, "empty property handle cannot be created");
        frame.Enqueue(new UiMutation(stale, "stale", UiDirtyFlags.Visual));
        frame.ApplyMutations();
        Assert.Equal(0, frame.AppliedMutationCount, "stale mutation not applied");
        Assert.Equal(1, frame.RejectedMutationCount, "stale mutation rejected");
        frame.Enqueue(new UiMutation(otherHandle, "removed", UiDirtyFlags.Visual));
        root.Remove(other);
        frame.ApplyMutations();
        Assert.Equal(0, frame.AppliedMutationCount, "removed element mutation not applied");
        Assert.Equal(1, frame.RejectedMutationCount, "removed element mutation rejected");
        frame.Layout(new(100, 20), 1);
        var draw = frame.ExtractDrawList(new UiFrameContext(new(100, 20), 1, 1));
        Assert.Equal(new UiRect(0, 0, 100, 20), text.Bounds, "frame layout boundary");
        Assert.True(draw.TextRuns.Length == 1 && draw.TextRuns.Span[0].Owner == text.Id, "frame draw boundary retains owner");
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiPointerEvent(UiPointerEventKind.Down, new(10, 10), 1)));
        if (frame.Input.Focused is not { } focused || frame.Input.Captured is not { } captured)
        {
            throw new InvalidOperationException("Frame input did not focus and capture element.");
        }

        Assert.Equal(text.Id, focused, "frame input focuses element");
        Assert.Equal(text.Id, captured, "frame input captures pointer");
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiPointerEvent(UiPointerEventKind.Up, new(10, 10), 1)));
        Assert.True(frame.Input.Captured is null, "frame input releases capture");
    }
}
