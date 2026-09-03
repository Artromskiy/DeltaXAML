using Delta.Maths;
using Delta.Text;
using DeltaXaml.Tests;
using DeltaXAML.Internal;
using Library = Delta.XAML;
using LibraryContract = Delta.XAML.Contract;
using TextContract = Delta.Text.Contract;

using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

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

    public static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}

internal static class RetainedLayoutTest
{
    internal static void Layout(UiElement root, UiSize available, UiRect bounds)
    {
        ArgumentNullException.ThrowIfNull(root);
        var nodes = new UiNodeStore(root);
        try
        {
            UiMeasureStage.Run(nodes, root, available, new UiMeasureQueueBuffer(), []);
            UiArrangeStage.Run(nodes, root, bounds, new UiArrangeQueueBuffer());
        }
        finally
        {
            nodes.Detach();
        }
    }

    internal static void Measure(UiElement root, UiSize available)
    {
        ArgumentNullException.ThrowIfNull(root);
        var nodes = new UiNodeStore(root);
        try
        {
            UiMeasureStage.Run(nodes, root, available, new UiMeasureQueueBuffer(), []);
        }
        finally
        {
            nodes.Detach();
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

sealed class EventProbe : UiElement { }

sealed class EmptyLibraryTypeResolver : Library.IXamlTypeResolver
{
    public bool TryResolveName(in Library.XamlQualifiedName name, out Library.UiTypeId type) { type = default; return false; }
    public bool TryCreate(Library.UiTypeId type, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Library.UiElement? element) { element = null; return false; }
}

sealed class EmptyLibraryResourceResolver : Library.IUiResourceResolver
{
    public bool TryResolve(LibraryContract.UiResourceId resource, out object? value) { value = null; return false; }
}

sealed class FixedLibraryResourceResolver(LibraryContract.UiResourceId resource, object? value) : Library.IUiResourceResolver
{
    public bool TryResolve(LibraryContract.UiResourceId requested, out object? resolved)
    {
        if (requested == resource)
        {
            resolved = value;
            return true;
        }

        resolved = null;
        return false;
    }
}

[Library.UiXamlType("urn:custom", "CustomBadge", "5B35E6E5-9B17-4F77-9BD4-63F25B89F7B5")]
sealed class LibraryCustomBadge : Library.UiElement
{
    [Library.UiXamlProperty("C851D40C-14E4-4B86-99B2-401A972342A9", Library.UiXamlValueKind.Text)]
    public string Label { get; set; } = string.Empty;

    [Library.UiXamlProperty("C851D40C-14E4-4B86-99B2-401A972342AA", Library.UiXamlValueKind.Brush)]
    public Library.UiBrush SurfaceBrush { get; set; }

    [Library.UiXamlProperty("C851D40C-14E4-4B86-99B2-401A972342AB", Library.UiXamlValueKind.ResourceId)]
    public LibraryContract.UiResourceId Texture { get; set; }

    [Library.UiXamlProperty("C851D40C-14E4-4B86-99B2-401A972342AC", Library.UiXamlValueKind.CornerRadii)]
    public Library.UiCornerRadii Radii { get; set; }
}

sealed class CustomLibraryTypeResolver : Library.IXamlTypeResolver
{
    private static readonly Library.UiTypeId BadgeType = new(new Guid("5B35E6E5-9B17-4F77-9BD4-63F25B89F7B5"));

    public bool TryResolveName(in Library.XamlQualifiedName name, out Library.UiTypeId type)
    {
        if (name.LocalName == "CustomBadge")
        {
            type = BadgeType;
            return true;
        }

        type = default;
        return false;
    }

    public bool TryCreate(Library.UiTypeId type, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Library.UiElement? element)
    {
        if (type == BadgeType)
        {
            element = new LibraryCustomBadge();
            return true;
        }

        element = null;
        return false;
    }
}

sealed class LabelTemplateFactory : Library.IUiTemplateFactory
{
    public Library.UiElement Create(Library.UiElement owner, Library.UiResourceCatalog resources) =>
        new Library.TextBlock { Text = "templated" };
}

sealed class ReplacementTemplateFactory : Library.IUiTemplateFactory
{
    public Library.UiElement Create(Library.UiElement owner, Library.UiResourceCatalog resources) =>
        new Library.TextBlock { Text = "replacement" };
}

sealed class UpperConverter : Library.IUiValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter) => value?.ToString()?.ToUpperInvariant();

    public bool TryConvertBack(object? value, Type sourceType, object? parameter, out object? result, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out Delta.Diagnostics.Diagnostic? diagnostic)
    {
        result = value?.ToString();
        diagnostic = null;
        return true;
    }
}

sealed class BindingResolver : Library.IUiBindingResolver
{
    private readonly UpperConverter _upper = new();

    public bool TryResolveConverter(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Library.IUiValueConverter? converter)
    {
        if (key == "Upper")
        {
            converter = _upper;
            return true;
        }

        converter = null;
        return false;
    }
}

sealed class EmptyTextService : TextContract.ITextService
{
    public TextContract.FontInstanceId OpenFont(in TextContract.FontOpenRequest request) => throw new NotSupportedException();
    public void CloseFont(TextContract.FontInstanceId font) { }
    public TextContract.FontMetrics GetFontMetrics(TextContract.FontInstanceId font, float pixelsPerEm) => throw new NotSupportedException();
    public TextContract.ShapedText Shape(in TextContract.TextShapeRequest request) => throw new NotSupportedException();
    public TextContract.GlyphImage GenerateGlyphImage(in TextContract.GlyphImageRequest request) => throw new NotSupportedException();
    public void Dispose() { }
}

sealed class CountingTextService : TextContract.ITextService
{
    private readonly SixLaborsTextService _inner = new();

    public int ShapeCount { get; private set; }

    public TextContract.FontInstanceId OpenFont(in TextContract.FontOpenRequest request) => _inner.OpenFont(in request);
    public void CloseFont(TextContract.FontInstanceId font) => _inner.CloseFont(font);
    public TextContract.FontMetrics GetFontMetrics(TextContract.FontInstanceId font, float pixelsPerEm) => _inner.GetFontMetrics(font, pixelsPerEm);
    public TextContract.ShapedText Shape(in TextContract.TextShapeRequest request)
    {
        ShapeCount++;
        return _inner.Shape(in request);
    }

    public TextContract.GlyphImage GenerateGlyphImage(in TextContract.GlyphImageRequest request) => _inner.GenerateGlyphImage(in request);
    public void Dispose() => _inner.Dispose();
}

sealed class CountingFontResolver(Library.UiFontCatalog catalog) : Library.IUiFontResolver
{
    public int ResolveCount { get; private set; }

    public bool TryResolve(string fontKey, out TextContract.FontOpenRequest request)
    {
        ResolveCount++;
        return catalog.TryResolve(fontKey, out request);
    }
}

internal static partial class Program
{
    private static LibraryContract.UiPointerEvent Pointer(
        LibraryContract.UiPointerEventKind kind,
        float x,
        float y,
        float wheelY = 0) =>
        new(
            kind,
            LibraryContract.UiPointerDeviceKind.Mouse,
            1,
            new(x, y),
            default,
            new(0, wheelY),
            kind is LibraryContract.UiPointerEventKind.ButtonDown or LibraryContract.UiPointerEventKind.ButtonUp
                ? LibraryContract.UiPointerButton.Primary
                : LibraryContract.UiPointerButton.None,
            default,
            0,
            default);

    private static LibraryContract.UiKeyEvent Key(uint physicalKey, ulong modifiers = 0) =>
        new(LibraryContract.UiKeyEventKind.Down, new(physicalKey), default, new(modifiers), false);

    public static void Main()
    {
        ArchitectureGate.Run();
        XamlSemanticCompilerTests();
        UnsupportedXamlTests();
        XamlGeneratorTests();
        TextBlockArchitectureTests.Run();
        StackPanelArchitectureTests.Run();
        ContentLayoutArchitectureTests.Run();
        PanelGridArchitectureTests.Run();
        ButtonArchitectureTests.Run();
        EditingArchitectureTests.Run();
        ItemsScrollArchitectureTests.Run();
        TypedCollectionTests.Run();
        AttachedPropertyTests.Run();
        RelationBindingTests.Run();
        GestureCommandTests.Run();
        ValueControlTests.Run();
        CompositeControlTests.Run();
        PlatformBoundaryTests.Run();
        ConditionTests.Run();
        BehaviorTests.Run();
        RichTextTests.Run();
        GeneratedCollectionTests.Run();
        GeneratedRelationTests.Run();
        GeneratedAttachedTests.Run();
        FullCapabilityGeneratedTests.Run();
        LayoutDiagnosticsTests.Run();
        TypedPropertyStateTests();
        PropertyPrecedenceTests();
        HiddenBindingTests();
        AnimationSourceTests();
        RejectedTypedSourceTests();
        PropertyInvalidation();
        EffectiveSourceInvalidation();
        GridSizing();
        TextClipboardUndoAndValidation();
        PointerFocusAndDispatch();
        PublicInputPreservesKeyModifiers();
        QueuedTextInputOwnsItsSnapshot();
        ImeFocusAndCaptureLifecycle();
        NestedHitAndDetachedInputCleanup();
        NumericEditorReceivesCanonicalInput();
        PublicWheelScrollsScrollViewer();
        ScrollAndClips();
        DocumentOwnsReusableDisplayListStorage();
        TextDisplayListUsesDeltaText();
        PaintPropertiesReachDisplayList();
        RoundedCornerRadiiAreNormalizedBeforeExtraction();
        DisplayListDirtySubtreeReusesStableText();
        TextCacheDropsRemovedNodes();
        TextVersionTracksTextInputsOnly();
        DpiInvalidatesLayoutWithoutCompoundingScale();
        CustomVisualsRemainNeutral();
        BorderWidthUnitsPropertyDescriptor();
        DisplayListOrderContract();
        PublicDisplayListWarmFrameHasNoAllocations();
        BindingExpressionsAndContexts();
        GeneratedEditorAndGameHostPaths();
        GeneratedToggleButtonTest();
        HandlesCompiledBindingsAndCustomTypes();
        TypedPropertyCatalog();
        RuntimeStagesAreOrdered();
        BindingTargetWritesReenterTheStagePipeline();
        BindingStageSkipsCleanSubtrees();
        ResourceLookupDiagnostics();
        ResourceBackedPrecedenceAndXaml();
        ResourceDependencyInvalidation();
        RuntimeResourceDiagnostics();
        PublicResourcesStylesTemplatesAndTypes();
        DocumentDisposesBindingSubscriptions();
        TypeCatalogUsesStableIds();
        LibraryApiSmoke();
        InterpretedLoaderTests.Run();
        FrameContractAndBatchedMutations();
        ParticipationBoundary();
        RuntimeLayoutQueuesAreNonRecursive();
        NodeStagesFollowStructuralMutations();
        NodeStoreHasSingleOwner();
        NodeStoreOwnsDetachedRoot();
        NodeStoreRejectsCrossDocumentReparenting();
        DisplayExtractionFollowsNodeLinksAfterTreeMutation();
        DescriptorLayoutDispatch();
        DescriptorPropertyDispatchIsNonVirtual();
        DescriptorLayoutEntryPointsAreExclusive();
        DisplayListBatcherWorkloadTests.Run();
        ControlStateOwnersAreFlat();
        CoordinateConventionTests.Run();
    }

    private static void PropertyInvalidation()
    {
        Assert.True(typeof(UiElement).GetProperty("Id") is not null, "element identity remains on the retained owner");
        Assert.True(typeof(UiElement).GetProperty("Generation") is not null, "element generation remains on the retained owner");
        Assert.True(typeof(UiElement).GetProperty("DirtyFlags") is null, "dirty flags are internal to the retained implementation");
        var defaultHandle = default(Library.UiPropertyHandle);
        Assert.True(!defaultHandle.IsValid && defaultHandle.Name.Length == 0, "default public handle has a safe empty name");
        var element = new UiElement();
        element.DirtyFlags = UiDirtyFlags.None;
        var firstDirtyVersion = element.OutputVersion;
        element.Invalidate(UiDirtyFlags.Visual);
        var coalescedDirtyVersion = element.OutputVersion;
        element.Invalidate(UiDirtyFlags.Visual);
        Assert.Equal(coalescedDirtyVersion, element.OutputVersion, "repeated visual invalidation coalesces before extraction");
        Assert.True(coalescedDirtyVersion > firstDirtyVersion, "first visual invalidation advances the output version");
        RetainedLayoutTest.Layout(element, new(100, 100), new(0, 0, 100, 100));
        element.SetLocal("Width", 42, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        RetainedLayoutTest.Layout(element, new(100, 100), new(0, 0, 100, 100));
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
        RetainedLayoutTest.Layout(element, new(100, 100), new(0, 0, 100, 100));
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

    private static void EffectiveSourceInvalidation()
    {
        var element = new UiElement();
        element.SetDefault("Value", 7, UiDirtyFlags.Visual);
        element.DirtyFlags = UiDirtyFlags.None;

        element.SetLocal("Value", 7, UiDirtyFlags.Visual);
        Assert.True((element.DirtyFlags & UiDirtyFlags.Visual) != 0, "an effective source change invalidates even when the value is equal");
        Assert.True(element.TryGet("Value", out var local) && local.Source == UiValueSource.Local, "local source becomes effective");

        element.DirtyFlags = UiDirtyFlags.None;
        element.SetLocal("Value", 7, UiDirtyFlags.Visual);
        Assert.Equal(UiDirtyFlags.None, element.DirtyFlags, "repeating the same effective source and value does not invalidate");

        element.Clear("Value", UiValueSource.Local);
        Assert.True((element.DirtyFlags & UiDirtyFlags.Visual) != 0, "clearing an override invalidates when the effective source changes");
        Assert.True(element.TryGet("Value", out var fallback) && fallback.Source == UiValueSource.Default, "clearing local reveals the default source");

        element.DirtyFlags = UiDirtyFlags.None;
        element.Clear("Value", UiValueSource.Handle);
        Assert.Equal(UiDirtyFlags.None, element.DirtyFlags, "clearing an absent source does not invalidate");
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
        var binding = new UiBindingValue(() => backing, value =>
        {
            if (value is not int next)
            {
                return (false, "Expected int.");
            }

            backing = next;
            return (true, null);
        });
        element.SetBinding("Count", binding, UiDirtyFlags.Binding);
        Assert.True(binding.TryWrite(9, out error) && backing == 9, "compiled binding writes typed value");
        var typedSource = new BindingModel { Name = "typed" };
        using var typedCompiled = new Library.UiCompiledBinding<BindingModel, string>(
            typedSource,
            static source => source.Name,
            static (source, value) => source.Name = value,
            Library.UiBindingMode.TwoWay);
        AssertTypedBinding(typedCompiled, typedSource);
    }

    private static void AssertTypedBinding<TBinding>(TBinding binding, BindingModel source)
        where TBinding : Library.IUiBinding<string>
    {
        Assert.Equal("typed", binding.ReadValue(), "compiled binding exposes typed reads");
        Assert.True(binding.TryWriteValue("updated", out var diagnostic) && diagnostic is null && source.Name == "updated", "compiled binding exposes typed writes");
    }

    private static void TypedPropertyCatalog()
    {
        var text = new Library.TextBlock();
        text.SetValue(Library.TextBlockProperties.Text, "typed text");
        text.SetValue(Library.TextBlockProperties.Foreground, new Library.UiColor(10, 20, 30));
        text.SetValue(Library.UiElementProperties.Width, 120f);
        Assert.Equal("typed text", text.GetValue(Library.TextBlockProperties.Text), "typed property catalog reads text");
        Assert.Equal(new Library.UiColor(10, 20, 30), text.GetValue(Library.TextBlockProperties.Foreground), "typed property catalog converts visual values");
        Assert.Equal(120f, text.GetValue(Library.UiElementProperties.Width), "typed property catalog writes common values");

        Assert.True(!text.TrySetValue(Library.TextBlockProperties.FontSize, "wrong", out var diagnostic) && diagnostic is not null, "typed property object path rejects incompatible values");
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
        RetainedLayoutTest.Layout(grid, new(200, 20), new(0, 0, 200, 20));
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
        var nonFocusableRoot = new Panel { Width = 40, Height = 20 };
        var nonFocusableRuntime = new UiRuntime(nonFocusableRoot);
        nonFocusableRuntime.Layout(new(40, 20), 1);
        nonFocusableRuntime.Input.Focus(nonFocusableRoot.Id);
        Assert.True(nonFocusableRuntime.Input.Focused is null, "explicit focus ignores non-focusable elements");
        var nonFocusableDown = Pointer(LibraryContract.UiPointerEventKind.ButtonDown, 5, 5);
        nonFocusableRuntime.Input.RoutePointer(in nonFocusableDown);
        Assert.True(nonFocusableRuntime.Input.Focused is null, "pointer focus ignores non-focusable elements");

        var root = new Panel();
        var probe = new EventProbe { Focusable = true, Width = 100, Height = 30 };
        var box = new TextBox { Width = 100, Height = 30, Focusable = true };
        root.Add(probe);
        root.Add(box);
        var button = new Button { Width = 100, Height = 30 };
        root.Add(button);
        var frame = new UiRuntime(root);
        frame.Layout(new(100, 90), 1);
        var clicked = false;
        button.Click += (_, _) => clicked = true;
        frame.Input.Focus(box.Id);
        Assert.True(box.IsFocused, "explicit focus updates the focused visual state");
        var typedText = new LibraryContract.UiTextInput("12".AsMemory());
        frame.Input.RouteText(in typedText);
        Assert.Equal(string.Empty, box.Text, "input routing does not bypass the mutation stage");
        frame.ApplyMutations();
        Assert.Equal("12", box.Text, "UTF text is separate input");
        var backspace = Key(8);
        frame.Input.RouteKey(in backspace);
        frame.ApplyMutations();
        Assert.Equal("1", box.Text, "physical backspace");
        var move = Pointer(LibraryContract.UiPointerEventKind.Move, 10, 65);
        frame.Input.RoutePointer(in move);
        Assert.True(button.IsHovered && !box.IsHovered, "pointer move updates only the current hovered visual state");
        var down = Pointer(LibraryContract.UiPointerEventKind.ButtonDown, 10, 65);
        frame.Input.RoutePointer(in down);
        Assert.True(!button.IsPressed, "pointer routing queues the control transition");
        if (frame.Input.Captured is not { } captured)
        {
            throw new InvalidOperationException("Pointer capture was not set.");
        }

        Assert.Equal(button.Id, captured, "pointer capture");
        frame.ApplyMutations();
        Assert.True(button.IsPressed, "mutation stage applies the queued pressed transition");
        var up = Pointer(LibraryContract.UiPointerEventKind.ButtonUp, 10, 65);
        frame.Input.RoutePointer(in up);
        frame.ApplyMutations();
        Assert.True(clicked, "button bubble click");
        Assert.True(!probe.IsHovered && !probe.IsFocused, "route does not include unrelated sibling nodes");
        Assert.True(frame.Input.Focused == button.Id, "pointer focuses control");
        Assert.True(button.IsFocused && !box.IsFocused, "pointer focus clears the previous focused visual state");
        var tab = Key(9);
        frame.Input.RouteKey(in tab);
        Assert.True(frame.Input.Focused == probe.Id || frame.Input.Focused == box.Id, "tab focus traversal");
        Assert.True(!button.IsFocused, "tab focus clears the previous focused visual state");

        var traversalRoot = new Panel();
        var disabled = new Button { IsEnabled = false };
        var hidden = new Button { Visibility = UiVisibility.Collapsed };
        var eligible = new TextBox { Width = 100, Height = 30 };
        traversalRoot.Add(disabled);
        traversalRoot.Add(hidden);
        traversalRoot.Add(eligible);
        var traversalRuntime = new UiRuntime(traversalRoot);
        traversalRuntime.Layout(new(100, 90), 1);
        traversalRuntime.Input.RouteKey(in tab);
        Assert.Equal(eligible.Id, traversalRuntime.Input.Focused, "tab traversal skips disabled and hidden controls");
    }

    private static void PublicInputPreservesKeyModifiers()
    {
        var root = new Library.UiStackPanel();
        var text = new Library.TextBox { Width = 100, Height = 20 };
        text.SetText("abc");
        root.Add(text);
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(root, textService);
        document.Layout(new Delta.Maths.float2(100, 20), 1);

        document.Dispatch(LibraryContract.UiInputEvent.FromPointingDevice(new LibraryContract.UiPointerEvent(
            LibraryContract.UiPointerEventKind.ButtonDown,
            LibraryContract.UiPointerDeviceKind.Mouse,
            1,
            new Delta.Maths.float2(10, 10),
            default,
            default,
            LibraryContract.UiPointerButton.Primary,
            new LibraryContract.UiPointerButtons(1),
            0,
            default)));
        document.Dispatch(LibraryContract.UiInputEvent.FromKey(new LibraryContract.UiKeyEvent(
            LibraryContract.UiKeyEventKind.Down,
            new LibraryContract.UiPhysicalKey(65),
            new LibraryContract.UiLogicalKey(65),
            new LibraryContract.UiModifierState(LibraryContract.UiModifierBits.Control),
            false)));
        document.Dispatch(LibraryContract.UiInputEvent.FromText(new LibraryContract.UiTextInput("z".AsMemory())));
        document.Layout(new Delta.Maths.float2(100, 20), 1);

        Assert.Equal("z", text.Text, "public input preserves Ctrl+A for text editing");
    }

    private static void QueuedTextInputOwnsItsSnapshot()
    {
        var text = new Library.TextBox { Width = 100, Height = 20 };
        var root = new Library.UiPanel();
        root.Add(text);
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(root, textService);
        document.Layout(new Delta.Maths.float2(100, 20), 1);
        document.Dispatch(LibraryContract.UiInputEvent.FromPointingDevice(new LibraryContract.UiPointerEvent(
            LibraryContract.UiPointerEventKind.ButtonDown,
            LibraryContract.UiPointerDeviceKind.Mouse,
            1,
            new(5, 5),
            default,
            default,
            LibraryContract.UiPointerButton.Primary,
            new(1),
            0,
            default)));

        var chars = new[] { 'A' };
        document.Dispatch(LibraryContract.UiInputEvent.FromText(new LibraryContract.UiTextInput(chars.AsMemory())));
        chars[0] = 'B';
        document.Layout(new Delta.Maths.float2(100, 20), 1);
        Assert.Equal("A", text.Text, "queued text input owns a snapshot until the next layout");

        var retainedText = new TextBox { Width = 100, Height = 20 };
        var retainedRuntime = new UiRuntime(retainedText);
        retainedRuntime.Layout(new(100, 20), 1);
        retainedRuntime.EnqueueInput(LibraryContract.UiInputEvent.FromPointingDevice(Pointer(
            LibraryContract.UiPointerEventKind.ButtonDown,
            5,
            5)));
        retainedRuntime.EnqueueInput(LibraryContract.UiInputEvent.FromText(new LibraryContract.UiTextInput("A".AsMemory())));
        retainedRuntime.ApplyMutations();
        retainedRuntime.EnqueueInput(LibraryContract.UiInputEvent.FromText(new LibraryContract.UiTextInput("B".AsMemory())));
        retainedRuntime.Layout(new(100, 20), 1);
        Assert.Equal("AB", retainedText.Text, "an explicit property-mutation flush does not release pending input snapshots");
        retainedRuntime.Dispose();

        var keyboardRoot = new Library.UiPanel();
        var keyboardText = new Library.TextBox { Width = 100, Height = 20 };
        keyboardRoot.Add(keyboardText);
        keyboardRoot.Add(new Library.UiButton { Width = 100, Height = 20 });
        using var keyboardTextService = new EmptyTextService();
        using var keyboardDocument = new Library.UiDocument(keyboardRoot, keyboardTextService);
        keyboardDocument.Layout(new Delta.Maths.float2(100, 40), 1);
        keyboardDocument.Dispatch(LibraryContract.UiInputEvent.FromKey(new LibraryContract.UiKeyEvent(
            LibraryContract.UiKeyEventKind.Down,
            new LibraryContract.UiPhysicalKey(9),
            default,
            default,
            false)));
        keyboardDocument.Dispatch(LibraryContract.UiInputEvent.FromText(new LibraryContract.UiTextInput("T".AsMemory())));
        keyboardDocument.Layout(new Delta.Maths.float2(100, 40), 1);
        Assert.Equal("T", keyboardText.Text, "text boxes participate in keyboard focus traversal");
        Assert.Equal(UiAutomationRole.TextBox, keyboardText.RetainedElement.Automation.Role, "text box exposes text-box automation metadata");
    }

    private static void ImeFocusAndCaptureLifecycle()
    {
        var root = new Library.UiStackPanel();
        var first = new Library.TextBox { Width = 100, Height = 20 };
        var second = new Library.TextBox { Width = 100, Height = 20 };
        var button = new Library.UiButton { Width = 100, Height = 20 };
        root.Add(first);
        root.Add(second);
        root.Add(button);
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(root, textService);
        document.Layout(new(100, 60), 1);

        document.Dispatch(LibraryContract.UiInputEvent.FromPointingDevice(Pointer(
            LibraryContract.UiPointerEventKind.ButtonDown,
            5,
            5)));
        var preedit = new[] { '\u5019' };
        document.Dispatch(LibraryContract.UiInputEvent.FromComposition(new LibraryContract.UiCompositionEvent(
            LibraryContract.UiCompositionStage.Started,
            preedit.AsMemory(),
            new TextContract.TextRange(0, 1))));
        preedit[0] = '\u5909';
        document.Layout(new(100, 60), 1);

        var retainedFirst = (TextBox)first.RetainedElement;
        Assert.True(retainedFirst.IsComposing, "IME start creates retained preedit state");
        Assert.Equal("\u5019", retainedFirst.CompositionText, "queued IME input owns its preedit snapshot");
        Assert.Equal(string.Empty, first.Text, "IME preedit does not commit the bound text value");
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                new UiRuntimeTypeIndex(9),
                retainedFirst,
                new(retainedFirst.Id, retainedFirst.Generation, retainedFirst.LayoutScale, retainedFirst.TextRunVersion),
                out var preeditRun) && preeditRun.Text == "\u5019",
            "text extraction renders the retained IME preedit without changing committed text");

        document.Dispatch(LibraryContract.UiInputEvent.FromComposition(new LibraryContract.UiCompositionEvent(
            LibraryContract.UiCompositionStage.Cancelled,
            ReadOnlyMemory<char>.Empty,
            default)));
        document.Layout(new(100, 60), 1);
        Assert.True(!retainedFirst.IsComposing && first.Text.Length == 0, "IME cancel clears preedit without committing text");

        document.Dispatch(LibraryContract.UiInputEvent.FromComposition(new LibraryContract.UiCompositionEvent(
            LibraryContract.UiCompositionStage.Updated,
            "\u5019".AsMemory(),
            new TextContract.TextRange(0, 1))));
        document.Dispatch(LibraryContract.UiInputEvent.FromComposition(new LibraryContract.UiCompositionEvent(
            LibraryContract.UiCompositionStage.Finished,
            ReadOnlyMemory<char>.Empty,
            default)));
        document.Dispatch(LibraryContract.UiInputEvent.FromText(new LibraryContract.UiTextInput("\u5019".AsMemory())));
        document.Layout(new(100, 60), 1);
        Assert.Equal("\u5019", first.Text, "finished composition commits only through the separate UTF text packet");

        var shiftTab = Key(9, LibraryContract.UiModifierBits.Shift);
        document.Dispatch(LibraryContract.UiInputEvent.FromKey(shiftTab));
        document.Layout(new(100, 60), 1);
        Assert.True(((Button)button.RetainedElement).IsFocused, "Shift+Tab traverses focus in reverse order");

        var captureRoot = new Panel();
        var captureButton = new Button { Width = 100, Height = 20 };
        captureRoot.Add(captureButton);
        var runtime = new UiRuntime(captureRoot);
        runtime.Layout(new(100, 20), 1);
        var buttonDown = Pointer(LibraryContract.UiPointerEventKind.ButtonDown, 5, 5);
        runtime.Input.RoutePointer(in buttonDown);
        runtime.ApplyMutations();
        Assert.True(runtime.Input.Captured == captureButton.Id && captureButton.IsPressed, "pointer down captures and presses the button");
        var cancel = Pointer(LibraryContract.UiPointerEventKind.Cancel, 5, 5);
        runtime.Input.RoutePointer(in cancel);
        runtime.ApplyMutations();
        Assert.True(runtime.Input.Captured is null && !captureButton.IsPressed, "pointer cancel releases capture and pressed state");
        runtime.Dispose();
    }

    private static void NestedHitAndDetachedInputCleanup()
    {
        var root = new Panel { Width = 100, Height = 40 };
        var container = new Border { Width = 100, Height = 40, Padding = new(5, 5, 5, 5) };
        var button = new Button { Width = 90, Height = 30 };
        container.Add(button);
        root.Add(container);
        var runtime = new UiRuntime(root);
        runtime.Layout(new(100, 40), 1);

        var clicks = 0;
        button.Click += (_, _) => clicks++;
        var down = Pointer(LibraryContract.UiPointerEventKind.ButtonDown, 10, 10);
        var up = Pointer(LibraryContract.UiPointerEventKind.ButtonUp, 10, 10);
        runtime.Input.RoutePointer(in down);
        runtime.Input.RoutePointer(in up);
        runtime.ApplyMutations();
        Assert.Equal(1, clicks, "nested hit testing routes input to the deepest visual child");

        runtime.Input.RoutePointer(in down);
        runtime.ApplyMutations();
        Assert.True(button.IsPressed && runtime.Input.Captured == button.Id, "nested button owns pointer capture");
        Assert.True(container.Remove(button), "captured nested button can be detached");
        runtime.Layout(new(100, 40), 1);
        Assert.True(runtime.Input.Captured is null && runtime.Input.Focused is null, "focus and capture are repaired after target removal");
        Assert.True(!button.IsPressed, "capture loss clears the detached button pressed state");
        runtime.Dispose();
    }

    private static void NumericEditorReceivesCanonicalInput()
    {
        var numeric = new Library.UiNumericEditor { Width = 100, Height = 20 };
        numeric.Initialize(2);
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(numeric, textService);
        document.Layout(new(100, 20), 1);
        document.Dispatch(LibraryContract.UiInputEvent.FromPointingDevice(Pointer(
            LibraryContract.UiPointerEventKind.ButtonDown,
            5,
            5)));
        document.Dispatch(LibraryContract.UiInputEvent.FromKey(Key(38)));
        document.Layout(new(100, 20), 1);
        Assert.Equal(3d, numeric.CurrentValue, "numeric editor receives physical-key increment through UiDocument.Dispatch");

        document.Dispatch(LibraryContract.UiInputEvent.FromText(new LibraryContract.UiTextInput("4".AsMemory())));
        document.Layout(new(100, 20), 1);
        Assert.True(numeric.Text.Contains('4', StringComparison.Ordinal), "numeric editor receives UTF text through the canonical input queue");
    }

    private static void PublicWheelScrollsScrollViewer()
    {
        var scroll = new Library.UiScrollViewer { Width = 100, Height = 40 };
        var content = new Library.UiStackPanel { Height = 120 };
        content.Add(new Library.UiPanel { Height = 120 });
        scroll.SetContent(content);
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(scroll, textService);
        document.Layout(new Delta.Maths.float2(100, 40), 1);

        document.Dispatch(LibraryContract.UiInputEvent.FromPointingDevice(new LibraryContract.UiPointerEvent(
            LibraryContract.UiPointerEventKind.Wheel,
            LibraryContract.UiPointerDeviceKind.Mouse,
            1,
            new Delta.Maths.float2(10, 10),
            default,
            new Delta.Maths.float2(0, -20),
            LibraryContract.UiPointerButton.None,
            default,
            0,
            default)));
        document.Layout(new Delta.Maths.float2(100, 40), 1);

        Assert.Equal(20f, scroll.OffsetY, "public wheel input scrolls the retained viewport");
    }

    private static void ScrollAndClips()
    {
        var scroll = new Library.UiScrollViewer { Width = 100, Height = 40 };
        var content = new Library.UiStackPanel { Height = 120 };
        content.Add(new Library.UiPanel { Width = 100, Height = 60, Background = new(255, 0, 0) });
        content.Add(new Library.UiPanel { Width = 100, Height = 60, Background = new(0, 255, 0) });
        scroll.SetContent(content);
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(scroll, textService);
        document.Layout(new(100, 40), 1);
        scroll.ScrollBy(0, 20);
        document.Layout(new(100, 40), 1);
        var list = document.BuildDisplayList();
        Assert.Equal(2, list.Visuals.Length, "scroll retains both commands");
        Assert.Equal(0, list.Text.Length, "plain scrolling content has no text runs");
        foreach (var command in list.Visuals)
        {
            Assert.True(list.Clips[command.Clip.Value].Bounds.z <= 100 && list.Clips[command.Clip.Value].Bounds.w <= 40, "scroll clips to viewport");
        }

        scroll.ScrollBy(0, 200);
        document.Layout(new(100, 40), 1);
        Assert.Equal(80f, scroll.OffsetY, "scroll offset clamps to content extent");
        Assert.Equal(new UiRect(0, -80, 100, 120), content.RetainedElement.Bounds, "clamped scroll positions content without changing its size");
        Assert.Equal(new UiRect(0, 0, 100, 40), content.RetainedElement.Clip, "scroll keeps the content clip inside the viewport");
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

    }

    private static void LibraryApiSmoke()
    {
        var loader = new Library.XamlLoader();
        var context = new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var loaded = loader.Load("<Panel Width=\"80\" Height=\"20\" Background=\"#102030\" />", in context);
        Assert.True(loaded.Success && loaded.Root is not null, "library loader returns retained root");
        if (loaded.Root is not { } root) { throw new InvalidOperationException("library loader root missing"); }
        using var text = new EmptyTextService();
        using var document = new Library.UiDocument(root, text);
        document.Layout(new Delta.Maths.float2(80, 20), 1);
        var display = document.BuildDisplayList();
        Assert.Equal(1, display.Visuals.Length, "library document builds canonical visual display list");
        Assert.Equal(1, display.Clips.Length, "library document builds canonical clip list");

        var invalid = loader.Load("<Unsupported />", in context);
        Assert.True(!invalid.Success && invalid.Diagnostics.Length == 1, "library loader returns canonical diagnostics");
        Assert.Equal("XAML002", invalid.Diagnostics.Span[0].Code.Value, "library diagnostic code is preserved");

        var customContext = new Library.XamlLoadContext(new CustomLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var custom = loader.Load("<CustomBadge />", in customContext);
        Assert.True(custom.Success && custom.Root is LibraryCustomBadge, "library type resolver creates custom element");

        var nested = loader.Load("<StackPanel Width=\"80\" Height=\"20\"><TextBlock Text=\"child\" /></StackPanel>", in context);
        Assert.True(nested.Success && nested.Root is { Children.Count: 1 }, "cold loader attaches nested StackPanel children to the canonical tree");

        var resourceId = new LibraryContract.UiResourceId(new Guid("F25871B4-23F7-4DA5-ADED-AF3F38C4E55F"));
        var resourceContext = new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), new FixedLibraryResourceResolver(resourceId, new UiColor(7, 8, 9)));
        var resource = loader.Load($"<TextBlock ForegroundResource=\"{resourceId.Value:D}\" />", in resourceContext);
        Assert.True(resource.Success && resource.Root is not null, "library resource resolver is consumed by loader");
        var missingResource = loader.Load($"<TextBlock ForegroundResource=\"{resourceId.Value:D}\" />", in context);
        Assert.True(!missingResource.Success && missingResource.Diagnostics.Length == 1 && missingResource.Diagnostics.Span[0].Code.Value == "XAML006", "missing canonical resource is diagnostic");

        var brushCatalog = new Library.UiResourceCatalog();
        var gradientId = new LibraryContract.UiResourceId(new Guid("F0D89E84-B5B4-45B8-9230-2F6B1AAE1D41"));
        brushCatalog.Set("AccentBrush", Library.UiBrush.LinearGradient(gradientId));
        var brushContext = new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), brushCatalog);
        var brush = loader.Load("<Panel BackgroundBrush=\"{DynamicResource AccentBrush}\" />", in brushContext);
        Assert.True(brush.Success && brush.Root is not null, "resource-backed BackgroundBrush uses the canonical retained brush path");
        var incompatibleBrush = loader.Load("<Panel Background=\"{StaticResource AccentBrush}\" />", in brushContext);
        Assert.True(!incompatibleBrush.Success && incompatibleBrush.Diagnostics.Length == 1 && incompatibleBrush.Diagnostics.Span[0].Code.Value == "XAML010", "incompatible resource/effect combinations are diagnosed");

        using var textDocument = new EmptyTextService();
        var textRoot = loader.Load("<TextBlock Text=\"Hello\" />", in context).Root;
        if (textRoot is null) { throw new InvalidOperationException("library text root missing"); }
        using var textDocumentOwner = new Library.UiDocument(textRoot, textDocument);
        textDocumentOwner.Layout(new Delta.Maths.float2(80, 20), 1);
        Assert.True(!textDocumentOwner.TryBuildDisplayList(out _, out var textDiagnostic) && textDiagnostic is { } unsupported && unsupported.Code.Value == "XAML_TEXT_FONT_NOT_FOUND", "unregistered text font returns a diagnostic");
    }

    private static void DocumentDisposesBindingSubscriptions()
    {
        var model = new BindingModel { Name = "before" };
        var text = new Library.TextBlock { BindingContext = model };
        text.SetBinding("Text", new Library.UiBindingExpression("Name"));
        using var textService = new EmptyTextService();
        var document = new Library.UiDocument(text, textService);
        Assert.Equal("before", text.Text, "document binding initializes before disposal");

        document.Dispose();
        model.Name = "after";
        Assert.Equal("before", text.Text, "disposed document unsubscribes expression bindings");

        var disposed = false;
        try
        {
            document.Layout(new Delta.Maths.float2(100, 20), 1);
        }
        catch (ObjectDisposedException)
        {
            disposed = true;
        }

        Assert.True(disposed, "disposed document rejects further layout");
        document.Dispose();
    }

    private static void TypeCatalogUsesStableIds()
    {
        var name = new Library.XamlQualifiedName("urn:sample", "Badge");
        var first = new Library.XamlTypeCatalog();
        first.Register(name, static () => new Library.UiBorder());
        Assert.True(first.TryResolveName(name, out var firstId), "type catalog resolves a registered name");

        var second = new Library.XamlTypeCatalog();
        second.Register(name, static () => new Library.UiBorder());
        Assert.True(second.TryResolveName(name, out var secondId) && firstId == secondId, "implicit custom type identity is stable across catalog instances");

        var explicitId = new Library.UiTypeId(new Guid("A9F13A7D-E6FA-4F11-9B28-3D87B9F4EE51"));
        var explicitCatalog = new Library.XamlTypeCatalog();
        explicitCatalog.Register(name, explicitId, static () => new Library.UiBorder());
        Assert.True(explicitCatalog.TryResolveName(name, out var resolvedId) && resolvedId == explicitId, "explicit custom type identity is preserved");
        Assert.True(explicitCatalog.TryCreate(explicitId, out var created) && created is Library.UiBorder, "custom factory resolves through the indexed type identity");

        var duplicate = new Library.XamlTypeCatalog();
        duplicate.Register(name, explicitId, static () => new Library.UiBorder());
        var duplicateName = new Library.XamlQualifiedName("urn:other", "Badge");
        var threw = false;
        try
        {
            duplicate.Register(duplicateName, explicitId, static () => new Library.UiBorder());
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Assert.True(threw, "a stable type identity cannot be registered for two XAML names");
    }

    private static void ResourceDependencyInvalidation()
    {
        var resources = new UiResourceStore();
        resources.Set("Base", new UiColor(10, 20, 30));
        resources.Set("Alias", new UiResourceReference("Base"));
        resources.Set("Other", new UiColor(40, 50, 60));
        var element = new UiElement();
        element.SetStyleResource("Value", resources, new("Alias"), UiDirtyFlags.Visual);
        element.DirtyFlags = UiDirtyFlags.None;

        resources.Set("Other", new UiColor(41, 51, 61));
        Assert.Equal(UiDirtyFlags.None, element.DirtyFlags, "unrelated resource updates do not invalidate a dependent property");

        resources.Set("Base", new UiColor(11, 21, 31));
        Assert.True((element.DirtyFlags & UiDirtyFlags.Visual) != 0, "an alias target update invalidates its dependent property");
        element.DirtyFlags = UiDirtyFlags.None;

        resources.Set("Alias", new UiResourceReference("Other"));
        Assert.True((element.DirtyFlags & UiDirtyFlags.Visual) != 0, "changing an alias target invalidates the dependent property");

        var targetId = new LibraryContract.UiResourceId(new Guid("A3D23E31-9C65-4C72-A60A-1C7A0E7CE001"));
        var aliasId = new LibraryContract.UiResourceId(new Guid("A3D23E31-9C65-4C72-A60A-1C7A0E7CE002"));
        var catalog = new Library.UiResourceCatalog();
        catalog.Set(targetId, new Library.UiColor(30, 40, 50));
        catalog.Set(aliasId, new Library.UiResourceReference(targetId));
        Assert.Equal(2, catalog.Store.ResourceSlotCount, "compiled resource identities occupy compact catalog slots");
        Assert.True(catalog.Store.TryGet(aliasId.Value.ToString("D"), out var retainedAlias) &&
            retainedAlias is DeltaXAML.Internal.UiResourceReference { HasResourceId: true, ResourceId: var retainedTarget } &&
            retainedTarget == targetId.Value, "resource catalog preserves a reference's stable target identity");
        Assert.True(catalog.TryResolve(aliasId, out var resolvedAlias) && resolvedAlias is Library.UiColor { R: 30, G: 40, B: 50 }, "resource aliases resolve through their stable identity");
        var typedResourceText = new Library.TextBlock();
        typedResourceText.SetDynamicResource("Foreground", catalog, aliasId);
        Assert.Equal(new Library.UiColor(30, 40, 50), typedResourceText.Foreground, "typed dynamic resource keeps its stable identity");
        catalog.Set(targetId, new Library.UiColor(31, 41, 51));
        Assert.Equal(new Library.UiColor(31, 41, 51), typedResourceText.Foreground, "typed alias target invalidates its dependent property");
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(typedResourceText, textService);
        document.Layout(new(100, 20), 1);
        catalog.Set(targetId, new Library.UiColor(32, 42, 52));
        Assert.Equal(new Library.UiColor(31, 41, 51), typedResourceText.Foreground, "attached resource callback only queues retained work");
        document.Layout(new(100, 20), 1);
        Assert.Equal(new Library.UiColor(32, 42, 52), typedResourceText.Foreground, "style/resource stage applies the queued dependency update");
    }

    private static void RuntimeResourceDiagnostics()
    {
        var resources = new Library.UiResourceCatalog();
        resources.Set("Accent", new Library.UiColor(10, 20, 30));
        var panel = new Library.UiPanel();
        panel.SetDynamicResource("Background", resources, "Accent");
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(panel, textService);
        document.Layout(new Delta.Maths.float2(100, 20), 1);
        Assert.True(document.TryBuildDisplayList(out _, out var diagnostic) && diagnostic is null, $"a compatible dynamic resource has no runtime diagnostic: {diagnostic?.Code.Value} {diagnostic?.Message}");

        resources.Set("Accent", Library.UiBrush.Solid(new Library.UiColor(40, 50, 60)));
        document.Layout(new Delta.Maths.float2(100, 20), 1);
        Assert.True(!document.TryBuildDisplayList(out _, out diagnostic) && diagnostic is { Code.Value: "XAML010" }, "an incompatible dynamic resource is reported at display-list build");

        resources.Set("Accent", new Library.UiColor(70, 80, 90));
        document.Layout(new Delta.Maths.float2(100, 20), 1);
        Assert.True(document.TryBuildDisplayList(out _, out diagnostic) && diagnostic is null, "a corrected dynamic resource clears the runtime diagnostic");
    }

    private static void TextDisplayListUsesDeltaText()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        Assert.True(File.Exists(fontPath), "DeltaText fixture font is copied for the text adapter test");
        var fonts = new Library.UiFontCatalog();
        fonts.Register(
            "default",
            new TextContract.FontSourceId(new Guid("E7C9B4D5-FD99-4D2A-8A6C-4A2A5B8F2FCB")),
            File.ReadAllBytes(fontPath));
        var text = new Library.TextBlock { Text = "A", Width = 240, Height = 40 };
        var unchangedText = new Library.TextBlock { Text = "unchanged", Width = 240, Height = 40 };
        var root = new Library.UiPanel { Background = new(1, 2, 3) };
        root.Add(text);
        root.Add(unchangedText);
        using var textService = new CountingTextService();
        using var document = new Library.UiDocument(root, textService, fonts);
        document.Layout(new Delta.Maths.float2(240, 40), 1);
        var first = document.BuildDisplayList();
        Assert.Equal(2, first.Text.Length, "facade emits canonical text draws");
        Assert.Equal(3, first.Order.Length, "mixed display output emits one ordered reference per payload");
        Assert.Equal(LibraryContract.UiTextPaint.Solid(new float4(1, 1, 1, 1)), first.Text[0].Paint, "text extraction emits explicit fill-only paint");
        Assert.Equal(new LibraryContract.UiDrawRef(LibraryContract.UiDrawKind.Visual, 0), first.Order[0], "visual payload is ordered before its text children");
        Assert.Equal(new LibraryContract.UiDrawRef(LibraryContract.UiDrawKind.Text, 0), first.Order[1], "first text payload keeps traversal order");
        Assert.Equal(new LibraryContract.UiDrawRef(LibraryContract.UiDrawKind.Text, 1), first.Order[2], "second text payload keeps traversal order");
        Assert.Equal(first.Order.Length, first.Identities.Length, "each ordered payload has one aligned identity");
        var firstIdentity = first.Identities[1];
        var unchangedIdentity = first.Identities[2];
        Assert.True(firstIdentity.Value != 0 && firstIdentity.Generation != 0, "canonical text output carries a retained owner identity");
        Assert.True(firstIdentity.Version > 0, "canonical text output carries a producer version");
        Assert.Equal(2, textService.ShapeCount, "initial text output shapes each retained text node");
        var firstShaped = first.Text[0].Text;
        Assert.True(firstShaped.Runs.Length > 0, "DeltaText returns positioned shaped runs");
        var second = document.BuildDisplayList();
        Assert.True(ReferenceEquals(firstShaped, second.Text[0].Text), "unchanged text reuses shaped cache");
        Assert.Equal(firstIdentity, second.Identities[1], "unchanged text preserves its aligned identity and version");
        text.Text = "B";
        document.Layout(new Delta.Maths.float2(240, 40), 1);
        var third = document.BuildDisplayList();
        Assert.True(!ReferenceEquals(firstShaped, third.Text[0].Text), "text mutation reshapes only the changed text cache");
        Assert.Equal(3, textService.ShapeCount, "value-only visual update does not reshape unchanged text");
        Assert.True(ReferenceEquals(first.Text[1].Text, third.Text[1].Text), "value-only visual update preserves unchanged shaped text");
        Assert.Equal(first.Text[0].Clip, third.Text[0].Clip, "text clip identity remains canonical");
        Assert.Equal(firstIdentity.Value, third.Identities[1].Value, "text mutation preserves the retained owner identity");
        Assert.Equal(firstIdentity.Generation, third.Identities[1].Generation, "text mutation preserves the retained owner generation");
        Assert.True(third.Identities[1].Version > firstIdentity.Version, "text mutation advances only the producer version");
        Assert.Equal(unchangedIdentity, third.Identities[2], "unmodified text preserves its aligned identity and version");
    }

    private static void PaintPropertiesReachDisplayList()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new Library.UiFontCatalog();
        fonts.Register(
            "default",
            new TextContract.FontSourceId(new Guid("4B1E8A67-CE40-4B5C-9E76-2AA4B58E1F45")),
            File.ReadAllBytes(fontPath));
        var effect = new LibraryContract.UiResourceId(new Guid("A82B3F7C-5D09-43F1-9DB5-1A377E4B2401"));
        var border = new Library.UiBorder
        {
            Width = 120,
            Height = 40,
            Background = new(20, 30, 40),
            BorderColor = new(200, 210, 220),
            BorderWidth = 2,
            BorderWidthUnits = LibraryContract.PaintUnits.Device,
            CornerRadius = new Library.UiCornerRadii(2, 4, 6, 8),
        };
        var text = new Library.TextBlock
        {
            Text = "Outlined",
            Width = 120,
            Height = 40,
            OutlineColor = new(255, 80, 40),
            OutlineWidth = 1.5f,
            TextEffect = effect,
        };
        border.SetChild(text);
        using var textService = new CountingTextService();
        using var document = new Library.UiDocument(border, textService, fonts);
        document.Layout(new(120, 40), 1);
        var display = document.BuildDisplayList();

        Assert.Equal(LibraryContract.UiVisualKind.Border, display.Visuals[0].Kind, "border paint selects the border visual kind");
        Assert.Equal(new float4(20 / 255f, 30 / 255f, 40 / 255f, 1), display.Visuals[0].Paint.FillColor, "border fill reaches the canonical paint");
        Assert.Equal(new float4(200 / 255f, 210 / 255f, 220 / 255f, 1), display.Visuals[0].Paint.StrokeColor, "border color reaches the canonical paint");
        Assert.Equal(2f, display.Visuals[0].Paint.StrokeWidth, "border width reaches the canonical paint");
        Assert.Equal(LibraryContract.PaintUnits.Device, display.Visuals[0].Paint.Units, "device border units reach the canonical paint");
        Assert.Equal(new float4(2, 4, 6, 8), display.Visuals[0].Paint.CornerRadii, "per-corner radii reach the canonical paint in contract order");
        Assert.Equal(new LibraryContract.UiTextPaint(
            new float4(1, 1, 1, 1),
            new float4(1, 80 / 255f, 40 / 255f, 1),
            1.5f,
            effect), display.Text[0].Paint, "text outline and effect identity reach the canonical paint");
        var shaped = display.Text[0].Text;
        text.OutlineWidth = 2;
        document.Layout(new(120, 40), 1);
        var recolored = document.BuildDisplayList();
        Assert.True(ReferenceEquals(shaped, recolored.Text[0].Text), "paint-only text changes reuse the shaped text cache");
        Assert.Equal(2f, recolored.Text[0].Paint.OutlineWidth, "paint-only text changes update the neutral request");

        var resources = new Library.UiResourceCatalog();
        resources.Set("TextEffect", effect);
        var loader = new Library.XamlLoader();
        var context = new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), resources);
        var loaded = loader.Load(
            "<TextBlock Text=\"A\" TextEffect=\"{DynamicResource TextEffect}\" OutlineColor=\"#FF8040\" OutlineWidth=\"1\" />",
            in context);
        Assert.True(loaded.Success && loaded.Root is Library.TextBlock, "XAML accepts text paint properties and dynamic effect resources");
        if (loaded.Root is Library.TextBlock loadedText)
        {
            Assert.Equal(effect, loadedText.TextEffect, "dynamic text effect resolves to the resource identity");
            Assert.Equal(1f, loadedText.OutlineWidth, "XAML preserves outline width");
        }

        var cornerMarkup = loader.Load(
            "<Border Width=\"100\" Height=\"40\" CornerRadius=\"2,4,6,8\" Background=\"#FFFFFF\" BorderWidth=\"1\" BorderWidthUnits=\"Device\" />",
            in context);
        Assert.True(cornerMarkup.Success && cornerMarkup.Root is Library.UiBorder, "XAML accepts four corner radii");
        if (cornerMarkup.Root is Library.UiBorder loadedBorder)
        {
            Assert.Equal(new Library.UiCornerRadii(2, 4, 6, 8), loadedBorder.CornerRadius, "XAML preserves per-corner radius order");
            Assert.Equal(LibraryContract.PaintUnits.Device, loadedBorder.BorderWidthUnits, "XAML selects device-pixel border units");
        }

        var invalidUnits = loader.Load(
            "<Border BorderWidth=\"1\" BorderWidthUnits=\"Pixels\" />",
            in context);
        Assert.True(!invalidUnits.Success && invalidUnits.Diagnostics.Length == 1 && invalidUnits.Diagnostics.Span[0].Code.Value == "XAML003", "XAML diagnoses unsupported border unit values");
    }

    private static void RoundedCornerRadiiAreNormalizedBeforeExtraction()
    {
        var border = new Library.UiBorder
        {
            Width = 320,
            Height = 320,
            Background = new(40, 80, 120),
            CornerRadius = new Library.UiCornerRadii(0, 0, 0, 200),
        };
        using var document = new Library.UiDocument(border, new EmptyTextService());
        document.Layout(new(320, 320), 1);
        var display = document.BuildDisplayList();

        Assert.Equal(new float4(0, 0, 0, 160), display.Visuals[0].Paint.CornerRadii, "oversized corner is limited before display-list extraction");
        Assert.Equal(new Library.UiCornerRadii(0, 0, 0, 200), border.CornerRadius, "normalization does not mutate the declared property");

        border.Width = 200;
        border.Height = 100;
        border.CornerRadius = new Library.UiCornerRadii(140, 140, 0, 0);
        document.Layout(new(200, 100), 1);
        var scaled = document.BuildDisplayList();
        Assert.Equal(new float4(50, 50, 0, 0), scaled.Visuals[0].Paint.CornerRadii, "adjacent radii are limited to the arranged bounds");
    }

    private static void BorderWidthUnitsPropertyDescriptor()
    {
        var border = new Library.UiBorder();
        Assert.Equal(LibraryContract.PaintUnits.Logical, border.BorderWidthUnits, "logical units are the default border mode");
        border.RetainedElement.DirtyFlags = UiDirtyFlags.None;
        Assert.True(border.TrySetValue(Library.UiElementProperties.BorderWidthUnits, LibraryContract.PaintUnits.Device, out var diagnostic), "typed property sets device border units");
        Assert.True(diagnostic is null && border.BorderWidthUnits == LibraryContract.PaintUnits.Device, "typed border unit value updates retained state");
        Assert.True((border.RetainedElement.DirtyFlags & UiDirtyFlags.Visual) != 0 &&
            (border.RetainedElement.DirtyFlags & UiDirtyFlags.Measure) == 0, "border unit changes invalidate visual output without relayout");
        Assert.Throws<ArgumentOutOfRangeException>(
            () => border.BorderWidthUnits = (LibraryContract.PaintUnits)99,
            "unknown border units are rejected at the user API boundary");
    }

    private static void DisplayListDirtySubtreeReusesStableText()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new Library.UiFontCatalog();
        fonts.Register(
            "default",
            new TextContract.FontSourceId(new Guid("B67E4FD5-9A68-49F1-BB7E-3C841779B0C3")),
            File.ReadAllBytes(fontPath));
        var changed = new Library.TextBlock { Text = "A", Width = 100, Height = 20 };
        var stable = new Library.TextBlock { Text = "unchanged", Width = 100, Height = 20 };
        var root = new Library.UiPanel();
        root.Add(changed);
        root.Add(stable);
        var resolver = new CountingFontResolver(fonts);
        using var textService = new CountingTextService();
        using var document = new Library.UiDocument(root, textService, resolver);
        document.Layout(new Delta.Maths.float2(100, 40), 1);
        var first = document.BuildDisplayList();
        Assert.Equal(1, resolver.ResolveCount, "initial display extraction resolves one shared font and reuses its identity");
        var stableShaped = first.Text[1].Text;

        changed.Text = "B";
        document.Layout(new Delta.Maths.float2(100, 40), 1);
        var second = document.BuildDisplayList();
        Assert.Equal(1, resolver.ResolveCount, "dirty extraction reuses the changed text subtree font slot");
        Assert.Equal(3, textService.ShapeCount, "dirty extraction reshapes only the changed text");
        Assert.True(ReferenceEquals(stableShaped, second.Text[1].Text), "dirty extraction reuses the stable shaped text");
    }

    private static void TextCacheDropsRemovedNodes()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new Library.UiFontCatalog();
        fonts.Register(
            "default",
            new TextContract.FontSourceId(new Guid("5B9AA8C5-4B0A-4AE4-8B6D-9E2F6C2D46B0")),
            File.ReadAllBytes(fontPath));
        var root = new Library.UiPanel { Width = 100, Height = 40 };
        var removed = new Library.TextBlock { Text = "removed", Width = 100, Height = 20 };
        var retained = new Library.TextBlock { Text = "retained", Width = 100, Height = 20 };
        root.Add(removed);
        root.Add(retained);
        using var textService = new CountingTextService();
        using var document = new Library.UiDocument(root, textService, fonts);
        document.Layout(new Delta.Maths.float2(100, 40), 1);
        _ = document.BuildDisplayList();
        Assert.Equal(2, document.TextCacheCount, "initial extraction caches both live text nodes");

        root.Remove(removed);
        document.Layout(new Delta.Maths.float2(100, 40), 1);
        _ = document.BuildDisplayList();
        Assert.Equal(1, document.TextCacheCount, "structural extraction drops the removed text cache entry");

        root.Add(removed);
        document.Layout(new Delta.Maths.float2(100, 40), 1);
        _ = document.BuildDisplayList();
        Assert.Equal(2, document.TextCacheCount, "re-added text gets one live cache entry");
        Assert.Equal(3, textService.ShapeCount, "re-added text reshapes after its old cache entry was removed");
    }

    private static void DpiInvalidatesLayoutWithoutCompoundingScale()
    {
        using var publicTextService = new EmptyTextService();
        using var publicDocument = new Library.UiDocument(new Library.UiPanel(), publicTextService);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => publicDocument.Layout(new(100, 40), 0),
            "public layout rejects a non-positive DPI scale");

        var text = new TextBlock { Text = "dpi" };
        var runtime = new UiRuntime(text);
        runtime.Layout(new(100, 40), 1);
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                new UiRuntimeTypeIndex(1),
                text,
                new(text.Id, text.Generation, text.LayoutScale, text.TextRunVersion),
                out var first),
            "DPI fixture emits a text run");
        var firstLayoutVersion = text.LayoutVersion;

        runtime.Layout(new(100, 40), 2);
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                new UiRuntimeTypeIndex(1),
                text,
                new(text.Id, text.Generation, text.LayoutScale, text.TextRunVersion),
                out var second),
            "DPI update keeps the text run available");
        Assert.True(second.Version != first.Version, "DPI change invalidates the text-run version");
        Assert.True(second.FontSize.Equals(first.FontSize * 2), "DPI scales text metrics exactly once");
        Assert.True(text.LayoutVersion > firstLayoutVersion, "DPI change invalidates layout");

        var stableLayoutVersion = text.LayoutVersion;
        runtime.Layout(new(100, 40), 2);
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                new UiRuntimeTypeIndex(1),
                text,
                new(text.Id, text.Generation, text.LayoutScale, text.TextRunVersion),
                out var third),
            "repeated DPI layout keeps the text run available");
        Assert.Equal(stableLayoutVersion, text.LayoutVersion, "unchanged DPI does not repeat layout invalidation");
        Assert.Equal(second.Version, third.Version, "unchanged DPI keeps the text-run version");
        Assert.Equal(second.FontSize, third.FontSize, "unchanged DPI does not compound text scaling");

        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new Library.UiFontCatalog();
        fonts.Register(
            "default",
            new TextContract.FontSourceId(new Guid("2A8B91CD-845B-4F92-8873-E60A0F30323A")),
            File.ReadAllBytes(fontPath));
        using var shaping = new CountingTextService();
        using var dpiDocument = new Library.UiDocument(
            new Library.TextBlock { Text = "dpi", Width = 100, Height = 40 },
            shaping,
            fonts);
        dpiDocument.Layout(new(100, 40), 1);
        _ = dpiDocument.BuildDisplayList();
        dpiDocument.Layout(new(100, 40), 2);
        _ = dpiDocument.BuildDisplayList();
        Assert.Equal(2, shaping.ShapeCount, "DPI change reshapes the affected text at its new pixel size");
        dpiDocument.Layout(new(100, 40), 2);
        _ = dpiDocument.BuildDisplayList();
        Assert.Equal(2, shaping.ShapeCount, "unchanged DPI preserves the shaped-text cache");
    }

    private static void TextVersionTracksTextInputsOnly()
    {
        var root = new Panel();
        var text = new TextBlock { Text = "stable", Width = 100, Height = 20 };
        root.Add(text);
        var runtime = new UiRuntime(root);
        runtime.Layout(new(100, 40), 1);
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                new UiRuntimeTypeIndex(1),
                text,
                new(text.Id, text.Generation, text.LayoutScale, text.TextRunVersion),
                out var first),
            "text fixture emits an initial run");
        root.CompleteVisualExtraction();
        text.CompleteVisualExtraction();
        root.DirtyFlags = UiDirtyFlags.None;
        text.DirtyFlags = UiDirtyFlags.None;
        var rootOutputVersion = root.OutputVersion;
        text.InvalidateChanged(UiDirtyFlags.Text);
        Assert.True(root.OutputVersion > rootOutputVersion, "child text invalidation reaches the document output");
        root.CompleteVisualExtraction();
        text.CompleteVisualExtraction();
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                new UiRuntimeTypeIndex(1),
                text,
                new(text.Id, text.Generation, text.LayoutScale, text.TextRunVersion),
                out var textInvalidated),
            "text invalidation keeps the run available");
        Assert.True(textInvalidated.Version != first.Version, "text invalidation advances text identity");

        text.Background = new UiColor(1, 2, 3);
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                new UiRuntimeTypeIndex(1),
                text,
                new(text.Id, text.Generation, text.LayoutScale, text.TextRunVersion),
                out var background),
            "background update keeps the text run available");
        Assert.Equal(textInvalidated.Version, background.Version, "background-only updates preserve text identity");

        root.Background = new UiColor(4, 5, 6);
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                new UiRuntimeTypeIndex(1),
                text,
                new(text.Id, text.Generation, text.LayoutScale, text.TextRunVersion),
                out var parentVisual),
            "parent visual update keeps the child text run available");
        Assert.Equal(textInvalidated.Version, parentVisual.Version, "parent visual updates preserve child text identity");

        text.Foreground = new UiColor(10, 11, 12);
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                new UiRuntimeTypeIndex(1),
                text,
                new(text.Id, text.Generation, text.LayoutScale, text.TextRunVersion),
                out var foreground),
            "foreground update keeps the text run available");
        Assert.True(foreground.Version != textInvalidated.Version, "foreground updates invalidate text identity");
    }

    private static void PublicDisplayListWarmFrameHasNoAllocations()
    {
        var root = new Library.UiPanel { Width = 80, Height = 40, Background = new(1, 2, 3) };
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(root, textService);
        document.Layout(new Delta.Maths.float2(80, 40), 1);
        var first = document.BuildDisplayList();
        Assert.Equal(1, first.Visuals.Length, "warm-frame fixture has one visual");
        _ = document.BuildDisplayList();
        for (var i = 0; i < 3; i++)
        {
            document.Layout(new Delta.Maths.float2(80, 40), 1);
            _ = document.BuildDisplayList();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++)
        {
            document.Layout(new Delta.Maths.float2(80, 40), 1);
            _ = document.BuildDisplayList();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated, "unchanged public frame has no allocations");
    }

    private static void DocumentOwnsReusableDisplayListStorage()
    {
        var root = new Library.UiPanel { Width = 80, Height = 40, Background = new(1, 2, 3) };
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(root, textService);
        document.Layout(new Delta.Maths.float2(80, 40), 1);
        _ = document.BuildDisplayList();

        var storage = document.DisplayListStorage;
        var visuals = storage.Visuals;
        var clips = storage.Clips;
        var text = storage.Text;
        var order = storage.Order;
        var identities = storage.Identities;
        _ = document.BuildDisplayList();

        Assert.True(ReferenceEquals(storage, document.DisplayListStorage), "document keeps one display-list owner");
        Assert.True(ReferenceEquals(visuals, storage.Visuals), "unchanged visual output keeps its backing storage");
        Assert.True(ReferenceEquals(clips, storage.Clips), "unchanged clip output keeps its backing storage");
        Assert.True(ReferenceEquals(text, storage.Text), "unchanged text output keeps its backing storage");
        Assert.True(ReferenceEquals(order, storage.Order), "unchanged draw order keeps its backing storage");
        Assert.True(ReferenceEquals(identities, storage.Identities), "unchanged identities keep their backing storage");
    }

    private static void DisplayListOrderContract()
    {
        var visuals = new[]
        {
            new LibraryContract.UiVisualDraw(
                LibraryContract.UiVisualKind.SolidRectangle,
                default,
                new(0, 0, 10, 10),
                new(1, 1, 1, 1),
                LibraryContract.UiClipId.None,
                LibraryContract.UiResourceId.Empty),
        };
        var clips = Array.Empty<LibraryContract.UiClipRegion>();
        var order = new[]
        {
            new LibraryContract.UiDrawRef(LibraryContract.UiDrawKind.Visual, 0),
        };
        var identities = new[] { new LibraryContract.UiElementIdentity(1, 1, 1) };
        var text = Array.Empty<LibraryContract.UiTextDraw>();
        var display = new LibraryContract.UiDisplayList(visuals, clips, text, order, identities);
        Assert.Equal(1, display.Order.Length, "explicit display-list order is exposed");
        Assert.Equal(1, display.Identities.Length, "explicit display-list identity alignment is exposed");
        Assert.True(display.Order[0].IsValid, "visual draw reference is well formed");
        Assert.True(new LibraryContract.UiDrawRef(LibraryContract.UiDrawKind.Text, 0).IsValid, "text draw reference is well formed");
        Assert.True(!new LibraryContract.UiDrawRef(LibraryContract.UiDrawKind.Unknown, 0).IsValid, "unknown draw kind is rejected");
        Assert.True(!new LibraryContract.UiDrawRef(LibraryContract.UiDrawKind.Visual, -1).IsValid, "negative draw index is rejected");
        Assert.Throws<ArgumentException>(
            () =>
            {
                _ = new LibraryContract.UiDisplayList(visuals, clips, text, order, Array.Empty<LibraryContract.UiElementIdentity>());
            },
            "display-list construction rejects identity/order length mismatch");

        var paint = new LibraryContract.UiVisualPaint(
            new float4(1, 1, 1, 1),
            new float4(0, 0, 0, 1),
            2,
            new float4(4, 4, 4, 4));
        var visualWithPaint = LibraryContract.UiVisualDraw.WithPaint(
            LibraryContract.UiVisualKind.RoundedRectangle,
            default,
            new float4(0, 0, 100, 40),
            paint,
            LibraryContract.UiClipId.None,
            LibraryContract.UiResourceId.Empty);
        Assert.Equal(paint, visualWithPaint.Paint, "visual paint remains in the neutral command");
        Assert.Equal(paint.FillColor, visualWithPaint.Color, "visual compatibility color maps to fill");

        var clip = new LibraryContract.UiClipRegion(
            new float4(0, 0, 100, 40),
            LibraryContract.UiClipId.None,
            LibraryContract.UiClipKind.RoundedRectangle,
            new float4(4, 4, 4, 4));
        Assert.Equal(LibraryContract.UiClipKind.RoundedRectangle, clip.Kind, "rounded clip shape remains semantic");
        Assert.Equal(new float4(4, 4, 4, 4), clip.CornerRadii, "rounded clip radii remain neutral data");
    }

    private static void CustomVisualsRemainNeutral()
    {
        var element = new Library.UiBorder { Width = 20, Height = 10 };
        var visualType = new LibraryContract.UiVisualTypeId(new Guid("0A6B9E9D-92F4-44A1-8E61-8B1B7CF4B8D6"));
        var resource = new LibraryContract.UiResourceId(new Guid("9DD4E4FA-3B87-4FE3-A54B-9B1F5A2FCA52"));
        element.SetCustomVisual(visualType, resource, new Library.UiColor(12, 34, 56));
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(element, textService);
        document.Layout(new Delta.Maths.float2(20, 10), 1);
        var display = document.BuildDisplayList();
        Assert.Equal(1, display.Visuals.Length, "custom visual emits one neutral visual command");
        Assert.Equal(LibraryContract.UiVisualKind.Custom, display.Visuals[0].Kind, "custom visual kind is preserved");
        Assert.Equal(LibraryContract.UiVisualPaint.Solid(new float4(12 / 255f, 34 / 255f, 56 / 255f, 1)), display.Visuals[0].Paint, "custom visual extraction emits explicit fill-only paint");
        Assert.Equal(visualType, display.Visuals[0].VisualType, "custom visual identity is preserved");
        Assert.Equal(resource, display.Visuals[0].Resource, "custom resource identity is preserved");
        element.ClearCustomVisual();
        var cleared = document.BuildDisplayList();
        Assert.Equal(0, cleared.Visuals.Length, "clearing custom visual removes the neutral command");
    }

    private static void BindingExpressionsAndContexts()
    {
        var loader = new Library.XamlLoader();
        var context = new Library.XamlLoadContext(
            new EmptyLibraryTypeResolver(),
            new EmptyLibraryResourceResolver(),
            new BindingResolver());
        var loaded = loader.Load("<Panel><TextBlock Text=\"{Binding Name, Converter=Upper}\" /></Panel>", in context);
        Assert.True(loaded.Success && loaded.Root is not null, "binding expression loads");
        if (loaded.Root is not { } loadedRoot) { throw new InvalidOperationException("binding root missing"); }
        var model = new BindingModel { Name = "Ada" };
        loadedRoot.BindingContext = model;
        if (loadedRoot.Children[0] is not Library.TextBlock boundText) { throw new InvalidOperationException("bound text missing"); }
        Assert.True(boundText.Text == "ADA", "binding context and converter update target");
        model.Name = "Grace";
        Assert.True(boundText.Text == "GRACE", "source notification updates target");
        var parsed = Library.UiBindingExpression.Parse("{Binding Path=Name, Mode=TwoWay}");
        Assert.Equal(Library.UiBindingMode.TwoWay, parsed.Mode, "binding parser preserves the declared mode");
        var compiledText = new Library.TextBox();
        using var compiled = new Library.UiCompiledBinding<BindingModel, string>(model, source => source.Name, (source, value) => source.Name = value, Library.UiBindingMode.TwoWay);
        compiledText.SetBinding("Text", compiled);
        Assert.Equal("Grace", compiledText.Text, "compiled binding initializes the target");
        model.Name = "Lin";
        Assert.Equal("Lin", compiledText.Text, "compiled binding observes source changes");
        compiledText.SetText("Mina");
        Assert.Equal("Mina", model.Name, "compiled two-way binding writes the source");

        var lateContextRoot = new Library.UiPanel { BindingContext = model };
        var lateContextText = new Library.TextBlock();
        using var lateContextBinding = new Library.UiCompiledBinding<BindingModel, string>(model, source => source.Name);
        lateContextText.SetBinding("Text", lateContextBinding);
        lateContextRoot.Add(lateContextText);
        Assert.Equal("Mina", lateContextText.Text, "children added after the parent inherit its binding context");

        var deepRoot = new Library.UiPanel();
        var deepParent = deepRoot;
        for (var i = 0; i < 2048; i++)
        {
            var child = new Library.UiPanel();
            deepParent.Add(child);
            deepParent = child;
        }

        var deepText = new Library.TextBlock();
        deepText.SetBinding("Text", new Library.UiBindingExpression("Name"));
        deepParent.Add(deepText);
        using var deepTextService = new EmptyTextService();
        using var deepDocument = new Library.UiDocument(deepRoot, deepTextService);
        deepRoot.BindingContext = model;
        Assert.Equal(string.Empty, deepText.Text, "attached binding context queues the affected binding instead of executing a nested stage");
        deepDocument.Layout(new(100, 20), 1);
        Assert.Equal("Mina", deepText.Text, "attached binding context propagates through the node-store queue without recursive traversal");

        var twoWay = loader.Load("<Panel><TextBox Text=\"{Binding Name, Mode=TwoWay}\" /></Panel>", in context);
        Assert.True(twoWay.Success && twoWay.Root is not null, "two-way binding expression loads");
        if (twoWay.Root is not { } twoWayRoot) { throw new InvalidOperationException("two-way root missing"); }
        var editModel = new BindingModel();
        twoWayRoot.BindingContext = editModel;
        using var emptyTextService = new EmptyTextService();
        using var document = new Library.UiDocument(twoWayRoot, emptyTextService);
        document.Layout(new Delta.Maths.float2(100, 30), 1);
        document.Dispatch(LibraryContract.UiInputEvent.FromPointingDevice(new LibraryContract.UiPointerEvent(
            LibraryContract.UiPointerEventKind.ButtonDown,
            LibraryContract.UiPointerDeviceKind.Mouse,
            1,
            new Delta.Maths.float2(1, 1),
            default,
            default,
            LibraryContract.UiPointerButton.Primary,
            new LibraryContract.UiPointerButtons(1),
            0,
            default)));
        document.Dispatch(LibraryContract.UiInputEvent.FromText(new LibraryContract.UiTextInput("Bob".AsMemory())));
        document.Layout(new Delta.Maths.float2(100, 30), 1);
        if (twoWayRoot.Children[0] is not Library.TextBox editText) { throw new InvalidOperationException("edit text missing"); }
        Assert.True(editText.Text == "Bob" && editModel.Name == "Bob", "two-way text edit writes the source");
    }

    private static void RuntimeStagesAreOrdered()
    {
        var model = new BindingModel { Name = "before" };
        var text = new Library.TextBlock { Width = 100, Height = 20 };
        var root = new Library.UiPanel();
        root.Add(text);
        using var binding = new Library.UiCompiledBinding<BindingModel, string>(model, source => source.Name);
        text.SetBinding("Text", binding);
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(root, textService);
        document.Layout(new Delta.Maths.float2(100, 20), 1);
        model.Name = "after";
        Assert.True((text.RetainedElement.DirtyFlags & UiDirtyFlags.Binding) != 0, "source notification marks the binding stage dirty");
        Assert.Equal("before", text.Text, "binding notifications wait for the document binding stage");
        document.Layout(new Delta.Maths.float2(100, 20), 1);
        Assert.Equal("after", text.Text, "binding stage applies queued source changes before measure");
        Assert.True((text.RetainedElement.DirtyFlags & UiDirtyFlags.Binding) == 0, "binding stage clears its transient dirty flag");
    }

    private static void BindingTargetWritesReenterTheStagePipeline()
    {
        var model = new BindingModel { Name = "before" };
        var text = new Library.TextBox { Width = 100, Height = 20 };
        using var binding = new Library.UiCompiledBinding<BindingModel, string>(
            model,
            static source => source.Name,
            static (source, value) => source.Name = value.ToUpperInvariant(),
            Library.UiBindingMode.TwoWay);
        text.SetBinding("Text", binding);
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(text, textService);
        document.Layout(new(100, 20), 1);

        text.SetText("mixed");
        Assert.Equal("MIXED", model.Name, "two-way write updates the typed source immediately");
        Assert.Equal("mixed", text.Text, "two-way source normalization does not bypass the binding stage");
        document.Layout(new(100, 20), 1);
        Assert.Equal("MIXED", text.Text, "the next binding stage reveals the normalized source value");
    }

    private static void BindingStageSkipsCleanSubtrees()
    {
        var oneTimeModel = new BindingModel { Name = "one-time" };
        var oneTimeText = new Library.TextBlock();
        using var oneTimeBinding = new Library.UiCompiledBinding<BindingModel, string>(
            oneTimeModel,
            static model => model.Name,
            mode: Library.UiBindingMode.OneTime);
        oneTimeText.SetBinding("Text", oneTimeBinding);
        oneTimeModel.Name = "changed";
        oneTimeBinding.NotifyChanged();
        Assert.Equal("one-time", oneTimeText.Text, "external one-time binding does not subscribe through the interpreted runtime");

        var firstModel = new BindingModel { Name = "first" };
        var secondModel = new BindingModel { Name = "second" };
        var firstReads = 0;
        var secondReads = 0;
        var root = new Panel();
        var first = new TextBlock();
        var second = new TextBlock();
        root.Add(first);
        root.Add(second);
        using var firstBinding = new Library.UiCompiledBinding<BindingModel, string>(firstModel, model => { firstReads++; return model.Name; });
        using var secondBinding = new Library.UiCompiledBinding<BindingModel, string>(secondModel, model => { secondReads++; return model.Name; });
        first.AttachExternalBinding("Text", firstBinding);
        second.AttachExternalBinding("Text", secondBinding);
        var runtime = new UiRuntime(root);

        runtime.Layout(new(100, 40), 1);
        var firstInitialReads = firstReads;
        var secondInitialReads = secondReads;
        runtime.Layout(new(100, 40), 1);
        Assert.Equal(firstInitialReads, firstReads, "unchanged binding stage does not reread the first source");
        Assert.Equal(secondInitialReads, secondReads, "unchanged binding stage does not reread the second source");

        firstModel.Name = "updated";
        runtime.Layout(new(100, 40), 1);
        Assert.True(firstReads > firstInitialReads, "dirty binding branch refreshes its source");
        Assert.Equal(secondInitialReads, secondReads, "dirty binding branch does not refresh a clean sibling");
        Assert.Equal("updated", first.Text, "dirty binding value is applied before layout");
    }

    private static void GeneratedEditorAndGameHostPaths()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new Library.UiFontCatalog();
        fonts.Register("default", new TextContract.FontSourceId(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3")), File.ReadAllBytes(fontPath));

        using var editorText = new CountingTextService();
        using var editor = new DeltaXaml.Generated.EditorShellArtifact(editorText, fonts);
        ExerciseGeneratedDocument(editor.Document, editorText, new(960, 540), new(800, 450), "generated editor shell");

        using var gameText = new CountingTextService();
        using var game = new DeltaXaml.Generated.GameHudArtifact(gameText, fonts);
        ExerciseGeneratedDocument(game.Document, gameText, new(640, 360), new(480, 270), "generated game HUD");
        Assert.True(game.TryFindName("PauseButton", out var named) && named is Library.UiButton, "generated namescope resolves the HUD button");
        if (named is not Library.UiButton button)
        {
            throw new InvalidOperationException("Generated HUD button missing.");
        }

        var clicks = 0;
        button.Click += (_, _) => clicks++;
        var bounds = button.RetainedElement.Bounds;
        var down = LibraryContract.UiInputEvent.FromPointingDevice(Pointer(
            LibraryContract.UiPointerEventKind.ButtonDown,
            bounds.X + (bounds.Width * 0.5f),
            bounds.Y + (bounds.Height * 0.5f)));
        var up = LibraryContract.UiInputEvent.FromPointingDevice(Pointer(
            LibraryContract.UiPointerEventKind.ButtonUp,
            bounds.X + (bounds.Width * 0.5f),
            bounds.Y + (bounds.Height * 0.5f)));
        game.Document.Dispatch(in down);
        game.Document.Dispatch(in up);
        game.Document.Layout(new(480, 270), 1);
        Assert.Equal(1, clicks, "generated game HUD uses canonical input dispatch");

        using var customText = new EmptyTextService();
        using var custom = new DeltaXaml.Generated.CustomBadgeArtifact(customText);
        Assert.True(custom.Document.Root is LibraryCustomBadge { Label: "generated", SurfaceBrush.Kind: Library.UiBrushKind.Solid, Texture.IsValid: true, Radii.TopLeft: 1 }, "attributed custom type and extended literal metadata use direct generated construction");
        custom.Document.Layout(new(120, 32), 1);
        var customDisplay = custom.Document.BuildDisplayList();
        Assert.True(customDisplay.Visuals.Length == 1, "attributed custom type inherits typed common properties and canonical visual extraction");

        var model = new BindingModel { Name = "generated binding" };
        using var bindingText = new CountingTextService();
        using var bound = new DeltaXaml.Generated.BoundTextArtifact(model, bindingText, fonts);
        Assert.True(bound.Document.Root is Library.TextBox { Text: "GENERATED BINDING" }, "x:DataType binding and converter apply through generated typed accessors");
        model.Name = "updated binding";
        bound.Document.Layout(new(320, 80), 1);
        Assert.True(bound.Document.Root is Library.TextBox { Text: "UPDATED BINDING" }, "generated source notification refreshes through the binding stage");
        if (bound.Document.Root is not Library.TextBox boundEditor)
        {
            throw new InvalidOperationException("Generated bound text editor missing.");
        }

        boundEditor.SetText(" Round Trip ");
        Assert.Equal("Round Trip", model.Name, "generated two-way binding calls its typed backward converter");

        using var compositionText = new CountingTextService();
        using var composition = new DeltaXaml.Generated.CompositionArtifact(compositionText, fonts);
        composition.Document.Layout(new(320, 120), 1);
        var compositionDisplay = composition.Document.BuildDisplayList();
        Assert.True(compositionDisplay.Visuals.Length >= 2, "generated style contributes retained visual output");
        Assert.True(compositionDisplay.Text.Length == 1, "generated template contributes one retained text leaf");
        var templatedButton = (Library.UiButton)composition.Document.Root.Children[0];
        var templatedLabel = (Library.TextBlock)templatedButton.Children[0];
        Assert.Equal(templatedButton.Background, templatedLabel.Background, "TemplateBinding reads the owner through a generated relation plan");
        var initialButtonVisual = compositionDisplay.Visuals[1];
        Assert.True(composition.TryGetResourceId("Accent", out var accent), "generated artifact exposes its stable resource identity");
        composition.Resources.Set(accent, new Library.UiColor(80, 100, 120));
        composition.Document.Layout(new(320, 120), 1);
        var changedComposition = composition.Document.BuildDisplayList();
        Assert.True(!initialButtonVisual.Equals(changedComposition.Visuals[1]), "dynamic resource update invalidates the dependent style output");
        Assert.Equal(templatedButton.Background, templatedLabel.Background, "TemplateBinding follows owner style/resource changes");

    }

    private static void ExerciseGeneratedDocument(
        Library.UiDocument document,
        CountingTextService textService,
        Delta.Maths.float2 initialViewport,
        Delta.Maths.float2 resizedViewport,
        string scenario)
    {
        document.Layout(initialViewport, 1);
        var storage = document.DisplayListStorage;
        var first = document.BuildDisplayList();
        Assert.True(first.Visuals.Length > 0, $"{scenario} emits visuals");
        Assert.True(first.Clips.Length > 0, $"{scenario} emits clips");
        Assert.True(first.Text.Length > 0, $"{scenario} emits neutral shaped text");
        AssertDisplayListWithinViewport(first, initialViewport, scenario);
        var firstShapeCount = textService.ShapeCount;
        var firstText = first.Text[0].Text;

        document.Layout(initialViewport, 1);
        var unchanged = document.BuildDisplayList();
        Assert.True(ReferenceEquals(storage, document.DisplayListStorage), $"{scenario} retains borrowed display-list storage");
        Assert.Equal(firstShapeCount, textService.ShapeCount, $"{scenario} does not reshape unchanged text");
        Assert.True(ReferenceEquals(firstText, unchanged.Text[0].Text), $"{scenario} reuses unchanged shaped text");

        document.Layout(resizedViewport, 1);
        var resized = document.BuildDisplayList();
        AssertDisplayListWithinViewport(resized, resizedViewport, $"{scenario} after resize");
    }

    private static void AssertDisplayListWithinViewport(
        LibraryContract.UiDisplayList displayList,
        Delta.Maths.float2 viewport,
        string scenario)
    {
        const float tolerance = 0.001f;
        for (var i = 0; i < displayList.Visuals.Length; i++)
        {
            var bounds = displayList.Visuals[i].Bounds;
            Assert.True(
                bounds.x >= -tolerance && bounds.y >= -tolerance &&
                bounds.x + bounds.z <= viewport.x + tolerance &&
                bounds.y + bounds.w <= viewport.y + tolerance,
                $"{scenario} visual {i} stays inside the viewport");
        }

        for (var i = 0; i < displayList.Clips.Length; i++)
        {
            var bounds = displayList.Clips[i].Bounds;
            Assert.True(
                bounds.x >= -tolerance && bounds.y >= -tolerance &&
                bounds.x + bounds.z <= viewport.x + tolerance &&
                bounds.y + bounds.w <= viewport.y + tolerance,
                $"{scenario} clip {i} stays inside the viewport");
        }
    }

    private static void PublicResourcesStylesTemplatesAndTypes()
    {
        var resources = new Library.UiResourceCatalog();
        var firstColor = new Library.UiColor(10, 20, 30);
        var secondColor = new Library.UiColor(40, 50, 60);
        resources.Set("TextColor", firstColor);
        var style = new Library.UiStyle("Body", "TextBlock", resources);
        style.SetResource("Foreground", "TextColor");
        style.Set("FontSize", 18f);
        var hoverColor = new Library.UiColor(70, 80, 90);
        style.SetState(Library.UiStyleState.Hover, "Foreground", hoverColor);
        var alternateStyle = new Library.UiStyle("Alternate", "TextBlock");
        alternateStyle.Set("FontSize", 22f);
        var theme = new Library.UiTheme(resources);
        theme.Add(style);
        theme.Add(alternateStyle);

        var deepRoot = new Library.UiPanel();
        var deep = deepRoot;
        for (var i = 0; i < 2048; i++)
        {
            var child = new Library.UiPanel { StyleKey = "Node" };
            deep.Add(child);
            deep = child;
        }

        var deepStyle = new Library.UiStyle("Node", "Panel");
        deepStyle.Set("Width", 17f);
        theme.Add(deepStyle);
        theme.Apply(deepRoot);
        Assert.Equal(17f, deep.Width, "theme application traverses deep trees without recursive stack growth");

        var text = new Library.TextBlock { StyleKey = "Body" };
        var other = new Library.TextBlock();
        theme.Apply(text);
        Assert.Equal(firstColor, text.Foreground, "resource-backed style resolves through the user API");
        Assert.Equal(18f, text.FontSize, "style value uses the retained style source");
        var unchangedStyleVersion = text.RetainedElement.OutputVersion;
        style.Set("FontSize", 20f);
        using var styleTextService = new EmptyTextService();
        using var styleDocument = new Library.UiDocument(text, styleTextService, null, theme);
        styleDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal(20f, text.FontSize, "changing an applied style refreshes its retained value");
        Assert.True(text.RetainedElement.OutputVersion > unchangedStyleVersion, "changed style invalidates the dependent retained element");
        styleDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal(0, theme.LastRefreshCount, "unchanged style frame skips the style tree");
        var changedStyleVersion = text.RetainedElement.OutputVersion;
        style.Set("FontSize", 20f);
        styleDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal(changedStyleVersion, text.RetainedElement.OutputVersion, "equal style assignment does not add invalidation");
        text.StyleKey = "Alternate";
        styleDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal(22f, text.FontSize, "changing StyleKey applies the replacement style");
        text.StyleKey = "Missing";
        styleDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal(14f, text.FontSize, "missing StyleKey clears the previous style source");
        text.StyleKey = "Body";
        styleDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        resources.Set("TextColor", secondColor);
        Assert.Equal(firstColor, text.Foreground, "attached resource callback does not mutate effective state before the style stage");
        styleDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal(secondColor, text.Foreground, "resource stage updates the dependent style value");
        Assert.True((text.RetainedElement.DirtyFlags & UiDirtyFlags.Measure) == 0, "foreground resource change does not invalidate measure");
        Assert.Equal(new Library.UiColor(255, 255, 255), other.Foreground, "unrelated element is not changed by a resource update");

        text.RetainedElement.SetHovered(true);
        styleDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal(1, theme.LastRefreshCount, "state invalidation visits only the changed style subtree");
        Assert.Equal(hoverColor, text.Foreground, "visual state overrides the base style after input state changes");
        text.RetainedElement.SetHovered(false);
        styleDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal(1, theme.LastRefreshCount, "leaving a visual state visits only the changed style subtree");
        Assert.Equal(secondColor, text.Foreground, "leaving a visual state restores the base resource style");

        var styleRoot = new Library.UiPanel();
        var styled = new Library.TextBlock { StyleKey = "Body" };
        var unrelated = new Library.TextBlock { StyleKey = "Alternate" };
        styleRoot.Add(styled);
        styleRoot.Add(unrelated);
        theme.Apply(styleRoot);
        using var targetedStyleTextService = new EmptyTextService();
        using var targetedStyleDocument = new Library.UiDocument(styleRoot, targetedStyleTextService, null, theme);
        targetedStyleDocument.Layout(new Delta.Maths.float2(100, 60), 1);
        style.Set("FontSize", 19f);
        targetedStyleDocument.Layout(new Delta.Maths.float2(100, 60), 1);
        Assert.Equal(2, theme.LastRefreshCount, "style change refreshes the root and only the dependent styled element");
        Assert.Equal(19f, styled.FontSize, "dependent element receives the changed style value");
        Assert.Equal(22f, unrelated.FontSize, "unrelated style remains unchanged");

        var loader = new Library.XamlLoader();
        var dynamicLoaded = loader.Load(
            "<TextBlock Foreground=\"{DynamicResource TextColor}\" />",
            new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), resources));
        Assert.True(dynamicLoaded.Success && dynamicLoaded.Root is Library.TextBlock, "DynamicResource markup loads through the public facade");
        if (dynamicLoaded.Root is not Library.TextBlock dynamicText) { throw new InvalidOperationException("dynamic resource text missing"); }
        Assert.Equal(secondColor, dynamicText.Foreground, "DynamicResource uses the named resource catalog");
        var dynamicTextVersion = dynamicText.RetainedElement.TextVersion;
        resources.Set("TextColor", firstColor);
        Assert.Equal(firstColor, dynamicText.Foreground, "DynamicResource remains live after a catalog update");
        Assert.True(dynamicText.RetainedElement.TextVersion > dynamicTextVersion, "DynamicResource foreground changes invalidate text style identity");
        var staticLoaded = loader.Load(
            "<TextBlock Foreground=\"{StaticResource TextColor}\" />",
            new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), resources));
        Assert.True(staticLoaded.Success && staticLoaded.Root is Library.TextBlock, "StaticResource markup loads through the public facade");
        if (staticLoaded.Root is not Library.TextBlock staticText) { throw new InvalidOperationException("static resource text missing"); }
        Assert.Equal(firstColor, staticText.Foreground, "StaticResource resolves its initial value");
        resources.Set("TextColor", secondColor);
        Assert.Equal(firstColor, staticText.Foreground, "StaticResource does not subscribe to later catalog changes");
        resources.Set("Alias", new Library.UiResourceReference("TextColor"));
        Assert.True(resources.TryResolve("Alias", out var aliasValue) && aliasValue is Library.UiColor, "resource aliases resolve through the public catalog");
        resources.Set("CycleA", new Library.UiResourceReference("CycleB"));
        resources.Set("CycleB", new Library.UiResourceReference("CycleA"));
        var cycle = loader.Load(
            "<TextBlock Foreground=\"{DynamicResource CycleA}\" />",
            new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), resources));
        Assert.True(!cycle.Success && cycle.Diagnostics.Length == 1 && cycle.Diagnostics.Span[0].Code.Value == "XAML006", "resource cycles are reported instead of producing a partial tree");

        var types = new Library.XamlTypeCatalog();
        types.Register(new Library.XamlQualifiedName("urn:sample", "Badge"), static () => new Library.UiBorder());
        Assert.True(types.TryResolveName(new Library.XamlQualifiedName("urn:sample", "Badge"), out var badgeType) && types.TryCreate(badgeType, out _), "custom type catalog resolves and creates directly");
        var custom = loader.Load(
            "<Badge xmlns=\"urn:sample\" />",
            new Library.XamlLoadContext(types, resources));
        Assert.True(custom.Success && custom.Root is Library.UiBorder, "registered custom type is created without reflection discovery");

        var grid = loader.Load(
            "<Grid Columns=\"40,*,Auto\" Rows=\"Auto,*\"><TextBlock Text=\"Name\" /><NumericEditor Value=\"2\" Minimum=\"0\" Maximum=\"10\" /></Grid>",
            new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), resources));
        Assert.True(grid.Success && grid.Root is Library.UiGrid && grid.Root.Children.Count == 2, "XAML grid and numeric control attributes load together");
        if (grid.Root is not Library.UiGrid loadedGrid) { throw new InvalidOperationException("grid root missing"); }
        Assert.Equal(2, loadedGrid.Children.Count, "grid fixture retains both declared children");
        Assert.True(loadedGrid.Children[1] is Library.UiNumericEditor numericFromXaml && numericFromXaml.CurrentValue == 2, "numeric XAML attributes initialize the editor");

        var host = new Library.UiContentControl { TemplateKey = "Label" };
        theme.RegisterTemplate("Label", new Library.UiTemplate(new LabelTemplateFactory()));
        theme.Apply(host);
        Assert.True(host.Content is Library.TextBlock templated && templated.Text == "templated", "template creates one retained child");
        theme.RegisterTemplate("OtherLabel", new Library.UiTemplate(new ReplacementTemplateFactory()));
        using var templateTextService = new EmptyTextService();
        using var templateDocument = new Library.UiDocument(host, templateTextService, null, theme);
        host.TemplateKey = "OtherLabel";
        templateDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.True(host.Content is Library.TextBlock replacement && replacement.Text == "replacement", "template key change replaces only the retained template child");
        host.TemplateKey = null;
        templateDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.True(host.Content is null, "clearing template key removes the retained template child");

        var items = new Library.UiItemsControl();
        var created = 0;
        var values = new object?[] { "one", "two" };
        items.SetItems(values, value =>
        {
            created++;
            return new Library.TextBlock { Text = value?.ToString() ?? string.Empty };
        });
        items.SetItems(values, value =>
        {
            created++;
            return new Library.TextBlock { Text = value?.ToString() ?? string.Empty };
        });
        Assert.Equal(2, created, "unchanged collection items reuse retained rows");
        items.SetItems(new object?[] { "one", "three" }, value =>
        {
            created++;
            return new Library.TextBlock { Text = value?.ToString() ?? string.Empty };
        });
        Assert.Equal(3, created, "only changed collection item creates a replacement row");

        var clipboard = new Library.UiClipboard();
        var editor = new Library.TextBox { Clipboard = clipboard };
        editor.SetText("hello");
        editor.SelectAll();
        editor.Copy();
        Assert.Equal("hello", clipboard.ReadText(), "public text editor uses the platform-neutral clipboard");
        editor.SetText("changed");
        Assert.True(editor.Undo() && editor.Text == "hello", "public text editor exposes retained undo");

        var hostWrite = new Library.TextBlock { Text = "before" };
        var textHandle = hostWrite.GetHandle("Text");
        Assert.True(textHandle.IsValid && hostWrite.TrySet(textHandle, "after", out var handleDiagnostic) && handleDiagnostic is null && hostWrite.Text == "after", "public handle performs a generation-safe host write");

        var composition = new Library.UiPanel();
        var compositionText = new Library.TextBlock { Text = "stable" };
        composition.Add(compositionText);
        Assert.True(ReferenceEquals(composition.Children, composition.Children), "public children view is retained");
        Assert.True(ReferenceEquals(compositionText, composition.Children[0]), "code-authored child wrapper is retained");
        Assert.True(ReferenceEquals(composition, compositionText.Parent), "parent wrapper is retained");

        var numeric = new Library.UiNumericEditor();
        numeric.Initialize(2);
        Assert.True(!numeric.TryCommitText("not-a-number") && numeric.HasValidationError && numeric.CurrentValue == 2, "numeric editor preserves the committed value on validation failure");
        Assert.True(!numeric.TryCommitText("NaN") && numeric.CurrentValue == 2, "numeric editor rejects non-finite values");
        Assert.True(numeric.TryCommitText("3") && numeric.CurrentValue == 3 && numeric.Increment(), "numeric editor commits and increments through the user API");
    }

    private static void FrameContractAndBatchedMutations()
    {
        var text = new TextBlock { Width = 100, Height = 20, Focusable = true };
        var other = new TextBlock { Width = 100, Height = 20 };
        var root = new Panel();
        root.Add(text);
        root.Add(other);
        var frame = new UiRuntime(root);
        var handle = text.GetHandle("Text");
        var otherHandle = other.GetHandle("Text");
        frame.Enqueue(new UiMutation(handle, "value", UiDirtyFlags.Binding | UiDirtyFlags.Visual));
        frame.Enqueue(new UiMutation(otherHandle, "other", UiDirtyFlags.Binding | UiDirtyFlags.Visual));
        Assert.Equal(2, frame.PendingMutationCount, "multiple mutations are queued");
        frame.ApplyMutations();
        Assert.Equal(2, frame.AppliedMutationCount, "queued mutations applied");
        Assert.Equal(0, frame.RejectedMutationCount, "valid mutation accepted");
        var rootNodeId = new UiNodeId(root.Id.Value, root.Generation);
        var textNodeId = new UiNodeId(text.Id.Value, text.Generation);
        var otherNodeId = new UiNodeId(other.Id.Value, other.Generation);
        Assert.Equal(3, frame.NodeCount, "node store registers each live element once");
        Assert.True(frame.TryGetNode(rootNodeId, out var rootNode) && rootNode.FirstLogicalChild == textNodeId, "node store records the first logical child");
        Assert.True(frame.TryGetNode(textNodeId, out var textNode) && textNode.LogicalParent == rootNodeId && textNode.NextLogicalSibling == otherNodeId, "node store records logical parent and sibling links");
        Assert.True(textNode.VisualParent == rootNodeId && rootNode.FirstVisualChild == textNodeId, "node store records the visual relation in the same node record");
        Assert.Equal(new UiRuntimeTypeIndex(1), textNode.RuntimeType, "node record retains the compact text descriptor index");
        text.DirtyFlags = UiDirtyFlags.None;
        text.Invalidate(UiDirtyFlags.Visual);
        Assert.True(frame.TryGetNode(textNodeId, out var dirtyTextNode) && (dirtyTextNode.Dirty & UiDirtyFlags.Visual) != 0, "node lookup reports current element dirty state without rebuilding the index");
        Assert.True(UiDescriptorCatalog.TryResolve(textNode.RuntimeType, out var textDescriptor) && textDescriptor.Supports(UiDescriptorCapabilities.Measure | UiDescriptorCapabilities.Arrange | UiDescriptorCapabilities.Visual), "node descriptor resolves typed text operations");
        Assert.Equal(new UiRuntimeTypeIndex(5), rootNode.RuntimeType, "node record retains the compact panel descriptor index");
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
        Assert.True(!frame.TryResolve(otherHandle, out _), "removed element is absent from the frame identity index");
        Assert.True(frame.TryResolve(handle, out var indexed) && ReferenceEquals(indexed, text), "frame resolves a live handle through the dense node index");
        Assert.Equal(2, frame.NodeCount, "node store clears only the removed element slot through incremental removal");
        frame.Layout(new(100, 20), 1);
        Assert.Equal(new UiRect(0, 0, 100, 20), text.Bounds, "frame layout boundary");
        var down = Pointer(LibraryContract.UiPointerEventKind.ButtonDown, 10, 10);
        frame.Input.RoutePointer(in down);
        if (frame.Input.Focused is not { } focused || frame.Input.Captured is not { } captured)
        {
            throw new InvalidOperationException("Frame input did not focus and capture element.");
        }

        Assert.Equal(text.Id, focused, "frame input focuses element");
        Assert.Equal(text.Id, captured, "frame input captures pointer");
        var up = Pointer(LibraryContract.UiPointerEventKind.ButtonUp, 10, 10);
        frame.Input.RoutePointer(in up);
        Assert.True(frame.Input.Captured is null, "frame input releases capture");
        frame.Input.Focus(text.Id);
        text.IsEnabled = false;
        frame.Layout(new(100, 20), 1);
        Assert.True(frame.Input.Focused is null, "focus stage releases disabled focus");
    }

    private static void ParticipationBoundary()
    {
        var root = new Library.UiPanel { Background = new Library.UiColor(10, 20, 30) };
        root.Add(new Library.TextBlock { Text = "hidden" });
        root.Participation = Library.UiParticipation.None;
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(root, textService);
        document.Layout(new Delta.Maths.float2(100, 40), 1);
        Assert.True(document.TryBuildDisplayList(out var displayList, out var diagnostic) && diagnostic is null, "non-participating root still builds an empty display list");
        Assert.Equal(0, displayList.Visuals.Length, "non-participating root has no visuals");
        Assert.Equal(0, displayList.Clips.Length, "non-participating root has no clips");

        var invalid = false;
        try
        {
            root.Participation = Library.UiParticipation.Rendering;
        }
        catch (ArgumentException)
        {
            invalid = true;
        }

        Assert.True(invalid, "rendering requires layout participation");
    }

    private static void RuntimeLayoutQueuesAreNonRecursive()
    {
        var queueNode = new UiNodeId(17, 3);
        var deduplicatedMeasure = new UiMeasureQueueBuffer();
        deduplicatedMeasure.Add(new(queueNode, new(10, 10)));
        deduplicatedMeasure.Add(new(queueNode, new(20, 30)));
        Assert.Equal(1, deduplicatedMeasure.Count, "measure queue deduplicates a node within one stage pass");
        Assert.Equal(new UiSize(20, 30), deduplicatedMeasure[0].Available, "measure queue keeps the latest constraint for a node");

        var deduplicatedArrange = new UiArrangeQueueBuffer();
        deduplicatedArrange.Add(new(queueNode, new(1, 2, 3, 4), new(1, 2, 3, 4)));
        deduplicatedArrange.Add(new(queueNode, new(5, 6, 7, 8), new(5, 6, 7, 8)));
        Assert.Equal(1, deduplicatedArrange.Count, "arrange queue deduplicates a node within one stage pass");
        Assert.Equal(new UiRect(5, 6, 7, 8), deduplicatedArrange[0].Bounds, "arrange queue keeps the latest bounds for a node");
        Assert.Equal(new UiRect(5, 6, 7, 8), deduplicatedArrange[0].Clip, "arrange queue keeps the latest effective clip for a node");

        var root = new Panel();
        var current = root;
        const int depth = 2048;
        for (var i = 0; i < depth; i++)
        {
            var child = new Panel { Width = 10, Height = 10 };
            current.Add(child);
            current = child;
        }

        var measureQueue = new UiMeasureQueueBuffer();
        var nodes = new UiNodeStore(root);
        var childOrder = new List<UiNodeId>();
        UiMeasureStage.Run(nodes, root, new(100, 100), measureQueue, childOrder);
        Assert.Equal(depth + 1, measureQueue.Count, "measure stage visits the deep tree without recursive calls");
        Assert.Equal(new UiNodeId(root.Id.Value, root.Generation), measureQueue[0].Element, "measure queue is keyed by the root node identity");

        var arrangeQueue = new UiArrangeQueueBuffer();
        UiArrangeStage.Run(nodes, root, new(0, 0, 100, 100), arrangeQueue);
        Assert.Equal(depth + 1, arrangeQueue.Count, "arrange stage visits the deep tree without recursive calls");

        UiMeasureStage.Run(nodes, root, new(100, 100), measureQueue, childOrder);
        UiArrangeStage.Run(nodes, root, new(0, 0, 100, 100), arrangeQueue);
        Assert.Equal(0, measureQueue.Count, "clean measure stage does not rescan the retained tree");
        Assert.Equal(0, arrangeQueue.Count, "clean arrange stage does not rescan the retained tree");

        var leaf = current;
        for (UiElement? ancestor = leaf; ancestor is not null; ancestor = ancestor.Parent as UiElement)
        {
            ancestor.DirtyFlags = UiDirtyFlags.None;
        }

        leaf.Invalidate(UiDirtyFlags.Visual);
        Assert.True((root.DirtyFlags & UiDirtyFlags.Visual) != 0, "deep invalidation reaches the root without recursive propagation");

        var preciseRoot = new Panel();
        var cleanChild = new TextBlock { Width = 10, Height = 10 };
        var dirtyChild = new TextBlock { Width = 10, Height = 10 };
        preciseRoot.Add(cleanChild);
        preciseRoot.Add(dirtyChild);
        var preciseMeasure = new UiMeasureQueueBuffer();
        var preciseArrange = new UiArrangeQueueBuffer();
        var preciseNodes = new UiNodeStore(preciseRoot);
        var preciseChildOrder = new List<UiNodeId>();
        UiMeasureStage.Run(preciseNodes, preciseRoot, new(100, 100), preciseMeasure, preciseChildOrder);
        UiArrangeStage.Run(preciseNodes, preciseRoot, new(0, 0, 100, 100), preciseArrange);
        UiMeasureStage.Run(preciseNodes, preciseRoot, new(100, 100), preciseMeasure, preciseChildOrder);
        UiArrangeStage.Run(preciseNodes, preciseRoot, new(0, 0, 100, 100), preciseArrange);
        dirtyChild.Width = 20;
        UiMeasureStage.Run(preciseNodes, preciseRoot, new(100, 100), preciseMeasure, preciseChildOrder);
        Assert.Equal(2, preciseMeasure.Count, "measure queue contains the root and only the dirty child");
        UiArrangeStage.Run(preciseNodes, preciseRoot, new(0, 0, 100, 100), preciseArrange);
        Assert.Equal(2, preciseArrange.Count, "arrange queue contains the root and only the dirty child");
        UiMeasureStage.Run(preciseNodes, preciseRoot, new(200, 100), preciseMeasure, preciseChildOrder);
        Assert.True(preciseMeasure.Count > 0, "viewport resize invalidates measure even when no dirty flag was pending");
    }

    private static void NodeStagesFollowStructuralMutations()
    {
        var root = new Panel();
        var runtime = new UiRuntime(root);
        var child = new TextBlock { Text = "late" };

        root.Add(child);
        runtime.Layout(new(100, 40), 1.5f);
        Assert.Equal(2, runtime.NodeCount, "binding and scale stages see an incrementally registered child");
        Assert.Equal(1.5f, child.DpiScale, "scale stage visits a child through the node links");
        Assert.Equal(new UiRect(0, 0, 100, 40), child.Bounds, "new child is laid out after node refresh");

        var stableTreeVersion = root.TreeVersion;
        runtime.Layout(new(100, 40), 1.5f);
        Assert.Equal(stableTreeVersion, root.TreeVersion, "unchanged layout does not mutate the tree version");

        root.Remove(child);
        child.SetLayoutScale(1f);
        runtime.Layout(new(100, 40), 2f);
        Assert.Equal(1, runtime.NodeCount, "binding and scale stages see an incrementally removed node");
        Assert.Equal(1f, child.DpiScale, "detached child is not processed by the node-backed scale stage");
    }

    private static void NodeStoreHasSingleOwner()
    {
        var root = new Panel();
        var initialChild = new Panel();
        var initialGrandchild = new TextBlock { Text = "nested" };
        initialChild.Add(initialGrandchild);
        root.Add(initialChild);
        var first = new UiNodeStore(root);
        try
        {
            Assert.Equal(0, root.DetachedChildren.Count, "attached root releases its composition child storage");
            Assert.Equal(0, initialChild.DetachedChildren.Count, "attached descendants release their composition child storage");
            Assert.True(ReferenceEquals(initialChild, root.Children[0]) && ReferenceEquals(root, initialChild.Parent), "public relations read the authoritative node store");
            Assert.True(ReferenceEquals(initialGrandchild, initialChild.Children[0]), "nested public relations read the authoritative node store");
            var rejected = false;
            try
            {
                _ = new UiNodeStore(root);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Assert.True(rejected, "a retained root rejects a second authoritative node store");
            root.Add(new TextBlock { Text = "owned" });
            Assert.Equal(4, first.Count, "the original node store remains authoritative after a rejected attach");

            Assert.True(root.Remove(initialChild), "attached subtree can be detached through the authoritative store");
            Assert.True(initialChild.NodeStore is null && initialGrandchild.NodeStore is null, "detached subtree releases the document node store");
            Assert.True(ReferenceEquals(initialGrandchild, initialChild.Children[0]) && ReferenceEquals(initialChild, initialGrandchild.Parent), "detached subtree reconstructs its cold composition relations");
            Assert.Equal(2, first.Count, "detaching a subtree removes its complete node range");
        }
        finally
        {
            first.Detach();
        }

        var replacement = new UiNodeStore(root);
        replacement.Detach();
    }

    private static void NodeStoreOwnsDetachedRoot()
    {
        var parent = new Panel();
        var nested = new Panel();
        parent.Add(nested);
        var nestedRejected = false;
        try
        {
            _ = new UiNodeStore(nested);
        }
        catch (InvalidOperationException)
        {
            nestedRejected = true;
        }

        Assert.True(nestedRejected, "a node store rejects an element nested below another retained root");

        var ownedRoot = new Panel();
        var store = new UiNodeStore(ownedRoot);
        try
        {
            var reparentRejected = false;
            try
            {
                parent.Add(ownedRoot);
            }
            catch (InvalidOperationException)
            {
                reparentRejected = true;
            }

            Assert.True(reparentRejected, "an attached document root cannot be reparented");
            Assert.True(ownedRoot.Parent is null, "rejected reparenting leaves the document root detached");
        }
        finally
        {
            store.Detach();
        }
    }

    private static void NodeStoreRejectsCrossDocumentReparenting()
    {
        var sourceRoot = new Panel();
        var sourceParent = new Panel();
        var child = new TextBlock { Text = "owned" };
        sourceRoot.Add(sourceParent);
        sourceParent.Add(child);
        var sourceStore = new UiNodeStore(sourceRoot);
        var targetRoot = new Panel();
        var targetStore = new UiNodeStore(targetRoot);
        try
        {
            var rejected = false;
            try
            {
                targetRoot.Add(child);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Assert.True(rejected, "an attached document rejects direct cross-document reparenting");
            Assert.True(ReferenceEquals(child.Parent, sourceParent), "rejected cross-document reparenting preserves the original parent");
            Assert.Equal(3, sourceStore.Count, "the source node store keeps the child after rejected reparenting");
            Assert.Equal(1, targetStore.Count, "the target node store does not register a rejected child");

            Assert.True(sourceParent.Remove(child), "explicit detachment removes the child from the source document");
            targetRoot.Add(child);
            Assert.Equal(2, targetStore.Count, "an explicitly detached child can be attached to the target document");
        }
        finally
        {
            sourceStore.Detach();
            targetStore.Detach();
        }
    }

    private static void DisplayExtractionFollowsNodeLinksAfterTreeMutation()
    {
        var root = new Library.UiPanel { Width = 100, Height = 40, Background = new(1, 2, 3) };
        var firstChild = new Library.UiPanel { Width = 20, Height = 10, Background = new(4, 5, 6) };
        var secondChild = new Library.UiPanel { Width = 30, Height = 10, Background = new(7, 8, 9) };
        root.Add(firstChild);
        root.Add(secondChild);
        using var textService = new EmptyTextService();
        using var document = new Library.UiDocument(root, textService);

        document.Layout(new Delta.Maths.float2(100, 40), 1);
        var initial = document.BuildDisplayList();
        Assert.Equal(3, initial.Visuals.Length, "initial visual extraction follows retained child order");
        var initialFirst = initial.Visuals[1];
        var initialSecond = initial.Visuals[2];

        var lateChild = new Library.UiPanel { Width = 40, Height = 10, Background = new(10, 11, 12) };
        root.Add(lateChild);
        document.Layout(new Delta.Maths.float2(100, 40), 1);
        var added = document.BuildDisplayList();
        Assert.Equal(4, added.Visuals.Length, "visual extraction refreshes after a child is added");
        Assert.Equal(initialFirst with { Clip = added.Visuals[1].Clip }, added.Visuals[1], "first child visual remains in stable order");
        Assert.Equal(initialSecond with { Clip = added.Visuals[2].Clip }, added.Visuals[2], "second child visual remains in stable order");
        var lateVisual = added.Visuals[3];

        root.Remove(firstChild);
        document.Layout(new Delta.Maths.float2(100, 40), 1);
        var removed = document.BuildDisplayList();
        Assert.Equal(3, removed.Visuals.Length, "visual extraction refreshes after a child is removed");
        Assert.Equal(initialSecond with { Clip = removed.Visuals[1].Clip }, removed.Visuals[1], "remaining child visual follows node-store sibling links");
        Assert.Equal(lateVisual with { Clip = removed.Visuals[2].Clip }, removed.Visuals[2], "late child visual remains last after removal");
    }

    private static void DescriptorLayoutDispatch()
    {
        var roots = new UiElement[]
        {
            new Panel(),
            new StackPanel(),
            new Border { Padding = new(2, 2, 2, 2) },
            new Grid(),
            new ContentControl(),
            new Button(),
            new ItemsControl(),
            new ScrollViewer(),
        };

        foreach (var root in roots)
        {
            root.Width = 100;
            root.Height = 40;
            if (root is not ItemsControl)
            {
                root.Add(new TextBlock { Text = "layout" });
            }

            var runtime = new UiRuntime(root);
            runtime.Layout(new(100, 40), 1);
            Assert.Equal(new UiRect(0, 0, 100, 40), root.Bounds, $"descriptor layout bounds for {root.TypeName}");
        }

        var text = new TextBlock { Text = "typed" };
        var textRuntime = new UiRuntime(text);
        textRuntime.Layout(new(100, 40), 1);
        Assert.True(text.DesiredSize.Width > 0 && text.DesiredSize.Height > 0, "text descriptor dispatch produces desired size");
    }

    private static void DescriptorLayoutEntryPointsAreExclusive()
    {
        var flags = System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        Assert.True(typeof(UiElement).GetMethod("ExecuteMeasure", flags) is null, "layout enters through the stateless measure stage");
        Assert.True(typeof(UiElement).GetMethod("ExecuteArrange", flags) is null, "arrange enters through the stateless arrange stage");
    }

    private static void DescriptorPropertyDispatchIsNonVirtual()
    {
        var flags = System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        Assert.True(typeof(UiElement).GetMethod("TryApplyTypedProperty", flags) is null, "typed property writes enter through the descriptor catalog");

        var stack = new StackPanel();
        stack.SetLocal("Orientation", UiOrientation.Horizontal, UiDirtyFlags.Measure);
        Assert.Equal(UiOrientation.Horizontal, stack.Orientation, "descriptor applies stack property");

        var numeric = new NumericEditor();
        numeric.SetLocal("Value", 3d, UiDirtyFlags.Visual);
        Assert.Equal(3d, numeric.Value, "descriptor applies numeric property");
    }

    private static void ControlStateOwnersAreFlat()
    {
        var retainedControls = new[]
        {
            typeof(Panel), typeof(StackPanel), typeof(ItemsControl), typeof(Border),
            typeof(Grid), typeof(ContentControl), typeof(Button), typeof(ToggleButton),
            typeof(TextBlock), typeof(TextBox), typeof(NumericEditor), typeof(ScrollViewer),
        };
        for (var i = 0; i < retainedControls.Length; i++)
        {
            Assert.Equal(typeof(UiElement), retainedControls[i].BaseType, $"{retainedControls[i].Name} owns composite state without algorithm inheritance");
        }

        var publicControls = new[]
        {
            typeof(Library.UiPanel), typeof(Library.UiStackPanel), typeof(Library.UiItemsControl),
            typeof(Library.UiBorder), typeof(Library.UiGrid), typeof(Library.UiContentControl),
            typeof(Library.UiButton), typeof(Library.UiToggleButton), typeof(Library.TextBlock), typeof(Library.TextBox),
            typeof(Library.UiNumericEditor), typeof(Library.UiScrollViewer),
        };
        for (var i = 0; i < publicControls.Length; i++)
        {
            Assert.Equal(typeof(Library.UiElement), publicControls[i].BaseType, $"{publicControls[i].Name} is a flat public accessor shell");
        }
    }
}
