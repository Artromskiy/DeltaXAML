using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Delta.Maths;
using Delta.Render;
using Delta.Render.Platform.SDL3;
using Delta.Render.RenderGraph;
using Delta.Render.Text;
using Delta.Render.Vulkan;
using Delta.Render.XAML;
using Delta.Shader.Contract;
using Delta.Shader.Text;
using Delta.Shader.UI;
using Delta.Text;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Samples.RoundedRectangle.Render.Generated;

namespace DeltaXaml.Samples.RoundedRectangle.Render;

internal static class Program
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var frameLimit = ParseFrameLimit(args);
            var profile = HasFlag(args, "--profile");
            if (HasFlag(args, "--headless"))
            {
                return await RunHeadlessAsync(
                    frameLimit,
                    profile,
                    ParsePath(args, "--xaml", "RoundedRectangle.xaml"),
                    ParsePath(args, "--layout-json", "/tmp/delta-grid-two-rows-layout.json"),
                    ParsePath(args, "--readback", "/tmp/delta-grid-two-rows.ppm")).ConfigureAwait(false);
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
            return await RunAsync(
                window,
                frameLimit,
                profile,
                ParsePath(args, "--xaml", "RoundedRectangle.xaml")).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            return 1;
        }
#pragma warning restore CA1031
    }

    private static async Task<int> RunAsync(
        IRenderWindow window,
        int frameLimit,
        bool profile,
        string xamlPath)
    {
        var fonts = LoadFonts();
        using var textService = new SixLaborsTextService();
        using var loaded = LoadDocument(xamlPath, textService, fonts);
        var document = loaded.Document;
        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateWindowSession(window);

        var program = LoadRoundedProgram();
        var solidProgram = LoadSolidProgram();
        var textProgram = LoadTextProgram();
        using var textFeature = new TextRenderFeature(session, textService, textProgram, new PixelExtent(800, 500));
        var metrics = window.Metrics;
        var extent = metrics.DrawableExtent;
        return await RunFramesAsync(window, session, document, program, solidProgram, textFeature, extent, frameLimit, profile).ConfigureAwait(false);
    }

    private static async Task<int> RunHeadlessAsync(
        int frameLimit,
        bool profile,
        string xamlPath,
        string layoutJsonPath,
        string readbackPath)
    {
        var fonts = LoadFonts();
        using var textService = new SixLaborsTextService();
        using var loaded = LoadDocument(xamlPath, textService, fonts);
        var document = loaded.Document;
        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateHeadlessSession(800, 500);
        var program = LoadRoundedProgram();
        var solidProgram = LoadSolidProgram();
        var textProgram = LoadTextProgram();
        using var textFeature = new TextRenderFeature(session, textService, textProgram, new PixelExtent(800, 500));
        return await RunFramesAsync(
            null,
            session,
            document,
            program,
            solidProgram,
            textFeature,
            new PixelExtent(800, 500),
            frameLimit,
            profile,
            layoutJsonPath,
            readbackPath).ConfigureAwait(false);
    }

    private static async Task<int> RunFramesAsync(
        IRenderWindow? window,
        IRenderFrameSession session,
        UiDocument document,
        IGraphicsShaderProgram program,
        IGraphicsShaderProgram solidProgram,
        TextRenderFeature textFeature,
        PixelExtent initialExtent,
        int frameLimit,
        bool profile,
        string? layoutJsonPath = null,
        string? readbackPath = null)
    {
        var graph = session.CreateRenderGraph();
        var extent = initialExtent;
        session.ResizeTarget(in extent);
        textFeature.Resize(extent);
        using var ui = new UiDisplayListGraphFeature(
            session,
            program,
            extent,
            textFeature: textFeature,
            solidVisualProgram: solidProgram);
        var clear = new ClearFeature(session.Target, extent, program);
        var readback = window is null && readbackPath is not null
            ? new HeadlessReadbackFeature(session.Target, extent.Width, extent.Height)
            : null;
        IRenderFeature[] features = readback is null ? [clear, ui] : [clear, ui, readback];
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

            var nextExtent = window is null
                ? new PixelExtent(metrics.Width, metrics.Height)
                : metrics.DrawableExtent;
            if (nextExtent != extent)
            {
                session.ResizeTarget(in nextExtent);
                textFeature.Resize(nextExtent);
                extent = nextExtent;
            }

            var layoutStart = Stopwatch.GetTimestamp();
            document.Layout(new float2(metrics.Width, metrics.Height), metrics.DpiScale);
            var layoutEnd = Stopwatch.GetTimestamp();
            if (layoutJsonPath is not null && renderedFrames == 0)
            {
                File.WriteAllText(layoutJsonPath, document.BuildLayoutDiagnosticsJson());
            }

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

        if (readback is not null && readbackPath is not null)
        {
            var pixels = new byte[checked((int)((ulong)extent.Width * extent.Height * 4))];
            if (!readback.Readback.IsValid || graph.CopyReadback(readback.Readback, pixels) != pixels.Length)
            {
                await Console.Error.WriteLineAsync("Headless Vulkan readback did not complete.").ConfigureAwait(false);
                return 1;
            }

            SavePpm(pixels, extent.Width, extent.Height, readbackPath);
            await Console.Out.WriteLineAsync(
                $"readback: path={readbackPath}, nonZeroPixels={CountNonZeroPixels(pixels)}, " +
                $"center={FormatCenterPixel(pixels, extent)}").ConfigureAwait(false);
        }

        return 0;
    }

    private static LoadedDocument LoadDocument(
        string xamlPath,
        ITextService textService,
        IUiFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(textService);
        ArgumentNullException.ThrowIfNull(fontResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(xamlPath);
        if (string.Equals(Path.GetFileName(xamlPath), "GridTwoRows.xaml", StringComparison.OrdinalIgnoreCase))
        {
            var artifact = new GridTwoRowsArtifact(textService, fontResolver);
            return new LoadedDocument(artifact.Document, artifact);
        }

        var root = LoadRoot(xamlPath);
        return new LoadedDocument(new UiDocument(root, textService, fontResolver), null);
    }

    private static UiFontCatalog LoadFonts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Rounded rectangle sample font was not found: {path}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", SampleFontId, File.ReadAllBytes(path));
        return fonts;
    }

    private static UiElement LoadRoot(string xamlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xamlPath);
        var sourcePath = Path.IsPathRooted(xamlPath)
            ? xamlPath
            : Path.Combine(AppContext.BaseDirectory, xamlPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Sample XAML was not found: {sourcePath}");
        }

        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyTypeResolver(), new EmptyResourceResolver());
        var result = loader.Load(File.ReadAllText(sourcePath), in context);
        if (!result.Success || result.Root is not { } root)
        {
            var diagnostics = result.Diagnostics.IsEmpty
                ? "unknown XAML load failure"
                : string.Join(Environment.NewLine, result.Diagnostics.ToArray());
            throw new InvalidOperationException($"{xamlPath} failed to load: {diagnostics}");
        }

        return root;
    }

    private sealed class LoadedDocument(UiDocument document, IDisposable? owner) : IDisposable
    {
        internal UiDocument Document { get; } = document;

        public void Dispose()
        {
            if (owner is not null)
            {
                owner.Dispose();
            }
            else
            {
                Document.Dispose();
            }
        }
    }

    private static void SavePpm(byte[] pixels, uint width, uint height, string path)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.Create(path);
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write($"P6\n{width} {height}\n255\n");
        writer.Flush();
        for (var index = 0; index < pixels.Length; index += 4)
        {
            stream.WriteByte(pixels[index]);
            stream.WriteByte(pixels[index + 1]);
            stream.WriteByte(pixels[index + 2]);
        }
    }

    private static int CountNonZeroPixels(ReadOnlySpan<byte> pixels)
    {
        var count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if ((pixels[index] | pixels[index + 1] | pixels[index + 2] | pixels[index + 3]) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static string FormatCenterPixel(ReadOnlySpan<byte> pixels, PixelExtent extent)
    {
        var offset = checked(((int)extent.Height / 2 * (int)extent.Width + (int)extent.Width / 2) * 4);
        return $"({pixels[offset]},{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]})";
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

    private static IGraphicsShaderProgram LoadSolidProgram()
    {
        var vertexPath = Path.Combine(AppContext.BaseDirectory, "shaders", "SolidRectangleVertex.vert.spv");
        var fragmentPath = Path.Combine(AppContext.BaseDirectory, "shaders", "SolidRectangleFragment.frag.spv");
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
        {
            throw new FileNotFoundException($"Solid rectangle shader artifacts were not found: {vertexPath}");
        }

        return SolidRectangleGraphicsShaderProgram.CreateProgram(
            File.ReadAllBytes(vertexPath),
            File.ReadAllBytes(fragmentPath));
    }

    private static IGraphicsShaderProgram LoadTextProgram()
    {
        var vertexPath = Path.Combine(AppContext.BaseDirectory, "shaders", "SdfTextVertex.vert.spv");
        var fragmentPath = Path.Combine(AppContext.BaseDirectory, "shaders", "SdfTextFragment.frag.spv");
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
        {
            throw new FileNotFoundException($"SDF text shader artifacts were not found: {vertexPath}");
        }

        return SdfTextGraphicsShaderProgram.CreateProgram(
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

    private static string ParsePath(string[] args, string option, string fallback)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(option);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(args[index + 1])
                    ? throw new ArgumentException($"{option} requires a non-empty path.", nameof(args))
                    : args[index + 1];
            }
        }

        return fallback;
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

    private sealed class HeadlessReadbackFeature(RenderTargetHandle target, uint width, uint height) : IRenderFeature
    {
        internal RenderGraphReadbackHandle Readback { get; private set; }

        public void AddPasses(IRenderGraphBuilder graph, ulong frameNumber)
        {
            var texture = graph.ImportTarget(target);
            Readback = graph.ReadbackTexture(
                texture,
                new PixelRect(0, 0, checked((int)width), checked((int)height)));
        }
    }
}
