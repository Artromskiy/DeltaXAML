using System.Diagnostics;
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
using DeltaXaml.Samples.Snake.Generated;
using SDL3;

namespace DeltaXaml.Samples.Snake;

internal static class SnakeWindowRunner
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

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
            return await RunWindowAsync(window, ParseFrameLimit(args)).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            return 1;
        }
#pragma warning restore CA1031
    }

    private static async Task<int> RunWindowAsync(IRenderWindow window, int frameLimit)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"Snake sample font was not found: {fontPath}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        using var textService = new SixLaborsTextService();
        using var page = new SnakeArtifact(textService, fonts);
        var game = new SnakeGame();
        var view = new SnakeView(page);
        game.StartNewGame();
        view.Render(game);

        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateWindowSession(
            window,
            new RenderSessionOptions(EnableProfiling: true));
        var visualProgram = LoadRoundedProgram();
        var textProgram = LoadTextProgram();
        using var textFeature = new TextRenderFeature(session, textService, textProgram, new PixelExtent(980, 760));

        var metrics = ReadMetrics(window, new PixelExtent(980, 760));
        var extent = new PixelExtent(metrics.Width, metrics.Height);
        session.ResizeTarget(in extent);
        textFeature.Resize(extent);
        UiDisplayListGraphFeature? uiFeature = CreateFeature(session, visualProgram, textFeature, extent);
        var graph = session.CreateRenderGraph();
        var renderedFrames = 0;
        var clipCount = 0;
        var nextTick = Stopwatch.GetTimestamp();
        var running = true;

        try
        {
            while (running && !window.IsClosed && renderedFrames < frameLimit)
            {
                running = PumpEvents(page, game, view);
                metrics = ReadMetrics(window, extent);
                var nextExtent = new PixelExtent(metrics.Width, metrics.Height);
                if (!nextExtent.IsEmpty && nextExtent != extent)
                {
                    session.ResizeTarget(in nextExtent);
                    textFeature.Resize(nextExtent);
                    uiFeature.Dispose();
                    uiFeature = CreateFeature(session, visualProgram, textFeature, nextExtent);
                    extent = nextExtent;
                }

                var now = Stopwatch.GetTimestamp();
                if (now >= nextTick)
                {
                    game.Tick();
                    view.Render(game);
                    nextTick = now + Stopwatch.Frequency / 8;
                }

                page.Document.Layout(new float2(metrics.Width, metrics.Height), metrics.DpiScale);
                var displayList = page.Document.BuildDisplayList();
                clipCount = displayList.Clips.Length;
                if (!uiFeature.Consume(displayList))
                {
                    await Console.Error.WriteLineAsync(string.Join(Environment.NewLine, uiFeature.Diagnostics)).ConfigureAwait(false);
                    return 1;
                }

                IRenderFeature[] features = [uiFeature];
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
                    profiler.TryGetCompleted(frameNumber, out var profile) &&
                    (renderedFrames == 0 || frameNumber % 60 == 0))
                {
                    WriteProfile(profile);
                }

                renderedFrames++;
                Thread.Sleep(16);
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
            $"passes={report.Counters.PassCount}, gpu-timestamps={capabilities.GpuTimestamps}");

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
        TextRenderFeature textFeature,
        PixelExtent extent) =>
        new(session, visualProgram, extent, textFeature: textFeature);

    private static WindowMetrics ReadMetrics(IRenderWindow window, PixelExtent fallback)
    {
        var handle = new IntPtr(unchecked((long)window.Handle.Value));
        if (SDL.GetWindowSizeInPixels(handle, out var width, out var height) && width > 0 && height > 0)
        {
            return new WindowMetrics((uint)width, (uint)height, window.Metrics.DpiScale);
        }

        return new WindowMetrics(fallback.Width, fallback.Height, window.Metrics.DpiScale);
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
}
