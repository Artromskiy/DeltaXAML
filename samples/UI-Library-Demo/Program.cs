using System.Globalization;
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
using DeltaXaml.Samples.UiLibraryDemo.Generated;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal static class Program
{
    private const uint DefaultWidth = 1280;
    private const uint DefaultHeight = 860;
    private static readonly FontSourceId _sampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static async Task<int> Main(string[] args)
    {
        var options = SampleOptions.Parse(args);
        var fonts = LoadFonts();
        using var textService = new DeltaTextService();
        using var content = new UiLibraryDemoContent(textService, fonts);
        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());

        return options.Headless
            ? await RunHeadless(renderer, textService, content, options).ConfigureAwait(false)
            : await RunWindowed(renderer, textService, content, options).ConfigureAwait(false);
    }

    private static async Task<int> RunHeadless(
        VulkanRenderer renderer,
        ITextService textService,
        UiLibraryDemoContent content,
        SampleOptions options)
    {
        var extent = new PixelExtent(ScaleToPixels(options.Width, options.Dpi), ScaleToPixels(options.Height, options.Dpi));
        await using var session = renderer.CreateHeadlessSession(
            extent.Width,
            extent.Height,
            new RenderSessionOptions());
        return await RunFrames(session, null, textService, content, options, extent).ConfigureAwait(false);
    }

    private static async Task<int> RunWindowed(
        VulkanRenderer renderer,
        ITextService textService,
        UiLibraryDemoContent content,
        SampleOptions options)
    {
        var result = new Sdl3WindowFactory().CreateWindow(
            new WindowConfiguration("DeltaXAML UI Library Demo", options.Width, options.Height, true, true));
        if (!result.Succeeded || result.Window is not { } window)
        {
            await Console.Error.WriteLineAsync("Window creation failed.").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(result.Diagnostics.ToText()).ConfigureAwait(false);
            return 1;
        }

        await using var windowLease = window.ConfigureAwait(false);
        await using var session = renderer.CreateWindowSession(
            window,
            new RenderSessionOptions());
        var metrics = window.Metrics;
        var extent = metrics.DrawableExtent.IsEmpty ? new PixelExtent(options.Width, options.Height) : metrics.DrawableExtent;
        return await RunFrames(session, window, textService, content, options, extent).ConfigureAwait(false);
    }

    private static async Task<int> RunFrames(
        IRenderFrameSession session,
        IRenderWindow? window,
        ITextService textService,
        UiLibraryDemoContent content,
        SampleOptions options,
        PixelExtent initialExtent)
    {
        var extent = initialExtent;
        session.ResizeTarget(in extent);
        await using var pipeline = new ManualUiPipeline(session, textService, extent, window is null && options.ReadbackPath is not null);
        var frameLimit = options.Frames == 0 ? int.MaxValue : options.Frames;
        var renderedFrames = 0;
        var running = true;

        while (running && (window is null || !window.IsClosed) && renderedFrames < frameLimit)
        {
            if (window is not null && !PumpWindowEvents(window, content.Document, ref running))
            {
                break;
            }

            var metrics = window?.Metrics ?? new WindowMetrics(options.Width, options.Height, options.Dpi)
            {
                DrawableWidth = extent.Width,
                DrawableHeight = extent.Height,
            };
            var nextExtent = metrics.DrawableExtent.IsEmpty ? extent : metrics.DrawableExtent;
            if (nextExtent != extent)
            {
                session.ResizeTarget(in nextExtent);
                pipeline.Resize(nextExtent);
                extent = nextExtent;
            }

            content.AdvanceFrame();
            content.Document.Layout(new float2(metrics.Width, metrics.Height), metrics.DpiScale);
            var displayList = content.Document.BuildDisplayList();
            if (!pipeline.Render(displayList, (ulong)renderedFrames, out var diagnostics))
            {
                await Console.Error.WriteLineAsync(diagnostics).ConfigureAwait(false);
                return 1;
            }

            renderedFrames++;
        }

        if (options.LayoutPath is not null)
        {
            WriteText(options.LayoutPath, content.Document.BuildLayoutDiagnosticsJson());
        }

        if (options.ReadbackPath is not null && renderedFrames != 0)
        {
            var pixels = new byte[checked((int)((ulong)extent.Width * extent.Height * 4))];
            if (!pipeline.TryCopyReadback(pixels))
            {
                await Console.Error.WriteLineAsync("Headless Vulkan readback did not complete.").ConfigureAwait(false);
                return 1;
            }

            SavePpm(pixels, extent.Width, extent.Height, options.ReadbackPath);
            await Console.Out.WriteLineAsync($"readback: path={Path.GetFullPath(options.ReadbackPath)}").ConfigureAwait(false);
        }

        await Console.Out.WriteLineAsync(
            $"DeltaXAML manual renderer ({(window is null ? "headless" : "windowed")}): " +
            $"frames={renderedFrames}, visuals={pipeline.VisualCount}, " +
            $"clips={pipeline.ClipCount}, text={pipeline.TextCount}").ConfigureAwait(false);
        return 0;
    }

    private static bool PumpWindowEvents(IRenderWindow window, UiDocument document, ref bool running)
    {
        Sdl3WindowFactory.PumpEvents();
        while (SDL.PollEvent(out var @event))
        {
            var type = (SDL.EventType)@event.Type;
            if (type is SDL.EventType.Quit or SDL.EventType.WindowCloseRequested)
            {
                running = false;
                continue;
            }

            if (type is SDL.EventType.KeyDown or SDL.EventType.KeyUp)
            {
                var key = new UiKeyEvent(
                    type == SDL.EventType.KeyDown ? UiKeyEventKind.Down : UiKeyEventKind.Up,
                    new UiPhysicalKey((uint)@event.Key.Scancode),
                    new UiLogicalKey((uint)@event.Key.Scancode),
                    default,
                    @event.Key.Repeat);
                var input = UiInputEvent.FromKey(in key);
                document.Dispatch(in input);
            }
        }

        return !window.IsClosed;
    }

    private static UiFontCatalog LoadFonts()
    {
        string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"UI library demo font was not found: {fontPath}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", _sampleFontId, File.ReadAllBytes(fontPath));
        return fonts;
    }

    private static GraphicsShaderProgram CreateProgram(
        ReadOnlySpan<byte> vertexSpirv,
        ReadOnlySpan<byte> fragmentSpirv,
        ShaderAbi vertexAbi,
        ShaderAbi fragmentAbi) =>
        new(
            new ShaderArtifact(vertexSpirv, "main", vertexAbi),
            new ShaderArtifact(fragmentSpirv, "main", fragmentAbi));

    private static GraphicsShaderProgram CreateSolidProgram() => CreateProgram(
        UiShaders.Spv.UiRectangleShaders.SolidRectangle.Vertex(),
        UiShaders.Spv.UiRectangleShaders.SolidRectangle.Fragment(),
        UiShaders.Abi.UiRectangleShaders.SolidRectangle.Vertex(),
        UiShaders.Abi.UiRectangleShaders.SolidRectangle.Fragment());

    private static GraphicsShaderProgram CreateRoundedProgram() => CreateProgram(
        UiShaders.Spv.UiRectangleShaders.RoundedRectangle.Vertex(),
        UiShaders.Spv.UiRectangleShaders.RoundedRectangle.Fragment(),
        UiShaders.Abi.UiRectangleShaders.RoundedRectangle.Vertex(),
        UiShaders.Abi.UiRectangleShaders.RoundedRectangle.Fragment());

    private static GraphicsShaderProgram CreateTextProgram() => CreateProgram(
        TextShaders.Spv.TextShaders.SdfText.Vertex(),
        TextShaders.Spv.TextShaders.SdfText.Fragment(),
        TextShaders.Abi.TextShaders.SdfText.Vertex(),
        TextShaders.Abi.TextShaders.SdfText.Fragment());

    private static uint ScaleToPixels(uint logical, float dpi) => checked((uint)Maths.Ceil(logical * (double)dpi));

    private static void SavePpm(ReadOnlySpan<byte> pixels, uint width, uint height, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory);
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

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory);
        File.WriteAllText(path, content);
    }

    private sealed class UiLibraryDemoContent : IDisposable
    {
        private readonly UiLibraryDemoArtifact _artifact;
        private readonly UiItemsControl _verticalGridLines;
        private readonly UiItemsControl _horizontalGridLines;

        internal UiLibraryDemoContent(ITextService textService, UiFontCatalog fonts)
        {
            _artifact = new UiLibraryDemoArtifact(textService, fonts);
            if (!_artifact.TryFindName("VerticalGridLines", out var vertical) ||
                vertical is not UiItemsControl verticalLines ||
                !_artifact.TryFindName("HorizontalGridLines", out var horizontal) ||
                horizontal is not UiItemsControl horizontalLines)
            {
                throw new InvalidOperationException("UI library demo grid overlay names are missing from the generated XAML artifact.");
            }

            _verticalGridLines = verticalLines;
            _horizontalGridLines = horizontalLines;
        }

        internal UiDocument Document => _artifact.Document;

        internal void AdvanceFrame() => GridOverlay.Update(_verticalGridLines, _horizontalGridLines);

        public void Dispose() => _artifact.Dispose();
    }

    private sealed class ManualUiPipeline : IAsyncDisposable
    {
        private readonly IRenderFrameSession _session;
        private readonly GraphicsShaderProgram _solidProgram = CreateSolidProgram();
        private readonly GraphicsShaderProgram _roundedProgram = CreateRoundedProgram();
        private readonly TextRenderFeature _textFeature;
        private readonly IRenderGraph _graph;
        private readonly bool _withReadback;
        private int _clipCount;
        private UiDisplayListGraphFeature _uiFeature;
        private ClearFeature _clearFeature;
        private ReadbackFeature? _readbackFeature;
        private IRenderFeature[] _features;

        internal ManualUiPipeline(IRenderFrameSession session, ITextService textService, PixelExtent extent, bool withReadback)
        {
            _session = session;
            _withReadback = withReadback;
            _textFeature = new TextRenderFeature(session, textService, CreateTextProgram(), extent);
            _uiFeature = CreateUiFeature(extent);
            _clearFeature = new ClearFeature(session.Target, extent, _solidProgram);
            _readbackFeature = withReadback ? new ReadbackFeature(session.Target, extent) : null;
            _features = CreateFeatures();
            _graph = session.CreateRenderGraph();
        }

        internal int VisualCount => _uiFeature.VisualCount;
        internal int ClipCount => _clipCount;
        internal int TextCount => _uiFeature.TextCount;

        internal void Resize(PixelExtent extent)
        {
            _textFeature.Resize(extent);
            _uiFeature.Dispose();
            _uiFeature = CreateUiFeature(extent);
            _clearFeature = new ClearFeature(_session.Target, extent, _solidProgram);
            _readbackFeature = _withReadback ? new ReadbackFeature(_session.Target, extent) : null;
            _features = CreateFeatures();
        }

        internal bool Render(UiDisplayList displayList, ulong frameNumber, out string diagnostics)
        {
            _clipCount = displayList.Clips.Length;
            if (!_uiFeature.Consume(displayList))
            {
                diagnostics = string.Join(Environment.NewLine, _uiFeature.Diagnostics);
                return false;
            }

            _graph.Build(frameNumber, _features);
            var result = _graph.Execute();
            if (result.Status == RenderGraphExecutionStatus.Submitted)
            {
                diagnostics = string.Empty;
                return true;
            }

            diagnostics = $"Graph execution failed: {result.Status}{Environment.NewLine}{result.Diagnostics}";
            return false;
        }

        internal bool TryCopyReadback(Span<byte> destination) =>
            _readbackFeature is { Readback.IsValid: true } readback &&
            _graph.CopyReadback(readback.Readback, destination) == destination.Length;

        public async ValueTask DisposeAsync()
        {
            _uiFeature.Dispose();
            _textFeature.Dispose();
            if (_graph is IAsyncDisposable graph)
            {
                await graph.DisposeAsync().ConfigureAwait(false);
            }
        }

        private UiDisplayListGraphFeature CreateUiFeature(PixelExtent extent) => new(
            _session,
            _roundedProgram,
            extent,
            textFeature: _textFeature,
            solidVisualProgram: _solidProgram,
            roundedSliceVisualProgram: _roundedProgram);

        private IRenderFeature[] CreateFeatures() => _readbackFeature is null
            ? [_clearFeature, _uiFeature]
            : [_clearFeature, _uiFeature, _readbackFeature];
    }

    private sealed class ClearFeature(RenderTargetHandle target, PixelExtent extent, GraphicsShaderProgram program) : IRenderFeature
    {
        private readonly RenderTargetHandle _target = target;
        private readonly GraphicsShaderProgram _program = program;
        private readonly ClearPass _pass = new(extent);

        public void AddPasses(IRenderGraphBuilder graph, ulong frameNumber)
        {
            var target = graph.ImportTarget(_target);
            var pass = graph.AddRasterPass(
                new RasterPassDescription(
                    "DeltaRender.UI.LibraryDemo.Clear",
                    new RasterPipelineDescription(_program, cullMode: RasterCullMode.None)),
                _pass);
            graph.UseColorAttachment(
                pass,
                0,
                new ColorAttachmentDescription(
                    target,
                    AttachmentLoadOperation.Clear,
                    AttachmentStoreOperation.Store,
                    new ClearColor(0, 0, 0, 1)));
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

    private sealed class ReadbackFeature(RenderTargetHandle target, PixelExtent extent) : IRenderFeature
    {
        private readonly RenderTargetHandle _target = target;
        private readonly PixelRect _region = new(0, 0, checked((int)extent.Width), checked((int)extent.Height));

        internal RenderGraphReadbackHandle Readback { get; private set; }

        public void AddPasses(IRenderGraphBuilder graph, ulong frameNumber)
        {
            var target = graph.ImportTarget(_target);
            Readback = graph.ReadbackTexture(target, in _region);
        }
    }

    private sealed record SampleOptions(
        bool Headless,
        uint Width,
        uint Height,
        float Dpi,
        int Frames,
        string? ReadbackPath,
        string? LayoutPath)
    {
        internal static SampleOptions Parse(string[] args)
        {
            var headless = HasFlag(args, "--headless");
            var width = ParseUInt(args, "--width", DefaultWidth);
            var height = ParseUInt(args, "--height", DefaultHeight);
            var dpi = ParseFloat(args, "--dpi", 1f);
            var frames = ParseInt(args, "--frames", headless ? 1 : 0);
            ArgumentOutOfRangeException.ThrowIfNegative(frames);

            return new(
                headless,
                width,
                height,
                dpi,
                frames,
                GetOption(args, "--readback"),
                GetOption(args, "--layout-json"));
        }

        private static bool HasFlag(string[] args, string option) =>
            args.Any(argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));

        private static string? GetOption(string[] args, string option)
        {
            for (var index = 0; index + 1 < args.Length; index++)
            {
                if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        private static int ParseInt(string[] args, string option, int fallback) =>
            GetOption(args, option) is not { } value
                ? fallback
                : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : throw new ArgumentException($"{option} requires an integer value.", nameof(args));

        private static uint ParseUInt(string[] args, string option, uint fallback) =>
            GetOption(args, option) is not { } value
                ? fallback
                : uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed != 0
                    ? parsed
                    : throw new ArgumentException($"{option} requires a positive unsigned integer.", nameof(args));

        private static float ParseFloat(string[] args, string option, float fallback) =>
            GetOption(args, option) is not { } value
                ? fallback
                : float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                  float.IsFinite(parsed) && parsed > 0
                    ? parsed
                    : throw new ArgumentException($"{option} requires a positive finite number.", nameof(args));
    }
}
