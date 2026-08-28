using Delta.Diagnostics;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXAML.Compiler;
using DeltaXAML.Generator;

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

        var resourceRegistry = XamlSemanticRegistry.CreateBuiltIns();
        resourceRegistry.RegisterResource("Accent", new UiResourceId(new Guid("A4B05D1A-0A47-4E8C-B1B8-5DDA7EA1D403")));
        var resourcePlan = XamlCompiler.Compile(sourceId, "<TextBlock Foreground=\"{StaticResource Accent}\" />", resourceRegistry);
        Assert.True(resourcePlan.Success, "resource markup remains a valid semantic plan");
        Assert.True(!CSharpArtifactEmitter.TryEmit(resourcePlan, resourceRegistry, "Generated", "ResourceArtifact", out _, out var resourceDiagnostic), "resource values wait for the compiled resource slice");
        Assert.Equal("DXAMLGEN002", resourceDiagnostic?.Code, "deferred resource emission has a stable diagnostic");
    }
}
