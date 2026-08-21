using System.Runtime.InteropServices;
using DeltaXAML.Abstractions;
using DeltaXAML.Core;

static class Assert
{
    public static void True(bool value,string message){if(!value)throw new Exception(message);}
    public static void Equal<T>(T expected,T actual,string message){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"{message}: {expected} != {actual}");}
}

sealed class FakeClipboard:IUiClipboard
{
    public string? Text { get; private set; }
    public string? GetText()=>Text;
    public void SetText(string? text)=>Text=text;
    public bool HasText=>!string.IsNullOrEmpty(Text);
}

sealed class Source:IInspectorItemSource
{
    public List<InspectorFieldRecord> Items{get;}=new();
    public int Count=>Items.Count;
    public InspectorFieldRecord Get(int index)=>Items[index];
    public event Action<InspectorItemSourceChange>? Changed;
    public bool TryCommit(int index,string text,out string? error){error=null;Items[index]=Items[index] with{ValueText=text};Changed?.Invoke(new InspectorItemSourceChange(InspectorItemSourceChangeKind.Change,index,1));return true;}
    public void Add(InspectorFieldRecord record){Items.Add(record);Changed?.Invoke(new InspectorItemSourceChange(InspectorItemSourceChangeKind.Add,Items.Count-1,1));}
    public void RemoveAt(int index){Items.RemoveAt(index);Changed?.Invoke(new InspectorItemSourceChange(InspectorItemSourceChangeKind.Remove,index,1));}
    public void NotifyReset()=>Changed?.Invoke(new InspectorItemSourceChange(InspectorItemSourceChangeKind.Reset,0,Items.Count));
}

sealed class EventProbe:UiElement,IUiRoutedEventSink
{
    public List<UiRoutedEvent> Events{get;}=new();
    public void OnRoutedEvent(in UiRoutedEvent routedEvent)=>Events.Add(routedEvent);
}

internal static class Program
{
    public static void Main()
    {
        InspectorShellAndLoader();
        EditorShellAndTheme();
        PropertyInvalidation();
        GridSizing();
        TextClipboardUndoAndValidation();
        PointerFocusAndDispatch();
        ScrollAndClips();
        InspectorRows();
        TextRunStabilityAndDelta();
        StorageReuse();
        Console.WriteLine("DeltaXAML.Core.Tests: 8 groups passed");
    }

    static void InspectorShellAndLoader()
    {
        var result=XamlLoader.LoadFrame(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"ComponentInspector.xaml")));
        Assert.True(result.Success,"ComponentInspector.xaml loads to a frame");
        Assert.True(result.Frame!.Root is ComponentInspector,"inspector root type");
        result.Frame.Layout(new(800,450),1);
        Assert.True(result.Frame.Root.Bounds.Width==800&&result.Frame.Root.Bounds.Height==450,"inspector shell fits viewport");
        var list=result.Frame.ExtractDrawList(new UiFrameContext(new(800,450),1,1));
        Assert.True(list.Commands.Length>0,"inspector produces draw commands");
        foreach(var command in list.Commands.Span)Assert.True(command.Clip.IsInside(new UiRect(0,0,800,450)),"inspector stays inside viewport");
    }

    static void EditorShellAndTheme()
    {
        var result=XamlLoader.LoadFrame(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"EditorShell.xaml")));
        Assert.True(result.Success,"EditorShell.xaml loads to a frame");
        Assert.True(result.Frame!.Root is EditorShell,"editor shell root type");
        result.Frame.Layout(new(960,540),1);
        var list=result.Frame.ExtractDrawList(new UiFrameContext(new(960,540),1,1));
        var textVersions=new uint[list.TextRuns.Length];
        for(var i=0;i<textVersions.Length;i++)textVersions[i]=list.TextRuns.Span[i].Version;
        Assert.True(list.Commands.Length>0,"editor shell produces draw commands");
        foreach(var command in list.Commands.Span)Assert.True(command.Clip.IsInside(new UiRect(0,0,960,540)),"editor shell stays inside viewport");
        Assert.True(list.TextRuns.Length>=6,"editor shell emits visible text runs");
        Assert.Equal("Delta Editor",list.TextRuns.Span[0].Text,"text run order begins with toolbar title");
        result.Frame.Layout(new(960,540),2);
        var resized=result.Frame.ExtractDrawList(new UiFrameContext(new(960,540),2,2));
        Assert.True(resized.Commands.Length==list.Commands.Length,"dpi change keeps command count stable");
        Assert.True(resized.TextRuns.Length==list.TextRuns.Length,"dpi change keeps text run count stable");
        Assert.True(resized.TextRuns.Span[0].Version!=textVersions[0],"dpi change updates text run version");
        var template=new UiTemplate(_=>new Border{Padding=new UiThickness(1,1,1,1)});
        var first=template.Build(new Panel());
        var second=template.Build(new Panel());
        Assert.True(!ReferenceEquals(first,second),"template builds reusable retained instances");
    }

    static void PropertyInvalidation()
    {
        var element=new UiElement();
        element.Measure(new(100,100));
        element.Arrange(new(0,0,100,100));
        Assert.True((element.DirtyFlags&(UiDirtyFlags.Measure|UiDirtyFlags.Arrange|UiDirtyFlags.Visual))==0,"layout clears layout work");
        element.SetLocal("Width",42,UiDirtyFlags.Measure|UiDirtyFlags.Visual);
        Assert.True((element.DirtyFlags&UiDirtyFlags.Measure)!=0,"local invalidates measure");
        element.Measure(new(100,100));
        element.Arrange(new(0,0,100,100));
        element.SetStyle("Color",new UiColor(1,2,3),UiDirtyFlags.Visual);
        Assert.True((element.DirtyFlags&UiDirtyFlags.Visual)!=0,"style invalidates visual");
        var binding=new UiBindingValue(()=>17,_=>(true,null));
        element.SetBinding("Value",binding,UiDirtyFlags.Visual);
        element.Measure(new(100,100));
        element.Arrange(new(0,0,100,100));
        binding.NotifyChanged();
        Assert.True((element.DirtyFlags&UiDirtyFlags.Visual)!=0,"binding invalidates visual");
        var button=new Button();
        button.SetStyle("Button",null,UiDirtyFlags.Visual);
        Assert.True(button.VisualState.State==UiVisualState.Normal,"button starts normal");
        button.SetHovered(true);
        Assert.True(button.VisualState.State==UiVisualState.Hover,"hover state");
        button.SetPressed(true);
        Assert.True(button.VisualState.State==UiVisualState.Pressed,"pressed state");
        button.SetFocused(true);
        Assert.True(button.VisualState.IsFocused,"focused state data");
    }

    static void GridSizing()
    {
        var grid=new Grid();
        grid.SetColumns(GridLength.Fixed(40),GridLength.Auto,GridLength.Star());
        grid.SetRows(GridLength.Fixed(20));
        var a=new Panel{Width=30,Height=20};
        var b=new Panel{Width=50,Height=20};
        var c=new Panel{Fill=true};
        grid.Add(a);grid.Add(b);grid.Add(c);
        grid.Measure(new(200,20));
        grid.Arrange(new(0,0,200,20));
        Assert.Equal(new UiRect(0,0,40,20),a.Bounds,"grid fixed");
        Assert.Equal(new UiRect(40,0,50,20),b.Bounds,"grid auto");
        Assert.Equal(new UiRect(90,0,110,20),c.Bounds,"grid star");
    }

    static void TextClipboardUndoAndValidation()
    {
        var clipboard=new FakeClipboard();
        var box=new TextBox{Width=100,Height=30,Focusable=true,Clipboard=clipboard};
        box.SetText("abc",false);
        box.SelectAll();
        box.Copy();
        Assert.Equal("abc",clipboard.Text,"copy");
        box.SetText("z",false);
        box.Paste();
        Assert.Equal("zabc",box.Text,"paste");
        box.Undo();
        Assert.Equal("z",box.Text,"undo");
        box.Redo();
        Assert.Equal("zabc",box.Text,"redo");
        box.SelectAll();
        box.Cut();
        Assert.Equal("",box.Text,"cut");

        var numeric=new NumericEditor{Min=1,Max=10};
        numeric.Initialize(3);
        numeric.SetText("99",false);
        Assert.True(!numeric.TryCommit(),"numeric validation fails");
        Assert.Equal("99",numeric.Text,"invalid text retained");
        Assert.Equal(3d,numeric.Value,"invalid commit preserves value");
        Assert.True(numeric.HasValidationError,"inline diagnostic set");
        numeric.CancelEdit();
        Assert.Equal("3",numeric.Text,"cancel restores commit");
        numeric.Increment();
        Assert.Equal(4d,numeric.Value,"increment");
        numeric.Decrement();
        Assert.Equal(3d,numeric.Value,"decrement");
    }

    static void PointerFocusAndDispatch()
    {
        var root=new Panel();
        var probe=new EventProbe{Focusable=true,Width=100,Height=30};
        var box=new TextBox{Width=100,Height=30,Focusable=true};
        root.Add(probe);root.Add(box);
        var button=new Button{Width=100,Height=30};
        root.Add(button);
        var frame=new UiFrame(root);
        frame.Layout(new(100,90),1);
        var clicked=false;
        button.Click+=()=>clicked=true;
        frame.Input.Focus(box.Id);
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiTextInput("12")));
        Assert.Equal("12",box.Text,"UTF text is separate input");
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiKeyEvent(8,true)));
        Assert.Equal("1",box.Text,"physical backspace");
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiPointerEvent(UiPointerEventKind.Down,new(10,65),1)));
        Assert.Equal(button.Id,frame.Input.Captured!.Value,"pointer capture");
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiPointerEvent(UiPointerEventKind.Up,new(10,65),1)));
        Assert.True(clicked,"button bubble click");
        Assert.True(frame.Input.Focused==button.Id,"pointer focuses control");
        ((IUiInputDispatcher)frame.Input).Dispatch(UiInputPacket.From(new UiKeyEvent(9,true)));
        Assert.True(frame.Input.Focused==probe.Id||frame.Input.Focused==box.Id,"tab focus traversal");
    }

    static void ScrollAndClips()
    {
        var scroll=new ScrollViewer{Width=100,Height=40};
        var content=new StackPanel{Height=120};
        content.Add(new Panel{Width=100,Height=60,Background=new(255,0,0)});
        content.Add(new Panel{Width=100,Height=60,Background=new(0,255,0)});
        scroll.Content=content;
        var frame=new UiFrame(scroll);
        frame.Layout(new(100,40),1);
        scroll.ScrollBy(0,20);
        frame.Layout(new(100,40),1);
        var list=frame.ExtractDrawList(new UiFrameContext(new(100,40),1,1));
        Assert.True(list.Commands.Length==2,"scroll retains both commands");
        Assert.True(list.TextRuns.Length==0,"plain scrolling content has no text runs");
        foreach(var command in list.Commands.Span)Assert.True(command.Clip.IsInside(new UiRect(0,0,100,40)),"scroll clips to viewport");
    }

    static void InspectorRows()
    {
        var source=new Source();
        source.Add(new("Transform","X","X","1","Numeric"));
        source.Add(new("Transform","Y","Y","2","Numeric"));
        var inspector=new ComponentInspector{Width=200,Height=100,ItemSource=source};
        inspector.Refresh();
        var first=inspector.Rows.Children[0];
        var initial= new UiFrame(inspector).ExtractDrawList(new UiFrameContext(new(200,100),1,1));
        Assert.True(initial.TextRuns.Length>=4,"inspector emits ordered text runs");
        source.Items[0]=source.Items[0] with{ValueText="9"};
        source.NotifyReset();
        Assert.True(ReferenceEquals(first,inspector.Rows.Children[0]),"inspector row identity reused");
        Assert.Equal("9",((InspectorRow)first).Record.ValueText,"row value refreshed");
        var updated=new UiFrame(inspector).ExtractDrawList(new UiFrameContext(new(200,100),1,2));
        Assert.True(updated.TextRuns.Length==initial.TextRuns.Length,"inspector run count stays stable after value update");
        source.RemoveAt(1);
        Assert.Equal(1,inspector.Rows.Children.Count,"rows shrink without rebuilding tree");
    }

    static void TextRunStabilityAndDelta()
    {
        var shell=new EditorShell{Width=960,Height=540};
        var frame=new UiFrame(shell);
        frame.Layout(new(960,540),1);
        var draw=frame.ExtractDrawList(new UiFrameContext(new(960,540),1,1));
        var initialTextRuns=draw.TextRuns.ToArray();
        var initialCommands=draw.Commands.ToArray();
        for(var i=0;i<100;i++)
        {
            frame.Layout(new(960,540),1);
            var next=frame.ExtractDrawList(new UiFrameContext(new(960,540),1,(uint)(i+2)));
            Assert.True(next.TextRuns.Length==initialTextRuns.Length,"unchanged warm frame keeps text run count");
            Assert.True(next.Commands.Length==initialCommands.Length,"unchanged warm frame keeps command count");
            for(var j=0;j<next.TextRuns.Length;j++)
            {
                Assert.Equal(initialTextRuns[j].Version,next.TextRuns.Span[j].Version,"unchanged warm frame keeps text version");
                Assert.Equal(initialTextRuns[j].GlyphRunKey,next.TextRuns.Span[j].GlyphRunKey,"unchanged warm frame keeps run identity");
            }
        }
        var inspector=FindFirstInspector(shell);
        var source=new Source();
        source.Add(new("Transform","X","X","1","Numeric"));
        source.Add(new("Transform","Y","Y","2","Numeric"));
        inspector.ItemSource=source;
        frame.Layout(new(960,540),1);
        var before=frame.ExtractDrawList(new UiFrameContext(new(960,540),1,200));
        var beforeVersion=before.Version;
        var beforeVersions=before.TextRuns.ToArray();
        source.Items[1]=source.Items[1] with{ValueText="7"};
        source.NotifyReset();
        frame.Layout(new(960,540),1);
        var after=frame.ExtractDrawList(new UiFrameContext(new(960,540),1,201));
        Assert.True(after.TextRuns.Length==beforeVersions.Length,"value edit keeps text run count");
        var changed=0;
        for(var i=0;i<after.TextRuns.Length;i++)if(after.TextRuns.Span[i].Version!=beforeVersions[i].Version)changed++;
        Assert.True(changed==1,"one value edit changes one text run");
        var resizedFrame=new UiFrame(shell);
        resizedFrame.Layout(new(800,450),1);
        var resized=resizedFrame.ExtractDrawList(new UiFrameContext(new(800,450),1,1));
        Assert.True(resized.TextRuns.Length==after.TextRuns.Length,"resize keeps text content identity");
        var dpiFrame=new UiFrame(shell);
        dpiFrame.Layout(new(960,540),2);
        var dpi=dpiFrame.ExtractDrawList(new UiFrameContext(new(960,540),2,1));
        Assert.True(dpi.TextRuns.Length==after.TextRuns.Length,"dpi keeps text run count");
        Assert.True(dpi.TextRuns.Span[0].Version!=after.TextRuns.Span[0].Version,"dpi changes required text version");
        var delta=after.GetDeltaSince(beforeVersion);
        Assert.True(delta.TextRuns.Count>0,"delta reports changed text range");
    }

    static ComponentInspector FindFirstInspector(IUiElement root)
    {
        if(root is ComponentInspector inspector)return inspector;
        foreach(var child in root.Children)
        {
            if(child is ComponentInspector foundInspector)return foundInspector;
            if(child.Children.Count>0&&TryFindFirstInspector(child,out var found))
                return found;
        }
        throw new Exception("Inspector not found");
    }

    static bool TryFindFirstInspector(IUiElement root,out ComponentInspector? inspector)
    {
        if(root is ComponentInspector found){inspector=found;return true;}
        foreach(var child in root.Children)
            if(TryFindFirstInspector(child,out inspector))
                return true;
        inspector=null;
        return false;
    }

    static void StorageReuse()
    {
        var root=new Panel{Width=20,Height=20,Background=new(1,2,3)};
        var frame=new UiFrame(root);
        frame.Layout(new(20,20),1);
        var list=frame.ExtractDrawList(new UiFrameContext(new(20,20),1,1));
        var commands=list.Commands;
        Assert.True(MemoryMarshal.TryGetArray(commands,out ArraySegment<UiDrawCommand> first),"command backing array");
        frame.ExtractDrawList(new UiFrameContext(new(20,20),1,2));
        Assert.True(MemoryMarshal.TryGetArray(list.Commands,out ArraySegment<UiDrawCommand> second),"second command backing array");
        Assert.True(ReferenceEquals(first.Array,second.Array),"draw storage stable after warmup");
        Assert.Equal(1,list.Commands.Length,"draw count stable");
        for(var i=0;i<3;i++){frame.Layout(new(20,20),1);frame.ExtractDrawList(new UiFrameContext(new(20,20),1,(uint)i));}
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<20;i++){frame.Layout(new(20,20),1);frame.ExtractDrawList(new UiFrameContext(new(20,20),1,(uint)i));}
        var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.True(allocated==0,$"warm frame allocated {allocated} bytes");
    }
}
