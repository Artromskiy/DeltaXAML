using Delta;
using Delta.Render;
using Delta.Render.Platform.SDL3;
using Delta.Render.RenderGraph;
using Delta.Render.Vulkan;
using Delta.Shader.Contract;
using Delta.Text;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;
using SDL3;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal static class Program
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = SampleOptions.Parse(args);
        var fonts = LoadFonts();
        using var textService = new DeltaTextService();
        using var content = new UiLibraryDemoContent(textService, fonts);
        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());

        if (options.Headless)
        {
            return await RunHeadless(renderer, textService, content, options).ConfigureAwait(false);
        }

        return await RunWindowed(renderer, textService, content, options).ConfigureAwait(false);
    }

    private static async Task<int> RunHeadless(
        VulkanRenderer renderer,
        ITextService textService,
        UiLibraryDemoContent content,
        SampleOptions options)
    {
        var extent = options.ToPixelExtent();
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
        await using var session = renderer.CreateWindowSession(window, new RenderSessionOptions());
        var metrics = window.Metrics;
        var extent = metrics.DrawableExtent.IsEmpty
            ? new PixelExtent(options.Width, options.Height)
            : metrics.DrawableExtent;
        return await RunFrames(session, window, textService, content, options, extent).ConfigureAwait(false);
    }

    private static async Task<int> RunFrames(
        IRenderFrameSession session,
        IRenderWindow? window,
        ITextService textService,
        UiLibraryDemoContent content,
        SampleOptions options,
        PixelExtent extent)
    {
        session.ResizeTarget(in extent);
        await using var pipeline = new UiLibraryDemoPipeline(
            session,
            textService,
            extent,
            window is null && options.ReadbackPath is not null);
        var frame = 0;

        while (CanRender(window, frame, options.Frames))
        {
            if (window is not null && !PumpWindowEvents(window, content.Document))
            {
                break;
            }

            var metrics = GetMetrics(window, options, extent);
            extent = ResizeIfNeeded(session, pipeline, extent, metrics.DrawableExtent);
            content.AdvanceFrame();

            content.Document.Layout(new float2(metrics.Width, metrics.Height), metrics.DpiScale);
            var displayList = content.Document.BuildDisplayList();
            if (!pipeline.Render(displayList, (ulong)frame, out var diagnostics))
            {
                await Console.Error.WriteLineAsync(diagnostics).ConfigureAwait(false);
                return 1;
            }

            frame++;
        }

        WriteLayout(content.Document, options.LayoutPath);
        if (!await WriteReadback(pipeline, extent, frame, options.ReadbackPath).ConfigureAwait(false))
        {
            return 1;
        }

        await Console.Out.WriteLineAsync(
            $"DeltaXAML direct renderer ({(window is null ? "headless" : "windowed")}): " +
            $"frames={frame}, visuals={pipeline.VisualCount}, clips={pipeline.ClipCount}, " +
            $"text={pipeline.TextCount}").ConfigureAwait(false);
        return 0;
    }

    private static WindowMetrics GetMetrics(IRenderWindow? window, SampleOptions options, PixelExtent extent) =>
        window?.Metrics ?? new WindowMetrics(options.Width, options.Height, options.Dpi)
        {
            DrawableWidth = extent.Width,
            DrawableHeight = extent.Height,
        };

    private static PixelExtent ResizeIfNeeded(
        IRenderFrameSession session,
        UiLibraryDemoPipeline pipeline,
        PixelExtent current,
        PixelExtent next)
    {
        if (next.IsEmpty || next == current)
        {
            return current;
        }

        session.ResizeTarget(in next);
        pipeline.Resize(next);
        return next;
    }

    private static bool CanRender(IRenderWindow? window, int frame, int frameLimit) =>
        (window is null || !window.IsClosed) && (frameLimit == 0 || frame < frameLimit);

    private static bool PumpWindowEvents(IRenderWindow window, UiDocument document)
    {
        Sdl3WindowFactory.PumpEvents();
        while (SDL.PollEvent(out var @event))
        {
            switch ((SDL.EventType)@event.Type)
            {
                case SDL.EventType.Quit:
                case SDL.EventType.WindowCloseRequested:
                    return false;
                case SDL.EventType.KeyDown:
                case SDL.EventType.KeyUp:
                    DispatchKey(document, @event);
                    break;
            }
        }

        return !window.IsClosed;
    }

    private static void DispatchKey(UiDocument document, SDL.Event @event)
    {
        var kind = @event.Type == (uint)SDL.EventType.KeyDown ? UiKeyEventKind.Down : UiKeyEventKind.Up;
        var key = new UiKeyEvent(
            kind,
            new UiPhysicalKey((uint)@event.Key.Scancode),
            new UiLogicalKey((uint)@event.Key.Scancode),
            default,
            @event.Key.Repeat);
        var input = UiInputEvent.FromKey(in key);
        document.Dispatch(in input);
    }

    private static UiFontCatalog LoadFonts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"UI library demo font was not found: {path}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", SampleFontId, File.ReadAllBytes(path));
        return fonts;
    }

    private static void WriteLayout(UiDocument document, string? path)
    {
        if (path is null)
        {
            return;
        }

        WriteText(path, document.BuildLayoutDiagnosticsJson());
    }

    private static async Task<bool> WriteReadback(
        UiLibraryDemoPipeline pipeline,
        PixelExtent extent,
        int frame,
        string? path)
    {
        if (path is null || frame == 0)
        {
            return true;
        }

        var pixels = new byte[checked((int)((ulong)extent.Width * extent.Height * 4))];
        if (!pipeline.TryCopyReadback(pixels))
        {
            await Console.Error.WriteLineAsync("Headless Vulkan readback did not complete.").ConfigureAwait(false);
            return false;
        }

        SavePpm(pixels, extent.Width, extent.Height, path);
        await Console.Out.WriteLineAsync($"readback: path={Path.GetFullPath(path)}").ConfigureAwait(false);
        return true;
    }

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
}
