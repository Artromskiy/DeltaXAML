using DeltaXAML.Abstractions;
namespace DeltaXAML.Core;

public sealed class UiFrame:IUiFrame
{
    private readonly DrawList _drawList=new();private readonly UiInputRouter _input;
    public UiFrame(IUiElement root){Root=root;_input=new(this);} public IUiElement Root{get;} public IUiInputRouter Input=>_input;
    public void ApplyMutations(){}
    public void Layout(UiSize viewport,float dpiScale){Root.Measure(viewport);Root.Arrange(new(0,0,viewport.Width,viewport.Height));}
    public IUiDrawList ExtractDrawList(in UiFrameContext context){_drawList.Build(Root,new(0,0,context.Viewport.Width,context.Viewport.Height));return _drawList;}
}
internal sealed class DrawList:IUiDrawList
{
    private UiDrawCommand[] _commands=Array.Empty<UiDrawCommand>();private UiClipEntry[] _clips=Array.Empty<UiClipEntry>();private UiTextRun[] _textRuns=Array.Empty<UiTextRun>();private int _commandCount,_clipCount,_textCount;
    public ReadOnlyMemory<UiDrawCommand> Commands=>_commands.AsMemory(0,_commandCount);public ReadOnlyMemory<UiClipEntry> Clips=>_clips.AsMemory(0,_clipCount);public ReadOnlyMemory<UiTextRun> TextRuns=>_textRuns.AsMemory(0,_textCount);public uint Version{get;private set;}
    public void Build(IUiElement root,UiRect clip){_commandCount=0;_clipCount=0;_textCount=0;Version++;Visit(root,clip,new(0));}
    private void Visit(IUiElement e,UiRect clip,UiClipId parent){if(e.Visibility!=UiVisibility.Visible)return;var effective=UiRect.Intersect(clip,e.Bounds);var id=new UiClipId((uint)_clipCount+1);EnsureClip();_clips[_clipCount++]=new(id,effective,parent);if(e.Background.A>0){EnsureCommand();_commands[_commandCount]=new(UiDrawKind.Rectangle,e.Bounds,effective,id,default,e.Background,null,0,(uint)_commandCount,e.Id);_commandCount++;}if(e is TextBlock text){EnsureText();_textRuns[_textCount++]=new(text.FontKey,text.FontSize,text.Text,text.GlyphRunKey,text.Foreground,e.Bounds,effective,e.Id);}foreach(var child in e.Children)Visit(child,effective,id);}
    private void EnsureCommand(){if(_commandCount<_commands.Length)return;Array.Resize(ref _commands,Math.Max(8,_commands.Length*2));}private void EnsureClip(){if(_clipCount<_clips.Length)return;Array.Resize(ref _clips,Math.Max(8,_clips.Length*2));}private void EnsureText(){if(_textCount<_textRuns.Length)return;Array.Resize(ref _textRuns,Math.Max(8,_textRuns.Length*2));}
}

public sealed class UiInputRouter:IUiInputRouter
{
    private readonly UiFrame _frame;private UiElement? _focused,_captured,_hovered;
    public UiInputRouter(UiFrame frame)=>_frame=frame;public UiElementId? Focused=>_focused?.Id;public UiElementId? Captured=>_captured?.Id;
    public void Focus(UiElementId? element){_focused=element is null?null:Find(_frame.Root,element.Value);}
    public void RoutePointer(in UiPointerEvent input){var target=_captured??FindHit(_frame.Root,input.Position);if(input.Kind==UiPointerEventKind.Move)_hovered=target;if(input.Kind==UiPointerEventKind.Down){_captured=target;_focused=target;}if(target is not null){Raise(target,new UiRoutedEvent(target.Id,UiRoutedEventPhase.Preview,input.Kind,input.Position));Raise(target,new UiRoutedEvent(target.Id,UiRoutedEventPhase.Bubble,input.Kind,input.Position));}if(input.Kind==UiPointerEventKind.Up)_captured=null;}
    public void RouteKey(in UiKeyEvent input){if(!input.IsDown)return;if(input.PhysicalKey==9){FocusNext();return;}if(_focused is TextBox text)text.ApplyKey(input);}
    public void RouteText(in UiTextInput input){if(_focused is TextBox text)text.ApplyText(input);}
    public void RouteIme(in UiImeComposition input){if(input.IsCommitted)RouteText(new UiTextInput(input.Text));}
    private void FocusNext(){var focusable=new List<UiElement>();CollectFocusable(_frame.Root,focusable);var index=_focused is null?-1:focusable.IndexOf(_focused);_focused=focusable.Count==0?null:focusable[(index+1)%focusable.Count];}
    private static void CollectFocusable(IUiElement element,List<UiElement> result){if(element is UiElement e&&e.Focusable)result.Add(e);foreach(var child in element.Children)CollectFocusable(child,result);}
    private static void Raise(UiElement target,in UiRoutedEvent e){var path=new List<UiElement>();for(UiElement? node=target;node is not null;node=node.Parent as UiElement)path.Add(node);if(e.Phase==UiRoutedEventPhase.Preview){for(var i=path.Count-1;i>=0;i--)if(path[i] is IUiRoutedEventSink preview)preview.OnRoutedEvent(e);}else if(e.Phase==UiRoutedEventPhase.Bubble){foreach(var node in path)if(node is IUiRoutedEventSink bubble)bubble.OnRoutedEvent(e);}}
    private static UiElement? FindHit(IUiElement root,UiPoint p)=>root is UiElement e?e.HitTest(p) as UiElement:null;
    private static UiElement? Find(IUiElement root,UiElementId id){if(root.Id==id)return root as UiElement;foreach(var child in root.Children)if(Find(child,id)is{} found)return found;return null;}
}
