namespace DeltaXAML.Abstractions;

[Flags] public enum UiDirtyFlags { None=0, Tree=1, Style=2, Binding=4, Measure=8, Arrange=16, Visual=32, HitTest=64, Resource=128 }
public enum UiVisibility { Visible, Hidden, Collapsed }
public enum UiOrientation { Horizontal, Vertical }
public enum UiValueSource { Default, Local, Style, Binding }
public readonly record struct UiSize(float Width,float Height);
public readonly record struct UiPoint(float X,float Y);
public readonly record struct UiThickness(float Left,float Top,float Right,float Bottom) { public static UiThickness Zero=>default; public float Horizontal=>Left+Right; public float Vertical=>Top+Bottom; }
public readonly record struct UiRect(float X,float Y,float Width,float Height)
{
    public bool Contains(UiPoint p)=>p.X>=X&&p.Y>=Y&&p.X<=X+Width&&p.Y<=Y+Height;
    public bool IsInside(UiRect other)=>X>=other.X&&Y>=other.Y&&X+Width<=other.X+other.Width&&Y+Height<=other.Y+other.Height;
    public static UiRect Intersect(UiRect a,UiRect b){var x=MathF.Max(a.X,b.X);var y=MathF.Max(a.Y,b.Y);var r=MathF.Min(a.X+a.Width,b.X+b.Width);var z=MathF.Min(a.Y+a.Height,b.Y+b.Height);return new(x,y,MathF.Max(0,r-x),MathF.Max(0,z-y));}
}
public readonly record struct UiColor(byte R,byte G,byte B,byte A=255);
public readonly record struct UiElementId(uint Value) { public bool IsValid=>Value!=0; }
public readonly record struct UiResourceHandle(ulong Value,uint Generation);

public interface IUiElement
{
    UiElementId Id { get; } string TypeName { get; } IUiElement? Parent { get; } IReadOnlyList<IUiElement> Children { get; }
    UiVisibility Visibility { get; } bool Focusable { get; } float Width { get; } float Height { get; } bool Fill { get; }
    UiRect Bounds { get; } UiRect Clip { get; } UiSize DesiredSize { get; } UiColor Background { get; } UiDirtyFlags DirtyFlags { get; }
    void Measure(UiSize available); void Arrange(UiRect bounds);
}
public interface IUiPanel:IUiElement { void Add(IUiElement child); bool Remove(IUiElement child); }
public interface IUiValue
{
    object? UntypedValue { get; } UiValueSource Source { get; } UiDirtyFlags Invalidation { get; }
}
public interface IUiBinding
{
    object? Read(); bool TryWrite(object? value,out string? error); event Action? Changed;
}
public interface IUiPropertyStore
{
    void SetLocal(string name,object? value,UiDirtyFlags invalidation); void SetStyle(string name,object? value,UiDirtyFlags invalidation);
    void SetBinding(string name,IUiBinding binding,UiDirtyFlags invalidation); bool TryGet(string name,out IUiValue value);
}

public enum UiDrawKind:byte { Rectangle,TextRun,Image,Viewport }
public readonly record struct UiClipId(uint Value);
public readonly record struct UiClipEntry(UiClipId Id,UiRect Bounds,UiClipId Parent);
public readonly record struct UiTextRun(string FontKey,float FontSize,string Text,string GlyphRunKey,UiColor Color,UiRect Bounds,UiRect Clip,UiElementId Owner);
public readonly record struct UiDrawCommand(UiDrawKind Kind,UiRect Bounds,UiRect Clip,UiClipId ClipId,UiResourceHandle Resource,UiColor Color,string? Text,int ZIndex,uint Order,UiElementId Owner);
public interface IUiDrawList
{
    ReadOnlyMemory<UiDrawCommand> Commands { get; } ReadOnlyMemory<UiClipEntry> Clips { get; } ReadOnlyMemory<UiTextRun> TextRuns { get; } uint Version { get; }
}
public readonly record struct UiFrameContext(UiSize Viewport,float DpiScale,uint FrameNumber);
public interface IUiFrame { IUiElement Root { get; } void ApplyMutations(); void Layout(UiSize viewport,float dpiScale); IUiDrawList ExtractDrawList(in UiFrameContext context); }

public enum UiPointerEventKind:byte { Move,Down,Up,Wheel }
public readonly record struct UiPointerEvent(UiPointerEventKind Kind,UiPoint Position,int Button=0,float WheelDelta=0);
public readonly record struct UiKeyEvent(int PhysicalKey,bool IsDown,bool IsRepeat=false,bool Shift=false,bool Control=false,bool Alt=false,bool Meta=false);
public readonly record struct UiTextInput(string Text);
public readonly record struct UiImeComposition(string Text,int SelectionStart,int SelectionLength,bool IsCommitted);
public enum UiRoutedEventPhase:byte { Preview, Bubble }
public readonly record struct UiRoutedEvent(UiElementId Target,UiRoutedEventPhase Phase,UiPointerEventKind Kind,UiPoint Position);
public interface IUiInputRouter
{
    UiElementId? Focused { get; } UiElementId? Captured { get; }
    void RoutePointer(in UiPointerEvent input); void RouteKey(in UiKeyEvent input); void RouteText(in UiTextInput input); void RouteIme(in UiImeComposition input); void Focus(UiElementId? element);
}
public interface IUiRoutedEventSink { void OnRoutedEvent(in UiRoutedEvent routedEvent); }

public readonly record struct InspectorFieldRecord(string ComponentKey,string FieldKey,string Label,string ValueText,string EditorKind,bool IsReadOnly=false);
public interface IInspectorItemSource { int Count { get; } InspectorFieldRecord Get(int index); bool TryCommit(int index,string text,out string? error); event Action? Changed; }
