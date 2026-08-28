using System.Globalization;
using System.Xml;
using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;
namespace DeltaXAML.Internal;

internal readonly record struct XamlDiagnostic(string Code, string Message, int Line, int Column);
internal sealed record XamlLoadResult(UiElement? Root, IReadOnlyList<XamlDiagnostic> Diagnostics) { public bool Success => Root is not null && Diagnostics.Count == 0; }
/// <summary>Cold dialect parser used by the explicit tooling/library loader.</summary>
/// <remarks>Generated artifacts bypass this parser and construct the same retained tree directly.</remarks>
internal static class XamlDialectParser
{
    internal static XamlLoadResult ParseForAdapter(string source, Func<string, string, UiElement?>? factory, UiResourceStore? resources)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diagnostics = new List<XamlDiagnostic>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true });
            reader.MoveToContent();
            return new(Read(reader, diagnostics, resources, factory), diagnostics);
        }
        catch (XmlException exception)
        {
            diagnostics.Add(new("XAML001", exception.Message, exception.LineNumber, exception.LinePosition));
            return new(null, diagnostics);
        }
    }

    private static UiElement? Read(XmlReader r, List<XamlDiagnostic> d, UiResourceStore? resources, Func<string, string, UiElement?>? factory)
    {
        var line = (r as IXmlLineInfo)?.LineNumber ?? 0; UiElement? e = r.LocalName switch { "Panel" => UiPanelGenerated.Create(), "StackPanel" => UiStackPanelGenerated.Create(), "ItemsControl" => UiItemsControlGenerated.Create(), "Border" => UiBorderGenerated.Create(), "Grid" => UiGridGenerated.Create(), "ContentControl" => UiContentControlGenerated.Create(), "Button" => UiButtonGenerated.Create(), "ToggleButton" => UiToggleButtonGenerated.Create(), "TextBlock" => UiTextBlockGenerated.Create(), "TextBox" => UiTextBoxGenerated.Create(), "NumericEditor" => UiNumericEditorGenerated.Create(), "ScrollViewer" => UiScrollViewerGenerated.Create(), _ => null };
        if (e is null && factory is not null)
        {
            e = factory(r.NamespaceURI, r.LocalName);
        }
        if (e is null) { d.Add(new("XAML002", $"Unsupported element '{r.LocalName}'.", line, 1)); if (!r.IsEmptyElement) { r.Skip(); } return null; } while (r.MoveToNextAttribute())
        {
            if (r.Prefix == "xmlns" || r.Name == "xmlns")
            {
                continue;
            }

            Apply(e, r.LocalName, r.Value, line, d, resources);
        }
        r.MoveToElement(); if (r.IsEmptyElement) { r.Read(); return e; }
        r.Read(); while (!r.EOF && r.NodeType != XmlNodeType.EndElement)
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                var child = Read(r, d, resources, factory); if (child is not null && e is IUiPanel p)
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
    private static void Apply(UiElement e, string name, string value, int line, List<XamlDiagnostic> d, UiResourceStore? resources)
    {
        if (XamlBindingParser.TryParse(value, out var binding, out var bindingError))
        {
            e.AddBindingSpec(binding with { Property = name });
            return;
        }

        if (TryParseResourceReference(value, out var resourceKey, out var dynamicResource))
        {
            if (resources is null)
            {
                d.Add(new("XAML004", $"Resource reference '{resourceKey}' requires a resource store.", line, 1));
                return;
            }

            if (!resources.TryResolve(resourceKey, out _, out var resourceDiagnostic))
            {
                d.Add(new("XAML006", resourceDiagnostic ?? $"Resource '{resourceKey}' was not resolved.", line, 1));
                return;
            }

            if (dynamicResource)
            {
                e.SetStyleResource(name, resources, new(resourceKey), InvalidationFor(name));
            }
            else
            {
                resources.TryResolve(resourceKey, out var resolved, out _);
                e.SetStyle(name, resolved, InvalidationFor(name));
            }

            return;
        }

        if (bindingError is not null)
        {
            d.Add(new("XAML008", bindingError, line, 1));
            return;
        }

        switch (name)
        {
            case "Width" when TryFloat(value, out var w): e.Width = w; break;
            case "Height" when TryFloat(value, out var h): e.Height = h; break;
            case "Fill" when bool.TryParse(value, out var fill): e.Fill = fill; break;
            case "Background" when TryColor(value, out var color): e.Background = color; break;
            case "Orientation" when e is StackPanel s && Enum.TryParse(value, true, out UiOrientation orientation): s.Orientation = orientation; break;
            case "Columns" when e is Grid grid && TryGridLengths(value, out var columns): grid.SetColumns(columns); break;
            case "Rows" when e is Grid grid && TryGridLengths(value, out var rows): grid.SetRows(rows); break;
            case "Text" when e is TextBlock text: text.Text = value; break;
            case "FontKey" when e is TextBlock text: text.FontKey = value; break;
            case "FontSize" when e is TextBlock text && TryFloat(value, out var size): text.FontSize = size; break;
            case "Minimum" when e is NumericEditor numeric && TryFloat(value, out var minimum): numeric.Min = minimum; break;
            case "Maximum" when e is NumericEditor numeric && TryFloat(value, out var maximum): numeric.Max = maximum; break;
            case "Value" when e is NumericEditor numeric && TryFloat(value, out var numericValue): numeric.Initialize(numericValue); break;
            case "Foreground" when e is TextBlock text && TryColor(value, out var fg): text.Foreground = fg; break;
            case "ForegroundResource" when e is TextBlock text && resources is not null: text.SetStyleResource("Foreground", resources, new(NormalizeResourceKey(value)), InvalidationFor("Foreground")); break;
            case "ForegroundResource": d.Add(new("XAML004", "ForegroundResource requires a resource store.", line, 1)); break;
            case "Padding" when TryThickness(value, out var padding): e.Padding = padding; break;
            case "StyleKey": e.StyleKey = value; break;
            case "TemplateKey": e.TemplateKey = value; break;
            case "AutomationName": e.AutomationName = value; break;
            case "AutomationRole" when Enum.TryParse(value, true, out UiAutomationRole role): e.AutomationRole = role; break;
            case "IsEnabled" when bool.TryParse(value, out var enabled): e.IsEnabled = enabled; break;
            case "IsSelected" when bool.TryParse(value, out var selected): e.IsSelected = selected; break;
            default: d.Add(new("XAML003", $"Unsupported property '{name}'.", line, 1)); break;
        }
    }
    internal static bool TryParseResourceReference(string value, out string key, out bool dynamicResource)
    {
        key = string.Empty;
        dynamicResource = false;
        const string dynamicPrefix = "{DynamicResource ";
        const string staticPrefix = "{StaticResource ";
        var prefix = value.StartsWith(dynamicPrefix, StringComparison.Ordinal)
            ? dynamicPrefix
            : value.StartsWith(staticPrefix, StringComparison.Ordinal)
                ? staticPrefix
                : string.Empty;
        if (prefix.Length == 0)
        {
            return false;
        }

        if (!value.EndsWith('}'))
        {
            return false;
        }

        dynamicResource = prefix == dynamicPrefix;
        key = value[prefix.Length..^1].Trim();
        return key.Length != 0;
    }
    private static UiDirtyFlags InvalidationFor(string name) => name switch
    {
        "Text" or "FontKey" or "FontSize" => UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "Foreground" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "Width" or "Height" or "Padding" => UiDirtyFlags.Measure | UiDirtyFlags.Visual,
        _ => UiDirtyFlags.Visual,
    };
    private static string NormalizeResourceKey(string value) => Guid.TryParse(value, out var resourceId) ? resourceId.ToString("D") : value;
    private static bool TryFloat(string value, out float result) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static bool TryGridLengths(string value, out GridLength[] result)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        result = new GridLength[parts.Length];
        if (parts.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (string.Equals(part, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                result[i] = GridLength.Auto;
            }
            else if (part.EndsWith('*'))
            {
                var weight = part.Length == 1 ? 1 : TryFloat(part[..^1], out var parsed) ? parsed : float.NaN;
                if (float.IsNaN(weight) || weight <= 0)
                {
                    result = Array.Empty<GridLength>();
                    return false;
                }

                result[i] = GridLength.Star(weight);
            }
            else if (TryFloat(part, out var pixels) && pixels >= 0)
            {
                result[i] = GridLength.Fixed(pixels);
            }
            else
            {
                result = Array.Empty<GridLength>();
                return false;
            }
        }

        return true;
    }
    private static bool TryThickness(string value, out UiThickness result) { var parts = value.Split(',', StringSplitOptions.TrimEntries); result = default; if (parts.Length != 4) { return false; } var values = new float[4]; for (var i = 0; i < 4; i++) { if (!TryFloat(parts[i], out values[i])) { return false; } } result = new(values[0], values[1], values[2], values[3]); return true; }
    private static bool TryColor(string value, out UiColor color) { color = default; if (value.Length != 7 || value[0] != '#') { return false; } try { color = new(Convert.ToByte(value[1..3], 16), Convert.ToByte(value[3..5], 16), Convert.ToByte(value[5..7], 16)); return true; } catch (FormatException) { return false; } }
}
