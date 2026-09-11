using Delta;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Generated;
using DeltaXaml.Tests;

internal static class InterpretedLoaderTests
{
    internal static void Run()
    {
        LoadsCanonicalBuiltIns();
        LoadsContentChildren();
        LoadsCompositeChildren();
        LoadsAttachedGridPropertiesAndBrushLiterals();
        LoadsPerSideBorderThickness();
        LoadsRichTextSpans();
        RejectsMalformedSpanContent();
        DiagnosesCompilerOnlyDeclarations();
        DiagnosesGeneratedOnlyBindings();
        DiagnosesGeneratedOnlyCollectionProperties();
        GeneratedAndInterpretedToggleButtonHaveMatchingOutput();
        GeneratedAndInterpretedRichTextHaveMatchingOutput();
        GeneratedAndInterpretedDynamicResourceHaveMatchingOutput();
        GeneratedAndInterpretedBindingHaveMatchingOutput();
    }

    private static void LoadsContentChildren()
    {
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var result = loader.Load("<Button><TextBlock Text=\"Save\" /></Button>", in context);

        Assert.True(result.Success && result.Root is UiButton { Content: UiTextBlock { Text: "Save" } }, "interpreted loader attaches button content through the retained content slot");
    }

    private static void LoadsCompositeChildren()
    {
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var result = loader.Load(
            "<Panel><CollectionView><TextBlock Text=\"Row\" /></CollectionView><Menu><Button /></Menu></Panel>",
            in context);

        Assert.True(result.Success && result.Root is UiPanel { Children.Count: 2 }, "interpreted loader keeps composite children in the canonical tree");
        if (result.Root is not UiPanel { Children.Count: 2 } root ||
            root.Children[0] is not UiCollectionView collection ||
            root.Children[1] is not UiMenu menu)
        {
            throw new InvalidOperationException("Composite loader roots are missing.");
        }

        Assert.True(collection.ItemsHost.Children.Count == 1 && collection.ItemsHost.Children[0] is UiTextBlock { Text: "Row" }, "collection view routes child content to its generated items host");
        Assert.True(menu.ItemsHost.Children.Count == 1 && menu.ItemsHost.Children[0] is UiButton, "menu routes child content to its generated items host");
    }

    private static void LoadsCanonicalBuiltIns()
    {
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        const string source = "<Panel><Slider Minimum=\"0\" Maximum=\"10\" Value=\"4\" Step=\"0.5\" Orientation=\"Vertical\" /><Image Source=\"285a7033-e9eb-438e-81d8-7906cf978301\" Tint=\"#80112233\" Stretch=\"UniformToFill\" /><Overlay IsOpen=\"false\" /><CollectionView SelectedIndex=\"2\" /><Picker SelectedIndex=\"1\" IsOpen=\"true\" /><TabView SelectedIndex=\"1\" /><Menu /></Panel>";

        var result = loader.Load(source, in context);
        Assert.True(result.Success && result.Root is { Children.Count: 7 }, "interpreted loader creates every canonical built-in control");
        if (result.Root is not { } root)
        {
            throw new InvalidOperationException("interpreted built-in root missing");
        }

        Assert.True(root.Children[0] is UiSlider { Value: 4, Orientation: UiOrientation.Vertical }, "slider values use the typed retained state");
        Assert.True(root.Children[1] is UiImage { Stretch: UiImageStretch.UniformToFill, Tint.A: 128 }, "image values use neutral resource and paint data");
        Assert.True(root.Children[2] is UiOverlay { IsOpen: false }, "overlay state is loaded without a host object");
        Assert.True(root.Children[3] is UiCollectionView { SelectedIndex: 2 }, "collection selection is loaded into the canonical composite");
        Assert.True(root.Children[4] is UiPicker { SelectedIndex: 1, IsOpen: true }, "picker selection and overlay state are loaded");
        Assert.True(root.Children[5] is UiTabView { SelectedIndex: 1 }, "tab selection is loaded into the canonical composite");
        Assert.True(root.Children[6] is UiMenu, "menu is loaded through its generated composite shape");
    }

    private static void LoadsAttachedGridPropertiesAndBrushLiterals()
    {
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var result = loader.Load(
            "<Grid Columns=\"*,*\" Rows=\"Auto,*\" BackgroundBrush=\"#80112233\"><TextBlock Grid.Row=\"1\" Grid.Column=\"1\" Grid.RowSpan=\"1\" Grid.ColumnSpan=\"1\" Text=\"placed\" /></Grid>",
            in context);

        Assert.True(result.Success && result.Root is UiGrid { Children.Count: 1 }, "interpreted grid accepts literal brushes and attached placement");
        if (result.Root is not UiGrid grid || grid.Children[0] is not UiTextBlock child)
        {
            throw new InvalidOperationException("interpreted grid child missing");
        }

        Assert.Equal(new UiColor(17, 34, 51, 128), grid.BackgroundBrush.Color, "#AARRGGBB is parsed in public color order");
        Assert.Equal(1, child.GetAttachedValue(UiGridAttachedProperties.Row), "Grid.Row is preserved from the qualified XAML attribute");
        Assert.Equal(1, child.GetAttachedValue(UiGridAttachedProperties.Column), "Grid.Column is preserved from the qualified XAML attribute");
    }

    private static void LoadsPerSideBorderThickness()
    {
        var loader = new XamlLoader();
        var resources = new UiResourceCatalog();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), resources);
        var result = loader.Load(
            "<Border BorderColor=\"#102030\" BorderThickness=\"1,2,3,4\" BorderWidthUnits=\"Device\" />",
            in context);

        Assert.True(result.Success && result.Root is UiBorder, "interpreted loader accepts per-side border thickness");
        if (result.Root is not UiBorder border)
        {
            throw new InvalidOperationException("per-side border root missing");
        }

        Assert.Equal(new UiThickness(1, 2, 3, 4), border.BorderThickness, "per-side border values preserve left/top/right/bottom order");
        Assert.True(resources.TryResolveEffectResource(border.EffectSet.Resource, out var effect), "per-side border lowers to a canonical effect resource");
        Assert.Equal(new float4(1, 2, 3, 4), effect.Parameters.Stroke.SideWidths, "effect resource carries per-side widths");
        Assert.Equal(PaintUnits.Device, effect.Parameters.Units, "per-side border preserves the selected paint units");
    }

    private static void LoadsRichTextSpans()
    {
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var result = loader.Load(
            "<RichTextBlock><Span Text=\"Rewards\" Foreground=\"#FF8040\" FontSize=\"18\" TextDecorations=\"Underline\" Command=\"b3288fca-9dc6-4b90-ac40-c1c23366c301\" Argument=\"docs\" /><Span Text=\"!\" /></RichTextBlock>",
            in context);

        Assert.True(result.Success && result.Root is UiRichTextBlock, "interpreted loader creates a canonical rich text root");
        if (result.Root is not UiRichTextBlock rich)
        {
            throw new InvalidOperationException("rich text root missing");
        }

        Assert.Equal(2, rich.Spans.Length, "interpreted rich text retains every span");
        Assert.Equal("Rewards", rich.Spans.Span[0].Text, "span text is preserved");
        Assert.Equal(new UiColor(255, 128, 64), rich.Spans.Span[0].Color, "span color uses the canonical color value");
        Assert.Equal(UiTextDecorations.Underline, rich.Spans.Span[0].Decorations, "span decorations are parsed as flags");
        Assert.True(rich.Spans.Span[0].Link.IsValid && rich.Spans.Span[0].LinkArgument == "docs", "span links remain neutral command identities");
    }

    private static void RejectsMalformedSpanContent()
    {
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var result = loader.Load("<RichTextBlock><Span><TextBlock /></Span></RichTextBlock>", in context);

        Assert.True(!result.Success && result.Diagnostics.Length != 0, "unsupported rich text nesting returns a diagnostic instead of a fallback tree");
        Assert.True(ContainsDiagnostic(result, "self-closing") && ContainsDiagnostic(result, "Text property"),
            "malformed span diagnostics explain the unsupported shape and missing text value");
    }

    private static void DiagnosesCompilerOnlyDeclarations()
    {
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var result = loader.Load("<ResourceDictionary />", in context);

        Assert.True(!result.Success && result.Diagnostics.Length == 1, "interpreted loader rejects compiler-only declaration roots");
        Assert.Equal("XAML020", result.Diagnostics.Span[0].Code.Value, "compiler-only declarations have a stable diagnostic code");

        var declarations = new[] { "Resource", "Style", "Setter", "Template", "Trigger", "Behavior", "VisualState" };
        for (var i = 0; i < declarations.Length; i++)
        {
            var declaration = loader.Load($"<{declarations[i]} />", in context);
            Assert.True(!declaration.Success && declaration.Diagnostics.Length == 1, $"interpreted loader rejects {declarations[i]} without a partial tree");
            Assert.Equal("XAML020", declaration.Diagnostics.Span[0].Code.Value, $"{declarations[i]} has the stable generated-path diagnostic");
        }
    }

    private static void DiagnosesGeneratedOnlyBindings()
    {
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var templateBinding = loader.Load("<TextBlock Text=\"{TemplateBinding Foreground}\" />", in context);
        var multiBinding = loader.Load("<TextBlock Text=\"{MultiBinding Sources=Self.Text|Self.FontKey, StringFormat='{0}', Culture=en-US}\" />", in context);
        var relativeBinding = loader.Load("<TextBlock Text=\"{Binding Path=Text, RelativeSource=Self}\" />", in context);

        Assert.True(!templateBinding.Success && templateBinding.Diagnostics.Length == 1, "interpreted loader rejects TemplateBinding without a partial fallback tree");
        Assert.Equal("XAML020", templateBinding.Diagnostics.Span[0].Code.Value, "TemplateBinding has a stable generated-path diagnostic");
        Assert.True(!multiBinding.Success && multiBinding.Diagnostics.Length == 1, "interpreted loader rejects MultiBinding without a partial fallback tree");
        Assert.Equal("XAML020", multiBinding.Diagnostics.Span[0].Code.Value, "MultiBinding has a stable generated-path diagnostic");
        Assert.True(!relativeBinding.Success && relativeBinding.Diagnostics.Length == 1, "interpreted loader diagnoses relation-source bindings instead of silently treating them as context bindings");
        Assert.Equal("XAML008", relativeBinding.Diagnostics.Span[0].Code.Value, "cold relation-source bindings have a stable diagnostic");
    }

    private static void DiagnosesGeneratedOnlyCollectionProperties()
    {
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var source = loader.Load(
            "<CollectionView ItemsSource=\"{Binding Items}\" ItemTemplate=\"Row\" ItemTemplateSelector=\"Kind\" VirtualizationStart=\"2\" VirtualizationCount=\"8\" ItemExtent=\"24\" />",
            in context);

        Assert.True(!source.Success && source.Diagnostics.Length == 6, "cold collection markup reports every generated-only property");
        for (var i = 0; i < source.Diagnostics.Length; i++)
        {
            Assert.Equal("XAML020", source.Diagnostics.Span[i].Code.Value, "collection properties use the generated-path diagnostic");
        }
    }

    private static void GeneratedAndInterpretedToggleButtonHaveMatchingOutput()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ToggleButton.dxaml");
        var source = File.ReadAllText(fixturePath);
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var interpreted = loader.Load(source, in context);
        Assert.True(interpreted.Success && interpreted.Root is UiToggleButton { Content: UiTextBlock }, $"the parity fixture loads through the cold path ({DescribeDiagnostics(interpreted)})");
        if (interpreted.Root is not { } interpretedRoot)
        {
            throw new InvalidOperationException("Parity fixture root is missing.");
        }

        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new UiFontCatalog();
        fonts.Register("default", new Delta.Text.Contract.FontSourceId(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3")), File.ReadAllBytes(fontPath));
        using var generatedText = new CountingTextService();
        using var generated = new ToggleButtonArtifact(generatedText, fonts);
        using var interpretedText = new CountingTextService();
        using var interpretedDocument = new UiDocument(interpretedRoot, interpretedText, fonts);
        var viewport = new float2(120, 32);
        generated.Document.Layout(viewport, 1);
        interpretedDocument.Layout(viewport, 1);
        var generatedDisplay = generated.Document.BuildDisplayList();
        var interpretedDisplay = interpretedDocument.BuildDisplayList();

        Assert.Equal(generated.Document.Root.GetType(), interpretedDocument.Root.GetType(), "generated and interpreted paths preserve the same root control type");
        Assert.Equal(generated.Document.Root.Children.Count, interpretedDocument.Root.Children.Count, "generated and interpreted paths preserve the same child count");
        Assert.Equal(generated.Document.Root.Bounds, interpretedDocument.Root.Bounds, "generated and interpreted paths compute the same root bounds");
        Assert.Equal(generated.Document.Root.Background, interpretedDocument.Root.Background, "generated and interpreted paths preserve the same root background");
        if (generated.Document.Root.Children[0] is not UiTextBlock generatedTextBlock ||
            interpretedDocument.Root.Children[0] is not UiTextBlock interpretedTextBlock)
        {
            throw new InvalidOperationException("Parity fixture text child is missing.");
        }

        Assert.Equal(generatedTextBlock.Text, interpretedTextBlock.Text, "generated and interpreted paths preserve the same text value");
        Assert.Equal(generatedTextBlock.FontSize, interpretedTextBlock.FontSize, "generated and interpreted paths preserve the same font size");
        Assert.Equal(generatedTextBlock.Foreground, interpretedTextBlock.Foreground, "generated and interpreted paths preserve the same text color");
        Assert.Equal(generatedDisplay.Visuals.Length, interpretedDisplay.Visuals.Length, "generated and interpreted paths emit the same visual count");
        Assert.Equal(generatedDisplay.Clips.Length, interpretedDisplay.Clips.Length, "generated and interpreted paths emit the same clip count");
        Assert.Equal(generatedDisplay.Text.Length, interpretedDisplay.Text.Length, "generated and interpreted paths emit the same text count");
        Assert.Equal(generatedDisplay.Order.Length, interpretedDisplay.Order.Length, "generated and interpreted paths emit the same order count");
        for (var i = 0; i < generatedDisplay.Visuals.Length; i++)
        {
            Assert.Equal(generatedDisplay.Visuals[i], interpretedDisplay.Visuals[i], "generated and interpreted visual payloads match");
        }

        for (var i = 0; i < generatedDisplay.Clips.Length; i++)
        {
            Assert.Equal(generatedDisplay.Clips[i], interpretedDisplay.Clips[i], "generated and interpreted clip payloads match");
        }

        for (var i = 0; i < generatedDisplay.Order.Length; i++)
        {
            Assert.Equal(generatedDisplay.Order[i], interpretedDisplay.Order[i], "generated and interpreted order entries match");
            Assert.True(generatedDisplay.Identities[i].Version > 0 && interpretedDisplay.Identities[i].Version > 0, "both paths publish an identity version for every ordered entry");
        }

        for (var i = 0; i < generatedDisplay.Text.Length; i++)
        {
            Assert.Equal(generatedDisplay.Text[i].BaselineOrigin, interpretedDisplay.Text[i].BaselineOrigin, "generated and interpreted text baselines match");
            Assert.Equal(generatedDisplay.Text[i].Paint, interpretedDisplay.Text[i].Paint, "generated and interpreted text paint matches");
            Assert.Equal(generatedDisplay.Text[i].Clip, interpretedDisplay.Text[i].Clip, "generated and interpreted text clips match");
            Assert.Equal(generatedDisplay.Text[i].Text.TextLengthUtf16, interpretedDisplay.Text[i].Text.TextLengthUtf16, "generated and interpreted shaped text length matches");
        }
    }

    private static void GeneratedAndInterpretedRichTextHaveMatchingOutput()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RichTextParity.dxaml");
        var source = File.ReadAllText(fixturePath);
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var interpreted = loader.Load(source, in context);
        Assert.True(interpreted.Success && interpreted.Root is UiRichTextBlock, $"the rich-text parity fixture loads through the cold path ({DescribeDiagnostics(interpreted)})");
        if (interpreted.Root is not UiRichTextBlock interpretedRoot)
        {
            throw new InvalidOperationException("Rich-text parity root is missing.");
        }

        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new UiFontCatalog();
        fonts.Register("default", new Delta.Text.Contract.FontSourceId(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3")), File.ReadAllBytes(fontPath));
        using var generatedText = new CountingTextService();
        using var generated = new RichTextParityArtifact(generatedText, fonts);
        using var interpretedText = new CountingTextService();
        using var interpretedDocument = new UiDocument(interpretedRoot, interpretedText, fonts);
        var viewport = new float2(240, 40);
        generated.Document.Layout(viewport, 1);
        interpretedDocument.Layout(viewport, 1);
        var generatedDisplay = generated.Document.BuildDisplayList();
        var interpretedDisplay = interpretedDocument.BuildDisplayList();
        if (generated.Document.Root is not UiRichTextBlock generatedRoot)
        {
            throw new InvalidOperationException("Generated rich-text parity root is missing.");
        }

        Assert.Equal(generated.Document.Root.GetType(), interpretedDocument.Root.GetType(), "generated and interpreted rich-text paths preserve the same root type");
        Assert.Equal(generated.Document.Root.Bounds, interpretedDocument.Root.Bounds, "generated and interpreted rich-text paths compute the same bounds");
        Assert.Equal(generatedRoot.Spans.Length, interpretedRoot.Spans.Length, "generated and interpreted rich-text paths preserve the span count");
        for (var i = 0; i < interpretedRoot.Spans.Length; i++)
        {
            Assert.Equal(generatedRoot.Spans.Span[i], interpretedRoot.Spans.Span[i], "generated and interpreted rich-text spans match");
        }

        Assert.Equal(generatedDisplay.Visuals.Length, interpretedDisplay.Visuals.Length, "generated and interpreted rich-text paths emit the same visual count");
        Assert.Equal(generatedDisplay.Clips.Length, interpretedDisplay.Clips.Length, "generated and interpreted rich-text paths emit the same clip count");
        Assert.Equal(generatedDisplay.Text.Length, interpretedDisplay.Text.Length, "generated and interpreted rich-text paths emit the same text count");
        Assert.Equal(generatedDisplay.Order.Length, interpretedDisplay.Order.Length, "generated and interpreted rich-text paths emit the same order count");
        for (var i = 0; i < generatedDisplay.Text.Length; i++)
        {
            Assert.Equal(generatedDisplay.Text[i].BaselineOrigin, interpretedDisplay.Text[i].BaselineOrigin, "generated and interpreted rich-text baselines match");
            Assert.Equal(generatedDisplay.Text[i].Paint, interpretedDisplay.Text[i].Paint, "generated and interpreted rich-text paint matches");
            Assert.Equal(generatedDisplay.Text[i].Clip, interpretedDisplay.Text[i].Clip, "generated and interpreted rich-text clips match");
            Assert.Equal(generatedDisplay.Text[i].Text.TextLengthUtf16, interpretedDisplay.Text[i].Text.TextLengthUtf16, "generated and interpreted rich-text shaped lengths match");
        }

        for (var i = 0; i < generatedDisplay.Order.Length; i++)
        {
            Assert.Equal(generatedDisplay.Order[i], interpretedDisplay.Order[i], "generated and interpreted rich-text order entries match");
        }
    }

    private static void GeneratedAndInterpretedDynamicResourceHaveMatchingOutput()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ResourceParity.dxaml");
        var source = File.ReadAllText(fixturePath);
        var interpretedSource = source.Replace("  <Resource x:Key=\"Accent\" Type=\"Color\" Value=\"#FF8040\" />\n", string.Empty, StringComparison.Ordinal);
        var loader = new XamlLoader();
        var resources = new UiResourceCatalog();
        var firstColor = new UiColor(255, 128, 64);
        resources.Set("Accent", firstColor);
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), resources);
        var interpreted = loader.Load(interpretedSource, in context);
        Assert.True(interpreted.Success && interpreted.Root is UiPanel { Children.Count: 1 }, $"the resource parity fixture loads through the cold path ({DescribeDiagnostics(interpreted)})");
        if (interpreted.Root is not { } interpretedRoot)
        {
            throw new InvalidOperationException("Resource parity root is missing.");
        }

        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new UiFontCatalog();
        fonts.Register("default", new Delta.Text.Contract.FontSourceId(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3")), File.ReadAllBytes(fontPath));
        using var generatedText = new CountingTextService();
        using var generated = new ResourceParityArtifact(generatedText, fonts);
        using var interpretedText = new CountingTextService();
        using var interpretedDocument = new UiDocument(interpretedRoot, interpretedText, fonts);
        var viewport = new float2(240, 40);
        generated.Document.Layout(viewport, 1);
        interpretedDocument.Layout(viewport, 1);
        var generatedDisplay = generated.Document.BuildDisplayList();
        var interpretedDisplay = interpretedDocument.BuildDisplayList();
        Assert.True(generated.TryGetResourceId("Accent", out var resource), "generated resource identity is available for the parity update");
        Assert.Equal(generatedDisplay.Visuals.Length, interpretedDisplay.Visuals.Length, "generated and interpreted resource paths emit the same visual count");
        Assert.Equal(generatedDisplay.Text.Length, interpretedDisplay.Text.Length, "generated and interpreted resource paths emit the same text count");
        Assert.Equal(generatedDisplay.Order.Length, interpretedDisplay.Order.Length, "generated and interpreted resource paths emit the same order count");
        Assert.Equal(generatedDisplay.Text[0].Paint, interpretedDisplay.Text[0].Paint, "generated and interpreted resource paths resolve the same initial text paint");
        var initialGeneratedIdentity = generatedDisplay.Identities[0];
        var initialInterpretedIdentity = interpretedDisplay.Identities[0];

        var secondColor = new UiColor(20, 220, 180);
        generated.Resources.Set(resource, secondColor);
        resources.Set("Accent", secondColor);
        generated.Document.Layout(viewport, 1);
        interpretedDocument.Layout(viewport, 1);
        var updatedGenerated = generated.Document.BuildDisplayList();
        var updatedInterpreted = interpretedDocument.BuildDisplayList();
        Assert.Equal(updatedGenerated.Text[0].Paint, updatedInterpreted.Text[0].Paint, "generated and interpreted resource paths apply the same dynamic update");
        Assert.Equal(initialGeneratedIdentity.Value, updatedGenerated.Identities[0].Value, "resource update preserves generated owner identity");
        Assert.Equal(initialGeneratedIdentity.Generation, updatedGenerated.Identities[0].Generation, "resource update preserves generated owner generation");
        Assert.True(updatedGenerated.Identities[0].Version > initialGeneratedIdentity.Version, "resource update advances only the generated payload version");
        Assert.Equal(initialInterpretedIdentity.Value, updatedInterpreted.Identities[0].Value, "resource update preserves interpreted owner identity");
        Assert.Equal(initialInterpretedIdentity.Generation, updatedInterpreted.Identities[0].Generation, "resource update preserves interpreted owner generation");
        Assert.True(updatedInterpreted.Identities[0].Version > initialInterpretedIdentity.Version, "resource update advances only the interpreted payload version");
        var expectedFill = new float4(secondColor.R / 255f, secondColor.G / 255f, secondColor.B / 255f, secondColor.A / 255f);
        Assert.Equal(expectedFill, updatedGenerated.Text[0].Paint.FillColor, "dynamic resource update reaches generated text paint");
        Assert.Equal(expectedFill, updatedInterpreted.Text[0].Paint.FillColor, "dynamic resource update reaches interpreted text paint");
    }

    private static void GeneratedAndInterpretedBindingHaveMatchingOutput()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BoundText.dxaml");
        var source = File.ReadAllText(fixturePath);
        var model = new BindingModel { Name = "Ada" };
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver(), new BindingResolver());
        var interpreted = loader.Load(source, in context);
        Assert.True(interpreted.Success && interpreted.Root is UiTextBox, $"the binding parity fixture loads through the cold path ({DescribeDiagnostics(interpreted)})");
        if (interpreted.Root is not UiTextBox interpretedRoot)
        {
            throw new InvalidOperationException("Binding parity root is missing.");
        }

        interpretedRoot.BindingContext = model;
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new UiFontCatalog();
        fonts.Register("default", new Delta.Text.Contract.FontSourceId(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3")), File.ReadAllBytes(fontPath));
        using var generatedText = new CountingTextService();
        using var generated = new BoundTextArtifact(model, generatedText, fonts);
        using var interpretedText = new CountingTextService();
        using var interpretedDocument = new UiDocument(interpretedRoot, interpretedText, fonts);
        var viewport = new float2(240, 40);
        generated.Document.Layout(viewport, 1);
        interpretedDocument.Layout(viewport, 1);
        var generatedDisplay = generated.Document.BuildDisplayList();
        var interpretedDisplay = interpretedDocument.BuildDisplayList();
        var initialGeneratedIdentity = generatedDisplay.Identities[0];
        var initialInterpretedIdentity = interpretedDisplay.Identities[0];
        Assert.Equal(generated.Document.Root.GetType(), interpretedDocument.Root.GetType(), "generated and interpreted binding paths preserve the same root type");
        Assert.Equal(GetRootText(generated.Document.Root), interpretedRoot.Text, "generated and interpreted binding paths preserve the same effective text");
        Assert.Equal(generatedDisplay.Text.Length, interpretedDisplay.Text.Length, "generated and interpreted binding paths emit the same text count");
        Assert.Equal(generatedDisplay.Order.Length, interpretedDisplay.Order.Length, "generated and interpreted binding paths emit the same order count");

        model.Name = "Grace";
        generated.Document.Layout(viewport, 1);
        interpretedDocument.Layout(viewport, 1);
        var updatedGeneratedDisplay = generated.Document.BuildDisplayList();
        var updatedInterpretedDisplay = interpretedDocument.BuildDisplayList();
        Assert.Equal(GetRootText(generated.Document.Root), interpretedRoot.Text, "generated and interpreted binding paths apply the same source update");
        Assert.Equal(initialGeneratedIdentity.Value, updatedGeneratedDisplay.Identities[0].Value, "binding update preserves generated owner identity");
        Assert.Equal(initialGeneratedIdentity.Generation, updatedGeneratedDisplay.Identities[0].Generation, "binding update preserves generated owner generation");
        Assert.True(updatedGeneratedDisplay.Identities[0].Version > initialGeneratedIdentity.Version, "binding update advances generated text version");
        Assert.Equal(initialInterpretedIdentity.Value, updatedInterpretedDisplay.Identities[0].Value, "binding update preserves interpreted owner identity");
        Assert.Equal(initialInterpretedIdentity.Generation, updatedInterpretedDisplay.Identities[0].Generation, "binding update preserves interpreted owner generation");
        Assert.True(updatedInterpretedDisplay.Identities[0].Version > initialInterpretedIdentity.Version, "binding update advances interpreted text version");
    }

    private static string GetRootText(UiElement root) => root is UiTextBox textBox ? textBox.Text : string.Empty;

    private static bool ContainsDiagnostic(XamlLoadResult result, string text)
    {
        for (var i = 0; i < result.Diagnostics.Length; i++)
        {
            if (result.Diagnostics.Span[i].Message.Contains(text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeDiagnostics(XamlLoadResult result)
    {
        if (result.Diagnostics.Length == 0)
        {
            return "no diagnostics";
        }

        return result.Diagnostics.Span[0].Code.Value + ": " + result.Diagnostics.Span[0].Message;
    }
}
