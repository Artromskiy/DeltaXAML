using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Delta.Maths;
using Delta.Render;
using Delta.Render.Platform.SDL3;
using Delta.Render.RenderGraph;
using Delta.Render.Vulkan;
using Delta.Render.XAML;
using Delta.Shader.Contract;
using Delta.Shader.UI;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;

namespace DeltaXaml.Samples.RoundedRectangle.Render;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var frameLimit = ParseFrameLimit(args);
            var profile = HasFlag(args, "--profile");
            if (HasFlag(args, "--headless"))
            {
                return await RunHeadlessAsync(frameLimit, profile).ConfigureAwait(false);
            }

            var factory = new Sdl3WindowFactory();
            var createResult = factory.CreateWindow(
                new WindowConfiguration("DeltaXAML Rounded Rectangle", 800, 500, true, true));
            if (!createResult.Succeeded || createResult.Window is not { } window)
            {
                await Console.Error.WriteLineAsync("Window creation failed.").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(createResult.Diagnostics.ToText()).ConfigureAwait(false);
                return 1;
            }

            await using var windowLease = window.ConfigureAwait(false);
            return await RunAsync(window, frameLimit, profile).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            return 1;
        }
#pragma warning restore CA1031
    }

    private static async Task<int> RunAsync(IRenderWindow window, int frameLimit, bool profile)
    {
        var root = LoadRoot();
        using var textService = new UnsupportedTextService();
        using var document = new UiDocument(root, textService);
        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateWindowSession(window);

        var program = LoadRoundedProgram();
        var metrics = window.Metrics;
        var extent = new PixelExtent(metrics.Width, metrics.Height);
        return await RunFramesAsync(window, session, document, program, extent, frameLimit, profile).ConfigureAwait(false);
    }

    private static async Task<int> RunHeadlessAsync(int frameLimit, bool profile)
    {
        var root = LoadRoot();
        using var textService = new UnsupportedTextService();
        using var document = new UiDocument(root, textService);
        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateHeadlessSession(800, 500);
        var program = LoadRoundedProgram();
        return await RunFramesAsync(
            null,
            session,
            document,
            program,
            new PixelExtent(800, 500),
            frameLimit,
            profile).ConfigureAwait(false);
    }

    private static async Task<int> RunFramesAsync(
        IRenderWindow? window,
        IRenderFrameSession session,
        UiDocument document,
        IGraphicsShaderProgram program,
        PixelExtent initialExtent,
        int frameLimit,
        bool profile)
    {
        var graph = session.CreateRenderGraph();
        var extent = initialExtent;
        session.ResizeTarget(in extent);
        using var ui = new UiDisplayListGraphFeature(session, program, extent);
        var clear = new ClearFeature(session.Target, extent, program);
        IRenderFeature[] features = [clear, ui];
        var renderedFrames = 0;
        var timings = new TimingSummary();
        while ((window is null || !window.IsClosed) && renderedFrames < frameLimit)
        {
            WindowMetrics metrics;
            if (window is { } activeWindow)
            {
                Sdl3WindowFactory.PumpEvents();
                metrics = activeWindow.Metrics;
            }
            else
            {
                metrics = new WindowMetrics(extent.Width, extent.Height, 1);
            }

            var nextExtent = new PixelExtent(metrics.Width, metrics.Height);
            if (nextExtent != extent)
            {
                session.ResizeTarget(in nextExtent);
                extent = nextExtent;
            }

            var layoutStart = Stopwatch.GetTimestamp();
            document.Layout(new float2(metrics.Width, metrics.Height), metrics.DpiScale);
            var layoutEnd = Stopwatch.GetTimestamp();
            var displayList = document.BuildDisplayList();
            var displayListEnd = Stopwatch.GetTimestamp();
            if (!ui.Consume(displayList))
            {
                await Console.Error.WriteLineAsync(string.Join(Environment.NewLine, ui.Diagnostics)).ConfigureAwait(false);
                return 1;
            }
            var consumeEnd = Stopwatch.GetTimestamp();

            graph.Build((ulong)renderedFrames, features);
            var buildEnd = Stopwatch.GetTimestamp();
            var result = graph.Execute();
            var executeEnd = Stopwatch.GetTimestamp();
            if (result.Status != RenderGraphExecutionStatus.Submitted)
            {
                await Console.Error.WriteLineAsync($"Graph execution failed: {result.Status}").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(result.Diagnostics.ToString()).ConfigureAwait(false);
                return 1;
            }

            if (profile && renderedFrames >= TimingSummary.WarmupFrames)
            {
                timings.Add(
                    layoutEnd - layoutStart,
                    displayListEnd - layoutEnd,
                    consumeEnd - displayListEnd,
                    buildEnd - consumeEnd,
                    executeEnd - buildEnd);
            }

            renderedFrames++;
        }

        await Console.Out.WriteLineAsync(
            $"RoundedRectangle renderer sample ({(window is null ? "headless" : "windowed")}): frames={renderedFrames}, " +
            $"visuals={ui.VisualCount}, text={ui.TextCount}").ConfigureAwait(false);
        if (profile)
        {
            await Console.Out.WriteLineAsync(timings.ToReport()).ConfigureAwait(false);
        }

        return 0;
    }

    private static UiBorder LoadRoot()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "RoundedRectangle.xaml");
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Sample XAML was not found: {sourcePath}");
        }

        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyTypeResolver(), new EmptyResourceResolver());
        var result = loader.Load(File.ReadAllText(sourcePath), in context);
        if (!result.Success || result.Root is not UiBorder root)
        {
            var diagnostics = result.Diagnostics.IsEmpty
                ? "unknown XAML load failure"
                : string.Join(Environment.NewLine, result.Diagnostics.ToArray());
            throw new InvalidOperationException($"RoundedRectangle.xaml failed to load: {diagnostics}");
        }

        return root;
    }

    private static IGraphicsShaderProgram LoadRoundedProgram()
    {
        var vertexPath = Path.Combine(AppContext.BaseDirectory, "shaders", "RoundedRectangleVertex.vert.spv");
        var fragmentPath = Path.Combine(AppContext.BaseDirectory, "shaders", "RoundedRectangleFragment.frag.spv");
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
        {
            throw new FileNotFoundException($"Rounded rectangle shader artifacts were not found: {vertexPath}");
        }

        return RoundedRectangleGraphicsShaderProgram.CreateProgram(
            File.ReadAllBytes(vertexPath),
            File.ReadAllBytes(fragmentPath));
    }

    private static int ParseFrameLimit(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], "--frames", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], out var value) && value > 0)
            {
                return value;
            }
        }

        return int.MaxValue;
    }

    private static bool HasFlag(string[] args, string value)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

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

    private sealed class UnsupportedTextService : ITextService
    {
        public FontInstanceId OpenFont(in FontOpenRequest request) => throw NotSupported();

        public void CloseFont(FontInstanceId font) => throw NotSupported();

        public FontMetrics GetFontMetrics(FontInstanceId font, float pixelsPerEm) => throw NotSupported();

        public ShapedText Shape(in TextShapeRequest request) => throw NotSupported();

        public GlyphImage GenerateGlyphImage(in GlyphImageRequest request) => throw NotSupported();

        public void Dispose()
        {
        }

        private static NotSupportedException NotSupported() =>
            new("This sample contains no text; a text service is not expected to be called.");
    }

    private sealed class ClearFeature(RenderTargetHandle target, PixelExtent extent, IGraphicsShaderProgram program) : IRenderFeature
    {
        private readonly RenderTargetHandle _target = target;
        private readonly PixelExtent _extent = extent;
        private readonly IGraphicsShaderProgram _program = program;
        private readonly ClearPass _pass = new(extent);

        public void AddPasses(IRenderGraphBuilder graph, ulong frameNumber)
        {
            var target = graph.ImportTarget(_target);
            var pass = graph.AddRasterPass(
                new RasterPassDescription(
                    "RoundedRectangle.Clear",
                    new RasterPipelineDescription(_program, cullMode: RasterCullMode.None)),
                _pass);
            graph.UseColorAttachment(
                pass,
                0,
                new ColorAttachmentDescription(
                    target,
                    AttachmentLoadOperation.Clear,
                    AttachmentStoreOperation.Store,
                    new ClearColor(0.035f, 0.045f, 0.075f, 1f)));
        }
    }

    private sealed class ClearPass(PixelExtent extent) : IRasterPass
    {
        private readonly RenderViewport _viewport = new(0, 0, extent.Width, extent.Height);
        private readonly PixelRect _scissor = new(0, 0, checked((int)extent.Width), checked((int)extent.Height));

        public void Record(IRasterCommandContext commands)
        {
            commands.SetViewport(in _viewport);
            commands.SetScissor(in _scissor);
        }
    }

    private sealed class TimingSummary
    {
        internal const int WarmupFrames = 30;

        private TimingAccumulator _layout;
        private TimingAccumulator _displayList;
        private TimingAccumulator _consume;
        private TimingAccumulator _build;
        private TimingAccumulator _execute;
        private TimingAccumulator _uiTotal;
        private TimingAccumulator _rendererTotal;

        internal void Add(long layout, long displayList, long consume, long build, long execute)
        {
            _layout.Add(layout);
            _displayList.Add(displayList);
            _consume.Add(consume);
            _build.Add(build);
            _execute.Add(execute);
            _uiTotal.Add(checked(layout + displayList));
            _rendererTotal.Add(checked(consume + build + execute));
        }

        internal string ToReport()
        {
            var count = _layout.Count;
            if (count == 0)
            {
                return $"profile: fewer than {WarmupFrames + 1} frames; no post-warmup samples";
            }

            return string.Join(
                Environment.NewLine,
                $"profile: samples={count}, warmup={WarmupFrames}",
                "stage                                      avg us       min us       max us",
                Format("UiDocument.Layout", _layout),
                Format("UiDocument.BuildDisplayList", _displayList),
                Format("UiDisplayListGraphFeature.Consume", _consume),
                Format("IRenderGraph.Build", _build),
                Format("IRenderGraph.Execute*", _execute),
                Format("CPU UI total", _uiTotal),
                Format("CPU renderer total*", _rendererTotal),
                "* Execute includes CPU command recording/submission and the wait for the previous-frame fence; it is not a GPU timestamp.");
        }

        private static string Format(string name, TimingAccumulator value) =>
            $"{name,-42} {value.AverageMicroseconds,10:F3} {value.MinimumMicroseconds,12:F3} {value.MaximumMicroseconds,12:F3}";
    }

    private struct TimingAccumulator
    {
        private long _totalTicks;
        private long _minimumTicks;
        private long _maximumTicks;

        internal int Count { get; private set; }

        internal double AverageMicroseconds => ToMicroseconds(_totalTicks / (double)Count);

        internal double MinimumMicroseconds => ToMicroseconds(_minimumTicks);

        internal double MaximumMicroseconds => ToMicroseconds(_maximumTicks);

        internal void Add(long ticks)
        {
            _totalTicks = checked(_totalTicks + ticks);
            if (Count == 0)
            {
                _minimumTicks = ticks;
                _maximumTicks = ticks;
            }
            else
            {
                _minimumTicks = Math.Min(_minimumTicks, ticks);
                _maximumTicks = Math.Max(_maximumTicks, ticks);
            }

            Count++;
        }

        private static double ToMicroseconds(double ticks) => ticks * 1_000_000d / Stopwatch.Frequency;
    }
}
