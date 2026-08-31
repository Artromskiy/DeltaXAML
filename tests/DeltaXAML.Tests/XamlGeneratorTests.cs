using Delta.Diagnostics;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXAML.Compiler;
using DeltaXAML.Generator;
using DeltaXaml.Tests;
using Library = Delta.XAML;

internal static partial class Program
{
    private static void XamlGeneratorTests()
    {
        var registry = XamlSemanticRegistry.CreateBuiltIns();
        const string source = "<Panel x:Name=\"Root\" Width=\"120\"><TextBlock x:Name=\"Title\" Text=\"Hello\" FontSize=\"16\" /><Button x:Name=\"Action\"><TextBlock Text=\"Run\" /></Button></Panel>";
        var sourceId = new SourceId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D401"));
        var plan = XamlCompiler.Compile(sourceId, source, registry);
        Assert.True(plan.Success, "literal XAML is accepted by the companion pipeline");

        Assert.True(CSharpArtifactEmitter.TryEmit(plan, registry, "Generated", "SampleArtifact", out var first, out var firstDiagnostic), "direct artifact emission succeeds");
        Assert.True(firstDiagnostic is null, "successful artifact emission has no diagnostic");
        Assert.True(CSharpArtifactEmitter.TryEmit(plan, registry, "Generated", "SampleArtifact", out var second, out _), "the same plan can be emitted twice");
        Assert.Equal(first, second, "artifact emission is deterministic");
        Assert.True(first.Contains("new global::Delta.XAML.UiPanel()", StringComparison.Ordinal), "factory construction is direct");
        Assert.True(first.Contains("node0.Width = 120f;", StringComparison.Ordinal), "numeric property uses a typed assignment");
        Assert.True(first.Contains("node1.Text = \"Hello\";", StringComparison.Ordinal), "text property uses a typed assignment");
        Assert.True(first.Contains("node0.Add(node1);", StringComparison.Ordinal), "children use the panel attachment API");
        Assert.True(first.Contains("node2.SetContent(node3);", StringComparison.Ordinal), "content uses the content attachment API");
        Assert.True(first.Contains("TryFindName", StringComparison.Ordinal), "the companion contains a generated namescope lookup");
        Assert.True(!first.Contains("Activator", StringComparison.Ordinal) && !first.Contains("Type.GetType", StringComparison.Ordinal), "generated artifact has no reflection fallback");

        const string capabilitySource = "<Panel><Slider Minimum=\"0\" Maximum=\"10\" Value=\"4\" Step=\"0.5\" /><Image Source=\"285a7033-e9eb-438e-81d8-7906cf978301\" Tint=\"#112233\" /><Overlay IsOpen=\"true\"><TextBlock Text=\"popup\" /></Overlay><CollectionView SelectedIndex=\"2\" /></Panel>";
        var capabilityPlan = XamlCompiler.Compile(sourceId, capabilitySource, XamlSemanticRegistry.CreateBuiltIns());
        Assert.True(capabilityPlan.Success, "full-capability controls are accepted by the typed semantic model");
        Assert.True(CSharpArtifactEmitter.TryEmit(capabilityPlan, XamlSemanticRegistry.CreateBuiltIns(), "Generated", "CapabilityArtifact", out var capabilityArtifact, out _), "full-capability controls emit through direct factories");
        Assert.True(capabilityArtifact.Contains("new global::Delta.XAML.UiSlider()", StringComparison.Ordinal) && capabilityArtifact.Contains("node1.Value = 4d;", StringComparison.Ordinal), "slider construction and value assignment are direct");
        Assert.True(capabilityArtifact.Contains("new global::Delta.XAML.UiImage()", StringComparison.Ordinal) && capabilityArtifact.Contains("new global::Delta.XAML.Contract.UiResourceId", StringComparison.Ordinal), "image keeps a stable neutral resource identity");
        Assert.True(capabilityArtifact.Contains("node3.Add(node4);", StringComparison.Ordinal), "overlay content remains in the canonical generated tree");
        var attachedPlan = XamlCompiler.Compile(sourceId, "<Grid Columns=\"*\" Rows=\"Auto,*\"><TextBlock Grid.Row=\"1\" Grid.Column=\"0\" Grid.ColumnSpan=\"1\" Text=\"placed\" /></Grid>", XamlSemanticRegistry.CreateBuiltIns());
        Assert.True(attachedPlan.Success, "built-in attached layout slots compile without object-keyed storage");
        Assert.True(CSharpArtifactEmitter.TryEmit(attachedPlan, XamlSemanticRegistry.CreateBuiltIns(), "Generated", "AttachedArtifact", out var attachedArtifact, out _), "attached slots emit through generated typed setters");
        Assert.True(attachedArtifact.Contains("node1.SetAttachedValue(global::Delta.XAML.UiGridAttachedProperties.Row, 1);", StringComparison.Ordinal), "grid row uses its stable generated attached slot");
        var richPlan = XamlCompiler.Compile(sourceId, "<RichTextBlock><Span Text=\"Delta\" Foreground=\"#FF0000\" Command=\"b3288fca-9dc6-4b90-ac40-c1c23366c301\" Argument=\"docs\" /><Span Text=\"XAML\" FontSize=\"16\" /></RichTextBlock>", XamlSemanticRegistry.CreateBuiltIns());
        Assert.True(richPlan.Success, "formatted Span content lowers into one typed paragraph plan");
        Assert.True(CSharpArtifactEmitter.TryEmit(richPlan, XamlSemanticRegistry.CreateBuiltIns(), "Generated", "RichArtifact", out var richArtifact, out _), "rich paragraph emits through its generated factory");
        Assert.True(richArtifact.Contains("node0.Spans = new global::Delta.XAML.UiTextSpan[]", StringComparison.Ordinal) && richArtifact.Contains("new global::Delta.XAML.UiCommandId", StringComparison.Ordinal), "generated Span values preserve style and hyperlink command identity");

        var collectionRegistry = XamlSemanticRegistry.CreateBuiltIns();
        collectionRegistry.RegisterBinding(new(
            "Rows",
            "global::Sample.CollectionModel",
            "global::Sample.RowSource",
            "source.Rows",
            null,
            CollectionItemTypeName: "global::Sample.Row"));
        collectionRegistry.RegisterBinding(new(
            "Label",
            "global::Sample.Row",
            "string",
            "source.Label",
            null));
        var collectionPlan = XamlCompiler.Compile(
            sourceId,
            "<Panel x:DataType=\"global::Sample.CollectionModel\"><CollectionView ItemsSource=\"{Binding Rows}\" ItemTemplate=\"RowTemplate\" VirtualizationCount=\"8\" /><Template x:Key=\"RowTemplate\" x:DataType=\"global::Sample.Row\"><TextBlock Text=\"{Binding Label}\" /></Template></Panel>",
            collectionRegistry);
        Assert.True(collectionPlan.Success, "typed ItemsSource and data template share one semantic artifact");
        Assert.True(CSharpArtifactEmitter.TryEmit(collectionPlan, collectionRegistry, "Generated", "CollectionArtifact", out var collectionArtifact, out var collectionDiagnostic), "typed collection artifact emits");
        Assert.True(collectionDiagnostic is null && collectionArtifact.Contains("IUiItemTemplatePlan<ItemPlan0, global::Sample.Row>", StringComparison.Ordinal), "data template lowers to a static typed item plan");
        Assert.True(collectionArtifact.Contains("UiVirtualizingPresenter<global::Sample.Row, global::Sample.RowSource, ItemPlan0>", StringComparison.Ordinal), "ItemsSource lowers to the shared typed virtualizer");
        Assert.True(collectionArtifact.Contains("itemNode0.Text = item.Label;", StringComparison.Ordinal), "item binding remains a direct typed read");
        Assert.True(!collectionArtifact.Contains("Activator", StringComparison.Ordinal) && !collectionArtifact.Contains("GetProperty", StringComparison.Ordinal), "collection artifact has no reflection fallback");

        var customRegistry = XamlSemanticRegistry.CreateBuiltIns();
        customRegistry.RegisterType(new(
            new UiTypeId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D402")),
            new XamlQualifiedName("urn:custom", "Badge"),
            XamlContentKind.None,
            System.Collections.Immutable.ImmutableArray<XamlPropertyDefinition>.Empty));
        var customPlan = XamlCompiler.Compile(sourceId, "<Badge xmlns=\"urn:custom\" />", customRegistry);
        Assert.True(customPlan.Success, "custom semantic types remain valid plans");
        Assert.True(!CSharpArtifactEmitter.TryEmit(customPlan, customRegistry, "Generated", "CustomArtifact", out _, out var customDiagnostic), "a custom type without a companion factory is rejected");
        Assert.Equal("DXAMLGEN001", customDiagnostic?.Code, "missing factory has a stable diagnostic");

        var generatedCustomRegistry = XamlSemanticRegistry.CreateBuiltIns();
        generatedCustomRegistry.RegisterType(new(
            new UiTypeId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D404")),
            new XamlQualifiedName("urn:custom", "Badge"),
            XamlContentKind.None,
            System.Collections.Immutable.ImmutableArray.Create(
                new XamlPropertyDefinition(
                    new UiPropertyId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D405")),
                    "Label",
                    XamlValueKind.String,
                    "global::Sample.BadgeSetters.SetLabel")),
            "new global::Sample.Badge()"));
        var generatedCustomPlan = XamlCompiler.Compile(sourceId, "<Badge xmlns=\"urn:custom\" Label=\"hello\" />", generatedCustomRegistry);
        Assert.True(generatedCustomPlan.Success, "custom properties with a declared setter remain valid plans");
        Assert.True(CSharpArtifactEmitter.TryEmit(generatedCustomPlan, generatedCustomRegistry, "Generated", "GeneratedCustomArtifact", out var generatedCustomSource, out _), "custom property emits through its registered typed setter");
        Assert.True(generatedCustomSource.Contains("global::Sample.BadgeSetters.SetLabel(node0, \"hello\")", StringComparison.Ordinal), "custom property uses a direct generated setter thunk");
        Assert.True(!generatedCustomSource.Contains("Set(\"Label\"", StringComparison.Ordinal), "custom property does not use string runtime dispatch");

        var customCompositionRegistry = XamlSemanticRegistry.CreateBuiltIns();
        customCompositionRegistry.RegisterType(new(
            new UiTypeId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D408")),
            new XamlQualifiedName("urn:custom", "StackHost"),
            XamlContentKind.Children,
            System.Collections.Immutable.ImmutableArray<XamlPropertyDefinition>.Empty,
            "new global::Sample.StackHost()",
            "global::Sample.StackHostChildren.Add"));
        customCompositionRegistry.RegisterType(new(
            new UiTypeId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D409")),
            new XamlQualifiedName("urn:custom", "ContentHost"),
            XamlContentKind.SingleContent,
            System.Collections.Immutable.ImmutableArray<XamlPropertyDefinition>.Empty,
            "new global::Sample.ContentHost()",
            contentAttachmentExpression: "global::Sample.ContentHostContent.Set"));
        var customChildrenPlan = XamlCompiler.Compile(sourceId, "<StackHost xmlns=\"urn:custom\"><TextBlock xmlns=\"\" Text=\"child\" /></StackHost>", customCompositionRegistry);
        Assert.True(customChildrenPlan.Success, "custom children composition is a valid semantic plan");
        Assert.True(CSharpArtifactEmitter.TryEmit(customChildrenPlan, customCompositionRegistry, "Generated", "CustomChildrenArtifact", out var customChildrenSource, out _), "custom children composition emits");
        Assert.True(customChildrenSource.Contains("global::Sample.StackHostChildren.Add(node0, node1);", StringComparison.Ordinal), "custom children use the registered direct attachment thunk");
        var customContentPlan = XamlCompiler.Compile(sourceId, "<ContentHost xmlns=\"urn:custom\"><TextBlock xmlns=\"\" Text=\"content\" /></ContentHost>", customCompositionRegistry);
        Assert.True(customContentPlan.Success, "custom single content composition is a valid semantic plan");
        Assert.True(CSharpArtifactEmitter.TryEmit(customContentPlan, customCompositionRegistry, "Generated", "CustomContentArtifact", out var customContentSource, out _), "custom single content composition emits");
        Assert.True(customContentSource.Contains("global::Sample.ContentHostContent.Set(node0, node1);", StringComparison.Ordinal), "custom single content uses the registered direct attachment thunk");

        customCompositionRegistry.RegisterType(new(
            new UiTypeId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D40A")),
            new XamlQualifiedName("urn:custom", "MissingAttachmentHost"),
            XamlContentKind.Children,
            System.Collections.Immutable.ImmutableArray<XamlPropertyDefinition>.Empty,
            "new global::Sample.MissingAttachmentHost()"));
        var missingAttachmentPlan = XamlCompiler.Compile(sourceId, "<MissingAttachmentHost xmlns=\"urn:custom\"><TextBlock xmlns=\"\" /></MissingAttachmentHost>", customCompositionRegistry);
        Assert.True(missingAttachmentPlan.Success, "missing custom attachment remains a valid semantic plan");
        Assert.True(!CSharpArtifactEmitter.TryEmit(missingAttachmentPlan, customCompositionRegistry, "Generated", "MissingAttachmentArtifact", out _, out var missingAttachmentDiagnostic), "custom owner without an attachment thunk is rejected");
        Assert.Equal("DXAMLGEN003", missingAttachmentDiagnostic?.Code, "missing custom attachment has a stable diagnostic");
        Assert.True(missingAttachmentDiagnostic?.Message.Contains("child attachment thunk", StringComparison.Ordinal) == true, "missing custom attachment explains the required thunk");

        var incompleteCustomRegistry = XamlSemanticRegistry.CreateBuiltIns();
        incompleteCustomRegistry.RegisterType(new(
            new UiTypeId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D406")),
            new XamlQualifiedName("urn:custom", "NoSetterBadge"),
            XamlContentKind.None,
            System.Collections.Immutable.ImmutableArray.Create(
                new XamlPropertyDefinition(
                    new UiPropertyId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D407")),
                    "Label",
                    XamlValueKind.String)),
            "new global::Sample.NoSetterBadge()"));
        var incompleteCustomPlan = XamlCompiler.Compile(sourceId, "<NoSetterBadge xmlns=\"urn:custom\" Label=\"hello\" />", incompleteCustomRegistry);
        Assert.True(!CSharpArtifactEmitter.TryEmit(incompleteCustomPlan, incompleteCustomRegistry, "Generated", "IncompleteCustomArtifact", out _, out var incompleteCustomDiagnostic), "custom property without a typed setter is rejected");
        Assert.Equal("DXAMLGEN002", incompleteCustomDiagnostic?.Code, "missing custom setter has a stable diagnostic");

        var resourceRegistry = XamlSemanticRegistry.CreateBuiltIns();
        resourceRegistry.RegisterResource("Accent", new UiResourceId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D403")));
        var resourcePlan = XamlCompiler.Compile(sourceId, "<TextBlock Foreground=\"{StaticResource Accent}\" />", resourceRegistry);
        Assert.True(resourcePlan.Success, "resource markup remains a valid semantic plan");
        Assert.True(CSharpArtifactEmitter.TryEmit(resourcePlan, resourceRegistry, "Generated", "ResourceArtifact", out var resourceSource, out var resourceDiagnostic), "resource markup emits through the compiled resource path");
        Assert.True(resourceDiagnostic is null && resourceSource.Contains("SetStaticResource(global::Delta.XAML.TextBlockProperties.Foreground, Resources, _resourceIds[0])", StringComparison.Ordinal), "static resource values use the typed descriptor and artifact-local resource slot");
        Assert.True(resourceSource.Contains("private static readonly global::Delta.XAML.Contract.UiResourceId[] _resourceIds", StringComparison.Ordinal), "generated artifacts retain one stable resource identity table");
        Assert.True(!resourceSource.Contains("Resources, \"Accent\"", StringComparison.Ordinal), "generated resource values do not use name lookup");
        var resourceDocumentPlan = XamlCompiler.Compile(sourceId, "<Panel x:Key=\"Accent\" Background=\"#112233\" />", resourceRegistry);
        Assert.True(resourceDocumentPlan.Success && resourceDocumentPlan.Root?.Name.LocalName == "ResourceDictionary", "resource-only XAML gets a semantic resource root");
        Assert.True(CSharpArtifactEmitter.TryEmit(resourceDocumentPlan, resourceRegistry, "Generated", "ResourceDictionaryArtifact", out var resourceDocumentSource, out _), "resource-only XAML emits a catalog artifact");
        Assert.True(resourceDocumentSource.Contains("Resources.Set(_resourceIds[0], resource0);", StringComparison.Ordinal), "resource-only artifact registers its stable identity slot");
        Assert.True(!resourceDocumentSource.Contains("Document.Dispose();", StringComparison.Ordinal), "resource-only artifact does not dispose a missing document");

        var compositionRegistry = XamlSemanticRegistry.CreateBuiltIns();
        compositionRegistry.RegisterBinding(new(
            "Name",
            "global::Sample.BindingModel",
            "string",
            "source.Name",
            "source.Name = value"));
        var compositionPlan = XamlCompiler.Compile(
            sourceId,
            "<Panel><TextBlock StyleKey=\"Title\" TemplateKey=\"ButtonTemplate\" /><Style x:Key=\"Title\" TargetType=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"16\" /><VisualState Name=\"Pressed\"><Setter Property=\"FontSize\" Value=\"18\" /></VisualState></Style><Template x:Key=\"ButtonTemplate\"><Border><TextBlock Text=\"{Binding Name}\" /></Border></Template></Panel>",
            compositionRegistry);
        Assert.True(compositionPlan.Success, "styles and templates remain in the compiled semantic plan");
        Assert.True(compositionPlan.Styles.Length == 1 && compositionPlan.Styles[0].Id.IsValid, "compiled style has a stable identity");
        Assert.True(CSharpArtifactEmitter.TryEmit(compositionPlan, compositionRegistry, "Generated", "CompositionArtifact", out var compositionSource, out var compositionDiagnostic), "styles and templates emit through the companion path");
        Assert.True(compositionDiagnostic is null && compositionSource.Contains("new global::Delta.XAML.UiStyle(\"Title\", new global::Delta.XAML.UiTypeId", StringComparison.Ordinal), "compiled style initialization is direct and type-identity based");
        Assert.True(compositionSource.Contains("Theme.RegisterStyle(new global::Delta.XAML.UiStyleId", StringComparison.Ordinal), "compiled style registration uses its stable identity");
        Assert.True(compositionSource.Contains("node1.SetCompiledStyle(new global::Delta.XAML.UiStyleId", StringComparison.Ordinal), "compiled style selection uses its stable identity");
        Assert.True(compositionSource.Contains("Set(global::Delta.XAML.TextBlockProperties.FontSize, 16f)", StringComparison.Ordinal), "compiled style uses a typed property descriptor");
        Assert.True(compositionSource.Contains("SetState(global::Delta.XAML.UiStyleState.Pressed, global::Delta.XAML.TextBlockProperties.FontSize, 18f)", StringComparison.Ordinal), "compiled visual state uses a typed state setter");
        Assert.True(!compositionSource.Contains("Set(\"FontSize\"", StringComparison.Ordinal), "compiled style does not use string property dispatch");
        Assert.True(compositionSource.Contains("Theme.RegisterTemplate(new global::Delta.XAML.UiTemplateId", StringComparison.Ordinal), "compiled template registration uses a stable identity");
        Assert.True(compositionSource.Contains("SetCompiledTemplate(new global::Delta.XAML.UiTemplateId", StringComparison.Ordinal), "compiled template selection uses a stable identity");
        Assert.True(compositionSource.Contains("IUiTemplateFactory", StringComparison.Ordinal), "compiled templates use a typed factory boundary");
        Assert.True(!compositionSource.Contains("UiTemplate(owner =>", StringComparison.Ordinal), "compiled templates do not retain delegate construction");
        Assert.True(compositionSource.Contains("owner.BindingContext is not global::Sample.BindingModel templateContext", StringComparison.Ordinal), "compiled template bindings use the owner's typed context");
        Assert.True(compositionSource.Contains("template1.SetCompiledBinding(global::Delta.XAML.TextBlockProperties.Text, templateBinding0)", StringComparison.Ordinal), "compiled template bindings attach through the typed target");
        Assert.True(compositionSource.Contains("Theme.Apply(node0);", StringComparison.Ordinal), "compiled style/template state is applied after attachment");

        var typedResourceId = new UiResourceId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D403"));
        var typedResources = new Library.UiResourceCatalog();
        typedResources.Set(typedResourceId, new Library.UiColor(12, 34, 56));
        var typedStyle = new Library.UiStyle("Typed", new Library.UiTypeId(new Guid("22222222-2222-2222-2222-22222222220A")), typedResources);
        typedStyle.Set(Library.TextBlockProperties.FontSize, 18f);
        typedStyle.SetResource(Library.TextBlockProperties.Foreground, typedResourceId);
        typedStyle.SetState(Library.UiStyleState.Focused, Library.TextBlockProperties.FontSize, 20f);
        var styledText = new Library.TextBlock { StyleKey = "Typed" };
        var typedTheme = new Library.UiTheme();
        typedTheme.Add(typedStyle);
        typedTheme.Apply(styledText);
        Assert.Equal(18f, styledText.FontSize, "typed style descriptor applies its value");
        Assert.Equal(new Library.UiColor(12, 34, 56), styledText.Foreground, "typed resource identity applies its value");
        styledText.RetainedElement.SetFocused(true);
        typedTheme.Apply(styledText);
        Assert.Equal(20f, styledText.FontSize, "typed visual-state descriptor applies its value");

        var compiledStyleId = new Library.UiStyleId(new Guid("E2D9B7AB-6F54-4FE4-8A3F-5F2F1F5E4701"));
        var compiledStyle = new Library.UiStyle("Compiled", new Library.UiTypeId(new Guid("22222222-2222-2222-2222-22222222220A")));
        compiledStyle.Set(Library.TextBlockProperties.FontSize, 21f);
        var compiledTheme = new Library.UiTheme();
        compiledTheme.RegisterStyle(compiledStyleId, compiledStyle);
        var compiledText = new Library.TextBlock();
        compiledText.SetCompiledStyle(compiledStyleId);
        compiledTheme.Apply(compiledText);
        Assert.Equal(21f, compiledText.FontSize, "compiled style identity applies without a name lookup");

        var typedTemplateId = new Library.UiTemplateId(new Guid("D5A5E3D7-0B26-4A5A-A9D7-1E8F768A7E02"));
        typedTheme.RegisterTemplate(typedTemplateId, new Library.UiTemplate(new LabelTemplateFactory()));
        var typedTemplateHost = new Library.UiContentControl();
        typedTemplateHost.SetCompiledTemplate(typedTemplateId);
        typedTheme.Apply(typedTemplateHost);
        Assert.True(typedTemplateHost.Content is Library.TextBlock typedTemplateContent && typedTemplateContent.Text == "templated", "typed template identity selects a retained template");

        var bindingRegistry = XamlSemanticRegistry.CreateBuiltIns();
        bindingRegistry.RegisterBinding(new(
            "Name",
            "global::Sample.BindingModel",
            "string",
            "source.Name",
            "source.Name = value"));
        var bindingPlan = XamlCompiler.Compile(sourceId, "<TextBlock Text=\"{Binding Name, Mode=TwoWay}\" />", bindingRegistry);
        Assert.True(bindingPlan.Success, "declared binding markup is a valid semantic plan");
        Assert.True(CSharpArtifactEmitter.TryEmit(bindingPlan, bindingRegistry, "Generated", "BindingArtifact", out var bindingSource, out var bindingDiagnostic), "declared binding has a typed artifact");
        Assert.True(bindingDiagnostic is null, "typed binding emission has no diagnostic");
        Assert.True(bindingSource.Contains("UiCompiledBinding<global::Sample.BindingModel, string>", StringComparison.Ordinal), "binding artifact preserves source and value types");
        Assert.True(bindingSource.Contains("static source => source.Name", StringComparison.Ordinal), "binding read accessor is direct and static");
        Assert.True(bindingSource.Contains("static (source, value) => source.Name = value", StringComparison.Ordinal), "two-way binding write accessor is direct and static");
        Assert.True(bindingSource.Contains("SetCompiledBinding(global::Delta.XAML.TextBlockProperties.Text", StringComparison.Ordinal), "generated binding uses the typed target attachment");
        Assert.True(!bindingSource.Contains("SetBinding(\"Text\"", StringComparison.Ordinal), "generated binding bypasses the cold interpreted path");
        Assert.True(bindingSource.Contains("RefreshBindings", StringComparison.Ordinal), "binding artifact exposes one direct refresh batch");
        Assert.True(bindingSource.Contains("_bindingTarget0.QueueCompiledBindingRefresh(global::Delta.XAML.TextBlockProperties.Text, _binding0);", StringComparison.Ordinal), "binding batch queues the typed target for the binding stage");
        Assert.True(bindingSource.Contains("SetCompiledBinding(global::Delta.XAML.TextBlockProperties.Text, _binding0, true);", StringComparison.Ordinal), "generated binding marks source notifications as batch-managed");
        Assert.True(!bindingSource.Contains("_binding0.NotifyChanged();", StringComparison.Ordinal), "binding batch does not fan out through per-binding notifications");
        Assert.True(bindingSource.Contains("_bindingSource.PropertyChanged += OnContextPropertyChanged", StringComparison.Ordinal), "binding artifact uses one source notification boundary");
        Assert.True(bindingSource.Contains("UiBindingMode.TwoWay, false", StringComparison.Ordinal), "generated bindings disable per-binding source subscriptions");
        Assert.True(!bindingSource.Contains("GetProperty", StringComparison.Ordinal) && !bindingSource.Contains("Split", StringComparison.Ordinal), "typed binding artifact has no reflection or path traversal");

        bindingRegistry.RegisterBinding(new(
            "Count",
            "global::Sample.BindingModel",
            "int",
            "source.Count",
            "source.Count = value"));
        var formattedPlan = XamlCompiler.Compile(
            sourceId,
            "<TextBlock Text=\"{Binding Count, StringFormat='Items: {0:N0}', Culture=en-US}\" />",
            bindingRegistry);
        Assert.True(formattedPlan.Success, "formatted binding with an explicit culture is a valid semantic plan");
        Assert.True(CSharpArtifactEmitter.TryEmit(formattedPlan, bindingRegistry, "Generated", "FormattedBindingArtifact", out var formattedSource, out _), "formatted binding emits through the typed artifact path");
        Assert.True(formattedSource.Contains("UiCompiledBinding<global::Sample.BindingModel, global::System.String>", StringComparison.Ordinal), "formatted target has a typed string binding slot");
        Assert.True(formattedSource.Contains("CultureInfo.GetCultureInfo(\"en-US\")", StringComparison.Ordinal), "generated formatting embeds its explicit culture");
        Assert.True(formattedSource.Contains("source.Count", StringComparison.Ordinal) && !formattedSource.Contains("GetProperty", StringComparison.Ordinal), "formatted binding keeps direct typed source access");
        var implicitCulturePlan = XamlCompiler.Compile(
            sourceId,
            "<TextBlock Text=\"{Binding Count, StringFormat='Items: {0}'}\" />",
            bindingRegistry);
        Assert.True(!implicitCulturePlan.Success && HasCode(implicitCulturePlan.Diagnostics, "XAML008"), "StringFormat without an explicit culture has a stable compile diagnostic");

        bindingRegistry.RegisterBinding(new(
            "Other",
            "global::Sample.BindingModel",
            "string",
            "source.Other",
            "source.Other = value"));
        var targetedBindingPlan = XamlCompiler.Compile(
            sourceId,
            "<Panel><TextBlock Text=\"{Binding Name}\" /><TextBlock Text=\"{Binding Other}\" /></Panel>",
            bindingRegistry);
        Assert.True(targetedBindingPlan.Success, "multiple source bindings are a valid semantic plan");
        Assert.True(CSharpArtifactEmitter.TryEmit(targetedBindingPlan, bindingRegistry, "Generated", "TargetedBindingArtifact", out var targetedBindingSource, out _), "multiple source bindings emit");
        Assert.True(targetedBindingSource.Contains("case \"Name\":", StringComparison.Ordinal), "generated binding batch routes the Name notification");
        Assert.True(targetedBindingSource.Contains("case \"Other\":", StringComparison.Ordinal), "generated binding batch routes the Other notification");
        Assert.True(targetedBindingSource.Contains("case null:", StringComparison.Ordinal) && targetedBindingSource.Contains("case \"\":", StringComparison.Ordinal), "all-properties notifications refresh the complete binding batch");
        Assert.True(targetedBindingSource.Contains("default:\n                break;", StringComparison.Ordinal), "unrelated source notifications do not refresh binding slots");
        Assert.True(!targetedBindingSource.Contains("OnContextPropertyChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs args) => RefreshBindings();", StringComparison.Ordinal), "source notification routing is generated as a typed switch");

        bindingRegistry.RegisterBinding(new(
            "DisplayName",
            "global::Sample.BindingModel",
            "string",
            "source.Name.ToUpperInvariant()",
            "source.Name = value",
            "Upper"));
        var converterPlan = XamlCompiler.Compile(sourceId, "<TextBlock Text=\"{Binding DisplayName, Converter=Upper, Mode=TwoWay}\" />", bindingRegistry);
        Assert.True(converterPlan.Success, "typed converter binding markup is a valid semantic plan");
        Assert.True(CSharpArtifactEmitter.TryEmit(converterPlan, bindingRegistry, "Generated", "ConverterBindingArtifact", out var converterSource, out var converterDiagnostic), "registered typed converter binding emits");
        Assert.True(converterDiagnostic is null && converterSource.Contains("static source => source.Name.ToUpperInvariant()", StringComparison.Ordinal), "converter binding emits its direct typed read expression");
        Assert.True(converterSource.Contains("static (source, value) => source.Name = value", StringComparison.Ordinal), "converter binding preserves its direct typed write expression");
        var unknownConverterPlan = XamlCompiler.Compile(sourceId, "<TextBlock Text=\"{Binding DisplayName, Converter=Missing}\" />", bindingRegistry);
        Assert.True(!CSharpArtifactEmitter.TryEmit(unknownConverterPlan, bindingRegistry, "Generated", "UnknownConverterArtifact", out _, out var unknownConverterDiagnostic), "unregistered typed converter is rejected");
        Assert.Equal("DXAMLGEN002", unknownConverterDiagnostic?.Code, "unregistered converter has a stable diagnostic");

        var oneTimeModel = new BindingModel { Name = "Initial" };
        using var oneTime = new UiCompiledBinding<BindingModel, string>(oneTimeModel, static model => model.Name, mode: UiBindingMode.OneTime);
        var oneTimeNotifications = 0;
        oneTime.Changed += (_, _) => oneTimeNotifications++;
        oneTimeModel.Name = "Updated";
        Assert.Equal(0, oneTimeNotifications, "one-time binding does not subscribe to source notifications");

        var batchedModel = new BindingModel { Name = "Batched" };
        using var batched = new UiCompiledBinding<BindingModel, string>(
            batchedModel,
            static model => model.Name,
            mode: UiBindingMode.OneWay,
            subscribeToSource: false);
        var batchedNotifications = 0;
        batched.Changed += (_, _) => batchedNotifications++;
        batchedModel.Name = "Changed";
        Assert.Equal(0, batchedNotifications, "batched binding does not subscribe per binding");
        batched.NotifyChanged();
        Assert.Equal(1, batchedNotifications, "batched binding refreshes through the explicit batch boundary");

        var directModel = new BindingModel { Name = "Direct" };
        var directText = new Library.TextBlock();
        using var directBinding = new UiCompiledBinding<BindingModel, string>(
            directModel,
            static model => model.Name,
            mode: UiBindingMode.OneWay,
            subscribeToSource: false);
        directText.SetCompiledBinding(Library.TextBlockProperties.Text, directBinding);
        Assert.Equal("Direct", directText.Text, "typed binding attachment applies its initial value");
        directModel.Name = "Queued";
        directBinding.NotifyChanged();
        Assert.Equal("Direct", directText.Text, "typed binding notification waits for the binding stage");
        using var directTextService = new EmptyTextService();
        using var directDocument = new Library.UiDocument(directText, directTextService);
        directDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal("Queued", directText.Text, "typed binding stage applies the queued value");

        var sourceManagedModel = new BindingModel { Name = "ManagedInitial" };
        var sourceManagedText = new Library.TextBlock();
        using var sourceManagedBinding = new UiCompiledBinding<BindingModel, string>(
            sourceManagedModel,
            static model => model.Name,
            mode: UiBindingMode.OneWay,
            subscribeToSource: false);
        sourceManagedText.SetCompiledBinding(Library.TextBlockProperties.Text, sourceManagedBinding, true);
        sourceManagedModel.Name = "ManagedQueued";
        sourceManagedText.QueueCompiledBindingRefresh(Library.TextBlockProperties.Text, sourceManagedBinding);
        Assert.Equal("ManagedInitial", sourceManagedText.Text, "source-managed refresh waits for the binding stage");
        using var sourceManagedTextService = new EmptyTextService();
        using var sourceManagedDocument = new Library.UiDocument(sourceManagedText, sourceManagedTextService);
        sourceManagedDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal("ManagedQueued", sourceManagedText.Text, "source-managed binding applies at the binding stage");

        var replacementModel = new BindingModel { Name = "Replacement" };
        using var replacementBinding = new UiCompiledBinding<BindingModel, string>(
            replacementModel,
            static model => model.Name,
            mode: UiBindingMode.OneWay,
            subscribeToSource: false);
        directText.SetCompiledBinding(Library.TextBlockProperties.Text, replacementBinding);
        directModel.Name = "Stale";
        directBinding.NotifyChanged();
        directDocument.Layout(new Delta.Maths.float2(100, 30), 1);
        Assert.Equal("Replacement", directText.Text, "replacing a typed binding detaches the previous runtime");
    }
}
