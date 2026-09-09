using Delta.Render;
using Delta.Render.RenderGraph;
using Delta.Render.Text;
using Delta.Render.XAML;
using Delta.Shader.Contract;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal sealed class UiLibraryDemoPipeline : IAsyncDisposable
{
    private readonly IRenderFrameSession _session;
    private readonly GraphicsShaderProgram _solidProgram = UiLibraryDemoShaders.SolidRectangle();
    private readonly GraphicsShaderProgram _roundedProgram = UiLibraryDemoShaders.RoundedRectangle();
    private readonly GraphicsShaderProgram _solidStrokeProgram = UiLibraryDemoShaders.SolidStrokeRectangle();
    private readonly GraphicsShaderProgram _roundedStrokeProgram = UiLibraryDemoShaders.RoundedStrokeRectangle();
    private readonly UiDisplayListResourceRegistry _registry;
    private readonly TextRenderFeature _textFeature;
    private readonly IRenderGraph _graph;
    private readonly bool _withReadback;
    private int _clipCount;
    private UiDisplayListGraphFeature _uiFeature;
    private ClearFeature _clearFeature;
    private ReadbackFeature? _readbackFeature;
    private IRenderFeature[] _features;

    internal UiLibraryDemoPipeline(
        IRenderFrameSession session,
        ITextService textService,
        PixelExtent extent,
        bool withReadback,
        UiResourceCatalog resources)
    {
        _session = session;
        _withReadback = withReadback;
        _registry = CreateRegistry(resources);
        _textFeature = new TextRenderFeature(session, textService, UiLibraryDemoShaders.Text(), extent);
        _graph = session.CreateRenderGraph();
        _uiFeature = CreateUiFeature(extent);
        _clearFeature = new ClearFeature(_session.Target, extent, _solidProgram);
        _readbackFeature = _withReadback ? new ReadbackFeature(_session.Target, extent) : null;
        _features = CreateFeatures();
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

        diagnostics = $"Graph execution failed: {result.Status}{Environment.NewLine}" +
                      string.Join(Environment.NewLine, result.Diagnostics.ToArray());
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
            roundedSliceVisualProgram: _roundedProgram,
            registry: _registry);

    private IRenderFeature[] CreateFeatures() => _readbackFeature is null
            ? [_clearFeature, _uiFeature]
            : [_clearFeature, _uiFeature, _readbackFeature];

    private UiDisplayListResourceRegistry CreateRegistry(UiResourceCatalog resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var registry = new UiDisplayListResourceRegistry();
        var solid = new UiVisualShaderVariant(
            _solidStrokeProgram,
            UiVisualKind.SolidRectangle,
            UiVisualShaderPath.SolidStrokeEffect);
        var rounded = new UiVisualShaderVariant(
            _roundedStrokeProgram,
            UiVisualKind.RoundedRectangle,
            UiVisualShaderPath.RoundedStrokeEffect);
        foreach (var effect in resources.GetEffectResources())
        {
            if (effect.Set.Target == UiEffectTarget.Visual &&
                effect.Set.Quality == UiEffectQuality.Analytic &&
                effect.Set.Capabilities == UiEffectCapabilities.Stroke)
            {
                registry.RegisterVisualEffectResource(effect, solid);
                registry.RegisterVisualEffectResource(effect, rounded);
            }
        }

        return registry;
    }
}
