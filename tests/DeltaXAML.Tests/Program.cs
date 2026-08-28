using System.ComponentModel;
using Delta.Text;
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

sealed class LibraryCustomBadge : Library.UiElement { }

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

sealed class EditorShellTypeResolver : Library.IXamlTypeResolver
{
    private static readonly Library.UiTypeId EditorShellType = new(new Guid("A6D7C0B8-0A6A-46DC-9C72-726C1E6A2B4A"));

    public bool TryResolveName(in Library.XamlQualifiedName name, out Library.UiTypeId type)
    {
        if (name.Namespace == "urn:delta-editor-shell" && name.LocalName == "EditorShell")
        {
            type = EditorShellType;
            return true;
        }

        type = default;
        return false;
    }

    public bool TryCreate(Library.UiTypeId type, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Library.UiElement? element)
    {
        if (type == EditorShellType)
        {
            element = new Library.UiPanel();
            return true;
        }

        element = null;
        return false;
    }
}

sealed class BindingModel : INotifyPropertyChanged
{
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
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

internal static partial class Program
{
    public static void Main()
    {
        ArchitectureGate.Run();
        XamlSemanticCompilerTests();
        XamlGeneratorTests();
        TextBlockArchitectureTests.Run();
        StackPanelArchitectureTests.Run();
        ContentLayoutArchitectureTests.Run();
        PanelGridArchitectureTests.Run();
        ButtonArchitectureTests.Run();
        EditingArchitectureTests.Run();
        ItemsScrollArchitectureTests.Run();
        TypedPropertyStateTests();
        PropertyPrecedenceTests();
        HiddenBindingTests();
        AnimationSourceTests();
        RejectedTypedSourceTests();
        PropertyInvalidation();
        GridSizing();
        TextClipboardUndoAndValidation();
        PointerFocusAndDispatch();
        ScrollAndClips();
        TextDisplayListUsesDeltaText();
        PublicDisplayListWarmFrameHasNoAllocations();
        BindingExpressionsAndContexts();
        EditorShellLibrarySlice();
        HandlesCompiledBindingsAndCustomTypes();
        ResourceLookupDiagnostics();
        ResourceBackedPrecedenceAndXaml();
        PublicResourcesStylesTemplatesAndTypes();
        LibraryFacadeSmoke();
        FrameContractAndBatchedMutations();
        ParticipationBoundary();
    }

    private static void PropertyInvalidation()
    {
        Assert.True(typeof(IUiElement).GetProperty("Id") is not null, "element identity remains in the base contract");
        Assert.True(typeof(IUiElement).GetProperty("Generation") is not null, "element generation remains in the base contract");
        Assert.True(typeof(IUiElement).GetProperty("DirtyFlags") is null, "dirty flags are not part of the external element contract");
        Assert.True(typeof(UiElement).GetProperty("DirtyFlags") is null, "dirty flags are internal to the retained implementation");
        var defaultHandle = default(Library.UiPropertyHandle);
        Assert.True(!defaultHandle.IsValid && defaultHandle.Name.Length == 0, "default public handle has a safe empty name");
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
        var frame = new UiRuntime(root);
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

    private static void LibraryFacadeSmoke()
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

        var resourceId = new LibraryContract.UiResourceId(new Guid("F25871B4-23F7-4DA5-ADED-AF3F38C4E55F"));
        var resourceContext = new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), new FixedLibraryResourceResolver(resourceId, new UiColor(7, 8, 9)));
        var resource = loader.Load($"<TextBlock ForegroundResource=\"{resourceId.Value:D}\" />", in resourceContext);
        Assert.True(resource.Success && resource.Root is not null, "library resource resolver is consumed by loader");
        var missingResource = loader.Load($"<TextBlock ForegroundResource=\"{resourceId.Value:D}\" />", in context);
        Assert.True(!missingResource.Success && missingResource.Diagnostics.Length == 1 && missingResource.Diagnostics.Span[0].Code.Value == "XAML006", "missing canonical resource is diagnostic");

        using var textDocument = new EmptyTextService();
        var textRoot = loader.Load("<TextBlock Text=\"Hello\" />", in context).Root;
        if (textRoot is null) { throw new InvalidOperationException("library text root missing"); }
        using var textDocumentOwner = new Library.UiDocument(textRoot, textDocument);
        textDocumentOwner.Layout(new Delta.Maths.float2(80, 20), 1);
        Assert.True(!textDocumentOwner.TryBuildDisplayList(out _, out var textDiagnostic) && textDiagnostic is { } unsupported && unsupported.Code.Value == "XAML_TEXT_FONT_NOT_FOUND", "unregistered text font returns a diagnostic");
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
        var text = new Library.UiTextBlock { Text = "A", Width = 240, Height = 40 };
        var root = new Library.UiPanel();
        root.Add(text);
        using var textService = new SixLaborsTextService();
        using var document = new Library.UiDocument(root, textService, fonts);
        document.Layout(new Delta.Maths.float2(240, 40), 1);
        var first = document.BuildDisplayList();
        Assert.Equal(1, first.Text.Length, "facade emits one canonical text draw");
        var firstShaped = first.Text[0].Text;
        Assert.True(firstShaped.Runs.Length > 0, "DeltaText returns positioned shaped runs");
        var second = document.BuildDisplayList();
        Assert.True(ReferenceEquals(firstShaped, second.Text[0].Text), "unchanged text reuses shaped cache");
        text.Text = "B";
        var third = document.BuildDisplayList();
        Assert.True(!ReferenceEquals(firstShaped, third.Text[0].Text), "text mutation reshapes only the changed text cache");
        Assert.Equal(first.Text[0].Clip, third.Text[0].Clip, "text clip identity remains canonical");
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
        if (loadedRoot.Children[0] is not Library.UiTextBlock boundText) { throw new InvalidOperationException("bound text missing"); }
        Assert.True(boundText.Text == "ADA", "binding context and converter update target");
        model.Name = "Grace";
        Assert.True(boundText.Text == "GRACE", "source notification updates target");
        var parsed = Library.UiBindingExpression.Parse("{Binding Path=Name, Mode=TwoWay}");
        Assert.Equal(Library.UiBindingMode.TwoWay, parsed.Mode, "binding parser preserves the declared mode");
        var compiledText = new Library.UiTextBox();
        using var compiled = new Library.UiCompiledBinding<BindingModel, string>(model, source => source.Name, (source, value) => source.Name = value, Library.UiBindingMode.TwoWay);
        compiledText.SetBinding("Text", compiled);
        Assert.Equal("Grace", compiledText.Text, "compiled binding initializes the target");
        model.Name = "Lin";
        Assert.Equal("Lin", compiledText.Text, "compiled binding observes source changes");
        compiledText.SetText("Mina");
        Assert.Equal("Mina", model.Name, "compiled two-way binding writes the source");

        var lateContextRoot = new Library.UiPanel { BindingContext = model };
        var lateContextText = new Library.UiTextBlock();
        using var lateContextBinding = new Library.UiCompiledBinding<BindingModel, string>(model, source => source.Name);
        lateContextText.SetBinding("Text", lateContextBinding);
        lateContextRoot.Add(lateContextText);
        Assert.Equal("Mina", lateContextText.Text, "children added after the parent inherit its binding context");

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
        if (twoWayRoot.Children[0] is not Library.UiTextBox editText) { throw new InvalidOperationException("edit text missing"); }
        Assert.True(editText.Text == "Bob" && editModel.Name == "Bob", "two-way text edit writes the source");
    }

    private static void EditorShellLibrarySlice()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EditorShell.xaml"));
        var loader = new Library.XamlLoader();
        var context = new Library.XamlLoadContext(new EditorShellTypeResolver(), new EmptyLibraryResourceResolver());
        var loaded = loader.Load(source, in context);
        Assert.True(loaded.Success && loaded.Root is Library.UiPanel, "EditorShell fixture loads through the concrete library API");
        if (loaded.Root is not { } root)
        {
            throw new InvalidOperationException("EditorShell fixture root missing");
        }

        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new Library.UiFontCatalog();
        fonts.Register("default", new TextContract.FontSourceId(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3")), File.ReadAllBytes(fontPath));
        using var textService = new SixLaborsTextService();
        using var document = new Library.UiDocument(root, textService, fonts);
        document.Layout(new Delta.Maths.float2(960, 540), 1);
        var first = document.BuildDisplayList();
        Assert.True(first.Visuals.Length >= 4, "EditorShell emits colored visual rectangles");
        Assert.True(first.Clips.Length >= first.Visuals.Length, "EditorShell emits clip hierarchy entries");
        Assert.True(first.Text.Length >= 1 && first.Text[0].Text.Runs.Length > 0, "EditorShell emits renderer-neutral text through the library path");

        var second = document.BuildDisplayList();
        Assert.Equal(first.Visuals.Length, second.Visuals.Length, "unchanged EditorShell visual count is deterministic");
        Assert.Equal(first.Clips.Length, second.Clips.Length, "unchanged EditorShell clip count is deterministic");
        Assert.Equal(first.Text.Length, second.Text.Length, "unchanged EditorShell text count is deterministic");
        for (var i = 0; i < first.Visuals.Length; i++)
        {
            Assert.Equal(first.Visuals[i], second.Visuals[i], "unchanged EditorShell visual is stable");
        }

        for (var i = 0; i < first.Clips.Length; i++)
        {
            Assert.Equal(first.Clips[i], second.Clips[i], "unchanged EditorShell clip is stable");
        }

        for (var i = 0; i < first.Text.Length; i++)
        {
            Assert.True(ReferenceEquals(first.Text[i].Text, second.Text[i].Text), "unchanged EditorShell text reuses shaped state");
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
        var theme = new Library.UiTheme(resources);
        theme.Add(style);

        var text = new Library.UiTextBlock { StyleKey = "Body" };
        var other = new Library.UiTextBlock();
        theme.Apply(text);
        Assert.Equal(firstColor, text.Foreground, "resource-backed style resolves through the user API");
        Assert.Equal(18f, text.FontSize, "style value uses the retained style source");
        resources.Set("TextColor", secondColor);
        Assert.Equal(secondColor, text.Foreground, "resource change updates the dependent style value");
        Assert.Equal(new Library.UiColor(255, 255, 255), other.Foreground, "unrelated element is not changed by a resource update");

        using var stateTextService = new EmptyTextService();
        using var stateDocument = new Library.UiDocument(text, stateTextService, null, theme);
        text.RetainedElement.SetHovered(true);
        stateDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal(hoverColor, text.Foreground, "visual state overrides the base style after input state changes");
        text.RetainedElement.SetHovered(false);
        stateDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal(secondColor, text.Foreground, "leaving a visual state restores the base resource style");

        var loader = new Library.XamlLoader();
        var dynamicLoaded = loader.Load(
            "<TextBlock Foreground=\"{DynamicResource TextColor}\" />",
            new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), resources));
        Assert.True(dynamicLoaded.Success && dynamicLoaded.Root is Library.UiTextBlock, "DynamicResource markup loads through the public facade");
        if (dynamicLoaded.Root is not Library.UiTextBlock dynamicText) { throw new InvalidOperationException("dynamic resource text missing"); }
        Assert.Equal(secondColor, dynamicText.Foreground, "DynamicResource uses the named resource catalog");
        resources.Set("TextColor", firstColor);
        Assert.Equal(firstColor, dynamicText.Foreground, "DynamicResource remains live after a catalog update");
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
        theme.RegisterTemplate("Label", new Library.UiTemplate(_ => new Library.UiTextBlock { Text = "templated" }));
        theme.Apply(host);
        Assert.True(host.Content is Library.UiTextBlock templated && templated.Text == "templated", "template creates one retained child");

        var items = new Library.UiItemsControl();
        var created = 0;
        var values = new object?[] { "one", "two" };
        items.SetItems(values, value =>
        {
            created++;
            return new Library.UiTextBlock { Text = value?.ToString() ?? string.Empty };
        });
        items.SetItems(values, value =>
        {
            created++;
            return new Library.UiTextBlock { Text = value?.ToString() ?? string.Empty };
        });
        Assert.Equal(2, created, "unchanged collection items reuse retained rows");
        items.SetItems(new object?[] { "one", "three" }, value =>
        {
            created++;
            return new Library.UiTextBlock { Text = value?.ToString() ?? string.Empty };
        });
        Assert.Equal(3, created, "only changed collection item creates a replacement row");

        var clipboard = new Library.UiClipboard();
        var editor = new Library.UiTextBox { Clipboard = clipboard };
        editor.SetText("hello");
        editor.SelectAll();
        editor.Copy();
        Assert.Equal("hello", clipboard.ReadText(), "public text editor uses the platform-neutral clipboard");
        editor.SetText("changed");
        Assert.True(editor.Undo() && editor.Text == "hello", "public text editor exposes retained undo");

        var hostWrite = new Library.UiTextBlock { Text = "before" };
        var textHandle = hostWrite.GetHandle("Text");
        Assert.True(textHandle.IsValid && hostWrite.TrySet(textHandle, "after", out var handleDiagnostic) && handleDiagnostic is null && hostWrite.Text == "after", "public handle performs a generation-safe host write");

        var composition = new Library.UiPanel();
        var compositionText = new Library.UiTextBlock { Text = "stable" };
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
        Assert.True(frame.TryGetNode(rootNodeId, out var rootNode) && rootNode.FirstLogicalChild == textNodeId, "node store records the first logical child");
        Assert.True(frame.TryGetNode(textNodeId, out var textNode) && textNode.LogicalParent == rootNodeId && textNode.NextLogicalSibling == otherNodeId, "node store records logical parent and sibling links");
        Assert.True(textNode.VisualParent == rootNodeId && rootNode.FirstVisualChild == textNodeId, "node store records the visual relation in the same node record");
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
        frame.Layout(new(100, 20), 1);
        Assert.Equal(new UiRect(0, 0, 100, 20), text.Bounds, "frame layout boundary");
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

    private static void ParticipationBoundary()
    {
        var root = new Library.UiPanel { Background = new Library.UiColor(10, 20, 30) };
        root.Add(new Library.UiTextBlock { Text = "hidden" });
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
}
