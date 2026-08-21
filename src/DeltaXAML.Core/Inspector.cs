using DeltaXAML.Abstractions;

namespace DeltaXAML.Core;

public sealed class ComponentInspector:ContentControl
{
    private readonly Grid _shell=new();
    private readonly StackPanel _header=new(){Orientation=UiOrientation.Horizontal};
    private readonly ScrollViewer _scroll=new();
    private readonly StackPanel _rows=new(){Orientation=UiOrientation.Vertical};
    private readonly Dictionary<string,InspectorRow> _rowByKey=new(StringComparer.Ordinal);
    private readonly List<string> _activeKeys=new();
    private IInspectorItemSource? _source;
    public ComponentInspector()
    {
        var root=new StackPanel{Orientation=UiOrientation.Vertical};
        var title=new TextBlock{Text="Component Inspector",FontSize=18,Foreground=new(235,239,247)};
        var subtitle=new TextBlock{Text="Live retained component view",FontSize=12,Foreground=new(159,170,186)};
        _header.Add(title);
        _header.Add(subtitle);
        _scroll.Content=_rows;
        _shell.SetColumns(GridLength.Fixed(160),GridLength.Star());
        _shell.SetRows(GridLength.Auto,GridLength.Star());
        _shell.Add(_header);
        _shell.Add(_scroll);
        Content=root;
    }
    public IUiElement Rows=>_rows;
    public IInspectorItemSource? ItemSource
    {
        get=>_source;
        set
        {
            if(_source is not null)_source.Changed-=OnSourceChanged;
            _source=value;
            if(_source is not null)_source.Changed+=OnSourceChanged;
            Refresh(new InspectorItemSourceChange(InspectorItemSourceChangeKind.Reset,0,_source?.Count??0));
        }
    }
    public void Refresh()=>Refresh(new InspectorItemSourceChange(InspectorItemSourceChangeKind.Reset,0,_source?.Count??0));
    private void OnSourceChanged(InspectorItemSourceChange change)=>Refresh(change);
    private void Refresh(InspectorItemSourceChange change)
    {
        if(_source is null){_rows.ClearChildren();_rowByKey.Clear();_activeKeys.Clear();return;}
        switch(change.Kind)
        {
            case InspectorItemSourceChangeKind.Reset:
                SyncAll();
                break;
            case InspectorItemSourceChangeKind.Add:
            case InspectorItemSourceChangeKind.Remove:
            case InspectorItemSourceChangeKind.Change:
                SyncRange(change.Index,change.Count);
                break;
        }
        CleanupRemovedFocus(change);
        Invalidate(UiDirtyFlags.Tree|UiDirtyFlags.Measure|UiDirtyFlags.Visual);
    }
    private void SyncAll()
    {
        _activeKeys.Clear();
        for(var i=0;i<_source!.Count;i++)ApplyRecord(i);
        Trim(_source.Count);
    }
    private void SyncRange(int index,int count)
    {
        if(index<0)index=0;
        if(count<0)count=0;
        switch(count)
        {
            case 0:
                break;
            default:
                for(var i=index;i<Math.Min(_source!.Count,index+count);i++)ApplyRecord(i);
                if(index==0&&_activeKeys.Count==0)SyncAll();
                break;
        }
        Trim(_source!.Count);
    }
    private void ApplyRecord(int index)
    {
        var record=_source!.Get(index);
        var key=$"{record.ComponentKey}:{record.FieldKey}";
        if(!_rowByKey.TryGetValue(key,out var row))
        {
            row=new InspectorRow();
            _rowByKey[key]=row;
            _rows.Add(row);
        }
        row.Apply(record);
        if(index>=_activeKeys.Count)_activeKeys.Add(key);
        else _activeKeys[index]=key;
    }
    private void Trim(int count)
    {
        while(_activeKeys.Count>count)
        {
            var removed=_activeKeys[^1];
            _activeKeys.RemoveAt(_activeKeys.Count-1);
            if(_rowByKey.Remove(removed,out var row))_rows.Remove(row);
        }
    }
    private void CleanupRemovedFocus(InspectorItemSourceChange change)
    {
        if(change.Kind!=InspectorItemSourceChangeKind.Remove||change.Count==0)return;
        if(Parent is not UiElement parent)return;
        var frameRoot=parent;
        while(frameRoot.Parent is UiElement up)frameRoot=up;
    }
}

public sealed class InspectorRow:Border
{
    private readonly StackPanel _grid=new(){Orientation=UiOrientation.Horizontal};
    private readonly TextBlock _label=new();
    private readonly TextBlock _value=new();
    private readonly TextBox _editor=new(){Focusable=true};
    private readonly NumericEditor _numeric=new(){Focusable=true};
    public InspectorRow()
    {
        Padding=new UiThickness(6,4,6,4);
        _grid.Add(_label);
        _grid.Add(_value);
        _grid.Add(_editor);
        _grid.Add(_numeric);
        Add(_grid);
    }
    public InspectorFieldRecord Record{get;private set;}
    public IUiElement ActiveEditor=>Record.EditorKind.Equals("Numeric",StringComparison.OrdinalIgnoreCase)?_numeric:_editor;
    public void Apply(InspectorFieldRecord record)
    {
        Record=record;
        _label.Text=record.Label;
        _value.Text=record.ValueText;
        _editor.Text=record.ValueText;
        _numeric.Initialize(ParseNumeric(record.ValueText));
        _editor.Visibility=record.EditorKind.Equals("Numeric",StringComparison.OrdinalIgnoreCase)?UiVisibility.Collapsed:UiVisibility.Visible;
        _numeric.Visibility=record.EditorKind.Equals("Numeric",StringComparison.OrdinalIgnoreCase)?UiVisibility.Visible:UiVisibility.Collapsed;
    }
    private static double ParseNumeric(string text)=>double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:0;
}
