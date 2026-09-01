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
using DeltaXaml.Samples.Game2048.Generated;
using SDL3;

namespace DeltaXaml.Samples.Game2048.Render;

internal static class Program
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            var frameLimit = ParseFrameLimit(args);
            var displayListMode = ParseDisplayListMode(args);
            var layoutJsonPath = ParsePath(args, "--layout-json", "/tmp/delta-2048-layout.json");
            if (HasFlag(args, "--headless"))
            {
                return await RunHeadlessAsync(
                    frameLimit == int.MaxValue ? 1 : frameLimit,
                    displayListMode,
                    layoutJsonPath).ConfigureAwait(false);
            }

            var factory = new Sdl3WindowFactory();
            var createResult = factory.CreateWindow(
                new WindowConfiguration("DeltaXAML 2048", 960, 720, true, true));
            if (!createResult.Succeeded || createResult.Window is not { } window)
            {
                await Console.Error.WriteLineAsync("Window creation failed.").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(createResult.Diagnostics.ToText()).ConfigureAwait(false);
                return 1;
            }

            await using var windowLease = window.ConfigureAwait(false);
            return await RunWindowAsync(window, frameLimit, displayListMode).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            return 1;
        }
#pragma warning restore CA1031
    }

    private static async Task<int> RunWindowAsync(
        IRenderWindow window,
        int frameLimit,
        DisplayListMode displayListMode)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"2048 sample font was not found: {fontPath}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        using var textService = new SixLaborsTextService();
        using var page = new Game2048Artifact(textService, fonts);
        var game = new Game2048State();
        var view = new Game2048View(page);
        var displayListProjection = new DisplayListProjection(displayListMode);
        game.LoadDesignBoard();
        view.Render(game);

        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateWindowSession(window);
        var visualProgram = LoadRoundedProgram();
        var solidVisualProgram = LoadSolidProgram();
        var roundedSliceVisualProgram = LoadRoundedSliceProgram();
        var textProgram = LoadTextProgram();
        using var textFeature = new TextRenderFeature(session, textService, textProgram, new PixelExtent(960, 720));
        var graph = session.CreateRenderGraph();
        UiDisplayListGraphFeature? uiFeature = null;
        try
        {
            var extent = new PixelExtent(960, 720);
            session.ResizeTarget(in extent);
            textFeature.Resize(extent);
            uiFeature = CreateFeature(
                session,
                visualProgram,
                solidVisualProgram,
                roundedSliceVisualProgram,
                textFeature,
                extent);
            IRenderFeature[] features = [uiFeature];
            var renderedFrames = 0;
            var clipCount = 0;
            var running = true;

            while (running && !window.IsClosed && renderedFrames < frameLimit)
            {
                running = PumpEvents(page, game, view);
                var metrics = ReadMetrics(window, extent);
                var nextExtent = new PixelExtent(metrics.Width, metrics.Height);
                if (!nextExtent.IsEmpty && nextExtent != extent)
                {
                    session.ResizeTarget(in nextExtent);
                    textFeature.Resize(nextExtent);
                    uiFeature.Dispose();
                    uiFeature = CreateFeature(
                        session,
                        visualProgram,
                        solidVisualProgram,
                        roundedSliceVisualProgram,
                        textFeature,
                        nextExtent);
                    features = [uiFeature];
                    extent = nextExtent;
                }

                page.Document.Layout(new float2(metrics.Width, metrics.Height), metrics.DpiScale);
                var displayList = displayListProjection.Project(page.Document.BuildDisplayList());
                clipCount = displayList.Clips.Length;
                if (!uiFeature.Consume(displayList))
                {
                    await Console.Error.WriteLineAsync(string.Join(Environment.NewLine, uiFeature.Diagnostics)).ConfigureAwait(false);
                    return 1;
                }

                graph.Build((ulong)renderedFrames, features);
                if (uiFeature.Diagnostics.Count != 0)
                {
                    await Console.Error.WriteLineAsync(string.Join(Environment.NewLine, uiFeature.Diagnostics)).ConfigureAwait(false);
                    return 1;
                }

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
                $"DeltaXAML 2048 renderer sample: mode={displayListMode}, frames={renderedFrames}, " +
                $"visuals={uiFeature.VisualCount}, clips={clipCount}, text={uiFeature.TextCount}").ConfigureAwait(false);
            return 0;
        }
        finally
        {
            uiFeature?.Dispose();
        }
    }

    private static async Task<int> RunHeadlessAsync(
        int frameLimit,
        DisplayListMode displayListMode,
        string layoutJsonPath)
    {
        const uint width = 960;
        const uint height = 720;
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"2048 sample font was not found: {fontPath}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        using var textService = new SixLaborsTextService();
        using var page = new Game2048Artifact(textService, fonts);
        var game = new Game2048State();
        var view = new Game2048View(page);
        var displayListProjection = new DisplayListProjection(displayListMode);
        game.LoadDesignBoard();
        view.Render(game);

        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateHeadlessSession(width, height);
        var extent = new PixelExtent(width, height);
        var visualProgram = LoadRoundedProgram();
        var solidVisualProgram = LoadSolidProgram();
        var roundedSliceVisualProgram = LoadRoundedSliceProgram();
        var textProgram = LoadTextProgram();
        using var textFeature = new TextRenderFeature(session, textService, textProgram, extent);
        using var uiFeature = CreateFeature(
            session,
            visualProgram,
            solidVisualProgram,
            roundedSliceVisualProgram,
            textFeature,
            extent);
        var readbackFeature = new HeadlessReadbackFeature(session.Target, width, height);
        var graph = session.CreateRenderGraph();
        IRenderFeature[] features = [uiFeature, readbackFeature];

        page.Document.Layout(new float2(width, height), 1f);
        File.WriteAllText(layoutJsonPath, page.Document.BuildLayoutDiagnosticsJson());
        var displayList = displayListProjection.Project(page.Document.BuildDisplayList());
        var clipCount = displayList.Clips.Length;
        if (!uiFeature.Consume(displayList))
        {
            await Console.Error.WriteLineAsync(string.Join(Environment.NewLine, uiFeature.Diagnostics)).ConfigureAwait(false);
            return 1;
        }

        for (var frameNumber = 0UL; frameNumber < (ulong)frameLimit; frameNumber++)
        {
            graph.Build(frameNumber, features);
            if (uiFeature.Diagnostics.Count != 0)
            {
                await Console.Error.WriteLineAsync(string.Join(Environment.NewLine, uiFeature.Diagnostics)).ConfigureAwait(false);
                return 1;
            }

            var result = graph.Execute();
            if (result.Status != RenderGraphExecutionStatus.Submitted)
            {
                await Console.Error.WriteLineAsync($"Headless graph execution failed: {result.Status}").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(result.Diagnostics.ToString()).ConfigureAwait(false);
                return 1;
            }
        }

        var pixels = new byte[checked((int)((ulong)width * height * 4))];
        if (!readbackFeature.Readback.IsValid || graph.CopyReadback(readbackFeature.Readback, pixels) != pixels.Length)
        {
            await Console.Error.WriteLineAsync("Headless Vulkan readback did not complete.").ConfigureAwait(false);
            return 1;
        }

        var centerOffset = checked(((int)height / 2 * (int)width + (int)width / 2) * 4);
        var centerRed = pixels[centerOffset];
        var centerGreen = pixels[centerOffset + 1];
        var centerBlue = pixels[centerOffset + 2];
        var centerAlpha = pixels[centerOffset + 3];
        var nonZeroPixels = CountNonZeroPixels(pixels);
        SavePpm(pixels, width, height, "/tmp/delta-2048-current.ppm");
        await Console.Out.WriteLineAsync(
            $"DeltaXAML 2048 headless: mode={displayListMode}, frames={frameLimit}, " +
            $"target={width}x{height}, visuals={uiFeature.VisualCount}, clips={clipCount}, " +
            $"text={uiFeature.TextCount}, nonZeroPixels={nonZeroPixels}, " +
            $"center=({centerRed},{centerGreen},{centerBlue},{centerAlpha})").ConfigureAwait(false);

        if (displayListMode != DisplayListMode.TextOnly && centerAlpha == 0)
        {
            await Console.Error.WriteLineAsync("Headless target center is transparent; the UI did not produce visible pixels.").ConfigureAwait(false);
            return 1;
        }

        return 0;
    }

    private static void SavePpm(byte[] pixels, uint width, uint height, string path)
    {
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

    private static UiDisplayListGraphFeature CreateFeature(
        IRenderFrameSession session,
        IGraphicsShaderProgram visualProgram,
        IGraphicsShaderProgram solidVisualProgram,
        IGraphicsShaderProgram roundedSliceVisualProgram,
        TextRenderFeature textFeature,
        PixelExtent extent)
    {
        return new UiDisplayListGraphFeature(
            session,
            visualProgram,
            extent,
            textFeature: textFeature,
            solidVisualProgram: solidVisualProgram,
            roundedSliceVisualProgram: roundedSliceVisualProgram);
    }

    private static WindowMetrics ReadMetrics(IRenderWindow window, PixelExtent fallback)
    {
        var handle = new IntPtr(unchecked((long)window.Handle.Value));
        if (SDL.GetWindowSizeInPixels(handle, out var width, out var height) && width > 0 && height > 0)
        {
            return new WindowMetrics((uint)width, (uint)height, window.Metrics.DpiScale);
        }

        return new WindowMetrics(fallback.Width, fallback.Height, window.Metrics.DpiScale);
    }

    private static bool PumpEvents(Game2048Artifact page, Game2048State game, Game2048View view)
    {
        Sdl3WindowFactory.PumpEvents();
        var running = true;
        while (SDL.PollEvent(out var @event))
        {
            var type = (SDL.EventType)@event.Type;
            if (type is SDL.EventType.Quit or SDL.EventType.WindowCloseRequested)
            {
                running = false;
                continue;
            }

            if (type != SDL.EventType.KeyDown || @event.Key.Repeat)
            {
                continue;
            }

            if (DirectionFor(@event.Key.Scancode) is not { } direction)
            {
                continue;
            }

            var key = new UiKeyEvent(
                UiKeyEventKind.Down,
                new UiPhysicalKey((uint)@event.Key.Scancode),
                new UiLogicalKey((uint)@event.Key.Scancode),
                default,
                false);
            page.Document.Dispatch(UiInputEvent.FromKey(in key));
            if (game.TryMove(direction))
            {
                view.Render(game);
            }
        }

        return running;
    }

    private static MoveDirection? DirectionFor(SDL.Scancode scanCode) => scanCode switch
    {
        SDL.Scancode.Left or SDL.Scancode.A => MoveDirection.Left,
        SDL.Scancode.Up or SDL.Scancode.W => MoveDirection.Up,
        SDL.Scancode.Right or SDL.Scancode.D => MoveDirection.Right,
        SDL.Scancode.Down or SDL.Scancode.S => MoveDirection.Down,
        _ => null,
    };

    private static IGraphicsShaderProgram LoadRoundedProgram()
    {
        var vertexPath = Path.Combine(AppContext.BaseDirectory, "shaders", "ClipAwareRoundedRectangleVertex.vert.spv");
        var fragmentPath = Path.Combine(AppContext.BaseDirectory, "shaders", "ClipAwareRoundedRectangleFragment.frag.spv");
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
        {
            throw new FileNotFoundException($"Clip-aware rounded rectangle shader artifacts were not found: {vertexPath}");
        }

        return ClipAwareRoundedRectangleGraphicsShaderProgram.CreateProgram(
            File.ReadAllBytes(vertexPath),
            File.ReadAllBytes(fragmentPath));
    }

    private static IGraphicsShaderProgram LoadSolidProgram()
    {
        var vertexPath = Path.Combine(AppContext.BaseDirectory, "shaders", "ClipAwareSolidRectangleVertex.vert.spv");
        var fragmentPath = Path.Combine(AppContext.BaseDirectory, "shaders", "ClipAwareSolidRectangleFragment.frag.spv");
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
        {
            throw new FileNotFoundException($"Clip-aware solid rectangle shader artifacts were not found: {vertexPath}");
        }

        return ClipAwareSolidRectangleGraphicsShaderProgram.CreateProgram(
            File.ReadAllBytes(vertexPath),
            File.ReadAllBytes(fragmentPath));
    }

    private static IGraphicsShaderProgram LoadRoundedSliceProgram()
    {
        var vertexPath = Path.Combine(AppContext.BaseDirectory, "shaders", "ClipAwareRoundedRectangleSliceVertex.vert.spv");
        var fragmentPath = Path.Combine(AppContext.BaseDirectory, "shaders", "ClipAwareRoundedRectangleSliceFragment.frag.spv");
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
        {
            throw new FileNotFoundException($"Clip-aware rounded rectangle slice shader artifacts were not found: {vertexPath}");
        }

        return ClipAwareRoundedRectangleSliceGraphicsShaderProgram.CreateProgram(
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

    private static DisplayListMode ParseDisplayListMode(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (!string.Equals(args[index], "--probe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return args[index + 1].ToLowerInvariant() switch
            {
                "combined" => DisplayListMode.Combined,
                "visual" or "visuals" => DisplayListMode.VisualOnly,
                "text" => DisplayListMode.TextOnly,
                _ => throw new ArgumentException("--probe must be combined, visuals or text.", nameof(args)),
            };
        }

        return DisplayListMode.Combined;
    }

    private enum DisplayListMode : byte
    {
        Combined,
        VisualOnly,
        TextOnly,
    }

    private sealed class DisplayListProjection(DisplayListMode mode)
    {
        private UiDrawRef[] _order = [];
        private UiElementIdentity[] _identities = [];

        internal UiDisplayList Project(UiDisplayList source)
        {
            if (mode == DisplayListMode.Combined)
            {
                return source;
            }

            var selectedKind = mode == DisplayListMode.VisualOnly
                ? UiDrawKind.Visual
                : UiDrawKind.Text;
            EnsureCapacity(source.Order.Length);
            var count = 0;
            for (var index = 0; index < source.Order.Length; index++)
            {
                var draw = source.Order[index];
                if (draw.Kind == selectedKind)
                {
                    _order[count++] = draw;
                    _identities[count - 1] = source.Identities[index];
                }
            }

            return mode == DisplayListMode.VisualOnly
                ? new UiDisplayList(source.Visuals, source.Clips, ReadOnlySpan<UiTextDraw>.Empty, _order.AsSpan(0, count), _identities.AsSpan(0, count))
                : new UiDisplayList(ReadOnlySpan<UiVisualDraw>.Empty, source.Clips, source.Text, _order.AsSpan(0, count), _identities.AsSpan(0, count));
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _order.Length)
            {
                return;
            }

            _order = new UiDrawRef[required];
            _identities = new UiElementIdentity[required];
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
