using Delta.Render;
using Delta.Render.RenderGraph;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal sealed class ReadbackFeature(RenderTargetHandle target, PixelExtent extent) : IRenderFeature
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
