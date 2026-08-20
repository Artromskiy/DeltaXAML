using DeltaXAML.Abstractions;
namespace DeltaXAML.Core;

public sealed class ComponentInspector:ScrollViewer
{
    private readonly StackPanel _rows=new(){Orientation=UiOrientation.Vertical};private IInspectorItemSource? _source;private UiElement[] _rowCache=Array.Empty<UiElement>();
    public ComponentInspector(){Content=_rows;}
    public IUiElement Rows => _rows;
    public IInspectorItemSource? ItemSource{get=>_source;set{if(_source is not null)_source.Changed-=Refresh;_source=value;if(_source is not null)_source.Changed+=Refresh;Refresh();}}
    public void Refresh(){if(_source is null){_rows.ClearChildren();return;}while(_rowCache.Length<_source.Count)Array.Resize(ref _rowCache,Math.Max(_source.Count,Math.Max(4,_rowCache.Length*2)));for(var i=0;i<_source.Count;i++){var row=_rowCache[i]??=new InspectorRow();if(!_rows.Contains(row))_rows.Add(row);((InspectorRow)row).Apply(_source.Get(i));}while(_rows.Children.Count>_source.Count)_rows.Remove(_rows.Children[^1]);Invalidate(UiDirtyFlags.Tree|UiDirtyFlags.Measure|UiDirtyFlags.Visual);}
}
public sealed class InspectorRow:Border
{
    private readonly TextBlock _label=new();private readonly TextBlock _value=new(); public InspectorRow(){Padding=new(4,2,4,2);var panel=new StackPanel{Orientation=UiOrientation.Horizontal};panel.Add(_label);panel.Add(_value);Add(panel);}
    public InspectorFieldRecord Record{get;private set;}
    public void Apply(InspectorFieldRecord record){Record=record;_label.Text=record.Label;_value.Text=record.ValueText;}
}
internal static class PanelExtensions
{
    public static void ClearChildren(this UiElement element){foreach(var child in element.Children.ToArray())element.Remove(child);}
    public static bool Contains(this UiElement element,UiElement child)=>element.Children.Contains(child);
}
