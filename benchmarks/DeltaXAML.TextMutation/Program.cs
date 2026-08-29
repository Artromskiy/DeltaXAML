using System.Globalization;
using System.Diagnostics;
using Delta.Text;
using Delta.Text.Contract;
using Library = Delta.XAML;

namespace DeltaXAML.TextMutationBenchmark;

internal static class Program
{
    private const int OperationCount = 1_000;
    private const int SampleCount = 5;
    private const int WarmupCount = 100;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static void Main()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException("The NotoSans-Regular.ttf fixture is required.", fontPath);
        }

        var fontBytes = File.ReadAllBytes(fontPath);
        var mutationMicros = new double[SampleCount];
        var mutationBytes = new double[SampleCount];
        var unchangedMicros = new double[SampleCount];
        var unchangedBytes = new double[SampleCount];
        var mutationShapes = new int[SampleCount];
        var cachedMutationMicros = new double[SampleCount];
        var cachedMutationBytes = new double[SampleCount];
        var cachedMutationRequests = new int[SampleCount];
        var cachedMutationShapes = new int[SampleCount];

        for (var sample = 0; sample < SampleCount; sample++)
        {
            var mutation = MeasurePipeline(fontBytes, mutate: true);
            mutationMicros[sample] = mutation.MicrosecondsPerOperation;
            mutationBytes[sample] = mutation.BytesPerOperation;
            mutationShapes[sample] = mutation.ShapeCount;

            var cachedMutation = MeasurePipeline(fontBytes, mutate: true, reuseShapedText: true);
            cachedMutationMicros[sample] = cachedMutation.MicrosecondsPerOperation;
            cachedMutationBytes[sample] = cachedMutation.BytesPerOperation;
            cachedMutationRequests[sample] = cachedMutation.ShapeRequestCount;
            cachedMutationShapes[sample] = cachedMutation.ShapeCount;

            var unchanged = MeasurePipeline(fontBytes, mutate: false);
            unchangedMicros[sample] = unchanged.MicrosecondsPerOperation;
            unchangedBytes[sample] = unchanged.BytesPerOperation;
        }

        var setter = MeasureSetter();
        Array.Sort(mutationMicros);
        Array.Sort(mutationBytes);
        Array.Sort(unchangedMicros);
        Array.Sort(unchangedBytes);
        Array.Sort(cachedMutationMicros);
        Array.Sort(cachedMutationBytes);

        Console.WriteLine("DeltaXAML text mutation metrics (bounded Release probe)");
        Console.WriteLine("Constants A/B are reused; caller-created string allocation is excluded.");
        Console.WriteLine(FormattableString.Invariant($"setter-only:        {setter.MicrosecondsPerOperation:F3} us/op, {setter.BytesPerOperation:F1} B/op"));
        Console.WriteLine(FormattableString.Invariant($"unchanged pipeline:  {Median(unchangedMicros):F3} us/op, {Median(unchangedBytes):F1} B/op"));
        Console.WriteLine(FormattableString.Invariant($"text mutation:       {Median(mutationMicros):F3} us/op, {Median(mutationBytes):F1} B/op, {mutationShapes[SampleCount / 2]} shapes/{OperationCount} ops"));
        Console.WriteLine(FormattableString.Invariant($"cached shaped text:  {Median(cachedMutationMicros):F3} us/op, {Median(cachedMutationBytes):F1} B/op, {cachedMutationShapes[SampleCount / 2]} DeltaText shapes/{OperationCount} ops, {cachedMutationRequests[SampleCount / 2]} requests"));
        Console.WriteLine($"samples(us/op): unchanged={FormatSamples(unchangedMicros)}; mutation={FormatSamples(mutationMicros)}");
        Console.WriteLine($"samples(us/op): cached-shaped={FormatSamples(cachedMutationMicros)}");
        Console.WriteLine($"samples(B/op):  unchanged={FormatSamples(unchangedBytes)}; mutation={FormatSamples(mutationBytes)}; cached-shaped={FormatSamples(cachedMutationBytes)}");
    }

    private static PipelineMetric MeasurePipeline(byte[] fontBytes, bool mutate, bool reuseShapedText = false)
    {
        var fonts = new Library.UiFontCatalog();
        fonts.Register(
            "default",
            new FontSourceId(new Guid("5B79D1F4-2E6B-4B8B-9B2A-8A02E22FCEB5")),
            fontBytes);
        using IProbeTextService textService = reuseShapedText
            ? new ReusingTextService()
            : new CountingTextService();
        var text = new Library.UiTextBlock { Text = "A", Width = 240, Height = 40 };
        using var document = new Library.UiDocument(text, textService, fonts);

        document.Layout(new(240, 40), 1);
        _ = document.BuildDisplayList();
        for (var i = 0; i < WarmupCount; i++)
        {
            if (mutate)
            {
                text.Text = (i & 1) == 0 ? "B" : "A";
            }

            document.Layout(new(240, 40), 1);
            _ = document.BuildDisplayList();
        }

        CollectBeforeMeasurement();
        var shapeStart = textService.ShapeCount;
        var underlyingShapeStart = textService.UnderlyingShapeCount;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timestamp = Stopwatch.GetTimestamp();
        for (var i = 0; i < OperationCount; i++)
        {
            if (mutate)
            {
                text.Text = (i & 1) == 0 ? "B" : "A";
            }

            document.Layout(new(240, 40), 1);
            _ = document.BuildDisplayList();
        }

        return CreateMetric(
            Stopwatch.GetTimestamp() - timestamp,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            textService.UnderlyingShapeCount - underlyingShapeStart,
            textService.ShapeCount - shapeStart);
    }

    private static SetterMetric MeasureSetter()
    {
        var text = new Library.UiTextBlock { Text = "A" };
        for (var i = 0; i < WarmupCount; i++)
        {
            text.Text = (i & 1) == 0 ? "B" : "A";
        }

        CollectBeforeMeasurement();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timestamp = Stopwatch.GetTimestamp();
        for (var i = 0; i < OperationCount; i++)
        {
            text.Text = (i & 1) == 0 ? "B" : "A";
        }

        return new SetterMetric(
            ToMicroseconds(Stopwatch.GetTimestamp() - timestamp),
            (double)(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / OperationCount);
    }

    private static PipelineMetric CreateMetric(long ticks, long allocatedBytes, int shapeCount, int shapeRequestCount) =>
        new(ToMicroseconds(ticks), (double)allocatedBytes / OperationCount, shapeCount, shapeRequestCount);

    private static double ToMicroseconds(long ticks) =>
        ticks * 1_000_000.0 / Stopwatch.Frequency / OperationCount;

    private static void CollectBeforeMeasurement()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static double Median(double[] values) => values[values.Length / 2];

    private static string FormatSamples(double[] values)
    {
        var formatted = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            formatted[i] = values[i].ToString("F3", Invariant);
        }

        return string.Join(",", formatted);
    }

    private readonly record struct PipelineMetric(
        double MicrosecondsPerOperation,
        double BytesPerOperation,
        int ShapeCount,
        int ShapeRequestCount);

    private readonly record struct SetterMetric(
        double MicrosecondsPerOperation,
        double BytesPerOperation);

    private interface IProbeTextService : ITextService
    {
        int ShapeCount { get; }
        int UnderlyingShapeCount { get; }
    }

    private sealed class CountingTextService : IProbeTextService
    {
        private readonly SixLaborsTextService _inner = new();

        public int ShapeCount { get; private set; }

        public int UnderlyingShapeCount => ShapeCount;

        public FontInstanceId OpenFont(in FontOpenRequest request) => _inner.OpenFont(in request);

        public void CloseFont(FontInstanceId font) => _inner.CloseFont(font);

        public FontMetrics GetFontMetrics(FontInstanceId font, float pixelsPerEm) =>
            _inner.GetFontMetrics(font, pixelsPerEm);

        public ShapedText Shape(in TextShapeRequest request)
        {
            ShapeCount++;
            return _inner.Shape(in request);
        }

        public GlyphImage GenerateGlyphImage(in GlyphImageRequest request) =>
            _inner.GenerateGlyphImage(in request);

        public void Dispose() => _inner.Dispose();
    }

    private sealed class ReusingTextService : IProbeTextService
    {
        private readonly SixLaborsTextService _inner = new();
        private ShapedText? _a;
        private ShapedText? _b;

        public int ShapeCount { get; private set; }

        public int UnderlyingShapeCount { get; private set; }

        public FontInstanceId OpenFont(in FontOpenRequest request) => _inner.OpenFont(in request);

        public void CloseFont(FontInstanceId font) => _inner.CloseFont(font);

        public FontMetrics GetFontMetrics(FontInstanceId font, float pixelsPerEm) =>
            _inner.GetFontMetrics(font, pixelsPerEm);

        public ShapedText Shape(in TextShapeRequest request)
        {
            ShapeCount++;
            var text = request.Text.Span;
            if (text.Length == 1 && text[0] == 'A')
            {
                _a ??= ShapeInner(in request);
                return _a;
            }

            if (text.Length == 1 && text[0] == 'B')
            {
                _b ??= ShapeInner(in request);
                return _b;
            }

            throw new InvalidOperationException("The cached text probe only supports the A/B fixture strings.");
        }

        public GlyphImage GenerateGlyphImage(in GlyphImageRequest request) =>
            _inner.GenerateGlyphImage(in request);

        public void Dispose() => _inner.Dispose();

        private ShapedText ShapeInner(in TextShapeRequest request)
        {
            UnderlyingShapeCount++;
            return _inner.Shape(in request);
        }
    }
}
