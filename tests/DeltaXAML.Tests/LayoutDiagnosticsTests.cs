using System.Text.Json;
using Delta;
using Delta.XAML;

internal static class LayoutDiagnosticsTests
{
    internal static void Run()
    {
        GridChildrenDoNotIntersectWhenPlacedInDistinctCells();
        GridChildrenStayInsideGridWhenRequestedSizeIsLarger();

        var root = new UiPanel { Width = 100, Height = 80, Padding = new(3, 4, 5, 6) };
        var first = new UiBorder { Width = 40, Height = 20 };
        var nested = new UiBorder { Width = 12, Height = 8 };
        first.SetChild(nested);
        root.Add(first);

        using var text = new EmptyTextService();
        using var document = new UiDocument(root, text);
        using (var beforeLayout = JsonDocument.Parse(document.BuildLayoutDiagnosticsJson(false)))
        {
            Assert.True(!beforeLayout.RootElement.GetProperty("layoutCompleted").GetBoolean(), "layout diagnostics mark an unlaid document");
            Assert.True(beforeLayout.RootElement.GetProperty("root").GetProperty("requestedSize").GetProperty("width").GetSingle() == 100, "requested width is preserved before layout");
        }

        document.Layout(new float2(100, 80), 1);
        var firstJson = document.BuildLayoutDiagnosticsJson(false);
        var secondJson = document.BuildLayoutDiagnosticsJson(false);
        Assert.Equal(firstJson, secondJson, "unchanged layout diagnostics are deterministic");

        using var parsed = JsonDocument.Parse(firstJson);
        var jsonRoot = parsed.RootElement;
        Assert.Equal(2, jsonRoot.GetProperty("schemaVersion").GetInt32(), "layout diagnostics schema is versioned");
        Assert.True(jsonRoot.GetProperty("layoutCompleted").GetBoolean(), "layout diagnostics mark a completed layout");
        Assert.Equal(100f, jsonRoot.GetProperty("viewport").GetProperty("width").GetSingle(), "viewport width is reported");
        Assert.Equal(80f, jsonRoot.GetProperty("viewport").GetProperty("height").GetSingle(), "viewport height is reported");

        var rootNode = jsonRoot.GetProperty("root");
        Assert.Equal("Panel", rootNode.GetProperty("type").GetString(), "root type is reported");
        Assert.Equal(100f, rootNode.GetProperty("bounds").GetProperty("width").GetSingle(), "root arranged width is reported");
        Assert.Equal(80f, rootNode.GetProperty("bounds").GetProperty("height").GetSingle(), "root arranged height is reported");
        Assert.Equal(3f, rootNode.GetProperty("padding").GetProperty("left").GetSingle(), "layout input padding is reported");
        Assert.Equal(1, rootNode.GetProperty("children").GetArrayLength(), "hierarchy keeps the child count");

        var firstNode = rootNode.GetProperty("children")[0];
        Assert.Equal(0, firstNode.GetProperty("childIndex").GetInt32(), "hierarchy keeps child order");
        Assert.Equal("Border", firstNode.GetProperty("type").GetString(), "child type is reported");
        Assert.Equal(100f, firstNode.GetProperty("bounds").GetProperty("width").GetSingle(), "panel layout bounds are reported");
        Assert.Equal(40f, firstNode.GetProperty("requestedSize").GetProperty("width").GetSingle(), "child requested width is reported separately from its arranged bounds");
        Assert.Equal(20f, firstNode.GetProperty("requestedSize").GetProperty("height").GetSingle(), "child requested height is reported separately from its arranged bounds");

        var nestedNode = firstNode.GetProperty("children")[0];
        Assert.Equal(12f, nestedNode.GetProperty("requestedSize").GetProperty("width").GetSingle(), "nested requested width is reported");
        Assert.Equal(8f, nestedNode.GetProperty("desiredSize").GetProperty("height").GetSingle(), "nested desired height is reported");

        using var textDocument = new UiDocument(new UiTextBlock { Text = "diagnostic text" }, new EmptyTextService());
        textDocument.Layout(new float2(100, 30), 1);
        using var textJson = JsonDocument.Parse(textDocument.BuildLayoutDiagnosticsJson(false));
        Assert.Equal("diagnostic text", textJson.RootElement.GetProperty("root").GetProperty("text").GetString(), "text content is reported with its layout node");
    }

    private static void GridChildrenDoNotIntersectWhenPlacedInDistinctCells()
    {
        var grid = CreateDiagnosticGrid();
        for (var index = 0; index < 4; index++)
        {
            var child = new UiBorder { Fill = true };
            child.SetAttachedValue(UiGridAttachedProperties.Row, index / 2);
            child.SetAttachedValue(UiGridAttachedProperties.Column, index % 2);
            grid.Add(child);
        }

        using var text = new EmptyTextService();
        using var document = new UiDocument(grid, text);
        document.Layout(new float2(100, 100), 1);

        using var json = JsonDocument.Parse(document.BuildLayoutDiagnosticsJson(false));
        var root = json.RootElement.GetProperty("root");
        var children = root.GetProperty("children");
        Assert.Equal(0, CountIntersections(root.GetProperty("bounds"), children, "bounds"), "Grid children in distinct cells do not overlap according to JSON bounds");
    }

    private static void GridChildrenStayInsideGridWhenRequestedSizeIsLarger()
    {
        var grid = CreateDiagnosticGrid();
        for (var index = 0; index < 2; index++)
        {
            var child = new UiBorder { Width = 30, Height = 30, Fill = true };
            child.SetAttachedValue(UiGridAttachedProperties.Row, index);
            child.SetAttachedValue(UiGridAttachedProperties.Column, index);
            grid.Add(child);
        }

        using var text = new EmptyTextService();
        using var document = new UiDocument(grid, text);
        document.Layout(new float2(100, 100), 1);

        using var json = JsonDocument.Parse(document.BuildLayoutDiagnosticsJson(false));
        var root = json.RootElement.GetProperty("root");
        var children = root.GetProperty("children");
        Assert.Equal(0, CountOutside(root.GetProperty("bounds"), children, "bounds"), "Grid children remain inside the Grid bounds despite larger requested sizes");
        Assert.Equal(0, CountOutside(root.GetProperty("bounds"), children, "clip"), "Grid child clips remain inside the Grid bounds");
    }

    private static UiGrid CreateDiagnosticGrid()
    {
        var grid = new UiGrid { Width = 100, Height = 100 };
        grid.SetColumns(UiGridLength.Pixel(50), UiGridLength.Pixel(50));
        grid.SetRows(UiGridLength.Pixel(50), UiGridLength.Pixel(50));
        return grid;
    }

    private static int CountIntersections(JsonElement container, JsonElement children, string rectangleName)
    {
        var containerRect = ReadRect(container);
        var count = 0;
        for (var first = 0; first < children.GetArrayLength(); first++)
        {
            var firstRect = ReadRect(children[first].GetProperty(rectangleName));
            for (var second = first + 1; second < children.GetArrayLength(); second++)
            {
                var secondRect = ReadRect(children[second].GetProperty(rectangleName));
                if (HasPositiveAreaIntersection(firstRect, secondRect) &&
                    Contains(containerRect, firstRect) &&
                    Contains(containerRect, secondRect))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountOutside(JsonElement container, JsonElement children, string rectangleName)
    {
        var containerRect = ReadRect(container);
        var count = 0;
        for (var index = 0; index < children.GetArrayLength(); index++)
        {
            if (!Contains(containerRect, ReadRect(children[index].GetProperty(rectangleName))))
            {
                count++;
            }
        }

        return count;
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

    private static bool HasPositiveAreaIntersection(JsonRect first, JsonRect second) =>
        first.X < second.Right &&
        second.X < first.Right &&
        first.Y < second.Bottom &&
        second.Y < first.Bottom;

    private readonly record struct JsonRect(float X, float Y, float Width, float Height)
    {
        internal float Right => X + Width;
        internal float Bottom => Y + Height;
    }
}
