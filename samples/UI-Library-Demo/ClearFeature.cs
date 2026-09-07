using Delta.Render;
using Delta.Render.RenderGraph;
using Delta.Shader.Contract;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal sealed class ClearFeature(RenderTargetHandle target, PixelExtent extent, GraphicsShaderProgram program) : IRenderFeature
{
    private readonly RenderTargetHandle _target = target;
    private readonly GraphicsShaderProgram _program = program;
    private readonly ClearPass _pass = new(extent);

    public void AddPasses(IRenderGraphBuilder graph, ulong frameNumber)
    {
        var target = graph.ImportTarget(_target);
        var pass = graph.AddRasterPass(
            new RasterPassDescription(
                "DeltaRender.UI.LibraryDemo.Clear",
                new RasterPipelineDescription(_program, cullMode: RasterCullMode.None)),
            _pass);
        graph.UseColorAttachment(
            pass,
            0,
            new ColorAttachmentDescription(
                target,
                AttachmentLoadOperation.Clear,
                AttachmentStoreOperation.Store,
                new ClearColor(0, 0, 0, 1)));
    }
}

internal sealed class ClearPass(PixelExtent extent) : IRasterPass
{
    private readonly RenderViewport _viewport = new(0, 0, extent.Width, extent.Height);
    private readonly PixelRect _scissor = new(0, 0, checked((int)extent.Width), checked((int)extent.Height));

    public void Record(IRasterCommandContext commands)
    {
        commands.SetViewport(in _viewport);
        commands.SetScissor(in _scissor);
    }
}
