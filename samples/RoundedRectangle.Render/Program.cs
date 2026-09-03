using System.Diagnostics;
using Delta;
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
using SDL3;
using TextShaders = Delta.Shader.Text.Shaders;
using UiShaders = Delta.Shader.UI.Shaders;

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
            var watch = HasFlag(args, "--watch");
            var profile = HasFlag(args, "--profile");
            var assertNoBlackPixels = HasFlag(args, "--assert-no-black-pixels");
            var xamlPath = ParsePath(args, "--xaml", "RoundedRectangle.xaml");
            if (HasFlag(args, "--headless"))
            {
                return await RunHeadlessAsync(
                    frameLimit,
                    profile,
                    assertNoBlackPixels,
                    xamlPath,
                    watch,
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
                xamlPath,
                watch).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RunAsync(
        IRenderWindow window,
        int frameLimit,
        bool profile,
        string xamlPath,
        bool watch)
    {
        var fonts = LoadFonts();
        using var textService = new SixLaborsTextService();
        var sourcePath = ResolveSourcePath(xamlPath);
        using var documentState = new LoadedDocumentState(
            LoadDocument(sourcePath, textService, fonts),
            GetPathStamp(sourcePath));
        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateWindowSession(window);

        var program = LoadRoundedProgram();
        var solidProgram = LoadSolidProgram();
        var textProgram = LoadTextProgram();
        using var textFeature = new TextRenderFeature(session, textService, textProgram, new PixelExtent(800, 500));
        var metrics = window.Metrics;
        var extent = metrics.DrawableExtent;
        return await RunFramesAsync(
            window,
            session,
            documentState,
            program,
            solidProgram,
            textFeature,
            extent,
            frameLimit,
            profile,
            assertNoBlackPixels: false,
            watch: watch,
            xamlSourcePath: sourcePath,
            textService: textService,
            fontResolver: fonts).ConfigureAwait(false);
    }

    private static async Task<int> RunHeadlessAsync(
        int frameLimit,
        bool profile,
        bool assertNoBlackPixels,
        string xamlPath,
        bool watch,
        string layoutJsonPath,
        string readbackPath)
    {
        var fonts = LoadFonts();
        using var textService = new SixLaborsTextService();
        var sourcePath = ResolveSourcePath(xamlPath);
        using var documentState = new LoadedDocumentState(
            LoadDocument(sourcePath, textService, fonts),
            GetPathStamp(sourcePath));
        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateHeadlessSession(800, 500);
        var program = LoadRoundedProgram();
        var solidProgram = LoadSolidProgram();
        var textProgram = LoadTextProgram();
        using var textFeature = new TextRenderFeature(session, textService, textProgram, new PixelExtent(800, 500));
        return await RunFramesAsync(
            null,
            session,
            documentState,
            program,
            solidProgram,
            textFeature,
            new PixelExtent(800, 500),
            frameLimit,
            profile,
            assertNoBlackPixels: assertNoBlackPixels,
            watch: watch,
            xamlSourcePath: sourcePath,
            textService: textService,
            fontResolver: fonts,
            layoutJsonPath: layoutJsonPath,
            readbackPath: readbackPath).ConfigureAwait(false);
    }

    private static async Task<int> RunFramesAsync(
        IRenderWindow? window,
        IRenderFrameSession session,
        LoadedDocumentState documentState,
        IGraphicsShaderProgram program,
        IGraphicsShaderProgram solidProgram,
        TextRenderFeature textFeature,
        PixelExtent initialExtent,
        int frameLimit,
        bool profile,
        ITextService textService,
        IUiFontResolver fontResolver,
        bool assertNoBlackPixels = false,
        bool watch = false,
        string? xamlSourcePath = null,
        string? layoutJsonPath = null,
        string? readbackPath = null)
    {
        ArgumentNullException.ThrowIfNull(documentState);
        ArgumentNullException.ThrowIfNull(textService);
        ArgumentNullException.ThrowIfNull(fontResolver);
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
        var running = true;
        var sourcePath = watch ? xamlSourcePath : null;
        if (!watch)
        {
            sourcePath = null;
        }

        while (running && (window is null || !window.IsClosed) && renderedFrames < frameLimit)
        {
            WindowMetrics metrics;
            if (window is { } activeWindow)
            {
                running = PumpWindowEvents();
                if (!running)
                {
                    break;
                }

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

            if (watch && sourcePath is not null)
            {
                if (TryReloadDocument(
                    sourcePath,
                    documentState,
                    textService: textService,
                    fontResolver: fontResolver))
                {
                    Console.WriteLine($"[watch] Reloaded {sourcePath}");
                }
            }

            var layoutStart = Stopwatch.GetTimestamp();
            documentState.Document.Layout(new float2(metrics.Width, metrics.Height), metrics.DpiScale);
            var layoutEnd = Stopwatch.GetTimestamp();
            if (layoutJsonPath is not null && renderedFrames == 0)
            {
                File.WriteAllText(layoutJsonPath, documentState.Document.BuildLayoutDiagnosticsJson());
            }

            var displayList = documentState.Document.BuildDisplayList();
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
            var visibleBlackPixels = CountVisibleBlackPixels(pixels);
            await Console.Out.WriteLineAsync(
                $"readback pixels: black={visibleBlackPixels}/{extent.Width * extent.Height}").ConfigureAwait(false);
            if (assertNoBlackPixels && visibleBlackPixels != 0)
            {
                await Console.Error.WriteLineAsync($"black pixel regression: visible black pixels={visibleBlackPixels}").ConfigureAwait(false);
                return 1;
            }

            await Console.Out.WriteLineAsync(
                $"readback: path={readbackPath}, nonZeroPixels={CountNonZeroPixels(pixels)}, " +
                $"center={FormatCenterPixel(pixels, extent)}").ConfigureAwait(false);
        }

        return 0;
    }

    private static bool PumpWindowEvents()
    {
        Sdl3WindowFactory.PumpEvents();
        var running = true;
        while (SDL.PollEvent(out var @event))
        {
            var type = (SDL.EventType)@event.Type;
            if (type is SDL.EventType.Quit or SDL.EventType.WindowCloseRequested)
            {
                running = false;
            }
        }

        return running;
    }

    private static LoadedDocument LoadDocument(
        string xamlPath,
        ITextService textService,
        IUiFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(textService);
        ArgumentNullException.ThrowIfNull(fontResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(xamlPath);
        var root = LoadRoot(xamlPath);
        return new LoadedDocument(new UiDocument(root, textService, fontResolver), null);
    }

    private static bool TryReloadDocument(
        string xamlPath,
        LoadedDocumentState documentState,
        ITextService textService,
        IUiFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(xamlPath);
        ArgumentNullException.ThrowIfNull(documentState);
        ArgumentNullException.ThrowIfNull(textService);
        ArgumentNullException.ThrowIfNull(fontResolver);
        var nextStamp = GetPathStamp(xamlPath);
        if (nextStamp == -1L || nextStamp == documentState.SourceStamp)
        {
            return false;
        }

        try
        {
            var reloaded = LoadDocument(xamlPath, textService, fontResolver);
            documentState.Replace(reloaded, nextStamp);
            return true;
        }
        catch (Exception exception)
        {
            documentState.SourceStamp = nextStamp;
            Console.Error.WriteLine($"[watch] Failed to reload {xamlPath}: {exception.Message}");
            return false;
        }
    }

    private static long GetPathStamp(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
        {
            return -1L;
        }

        return unchecked(
            (File.GetLastWriteTimeUtc(sourcePath).Ticks * 397L) ^ new FileInfo(sourcePath).Length);
    }

    private static string ResolveSourcePath(string xamlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xamlPath);
        if (Path.IsPathRooted(xamlPath))
        {
            if (!File.Exists(xamlPath))
            {
                throw new FileNotFoundException($"Sample XAML was not found: {xamlPath}");
            }

            return xamlPath;
        }

        var cwdPath = Path.Combine(Environment.CurrentDirectory, xamlPath);
        if (File.Exists(cwdPath))
        {
            return cwdPath;
        }

        if (string.IsNullOrEmpty(Path.GetDirectoryName(xamlPath)))
        {
            var projectPath = Path.Combine(
                Environment.CurrentDirectory,
                "samples",
                "RoundedRectangle.Render",
                xamlPath);
            if (File.Exists(projectPath))
            {
                return projectPath;
            }
        }

        var basePath = Path.Combine(AppContext.BaseDirectory, xamlPath);
        if (File.Exists(basePath))
        {
            return basePath;
        }

        throw new FileNotFoundException($"Sample XAML was not found: {xamlPath}");
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
        var loader = new XamlLoader();
        var context = new XamlLoadContext(new EmptyTypeResolver(), new EmptyResourceResolver());
        var result = loader.Load(File.ReadAllText(xamlPath), in context);
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

    private sealed class LoadedDocumentState(LoadedDocument loaded, long sourceStamp) : IDisposable
    {
        internal LoadedDocument Loaded { get; private set; } = loaded;

        internal UiDocument Document => Loaded.Document;

        internal long SourceStamp { get; set; } = sourceStamp;

        internal void Replace(LoadedDocument replacement, long sourceStamp)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            var previous = Loaded;
            Loaded = replacement;
            SourceStamp = sourceStamp;
            previous.Dispose();
        }

        public void Dispose() => Loaded.Dispose();
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

    private static int CountVisibleBlackPixels(ReadOnlySpan<byte> pixels)
    {
        var count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] == 0 && pixels[index + 1] == 0 && pixels[index + 2] == 0 && pixels[index + 3] != 0)
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

    private static GraphicsShaderProgram LoadRoundedProgram()
        => LoadProgram(
            UiShaders.Spv.UiRectangleShaders.RoundedRectangle.Vertex(),
            UiShaders.Spv.UiRectangleShaders.RoundedRectangle.Fragment(),
            UiShaders.Abi.UiRectangleShaders.RoundedRectangle.Vertex(),
            UiShaders.Abi.UiRectangleShaders.RoundedRectangle.Fragment());

    private static GraphicsShaderProgram LoadSolidProgram()
        => LoadProgram(
            UiShaders.Spv.UiRectangleShaders.SolidRectangle.Vertex(),
            UiShaders.Spv.UiRectangleShaders.SolidRectangle.Fragment(),
            UiShaders.Abi.UiRectangleShaders.SolidRectangle.Vertex(),
            UiShaders.Abi.UiRectangleShaders.SolidRectangle.Fragment());

    private static GraphicsShaderProgram LoadTextProgram()
        => LoadProgram(
            TextShaders.Spv.TextShaders.SdfText.Vertex(),
            TextShaders.Spv.TextShaders.SdfText.Fragment(),
            TextShaders.Abi.TextShaders.SdfText.Vertex(),
            TextShaders.Abi.TextShaders.SdfText.Fragment());

    private static GraphicsShaderProgram LoadProgram(
        ReadOnlySpan<byte> vertexSpirv,
        ReadOnlySpan<byte> fragmentSpirv,
        ShaderAbi vertexAbi,
        ShaderAbi fragmentAbi)
    {
        ArgumentNullException.ThrowIfNull(vertexAbi);
        ArgumentNullException.ThrowIfNull(fragmentAbi);
        return new GraphicsShaderProgram(
            new ShaderArtifact(vertexSpirv, "main", vertexAbi),
            new ShaderArtifact(fragmentSpirv, "main", fragmentAbi));
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
                    new ClearColor(0f, 0f, 0f, 1f)));
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
                if (ticks < _minimumTicks)
                {
                    _minimumTicks = ticks;
                }

                if (ticks > _maximumTicks)
                {
                    _maximumTicks = ticks;
                }
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
