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

public sealed class UiClipboard : IUiClipboard
{
    public string? Text { get; private set; }
    public string? GetText()=>Text;
    public void SetText(string? text)=>Text=text;
    public bool HasText=>!string.IsNullOrEmpty(Text);
}

public sealed class UiPropertyStore : IUiPropertyStore
{
    private readonly Dictionary<string,IUiValue> _values=new(StringComparer.Ordinal);
    private readonly Dictionary<string,IUiBinding> _bindings=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Action> _bindingHandlers=new(StringComparer.Ordinal);
    private readonly UiElement _owner;
    public UiPropertyStore(UiElement owner)=>_owner=owner;
    public void SetLocal(string name,object? value,UiDirtyFlags invalidation){RemoveBinding(name);_values[name]=new UiValue(value,UiValueSource.Local,invalidation);_owner.Invalidate(invalidation);}
    public void SetStyle(string name,object? value,UiDirtyFlags invalidation){if(!_values.TryGetValue(name,out var old)||old.Source!=UiValueSource.Local){_values[name]=new UiValue(value,UiValueSource.Style,invalidation);_owner.Invalidate(invalidation);}}
    public void SetBinding(string name,IUiBinding binding,UiDirtyFlags invalidation){RemoveBinding(name);_bindings[name]=binding;_values[name]=new UiValue(binding.Read(),UiValueSource.Binding,invalidation);Action handler=()=>{_values[name]=new UiValue(binding.Read(),UiValueSource.Binding,invalidation);_owner.Invalidate(invalidation);};_bindingHandlers[name]=handler;binding.Changed+=handler;_owner.Invalidate(invalidation);}
    public bool TryGet(string name,out IUiValue value)=>_values.TryGetValue(name,out value!);
    private void RemoveBinding(string name){if(_bindings.Remove(name,out var binding)&&_bindingHandlers.Remove(name,out var handler))binding.Changed-=handler;}
}

public class UiElement : IUiElement, IUiPropertyStore
{
    private static uint _nextId;
    private readonly List<IUiElement> _children=new();
    private readonly UiPropertyStore _properties;
    public UiElement(){Id=new(++_nextId);_properties=new(this);}
    public UiElementId Id{get;} public virtual string TypeName=>"Element"; public IUiElement? Parent{get;private set;} public IReadOnlyList<IUiElement> Children=>_children;
    public UiVisibility Visibility{get;set;}=UiVisibility.Visible; public bool Focusable{get;set;} public float Width{get;set;}=float.NaN; public float Height{get;set;}=float.NaN; public bool Fill{get;set;}
    public UiRect Bounds{get;protected set;} public UiRect Clip{get;protected set;} public UiSize DesiredSize{get;protected set;} public UiColor Background{get;set;}
    public bool IsEnabled{get;set;}=true; public bool IsHovered{get;private set;} public bool IsPressed{get;protected set;} public bool IsSelected{get;set;} public bool IsInvalid{get;protected set;} public string? StyleKey{get;set;} public string? TemplateKey{get;set;} public string? AutomationName{get;set;} public string AutomationRole{get;set;}="generic";
    public UiThickness Margin { get; set; } public UiThickness Padding { get; set; } public UiDirtyFlags DirtyFlags{get;protected set;}=UiDirtyFlags.Tree|UiDirtyFlags.Measure|UiDirtyFlags.Visual;
    public UiAutomationMetadata Automation=>new(AutomationName??TypeName,AutomationRole,GetAutomationValueText(),IsEnabled,IsInvalid);
    public UiStateSnapshot VisualState=>new(IsEnabled?IsInvalid?UiVisualState.Invalid:IsPressed?UiVisualState.Pressed:IsHovered?UiVisualState.Hover:IsSelected?UiVisualState.Selected:IsFocused?UiVisualState.Focused:UiVisualState.Normal:UiVisualState.Disabled,IsEnabled,IsInvalid,IsSelected,IsFocused,IsHovered,IsPressed);
    public bool IsFocused{get;private set;}
    public void Add(IUiElement child){if(child is not UiElement owned)throw new ArgumentException("Child must be a DeltaXAML element.",nameof(child));if(owned.Parent is UiElement parent)parent.Remove(owned);owned.Parent=this;_children.Add(owned);Invalidate(UiDirtyFlags.Tree|UiDirtyFlags.Measure|UiDirtyFlags.Visual);}
    public bool Remove(IUiElement child){if(!_children.Remove(child))return false;if(child is UiElement owned)owned.Parent=null;Invalidate(UiDirtyFlags.Tree|UiDirtyFlags.Measure|UiDirtyFlags.Visual);return true;}
    public void ClearChildren(){foreach(var child in _children.ToArray())Remove(child);}
    public void Invalidate(UiDirtyFlags flags){DirtyFlags|=flags;if((flags&(UiDirtyFlags.Measure|UiDirtyFlags.Arrange))!=0)(Parent as UiElement)?.Invalidate(UiDirtyFlags.Measure);else if((flags&UiDirtyFlags.Visual)!=0)(Parent as UiElement)?.Invalidate(UiDirtyFlags.Visual);}
    public void SetHovered(bool value){if(IsHovered!=value){IsHovered=value;Invalidate(UiDirtyFlags.Visual);}}
    public void SetPressed(bool value){if(IsPressed!=value){IsPressed=value;Invalidate(UiDirtyFlags.Visual);}}
    public void SetFocused(bool value){if(IsFocused!=value){IsFocused=value;Invalidate(UiDirtyFlags.Visual);}}
    public void SetInvalid(bool value){if(IsInvalid!=value){IsInvalid=value;Invalidate(UiDirtyFlags.Visual);}}
    public virtual void Measure(UiSize available){foreach(var child in _children)child.Measure(available);DesiredSize=RequestedSize(new(0,0));DirtyFlags&=~UiDirtyFlags.Measure;}
    public virtual void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;foreach(var child in _children)child.Arrange(bounds);DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
    protected UiSize RequestedSize(UiSize measured)=>new(float.IsNaN(Width)?measured.Width:Width,float.IsNaN(Height)?measured.Height:Height);
    public IUiElement? HitTest(UiPoint point){if(Visibility!=UiVisibility.Visible||!Clip.Contains(point))return null;for(var i=_children.Count-1;i>=0;i--)if(_children[i] is UiElement c&&c.HitTest(point)is{} hit)return hit;return this;}
    public void SetLocal(string name,object? value,UiDirtyFlags invalidation)=>_properties.SetLocal(name,value,invalidation); public void SetStyle(string name,object? value,UiDirtyFlags invalidation)=>_properties.SetStyle(name,value,invalidation); public void SetBinding(string name,IUiBinding binding,UiDirtyFlags invalidation)=>_properties.SetBinding(name,binding,invalidation); public bool TryGet(string name,out IUiValue value)=>_properties.TryGet(name,out value);
    protected virtual string GetAutomationValueText()=>string.Empty;
    protected virtual string GetTextRunKey()=>TypeName;
    protected virtual bool HasTextRun=>false;
    protected virtual string GetTextRunText()=>string.Empty;
    protected virtual UiColor GetTextRunColor()=>new(255,255,255);
    protected virtual float GetTextRunFontSize()=>14;
    protected virtual string GetTextRunFontKey()=> "default";
    public bool TryGetTextRun(out UiTextRun run)
    {
        if(!HasTextRun){run=default;return false;}
        run=new UiTextRun(GetTextRunFontKey(),GetTextRunFontSize(),GetTextRunText(),GetTextRunKey(),GetTextRunColor(),Bounds,Clip,Id,0);
        return true;
    }
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

public class Border:UiElement,IUiPanel
{
    public override string TypeName=>"Border"; public IUiElement? Child=>Children.Count==0?null:Children[0];
    public override void Measure(UiSize available){if(Child is not null){Child.Measure(new(MathF.Max(0,available.Width-Padding.Horizontal),MathF.Max(0,available.Height-Padding.Vertical)));DesiredSize=RequestedSize(new(Child.DesiredSize.Width+Padding.Horizontal,Child.DesiredSize.Height+Padding.Vertical));}else DesiredSize=RequestedSize(new(Padding.Horizontal,Padding.Vertical));DirtyFlags&=~UiDirtyFlags.Measure;}
    public override void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;Child?.Arrange(new(bounds.X+Padding.Left,bounds.Y+Padding.Top,MathF.Max(0,bounds.Width-Padding.Horizontal),MathF.Max(0,bounds.Height-Padding.Vertical)));DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
}

public readonly record struct GridLength(float Value,GridUnitType Type){public static GridLength Fixed(float v)=>new(v,GridUnitType.Pixel);public static GridLength Auto=>new(1,GridUnitType.Auto);public static GridLength Star(float weight=1)=>new(weight,GridUnitType.Star);}
public enum GridUnitType{Pixel,Auto,Star}

public sealed class Grid:UiElement
{
    private GridLength[] _columns=Array.Empty<GridLength>();
    private GridLength[] _rows=Array.Empty<GridLength>();
    private float[] _measuredColumns=Array.Empty<float>();
    private float[] _measuredRows=Array.Empty<float>();
    private float[] _resolvedColumns=Array.Empty<float>();
    private float[] _resolvedRows=Array.Empty<float>();
    public override string TypeName=>"Grid"; public int ColumnCount=>_columns.Length;public int RowCount=>_rows.Length;
    public void SetColumns(params GridLength[] columns){_columns=columns;Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Arrange);}
    public void SetRows(params GridLength[] rows){_rows=rows;Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Arrange);}
    public override void Measure(UiSize available){foreach(var c in Children)c.Measure(available);AutoSizes(_columns,true,ref _measuredColumns);AutoSizes(_rows,false,ref _measuredRows);DesiredSize=RequestedSize(new(Sum(_measuredColumns,_columns.Length),Sum(_measuredRows,_rows.Length)));DirtyFlags&=~UiDirtyFlags.Measure;}
    public override void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;var cols=Resolve(_columns,bounds.Width,_measuredColumns,ref _resolvedColumns);var rows=Resolve(_rows,bounds.Height,_measuredRows,ref _resolvedRows);for(var i=0;i<Children.Count;i++){var col=i%Math.Max(1,cols.Length);var row=i/Math.Max(1,cols.Length);if(row>=rows.Length)break;var x=bounds.X+Sum(cols,col);var y=bounds.Y+Sum(rows,row);Children[i].Arrange(new(x,y,cols[col],rows[row]));}DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
    private void AutoSizes(GridLength[] defs,bool columns,ref float[] result){if(defs.Length==0)return;Ensure(ref result,defs.Length);Array.Clear(result,0,defs.Length);for(var i=0;i<defs.Length;i++)if(defs[i].Type==GridUnitType.Pixel)result[i]=defs[i].Value;else if(defs[i].Type==GridUnitType.Auto)for(var child=0;child<Children.Count;child++)if((columns?child%defs.Length:child/defs.Length)==i)result[i]=MathF.Max(result[i],columns?Children[child].DesiredSize.Width:Children[child].DesiredSize.Height);}
    private static float[] Resolve(GridLength[] defs,float available,float[] measured,ref float[] result){if(defs.Length==0){Ensure(ref result,1);result[0]=available;return result;}Ensure(ref result,defs.Length);Array.Clear(result,0,defs.Length);var rest=available;var stars=0f;for(var i=0;i<defs.Length;i++){if(defs[i].Type==GridUnitType.Pixel)result[i]=defs[i].Value;else if(defs[i].Type==GridUnitType.Auto)result[i]=measured.Length>i?measured[i]:0;else stars+=defs[i].Value;rest-=result[i];}if(stars>0)for(var i=0;i<defs.Length;i++)if(defs[i].Type==GridUnitType.Star)result[i]=MathF.Max(0,rest)*defs[i].Value/stars;return result;}
    private static void Ensure(ref float[] values,int count){if(values.Length<count)Array.Resize(ref values,Math.Max(count,Math.Max(4,values.Length*2)));}
    private static float Sum(float[] values,int count){var total=0f;for(var i=0;i<count;i++)total+=values[i];return total;}
}

public class ContentControl:UiElement
{
    public override string TypeName=>"ContentControl"; public IUiElement? Content{get=>Children.Count==0?null:Children[0];set{ClearChildren();if(value is not null)Add(value);}}
    public override void Measure(UiSize available){Content?.Measure(available);DesiredSize=RequestedSize(Content?.DesiredSize??new());DirtyFlags&=~UiDirtyFlags.Measure;}
    public override void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;Content?.Arrange(bounds);DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
}

public class Button:ContentControl,IUiRoutedEventSink
{
    public override string TypeName=>"Button"; public Button(){Focusable=true;AutomationRole="button";} public event Action? Click;
    public virtual void OnRoutedEvent(in UiRoutedEvent e){if(e.Phase==UiRoutedEventPhase.Bubble&&e.Kind==UiPointerEventKind.Down)SetPressed(true);if(e.Phase==UiRoutedEventPhase.Bubble&&e.Kind==UiPointerEventKind.Up){SetPressed(false);Click?.Invoke();}}
    protected override string GetAutomationValueText()=>Content is TextBlock t?t.Text:string.Empty;
}

public sealed class ToggleButton:Button
{
    public bool IsChecked{get;private set;}
    public override void OnRoutedEvent(in UiRoutedEvent e){base.OnRoutedEvent(e);if(e.Phase==UiRoutedEventPhase.Bubble&&e.Kind==UiPointerEventKind.Up)IsChecked=!IsChecked;}
}

public class TextBlock:UiElement
{
    public override string TypeName=>"TextBlock"; public string Text{get;set;}="";public string FontKey{get;set;}="default";public string GlyphRunKey{get;set;}="default";public float FontSize{get;set;}=14;public UiColor Foreground{get;set;}=new(255,255,255);
    public override void Measure(UiSize available){DesiredSize=RequestedSize(new(MathF.Min(available.Width,Text.Length*FontSize*.55f),FontSize*1.25f));DirtyFlags&=~UiDirtyFlags.Measure;}
    protected override string GetAutomationValueText()=>Text;
    protected override bool HasTextRun=>true;
    protected override string GetTextRunKey()=>GlyphRunKey;
    protected override string GetTextRunText()=>Text;
    protected override UiColor GetTextRunColor()=>Foreground;
    protected override float GetTextRunFontSize()=>FontSize;
    protected override string GetTextRunFontKey()=>FontKey;
}

public class TextBox:TextBlock
{
    private readonly List<string> _undo=new();
    private readonly List<string> _redo=new();
    public override string TypeName=>"TextBox"; public int CaretIndex{get;private set;} public int SelectionStart{get;private set;} public int SelectionLength{get;private set;} public string? Diagnostic{get;protected set;} public IUiClipboard? Clipboard{get;set;} public event Action<string>? TextChanged;
    public void SetText(string text,bool recordUndo=true)
    {
        if(recordUndo)PushUndo();
        Text=text;
        CaretIndex=Math.Min(CaretIndex,Text.Length);
        SelectionLength=0;
        Diagnostic=null;
        SetInvalid(false);
        Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Visual);
        TextChanged?.Invoke(Text);
    }
    public bool ApplyText(in UiTextInput input){ReplaceSelection(input.Text);return true;}
    public virtual bool ApplyKey(in UiKeyEvent input)
    {
        if(!input.IsDown)return false;
        if(input.Control)return ApplyCommand(input.PhysicalKey);
        if(input.PhysicalKey==8&&CaretIndex>0){PushUndo();DeleteRange(CaretIndex-1,1);return true;}
        if(input.PhysicalKey==46&&CaretIndex<Text.Length){PushUndo();DeleteRange(CaretIndex,1);return true;}
        if(input.PhysicalKey==37&&CaretIndex>0){CaretIndex--;SelectionLength=0;return true;}
        if(input.PhysicalKey==39&&CaretIndex<Text.Length){CaretIndex++;SelectionLength=0;return true;}
        return false;
    }
    public void SelectAll(){SelectionStart=0;SelectionLength=Text.Length;CaretIndex=Text.Length;}
    public void Copy(){if(Clipboard is not null&&HasSelection())Clipboard.SetText(GetSelection());}
    public void Cut(){if(!HasSelection())return;PushUndo();if(Clipboard is not null)Clipboard.SetText(GetSelection());DeleteRange(SelectionStart,SelectionLength);}
    public bool Paste(){if(Clipboard?.GetText() is not { Length: > 0 } text)return false;ReplaceSelection(text);return true;}
    public bool Undo(){if(_undo.Count==0)return false;_redo.Add(Text);Text=_undo[^1];_undo.RemoveAt(_undo.Count-1);CaretIndex=Text.Length;SelectionLength=0;Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Visual);TextChanged?.Invoke(Text);return true;}
    public bool Redo(){if(_redo.Count==0)return false;_undo.Add(Text);Text=_redo[^1];_redo.RemoveAt(_redo.Count-1);CaretIndex=Text.Length;SelectionLength=0;Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Visual);TextChanged?.Invoke(Text);return true;}
    protected void ReplaceSelection(string inserted)
    {
        PushUndo();
        if(HasSelection())DeleteRange(SelectionStart,SelectionLength,false);
        Text=Text.Insert(CaretIndex,inserted);
        CaretIndex+=inserted.Length;
        SelectionStart=CaretIndex;SelectionLength=0;
        Diagnostic=null;
        SetInvalid(false);
        Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Visual);
        TextChanged?.Invoke(Text);
    }
    private bool ApplyCommand(int physicalKey)
    {
        return physicalKey switch
        {
            65 => SelectAllCommand(),
            67 => CopyCommand(),
            88 => CutCommand(),
            86 => PasteCommand(),
            90 => UndoCommand(),
            89 => RedoCommand(),
            _ => false
        };
        bool SelectAllCommand(){SelectAll();return true;}
        bool CopyCommand(){Copy();return true;}
        bool CutCommand(){Cut();return true;}
        bool PasteCommand(){return Paste();}
        bool UndoCommand(){return Undo();}
        bool RedoCommand(){return Redo();}
    }
    private void PushUndo(){_undo.Add(Text);_redo.Clear();}
    private void DeleteRange(int start,int length,bool record=true){if(record)PushUndo();Text=Text.Remove(start,length);CaretIndex=start;SelectionStart=start;SelectionLength=0;Invalidate(UiDirtyFlags.Measure|UiDirtyFlags.Visual);TextChanged?.Invoke(Text);}
    private bool HasSelection()=>SelectionLength>0;
    private string GetSelection()=>Text.Substring(SelectionStart,SelectionLength);
    protected override string GetAutomationValueText()=>Text;
    protected override string GetTextRunKey()=>GlyphRunKey+":"+Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class NumericEditor:TextBox
{
    private string _committedText="";
    public override string TypeName=>"NumericEditor"; public double Value{get;private set;} public double Min{get;set;}=double.MinValue; public double Max{get;set;}=double.MaxValue; public bool HasValidationError=>Diagnostic is not null; public bool IsDirty=>Text!=_committedText;
    public void Initialize(double value){Value=value;_committedText=Format(value);SetText(_committedText,false);}
    public bool TryCommit()
    {
        if(!double.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)||v<Min||v>Max){Diagnostic=$"Value must be between {Min.ToString(CultureInfo.InvariantCulture)} and {Max.ToString(CultureInfo.InvariantCulture)}.";return false;}
        Value=v;_committedText=Format(v);SetText(_committedText,false);Diagnostic=null;return true;
    }
    public void CancelEdit(){SetText(_committedText,false);Diagnostic=null;}
    public bool TryCommitText(string text){SetText(text);return TryCommit();}
    public bool Increment(double step=1){return Adjust(step);}
    public bool Decrement(double step=1){return Adjust(-step);}
    public override bool ApplyKey(in UiKeyEvent input)
    {
        if(!input.IsDown)return false;
        if(input.PhysicalKey==38){return Increment();}
        if(input.PhysicalKey==40){return Decrement();}
        return base.ApplyKey(input);
    }
    public bool TryApplyInspectorValue(string text,out string? error)
    {
        if(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&v>=Min&&v<=Max){Value=v;_committedText=Format(v);SetText(_committedText,false);Diagnostic=null;SetInvalid(false);error=null;return true;}
        Diagnostic=$"Value must be between {Min.ToString(CultureInfo.InvariantCulture)} and {Max.ToString(CultureInfo.InvariantCulture)}.";SetInvalid(true);error=Diagnostic;return false;
    }
    private bool Adjust(double delta){var next=Value+delta;if(next<Min||next>Max){Diagnostic=$"Value must be between {Min.ToString(CultureInfo.InvariantCulture)} and {Max.ToString(CultureInfo.InvariantCulture)}.";SetInvalid(true);return false;}Value=next;_committedText=Format(next);SetText(_committedText,false);Diagnostic=null;SetInvalid(false);return true;}
    private static string Format(double value)=>value.ToString("G17",CultureInfo.InvariantCulture);
    protected override string GetAutomationValueText()=>Value.ToString(CultureInfo.InvariantCulture);
}

public class ScrollViewer:ContentControl
{
    public override string TypeName=>"ScrollViewer"; public UiPoint Offset{get;private set;}
    public void ScrollBy(float x,float y){Offset=new(MathF.Max(0,Offset.X+x),MathF.Max(0,Offset.Y+y));Invalidate(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
    public override void Arrange(UiRect bounds){Bounds=bounds;Clip=bounds;Content?.Arrange(new(bounds.X-Offset.X,bounds.Y-Offset.Y,Content.DesiredSize.Width,Content.DesiredSize.Height));DirtyFlags&=~(UiDirtyFlags.Arrange|UiDirtyFlags.Visual);}
}
