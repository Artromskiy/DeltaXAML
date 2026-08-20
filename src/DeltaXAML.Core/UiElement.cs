using System.Globalization;
using DeltaXAML.Abstractions;

namespace DeltaXAML.Core;

public sealed class UiValue : IUiValue
{
    public UiValue(object? value,UiValueSource source,UiDirtyFlags invalidation){UntypedValue=value;Source=source;Invalidation=invalidation;}
    public object? UntypedValue{get;} public UiValueSource Source{get;} public UiDirtyFlags Invalidation{get;}
}
public sealed class UiBindingValue : IUiBinding
{
    private readonly Func<object?> _read; private readonly Func<object?,(bool Success,string? Error)> _write;
    public UiBindingValue(Func<object?> read,Func<object?,(bool Success,string? Error)> write){_read=read;_write=write;}
    public object? Read()=>_read(); public bool TryWrite(object? value,out string? error){var r=_write(value);error=r.Error;return r.Success;} public event Action? Changed; public void NotifyChanged()=>Changed?.Invoke();
}
public sealed class UiPropertyStore : IUiPropertyStore
{
    private readonly Dictionary<string,IUiValue> _values=new(StringComparer.Ordinal); private readonly Dictionary<string,IUiBinding> _bindings=new(StringComparer.Ordinal); private readonly Dictionary<string,Action> _bindingHandlers=new(StringComparer.Ordinal); private readonly UiElement _owner;
    public UiPropertyStore(UiElement owner)=>_owner=owner;
    public void SetLocal(string name,object? value,UiDirtyFlags invalidation){RemoveBinding(name);_values[name]=new UiValue(value,UiValueSource.Local,invalidation);_owner.Invalidate(invalidation);}
    public void SetStyle(string name,object? value,UiDirtyFlags invalidation){if(!_values.TryGetValue(name,out var old)||old.Source!=UiValueSource.Local){_values[name]=new UiValue(value,UiValueSource.Style,invalidation);_owner.Invalidate(invalidation);}}
    public void SetBinding(string name,IUiBinding binding,UiDirtyFlags invalidation){RemoveBinding(name);_bindings[name]=binding;_values[name]=new UiValue(binding.Read(),UiValueSource.Binding,invalidation);Action handler=()=>{_values[name]=new UiValue(binding.Read(),UiValueSource.Binding,invalidation);_owner.Invalidate(invalidation);};_bindingHandlers[name]=handler;binding.Changed+=handler;_owner.Invalidate(invalidation);}
    public bool TryGet(string name,out IUiValue value)=>_values.TryGetValue(name,out value!);
    private void RemoveBinding(string name){if(_bindings.Remove(name,out var binding)&&_bindingHandlers.Remove(name,out var handler))binding.Changed-=handler;}
}

public class UiElement : IUiElement, IUiPropertyStore
{
    private static uint _nextId; private readonly List<IUiElement> _children=new(); private readonly UiPropertyStore _properties;
    public UiElement(){Id=new(++_nextId);_properties=new(this);}
    public UiElementId Id{get;} public virtual string TypeName=>"Element"; public IUiElement? Parent{get;private set;} public IReadOnlyList<IUiElement> Children=>_children;
    public UiVisibility Visibility{get;set;}=UiVisibility.Visible; public bool Focusable{get;set;} public float Width{get;set;}=float.NaN; public float Height{get;set;}=float.NaN; public bool Fill{get;set;}
    public UiRect Bounds{get;protected set;} public UiRect Clip{get;protected set;} public UiSize DesiredSize{get;protected set;} public UiColor Background{get;set;}
    public UiThickness Margin { get; set; } public UiThickness Padding { get; set; } public UiDirtyFlags DirtyFlags { get; protected set; } = UiDirtyFlags.Tree|UiDirtyFlags.Measure|UiDirtyFlags.Visual;
    public void Add(IUiElement child){if(child is not UiElement owned)throw new ArgumentException("Child must be a DeltaXAML element.",nameof(child));if(owned.Parent is UiElement p)p.Remove(owned);owned.Parent=this;_children.Add(owned);Invalidate(UiDirtyFlags.Tree|UiDirtyFlags.Measure|UiDirtyFlags.Visual);}
    public bool Remove(IUiElement child){if(!_children.Remove(child))return false;if(child is UiElement owned)owned.Parent=null;Invalidate(UiDirtyFlags.Tree|UiDirtyFlags.Measure|UiDirtyFlags.Visual);return true;}
    public void Invalidate(UiDirtyFlags flags){DirtyFlags|=flags;if((flags&(UiDirtyFlags.Measure|UiDirtyFlags.Arrange))!=0)(Parent as UiElement)?.Invalidate(UiDirtyFlags.Measure);else if((flags&UiDirtyFlags.Visual)!=0)(Parent as UiElement)?.Invalidate(UiDirtyFlags.Visual);}
    public virtual void Measure(UiSize available){foreach(var child in _children)child.Measure(available);DesiredSize=RequestedSize(new(0,0));DirtyFlags&=~UiDirtyFlags.Measure;}
    public virtual void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;foreach(var child in _children)child.Arrange(bounds);DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
    protected UiSize RequestedSize(UiSize measured)=>new(float.IsNaN(Width)?measured.Width:Width,float.IsNaN(Height)?measured.Height:Height);
    public IUiElement? HitTest(UiPoint point){if(Visibility!=UiVisibility.Visible||!Clip.Contains(point))return null;for(var i=_children.Count-1;i>=0;i--)if(_children[i] is UiElement c&&c.HitTest(point)is{} hit)return hit;return this;}
    public void SetLocal(string name,object? value,UiDirtyFlags invalidation)=>_properties.SetLocal(name,value,invalidation); public void SetStyle(string name,object? value,UiDirtyFlags invalidation)=>_properties.SetStyle(name,value,invalidation); public void SetBinding(string name,IUiBinding binding,UiDirtyFlags invalidation)=>_properties.SetBinding(name,binding,invalidation); public bool TryGet(string name,out IUiValue value)=>_properties.TryGet(name,out value);
}
public class Panel:UiElement,IUiPanel
{
    public override string TypeName=>"Panel";
    public override void Measure(UiSize available){var w=0f;var h=0f;foreach(var c in Children){c.Measure(available);w=MathF.Max(w,c.DesiredSize.Width);h=MathF.Max(h,c.DesiredSize.Height);}DesiredSize=RequestedSize(new(w,h));DirtyFlags&=~UiDirtyFlags.Measure;}
    public override void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;foreach(var c in Children)c.Arrange(bounds);DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
}
public sealed class StackPanel:Panel
{
    public override string TypeName=>"StackPanel"; public UiOrientation Orientation{get;set;}=UiOrientation.Vertical;
    public override void Measure(UiSize available){var w=0f;var h=0f;foreach(var c in Children){c.Measure(available);if(Orientation==UiOrientation.Horizontal){w+=c.DesiredSize.Width;h=MathF.Max(h,c.DesiredSize.Height);}else{w=MathF.Max(w,c.DesiredSize.Width);h+=c.DesiredSize.Height;}}DesiredSize=RequestedSize(new(w,h));DirtyFlags&=~UiDirtyFlags.Measure;}
    public override void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;var cursor=Orientation==UiOrientation.Horizontal?bounds.X:bounds.Y;var fixedSize=0f;var fillCount=0;foreach(var c in Children){if(c.Fill)fillCount++;else fixedSize+=Orientation==UiOrientation.Horizontal?c.DesiredSize.Width:c.DesiredSize.Height;}var remaining=MathF.Max(0,(Orientation==UiOrientation.Horizontal?bounds.Width:bounds.Height)-fixedSize);foreach(var c in Children){var s=c.DesiredSize;var main=c.Fill&&fillCount>0?remaining/fillCount:(Orientation==UiOrientation.Horizontal?s.Width:s.Height);c.Arrange(Orientation==UiOrientation.Horizontal?new UiRect(cursor,bounds.Y,main,bounds.Height):new UiRect(bounds.X,cursor,bounds.Width,main));cursor+=main;}DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
}
public class Border:UiElement
{
    public override string TypeName=>"Border"; public IUiElement? Child=>Children.Count==0?null:Children[0];
    public override void Measure(UiSize available){if(Child is not null){Child.Measure(new(MathF.Max(0,available.Width-Padding.Horizontal),MathF.Max(0,available.Height-Padding.Vertical)));DesiredSize=RequestedSize(new(Child.DesiredSize.Width+Padding.Horizontal,Child.DesiredSize.Height+Padding.Vertical));}else DesiredSize=RequestedSize(new(Padding.Horizontal,Padding.Vertical));DirtyFlags&=~UiDirtyFlags.Measure;}
    public override void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;Child?.Arrange(new(bounds.X+Padding.Left,bounds.Y+Padding.Top,MathF.Max(0,bounds.Width-Padding.Horizontal),MathF.Max(0,bounds.Height-Padding.Vertical)));DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
}
public readonly record struct GridLength(float Value,GridUnitType Type){public static GridLength Fixed(float v)=>new(v,GridUnitType.Pixel);public static GridLength Auto=>new(1,GridUnitType.Auto);public static GridLength Star(float weight=1)=>new(weight,GridUnitType.Star);}
public enum GridUnitType{Pixel,Auto,Star}
public sealed class Grid:UiElement
{
    private GridLength[] _columns=Array.Empty<GridLength>();private GridLength[] _rows=Array.Empty<GridLength>();private float[] _measuredColumns=Array.Empty<float>();private float[] _measuredRows=Array.Empty<float>();private float[] _resolvedColumns=Array.Empty<float>();private float[] _resolvedRows=Array.Empty<float>(); public override string TypeName=>"Grid"; public int ColumnCount=>_columns.Length;public int RowCount=>_rows.Length;
    public void SetColumns(params GridLength[] columns){_columns=columns;Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Arrange);} public void SetRows(params GridLength[] rows){_rows=rows;Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Arrange);}
    public override void Measure(UiSize available){foreach(var c in Children)c.Measure(available);AutoSizes(_columns,true,ref _measuredColumns);AutoSizes(_rows,false,ref _measuredRows);DesiredSize=RequestedSize(new(Sum(_measuredColumns,_columns.Length),Sum(_measuredRows,_rows.Length)));DirtyFlags&=~UiDirtyFlags.Measure;}
    public override void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;var cols=Resolve(_columns,bounds.Width,_measuredColumns,ref _resolvedColumns);var rows=Resolve(_rows,bounds.Height,_measuredRows,ref _resolvedRows);for(var i=0;i<Children.Count;i++){var col=i%Math.Max(1,cols.Length);var row=i/Math.Max(1,cols.Length);if(row>=rows.Length)break;var x=bounds.X+Sum(cols,col);var y=bounds.Y+Sum(rows,row);Children[i].Arrange(new(x,y,cols[col],rows[row]));}DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
    private void AutoSizes(GridLength[] defs,bool columns,ref float[] result){if(defs.Length==0)return;Ensure(ref result,defs.Length);Array.Clear(result,0,defs.Length);for(var i=0;i<defs.Length;i++)if(defs[i].Type==GridUnitType.Pixel)result[i]=defs[i].Value;else if(defs[i].Type==GridUnitType.Auto)for(var child=0;child<Children.Count;child++)if((columns?child%defs.Length:child/defs.Length)==i)result[i]=MathF.Max(result[i],columns?Children[child].DesiredSize.Width:Children[child].DesiredSize.Height);}
    private static float[] Resolve(GridLength[] defs,float available,float[] measured,ref float[] result){if(defs.Length==0){Ensure(ref result,1);result[0]=available;return result;}Ensure(ref result,defs.Length);Array.Clear(result,0,defs.Length);var rest=available;var stars=0f;for(var i=0;i<defs.Length;i++){if(defs[i].Type==GridUnitType.Pixel)result[i]=defs[i].Value;else if(defs[i].Type==GridUnitType.Auto)result[i]=measured.Length>i?measured[i]:0;else stars+=defs[i].Value;rest-=result[i];}if(stars>0)for(var i=0;i<defs.Length;i++)if(defs[i].Type==GridUnitType.Star)result[i]=MathF.Max(0,rest)*defs[i].Value/stars;return result;}
    private static void Ensure(ref float[] values,int count){if(values.Length<count)Array.Resize(ref values,Math.Max(count,Math.Max(4,values.Length*2)));}
    private static float Sum(float[] values,int count){var total=0f;for(var i=0;i<count;i++)total+=values[i];return total;}
}
public class ContentControl:UiElement
{
    public override string TypeName=>"ContentControl"; public IUiElement? Content{get=>Children.Count==0?null:Children[0];set{foreach(var c in Children.ToArray())Remove(c);if(value is not null)Add(value);}}
    public override void Measure(UiSize available){Content?.Measure(available);DesiredSize=RequestedSize(Content?.DesiredSize??new());DirtyFlags&=~UiDirtyFlags.Measure;} public override void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;Content?.Arrange(bounds);DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
}
public class Button:ContentControl,IUiRoutedEventSink
{
    public override string TypeName=>"Button"; public Button(){Focusable=true;} public event Action? Click; public bool IsPressed{get;private set;} public void OnRoutedEvent(in UiRoutedEvent e){if(e.Phase==UiRoutedEventPhase.Bubble&&e.Kind==UiPointerEventKind.Down)IsPressed=true;if(e.Phase==UiRoutedEventPhase.Bubble&&e.Kind==UiPointerEventKind.Up){IsPressed=false;Click?.Invoke();}}
}
public sealed class ToggleButton:Button{public bool IsChecked{get;private set;}public new void OnRoutedEvent(in UiRoutedEvent e){base.OnRoutedEvent(e);if(e.Phase==UiRoutedEventPhase.Bubble&&e.Kind==UiPointerEventKind.Up)IsChecked=!IsChecked;}}
public class TextBlock:UiElement
{
    public override string TypeName=>"TextBlock"; public string Text{get;set;}="";public string FontKey{get;set;}="default";public string GlyphRunKey{get;set;}="default";public float FontSize{get;set;}=14;public UiColor Foreground{get;set;}=new(255,255,255);
    public override void Measure(UiSize available){DesiredSize=RequestedSize(new(MathF.Min(available.Width,Text.Length*FontSize*.55f),FontSize*1.25f));DirtyFlags&=~UiDirtyFlags.Measure;}
}
public class TextBox:TextBlock
{
    public override string TypeName=>"TextBox"; public int CaretIndex{get;private set;} public event Action<string>? TextChanged;
    public bool ApplyText(in UiTextInput input){Text=Text.Insert(CaretIndex,input.Text);CaretIndex+=input.Text.Length;Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Visual);TextChanged?.Invoke(Text);return true;}
    public bool ApplyKey(in UiKeyEvent input){if(!input.IsDown)return false;if(input.PhysicalKey==8&&CaretIndex>0){Text=Text.Remove(--CaretIndex,1);Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Visual);TextChanged?.Invoke(Text);return true;}return false;}
}
public sealed class NumericEditor:TextBox
{
    public override string TypeName=>"NumericEditor"; public double Value { get; private set; } public double Min { get; set; } = double.MinValue; public double Max { get; set; } = double.MaxValue;
    public bool TryCommit(){if(!double.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)||v<Min||v>Max)return false;Value=v;return true;}
}
public class ScrollViewer:ContentControl
{
    public override string TypeName=>"ScrollViewer"; public UiPoint Offset{get;private set;} public void ScrollBy(float x,float y){Offset=new(MathF.Max(0,Offset.X+x),MathF.Max(0,Offset.Y+y));Invalidate(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
    public override void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;Content?.Arrange(new(bounds.X-Offset.X,bounds.Y-Offset.Y,Content.DesiredSize.Width,Content.DesiredSize.Height));DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
}
