using System.Buffers.Binary;
using Delta.Maths;
using Delta.Render;
using Delta.Render.Platform.SDL3;
using Delta.Render.RenderGraph;
using Delta.Render.UIShaders;
using Delta.Render.Vulkan;
using Delta.Shader.Contract;
using Delta.Text;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Samples.TipCalc.Render.Generated;

namespace DeltaXaml.Samples.TipCalc.Render;

internal static class Program
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var interactive = HasOption(args, "--interactive");
            var frameLimit = ParseFrameLimit(args, interactive ? int.MaxValue : 1);
            var factory = new Sdl3WindowFactory();
            var createResult = factory.CreateWindow(new WindowConfiguration("DeltaXAML TipCalc", 480, 360, true, true));
            if (!createResult.Succeeded || createResult.Window is not { } window)
            {
                await Console.Error.WriteLineAsync("Window creation failed.").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(createResult.Diagnostics.ToText()).ConfigureAwait(false);
                return 1;
            }

            await using var windowLease = window.ConfigureAwait(false);
            return await RunWindowAsync(window, frameLimit).ConfigureAwait(false);
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
        await using (var renderer = new VulkanRenderer(new VulkanRendererOptions()))
        {
            using var textService = new SixLaborsTextService();

            var model = new TipCalcModel
            {
                SubTotal = 42,
                PostTaxTotal = 45,
                TipPercent = 15,
            };
            var fonts = new UiFontCatalog();
            var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
            fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
            using var page = new TipCalcArtifact(model, textService, fonts);

            await using (var session = renderer.CreateWindowSession(window))
            {
                var program = LoadPanelProgram();
                var graph = session.CreateRenderGraph();
                var feature = new TipCalcFeature(program, session.Target);
                IRenderFeature[] features = [feature];
                var extent = default(PixelExtent);
                var renderedFrames = 0;

                while (!window.IsClosed && renderedFrames < frameLimit)
                {
                    Sdl3WindowFactory.PumpEvents();
                    var metrics = window.Metrics;
                    var nextExtent = new PixelExtent(metrics.Width, metrics.Height);
                    if (nextExtent != extent)
                    {
                        session.ResizeTarget(in nextExtent);
                        extent = nextExtent;
                    }

                    page.Document.Layout(new float2(metrics.Width, metrics.Height), metrics.DpiScale);
                    var displayList = page.Document.BuildDisplayList();
                    feature.Update(in displayList, metrics);
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
                    $"TipCalc renderer sample: frames={renderedFrames}, " +
                    $"visuals={feature.VisualCount}, clips={feature.ClipCount}, text={feature.TextCount}").ConfigureAwait(false);
                return 0;
            }
        }
    }

    private static IGraphicsShaderProgram LoadPanelProgram()
    {
        const string prefix = "ui-panel";
        var vertexPath = Path.Combine(AppContext.BaseDirectory, "shaders", prefix + ".vert.spv");
        var fragmentPath = Path.Combine(AppContext.BaseDirectory, "shaders", prefix + ".frag.spv");
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
        {
            throw new FileNotFoundException($"UI panel shader fixtures were not found: {vertexPath}, {fragmentPath}");
        }

        return UiPanelGraphicsShaderProgram.CreateProgram(File.ReadAllBytes(vertexPath), File.ReadAllBytes(fragmentPath));
    }

    private static bool HasOption(string[] args, string option)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int ParseFrameLimit(string[] args, int fallback)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (!string.Equals(args[index], "--frames", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(args[index + 1], out var value) || value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(args), "--frames must be a positive integer.");
            }

            return value;
        }

        return fallback;
    }

    private sealed class TipCalcFeature(IGraphicsShaderProgram program, RenderTargetHandle target) : IRenderFeature
    {
        private readonly IGraphicsShaderProgram _program = program ?? throw new ArgumentNullException(nameof(program));
        private readonly RenderTargetHandle _target = target;
        private readonly UiPanelPass _pass = new();
        private UiQuad[] _quads = [];
        private int _quadCount;
        private uint _width;
        private uint _height;

        internal int VisualCount { get; private set; }

        internal int ClipCount { get; private set; }

        internal int TextCount { get; private set; }

        internal void Update(in UiDisplayList displayList, WindowMetrics metrics)
        {
            _width = metrics.Width;
            _height = metrics.Height;
            VisualCount = displayList.Visuals.Length;
            ClipCount = displayList.Clips.Length;
            TextCount = displayList.Text.Length;
            EnsureCapacity(displayList.Visuals.Length);
            _quadCount = 0;

            for (var index = 0; index < displayList.Visuals.Length; index++)
            {
                var visual = displayList.Visuals[index];
                if (visual.Kind is not (UiVisualKind.SolidRectangle or UiVisualKind.RoundedRectangle or UiVisualKind.Border) ||
                    !TryResolveClip(displayList.Clips, visual.Clip, out var clip))
                {
                    continue;
                }

                var bounds = visual.Bounds;
                var color = visual.Color;
                _quads[_quadCount++] = new UiQuad(
                    bounds.x,
                    bounds.y,
                    bounds.z,
                    bounds.w,
                    color.x,
                    color.y,
                    color.z,
                    color.w,
                    clip);
            }
        }

        public void AddPasses(IRenderGraphBuilder graph, ulong frameNumber)
        {
            var target = graph.ImportTarget(_target);
            var pass = graph.AddRasterPass(
                new RasterPassDescription(
                    "tipcalc-ui",
                    new RasterPipelineDescription(_program, cullMode: RasterCullMode.None, blendMode: RenderBlendMode.Alpha)),
                _pass);
            graph.UseColorAttachment(
                pass,
                0,
                new ColorAttachmentDescription(
                    target,
                    AttachmentLoadOperation.Clear,
                    AttachmentStoreOperation.Store,
                    new ClearColor(0.04f, 0.05f, 0.08f, 1f)));
            _pass.Update(_quads, _quadCount, _width, _height);
        }

        private void EnsureCapacity(int required)
        {
            if (_quads.Length >= required)
            {
                return;
            }

            var capacity = Math.Max(4, _quads.Length);
            while (capacity < required)
            {
                capacity = checked(capacity * 2);
            }

            Array.Resize(ref _quads, capacity);
        }

        private static bool TryResolveClip(ReadOnlySpan<UiClipRegion> clips, UiClipId id, out UiClipRect result)
        {
            result = UiClipRect.Unbounded;
            if (!id.IsValid)
            {
                return true;
            }

            if ((uint)id.Value >= (uint)clips.Length)
            {
                return false;
            }

            result = ToClip(clips[id.Value].Bounds);
            var parent = clips[id.Value].Parent;
            var guard = clips.Length;
            while (parent.IsValid && guard-- > 0)
            {
                if ((uint)parent.Value >= (uint)clips.Length)
                {
                    return false;
                }

                result = Intersect(result, ToClip(clips[parent.Value].Bounds));
                parent = clips[parent.Value].Parent;
            }

            return result.IsValid;
        }

        private static UiClipRect ToClip(float4 bounds) => new(bounds.x, bounds.y, bounds.z, bounds.w);

        private static UiClipRect Intersect(UiClipRect left, UiClipRect right)
        {
            if (left.IsUnbounded)
            {
                return right;
            }

            if (right.IsUnbounded)
            {
                return left;
            }

            var x = MathF.Max(left.X, right.X);
            var y = MathF.Max(left.Y, right.Y);
            var r = MathF.Min(left.X + left.Width, right.X + right.Width);
            var b = MathF.Min(left.Y + left.Height, right.Y + right.Height);
            return new UiClipRect(x, y, MathF.Max(0, r - x), MathF.Max(0, b - y));
        }
    }

    private sealed class UiPanelPass : IRasterPass
    {
        private UiQuad[] _quads = [];
        private int _quadCount;
        private uint _width;
        private uint _height;

        internal void Update(UiQuad[] quads, int quadCount, uint width, uint height)
        {
            _quads = quads;
            _quadCount = quadCount;
            _width = width;
            _height = height;
        }

        public void Record(IRasterCommandContext commands)
        {
            var viewport = new RenderViewport(0, 0, _width, _height);
            commands.SetViewport(in viewport);
            Span<byte> constants = stackalloc byte[48];
            constants.Clear();
            var metrics = new WindowMetrics(_width, _height, 1);
            for (var index = 0; index < _quadCount; index++)
            {
                var quad = _quads[index];
                if (!quad.Clip.TryGetScissor(metrics, out var clip))
                {
                    continue;
                }

                var scissor = new PixelRect(clip.X, clip.Y, checked((int)clip.Width), checked((int)clip.Height));
                commands.SetScissor(in scissor);
                WriteFloat(constants, 0, _width);
                WriteFloat(constants, 4, _height);
                WriteFloat(constants, 16, quad.X);
                WriteFloat(constants, 20, _height - quad.Y - quad.Height);
                WriteFloat(constants, 24, quad.Width);
                WriteFloat(constants, 28, quad.Height);
                WriteFloat(constants, 32, quad.Red);
                WriteFloat(constants, 36, quad.Green);
                WriteFloat(constants, 40, quad.Blue);
                WriteFloat(constants, 44, quad.Alpha);
                commands.PushConstants(constants);
                commands.Draw(6);
            }
        }

        private static void WriteFloat(Span<byte> destination, int offset, float value) =>
            BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value);
    }

    private readonly record struct UiQuad(
        float X,
        float Y,
        float Width,
        float Height,
        float Red,
        float Green,
        float Blue,
        float Alpha,
        UiClipRect Clip);

    private readonly record struct UiClipRect(float X, float Y, float Width, float Height)
    {
        internal static UiClipRect Unbounded => new(0, 0, 0, 0);

        internal bool IsUnbounded => Width == 0 && Height == 0;

        internal bool IsValid => IsUnbounded || (Width > 0 && Height > 0);

        internal bool TryGetScissor(WindowMetrics metrics, out PixelRect result)
        {
            var x = IsUnbounded ? 0 : MathF.Max(0, X);
            var y = IsUnbounded ? 0 : MathF.Max(0, Y);
            var right = IsUnbounded ? metrics.Width : MathF.Min(metrics.Width, X + Width);
            var bottom = IsUnbounded ? metrics.Height : MathF.Min(metrics.Height, Y + Height);
            var width = MathF.Floor(right - x);
            var height = MathF.Floor(bottom - y);
            result = new PixelRect((int)MathF.Floor(x), (int)MathF.Floor(y), (int)width, (int)height);
            return !result.IsEmpty;
        }
    }
}
