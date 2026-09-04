using DeltaXAML.Internal;
using Delta.XAML.Contract;
using Library = Delta.XAML;

internal static class TextBlockArchitectureTests
{
    public static void Run()
    {
        TextLayoutPropertiesUseTypedState();
        XamlLoaderReadsTextProperties();
        TextEditorConstraintsAreEnforced();

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

    private static void TextLayoutPropertiesUseTypedState()
    {
        var state = new TextBlockState
        {
            Text = new string('x', 40),
            Visual = new TextBlockVisualState
            {
                FontKey = "default",
                GlyphRunKey = "long-text",
                FontSize = 14,
                Foreground = new UiColor(255, 255, 255),
            },
        };

        Assert.True(TextBlockGenerated.TrySetHorizontalTextAlignment(ref state, Library.UiTextHorizontalAlignment.Center), "typed horizontal alignment setter");
        Assert.True(TextBlockGenerated.TrySetVerticalTextAlignment(ref state, Library.UiTextVerticalAlignment.Center), "typed vertical alignment setter");
        Assert.True(TextBlockGenerated.TrySetTextWrapping(ref state, Library.UiTextWrapping.Word), "typed wrapping setter");
        Assert.True(TextBlockGenerated.TrySetTextTrimming(ref state, Library.UiTextTrimming.CharacterEllipsis), "typed trimming setter");
        Assert.True(TextBlockGenerated.TrySetMaxLines(ref state, 2), "typed max-lines setter");
        Assert.True(TextBlockGenerated.TrySetLineHeight(ref state, 20), "typed line-height setter");
        Assert.True(TextBlockGenerated.TrySetFontWeight(ref state, Library.UiFontWeight.SemiBold), "typed weight setter");
        Assert.True(TextBlockGenerated.TrySetFontStyle(ref state, Library.UiFontStyle.Italic), "typed style setter");
        Assert.True(TextBlockGenerated.TrySetTextDecorations(ref state, Library.UiTextDecorations.Underline), "typed decorations setter");
        Assert.True(!TextBlockGenerated.TrySetMaxLines(ref state, -1), "negative max-lines is rejected");

        TextBlockGenerated.Measure(ref state, new(new(100, 80), 1));
        Assert.Equal(100f, state.Layout.DesiredSize.Width, "wrapped text is constrained by available width");
        Assert.Equal(40f, state.Layout.DesiredSize.Height, "max-lines and line-height determine desired height");
        TextBlockGenerated.Arrange(ref state, new(new(10, 20, 100, 100), new(10, 20, 100, 100)));
        Assert.Equal(new UiRect(10, 50, 100, 40), state.Layout.TextBounds, "aligned text bounds stay inside the arranged element");

        var run = TextBlockGenerated.EmitVisual(ref state, new(new(8), 4, 1, 12));
        Assert.Equal(state.Layout.TextBounds, run.TextBounds, "text run carries aligned text bounds");
        Assert.Equal(Library.UiTextHorizontalAlignment.Center, run.HorizontalAlignment, "text run carries horizontal alignment");
        Assert.Equal(Library.UiTextVerticalAlignment.Center, run.VerticalAlignment, "text run carries vertical alignment");
        Assert.Equal(Library.UiTextWrapping.Word, run.Wrapping, "text run carries wrapping mode");
        Assert.Equal(Library.UiTextTrimming.CharacterEllipsis, run.Trimming, "text run carries trimming mode");
        Assert.Equal(2, run.MaxLines, "text run carries max-lines");
        Assert.Equal(20f, run.LineHeight, "text run carries line-height");
        Assert.Equal(Library.UiFontWeight.SemiBold, run.Weight, "text run carries font weight");
        Assert.Equal(Library.UiFontStyle.Italic, run.Style, "text run carries font style");
        Assert.Equal(Library.UiTextDecorations.Underline, run.Decorations, "text run carries decorations");
    }

    private static void XamlLoaderReadsTextProperties()
    {
        var result = new Library.XamlLoader().Load(
            "<TextBlock Text=\"Hello\" HorizontalTextAlignment=\"Center\" VerticalTextAlignment=\"Bottom\" TextWrapping=\"Word\" TextTrimming=\"CharacterEllipsis\" MaxLines=\"2\" LineHeight=\"18\" FontWeight=\"Bold\" FontStyle=\"Italic\" TextDecorations=\"Underline, Strikethrough\" />",
            new Library.XamlLoadContext(new EmptyLibraryTypeResolver(), new Library.UiResourceCatalog()));

        Assert.True(result.Success, "XAML loader accepts the text layout dialect");
        if (result.Root is not Library.UiTextBlock text)
        {
            throw new InvalidOperationException("XAML loader did not create a TextBlock.");
        }

        Assert.Equal("Hello", text.Text, "XAML loader applies text");
        Assert.Equal(Library.UiTextHorizontalAlignment.Center, text.HorizontalTextAlignment, "XAML loader applies horizontal alignment");
        Assert.Equal(Library.UiTextVerticalAlignment.Bottom, text.VerticalTextAlignment, "XAML loader applies vertical alignment");
        Assert.Equal(Library.UiTextWrapping.Word, text.TextWrapping, "XAML loader applies wrapping");
        Assert.Equal(Library.UiTextTrimming.CharacterEllipsis, text.TextTrimming, "XAML loader applies trimming");
        Assert.Equal(2, text.MaxLines, "XAML loader applies max-lines");
        Assert.Equal(18f, text.LineHeight, "XAML loader applies line-height");
        Assert.Equal(Library.UiFontWeight.Bold, text.FontWeight, "XAML loader applies weight");
        Assert.Equal(Library.UiFontStyle.Italic, text.FontStyle, "XAML loader applies style");
        Assert.Equal(Library.UiTextDecorations.Underline | Library.UiTextDecorations.Strikethrough, text.TextDecorations, "XAML loader applies decorations");
    }

    private static void TextEditorConstraintsAreEnforced()
    {
        var editor = TextBoxGenerated.Create();
        editor.MaxLength = 3;
        editor.SetText("abcd", false);
        Assert.Equal("abc", editor.Text, "TextBox clamps programmatic text to MaxLength");
        editor.SetSelection(0, editor.Text.Length);
        var replacement = new UiTextInput("xy".AsMemory());
        Assert.True(editor.ApplyText(in replacement), "TextBox accepts an in-limit replacement");
        Assert.Equal("xy", editor.Text, "TextBox replaces selected text");
        editor.PlaceholderText = "Enter value";
        Assert.Equal("xy", editor.VisualText, "non-empty TextBox displays its value instead of placeholder");
        editor.SetText(string.Empty, false);
        Assert.Equal("Enter value", editor.VisualText, "empty TextBox exposes placeholder text to visual extraction");
        Assert.True(
            UiDescriptorCatalog.TryGetTextRun(
                TextBoxGenerated.Descriptor.Index,
                editor,
                new(editor.Id, editor.Generation, editor.LayoutScale, editor.TextRunVersion),
                out var placeholderRun),
            "empty TextBox emits a placeholder text run");
        Assert.Equal("Enter value", placeholderRun.Text, "placeholder reaches the renderer-neutral text run");
        editor.SetText("xy", false);

        editor.SetSelection(0, 0);
        var tooLong = new UiTextInput("1234".AsMemory());
        Assert.True(!editor.ApplyText(in tooLong), "TextBox reports a rejected MaxLength edit");
        Assert.Equal("xy", editor.Text, "rejected MaxLength edit preserves text");

        editor.IsReadOnly = true;
        var readOnlyInput = new UiTextInput("z".AsMemory());
        Assert.True(!editor.ApplyText(in readOnlyInput), "read-only TextBox rejects text input");
        Assert.Equal("xy", editor.Text, "read-only edit preserves text");

        editor.IsReadOnly = false;
        editor.MaxLength = 0;
        var lineBreak = new UiTextInput("a\nb".AsMemory());
        Assert.True(!editor.ApplyText(in lineBreak), "single-line TextBox rejects line breaks");
        editor.AcceptsReturn = true;
        Assert.True(editor.ApplyText(in lineBreak), "multiline TextBox accepts line breaks");
        Assert.Equal("a\nbxy", editor.Text, "multiline TextBox stores line breaks");
    }
}
