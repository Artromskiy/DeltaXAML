using DeltaXAML.Abstractions;
namespace DeltaXAML.Core;
public class UiElement : IUiElement
{
    private static uint _nextId; private readonly List<IUiElement> _children = new();
    public UiElement() => Id = new(++_nextId);
    public UiElementId Id { get; } public virtual string TypeName => "Element"; public IUiElement? Parent { get; private set; } public IReadOnlyList<IUiElement> Children => _children;
    public UiVisibility Visibility { get; set; } = UiVisibility.Visible; public float Width { get; set; } = float.NaN; public float Height { get; set; } = float.NaN; public bool Fill { get; set; } public UiRect Bounds { get; protected set; } public UiSize DesiredSize { get; protected set; } public UiColor Background { get; set; }
    public UiDirtyFlags DirtyFlags { get; protected set; } = UiDirtyFlags.Tree|UiDirtyFlags.Layout|UiDirtyFlags.Visual;
    public void Add(IUiElement child) { if (child is not UiElement owned) throw new ArgumentException("Child must be a DeltaXAML element.",nameof(child)); if (owned.Parent is UiElement parent) parent.Remove(owned); owned.Parent=this; _children.Add(owned); Invalidate(UiDirtyFlags.Tree|UiDirtyFlags.Layout|UiDirtyFlags.Visual); }
    public bool Remove(IUiElement child) { if(!_children.Remove(child)) return false; if(child is UiElement owned) owned.Parent=null; Invalidate(UiDirtyFlags.Tree|UiDirtyFlags.Layout|UiDirtyFlags.Visual); return true; }
    public void Invalidate(UiDirtyFlags flags) { DirtyFlags|=flags; if((flags&UiDirtyFlags.Layout)!=0)(Parent as UiElement)?.Invalidate(UiDirtyFlags.Layout); else(Parent as UiElement)?.Invalidate(flags&UiDirtyFlags.Visual); }
    public virtual void Measure(UiSize available) { foreach(var child in _children) child.Measure(available); DesiredSize=new(float.IsNaN(Width)?0:Width,float.IsNaN(Height)?0:Height); }
    public virtual void Arrange(UiRect bounds) { Bounds=bounds; foreach(var child in _children) child.Arrange(bounds); DirtyFlags=UiDirtyFlags.None; }
    public IUiElement? HitTest(UiPoint point) { if(Visibility!=UiVisibility.Visible||!Bounds.Contains(point))return null; for(var i=_children.Count-1;i>=0;i--)if(_children[i] is UiElement c&&c.HitTest(point)is{} hit)return hit; return this; }
}
public class Panel : UiElement, IUiPanel
{
    public override string TypeName=>"Panel";
    public override void Measure(UiSize available) { var size=new UiSize(); foreach(var child in Children){child.Measure(available);size=new(MathF.Max(size.Width,child.DesiredSize.Width),MathF.Max(size.Height,child.DesiredSize.Height));} DesiredSize=new(float.IsNaN(Width)?size.Width:Width,float.IsNaN(Height)?size.Height:Height); }
    public override void Arrange(UiRect bounds) { Bounds=bounds; foreach(var child in Children)child.Arrange(bounds); DirtyFlags=UiDirtyFlags.None; }
}
public sealed class StackPanel : Panel
{
    public override string TypeName=>"StackPanel";
    public UiOrientation Orientation { get; set; } = UiOrientation.Vertical;
    public override void Measure(UiSize available) { var w=0f; var h=0f; foreach(var child in Children){child.Measure(available); if(Orientation==UiOrientation.Horizontal){w+=child.DesiredSize.Width;h=MathF.Max(h,child.DesiredSize.Height);}else{w=MathF.Max(w,child.DesiredSize.Width);h+=child.DesiredSize.Height;}} DesiredSize=new(float.IsNaN(Width)?w:Width,float.IsNaN(Height)?h:Height); }
    public override void Arrange(UiRect bounds) { Bounds=bounds; var cursor=Orientation==UiOrientation.Horizontal?bounds.X:bounds.Y; var fixedSize=0f; var fills=0; foreach(var child in Children){if(child.Fill)fills++;else fixedSize+=Orientation==UiOrientation.Horizontal?child.DesiredSize.Width:child.DesiredSize.Height;} var remaining=MathF.Max(0,(Orientation==UiOrientation.Horizontal?bounds.Width:bounds.Height)-fixedSize); foreach(var child in Children){var s=child.DesiredSize; var main=child.Fill&&fills>0?remaining/fills:(Orientation==UiOrientation.Horizontal?s.Width:s.Height); UiRect rect=Orientation==UiOrientation.Horizontal?new UiRect(cursor,bounds.Y,main,bounds.Height):new UiRect(bounds.X,cursor,bounds.Width,main); child.Arrange(rect); cursor+=main;} DirtyFlags=UiDirtyFlags.None; }
}
