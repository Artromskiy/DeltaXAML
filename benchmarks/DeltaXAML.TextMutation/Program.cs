using System.Globalization;
using System.Diagnostics;
using Delta.Text;
using Delta.Text.Contract;
using Library = Delta.XAML;

namespace DeltaXAML.TextMutationBenchmark;

internal static class Program
{
    private const int OperationCount = 1_000;
    private const int ManyTextElementCount = 256;
    private const int ManyOperationCount = 100;
    private const int LargeTextBoxCount = 5_000;
    private const int LargeOperationCount = 20;
    private const int SampleCount = 5;
    private const int WarmupCount = 100;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static void Main(string[] args)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException("The NotoSans-Regular.ttf fixture is required.", fontPath);
        }

        var fontBytes = File.ReadAllBytes(fontPath);
        if (args is ["--5000-textboxes"])
        {
            RunLargeTextBoxProbe(fontBytes);
            return;
        }

        var mutationMicros = new double[SampleCount];
        var mutationBytes = new double[SampleCount];
        var unchangedMicros = new double[SampleCount];
        var unchangedBytes = new double[SampleCount];
        var mutationShapes = new int[SampleCount];
        var cachedMutationMicros = new double[SampleCount];
        var cachedMutationBytes = new double[SampleCount];
        var cachedMutationRequests = new int[SampleCount];
        var cachedMutationShapes = new int[SampleCount];
        var manySetter = MeasureManySetter();
        var manyPipeline = MeasureManyPipeline(fontBytes);

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
        Console.WriteLine(FormattableString.Invariant($"many same-text setters: {manySetter.MicrosecondsPerOperation:F3} us/frame, {manySetter.BytesPerOperation:F1} B/frame, {manySetter.MicrosecondsPerElement:F3} us/element"));
        Console.WriteLine(FormattableString.Invariant($"many same-text pipeline: {manyPipeline.MicrosecondsPerOperation:F3} us/frame, {manyPipeline.BytesPerOperation:F1} B/frame, {manyPipeline.MicrosecondsPerElement:F3} us/element"));
        Console.WriteLine($"samples(us/op): unchanged={FormatSamples(unchangedMicros)}; mutation={FormatSamples(mutationMicros)}");
        Console.WriteLine($"samples(us/op): cached-shaped={FormatSamples(cachedMutationMicros)}");
        Console.WriteLine($"samples(B/op):  unchanged={FormatSamples(unchangedBytes)}; mutation={FormatSamples(mutationBytes)}; cached-shaped={FormatSamples(cachedMutationBytes)}");
    }

    private static void RunLargeTextBoxProbe(byte[] fontBytes)
    {
        CollectBeforeMeasurement();
        var managedBefore = GC.GetTotalMemory(true);
        var processBefore = ReadProcessMemory();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var fonts = new Library.UiFontCatalog();
        fonts.Register(
            "default",
            new FontSourceId(new Guid("5B79D1F4-2E6B-4B8B-9B2A-8A02E22FCEB5")),
            fontBytes);
        using IProbeTextService textService = new ReusingTextService();
        var root = new Library.UiStackPanel();
        var textBoxes = new Library.UiTextBox[LargeTextBoxCount];
        for (var i = 0; i < textBoxes.Length; i++)
        {
            var textBox = new Library.UiTextBox
            {
                Text = "A",
                Width = 240,
                Height = 20,
            };
            textBoxes[i] = textBox;
            root.Add(textBox);
        }

        using var document = new Library.UiDocument(root, textService, fonts);
        var constructionAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        CollectBeforeMeasurement();
        var managedAfterConstruction = GC.GetTotalMemory(false);
        var processAfterConstruction = ReadProcessMemory();
        var viewport = new Delta.float2(240, LargeTextBoxCount * 20);
        document.Layout(viewport, 1);
        _ = document.BuildDisplayList();
        for (var i = 0; i < WarmupCount; i++)
        {
            document.Layout(viewport, 1);
            _ = document.BuildDisplayList();
        }

        CollectBeforeMeasurement();
        var managedAfterWarmFrame = GC.GetTotalMemory(false);
        var processAfterWarmFrame = ReadProcessMemory();
        var setter = MeasureLargeTextBoxSetter(textBoxes);
        var pipeline = MeasureLargeTextBoxPipeline(document, viewport);

        Console.WriteLine("DeltaXAML 5000 TextBox probe (same text: A)");
        Console.WriteLine(FormattableString.Invariant($"construction managed allocation: {constructionAllocated:N0} B"));
        Console.WriteLine(FormattableString.Invariant($"managed heap after construction: {managedAfterConstruction:N0} B (delta {managedAfterConstruction - managedBefore:+#,##0;-#,##0;0} B)"));
        Console.WriteLine(FormattableString.Invariant($"managed heap after warm frame:   {managedAfterWarmFrame:N0} B (delta {managedAfterWarmFrame - managedBefore:+#,##0;-#,##0;0} B)"));
        if (processAfterWarmFrame.PrivateBytes == 0 && processBefore.PrivateBytes == 0)
        {
            Console.WriteLine("process private bytes:             unavailable on this runtime");
        }
        else
        {
            Console.WriteLine(FormattableString.Invariant($"process private bytes:             {processAfterWarmFrame.PrivateBytes:N0} B (delta {processAfterWarmFrame.PrivateBytes - processBefore.PrivateBytes:+#,##0;-#,##0;0} B)"));
        }
        Console.WriteLine(FormattableString.Invariant($"process working set:               {processAfterWarmFrame.WorkingSetBytes:N0} B (delta {processAfterWarmFrame.WorkingSetBytes - processBefore.WorkingSetBytes:+#,##0;-#,##0;0} B)"));
        if (processAfterConstruction.PrivateBytes != 0 || processBefore.PrivateBytes != 0)
        {
            Console.WriteLine(FormattableString.Invariant($"construction process private delta: {processAfterConstruction.PrivateBytes - processBefore.PrivateBytes:+#,##0;-#,##0;0} B"));
        }
        Console.WriteLine(FormattableString.Invariant($"same-text setters: {setter.MicrosecondsPerOperation:F3} us/frame, {setter.BytesPerOperation:F1} B/frame, {setter.MicrosecondsPerElement:F4} us/element"));
        Console.WriteLine(FormattableString.Invariant($"warm layout/display: {pipeline.MicrosecondsPerOperation:F3} us/frame, {pipeline.BytesPerOperation:F1} B/frame, {pipeline.MicrosecondsPerElement:F4} us/element"));
    }

    private static ManyMetric MeasureLargeTextBoxSetter(Library.UiTextBox[] textBoxes)
    {
        for (var i = 0; i < WarmupCount; i++)
        {
            for (var element = 0; element < textBoxes.Length; element++)
            {
                textBoxes[element].Text = "A";
            }
        }

        CollectBeforeMeasurement();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timestamp = Stopwatch.GetTimestamp();
        for (var i = 0; i < LargeOperationCount; i++)
        {
            for (var element = 0; element < textBoxes.Length; element++)
            {
                textBoxes[element].Text = "A";
            }
        }

        return CreateLargeMetric(
            Stopwatch.GetTimestamp() - timestamp,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static ManyMetric MeasureLargeTextBoxPipeline(Library.UiDocument document, Delta.float2 viewport)
    {
        CollectBeforeMeasurement();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timestamp = Stopwatch.GetTimestamp();
        for (var i = 0; i < LargeOperationCount; i++)
        {
            document.Layout(viewport, 1);
            _ = document.BuildDisplayList();
        }

        return CreateLargeMetric(
            Stopwatch.GetTimestamp() - timestamp,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static ManyMetric CreateLargeMetric(long ticks, long allocatedBytes) =>
        new(
            ToMicroseconds(ticks, LargeOperationCount),
            (double)allocatedBytes / LargeOperationCount,
            ToMicroseconds(ticks, LargeOperationCount * LargeTextBoxCount));

    private static ProcessMemory ReadProcessMemory()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(process.PrivateMemorySize64, process.WorkingSet64);
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

    private static ManyMetric MeasureManySetter()
    {
        var texts = CreateManyTextElements();
        for (var i = 0; i < WarmupCount; i++)
        {
            for (var element = 0; element < texts.Length; element++)
            {
                texts[element].Text = "A";
            }
        }

        CollectBeforeMeasurement();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timestamp = Stopwatch.GetTimestamp();
        for (var i = 0; i < ManyOperationCount; i++)
        {
            for (var element = 0; element < texts.Length; element++)
            {
                texts[element].Text = "A";
            }
        }

        return CreateManyMetric(
            Stopwatch.GetTimestamp() - timestamp,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static ManyMetric MeasureManyPipeline(byte[] fontBytes)
    {
        var fonts = new Library.UiFontCatalog();
        fonts.Register(
            "default",
            new FontSourceId(new Guid("5B79D1F4-2E6B-4B8B-9B2A-8A02E22FCEB5")),
            fontBytes);
        using IProbeTextService textService = new CountingTextService();
        var root = new Library.UiStackPanel();
        var texts = CreateManyTextElements();
        for (var i = 0; i < texts.Length; i++)
        {
            root.Add(texts[i]);
        }

        using var document = new Library.UiDocument(root, textService, fonts);
        var viewport = new Delta.float2(240, ManyTextElementCount * 20);
        document.Layout(viewport, 1);
        _ = document.BuildDisplayList();
        for (var i = 0; i < WarmupCount; i++)
        {
            document.Layout(viewport, 1);
            _ = document.BuildDisplayList();
        }

        CollectBeforeMeasurement();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timestamp = Stopwatch.GetTimestamp();
        for (var i = 0; i < ManyOperationCount; i++)
        {
            document.Layout(viewport, 1);
            _ = document.BuildDisplayList();
        }

        return CreateManyMetric(
            Stopwatch.GetTimestamp() - timestamp,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static Library.UiTextBlock[] CreateManyTextElements()
    {
        var texts = new Library.UiTextBlock[ManyTextElementCount];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = new Library.UiTextBlock
            {
                Text = "A",
                Width = 240,
                Height = 20,
            };
        }

        return texts;
    }

    private static PipelineMetric CreateMetric(long ticks, long allocatedBytes, int shapeCount, int shapeRequestCount) =>
        new(ToMicroseconds(ticks), (double)allocatedBytes / OperationCount, shapeCount, shapeRequestCount);

    private static ManyMetric CreateManyMetric(long ticks, long allocatedBytes) =>
        new(
            ToMicroseconds(ticks, ManyOperationCount),
            (double)allocatedBytes / ManyOperationCount,
            ToMicroseconds(ticks, ManyOperationCount * ManyTextElementCount));

    private static double ToMicroseconds(long ticks) => ToMicroseconds(ticks, OperationCount);

    private static double ToMicroseconds(long ticks, int operationCount) =>
        ticks * 1_000_000.0 / Stopwatch.Frequency / operationCount;

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

    private readonly record struct ManyMetric(
        double MicrosecondsPerOperation,
        double BytesPerOperation,
        double MicrosecondsPerElement);

    private readonly record struct ProcessMemory(long PrivateBytes, long WorkingSetBytes);

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
