using Delta;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;

internal static class RichTextTests
{
    internal static void Run()
    {
        ParagraphEmitsOrderedRunsAndReusesShapeForPaint();
        ParagraphSharesBaselinesAndDirectionAcrossStyleRuns();
    }

    private static void ParagraphEmitsOrderedRunsAndReusesShapeForPaint()
    {
        var link = new UiCommandId(new Guid("B3288FCA-9DC6-4B90-AC40-C1C23366C301"));
        var rich = new UiRichTextBlock
        {
            Width = 240,
            Height = 30,
            Spans = new UiTextSpan[]
            {
                new("Delta", "default", 14, new UiColor(200, 20, 20), link, "docs"),
                new("XAML", "default", 14, new UiColor(20, 200, 20)),
            },
        };
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new UiFontCatalog();
        fonts.Register("default", new FontSourceId(new Guid("B3288FCA-9DC6-4B90-AC40-C1C23366C302")), File.ReadAllBytes(fontPath));
        using var text = new CountingTextService();
        using var document = new UiDocument(rich, text, fonts);
        document.Layout(new float2(240, 30), 1);
        var first = document.BuildDisplayList();
        Assert.Equal(2, first.Text.Length, "one rich paragraph may emit several canonical text draws");
        var firstIdentity = first.Identities[0];
        Assert.True(firstIdentity.Value != 0 && firstIdentity.Generation != 0, "rich text output carries the retained paragraph identity");
        Assert.True(firstIdentity.Version > 0, "rich text output carries the producer version");
        Assert.Equal(2, text.ShapeCount, "each typography span is shaped once on its initial paragraph layout");
        var retained = (DeltaXAML.Internal.RichTextBlock)rich.RetainedElement;
        var hitStorage = retained.State.HitRanges;

        rich.Spans = new UiTextSpan[]
        {
            new("Delta", "default", 14, new UiColor(20, 20, 220), link, "docs"),
            new("XAML", "default", 14, new UiColor(220, 220, 20)),
        };
        document.Layout(new float2(240, 30), 1);
        var painted = document.BuildDisplayList();
        Assert.Equal(2, text.ShapeCount, "paint-only rich-text changes preserve shaped runs");
        Assert.True(painted.Text[0].Color.z > painted.Text[0].Color.x, "paint-only update reaches the canonical text draw");
        Assert.Equal(firstIdentity.Value, painted.Identities[0].Value, "rich text paint changes preserve paragraph identity");
        Assert.Equal(firstIdentity.Generation, painted.Identities[0].Generation, "rich text paint changes preserve paragraph generation");
        Assert.True(painted.Identities[0].Version > firstIdentity.Version, "rich text paint changes advance the producer version");
        Assert.True(ReferenceEquals(hitStorage, retained.State.HitRanges), "paint-only extraction reuses retained inline hit storage");

        document.Dispatch(UiInputEvent.FromPointingDevice(Pointer(UiPointerEventKind.ButtonDown, 3, 8)));
        document.Dispatch(UiInputEvent.FromPointingDevice(Pointer(UiPointerEventKind.ButtonUp, 3, 8)));
        document.Layout(new float2(240, 30), 1);
        Assert.Equal(1, document.SemanticCommands.Length, "inline link hit range publishes one semantic action");
        Assert.Equal(link, document.SemanticCommands[0].Command, "inline link preserves its application-owned command identity");
        Assert.Equal(UiSemanticActionKind.Hyperlink, document.SemanticCommands[0].Action, "inline activation remains semantic rather than platform-specific");
    }

    private static void ParagraphSharesBaselinesAndDirectionAcrossStyleRuns()
    {
        var rich = new UiRichTextBlock
        {
            Width = 120,
            Height = 60,
            Spans = new UiTextSpan[]
            {
                new("مرحبا", "default", 14, new UiColor(240, 240, 240)),
                new(" بالعالم", "default", 20, new UiColor(80, 200, 255)),
            },
        };
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        var fonts = new UiFontCatalog();
        fonts.Register("default", new FontSourceId(new Guid("B3288FCA-9DC6-4B90-AC40-C1C23366C303")), File.ReadAllBytes(fontPath));
        using var text = new CountingTextService();
        using var document = new UiDocument(rich, text, fonts)
        {
            Localization = new UiLocalizationContext(
                System.Globalization.CultureInfo.GetCultureInfo("ar"),
                TextDirection.Auto),
        };

        document.Layout(new float2(120, 60), 1);
        var display = document.BuildDisplayList();
        Assert.Equal(2, display.Text.Length, "one paragraph preserves separate paint runs at the canonical boundary");
        Assert.True(display.Text[0].BaselineOrigin.x > display.Text[1].BaselineOrigin.x,
            "auto direction is resolved once for the paragraph and places RTL style runs from the right edge");

        rich.Width = 55;
        document.Layout(new float2(55, 80), 1);
        var wrapped = document.BuildDisplayList();
        Assert.True(wrapped.Text[1].BaselineOrigin.y > wrapped.Text[0].BaselineOrigin.y,
            "width changes wrap the next style run onto a shared-baseline line");
    }

    private static UiPointerEvent Pointer(UiPointerEventKind kind, float x, float y) => new(
        kind,
        UiPointerDeviceKind.Mouse,
        1,
        new float2(x, y),
        default,
        default,
        kind is UiPointerEventKind.ButtonDown or UiPointerEventKind.ButtonUp ? UiPointerButton.Primary : default,
        default,
        0,
        default);
}
