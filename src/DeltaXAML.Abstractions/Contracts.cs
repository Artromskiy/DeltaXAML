namespace DeltaXAML.Abstractions;
[Flags] public enum UiDirtyFlags { None=0, Tree=1, Style=2, Layout=4, HitTest=8, Visual=16, Resource=32 }
public enum UiVisibility { Visible, Hidden, Collapsed }
public readonly record struct UiSize(float Width,float Height);
public readonly record struct UiPoint(float X,float Y);
public readonly record struct UiRect(float X,float Y,float Width,float Height) { public bool Contains(UiPoint p)=>p.X>=X&&p.Y>=Y&&p.X<=X+Width&&p.Y<=Y+Height; public static UiRect Intersect(UiRect a,UiRect b){var x=MathF.Max(a.X,b.X);var y=MathF.Max(a.Y,b.Y);var r=MathF.Min(a.X+a.Width,b.X+b.Width);var z=MathF.Min(a.Y+a.Height,b.Y+b.Height);return new(x,y,MathF.Max(0,r-x),MathF.Max(0,z-y));} }
public readonly record struct UiColor(byte R,byte G,byte B,byte A=255);
public readonly record struct UiElementId(uint Value) { public bool IsValid=>Value!=0; }
public interface IUiElement { UiElementId Id{get;} string TypeName{get;} IUiElement? Parent{get;} IReadOnlyList<IUiElement> Children{get;} UiVisibility Visibility{get;} float Width{get;} float Height{get;} bool Fill{get;} UiRect Bounds{get;} UiSize DesiredSize{get;} UiColor Background{get;} UiDirtyFlags DirtyFlags{get;} void Measure(UiSize available); void Arrange(UiRect bounds); }
public interface IUiPanel:IUiElement { void Add(IUiElement child); bool Remove(IUiElement child); }
public enum UiOrientation { Horizontal, Vertical }
public enum UiDrawKind:byte { Rectangle,Text,Image,Viewport }
public readonly record struct UiClipId(uint Value);
public readonly record struct UiResourceHandle(ulong Value,uint Generation);
public readonly record struct UiClipEntry(UiClipId Id,UiRect Bounds,UiClipId Parent);
public readonly record struct UiDrawCommand(UiDrawKind Kind,UiRect Bounds,UiRect Clip,UiClipId ClipId,UiResourceHandle Resource,UiColor Color,string? Text,int ZIndex,uint Order);
public interface IUiDrawList { ReadOnlyMemory<UiDrawCommand> Commands{get;} ReadOnlyMemory<UiClipEntry> Clips{get;} uint Version{get;} }
public readonly record struct UiFrameContext(UiSize Viewport,float DpiScale,uint FrameNumber);
public interface IUiFrame { IUiElement Root{get;} void ApplyMutations(); void Layout(UiSize viewport,float dpiScale); IUiDrawList ExtractDrawList(in UiFrameContext context); }
