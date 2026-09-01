using System.Globalization;
using System.Xml;
using Delta.XAML.Contract;
using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;
namespace DeltaXAML.Internal;

internal readonly record struct InterpretedXamlDiagnostic(string Code, string Message, int Line, int Column);
internal sealed record InterpretedXamlResult(UiElement? Root, IReadOnlyList<InterpretedXamlDiagnostic> Diagnostics);
/// <summary>Cold source reader selected only by the explicit <c>IXamlLoader</c> API.</summary>
/// <remarks>It constructs canonical descriptor-backed elements; generated production artifacts bypass XML inflation.</remarks>
internal static class InterpretedXamlReader
{
    internal static InterpretedXamlResult Read(string source, Func<string, string, UiElement?>? factory, UiResourceStore? resources)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diagnostics = new List<InterpretedXamlDiagnostic>();
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

    private static UiElement? Read(XmlReader r, List<InterpretedXamlDiagnostic> d, UiResourceStore? resources, Func<string, string, UiElement?>? factory)
    {
        var line = (r as IXmlLineInfo)?.LineNumber ?? 0; UiElement? e = r.LocalName switch { "Panel" => UiPanelGenerated.Create(), "StackPanel" => UiStackPanelGenerated.Create(), "ItemsControl" => UiItemsControlGenerated.Create(), "Border" => UiBorderGenerated.Create(), "Grid" => UiGridGenerated.Create(), "ContentControl" => UiContentControlGenerated.Create(), "Button" => UiButtonGenerated.Create(), "ToggleButton" => UiToggleButtonGenerated.Create(), "TextBlock" => TextBlockGenerated.Create(), "TextBox" => TextBoxGenerated.Create(), "NumericEditor" => UiNumericEditorGenerated.Create(), "ScrollViewer" => UiScrollViewerGenerated.Create(), _ => null };
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
                var child = Read(r, d, resources, factory); if (child is not null && e is Panel or StackPanel or Border or Grid)
                {
                    e.Add(child);
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
    private static void Apply(UiElement e, string name, string value, int line, List<InterpretedXamlDiagnostic> d, UiResourceStore? resources)
    {
        if (!SupportsProperty(e, name))
        {
            d.Add(new("XAML003", $"Unsupported property '{name}' on '{e.TypeName}'.", line, 1));
            return;
        }

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

            resources.TryResolve(resourceKey, out var resolvedResource, out _);
            if (!IsResourceValueCompatible(name, resolvedResource))
            {
                d.Add(new("XAML010", $"Resource '{resourceKey}' is not compatible with property '{name}' on '{e.TypeName}'.", line, 1));
                return;
            }

            if (dynamicResource)
            {
                e.SetStyleResource(name, resources, new(resourceKey), InvalidationFor(name));
            }
            else
            {
                e.SetStyle(name, resolvedResource, InvalidationFor(name));
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
            case "BorderColor" when TryColor(value, out var borderColor): e.BorderColor = borderColor; break;
            case "BorderWidth" when TryFloat(value, out var borderWidth): e.BorderWidth = borderWidth; break;
            case "CornerRadius" when TryCornerRadii(value, out var cornerRadii): e.CornerRadius = cornerRadii; break;
            case "CornerRadius": d.Add(new("XAML003", $"Invalid CornerRadius '{value}'. Expected one value or four comma-separated values.", line, 1)); break;
            case "Orientation" when e is StackPanel s && Enum.TryParse(value, true, out UiOrientation orientation): s.Orientation = orientation; break;
            case "Columns" when e is Grid grid && TryGridLengths(value, out var columns): grid.SetColumns(columns); break;
            case "Rows" when e is Grid grid && TryGridLengths(value, out var rows): grid.SetRows(rows); break;
            case "Text": e.SetLocal("Text", value, InvalidationFor("Text")); break;
            case "FontKey": e.SetLocal("FontKey", value, InvalidationFor("FontKey")); break;
            case "FontSize" when TryFloat(value, out var size): e.SetLocal("FontSize", size, InvalidationFor("FontSize")); break;
            case "HorizontalTextAlignment" when Enum.TryParse(value, true, out Delta.XAML.UiTextHorizontalAlignment horizontal) && horizontal != Delta.XAML.UiTextHorizontalAlignment.Unknown: e.SetLocal(name, horizontal, InvalidationFor(name)); break;
            case "VerticalTextAlignment" when Enum.TryParse(value, true, out Delta.XAML.UiTextVerticalAlignment vertical) && vertical != Delta.XAML.UiTextVerticalAlignment.Unknown: e.SetLocal(name, vertical, InvalidationFor(name)); break;
            case "TextWrapping" when Enum.TryParse(value, true, out Delta.XAML.UiTextWrapping wrapping) && wrapping != Delta.XAML.UiTextWrapping.Unknown: e.SetLocal(name, wrapping, InvalidationFor(name)); break;
            case "TextTrimming" when Enum.TryParse(value, true, out Delta.XAML.UiTextTrimming trimming) && trimming != Delta.XAML.UiTextTrimming.Unknown: e.SetLocal(name, trimming, InvalidationFor(name)); break;
            case "MaxLines" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLines) && maxLines >= 0: e.SetLocal(name, maxLines, InvalidationFor(name)); break;
            case "LineHeight" when TryFloat(value, out var lineHeight) && lineHeight >= 0: e.SetLocal(name, lineHeight, InvalidationFor(name)); break;
            case "FontWeight" when Enum.TryParse(value, true, out Delta.XAML.UiFontWeight weight) && weight != Delta.XAML.UiFontWeight.Unknown: e.SetLocal(name, weight, InvalidationFor(name)); break;
            case "FontStyle" when Enum.TryParse(value, true, out Delta.XAML.UiFontStyle style) && style != Delta.XAML.UiFontStyle.Unknown: e.SetLocal(name, style, InvalidationFor(name)); break;
            case "TextDecorations" when TryTextDecorations(value, out var decorations): e.SetLocal(name, decorations, InvalidationFor(name)); break;
            case "PlaceholderText" when e is TextBox or NumericEditor: e.SetLocal(name, value, InvalidationFor(name)); break;
            case "IsReadOnly" when e is TextBox or NumericEditor && bool.TryParse(value, out var isReadOnly): e.SetLocal(name, isReadOnly, InvalidationFor(name)); break;
            case "AcceptsReturn" when e is TextBox or NumericEditor && bool.TryParse(value, out var acceptsReturn): e.SetLocal(name, acceptsReturn, InvalidationFor(name)); break;
            case "MaxLength" when e is TextBox or NumericEditor && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLength) && maxLength >= 0: e.SetLocal(name, maxLength, InvalidationFor(name)); break;
            case "Minimum" when e is NumericEditor numeric && TryFloat(value, out var minimum): numeric.Min = minimum; break;
            case "Maximum" when e is NumericEditor numeric && TryFloat(value, out var maximum): numeric.Max = maximum; break;
            case "Value" when e is NumericEditor numeric && TryFloat(value, out var numericValue): numeric.Initialize(numericValue); break;
            case "Foreground" when TryColor(value, out var fg): e.SetLocal("Foreground", fg, InvalidationFor("Foreground")); break;
            case "OutlineColor" when TryColor(value, out var outlineColor): e.SetLocal("OutlineColor", outlineColor, InvalidationFor("OutlineColor")); break;
            case "OutlineWidth" when TryFloat(value, out var outlineWidth): e.SetLocal("OutlineWidth", outlineWidth, InvalidationFor("OutlineWidth")); break;
            case "TextEffect" when Guid.TryParse(value, out var effect): e.SetLocal("TextEffect", new UiResourceId(effect), InvalidationFor("TextEffect")); break;
            case "ForegroundResource" when resources is not null: e.SetStyleResource("Foreground", resources, new(NormalizeResourceKey(value)), InvalidationFor("Foreground")); break;
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

    private static bool SupportsProperty(UiElement element, string name)
    {
        if (name is "Width" or "Height" or "Fill" or "Background" or "BorderColor" or "BorderWidth" or "CornerRadius" or "Padding" or
            "StyleKey" or "TemplateKey" or "AutomationName" or "AutomationRole" or
            "IsEnabled" or "IsSelected" or "BackgroundBrush")
        {
            return true;
        }

        if (element is TextBlock or TextBox or NumericEditor &&
            name is "Text" or "FontKey" or "FontSize" or "Foreground" or "ForegroundResource" or "OutlineColor" or "OutlineWidth" or "TextEffect" or
            "HorizontalTextAlignment" or "VerticalTextAlignment" or "TextWrapping" or "TextTrimming" or "MaxLines" or "LineHeight" or
            "FontWeight" or "FontStyle" or "TextDecorations")
        {
            return true;
        }

        if (element is TextBox or NumericEditor && name is "PlaceholderText" or "IsReadOnly" or "AcceptsReturn" or "MaxLength")
        {
            return true;
        }

        return (element is StackPanel && name == "Orientation") ||
               (element is Grid && name is "Columns" or "Rows") ||
               (element is NumericEditor && name is "Minimum" or "Maximum" or "Value");
    }

    private static bool IsResourceValueCompatible(string property, object? value) => property switch
    {
        "Background" or "BorderColor" or "Foreground" or "OutlineColor" => value is UiColor or Delta.XAML.UiColor,
        "BorderWidth" or "OutlineWidth" => value is
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal,
        "CornerRadius" => value is Delta.XAML.UiCornerRadii,
        "TextEffect" => value is UiResourceId,
        "BackgroundBrush" => value is Delta.XAML.UiBrush,
        "Padding" => value is UiThickness or Delta.XAML.UiThickness,
        "Width" or "Height" or "FontSize" or "Minimum" or "Maximum" or "Value" => value is
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal,
        "Fill" or "IsEnabled" or "IsSelected" or "IsReadOnly" or "AcceptsReturn" => value is bool,
        "MaxLines" or "MaxLength" => value is int or byte or sbyte or short or ushort or uint,
        "HorizontalTextAlignment" => value is Delta.XAML.UiTextHorizontalAlignment,
        "VerticalTextAlignment" => value is Delta.XAML.UiTextVerticalAlignment,
        "TextWrapping" => value is Delta.XAML.UiTextWrapping,
        "TextTrimming" => value is Delta.XAML.UiTextTrimming,
        "FontWeight" => value is Delta.XAML.UiFontWeight,
        "FontStyle" => value is Delta.XAML.UiFontStyle,
        "TextDecorations" => value is Delta.XAML.UiTextDecorations,
        "LineHeight" => value is float or double or int,
        "Text" or "FontKey" or "StyleKey" or "TemplateKey" or "AutomationName" => value is string,
        _ => true,
    };
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
        "FontWeight" or "FontStyle" => UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "TextDecorations" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "TextWrapping" or "MaxLines" or "LineHeight" => UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual,
        "HorizontalTextAlignment" or "VerticalTextAlignment" or "TextTrimming" => UiDirtyFlags.Arrange | UiDirtyFlags.Visual,
        "PlaceholderText" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "IsReadOnly" or "AcceptsReturn" or "MaxLength" => UiDirtyFlags.Visual,
        "Foreground" or "OutlineColor" or "OutlineWidth" or "TextEffect" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "BorderColor" or "BorderWidth" or "CornerRadius" => UiDirtyFlags.Visual,
        "Width" or "Height" or "Padding" => UiDirtyFlags.Measure | UiDirtyFlags.Visual,
        _ => UiDirtyFlags.Visual,
    };
    private static string NormalizeResourceKey(string value) => Guid.TryParse(value, out var resourceId) ? resourceId.ToString("D") : value;
    private static bool TryFloat(string value, out float result) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static bool TryTextDecorations(string value, out Delta.XAML.UiTextDecorations result)
    {
        result = Delta.XAML.UiTextDecorations.None;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            if (!Enum.TryParse(parts[i], true, out Delta.XAML.UiTextDecorations decoration) || decoration == Delta.XAML.UiTextDecorations.None)
            {
                result = default;
                return false;
            }

            result |= decoration;
        }

        return true;
    }
    private static bool TryCornerRadii(string value, out Delta.XAML.UiCornerRadii result)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        result = default;
        if (parts.Length == 1 && TryFloat(parts[0], out var uniform))
        {
            result = Delta.XAML.UiCornerRadii.Uniform(uniform);
            return result.IsFiniteNonNegative;
        }

        if (parts.Length != 4)
        {
            return false;
        }

        if (!TryFloat(parts[0], out var topLeft) ||
            !TryFloat(parts[1], out var topRight) ||
            !TryFloat(parts[2], out var bottomRight) ||
            !TryFloat(parts[3], out var bottomLeft))
        {
            return false;
        }

        result = new(topLeft, topRight, bottomRight, bottomLeft);
        return result.IsFiniteNonNegative;
    }
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
