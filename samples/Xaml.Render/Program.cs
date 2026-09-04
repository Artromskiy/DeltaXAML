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
using DeltaXaml.Samples.Xaml.Render.Generated;
using SDL3;
using TextShaders = Delta.Shader.Text.Shaders;
using UiShaders = Delta.Shader.UI.Shaders;

namespace DeltaXaml.Samples.Xaml.Render;

internal static class Program
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            var headless = HasFlag(args, "--headless");
            var frameLimit = ParseFrameLimit(args, headless ? 1 : int.MaxValue);
            var width = ParsePositiveUInt(args, "--width", 1280);
            var height = ParsePositiveUInt(args, "--height", 860);
            var sample = ParseSample(args);
            var readbackPath = ParsePath(args, "--readback", "/tmp/delta-xaml-render.ppm");

            if (headless)
            {
                return await RunHeadlessAsync(sample, frameLimit, width, height, readbackPath).ConfigureAwait(false);
            }

            var factory = new Sdl3WindowFactory();
            var createResult = factory.CreateWindow(
                new WindowConfiguration("DeltaXAML XAML Render", width, height, true, true));
            if (!createResult.Succeeded || createResult.Window is not { } window)
            {
                await Console.Error.WriteLineAsync("Window creation failed.").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(createResult.Diagnostics.ToText()).ConfigureAwait(false);
                return 1;
            }

            await using var windowLease = window.ConfigureAwait(false);
            return await RunWindowAsync(window, sample, frameLimit).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RunHeadlessAsync(
        string sample,
        int frameLimit,
        uint width,
        uint height,
        string readbackPath)
    {
        var extent = new PixelExtent(width, height);
        using var textService = new SixLaborsTextService();
        var fonts = LoadFonts();
        using var page = CreatePage(sample, textService, fonts);
        var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var rendererScope = renderer.ConfigureAwait(false);
        var session = renderer.CreateHeadlessSession(width, height);
        await using var sessionScope = session.ConfigureAwait(false);
        return await RunFramesAsync(
            window: null,
            session,
            page,
            textService,
            sample,
            extent,
            frameLimit,
            readbackPath).ConfigureAwait(false);
    }

    private static async Task<int> RunWindowAsync(IRenderWindow window, string sample, int frameLimit)
    {
        ArgumentNullException.ThrowIfNull(window);
        var metrics = window.Metrics;
        var extent = metrics.DrawableExtent;
        if (extent.IsEmpty)
        {
            extent = new PixelExtent(metrics.Width, metrics.Height);
        }

        using var textService = new SixLaborsTextService();
        var fonts = LoadFonts();
        using var page = CreatePage(sample, textService, fonts);
        var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var rendererScope = renderer.ConfigureAwait(false);
        var session = renderer.CreateWindowSession(window);
        await using var sessionScope = session.ConfigureAwait(false);
        return await RunFramesAsync(
            window,
            session,
            page,
            textService,
            sample,
            extent,
            frameLimit,
            readbackPath: null).ConfigureAwait(false);
    }

    private static async Task<int> RunFramesAsync(
        IRenderWindow? window,
        IRenderFrameSession session,
        RenderedPage page,
        ITextService textService,
        string sample,
        PixelExtent initialExtent,
        int frameLimit,
        string? readbackPath)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(textService);
        var extent = initialExtent;
        session.ResizeTarget(in extent);

        using var textFeature = new TextRenderFeature(
            session,
            textService,
            LoadTextProgram(),
            extent);
        var roundedProgram = LoadRoundedProgram();
        var solidProgram = LoadSolidProgram();
        var uiFeature = CreateUiFeature(session, roundedProgram, solidProgram, textFeature, extent);
        var clearFeature = new ClearFeature(session.Target, extent, solidProgram);
        var readbackFeature = window is null && readbackPath is not null
            ? new HeadlessReadbackFeature(session.Target, extent.Width, extent.Height)
            : null;
        IRenderFeature[] features = readbackFeature is null
            ? [clearFeature, uiFeature]
            : [clearFeature, uiFeature, readbackFeature];
        var graph = session.CreateRenderGraph();
        var renderedFrames = 0;
        var clipCount = 0;
        var running = true;

        try
        {
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
                    ? extent
                    : GetWindowExtent(metrics, extent);
                if (nextExtent != extent)
                {
                    session.ResizeTarget(in nextExtent);
                    textFeature.Resize(nextExtent);
                    clearFeature.Resize(nextExtent);
                    uiFeature.Dispose();
                    uiFeature = CreateUiFeature(session, roundedProgram, solidProgram, textFeature, nextExtent);
                    features[1] = uiFeature;
                    extent = nextExtent;
                }

                page.Document.Layout(new float2(metrics.Width, metrics.Height), metrics.DpiScale);
                var displayList = page.Document.BuildDisplayList();
                clipCount = displayList.Clips.Length;
                if (!uiFeature.Consume(displayList))
                {
                    await Console.Error.WriteLineAsync(string.Join(Environment.NewLine, uiFeature.Diagnostics)).ConfigureAwait(false);
                    return 1;
                }

                graph.Build((ulong)renderedFrames, features);
                var result = graph.Execute();
                if (result.Status != RenderGraphExecutionStatus.Submitted)
                {
                    await Console.Error.WriteLineAsync($"Graph execution failed: {result.Status}").ConfigureAwait(false);
                    await Console.Error.WriteLineAsync(result.Diagnostics.ToString()).ConfigureAwait(false);
                    return 1;
                }

                renderedFrames++;
            }

            await Console.Out.WriteLineAsync(
                $"DeltaXAML XAML render ({(window is null ? "headless" : "windowed")}, sample={sample}): " +
                $"frames={renderedFrames}, visuals={uiFeature.VisualCount}, " +
                $"clips={clipCount}, text={uiFeature.TextCount}").ConfigureAwait(false);

            if (readbackFeature is not null && readbackPath is not null)
            {
                var pixels = new byte[checked((int)((ulong)extent.Width * extent.Height * 4))];
                if (!readbackFeature.Readback.IsValid ||
                    graph.CopyReadback(readbackFeature.Readback, pixels) != pixels.Length)
                {
                    await Console.Error.WriteLineAsync("Headless Vulkan readback did not complete.").ConfigureAwait(false);
                    return 1;
                }

                SavePpm(pixels, extent.Width, extent.Height, readbackPath);
                await Console.Out.WriteLineAsync(
                    $"readback: path={Path.GetFullPath(readbackPath)}, non-zero-pixels={CountNonZeroPixels(pixels)}")
                    .ConfigureAwait(false);
            }

            return 0;
        }
        finally
        {
            uiFeature.Dispose();
        }
    }

    private static PixelExtent GetWindowExtent(WindowMetrics metrics, PixelExtent fallback)
    {
        var extent = metrics.DrawableExtent;
        return extent.IsEmpty ? fallback : extent;
    }

    private static RenderedPage CreatePage(
        string sample,
        ITextService textService,
        UiFontCatalog fonts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sample);
        ArgumentNullException.ThrowIfNull(textService);
        ArgumentNullException.ThrowIfNull(fonts);

        return sample.Trim().ToUpperInvariant() switch
        {
            "MAIN" or "MAIN-WINDOW" => CreatePage(new MainWindowArtifact(textService, fonts)),
            "YAGE" => CreatePage(new YageArtifact(textService, fonts)),
            "ROUNDED" or "ROUNDED-RECTANGLE" => CreatePage(new RoundedRectangleArtifact(textService, fonts)),
            _ => throw new ArgumentException(
                $"Unknown XAML sample '{sample}'. Use main-window, yage or rounded.",
                nameof(sample)),
        };
    }

    private static RenderedPage CreatePage(MainWindowArtifact artifact) =>
        new(artifact.Document, artifact);

    private static RenderedPage CreatePage(YageArtifact artifact) =>
        new(artifact.Document, artifact);

    private static RenderedPage CreatePage(RoundedRectangleArtifact artifact) =>
        new(artifact.Document, artifact);

    private static UiFontCatalog LoadFonts()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"XAML render sample font was not found: {fontPath}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        return fonts;
    }

    private static UiDisplayListGraphFeature CreateUiFeature(
        IRenderFrameSession session,
        IGraphicsShaderProgram roundedProgram,
        IGraphicsShaderProgram solidProgram,
        TextRenderFeature textFeature,
        PixelExtent extent)
        => new(
            session,
            roundedProgram,
            extent,
            textFeature: textFeature,
            solidVisualProgram: solidProgram,
            roundedSliceVisualProgram: roundedProgram);

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

    private static void SavePpm(ReadOnlySpan<byte> pixels, uint width, uint height, string path)
    {
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

    private static int ParseFrameLimit(string[] args, int fallback)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], "--frames", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], out var value) && value > 0)
            {
                return value;
            }
        }

        return fallback;
    }

    private static uint ParsePositiveUInt(string[] args, string option, uint fallback)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(args[index + 1], out var value) && value > 0)
            {
                return value;
            }
        }

        return fallback;
    }

    private static string ParsePath(string[] args, string option, string fallback)
    {
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

    private static string ParseSample(string[] args)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], "--sample", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(args[index + 1])
                    ? throw new ArgumentException("--sample requires a non-empty name.", nameof(args))
                    : args[index + 1];
            }
        }

        return "main-window";
    }

    private static bool HasFlag(string[] args, string option)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class RenderedPage : IDisposable
    {
        private readonly IDisposable _owner;

        internal RenderedPage(UiDocument? document, IDisposable owner)
        {
            Document = document ?? throw new InvalidOperationException("The generated XAML document has no visual root.");
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        internal UiDocument Document { get; }

        public void Dispose() => _owner.Dispose();
    }

    private sealed class ClearFeature(RenderTargetHandle target, PixelExtent extent, IGraphicsShaderProgram program) : IRenderFeature
    {
        private readonly RenderTargetHandle _target = target;
        private readonly IGraphicsShaderProgram _program = program;
        private ClearPass _pass = new(extent);

        internal void Resize(PixelExtent extent) => _pass = new ClearPass(extent);

        public void AddPasses(IRenderGraphBuilder graph, ulong frameNumber)
        {
            var target = graph.ImportTarget(_target);
            var pass = graph.AddRasterPass(
                new RasterPassDescription(
                    "DeltaXAML.XAML.Clear",
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
