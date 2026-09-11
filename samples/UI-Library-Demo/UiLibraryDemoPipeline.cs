using Delta;
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
    private readonly GraphicsShaderProgram _linearGradientProgram = UiLibraryDemoShaders.LinearGradient();
    private readonly UiDisplayListResourceRegistry _registry;
    private readonly UiResourceCatalog _resources;
    private readonly TextRenderFeature _textFeature;
    private readonly IRenderGraph _graph;
    private readonly bool _withReadback;
    private int _clipCount;
    private UiDisplayListGraphFeature _uiFeature;
    private ClearFeature _clearFeature;
    private ReadbackFeature? _readbackFeature;
    private IRenderFeature[] _features;
    private ulong _registeredEffectVersion;

    internal UiLibraryDemoPipeline(
        IRenderFrameSession session,
        ITextService textService,
        PixelExtent extent,
        bool withReadback,
        UiResourceCatalog resources)
    {
        _session = session;
        _withReadback = withReadback;
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _registry = CreateRegistry(resources);
        _registeredEffectVersion = resources.EffectVersion;
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
        RefreshVisualEffects();
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
            linearGradientVisualProgram: _linearGradientProgram,
            registry: _registry);

    private IRenderFeature[] CreateFeatures() => _readbackFeature is null
            ? [_clearFeature, _uiFeature]
            : [_clearFeature, _uiFeature, _readbackFeature];

    private UiDisplayListResourceRegistry CreateRegistry(UiResourceCatalog resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var registry = new UiDisplayListResourceRegistry();
        foreach (var resourceId in UiLibraryDemoResources.Gradients)
        {
            if (!resources.TryResolve(resourceId, out var value) || value is not UiLinearGradient gradient)
            {
                throw new InvalidOperationException($"UI library demo gradient resource '{resourceId.Value}' is missing or has an invalid payload.");
            }

            var sourceStops = gradient.Stops.Span;
            var stops = new UiLinearGradientStop[sourceStops.Length];
            for (var index = 0; index < stops.Length; index++)
            {
                var stop = sourceStops[index];
                var color = stop.Color;
                stops[index] = new(
                    stop.Offset,
                    new float4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
            }

            registry.RegisterLinearGradient(new UiLinearGradientResource(
                resourceId,
                new float2(gradient.StartX, gradient.StartY),
                new float2(gradient.EndX, gradient.EndY),
                PaintUnits.Logical,
                stops)
            {
                IsRelativeToBounds = gradient.IsRelativeToBounds,
                AngleDegrees = gradient.AngleDegrees,
                OutlineColor = new float4(
                    gradient.OutlineColor.R / 255f,
                    gradient.OutlineColor.G / 255f,
                    gradient.OutlineColor.B / 255f,
                    gradient.OutlineColor.A / 255f),
                OutlineWidth = gradient.OutlineWidth,
            });
        }
        RegisterVisualEffects(resources.GetEffectResources(), registry);

        return registry;
    }

    private void RefreshVisualEffects()
    {
        var version = _resources.EffectVersion;
        if (version == _registeredEffectVersion)
        {
            return;
        }

        RegisterVisualEffects(_resources.GetEffectResources(), _registry);
        _registeredEffectVersion = version;
    }

    private void RegisterVisualEffects(UiEffectResource[] effects, UiDisplayListResourceRegistry registry)
    {
        var solid = new UiVisualShaderVariant(
            _solidStrokeProgram,
            UiVisualKind.SolidRectangle,
            UiVisualShaderPath.SolidStrokeEffect);
        var rounded = new UiVisualShaderVariant(
            _roundedStrokeProgram,
            UiVisualKind.RoundedRectangle,
            UiVisualShaderPath.RoundedStrokeEffect);
        foreach (var effect in effects)
        {
            if (effect.Set.Target == UiEffectTarget.Visual &&
                effect.Set.Quality == UiEffectQuality.Analytic &&
                effect.Set.Capabilities == UiEffectCapabilities.Stroke)
            {
                registry.RegisterVisualEffectResource(effect, solid);
                registry.RegisterVisualEffectResource(effect, rounded);
            }
        }
    }
}
