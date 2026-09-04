using Delta;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;

internal static class TextAlignmentTests
{
    internal static void Run()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf"));
        var fonts = new UiFontCatalog();
        fonts.Register("default", new FontSourceId(new Guid("317c0b5b-e50a-4be4-b9ba-eef7816a0041")), bytes);
        using var service = new CountingTextService();
        var text = new UiTextBlock
        {
            Text = "Delta",
            FontSize = 48,
            HorizontalTextAlignment = UiTextHorizontalAlignment.Center,
            VerticalTextAlignment = UiTextVerticalAlignment.Center,
        };
        using var document = new UiDocument(text, service, fonts);
        foreach (var scale in new[] { 1f, 1.5f, 2f })
        {
            foreach (var value in new[] { "Delta", "gj", "ÅÉ", "iiii", "WWWW" })
            {
                text.Text = value;
                document.Layout(new(300, 150), scale);
                var display = document.BuildDisplayList();
                var draw = display.Text[0];
                AssertCentered(draw, scale, 150, 75);
                var run = draw.Text.Runs.Span[0];
                var metrics = service.GetFontMetrics(run.Font, run.PixelsPerEm);
                var desired = text.RetainedElement.DesiredSize;
                Near((metrics.Ascent + metrics.Descent + metrics.LineGap) / scale, desired.Height, "real font line height");
                Near(Maths.Max(run.AdvanceX, run.Bounds.Width) / scale, desired.Width, "real shaped width");

                var count = service.ShapeCount;
                document.Layout(new(400, 200), scale);
                var resized = document.BuildDisplayList().Text[0];
                AssertCentered(resized, scale, 200, 100);
                Assert.True(ReferenceEquals(draw.Text, resized.Text), "resize reuses shaping");
                Assert.Equal(count, service.ShapeCount, "resize does not reshape text");
            }
        }

        text.LineHeight = 100;
        document.Layout(new(400, 200), 2);
        AssertCentered(document.BuildDisplayList().Text[0], 2, 200, 100);
        Near(100, text.RetainedElement.DesiredSize.Height, "explicit line height remains logical");
        text.VerticalTextAlignment = UiTextVerticalAlignment.Bottom;
        text.HorizontalTextAlignment = UiTextHorizontalAlignment.Right;
        document.Layout(new(400, 200), 2);
        var bottom = document.BuildDisplayList().Text[0];
        var bounds = bottom.Text.Runs.Span[0].Bounds;
        Near(200, bottom.BaselineOrigin.y + bounds.Bottom / 2, "bottom aligns ink bottom");
        Near(400, bottom.BaselineOrigin.x + bounds.Right / 2, "right aligns ink right");
        text.VerticalTextAlignment = UiTextVerticalAlignment.Top;
        text.HorizontalTextAlignment = UiTextHorizontalAlignment.Left;
        document.Layout(new(400, 200), 2);
        var top = document.BuildDisplayList().Text[0];
        Near(0, top.BaselineOrigin.y + bounds.Top / 2, "top aligns ink top");
        Near(0, top.BaselineOrigin.x + bounds.Left / 2, "left aligns ink left");

        text.Text = "abc אבג 123";
        text.VerticalTextAlignment = UiTextVerticalAlignment.Center;
        text.HorizontalTextAlignment = UiTextHorizontalAlignment.Center;
        document.Layout(new(400, 200), 2);
        var mixed = document.BuildDisplayList().Text[0];
        Assert.True(mixed.Text.Runs.Length > 1, "mixed-direction fixture has several runs");
        AssertCentered(mixed, 2, 200, 100);

        for (var i = 0; i < 20; i++)
        {
            document.Layout(new(400, 200), 2);
            _ = document.BuildDisplayList();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var shapeCount = service.ShapeCount;
        for (var i = 0; i < 100; i++)
        {
            document.Layout(new(400, 200), 2);
            _ = document.BuildDisplayList();
        }

        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before, "100 unchanged text frames allocate nothing");
        Assert.Equal(shapeCount, service.ShapeCount, "100 unchanged frames do not reshape");
        text.Foreground = new UiColor(128, 192, 255);
        document.Layout(new(400, 200), 2);
        _ = document.BuildDisplayList();
        Assert.Equal(shapeCount, service.ShapeCount, "paint changes reuse shaping");

        var editor = new UiTextBox
        {
            PlaceholderText = "Delta",
            FontSize = 48,
            HorizontalTextAlignment = UiTextHorizontalAlignment.Center,
            VerticalTextAlignment = UiTextVerticalAlignment.Center,
        };
        using var editorDocument = new UiDocument(editor, service, fonts);
        editorDocument.Layout(new(300, 150), 2);
        AssertCentered(editorDocument.BuildDisplayList().Text[0], 2, 150, 75);
        var placeholderWidth = editor.RetainedElement.DesiredSize.Width;
        editor.PlaceholderText = "Wide placeholder";
        editorDocument.Layout(new(600, 150), 2);
        AssertCentered(editorDocument.BuildDisplayList().Text[0], 2, 300, 75);
        Assert.True(editor.RetainedElement.DesiredSize.Width > placeholderWidth, "placeholder mutations remeasure displayed text");
        using var numberDocument = new UiDocument(new UiNumericEditor
        {
            Text = "123",
            FontSize = 48,
            HorizontalTextAlignment = UiTextHorizontalAlignment.Center,
            VerticalTextAlignment = UiTextVerticalAlignment.Center,
        }, service, fonts);
        numberDocument.Layout(new(300, 150), 2);
        AssertCentered(numberDocument.BuildDisplayList().Text[0], 2, 150, 75);

        var rich = new UiRichTextBlock
        {
            Spans = new UiTextSpan[]
            {
                new("Delta", "default", 24, new UiColor(240, 240, 240)),
                new(" XAML", "default", 18, new UiColor(160, 200, 255)),
            },
        };
        using var richDocument = new UiDocument(rich, service, fonts);
        richDocument.Layout(new(300, 150), 1);
        var richDisplay = richDocument.BuildDisplayList();
        Assert.Equal(2, richDisplay.Text.Length, "rich text uses the shared shaping cache for each span");
        Assert.True(rich.RetainedElement.DesiredSize.Width > 0 && rich.RetainedElement.DesiredSize.Height > 0,
            "rich text receives measured metrics during the regular measure traversal");
        var richShapeCount = service.ShapeCount;
        richDocument.Layout(new(420, 180), 1);
        _ = richDocument.BuildDisplayList();
        Assert.Equal(richShapeCount, service.ShapeCount, "rich-text resize reuses shaped spans without a second shape pass");
    }

    private static void AssertCentered(UiTextDraw draw, float scale, float x, float y)
    {
        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;
        var pen = draw.BaselineOrigin * scale;
        foreach (var run in draw.Text.Runs.Span)
        {
            left = Maths.Min(left, pen.x + run.Bounds.Left);
            top = Maths.Min(top, pen.y + run.Bounds.Top);
            right = Maths.Max(right, pen.x + run.Bounds.Right);
            bottom = Maths.Max(bottom, pen.y + run.Bounds.Bottom);
            pen += new float2(run.AdvanceX, run.AdvanceY);
        }

        Near(x, (left + right) / (2 * scale), "horizontal ink center");
        Near(y, (top + bottom) / (2 * scale), "vertical ink center");
    }

    private static void Near(float expected, float actual, string message) =>
        Assert.True(Maths.Abs(expected - actual) < 0.001f, $"{message}: {expected} != {actual}");
}
