using System.Diagnostics;
using System.Globalization;
using Delta.Maths;
using Delta.Render;
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
using TextShaders = Delta.Shader.Text.Shaders;
using UiShaders = Delta.Shader.UI.Shaders;

namespace DeltaXaml.Samples.Snake;

internal static class SnakeHeadlessRenderRunner
{
    private const int Width = 980;
    private const int Height = 760;
    private const int DefaultFrames = 600;
    private const int DefaultFramesInFlight = 16;
    private const int DefaultSkipFrames = 100;

    private static readonly FontSourceId SampleFontId =
        new(new Guid("8C1F6D6B-0F9A-4B3A-9D38-6C9D2B6E4F51"));

    internal static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        int frameCount = ParsePositiveInt(args, "--frames", DefaultFrames);
        int skipFrames = ParseNonNegativeInt(args, "--skip", DefaultSkipFrames);
        int framesInFlight = ParsePositiveInt(args, "--slots", DefaultFramesInFlight);
        string layoutJsonPath = ParsePath(args, "--layout-json", "/tmp/delta-snake-layout.json");
        string profileReportPath = GetOption(args, "--profile-report", string.Empty);
        var fonts = new UiFontCatalog();
        string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LuckiestGuy-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"Snake sample font was not found: {fontPath}");
        }

        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        using var textService = new SixLaborsTextService();
        using var page = new SnakeArtifact(textService, fonts);
        var game = new SnakeGame();
        var view = new SnakeView(page);
        game.StartNewGame();
        view.Render(game);

        var initial = LayoutAndBuild(page.Document);
        File.WriteAllText(layoutJsonPath, page.Document.BuildLayoutDiagnosticsJson());
        Require(initial.Visuals.Length >= SnakeGame.CellCount, "Snake must emit one retained visual per board cell.");
        Require(initial.Text.Length >= 5, "Snake must emit its retained HUD text.");

        game.SetDirection(Direction.Down);
        for (var tick = 0; tick < 8; tick++)
        {
            game.Tick();
            view.Render(game);
        }

        var checkedFrame = LayoutAndBuild(page.Document);
        Require(checkedFrame.Visuals.Length == initial.Visuals.Length, "Ticks must reuse the fixed XAML visual slots.");
        Require(checkedFrame.Text.Length == initial.Text.Length, "Ticks must reuse the fixed XAML text slots.");
        Require(checkedFrame.Order.Length == checkedFrame.Visuals.Length + checkedFrame.Text.Length, "Display order must cover every Snake draw.");

        await using var renderer = new VulkanRenderer(new VulkanRendererOptions());
        await using var session = renderer.CreateHeadlessSession(
            Width,
            Height,
            new RenderSessionOptions(EnableProfiling: true, FramesInFlight: framesInFlight));
        var extent = new PixelExtent(Width, Height);
        var visualProgram = LoadRoundedProgram();
        var solidVisualProgram = LoadSolidProgram();
        var textProgram = LoadTextProgram();
        using var textFeature = new TextRenderFeature(session, textService, textProgram, extent);
        using var uiFeature = new UiDisplayListGraphFeature(
            session,
            visualProgram,
            extent,
            textFeature: textFeature,
            solidVisualProgram: solidVisualProgram);
        var graph = session.CreateRenderGraph();
        IRenderFeature[] features = [uiFeature];
        int measuredFrameCount = Math.Max(0, frameCount - skipFrames);
        var profiles = new List<HeadlessProfile>(measuredFrameCount);
        var layoutNanoseconds = new List<double>(measuredFrameCount);

        for (var frameNumber = 0; frameNumber < frameCount; frameNumber++)
        {
            game.Tick();
            view.Render(game);

            double frameLayoutNanoseconds = 0;
            long layoutStart = Stopwatch.GetTimestamp();
            page.Document.Layout(new float2(Width, Height), 1);
            var displayList = page.Document.BuildDisplayList();
            if (frameNumber >= skipFrames)
            {
                frameLayoutNanoseconds = Stopwatch.GetElapsedTime(layoutStart).TotalNanoseconds;
                layoutNanoseconds.Add(frameLayoutNanoseconds);
            }

            if (!uiFeature.Consume(displayList))
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, uiFeature.Diagnostics));
            }

            graph.Build((ulong)frameNumber, features);
            if (uiFeature.Diagnostics.Count != 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, uiFeature.Diagnostics));
            }

            var result = graph.Execute();
            if (result.Status != RenderGraphExecutionStatus.Submitted)
            {
                throw new InvalidOperationException($"Snake headless frame {frameNumber} failed: {result.Status}\n{result.Diagnostics}");
            }

            if (frameNumber >= skipFrames &&
                session.Profiler is { } profiler &&
                profiler.TryGetCompleted((ulong)frameNumber, out var report))
            {
                profiles.Add(HeadlessProfile.From(report, frameNumber, frameLayoutNanoseconds));
            }
        }

        if (!string.IsNullOrWhiteSpace(profileReportPath))
        {
            WriteRawProfiles(profileReportPath, profiles);
        }

        Console.WriteLine("DeltaXAML Snake headless render");
        Console.WriteLine($"frames={frameCount}, skip={skipFrames}, measured={measuredFrameCount}, profiles={profiles.Count}, slots={framesInFlight}, layout-json={layoutJsonPath}");
        Console.WriteLine($"display list: visuals={checkedFrame.Visuals.Length}, clips={checkedFrame.Clips.Length}, text={checkedFrame.Text.Length}, order={checkedFrame.Order.Length}");
        WriteTimingSummary("layout-shaping", layoutNanoseconds);
        WriteProfileSummary(profiles);
        return 0;
    }

    private static UiDisplayList LayoutAndBuild(UiDocument document)
    {
        document.Layout(new float2(Width, Height), 1);
        return document.BuildDisplayList();
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

    private static void WriteProfileSummary(List<HeadlessProfile> profiles)
    {
        if (profiles.Count == 0)
        {
            Console.WriteLine("profile tail: unavailable");
            return;
        }

        WriteTimingSummary("build", profiles, static profile => profile.BuildNanoseconds);
        WriteTimingSummary("acquire", profiles, static profile => profile.AcquireNanoseconds);
        WriteTimingSummary("record", profiles, static profile => profile.RecordNanoseconds);
        WriteTimingSummary("submit-present", profiles, static profile => profile.SubmitNanoseconds);
        WriteTimingSummary("fence-wait", profiles, static profile => profile.FenceNanoseconds);
        WriteTimingSummary("pass-cpu", profiles, static profile => profile.PassCpuNanoseconds);
        WriteTimingSummary("pass-gpu", profiles, static profile => profile.PassGpuNanoseconds);

        HeadlessProfile latest = profiles[^1];
        Console.WriteLine(
            $"counters: passes={latest.PassCount}, resources={latest.ResourceCount}, draws={latest.DrawCount}, " +
            $"descriptor-binds={latest.DescriptorBindCount}, upload-bytes={latest.UploadBytes}, " +
            $"gpu-timestamps={latest.GpuTimestamps}");
    }

    private static void WriteTimingSummary(string name, List<double> values)
    {
        var samples = new double[values.Count];
        values.CopyTo(samples);
        WriteTimingSummary(name, samples);
    }

    private static void WriteTimingSummary<T>(string name, List<T> values, Func<T, double> selector)
    {
        var samples = new double[values.Count];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = selector(values[index]);
        }

        WriteTimingSummary(name, samples);
    }

    private static void WriteTimingSummary(string name, double[] values)
    {
        if (values.Length == 0)
        {
            Console.WriteLine($"{name}: unavailable");
            return;
        }

        Array.Sort(values);
        double mean = 0;
        for (int index = 0; index < values.Length; index++)
        {
            mean += values[index];
        }

        mean /= values.Length;
        int medianIndex = values.Length / 2;
        double median = values.Length % 2 == 0
            ? (values[medianIndex - 1] + values[medianIndex]) / 2
            : values[medianIndex];
        int p95Index = Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * 0.95) - 1);
        Console.WriteLine($"{name}: mean={FormatNanoseconds(mean)}, median={FormatNanoseconds(median)}, p95={FormatNanoseconds(values[p95Index])}");
    }

    private static void WriteRawProfiles(string path, List<HeadlessProfile> profiles)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(fullPath, false);
        writer.WriteLine(
            "frame\tbuild_ns\tacquire_ns\trecord_ns\tsubmit_present_ns\tfence_wait_ns\t" +
            "layout_shaping_ns\tpass_cpu_ns\tpass_gpu_ns\tpass_count\tresource_count\t" +
            "draw_count\tdescriptor_bind_count\tupload_bytes\tgpu_timestamps");
        foreach (var profile in profiles)
        {
            writer.Write(profile.FrameNumber.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.BuildNanoseconds.ToString("R", CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.AcquireNanoseconds.ToString("R", CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.RecordNanoseconds.ToString("R", CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.SubmitNanoseconds.ToString("R", CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.FenceNanoseconds.ToString("R", CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.LayoutNanoseconds.ToString("R", CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.PassCpuNanoseconds.ToString("R", CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.PassGpuNanoseconds.ToString("R", CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.PassCount.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.ResourceCount.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.DrawCount.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.DescriptorBindCount.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(profile.UploadBytes.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.WriteLine(profile.GpuTimestamps ? "true" : "false");
        }
    }

    private static string FormatNanoseconds(double nanoseconds)
    {
        if (nanoseconds >= 1_000_000_000)
        {
            return $"{nanoseconds / 1_000_000_000:0.###}s";
        }

        if (nanoseconds >= 1_000_000)
        {
            return $"{nanoseconds / 1_000_000:0.###}ms";
        }

        if (nanoseconds >= 1_000)
        {
            return $"{nanoseconds / 1_000:0.###}us";
        }

        return $"{nanoseconds:0.###}ns";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static int ParsePositiveInt(string[] args, string option, int fallback)
    {
        string value = GetOption(args, option, string.Empty);
        return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
    }

    private static int ParseNonNegativeInt(string[] args, string option, int fallback)
    {
        string value = GetOption(args, option, string.Empty);
        return int.TryParse(value, out int parsed) && parsed >= 0 ? parsed : fallback;
    }

    private static string ParsePath(string[] args, string option, string fallback)
    {
        string value = GetOption(args, option, fallback);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{option} requires a non-empty path.", nameof(args))
            : value;
    }

    private static string GetOption(string[] args, string option, string fallback)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return fallback;
    }

    private readonly record struct HeadlessProfile(
        int FrameNumber,
        double BuildNanoseconds,
        double AcquireNanoseconds,
        double RecordNanoseconds,
        double SubmitNanoseconds,
        double FenceNanoseconds,
        double LayoutNanoseconds,
        double PassCpuNanoseconds,
        double PassGpuNanoseconds,
        int PassCount,
        int ResourceCount,
        int DrawCount,
        int DescriptorBindCount,
        ulong UploadBytes,
        bool GpuTimestamps)
    {
        internal static HeadlessProfile From(RenderProfileReport report, int frameNumber, double layoutNanoseconds)
        {
            double passGpuNanoseconds = 0;
            double passCpuNanoseconds = 0;
            foreach (var pass in report.Passes)
            {
                passCpuNanoseconds += pass.CpuRecordDuration.Nanoseconds;
                if (pass.GpuDuration is { } gpuDuration)
                {
                    passGpuNanoseconds += gpuDuration.Nanoseconds;
                }
            }

            var timing = report.Timing;
            var counters = report.Counters;
            return new HeadlessProfile(
                frameNumber,
                timing.Build.Nanoseconds,
                timing.Acquire.Nanoseconds,
                timing.Record.Nanoseconds,
                timing.SubmitAndPresent.Nanoseconds,
                timing.FenceWait.Nanoseconds,
                layoutNanoseconds,
                passCpuNanoseconds,
                passGpuNanoseconds,
                counters.PassCount,
                counters.ResourceCount,
                counters.DrawCallCount,
                counters.DescriptorBindCount,
                counters.UploadBytes,
                report.Capabilities.GpuTimestamps);
        }
    }
}
