using DeltaXAML.Abstractions;

namespace DeltaXAML.Core;

public sealed class UiResourceDictionary : IUiResourceDictionary
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    public void Set(string key, object? value) => _values[key] = value;
    public bool TryGet(string key, out object? value) => _values.TryGetValue(key, out value);
}

public sealed class UiStyle : IUiStyle
{
    private readonly Action<UiElement> _apply;
    public UiStyle(string key, string targetType, Action<UiElement> apply) { Key = key; TargetType = targetType; _apply = apply; }
    public string Key { get; }
    public string TargetType { get; }
    public void Apply(IUiElement element) { if (element is UiElement concrete) _apply(concrete); }
}

public sealed class UiTemplate : IUiTemplate
{
    private readonly Func<IUiElement, IUiElement> _build;
    public UiTemplate(Func<IUiElement, IUiElement> build) => _build = build;
    public IUiElement Build(IUiElement owner) => _build(owner);
}

public sealed class UiTheme : IUiTheme
{
    private readonly Dictionary<string, IUiTemplate> _templates = new(StringComparer.Ordinal);
    public UiTheme(IUiResourceDictionary resources, IReadOnlyList<IUiStyle> styles) { Resources = resources; Styles = styles; }
    public IUiResourceDictionary Resources { get; }
    public IReadOnlyList<IUiStyle> Styles { get; }
    public void RegisterTemplate(string key, IUiTemplate template) => _templates[key] = template;
    public IUiTemplate? GetTemplate(string key) => _templates.TryGetValue(key, out var template) ? template : null;
    public void Apply(IUiElement root)
    {
        ApplyRecursive(root);
    }
    private void ApplyRecursive(IUiElement element)
    {
        if (element is UiElement concrete)
        {
            if (concrete.StyleKey is not null)
            {
                foreach (var style in Styles) if (style.Key == concrete.StyleKey && style.TargetType == concrete.TypeName) style.Apply(concrete);
            }
            if (concrete.TemplateKey is not null && GetTemplate(concrete.TemplateKey) is { } template && element.Children.Count == 0 && element is IUiPanel panel)
            {
                panel.Add(template.Build(element));
            }
        }
        foreach (var child in element.Children) ApplyRecursive(child);
    }
}

public static class DeltaTheme
{
    public static readonly UiTheme Default = CreateDefault();
    private static UiColor Color(IUiResourceDictionary resources, string key, UiColor fallback) => resources.TryGet(key, out var value) && value is UiColor color ? color : fallback;
    private static UiTheme CreateDefault()
    {
        var resources = new UiResourceDictionary();
        resources.Set("Color.Window", new UiColor(31, 41, 51));
        resources.Set("Color.Toolbar", new UiColor(38, 53, 74));
        resources.Set("Color.Sidebar", new UiColor(52, 73, 94));
        resources.Set("Color.Inspector", new UiColor(61, 74, 92));
        resources.Set("Color.Status", new UiColor(32, 42, 54));
        resources.Set("Color.Text", new UiColor(230, 236, 244));
        resources.Set("Color.TextMuted", new UiColor(155, 166, 182));
        resources.Set("Color.Accent", new UiColor(76, 111, 164));
        resources.Set("Spacing.Page", new UiThickness(12, 12, 12, 12));
        resources.Set("Spacing.Row", new UiThickness(8, 6, 8, 6));
        resources.Set("Font.Body", "DeltaBody");
        resources.Set("Font.Mono", "DeltaMono");

        var styles = new List<IUiStyle>
        {
            new UiStyle("ShellRoot","ComponentInspector",e=>{e.Background=Color(resources,"Color.Window",default);e.AutomationName="Component Inspector";e.AutomationRole="window";}),
            new UiStyle("ShellRoot","EditorShell",e=>{e.Background=Color(resources,"Color.Window",default);e.AutomationName="Editor Shell";e.AutomationRole="window";}),
            new UiStyle("Title","TextBlock",e=>{if(e is TextBlock t){t.Foreground=Color(resources,"Color.Text",default);t.FontSize=18;}}),
            new UiStyle("Muted","TextBlock",e=>{if(e is TextBlock t){t.Foreground=Color(resources,"Color.TextMuted",default);t.FontSize=12;}}),
            new UiStyle("Toolbar","Border",e=>e.Background=Color(resources,"Color.Toolbar",default)),
            new UiStyle("Sidebar","Border",e=>e.Background=Color(resources,"Color.Sidebar",default)),
            new UiStyle("Inspector","Border",e=>e.Background=Color(resources,"Color.Inspector",default)),
            new UiStyle("Status","Border",e=>e.Background=Color(resources,"Color.Status",default)),
            new UiStyle("TextEditor","TextBox",e=>{if(e is TextBox t){t.Background=new UiColor(19,24,32);t.Foreground=Color(resources,"Color.Text",default);}}),
            new UiStyle("NumericEditor","NumericEditor",e=>{if(e is NumericEditor t){t.Background=new UiColor(19,24,32);t.Foreground=Color(resources,"Color.Text",default);}}),
            new UiStyle("Button","Button",e=>{e.Background=Color(resources,"Color.Accent",default);e.AutomationRole="button";}),
        };

        var theme = new UiTheme(resources, styles);
        theme.RegisterTemplate("ButtonChrome", new UiTemplate(owner =>
        {
            var border = new Border { Padding = new UiThickness(8, 4, 8, 4), Background = new UiColor(76, 111, 164) };
            var label = new TextBlock { FontSize = 14, Foreground = Color(resources, "Color.Text", default) };
            border.Add(label);
            return border;
        }));
        theme.RegisterTemplate("EditorField", new UiTemplate(owner =>
        {
            var row = new Border { Padding = new UiThickness(8, 4, 8, 4) };
            row.Add(new TextBlock { FontSize = 14 });
            return row;
        }));
        return theme;
    }
}
