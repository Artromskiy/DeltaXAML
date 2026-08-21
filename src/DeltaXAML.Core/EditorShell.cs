using DeltaXAML.Abstractions;

namespace DeltaXAML.Core;

public sealed class EditorShell:ContentControl
{
    private readonly StackPanel _root=new(){Orientation=UiOrientation.Vertical};
    private readonly Border _toolbar=new();
    private readonly Grid _body=new();
    private readonly Border _hierarchy=new();
    private readonly Border _viewport=new();
    private readonly Border _sideInspector=new();
    private readonly ScrollViewer _scroll=new();
    private readonly StackPanel _status=new(){Orientation=UiOrientation.Horizontal};

    public EditorShell()
    {
        StyleKey="ShellRoot";
        AutomationName="Editor Shell";
        AutomationRole="window";
        _toolbar.StyleKey="Toolbar";
        _hierarchy.StyleKey="Sidebar";
        _viewport.StyleKey="Inspector";
        _sideInspector.StyleKey="Inspector";
        _scroll.Content=new ComponentInspector{StyleKey="ShellRoot"};
        var toolbarRow=new StackPanel{Orientation=UiOrientation.Horizontal};
        toolbarRow.Add(new TextBlock{Text="Delta Editor",StyleKey="Title"});
        toolbarRow.Add(new TextBlock{Text="shell",StyleKey="Muted"});
        toolbarRow.Add(CreateButton("Save"));
        toolbarRow.Add(CreateButton("Run"));
        _toolbar.Add(toolbarRow);
        var hierarchyContent=new StackPanel{Orientation=UiOrientation.Vertical};
        hierarchyContent.Add(new TextBlock{Text="Hierarchy",StyleKey="Title"});
        hierarchyContent.Add(new TextBlock{Text="Scene graph placeholder",StyleKey="Muted"});
        hierarchyContent.Add(CreateButton("Add Entity"));
        _hierarchy.Add(hierarchyContent);
        var viewportContent=new StackPanel{Orientation=UiOrientation.Vertical};
        viewportContent.Add(new TextBlock{Text="Viewport",StyleKey="Title"});
        viewportContent.Add(new TextBlock{Text="Renderer-neutral preview placeholder",StyleKey="Muted"});
        _viewport.Add(viewportContent);
        var inspectorChrome=new StackPanel{Orientation=UiOrientation.Vertical};
        inspectorChrome.Add(new TextBlock{Text="Inspector",StyleKey="Title"});
        inspectorChrome.Add(_scroll);
        _sideInspector.Add(inspectorChrome);
        _body.SetColumns(GridLength.Fixed(220),GridLength.Star(),GridLength.Fixed(320));
        _body.SetRows(GridLength.Star());
        _body.Add(_hierarchy);
        _body.Add(_viewport);
        _body.Add(_sideInspector);
        _status.Add(new TextBlock{Text="Ready",StyleKey="Muted"});
        _status.Add(new TextBlock{Text="No diagnostics",StyleKey="Muted"});
        _root.Add(_toolbar);
        _root.Add(_body);
        _root.Add(_status);
        Content=_root;
    }

    private static Button CreateButton(string text)
    {
        var button=new Button{StyleKey="Button",AutomationName=text};
        button.Content=new TextBlock{Text=text,StyleKey="Muted"};
        return button;
    }
}
