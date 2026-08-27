using System.Diagnostics.CodeAnalysis;
using IUiResourceDictionary = DeltaXAML.Internal.IUiResourceStore;

namespace DeltaXAML.Internal;

internal sealed class UiResourceChangedEventArgs(string key) : EventArgs
{
    public string Key { get; } = key;
}

internal sealed class UiResourceStore : IUiResourceDictionary
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    public event EventHandler<UiResourceChangedEventArgs>? Changed;
    public void Set(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_values.TryGetValue(key, out var current) && Equals(current, value)) { return; }
        _values[key] = value;
        Changed?.Invoke(this, new(key));
    }
    public bool TryGet(string key, out object? value) { ArgumentException.ThrowIfNullOrWhiteSpace(key); return _values.TryGetValue(key, out value); }
    public bool TryResolve(string key, out object? value, [NotNullWhen(false)] out string? diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_values.TryGetValue(key, out var candidate))
        {
            value = null;
            diagnostic = $"Resource '{key}' was not found.";
            return false;
        }

        if (candidate is not UiResourceReference firstReference)
        {
            value = candidate;
            diagnostic = null;
            return true;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { key };
        var current = firstReference.Key;
        while (true)
        {
            if (!visited.Add(current)) { value = null; diagnostic = $"Resource cycle detected at '{current}'."; return false; }
            if (!_values.TryGetValue(current, out var nextValue)) { value = null; diagnostic = $"Resource '{current}' was not found."; return false; }
            if (nextValue is not UiResourceReference reference) { value = nextValue; diagnostic = null; return true; }
            current = reference.Key;
        }
    }
}

internal sealed class UiStyle : IUiStyle
{
    private readonly Action<UiElement> _apply;
    public UiStyle(string key, string targetType, Action<UiElement> apply) { ArgumentException.ThrowIfNullOrWhiteSpace(key); ArgumentException.ThrowIfNullOrWhiteSpace(targetType); ArgumentNullException.ThrowIfNull(apply); Key = key; TargetType = targetType; _apply = apply; }
    public string Key { get; }
    public string TargetType { get; }
    public void Apply(IUiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UiElement concrete)
        {
            _apply(concrete);
        }
    }
}

internal sealed class UiTemplate : IUiTemplate
{
    private readonly Func<IUiElement, IUiElement> _build;
    public UiTemplate(Func<IUiElement, IUiElement> build) { ArgumentNullException.ThrowIfNull(build); _build = build; }
    public IUiElement Build(IUiElement owner) { ArgumentNullException.ThrowIfNull(owner); return _build(owner); }
}

internal sealed class UiTheme : IUiTheme
{
    private readonly Dictionary<string, IUiTemplate> _templates = new(StringComparer.Ordinal);
    public UiTheme(IUiResourceDictionary resources, IReadOnlyList<IUiStyle> styles) { ArgumentNullException.ThrowIfNull(resources); ArgumentNullException.ThrowIfNull(styles); Resources = resources; Styles = styles; }
    public IUiResourceDictionary Resources { get; }
    public IReadOnlyList<IUiStyle> Styles { get; }
    public void RegisterTemplate(string key, IUiTemplate template) { ArgumentException.ThrowIfNullOrWhiteSpace(key); ArgumentNullException.ThrowIfNull(template); _templates[key] = template; }
    public IUiTemplate? GetTemplate(string key) { ArgumentException.ThrowIfNullOrWhiteSpace(key); return _templates.TryGetValue(key, out var template) ? template : null; }
    public void Apply(IUiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        ApplyRecursive(root);
    }
    private void ApplyRecursive(IUiElement element)
    {
        if (element is UiElement concrete)
        {
            if (concrete.StyleKey is not null)
            {
                foreach (var style in Styles)
                {
                    if (style.Key == concrete.StyleKey && style.TargetType == concrete.TypeName)
                    {
                        style.Apply(concrete);
                    }
                }
            }
            if (concrete.TemplateKey is not null && GetTemplate(concrete.TemplateKey) is { } template && element.Children.Count == 0 && element is IUiPanel panel)
            {
                panel.Add(template.Build(element));
            }
        }
        foreach (var child in element.Children)
        {
            ApplyRecursive(child);
        }
    }
}

internal static class DeltaTheme
{
    public static readonly UiTheme Default = CreateDefault();
    private static UiColor Color(UiResourceStore resources, string key, UiColor fallback) => resources.TryGet(key, out var value) && value is UiColor color ? color : fallback;
    private static UiTheme CreateDefault()
    {
        var resources = new UiResourceStore();
        resources.Set("Color.Window", new UiColor(31, 41, 51));
        resources.Set("Color.Text", new UiColor(230, 236, 244));
        resources.Set("Color.TextMuted", new UiColor(155, 166, 182));
        resources.Set("Color.Accent", new UiColor(76, 111, 164));
        resources.Set("Spacing.Page", new UiThickness(12, 12, 12, 12));
        resources.Set("Spacing.Row", new UiThickness(8, 6, 8, 6));
        resources.Set("Font.Body", "DeltaBody");
        resources.Set("Font.Mono", "DeltaMono");

        var styles = new List<IUiStyle>
        {
            new UiStyle("Title","TextBlock",e=>{if(e is TextBlock t){t.Foreground=Color(resources,"Color.Text",default);t.FontSize=18;}}),
            new UiStyle("Muted","TextBlock",e=>{if(e is TextBlock t){t.Foreground=Color(resources,"Color.TextMuted",default);t.FontSize=12;}}),
            new UiStyle("TextEditor","TextBox",e=>{if(e is TextBox t){t.Background=new UiColor(19,24,32);t.Foreground=Color(resources,"Color.Text",default);}}),
            new UiStyle("NumericEditor","NumericEditor",e=>{if(e is NumericEditor t){t.Background=new UiColor(19,24,32);t.Foreground=Color(resources,"Color.Text",default);}}),
            new UiStyle("Button","Button",e=>{e.Background=Color(resources,"Color.Accent",default);e.AutomationRole=UiAutomationRole.Button;}),
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
