using System.Globalization;
using Delta;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Tests;

internal static class PlatformBoundaryTests
{
    internal static void Run()
    {
        ResourceBrushUsesCanonicalVisual();
        GradientResourcesOwnValidatedStops();
        GradientResourcesUpdateThroughOneDynamicSlot();
        AccessibilitySnapshotIsBorrowedAndRetained();
        LocalizationIsExplicitAndInvalidatesText();
        CollectionRowsExposeSetMetadata();
        HostServicesRemainSemanticRequests();
    }

    private static void GradientResourcesOwnValidatedStops()
    {
        var source = new[]
        {
            new UiGradientStop(0, new UiColor(1, 2, 3)),
            new UiGradientStop(1, new UiColor(4, 5, 6)),
        };
        var gradient = new UiLinearGradient(0, 0, 1, 1, source);
        source[0] = new(0.5f, new UiColor(7, 8, 9));
        Assert.Equal(0f, gradient.Stops.Span[0].Offset, "gradient resources own their stop snapshot for deterministic invalidation");
        Assert.Throws<ArgumentException>(
            CreateUnorderedGradient,
            "unordered gradient stops are rejected at the public resource boundary");
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateRadialGradient(source),
            "a radial gradient rejects a non-positive radius");
    }

    private static void CreateUnorderedGradient() =>
        _ = new UiLinearGradient(0, 0, 1, 1,
            new[] { new UiGradientStop(0.75f, default), new UiGradientStop(0.25f, default) });

    private static void CreateRadialGradient(ReadOnlyMemory<UiGradientStop> stops) =>
        _ = new UiRadialGradient(0.5f, 0.5f, 0, stops);

    private static void ResourceBrushUsesCanonicalVisual()
    {
        var gradient = new UiResourceId(new Guid("285A7033-E9EB-438E-81D8-7906CF978301"));
        var panel = new UiPanel { Width = 80, Height = 40, BackgroundBrush = UiBrush.LinearGradient(gradient) };
        using var text = new EmptyTextService();
        using var document = new UiDocument(panel, text);
        document.Layout(new float2(80, 40), 1);
        var display = document.BuildDisplayList();
        Assert.Equal(1, display.Visuals.Length, "gradient emits one canonical visual");
        Assert.Equal(UiVisualKind.Custom, display.Visuals[0].Kind, "gradient uses the frozen custom visual kind");
        Assert.Equal(gradient, display.Visuals[0].Resource, "gradient preserves its stable renderer resource");
        Assert.Equal(UiKnownVisuals.LinearGradient, display.Visuals[0].VisualType, "gradient keeps a stable semantic visual identity");
    }

    private static void AccessibilitySnapshotIsBorrowedAndRetained()
    {
        var root = new UiStackPanel { Width = 120, Height = 80 };
        var label = new TextBlock { Text = "Volume", AutomationName = "Volume label" };
        var slider = new UiSlider { AutomationName = "Volume", Value = 0.5 };
        root.Add(label);
        root.Add(slider);
        using var text = new EmptyTextService();
        using var document = new UiDocument(root, text);
        document.Layout(new float2(120, 80), 1);
        var first = document.BuildSemanticSnapshot();
        Assert.Equal(3, first.Nodes.Length, "semantic extraction follows the retained tree without platform objects");
        Assert.Equal(UiSemanticRole.Slider, first.Nodes[2].Role, "slider exposes its value-level semantic role");
        Assert.True((first.Nodes[2].Actions & UiSemanticActions.Increment) != 0, "slider exposes semantic increment without a callback object");
        var version = first.Version;
        var second = document.BuildSemanticSnapshot();
        Assert.Equal(version, second.Version, "unchanged semantic extraction reuses retained storage and version");
    }

    private static void GradientResourcesUpdateThroughOneDynamicSlot()
    {
        var brushSlot = new UiResourceId(new Guid("FEAAE4B7-9B3A-49F8-94E9-D18F84D2FB31"));
        var linear = new UiResourceId(new Guid("0DFFCA58-49B0-4374-9D3E-049A1D489A0A"));
        var radial = new UiResourceId(new Guid("2E8B661D-0FC6-4455-B7A0-B94F6E014769"));
        var stops = new UiGradientStop[]
        {
            new(0, new UiColor(20, 40, 80)),
            new(1, new UiColor(120, 180, 240)),
        };
        var resources = new UiResourceCatalog();
        resources.Set(linear, new UiLinearGradient(0, 0, 1, 1, stops));
        resources.Set(radial, new UiRadialGradient(0.5f, 0.5f, 0.5f, stops));
        resources.Set(brushSlot, UiBrush.LinearGradient(linear));
        var panel = new UiPanel { Width = 80, Height = 40 };
        panel.SetDynamicResource(UiElementProperties.BackgroundBrush, resources, brushSlot);
        using var text = new EmptyTextService();
        using var document = new UiDocument(panel, text);
        document.Layout(new float2(80, 40), 1);
        var first = document.BuildDisplayList();
        Assert.Equal(linear, first.Visuals[0].Resource, "linear-gradient payload remains a stable resource identity at extraction");
        Assert.True(resources.TryResolve(linear, out var payload) && payload is UiLinearGradient, "resource catalog retains renderer-neutral gradient stops");

        resources.Set(brushSlot, UiBrush.RadialGradient(radial));
        document.Layout(new float2(80, 40), 1);
        var second = document.BuildDisplayList();
        Assert.Equal(radial, second.Visuals[0].Resource, "dynamic brush mutation invalidates only the dependent canonical visual");
        Assert.Equal(UiKnownVisuals.RadialGradient, second.Visuals[0].VisualType, "brush-kind change preserves its semantic visual type");
    }

    private static void LocalizationIsExplicitAndInvalidatesText()
    {
        var root = new TextBlock { Text = "مرحبا" };
        using var text = new EmptyTextService();
        using var document = new UiDocument(root, text);
        var before = root.RetainedElement.OutputVersion;
        document.Localization = new UiLocalizationContext(CultureInfo.GetCultureInfo("ar-EG"), TextDirection.RightToLeft, "ar");
        Assert.True(root.RetainedElement.OutputVersion > before, "changing explicit localization invalidates retained text output");
        Assert.Equal("ar-EG", document.Localization.Culture.Name, "document preserves explicit formatting culture");
        Assert.Equal(TextDirection.RightToLeft, document.Localization.Direction, "document preserves explicit shaping direction");
    }

    private static void CollectionRowsExposeSetMetadata()
    {
        var source = new CollectionRowSource(new(1, "one"), new(2, "two"), new(3, "three"));
        var collection = new UiCollectionView();
        var presenter = new UiVirtualizingPresenter<CollectionRow, CollectionRowSource, SemanticRowPlan>(collection.ItemsHost, source);
        presenter.Realize(new(0, 3));
        using var text = new EmptyTextService();
        using var document = new UiDocument(collection, text);
        document.Layout(new float2(120, 80), 1);
        var snapshot = document.BuildSemanticSnapshot();
        var row = snapshot.Nodes[^1];
        Assert.Equal(UiSemanticRole.ListItem, row.Role, "realized rows expose a list-item semantic role");
        Assert.Equal(3, row.PositionInSet, "semantic row position follows retained collection order");
        Assert.Equal(3, row.SetSize, "semantic row set size follows the realized range");
        Assert.True((row.Actions & UiSemanticActions.Select) != 0, "collection rows expose neutral selection semantics");
    }

    private static void HostServicesRemainSemanticRequests()
    {
        var source = new UiButton { Command = new(new Guid("89C115F9-4DC8-4E13-8A13-71DC79F52550")) };
        using var text = new EmptyTextService();
        using var document = new UiDocument(source, text);
        document.Layout(new float2(80, 24), 1);

        UiGeneratedActions.Publish(document, source, UiSemanticActionKind.Navigate, "details/7");
        UiGeneratedActions.Publish(document, source, UiSemanticActionKind.OpenUri, "https://example.invalid/docs");
        UiGeneratedActions.Publish(document, source, UiSemanticActionKind.BeginDragDrop, "application/x-delta");
        UiGeneratedActions.Publish(document, source, UiSemanticActionKind.OpenFileDialog, "*.xaml");
        UiGeneratedActions.Publish(document, source, UiSemanticActionKind.RequestAsset, "asset://icon/save");

        Assert.Equal(5, document.SemanticCommands.Length, "host-owned operations remain a compact borrowed semantic request batch");
        Assert.Equal(UiSemanticActionKind.OpenUri, document.SemanticCommands[1].Action, "URI activation is data rather than an OS callback");
        Assert.Equal("*.xaml", document.SemanticCommands[3].Argument, "file-dialog request preserves its host policy argument");
    }

    private readonly struct SemanticRowPlan : IUiItemTemplatePlan<SemanticRowPlan, CollectionRow>
    {
        private static readonly UiTemplateId Template = new(new Guid("D17C84CF-1D88-4C44-97AE-E11A3587CF83"));

        public static UiTemplateId SelectTemplate(in CollectionRow item) => Template;

        public static UiElement Create(UiTemplateId templateId, in CollectionRow item, UiResourceCatalog resources) =>
            new TextBlock { Text = item.Label };

        public static void Bind(UiElement element, UiTemplateId templateId, in CollectionRow item, UiResourceCatalog resources) =>
            ((TextBlock)element).Text = item.Label;

        public static void Unbind(UiElement element, UiTemplateId templateId)
        {
        }
    }
}
