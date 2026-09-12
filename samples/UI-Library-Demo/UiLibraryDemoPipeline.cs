using Delta;
using Delta.Render;
using Delta.Render.RenderGraph;
using Delta.Render.Text;
using Delta.Render.UI;
using Delta.Render.XAML;
using Delta.Shader.Contract;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal sealed class UiLibraryDemoPipeline : IAsyncDisposable
{
    private static readonly string[] GradientKeys =
    [
        "SpectrumBrush",
        "SelectionBrush",
        "SoftSpectrumBrush",
        "OrangeRedBrush",
        "MagentaVioletBrush",
        "CyanGreenBrush",
    ];

    private readonly IRenderFrameSession _session;
    private readonly GraphicsShaderProgram _solidProgram = UiLibraryDemoShaders.SolidRectangle();
    private readonly GraphicsShaderProgram _roundedProgram = UiLibraryDemoShaders.RoundedRectangle();
    private readonly GraphicsShaderProgram _solidStrokeProgram = UiLibraryDemoShaders.SolidStrokeRectangle();
    private readonly GraphicsShaderProgram _roundedStrokeProgram = UiLibraryDemoShaders.RoundedStrokeRectangle();
    private readonly GraphicsShaderProgram _roundedInnerEffectProgram = UiLibraryDemoShaders.RoundedInnerEffect();
    private readonly GraphicsShaderProgram _linearGradientProgram = UiLibraryDemoShaders.LinearGradient();
    private readonly UiDisplayListResourceRegistry _registry;
    private readonly UiResourceCatalog _resources;
    private readonly Func<string, UiResourceId> _resolveResourceId;
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
        UiResourceCatalog resources,
        Func<string, UiResourceId> resolveResourceId)
    {
        _session = session;
        _withReadback = withReadback;
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _resolveResourceId = resolveResourceId ?? throw new ArgumentNullException(nameof(resolveResourceId));
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
        foreach (var key in GradientKeys)
        {
            var resourceId = _resolveResourceId(key);
            if (!resources.TryResolve(resourceId, out var brushValue) ||
                brushValue is not UiBrush brush ||
                !resources.TryResolve(brush.Resource, out var gradientValue))
            {
                throw new InvalidOperationException($"UI library demo gradient resource '{key}' is missing.");
            }

            switch (brush.Kind)
            {
                case UiBrushKind.LinearGradient when gradientValue is UiLinearGradient linear:
                    UiLinearGradientAdapter.RegisterLinearGradient(registry, brush.Resource, linear);
                    break;
                case UiBrushKind.RadialGradient when gradientValue is UiRadialGradient radial:
                    UiLinearGradientAdapter.RegisterRadialGradient(registry, brush.Resource, radial);
                    break;
                default:
                    throw new InvalidOperationException($"UI library demo gradient resource '{key}' has an invalid payload.");
            }
        }
        RegisterEffects(resources.GetEffectResources(), registry);

        return registry;
    }

    private void RefreshVisualEffects()
    {
        var version = _resources.EffectVersion;
        if (version == _registeredEffectVersion)
        {
            return;
        }

        RegisterEffects(_resources.GetEffectResources(), _registry);
        _registeredEffectVersion = version;
    }

    private void RegisterEffects(UiEffectResource[] effects, UiDisplayListResourceRegistry registry)
    {
        RegisterTextEffects(effects, registry);

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

            if (effect.Set.Target == UiEffectTarget.Visual &&
                effect.Set.Quality == UiEffectQuality.Analytic &&
                effect.Set.Capabilities is UiEffectCapabilities.InnerShadow or UiEffectCapabilities.InnerGlow)
            {
                var path = effect.Set.Capabilities == UiEffectCapabilities.InnerShadow
                    ? UiVisualShaderPath.InnerShadowEffect
                    : UiVisualShaderPath.InnerGlowEffect;
                registry.RegisterVisualEffectResource(
                    effect,
                    new UiVisualShaderVariant(_roundedInnerEffectProgram, UiVisualKind.SolidRectangle, path));
                registry.RegisterVisualEffectResource(
                    effect,
                    new UiVisualShaderVariant(_roundedInnerEffectProgram, UiVisualKind.RoundedRectangle, path));
            }
        }
    }

    private static void RegisterTextEffects(UiEffectResource[] effects, UiDisplayListResourceRegistry registry)
    {
        const UiEffectCapabilities supported = UiEffectCapabilities.Stroke |
            UiEffectCapabilities.OuterShadow | UiEffectCapabilities.InnerShadow |
            UiEffectCapabilities.OuterGlow | UiEffectCapabilities.InnerGlow;

        foreach (var effect in effects)
        {
            if (effect.Set.Target != UiEffectTarget.Text ||
                effect.Set.Quality != UiEffectQuality.Analytic ||
                effect.Set.Capabilities == UiEffectCapabilities.None ||
                (effect.Set.Capabilities & ~supported) != UiEffectCapabilities.None)
            {
                continue;
            }

            var capabilities = effect.Set.Capabilities;
            registry.RegisterTextEffectResourceAllLayers(
                effect,
                new TextShaderVariant(
                    capabilities.HasFlag(UiEffectCapabilities.Stroke)
                        ? UiLibraryDemoShaders.TextStroke()
                        : UiLibraryDemoShaders.Text(),
                    GlyphImageMode.Sdf,
                    capabilities.HasFlag(UiEffectCapabilities.Stroke)
                        ? TextShaderPath.Stroke
                        : TextShaderPath.Standard),
                capabilities.HasFlag(UiEffectCapabilities.OuterShadow)
                    ? new TextShaderVariant(UiLibraryDemoShaders.TextOuterShadow(), GlyphImageMode.Sdf, TextShaderPath.OuterShadow)
                    : null,
                capabilities.HasFlag(UiEffectCapabilities.OuterGlow)
                    ? new TextShaderVariant(UiLibraryDemoShaders.TextOuterGlowOnly(), GlyphImageMode.Sdf, TextShaderPath.OuterGlowOnly)
                    : null,
                capabilities.HasFlag(UiEffectCapabilities.InnerShadow)
                    ? new TextShaderVariant(UiLibraryDemoShaders.TextInnerShadow(), GlyphImageMode.Sdf, TextShaderPath.InnerShadow)
                    : null,
                capabilities.HasFlag(UiEffectCapabilities.InnerGlow)
                    ? new TextShaderVariant(UiLibraryDemoShaders.TextInnerGlowOnly(), GlyphImageMode.Sdf, TextShaderPath.InnerGlowOnly)
                    : null);
        }
    }
}
