using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Delta;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;

namespace DeltaXaml.Samples.RoundedRectangle.Headless;

internal static class Program
{
    private const int RectangleCount = 100;
    private const int WarmupFrames = 64;
    private const int DefaultIterations = 1_000;
    private const float ViewportWidth = 800;
    private const float ViewportHeight = 400;

    private static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        try
        {
            var iterations = ParseIterations(args);
            var fixture = LoadFixture();
            using var textService = new EmptyTextService();
            using var document = new UiDocument(fixture.Root, textService);

            var initial = RenderFrame(document, ViewportWidth, ViewportHeight, 1);
            if (initial.Visuals.Length != RectangleCount)
            {
                throw new InvalidOperationException(
                    $"Expected {RectangleCount} rounded rectangles, got {initial.Visuals.Length} visual commands.");
            }

            for (var index = 0; index < initial.Visuals.Length; index++)
            {
                var kind = initial.Visuals[index].Kind;
                if (kind is not (UiVisualKind.RoundedRectangle or UiVisualKind.Border))
                {
                    throw new InvalidOperationException(
                        $"Visual {index} is {kind}; expected a rounded rectangle or rounded border command.");
                }
            }

            Console.WriteLine("DeltaXAML rounded rectangle headless sample");
            Console.WriteLine($"retained rectangles: {initial.Visuals.Length}");
            Console.WriteLine($"iterations: {iterations}, warmup: {WarmupFrames} frames/scenario");
            Console.WriteLine("scenario                         us/frame       managed B/frame");

            PrintMeasurement("unchanged Layout + display", Measure(
                Scenario.Unchanged,
                document,
                fixture,
                iterations));
            PrintMeasurement("hierarchy rotate one child", Measure(
                Scenario.Hierarchy,
                document,
                fixture,
                iterations));
            PrintMeasurement("one rectangle size change", Measure(
                Scenario.Size,
                document,
                fixture,
                iterations));
            PrintMeasurement("one rectangle paint change", Measure(
                Scenario.Paint,
                document,
                fixture,
                iterations));
            PrintMeasurement("alternating DPI scale", Measure(
                Scenario.Dpi,
                document,
                fixture,
                iterations));

            var unchanged = Measure(Scenario.Unchanged, document, fixture, iterations);
            if (unchanged.AllocatedBytes != 0)
            {
                throw new InvalidOperationException(
                    $"Unchanged warm frame allocated {unchanged.AllocatedBytes} bytes; expected zero.");
            }

            Console.WriteLine("acceptance: 100 visuals and zero unchanged-frame allocations");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static UiDisplayList RenderFrame(UiDocument document, float width, float height, float dpiScale)
    {
        document.Layout(new float2(width, height), dpiScale);
        var displayList = document.BuildDisplayList();
        if (displayList.Visuals.Length != RectangleCount)
        {
            throw new InvalidOperationException(
                $"Frame produced {displayList.Visuals.Length} visual commands instead of {RectangleCount}.");
        }

        return displayList;
    }

    private static Measurement Measure(
        Scenario scenario,
        UiDocument document,
        Fixture fixture,
        int iterations)
    {
        for (var frame = 0; frame < WarmupFrames; frame++)
        {
            RunScenarioFrame(scenario, frame, document, fixture);
        }

        CollectBeforeMeasurement();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var frame = 0; frame < iterations; frame++)
        {
            RunScenarioFrame(scenario, frame, document, fixture);
        }

        var elapsedTicks = Stopwatch.GetTimestamp() - start;
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var microsecondsPerFrame = elapsedTicks * 1_000_000d / Stopwatch.Frequency / iterations;
        return new Measurement(microsecondsPerFrame, (double)allocatedBytes / iterations, allocatedBytes);
    }

    private static void RunScenarioFrame(
        Scenario scenario,
        int frame,
        UiDocument document,
        Fixture fixture)
    {
        var rectangle = fixture.Rectangles[frame % fixture.Rectangles.Length];
        var row = fixture.Rows[frame % fixture.Rows.Length];
        switch (scenario)
        {
            case Scenario.Unchanged:
                RenderFrame(document, ViewportWidth, ViewportHeight, 1);
                return;
            case Scenario.Hierarchy:
                var child = row.Children[0];
                row.Remove(child);
                row.Add(child);
                RenderFrame(document, ViewportWidth, ViewportHeight, 1);
                return;
            case Scenario.Size:
                rectangle.Width = frame % 2 == 0 ? 78 : 80;
                RenderFrame(document, ViewportWidth, ViewportHeight, 1);
                return;
            case Scenario.Paint:
                rectangle.CornerRadius = frame % 2 == 0
                    ? new UiCornerRadii(4, 8, 12, 16)
                    : new UiCornerRadii(16, 12, 8, 4);
                RenderFrame(document, ViewportWidth, ViewportHeight, 1);
                return;
            case Scenario.Dpi:
                RenderFrame(document, ViewportWidth, ViewportHeight, frame % 2 == 0 ? 1 : 2);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown probe scenario.");
        }
    }

    private static Fixture LoadFixture()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "RoundedRectangles.dxaml");
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Sample XAML was not found: {sourcePath}");
        }

        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyTypeResolver(), new EmptyResourceResolver());
        var result = loader.Load(File.ReadAllText(sourcePath), in context);
        if (!result.Success || result.Root is not UiStackPanel root)
        {
            var diagnostics = result.Diagnostics.Length == 0
                ? "unknown XAML load failure"
                : string.Join(Environment.NewLine, result.Diagnostics.ToArray());
            throw new InvalidOperationException($"RoundedRectangles.dxaml failed to load: {diagnostics}");
        }

        var rows = new UiStackPanel[root.Children.Count];
        var rectangles = new UiBorder[RectangleCount];
        var rectangleIndex = 0;
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            if (root.Children[rowIndex] is not UiStackPanel row)
            {
                throw new InvalidOperationException($"XAML row {rowIndex} is not a StackPanel.");
            }

            rows[rowIndex] = row;
            for (var childIndex = 0; childIndex < row.Children.Count; childIndex++)
            {
                if (row.Children[childIndex] is not UiBorder rectangle || rectangleIndex == rectangles.Length)
                {
                    throw new InvalidOperationException("XAML fixture does not contain exactly 100 Border elements.");
                }

                rectangles[rectangleIndex++] = rectangle;
            }
        }

        if (rectangleIndex != RectangleCount || rows.Length != 10)
        {
            throw new InvalidOperationException(
                $"Expected 10 rows and {RectangleCount} rectangles, got {rows.Length} rows and {rectangleIndex} rectangles.");
        }

        return new Fixture(root, rows, rectangles);
    }

    private static int ParseIterations(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--iterations", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var value) || value <= 0)
            {
                throw new ArgumentException("--iterations requires a positive integer.", nameof(args));
            }

            return value;
        }

        return DefaultIterations;
    }

    private static void PrintMeasurement(string name, Measurement measurement)
    {
        Console.WriteLine($"{name,-32} {measurement.MicrosecondsPerFrame,10:F3} {measurement.BytesPerFrame,18:F1}");
    }

    private static void CollectBeforeMeasurement()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private enum Scenario
    {
        Unchanged,
        Hierarchy,
        Size,
        Paint,
        Dpi,
    }

    private readonly record struct Measurement(
        double MicrosecondsPerFrame,
        double BytesPerFrame,
        long AllocatedBytes);

    private sealed record Fixture(
        UiStackPanel Root,
        UiStackPanel[] Rows,
        UiBorder[] Rectangles);

    private sealed class EmptyTypeResolver : IXamlTypeResolver
    {
        public bool TryResolveName(in XamlQualifiedName name, out UiTypeId type)
        {
            type = default;
            return false;
        }

        public bool TryCreate(UiTypeId type, [NotNullWhen(true)] out UiElement? element)
        {
            element = null;
            return false;
        }
    }

    private sealed class EmptyResourceResolver : IUiResourceResolver
    {
        public bool TryResolve(UiResourceId resource, out object? value)
        {
            value = null;
            return false;
        }
    }

    private sealed class EmptyTextService : ITextService
    {
        public FontInstanceId OpenFont(in FontOpenRequest request) => throw NotSupported();

        public void CloseFont(FontInstanceId font)
        {
        }

        public FontMetrics GetFontMetrics(FontInstanceId font, float pixelsPerEm) => throw NotSupported();

        public ShapedText Shape(in TextShapeRequest request) => throw NotSupported();

        public GlyphImage GenerateGlyphImage(in GlyphImageRequest request) => throw NotSupported();

        public void Dispose()
        {
        }

        private static NotSupportedException NotSupported() =>
            new("This sample contains no text; a text service is not expected to be called.");
    }
}
