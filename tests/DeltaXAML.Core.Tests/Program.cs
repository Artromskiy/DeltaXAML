using System.Runtime.InteropServices;
using DeltaXAML.Abstractions;
using DeltaXAML.Core;

static class Assert
{
    public static void True(bool value,string message){if(!value)throw new Exception(message);}
    public static void Equal<T>(T expected,T actual,string message){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"{message}: {expected} != {actual}");}
}
sealed class Source:IInspectorItemSource
{
    public List<InspectorFieldRecord> Items{get;}=new(); public int Count=>Items.Count; public InspectorFieldRecord Get(int index)=>Items[index]; public event Action? Changed; public void Notify()=>Changed?.Invoke(); public bool TryCommit(int index,string text,out string? error){error=null;return true;}
}
sealed class EventProbe:UiElement,IUiRoutedEventSink
{
    public List<UiRoutedEvent> Events{get;}=new(); public void OnRoutedEvent(in UiRoutedEvent routedEvent)=>Events.Add(routedEvent);
}
internal static class Program
{
    public static void Main()
    {
        ParserAndFrame(); PropertyInvalidation(); GridSizing(); TextAndInput(); ScrollAndClips(); InspectorRows(); StorageReuse();
        Console.WriteLine("DeltaXAML.Core.Tests: 7 groups passed");
    }
    static void ParserAndFrame()
    {
        var result=XamlLoader.LoadFrame(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"ComponentInspector.xaml")));Assert.True(result.Success,"ComponentInspector.xaml loads to a frame");Assert.True(result.Frame!.Root is ComponentInspector,"inspector root type");
        var shell=XamlLoader.Load("<StackPanel Orientation=\"Horizontal\"><Panel Width=\"40\" Background=\"#FF0000\"/><Panel Fill=\"true\" Background=\"#00FF00\"/></StackPanel>");Assert.True(shell.Success,"nested dialect");var frame=shell.CreateFrame()!;frame.Layout(new(120,40),1);Assert.Equal(new UiRect(0,0,40,40),shell.Root!.Children[0].Bounds,"fixed child");Assert.Equal(new UiRect(40,0,80,40),shell.Root.Children[1].Bounds,"fill child");
    }
    static void PropertyInvalidation()
    {
        var element=new UiElement();element.Measure(new(100,100));element.Arrange(new(0,0,100,100));Assert.True((element.DirtyFlags&(UiDirtyFlags.Measure|UiDirtyFlags.Arrange|UiDirtyFlags.Visual))==0,"layout clears layout work");element.SetLocal("Width",42,UiDirtyFlags.Measure|UiDirtyFlags.Visual);Assert.True((element.DirtyFlags&UiDirtyFlags.Measure)!=0,"local invalidates measure");element.Measure(new(100,100));element.Arrange(new(0,0,100,100));element.SetStyle("Color",new UiColor(1,2,3),UiDirtyFlags.Visual);Assert.True((element.DirtyFlags&UiDirtyFlags.Visual)!=0,"style invalidates visual");var binding=new UiBindingValue(()=>17,_=>(true,null));element.SetBinding("Value",binding,UiDirtyFlags.Visual);element.Measure(new(100,100));element.Arrange(new(0,0,100,100));binding.NotifyChanged();Assert.True((element.DirtyFlags&UiDirtyFlags.Visual)!=0,"binding invalidates visual");
    }
    static void GridSizing()
    {
        var grid=new Grid();grid.SetColumns(GridLength.Fixed(40),GridLength.Auto,GridLength.Star());grid.SetRows(GridLength.Fixed(20));var a=new Panel{Width=30,Height=20};var b=new Panel{Width=50,Height=20};var c=new Panel{Fill=true};grid.Add(a);grid.Add(b);grid.Add(c);grid.Measure(new(200,20));grid.Arrange(new(0,0,200,20));Assert.Equal(new UiRect(0,0,40,20),a.Bounds,"grid fixed");Assert.Equal(new UiRect(40,0,50,20),b.Bounds,"grid auto");Assert.Equal(new UiRect(90,0,110,20),c.Bounds,"grid star");
    }
    static void TextAndInput()
    {
        var root=new Panel();var probe=new EventProbe{Focusable=true,Width=100,Height=30};var box=new TextBox{Width=100,Height=30,Focusable=true};root.Add(probe);root.Add(box);var button=new Button{Width=100,Height=30};root.Add(button);var frame=new UiFrame(root);frame.Layout(new(100,90),1);var clicked=false;button.Click+=()=>clicked=true;frame.Input.Focus(box.Id);frame.Input.RouteText(new UiTextInput("12"));Assert.Equal("12",box.Text,"UTF text is separate input");frame.Input.RouteKey(new UiKeyEvent(8,true));Assert.Equal("1",box.Text,"physical backspace");frame.Input.RoutePointer(new UiPointerEvent(UiPointerEventKind.Down,new(10,65),1));Assert.Equal(button.Id,frame.Input.Captured!.Value,"pointer capture");frame.Input.RoutePointer(new UiPointerEvent(UiPointerEventKind.Up,new(10,65),1));Assert.True(clicked,"button bubble click");Assert.True(frame.Input.Focused==button.Id,"pointer focuses control");frame.Input.RouteKey(new UiKeyEvent(9,true));Assert.True(frame.Input.Focused==probe.Id||frame.Input.Focused==box.Id,"tab focus traversal");
    }
    static void ScrollAndClips()
    {
        var scroll=new ScrollViewer{Width=100,Height=40};var content=new StackPanel{Height=120};content.Add(new Panel{Width=100,Height=60,Background=new(255,0,0)});content.Add(new Panel{Width=100,Height=60,Background=new(0,255,0)});scroll.Content=content;var frame=new UiFrame(scroll);frame.Layout(new(100,40),1);scroll.ScrollBy(0,20);frame.Layout(new(100,40),1);var list=frame.ExtractDrawList(new UiFrameContext(new(100,40),1,1));Assert.True(list.Commands.Length==2,"scroll retains both commands");foreach(var command in list.Commands.Span)Assert.True(command.Clip.IsInside(new UiRect(0,0,100,40)),"scroll clips to viewport");
    }
    static void InspectorRows()
    {
        var source=new Source();source.Items.Add(new("Transform","X","X","1","Numeric"));source.Items.Add(new("Transform","Y","Y","2","Numeric"));var inspector=new ComponentInspector{Width=200,Height=100,ItemSource=source};inspector.Refresh();var first=inspector.Rows.Children[0];source.Items[0]=source.Items[0] with{ValueText="9"};source.Notify();Assert.True(ReferenceEquals(first,inspector.Rows.Children[0]),"inspector row identity reused");Assert.Equal("9",((InspectorRow)first).Record.ValueText,"row value refreshed");source.Items.RemoveAt(1);source.Notify();Assert.Equal(1,inspector.Rows.Children.Count,"rows shrink without rebuilding tree");
    }
    static void StorageReuse()
    {
        var root=new Panel{Width=20,Height=20,Background=new(1,2,3)};var frame=new UiFrame(root);frame.Layout(new(20,20),1);var list=frame.ExtractDrawList(new UiFrameContext(new(20,20),1,1));var commands=list.Commands;Assert.True(MemoryMarshal.TryGetArray(commands,out ArraySegment<UiDrawCommand> first),"command backing array");frame.ExtractDrawList(new UiFrameContext(new(20,20),1,2));Assert.True(MemoryMarshal.TryGetArray(list.Commands,out ArraySegment<UiDrawCommand> second),"second command backing array");Assert.True(ReferenceEquals(first.Array,second.Array),"draw storage stable after warmup");Assert.Equal(1,list.Commands.Length,"draw count stable");for(var i=0;i<3;i++){frame.Layout(new(20,20),1);frame.ExtractDrawList(new UiFrameContext(new(20,20),1,(uint)i));}var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<20;i++){frame.Layout(new(20,20),1);frame.ExtractDrawList(new UiFrameContext(new(20,20),1,(uint)i));}var allocated=GC.GetAllocatedBytesForCurrentThread()-before;Assert.True(allocated==0,$"warm frame allocated {allocated} bytes");
    }
}
