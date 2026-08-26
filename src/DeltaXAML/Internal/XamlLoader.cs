using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;
using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;
namespace DeltaXAML.Internal;

public readonly record struct XamlDiagnostic(string Code, string Message, int Line, int Column);
public sealed record XamlLoadResult(UiElement? Root, IReadOnlyList<XamlDiagnostic> Diagnostics) { public bool Success => Root is not null && Diagnostics.Count == 0; public UiFrame? CreateFrame() { if (!Success || Root is null) { return null; } return new UiFrame(Root); } }
public sealed record XamlFrameLoadResult(UiFrame? Frame, IReadOnlyList<XamlDiagnostic> Diagnostics) { public bool Success => Frame is not null && Diagnostics.Count == 0; }
public sealed class XamlTypeRegistry
{
    private readonly Dictionary<string, Func<UiElement>> _factories = new(StringComparer.Ordinal);
    public void Register(string name, Func<UiElement> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[name] = factory;
    }
    internal bool TryCreate(string name, [NotNullWhen(true)] out UiElement? element)
    {
        if (_factories.TryGetValue(name, out var factory)) { element = factory(); return true; }
        element = null; return false;
    }
}
public static class XamlLoader
{
    public static XamlLoadResult Load(string source) => Load(source, null, null, null);
    public static XamlLoadResult Load(string source, XamlTypeRegistry? registry) => Load(source, registry, null, null);
    internal static XamlLoadResult LoadForAdapter(string source, Func<string, string, UiElement?>? factory, UiResourceStore? resources) => Load(source, null, resources, factory);
    private static XamlLoadResult Load(string source, XamlTypeRegistry? registry, UiResourceStore? resources, Func<string, string, UiElement?>? factory) { ArgumentNullException.ThrowIfNull(source); var d = new List<XamlDiagnostic>(); try { using var r = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true }); r.MoveToContent(); return new(Read(r, d, registry, resources, factory), d); } catch (XmlException e) { d.Add(new("XAML001", e.Message, e.LineNumber, e.LinePosition)); return new(null, d); } }
    public static XamlFrameLoadResult LoadFrame(string source) => LoadFrame(source, (XamlTypeRegistry?)null);
    public static XamlFrameLoadResult LoadFrame(string source, XamlTypeRegistry? registry) { var result = Load(source, registry); var frame = result.CreateFrame(); if (frame is not null) { DeltaTheme.Default.Apply(frame.Root); } return new(frame, result.Diagnostics); }
    public static XamlFrameLoadResult LoadFrame(string source, UiResourceStore resources) { ArgumentNullException.ThrowIfNull(resources); var result = Load(source, null, resources, null); var frame = result.CreateFrame(); if (frame is not null) { DeltaTheme.Default.Apply(frame.Root); } return new(frame, result.Diagnostics); }
    private static UiElement? Read(XmlReader r, List<XamlDiagnostic> d, XamlTypeRegistry? registry, UiResourceStore? resources, Func<string, string, UiElement?>? factory)
    {
        var line = (r as IXmlLineInfo)?.LineNumber ?? 0; UiElement? e = r.LocalName switch { "Panel" => new Panel(), "StackPanel" => new StackPanel(), "Border" => new Border(), "Grid" => new Grid(), "ContentControl" => new ContentControl(), "Button" => new Button(), "ToggleButton" => new ToggleButton(), "TextBlock" => new TextBlock(), "TextBox" => new TextBox(), "NumericEditor" => new NumericEditor(), "ScrollViewer" => new ScrollViewer(), _ => null }; if (e is null && registry is not null)
        {
            registry.TryCreate(r.LocalName, out e);
        }
        if (e is null && factory is not null)
        {
            e = factory(r.NamespaceURI, r.LocalName);
        }
        if (e is null) { d.Add(new("XAML002", $"Unsupported element '{r.LocalName}'.", line, 1)); if (!r.IsEmptyElement) { r.Skip(); } return null; } while (r.MoveToNextAttribute())
        {
            Apply(e, r.LocalName, r.Value, line, d, resources);
        }
        r.MoveToElement(); if (r.IsEmptyElement) { r.Read(); return e; }
        r.Read(); while (!r.EOF && r.NodeType != XmlNodeType.EndElement)
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                var child = Read(r, d, registry, resources, factory); if (child is not null && e is IUiPanel p)
                {
                    p.Add(child);
                }
                else if (child is not null && e is ContentControl c)
                {
                    c.Content = child;
                }
            }
            else
            {
                r.Read();
            }
        }
        if (!r.EOF)
        {
            r.Read();
        }
        return e;
    }
    private static void Apply(UiElement e, string name, string value, int line, List<XamlDiagnostic> d, UiResourceStore? resources) { switch (name) { case "Width" when TryFloat(value, out var w): e.Width = w; break; case "Height" when TryFloat(value, out var h): e.Height = h; break; case "Fill" when bool.TryParse(value, out var fill): e.Fill = fill; break; case "Background" when TryColor(value, out var color): e.Background = color; break; case "Orientation" when e is StackPanel s && Enum.TryParse(value, true, out UiOrientation orientation): s.Orientation = orientation; break; case "Text" when e is TextBlock text: text.Text = value; break; case "FontKey" when e is TextBlock text: text.FontKey = value; break; case "FontSize" when e is TextBlock text && TryFloat(value, out var size): text.FontSize = size; break; case "Foreground" when e is TextBlock text && TryColor(value, out var fg): text.Foreground = fg; break; case "ForegroundResource" when e is TextBlock text && resources is not null: text.SetStyleResource("Foreground", resources, new(value), UiDirtyFlags.Visual); break; case "ForegroundResource": d.Add(new("XAML004", "ForegroundResource requires a resource store.", line, 1)); break; case "Padding" when TryThickness(value, out var padding): e.Padding = padding; break; case "StyleKey": e.StyleKey = value; break; case "TemplateKey": e.TemplateKey = value; break; case "AutomationName": e.AutomationName = value; break; case "AutomationRole" when Enum.TryParse(value, true, out UiAutomationRole role): e.AutomationRole = role; break; case "IsEnabled" when bool.TryParse(value, out var enabled): e.IsEnabled = enabled; break; case "IsSelected" when bool.TryParse(value, out var selected): e.IsSelected = selected; break; default: d.Add(new("XAML003", $"Unsupported property '{name}'.", line, 1)); break; } }
    private static bool TryFloat(string value, out float result) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static bool TryThickness(string value, out UiThickness result) { var parts = value.Split(',', StringSplitOptions.TrimEntries); result = default; if (parts.Length != 4) { return false; } var values = new float[4]; for (var i = 0; i < 4; i++) { if (!TryFloat(parts[i], out values[i])) { return false; } } result = new(values[0], values[1], values[2], values[3]); return true; }
    private static bool TryColor(string value, out UiColor color) { color = default; if (value.Length != 7 || value[0] != '#') { return false; } try { color = new(Convert.ToByte(value[1..3], 16), Convert.ToByte(value[3..5], 16), Convert.ToByte(value[5..7], 16)); return true; } catch (FormatException) { return false; } }
}
