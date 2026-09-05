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
                var run = draw.Text.Runs.Span[0];
                var metrics = service.GetFontMetrics(run.Font, run.PixelsPerEm);
                AssertMetricAligned(
                    text,
                    draw,
                    scale,
                    metrics,
                    UiTextHorizontalAlignment.Center,
                    UiTextVerticalAlignment.Center,
                    text.LineHeight);
                var desired = text.RetainedElement.DesiredSize;
                Near((metrics.Ascent + metrics.Descent + metrics.LineGap) / scale, desired.Height, "real font line height");
                Near(Maths.Max(run.AdvanceX, run.Bounds.Width) / scale, desired.Width, "real shaped width");

                var count = service.ShapeCount;
                document.Layout(new(400, 200), scale);
                var resized = document.BuildDisplayList().Text[0];
                AssertMetricAligned(
                    text,
                    resized,
                    scale,
                    metrics,
                    UiTextHorizontalAlignment.Center,
                    UiTextVerticalAlignment.Center,
                    text.LineHeight);
                Assert.True(ReferenceEquals(draw.Text, resized.Text), "resize reuses shaping");
                Assert.Equal(count, service.ShapeCount, "resize does not reshape text");
            }
        }

        text.LineHeight = 100;
        document.Layout(new(400, 200), 2);
        var explicitLineHeightDraw = document.BuildDisplayList().Text[0];
        var explicitLineHeightMetrics = service.GetFontMetrics(
            explicitLineHeightDraw.Text.Runs.Span[0].Font,
            explicitLineHeightDraw.Text.Runs.Span[0].PixelsPerEm);
        AssertMetricAligned(
            text,
            explicitLineHeightDraw,
            2,
            explicitLineHeightMetrics,
            UiTextHorizontalAlignment.Center,
            UiTextVerticalAlignment.Center,
            text.LineHeight);
        Near(100, text.RetainedElement.DesiredSize.Height, "explicit line height remains logical");
        text.VerticalTextAlignment = UiTextVerticalAlignment.Bottom;
        text.HorizontalTextAlignment = UiTextHorizontalAlignment.Right;
        document.Layout(new(400, 200), 2);
        var bottom = document.BuildDisplayList().Text[0];
        var bottomMetrics = service.GetFontMetrics(bottom.Text.Runs.Span[0].Font, bottom.Text.Runs.Span[0].PixelsPerEm);
        AssertMetricAligned(
            text,
            bottom,
            2,
            bottomMetrics,
            UiTextHorizontalAlignment.Right,
            UiTextVerticalAlignment.Bottom,
            text.LineHeight);
        text.VerticalTextAlignment = UiTextVerticalAlignment.Top;
        text.HorizontalTextAlignment = UiTextHorizontalAlignment.Left;
        document.Layout(new(400, 200), 2);
        var top = document.BuildDisplayList().Text[0];
        var topMetrics = service.GetFontMetrics(top.Text.Runs.Span[0].Font, top.Text.Runs.Span[0].PixelsPerEm);
        AssertMetricAligned(
            text,
            top,
            2,
            topMetrics,
            UiTextHorizontalAlignment.Left,
            UiTextVerticalAlignment.Top,
            text.LineHeight);

        text.Text = "abc אבג 123";
        text.VerticalTextAlignment = UiTextVerticalAlignment.Center;
        text.HorizontalTextAlignment = UiTextHorizontalAlignment.Center;
        document.Layout(new(400, 200), 2);
        var mixed = document.BuildDisplayList().Text[0];
        Assert.True(mixed.Text.Runs.Length > 1, "mixed-direction fixture has several runs");
        var mixedMetrics = service.GetFontMetrics(mixed.Text.Runs.Span[0].Font, mixed.Text.Runs.Span[0].PixelsPerEm);
        AssertMetricAligned(
            text,
            mixed,
            2,
            mixedMetrics,
            UiTextHorizontalAlignment.Center,
            UiTextVerticalAlignment.Center,
            text.LineHeight);

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
        var editorDraw = editorDocument.BuildDisplayList().Text[0];
        var editorMetrics = service.GetFontMetrics(editorDraw.Text.Runs.Span[0].Font, editorDraw.Text.Runs.Span[0].PixelsPerEm);
        AssertMetricAligned(
            editor,
            editorDraw,
            2,
            editorMetrics,
            UiTextHorizontalAlignment.Center,
            UiTextVerticalAlignment.Center,
            editor.LineHeight);
        var placeholderWidth = editor.RetainedElement.DesiredSize.Width;
        editor.PlaceholderText = "Wide placeholder";
        editorDocument.Layout(new(600, 150), 2);
        editorDraw = editorDocument.BuildDisplayList().Text[0];
        AssertMetricAligned(
            editor,
            editorDraw,
            2,
            editorMetrics,
            UiTextHorizontalAlignment.Center,
            UiTextVerticalAlignment.Center,
            editor.LineHeight);
        Assert.True(editor.RetainedElement.DesiredSize.Width > placeholderWidth, "placeholder mutations remeasure displayed text");
        var number = new UiNumericEditor
        {
            Text = "123",
            FontSize = 48,
            HorizontalTextAlignment = UiTextHorizontalAlignment.Center,
            VerticalTextAlignment = UiTextVerticalAlignment.Center,
        };
        using var numberDocument = new UiDocument(number, service, fonts);
        numberDocument.Layout(new(300, 150), 2);
        var numberDraw = numberDocument.BuildDisplayList().Text[0];
        var numberMetrics = service.GetFontMetrics(numberDraw.Text.Runs.Span[0].Font, numberDraw.Text.Runs.Span[0].PixelsPerEm);
        AssertMetricAligned(
            number,
            numberDraw,
            2,
            numberMetrics,
            UiTextHorizontalAlignment.Center,
            UiTextVerticalAlignment.Center,
            number.LineHeight);

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

    private static void AssertMetricAligned(
        UiElement element,
        UiTextDraw draw,
        float scale,
        FontMetrics metrics,
        UiTextHorizontalAlignment horizontal,
        UiTextVerticalAlignment vertical,
        float explicitLineHeight)
    {
        var bounds = element.RetainedElement.Bounds;
        var desired = element.RetainedElement.DesiredSize;
        var textWidth = Maths.Min(desired.Width, Maths.Max(0, bounds.Width));
        var textHeight = Maths.Min(desired.Height, Maths.Max(0, bounds.Height));
        var horizontalFactor = horizontal switch
        {
            UiTextHorizontalAlignment.Center => 0.5f,
            UiTextHorizontalAlignment.Right => 1f,
            _ => 0f,
        };
        var verticalFactor = vertical switch
        {
            UiTextVerticalAlignment.Center => 0.5f,
            UiTextVerticalAlignment.Bottom => 1f,
            _ => 0f,
        };
        var lineHeight = explicitLineHeight > 0
            ? explicitLineHeight
            : (metrics.Ascent + metrics.Descent + metrics.LineGap) / scale;
        var naturalLineHeight = (metrics.Ascent + metrics.Descent + metrics.LineGap) / scale;
        var lineTop = bounds.Y + (bounds.Height - textHeight) * verticalFactor;
        var advanceWidth = 0f;
        foreach (var run in draw.Text.Runs.Span)
        {
            advanceWidth += Maths.Abs(run.AdvanceX) / scale;
        }

        var expected = new float2(
            bounds.X + (bounds.Width - textWidth) * horizontalFactor +
            (textWidth - advanceWidth) * horizontalFactor,
            lineTop + (textHeight - lineHeight) * verticalFactor +
            metrics.Ascent / scale + Maths.Max(0, lineHeight - naturalLineHeight) * 0.5f);
        Near(expected.x, draw.BaselineOrigin.x, "baseline horizontal alignment");
        Near(expected.y, draw.BaselineOrigin.y, "baseline vertical alignment");
    }

    private static void Near(float expected, float actual, string message) =>
        Assert.True(Maths.Abs(expected - actual) < 0.001f, $"{message}: {expected} != {actual}");
}
