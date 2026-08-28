using System.Collections.Immutable;
using Delta.Diagnostics;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXAML.Compiler;

internal static partial class Program
{
    private static void XamlSemanticCompilerTests()
    {
        var registry = XamlSemanticRegistry.CreateBuiltIns();
        var accent = new UiResourceId(new Guid("33333333-3333-3333-3333-333333333301"));
        registry.RegisterResource("Accent", accent);
        registry.RegisterResource("PanelResource", new UiResourceId(new Guid("33333333-3333-3333-3333-333333333302")));
        registry.RegisterResource("AccentValue", new UiResourceId(new Guid("33333333-3333-3333-3333-333333333303")));
        var customType = new UiTypeId(new Guid("55555555-5555-5555-5555-555555555501"));
        var customProperty = new UiPropertyId(new Guid("55555555-5555-5555-5555-555555555502"));
        registry.RegisterType(new(
            customType,
            new XamlQualifiedName("urn:custom", "Badge"),
            XamlContentKind.None,
            ImmutableArray.Create(new XamlPropertyDefinition(customProperty, "Label", XamlValueKind.String))));
        const string source = "<Panel xmlns=\"urn:delta\" x:Name=\"Root\" Width=\"120\">\n  <TextBlock x:Name=\"Label\" Text=\"Hello &amp; world\" FontSize=\"16\" Foreground=\"{DynamicResource Accent}\" />\n  <Grid Columns=\"40,Auto,2*\" />\n</Panel>";

        var sourceId = new SourceId(new Guid("44444444-4444-4444-4444-444444444401"));
        var first = XamlCompiler.Compile(sourceId, source, registry);
        var second = XamlCompiler.Compile(sourceId, source, registry);
        Assert.True(first.Success, "valid XAML produces a successful semantic plan");
        if (first.Root is not { } firstRoot || second.Root is not { } secondRoot)
        {
            throw new InvalidOperationException("Semantic plan has no root.");
        }

        Assert.Equal(firstRoot.Type, secondRoot.Type, "type identity is deterministic");
        Assert.Equal(firstRoot.Range, secondRoot.Range, "element ranges are deterministic");
        Assert.Equal(2, firstRoot.Children.Length, "children are retained in source order");
        Assert.Equal("Width=\"120\"", source[firstRoot.Members[0].Range.Start.Offset..firstRoot.Members[0].Range.End.Offset], "member range is exact");
        Assert.Equal(XamlValueKind.Single, firstRoot.Members[0].Value.Kind, "literal value is typed");
        Assert.Equal(UiElementProperties.Width.Id, firstRoot.Members[0].Property, "common property identity matches the runtime descriptor");
        Assert.Equal("16", firstRoot.Children[0].Members[1].Value.Literal.CanonicalText, "numeric literal is canonical");
        Assert.Equal(XamlValueKind.ResourceReference, firstRoot.Children[0].Members[2].Value.Kind, "resource markup is semantic");
        Assert.Equal(accent, firstRoot.Children[0].Members[2].Value.Resource.Id, "resource identity is stable and resolved");
        Assert.Equal(UiTextBlockProperties.Foreground.Id, firstRoot.Children[0].Members[2].Property, "text property identity matches the runtime descriptor");
        Assert.Equal("Hello & world", firstRoot.Children[0].Members[0].Value.Literal.CanonicalText, "XML entities are decoded in typed literals");

        var resource = XamlCompiler.Compile(sourceId, "<Panel x:Key=\"PanelResource\" />", registry);
        Assert.True(resource.Success && resource.Resources.Length == 1, "resource declarations are retained in the semantic plan");
        Assert.Equal(new UiResourceId(new Guid("33333333-3333-3333-3333-333333333302")), resource.Resources[0].Id, "resource declaration uses its registered identity");

        var custom = XamlCompiler.Compile(sourceId, "<Badge xmlns=\"urn:custom\" Label=\"badge\" />", registry);
        Assert.True(custom.Success && custom.Root is not null, "explicit custom type resolves without reflection");
        if (custom.Root is { } customRoot)
        {
            Assert.Equal(customType, customRoot.Type, "custom type identity is stable");
            Assert.Equal(customProperty, customRoot.Members[0].Property, "custom property identity is stable");
        }

        var starGrid = XamlCompiler.Compile(sourceId, "<Grid Columns=\"40,*,Auto\" Rows=\"*\" />", registry);
        Assert.True(starGrid.Success, "semantic compiler accepts the canonical bare-star grid length");

        var errors = XamlCompiler.Compile(
            sourceId,
            "<Panel Width=\"bad\" Broken=bad Height=\"20\"><TextBlock x:Name=\"Same\" /><Button x:Name=\"Same\" Missing=\"x\" /></Panel>",
            registry);
        Assert.True(!errors.Success, "invalid XAML is not successful");
        Assert.True(HasCode(errors.Diagnostics, "XAML007"), "invalid typed literal is diagnosed");
        Assert.True(HasCode(errors.Diagnostics, "XAML019"), "malformed attribute is diagnosed");
        Assert.True(HasCode(errors.Diagnostics, "XAML010"), "duplicate names are diagnosed");
        Assert.True(HasCode(errors.Diagnostics, "XAML003"), "unknown properties are diagnosed");
        Assert.True(errors.Root is not null && errors.Root.Members.Any(static member => member.Name == "Height"), "attribute recovery continues after a local error");

        var unsupported = XamlCompiler.Compile(sourceId, "<Panel><Missing><TextBlock /></Missing><Button /></Panel>", registry);
        Assert.True(HasCode(unsupported.Diagnostics, "XAML002"), "unknown element is diagnosed");
        Assert.True(unsupported.Root is not null && unsupported.Root.Children.Length == 2, "sibling recovery preserves later elements");

        var missingResource = XamlCompiler.Compile(sourceId, "<TextBlock Foreground=\"{StaticResource Missing}\" />", registry);
        Assert.True(HasCode(missingResource.Diagnostics, "XAML006"), "missing resource identity is diagnosed");

        var resourceDocument = XamlCompiler.Compile(
            sourceId,
            "<ResourceDictionary><TextBlock x:Key=\"AccentValue\" Foreground=\"#112233\" /><TextBlock Foreground=\"{DynamicResource AccentValue}\" /><Style x:Key=\"TitleStyle\" TargetType=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"16\" /><Setter Property=\"Foreground\" Value=\"#AABBCC\" /><VisualState Name=\"Hover\"><Setter Property=\"Foreground\" Value=\"#DDEEFF\" /></VisualState></Style><Template x:Key=\"ButtonTemplate\"><Border><TextBlock Text=\"Template\" /></Border></Template></ResourceDictionary>",
            registry);
        Assert.True(resourceDocument.Success, "resource, style and template declarations recover into one document plan");
        Assert.Equal(1, resourceDocument.Resources.Length, "x:Key resource declaration is retained");
        Assert.Equal(1, resourceDocument.Styles.Length, "Style declaration is retained");
        Assert.Equal(2, resourceDocument.Styles[0].Setters.Length, "Style setters are typed and ordered");
        Assert.Equal("FontSize", resourceDocument.Styles[0].Setters[0].Name, "style setter preserves property identity");
        Assert.Equal(1, resourceDocument.Styles[0].VisualStates.Length, "VisualState declaration is retained");
        Assert.Equal(XamlVisualStateName.Hover, resourceDocument.Styles[0].VisualStates[0].State, "VisualState uses the closed state vocabulary");
        Assert.Equal(1, resourceDocument.Templates.Length, "Template declaration is retained");
        Assert.Equal("Border", resourceDocument.Templates[0].Root.Name.LocalName, "template keeps its semantic visual root");
        Assert.Equal(1, resourceDocument.ResourceSlots.Length, "one stable resource identity uses one local slot");
        Assert.Equal(new UiResourceId(new Guid("33333333-3333-3333-3333-333333333303")), resourceDocument.ResourceSlots[0].Id, "resource slot retains the registered stable identity");
        Assert.True(resourceDocument.ResourceSlots[0].IsDynamic, "dynamic resource reference marks its dependency slot");
        Assert.Equal(0, resourceDocument.Root?.Children[0].Members[0].Value.Resource.Slot, "resource reference points at its compact local slot");

        var duplicateDeclarations = XamlCompiler.Compile(
            sourceId,
            "<ResourceDictionary><Style x:Key=\"Title\" TargetType=\"TextBlock\"><VisualState Name=\"Hover\" /><VisualState Name=\"Hover\" /></Style><Style x:Key=\"Title\" TargetType=\"TextBlock\" /><Template x:Key=\"ButtonTemplate\"><Border /></Template><Template x:Key=\"ButtonTemplate\"><Border /></Template></ResourceDictionary>",
            registry);
        Assert.True(!duplicateDeclarations.Success, "duplicate compiled declarations are rejected");
        Assert.True(HasCode(duplicateDeclarations.Diagnostics, "XAML031"), "duplicate style key has a source diagnostic");
        Assert.True(HasCode(duplicateDeclarations.Diagnostics, "XAML032"), "duplicate visual state has a source diagnostic");
        Assert.True(HasCode(duplicateDeclarations.Diagnostics, "XAML033"), "duplicate template key has a source diagnostic");
    }

    private static bool HasCode(IEnumerable<Diagnostic> diagnostics, string code)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Code.Value == code)
            {
                return true;
            }
        }

        return false;
    }
}
