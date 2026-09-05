using System.Text.Json;
using Delta;
using Delta.Diagnostics;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXAML.Compiler;
using DeltaXAML.Generator;
using DeltaXAML.Internal;
using PublicThickness = Delta.XAML.UiThickness;

internal static class LayoutPlacementTests
{
    internal static void Run()
    {
        ElementAlignmentPlacesFixedElementInsideParentSlot();
        ElementAlignmentPlacesFixedElementInsideGridCellAndBorderContentBox();
        StretchAndMarginUseTheSameElementPlacementPath();
        PlacementPropertiesInvalidateOnlyTheirRequiredStages();
        XamlAndGeneratedPathsExposeElementPlacementProperties();
        CompactThicknessLiteralsExpandForMarginAndPadding();
        InvalidPlacementValuesAreRejectedAtEveryEntryPoint();
        TextPlacementIsIndependentFromElementPlacement();
        AutomaticPlacementStaysInsideAResizedViewport();
    }

    private static void ElementAlignmentPlacesFixedElementInsideParentSlot()
    {
        var parent = new UiPanel { Width = 100, Height = 80 };
        var child = new UiBorder
        {
            Width = 20,
            Height = 10,
            HorizontalAlignment = UiHorizontalAlignment.Center,
            VerticalAlignment = UiVerticalAlignment.End,
        };
        parent.Add(child);

        RetainedLayoutTest.Layout(parent.RetainedElement, new(100, 80), new(0, 0, 100, 80));

        Assert.Equal(new UiRect(40, 70, 20, 10), child.RetainedElement.Bounds, "element alignment positions a fixed child in its parent slot");
    }

    private static void ElementAlignmentPlacesFixedElementInsideGridCellAndBorderContentBox()
    {
        var grid = new UiGrid { Width = 100, Height = 100 };
        grid.SetColumns(UiGridLength.Pixel(50), UiGridLength.Pixel(50));
        grid.SetRows(UiGridLength.Pixel(50), UiGridLength.Pixel(50));
        var gridChild = new UiBorder
        {
            Width = 20,
            Height = 10,
            HorizontalAlignment = UiHorizontalAlignment.Center,
            VerticalAlignment = UiVerticalAlignment.End,
        };
        gridChild.SetAttachedValue(UiGridAttachedProperties.Row, 1);
        gridChild.SetAttachedValue(UiGridAttachedProperties.Column, 1);
        grid.Add(gridChild);

        RetainedLayoutTest.Layout(grid.RetainedElement, new(100, 100), new(0, 0, 100, 100));

        Assert.Equal(new UiRect(65, 90, 20, 10), gridChild.RetainedElement.Bounds, "element alignment positions a child inside a Grid cell");

        var border = new UiBorder { Width = 100, Height = 60, Padding = new PublicThickness(5, 5, 5, 5) };
        var borderChild = new UiBorder
        {
            Width = 20,
            Height = 10,
            HorizontalAlignment = UiHorizontalAlignment.End,
            VerticalAlignment = UiVerticalAlignment.Center,
        };
        border.SetChild(borderChild);

        RetainedLayoutTest.Layout(border.RetainedElement, new(100, 60), new(0, 0, 100, 60));

        Assert.Equal(new UiRect(75, 25, 20, 10), borderChild.RetainedElement.Bounds, "element alignment positions a child inside a Border content box");
    }

    private static void StretchAndMarginUseTheSameElementPlacementPath()
    {
        var parent = new UiPanel { Width = 100, Height = 80 };
        var stretched = new UiBorder { Margin = new PublicThickness(5, 6, 7, 8) };
        parent.Add(stretched);

        RetainedLayoutTest.Measure(parent.RetainedElement, new(100, 80));
        Assert.Equal(new UiSize(12, 14), stretched.RetainedElement.DesiredSize, "margin is part of measured size");
        RetainedLayoutTest.Layout(parent.RetainedElement, new(100, 80), new(0, 0, 100, 80));

        Assert.Equal(new UiRect(5, 6, 88, 66), stretched.RetainedElement.Bounds, "automatic stretch fills the slot after margin");
        Assert.True((stretched.RetainedElement.DirtyFlags & UiDirtyMask.Arrange) == 0, "placement completes arrange without leaving an arrange invalidation");
    }

    private static void PlacementPropertiesInvalidateOnlyTheirRequiredStages()
    {
        var parent = new UiPanel { Width = 100, Height = 80 };
        var child = new UiBorder { Width = 20, Height = 10 };
        parent.Add(child);
        RetainedLayoutTest.Layout(parent.RetainedElement, new(100, 80), new(0, 0, 100, 80));

        child.HorizontalAlignment = UiHorizontalAlignment.Center;
        Assert.True((child.RetainedElement.DirtyFlags & UiDirtyMask.Arrange) != 0, "horizontal alignment invalidates arrange");
        Assert.True((child.RetainedElement.DirtyFlags & UiDirtyMask.Measure) == 0, "horizontal alignment does not invalidate measure");
        RetainedLayoutTest.Layout(parent.RetainedElement, new(100, 80), new(0, 0, 100, 80));

        child.Margin = new PublicThickness(1, 2, 3, 4);
        Assert.True((child.RetainedElement.DirtyFlags & (UiDirtyMask.Measure | UiDirtyMask.Arrange)) == (UiDirtyMask.Measure | UiDirtyMask.Arrange), "margin invalidates measure and arrange");

        RetainedLayoutTest.Layout(parent.RetainedElement, new(100, 80), new(0, 0, 100, 80));
        child.Width = 30;
        Assert.True((child.RetainedElement.DirtyFlags & (UiDirtyMask.Measure | UiDirtyMask.Arrange | UiDirtyMask.Visual)) == (UiDirtyMask.Measure | UiDirtyMask.Arrange | UiDirtyMask.Visual), "width invalidates measure, arrange and visual output");

        RetainedLayoutTest.Layout(parent.RetainedElement, new(100, 80), new(0, 0, 100, 80));
        child.Height = 15;
        Assert.True((child.RetainedElement.DirtyFlags & (UiDirtyMask.Measure | UiDirtyMask.Arrange | UiDirtyMask.Visual)) == (UiDirtyMask.Measure | UiDirtyMask.Arrange | UiDirtyMask.Visual), "height invalidates measure, arrange and visual output");

        RetainedLayoutTest.Layout(parent.RetainedElement, new(100, 80), new(0, 0, 100, 80));
        child.Padding = new PublicThickness(2, 3, 4, 5);
        Assert.True((child.RetainedElement.DirtyFlags & (UiDirtyMask.Measure | UiDirtyMask.Arrange | UiDirtyMask.Visual)) == (UiDirtyMask.Measure | UiDirtyMask.Arrange | UiDirtyMask.Visual), "padding invalidates measure, arrange and visual output");
    }

    private static void XamlAndGeneratedPathsExposeElementPlacementProperties()
    {
        const string source = "<Panel Width=\"100\" Height=\"80\"><Border Width=\"20\" Height=\"10\" Margin=\"1,2,3,4\" HorizontalAlignment=\"Center\" VerticalAlignment=\"End\" /></Panel>";
        var registry = XamlSemanticRegistry.CreateBuiltIns();
        var plan = XamlCompiler.Compile(
            new SourceId(new Guid("C4A9E6CF-2E44-4EE6-92D6-8CF4F0E3C101")),
            source,
            registry);
        Assert.True(plan.Success, "element placement properties compile as common XAML properties");
        Assert.True(CSharpArtifactEmitter.TryEmit(plan, registry, "Generated", "PlacementArtifact", out var artifact, out _), "element placement properties emit through the generated path");
        Assert.True(artifact.Contains("node1.Margin = new global::Delta.XAML.UiThickness(1f, 2f, 3f, 4f)", StringComparison.Ordinal), "generated artifact emits the typed margin value");
        Assert.True(artifact.Contains("node1.HorizontalAlignment = global::Delta.XAML.UiHorizontalAlignment.Center", StringComparison.Ordinal), "generated artifact emits horizontal placement");
        Assert.True(artifact.Contains("node1.VerticalAlignment = global::Delta.XAML.UiVerticalAlignment.End", StringComparison.Ordinal), "generated artifact emits vertical placement");

        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var loaded = loader.Load(source, in context);
        Assert.True(loaded.Success && loaded.Root is UiPanel { Children.Count: 1 }, "interpreted loader accepts common element placement properties");
        if (loaded.Root is UiPanel { Children.Count: 1 } loadedPanel && loadedPanel.Children[0] is UiBorder loadedChild)
        {
            Assert.Equal(new PublicThickness(1, 2, 3, 4), loadedChild.Margin, "interpreted loader preserves margin");
            Assert.Equal(UiHorizontalAlignment.Center, loadedChild.HorizontalAlignment, "interpreted loader preserves horizontal placement");
            Assert.Equal(UiVerticalAlignment.End, loadedChild.VerticalAlignment, "interpreted loader preserves vertical placement");
        }
        else
        {
            throw new InvalidOperationException("interpreted placement tree is missing the Border child");
        }
    }

    private static void InvalidPlacementValuesAreRejectedAtEveryEntryPoint()
    {
        var element = new UiBorder();
        Assert.Throws<ArgumentOutOfRangeException>(() => element.Width = -1, "negative Width is rejected by the public API");
        Assert.Throws<ArgumentOutOfRangeException>(() => element.Height = float.PositiveInfinity, "infinite Height is rejected by the public API");
        Assert.Throws<ArgumentOutOfRangeException>(() => element.Margin = new PublicThickness(float.NaN, 0, 0, 0), "non-finite Margin is rejected by the public API");
        Assert.Throws<ArgumentOutOfRangeException>(() => element.Padding = new PublicThickness(0, float.NegativeInfinity, 0, 0), "non-finite Padding is rejected by the public API");
        element.Width = float.NaN;

        var invalid = XamlCompiler.Compile(
            new SourceId(new Guid("D0E5F4EF-3F95-45E9-A48C-9C02A3E1C8B3")),
            "<Border Width=\"-1\" Height=\"Infinity\" Margin=\"0,NaN,0,0\" Padding=\"0,0,Infinity,0\" />",
            XamlSemanticRegistry.CreateBuiltIns());
        Assert.True(!invalid.Success, "invalid placement literals are rejected by the compiler");
        Assert.True(HasDiagnostic(invalid.Diagnostics, "XAML007"), "invalid placement literals produce the canonical compiler diagnostic");

        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var loaded = loader.Load("<Border Width=\"-1\" Padding=\"0,0,Infinity,0\" />", in context);
        Assert.True(!loaded.Success && loaded.Diagnostics.Length != 0, "invalid placement literals produce a loader diagnostic");
    }

    private static void CompactThicknessLiteralsExpandForMarginAndPadding()
    {
        const string source = "<Border Margin=\"10\" Padding=\"2,3\" />";
        var registry = XamlSemanticRegistry.CreateBuiltIns();
        var plan = XamlCompiler.Compile(
            new SourceId(new Guid("EAE3E9FC-8E8C-45F1-9AA1-5D7F54A1C09E")),
            source,
            registry);
        Assert.True(plan.Success, "compact Margin and Padding literals compile");
        Assert.True(CSharpArtifactEmitter.TryEmit(plan, registry, "Generated", "CompactThicknessArtifact", out var artifact, out _), "compact thickness literals emit through the generated path");
        Assert.True(artifact.Contains("new global::Delta.XAML.UiThickness(10f, 10f, 10f, 10f)", StringComparison.Ordinal), "one thickness value expands to all sides in generated code");
        Assert.True(artifact.Contains("new global::Delta.XAML.UiThickness(2f, 3f, 2f, 3f)", StringComparison.Ordinal), "two thickness values expand to horizontal and vertical sides in generated code");

        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyLibraryTypeResolver(), new EmptyLibraryResourceResolver());
        var loaded = loader.Load(source, in context);
        Assert.True(loaded.Success && loaded.Root is UiBorder, "interpreted loader accepts compact thickness literals");
        if (loaded.Root is UiBorder border)
        {
            Assert.Equal(new PublicThickness(10, 10, 10, 10), border.Margin, "one Margin value applies to every side");
            Assert.Equal(new PublicThickness(2, 3, 2, 3), border.Padding, "two Padding values apply to horizontal and vertical sides");
        }
    }

    private static void TextPlacementIsIndependentFromElementPlacement()
    {
        var root = new UiBorder { Width = 100, Height = 60 };
        var text = new UiTextBlock
        {
            Text = "Delta",
            Width = 80,
            Height = 40,
            HorizontalAlignment = UiHorizontalAlignment.Center,
            VerticalAlignment = UiVerticalAlignment.Center,
            HorizontalTextAlignment = UiTextHorizontalAlignment.Center,
            VerticalTextAlignment = UiTextVerticalAlignment.Bottom,
        };
        root.SetChild(text);

        using var document = new UiDocument(root, new EmptyTextService());
        document.Layout(new float2(100, 60), 1);
        using var json = JsonDocument.Parse(document.BuildLayoutDiagnosticsJson(false));
        var textNode = json.RootElement.GetProperty("root").GetProperty("children")[0];
        var bounds = ReadRect(textNode.GetProperty("bounds"));
        var textBounds = ReadRect(textNode.GetProperty("textBounds"));
        Assert.Equal(new JsonRect(10, 10, 80, 40), bounds, "element alignment positions the TextBlock inside its parent");
        Assert.True(textBounds.X > bounds.X && textBounds.Right <= bounds.Right && textBounds.Bottom <= bounds.Bottom, "text alignment moves text inside the arranged TextBlock");
        Assert.Equal(50f, textBounds.Bottom, "bottom text alignment uses the TextBlock's arranged bottom");
    }

    private static void AutomaticPlacementStaysInsideAResizedViewport()
    {
        var root = new UiPanel();
        var child = new UiBorder();
        root.Add(child);
        using var document = new UiDocument(root, new EmptyTextService());

        document.Layout(new float2(100, 80), 1);
        document.Layout(new float2(30, 30), 1);
        using var json = JsonDocument.Parse(document.BuildLayoutDiagnosticsJson(false));
        var rootNode = json.RootElement.GetProperty("root");
        var rootBounds = ReadRect(rootNode.GetProperty("bounds"));
        var childBounds = ReadRect(rootNode.GetProperty("children")[0].GetProperty("bounds"));
        Assert.Equal(new JsonRect(0, 0, 30, 30), rootBounds, "automatic root placement follows the resized viewport");
        Assert.Equal(new JsonRect(0, 0, 30, 30), childBounds, "automatic child placement follows the resized parent slot");
        Assert.True(Contains(rootBounds, childBounds), "automatic placement stays inside the resized viewport");
    }

    private static bool HasDiagnostic(IEnumerable<Diagnostic> diagnostics, string code)
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

    private static JsonRect ReadRect(JsonElement value) => new(
        value.GetProperty("x").GetSingle(),
        value.GetProperty("y").GetSingle(),
        value.GetProperty("width").GetSingle(),
        value.GetProperty("height").GetSingle());

    private static bool Contains(JsonRect container, JsonRect value) =>
        value.X >= container.X &&
        value.Y >= container.Y &&
        value.Right <= container.Right &&
        value.Bottom <= container.Bottom;

    private readonly record struct JsonRect(float X, float Y, float Width, float Height)
    {
        internal float Right => X + Width;
        internal float Bottom => Y + Height;
    }
}
