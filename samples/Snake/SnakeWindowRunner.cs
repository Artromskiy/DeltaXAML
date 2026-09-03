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
using DeltaXaml.Samples.Snake.Generated;
using SDL3;
using TextShaders = Delta.Shader.Text.Shaders;
using UiShaders = Delta.Shader.UI.Shaders;

namespace DeltaXaml.Samples.Snake;

internal static class SnakeWindowRunner
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("8C1F6D6B-0F9A-4B3A-9D38-6C9D2B6E4F51"));

    internal static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            var factory = new Sdl3WindowFactory();
            var createResult = factory.CreateWindow(
                new WindowConfiguration("DeltaXAML Snake", 980, 760, true, true));
            if (!createResult.Succeeded || createResult.Window is not { } window)
            {
                await Console.Error.WriteLineAsync("Window creation failed.").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(createResult.Diagnostics.ToText()).ConfigureAwait(false);
                return 1;
            }

            await using var windowLease = window.ConfigureAwait(false);
            var grid = SnakeArguments.ParseGrid(args);
            return await RunWindowAsync(
                window,
                ParseFrameLimit(args),
                HasFlag(args, "--profile") || HasFlag(args, "--profiling"),
                grid).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RunWindowAsync(
        IRenderWindow window,
        int frameLimit,
        bool enableProfiling,
        (int Columns, int Rows) grid)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LuckiestGuy-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"Snake sample font was not found: {fontPath}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        using var textService = new SixLaborsTextService();
        var game = new SnakeGame(grid.Columns, grid.Rows);
        using var page = new SnakeArtifact(game, textService, fonts);
        var view = new SnakeView(page);
        game.StartNewGame();
        view.Render(game);

        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateWindowSession(
            window,
            new RenderSessionOptions(EnableProfiling: enableProfiling, FramesInFlight: 16));
        var roundVisualProgram = LoadRoundProgram();
        var solidVisualProgram = LoadSolidProgram();
        var textProgram = LoadTextProgram();

        var metrics = ReadMetrics(
            window,
            new WindowMetrics(980, 760, 1f)
            {
                DrawableWidth = 980,
                DrawableHeight = 760,
            });
        var extent = metrics.DrawableExtent;
        session.ResizeTarget(in extent);
        using var textFeature = new TextRenderFeature(session, textService, textProgram, extent);
        textFeature.Resize(extent);
        UiDisplayListGraphFeature uiFeature = CreateFeature(
            session,
            roundVisualProgram,
            solidVisualProgram,
            textFeature,
            extent);
        var graph = session.CreateRenderGraph();
        IRenderFeature[] features = [uiFeature];
        var renderedFrames = 0;
        var clipCount = 0;
        var running = true;

        try
        {
            while (running && !window.IsClosed && renderedFrames < frameLimit)
            {
                running = PumpEvents(page, game, view);
                metrics = ReadMetrics(window, metrics);
                var nextExtent = metrics.DrawableExtent;
                if (!nextExtent.IsEmpty && nextExtent != extent)
                {
                    session.ResizeTarget(in nextExtent);
                    textFeature.Resize(nextExtent);
                    uiFeature.Dispose();
                    uiFeature = CreateFeature(
                        session,
                        roundVisualProgram,
                        solidVisualProgram,
                        textFeature,
                        nextExtent);
                    features[0] = uiFeature;
                    extent = nextExtent;
                }

                game.Tick();
                view.Render(game);

                page.Document.Layout(new float2(metrics.Width, metrics.Height), metrics.DpiScale);
                var displayList = page.Document.BuildDisplayList();
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

                var frameNumber = (ulong)renderedFrames;
                var result = graph.Execute();
                if (result.Status != RenderGraphExecutionStatus.Submitted)
                {
                    await Console.Error.WriteLineAsync($"Graph execution failed: {result.Status}").ConfigureAwait(false);
                    await Console.Error.WriteLineAsync(result.Diagnostics.ToString()).ConfigureAwait(false);
                    return 1;
                }

                if (session.Profiler is { } profiler &&
                    profiler.TryGetCompleted(frameNumber, out var profile))
                {
                    WriteProfile(profile);
                }

                renderedFrames++;
            }

            await Console.Out.WriteLineAsync(
                $"DeltaXAML Snake window sample: frames={renderedFrames}, " +
                $"visuals={uiFeature.VisualCount}, clips={clipCount}, text={uiFeature.TextCount}").ConfigureAwait(false);
            return 0;
        }
        finally
        {
            uiFeature.Dispose();
        }
    }

    private static void WriteProfile(RenderProfileReport report)
    {
        var timing = report.Timing;
        var capabilities = report.Capabilities;
        Console.WriteLine(
            $"Render profile: frame={report.FrameNumber}, status={report.Status}, " +
            $"build={timing.Build}, acquire={timing.Acquire}, " +
            $"record={timing.Record}, submit-present={timing.SubmitAndPresent}, " +
            $"fence-wait={timing.FenceWait}, layout-shaping={timing.LayoutAndShapingCpu}, " +
            $"passes={report.Counters.PassCount}, resources={report.Counters.ResourceCount}, " +
            $"draws={report.Counters.DrawCallCount}, descriptor-binds={report.Counters.DescriptorBindCount}, " +
            $"upload-bytes={report.Counters.UploadBytes}, gpu-timestamps={capabilities.GpuTimestamps}");

        foreach (var pass in report.Passes)
        {
            Console.WriteLine(
                $"  pass={pass.Name}, kind={pass.Kind}, cpu-record={pass.CpuRecordDuration}" +
                (pass.GpuDuration is { } gpu ? $", gpu={gpu}" : ", gpu=unavailable"));
        }
    }

    private static bool PumpEvents(SnakeArtifact page, SnakeGame game, SnakeView view)
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

            var key = new UiKeyEvent(
                UiKeyEventKind.Down,
                new UiPhysicalKey((uint)@event.Key.Scancode),
                new UiLogicalKey((uint)@event.Key.Scancode),
                default,
                false);
            page.Document.Dispatch(UiInputEvent.FromKey(in key));
            switch (@event.Key.Scancode)
            {
                case SDL.Scancode.Up or SDL.Scancode.W:
                    game.SetDirection(Direction.Up);
                    break;
                case SDL.Scancode.Down or SDL.Scancode.S:
                    game.SetDirection(Direction.Down);
                    break;
                case SDL.Scancode.Left or SDL.Scancode.A:
                    game.SetDirection(Direction.Left);
                    break;
                case SDL.Scancode.Right or SDL.Scancode.D:
                    game.SetDirection(Direction.Right);
                    break;
                case SDL.Scancode.Space:
                    game.TogglePause();
                    view.Render(game);
                    break;
                case SDL.Scancode.Return:
                    game.StartNewGame();
                    view.Render(game);
                    break;
            }
        }

        return running;
    }

    private static UiDisplayListGraphFeature CreateFeature(
        IRenderFrameSession session,
        IGraphicsShaderProgram visualProgram,
        IGraphicsShaderProgram solidVisualProgram,
        TextRenderFeature textFeature,
        PixelExtent extent) =>
        new(
            session,
            visualProgram,
            extent,
            textFeature: textFeature,
            solidVisualProgram: solidVisualProgram);

    private static WindowMetrics ReadMetrics(IRenderWindow window, WindowMetrics fallback)
    {
        var handle = new IntPtr(unchecked((long)window.Handle.Value));
        if (!SDL.GetWindowSize(handle, out var logicalWidth, out var logicalHeight) || logicalWidth <= 0 || logicalHeight <= 0)
        {
            return fallback;
        }

        var drawableWidth = logicalWidth;
        var drawableHeight = logicalHeight;
        if (!SDL.GetWindowSizeInPixels(handle, out drawableWidth, out drawableHeight) || drawableWidth <= 0 || drawableHeight <= 0)
        {
            drawableWidth = logicalWidth;
            drawableHeight = logicalHeight;
        }

        var dpiScale = SDL.GetWindowPixelDensity(handle);
        if (!float.IsFinite(dpiScale) || dpiScale <= 0)
        {
            dpiScale = Maths.Max(
                (float)drawableWidth / logicalWidth,
                (float)drawableHeight / logicalHeight);
        }

        if (!float.IsFinite(dpiScale) || dpiScale <= 0)
        {
            return fallback;
        }

        return new WindowMetrics((uint)logicalWidth, (uint)logicalHeight, dpiScale)
        {
            DrawableWidth = (uint)drawableWidth,
            DrawableHeight = (uint)drawableHeight,
        };
    }

    private static GraphicsShaderProgram LoadRoundProgram()
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

    private static bool HasFlag(string[] args, string flag)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
}
