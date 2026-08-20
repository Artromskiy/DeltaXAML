using DeltaXAML.Abstractions;
namespace DeltaXAML.Core;
public sealed class UiFrame : IUiFrame
{
    private readonly DrawList _drawList=new(); public UiFrame(IUiElement root)=>Root=root; public IUiElement Root{get;}
    public void ApplyMutations(){} public void Layout(UiSize viewport,float dpiScale){Root.Measure(viewport);Root.Arrange(new(0,0,viewport.Width,viewport.Height));}
    public IUiDrawList ExtractDrawList(in UiFrameContext context){_drawList.Build(Root,new(0,0,context.Viewport.Width,context.Viewport.Height));return _drawList;}
}
internal sealed class DrawList:IUiDrawList
{
    private UiDrawCommand[] _commands=Array.Empty<UiDrawCommand>(); private UiClipEntry[] _clips=Array.Empty<UiClipEntry>(); private int _commandCount; private int _clipCount; public ReadOnlyMemory<UiDrawCommand> Commands=>_commands.AsMemory(0,_commandCount); public ReadOnlyMemory<UiClipEntry> Clips=>_clips.AsMemory(0,_clipCount); public uint Version{get;private set;}
    public void Build(IUiElement root,UiRect clip){_commandCount=0;_clipCount=0;Version++;Visit(root,clip,new(0));}
    private void Visit(IUiElement e,UiRect clip,UiClipId parent){if(e.Visibility!=UiVisibility.Visible)return;var effective=UiRect.Intersect(clip,e.Bounds);var id=new UiClipId((uint)_clipCount+1);EnsureClipCapacity();_clips[_clipCount++]=new(id,effective,parent);if(e.Background.A>0){EnsureCommandCapacity();_commands[_commandCount]=new(UiDrawKind.Rectangle,e.Bounds,effective,id,default,e.Background,null,0,(uint)_commandCount);_commandCount++;}foreach(var child in e.Children)Visit(child,effective,id);}
    private void EnsureCommandCapacity(){if(_commandCount<_commands.Length)return;Array.Resize(ref _commands,Math.Max(4,_commands.Length*2));}
    private void EnsureClipCapacity(){if(_clipCount<_clips.Length)return;Array.Resize(ref _clips,Math.Max(4,_clips.Length*2));}
}
