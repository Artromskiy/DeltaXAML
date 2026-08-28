using System.Globalization;
using System.Text;
using Delta.Diagnostics;
using Delta.XAML;
using DeltaXAML.Compiler;

namespace DeltaXAML.Generator;

internal readonly record struct ArtifactDiagnostic(
    string Code,
    string Message,
    SourceRange? Location);

/// <summary>Emits a deterministic companion factory from the compiler's semantic plan.</summary>
/// <remarks>This is a cold build-time emitter. It produces direct typed calls and never discovers types at runtime.</remarks>
internal static class CSharpArtifactEmitter
{
    internal static bool TryEmit(
        XamlDocumentPlan plan,
        XamlSemanticRegistry registry,
        string namespaceName,
        string className,
        out string source,
        out ArtifactDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(className);

        source = string.Empty;
        if (plan.Root is not { } root)
        {
            diagnostic = new("DXAMLGEN000", "A XAML document must have a root element before a companion can be emitted.", null);
            return false;
        }

        var nodes = new List<XamlObjectPlan>();
        Flatten(root, nodes);
        for (var i = 0; i < nodes.Count; i++)
        {
            if (!registry.TryResolveType(nodes[i].Type, out var type))
            {
                diagnostic = new("DXAMLGEN001", $"No generated factory is registered for '{nodes[i].Name.LocalName}'.", nodes[i].Range);
                return false;
            }

            if (string.IsNullOrWhiteSpace(type.FactoryExpression))
            {
                diagnostic = new("DXAMLGEN001", $"Type '{type.Name.LocalName}' has no compile-time factory expression.", nodes[i].Range);
                return false;
            }

            if (!TryValidateFactory(type.FactoryExpression, out var factoryError))
            {
                diagnostic = new("DXAMLGEN001", factoryError, nodes[i].Range);
                return false;
            }

            for (var memberIndex = 0; memberIndex < nodes[i].Members.Length; memberIndex++)
            {
                if (!CanEmitMember(nodes[i].Members[memberIndex], out var memberError))
                {
                    diagnostic = new("DXAMLGEN002", memberError, nodes[i].Members[memberIndex].Range);
                    return false;
                }
            }
        }

        var writer = new StringBuilder(2048);
        writer.AppendLine("#nullable enable");
        writer.Append("namespace ").Append(namespaceName).AppendLine(";");
        writer.AppendLine();
        writer.Append("public sealed class ").Append(className).AppendLine();
        writer.AppendLine("{");
        writer.AppendLine("    private readonly global::Delta.XAML.UiElement[] _scopeElements;");
        writer.AppendLine("    public global::Delta.XAML.UiDocument Document { get; }");
        writer.AppendLine();
        writer.Append("    public ").Append(className).AppendLine("(global::Delta.Text.Contract.ITextService textService)");
        writer.AppendLine("    {");
        writer.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(textService);");

        for (var i = 0; i < nodes.Count; i++)
        {
            if (!registry.TryResolveType(nodes[i].Type, out var type))
            {
                diagnostic = new("DXAMLGEN001", $"No generated factory is registered for '{nodes[i].Name.LocalName}'.", nodes[i].Range);
                return false;
            }

            writer.Append("        var node").Append(i).Append(" = ").Append(type.FactoryExpression).AppendLine(";");
            for (var memberIndex = 0; memberIndex < nodes[i].Members.Length; memberIndex++)
            {
                EmitMember(writer, i, nodes[i].Members[memberIndex]);
            }
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            var childCount = nodes[i].Children.Length;
            for (var childIndex = 0; childIndex < childCount; childIndex++)
            {
                var child = nodes[i].Children[childIndex];
                var childPosition = FindNode(nodes, child);
                if (childPosition < 0)
                {
                    diagnostic = new("DXAMLGEN003", "A child plan is not present in the flattened semantic tree.", child.Range);
                    return false;
                }

                if (!registry.TryResolveType(nodes[i].Type, out var parentType))
                {
                    diagnostic = new("DXAMLGEN001", $"No generated factory is registered for '{nodes[i].Name.LocalName}'.", nodes[i].Range);
                    return false;
                }

                if (!TryEmitAttachment(writer, i, childPosition, parentType.Name.LocalName, child.Range, out var attachmentError))
                {
                    diagnostic = new("DXAMLGEN003", attachmentError, child.Range);
                    return false;
                }
            }
        }

        writer.Append("        Document = new global::Delta.XAML.UiDocument(node0, textService);").AppendLine();
        var namedNodes = nodes.Where(static node => node.ScopeName is not null).ToList();
        writer.Append("        _scopeElements = new global::Delta.XAML.UiElement[]").AppendLine();
        writer.AppendLine("        {");
        for (var i = 0; i < namedNodes.Count; i++)
        {
            writer.Append("            node").Append(FindNode(nodes, namedNodes[i])).AppendLine(",");
        }

        writer.AppendLine("        };");
        writer.AppendLine("    }");
        writer.AppendLine();
        writer.AppendLine("    public bool TryFindName(string name, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out global::Delta.XAML.UiElement? element)");
        writer.AppendLine("    {");
        writer.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(name);");
        for (var i = 0; i < namedNodes.Count; i++)
        {
            writer.Append("        if (name == ").Append(Quote(namedNodes[i].ScopeName ?? string.Empty)).AppendLine(")");
            writer.AppendLine("        {");
            writer.Append("            element = _scopeElements[").Append(i).AppendLine("]; ");
            writer.AppendLine("            return true;");
            writer.AppendLine("        }");
        }

        writer.AppendLine("        element = null;");
        writer.AppendLine("        return false;");
        writer.AppendLine("    }");
        writer.AppendLine("}");
        source = writer.ToString();
        diagnostic = null;
        return true;
    }

    private static void Flatten(XamlObjectPlan node, List<XamlObjectPlan> nodes)
    {
        nodes.Add(node);
        for (var i = 0; i < node.Children.Length; i++)
        {
            Flatten(node.Children[i], nodes);
        }
    }

    private static int FindNode(List<XamlObjectPlan> nodes, XamlObjectPlan target)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], target))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryValidateFactory(string factory, out string error)
    {
        if (factory.Contains("Activator", StringComparison.Ordinal) ||
            factory.Contains("Type.", StringComparison.Ordinal) ||
            factory.Contains("Assembly", StringComparison.Ordinal) ||
            factory.Contains("GetType", StringComparison.Ordinal))
        {
            error = "A generated factory must be a direct construction expression without reflection or assembly discovery.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool CanEmitMember(XamlMemberPlan member, out string error)
    {
        if (member.Value.Kind is XamlValueKind.Binding or XamlValueKind.ResourceReference)
        {
            error = $"Property '{member.Name}' uses a binding/resource plan; compiled binding/resource emission belongs to a later compile slice.";
            return false;
        }

        if (!IsSupportedProperty(member.Name))
        {
            error = $"Property '{member.Name}' has no typed library setter in the current compile slice.";
            return false;
        }

        if (!IsIdentifier(member.Name))
        {
            error = $"Property '{member.Name}' is not a valid generated member identifier.";
            return false;
        }

        if (!TryLiteralExpression(member.Name, member.Value, out _, out error))
        {
            return false;
        }

        return true;
    }

    private static bool IsSupportedProperty(string name) => name switch
    {
        "Width" or "Height" or "Background" or "Padding" or "Fill" or "IsEnabled" or "IsSelected" or
        "StyleKey" or "TemplateKey" or "Text" or "FontKey" or "FontSize" or "Foreground" or
        "Minimum" or "Maximum" or "Value" or "Orientation" or "Columns" or "Rows" => true,
        _ => false,
    };

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || (!char.IsLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!char.IsLetterOrDigit(value[i]) && value[i] != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static void EmitMember(StringBuilder writer, int nodeIndex, XamlMemberPlan member)
    {
        if (!TryLiteralExpression(member.Name, member.Value, out var expression, out var error))
        {
            throw new InvalidOperationException(error);
        }

        writer.Append("        node").Append(nodeIndex).Append('.');
        switch (member.Name)
        {
            case "Columns":
                writer.Append("SetColumns(").Append(expression).AppendLine(");");
                break;
            case "Rows":
                writer.Append("SetRows(").Append(expression).AppendLine(");");
                break;
            case "Value":
                writer.Append("Initialize(").Append(expression).AppendLine(");");
                break;
            default:
                writer.Append(member.Name).Append(" = ").Append(expression).AppendLine(";");
                break;
        }
    }

    private static bool TryEmitAttachment(
        StringBuilder writer,
        int parentIndex,
        int childIndex,
        string parentType,
        SourceRange range,
        out string error)
    {
        switch (parentType)
        {
            case "Panel":
            case "StackPanel":
            case "Grid":
            case "ItemsControl":
                writer.Append("        node").Append(parentIndex).Append(".Add(node").Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case "Border":
                writer.Append("        node").Append(parentIndex).Append(".SetChild(node").Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case "ContentControl":
            case "Button":
            case "ToggleButton":
            case "ScrollViewer":
                writer.Append("        node").Append(parentIndex).Append(".SetContent(node").Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            default:
                error = $"Type '{parentType}' has no generated child/content attachment operation.";
                return false;
        }
    }

    private static bool TryLiteralExpression(
        string propertyName,
        XamlValuePlan value,
        out string expression,
        out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        if (value.Kind != XamlValueKind.Invalid && value.Kind != XamlValueKind.String && value.Kind != XamlValueKind.Boolean &&
            value.Kind != XamlValueKind.Single && value.Kind != XamlValueKind.Double && value.Kind != XamlValueKind.Color &&
            value.Kind != XamlValueKind.Thickness && value.Kind != XamlValueKind.GridLengthList && value.Kind != XamlValueKind.Enum)
        {
            error = $"Property '{propertyName}' is not a literal value in this compile slice.";
            return false;
        }

        var literal = value.Literal.CanonicalText;
        switch (value.Kind)
        {
            case XamlValueKind.String:
                expression = Quote(literal);
                return true;
            case XamlValueKind.Boolean when bool.TryParse(literal, out var boolean):
                expression = boolean ? "true" : "false";
                return true;
            case XamlValueKind.Single when float.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) && float.IsFinite(single):
                expression = single.ToString("R", CultureInfo.InvariantCulture) + "f";
                return true;
            case XamlValueKind.Double when double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number):
                expression = number.ToString("R", CultureInfo.InvariantCulture) + "d";
                return true;
            case XamlValueKind.Color:
                return TryColor(literal, out expression, out error);
            case XamlValueKind.Thickness:
                return TryThickness(literal, out expression, out error);
            case XamlValueKind.GridLengthList:
                return TryGridLengths(literal, out expression, out error);
            case XamlValueKind.Enum when propertyName == "Orientation" && (literal == "Horizontal" || literal == "Vertical"):
                expression = "global::Delta.XAML.UiOrientation." + literal;
                return true;
            default:
                error = $"Literal '{literal}' is not supported for property '{propertyName}'.";
                return false;
        }
    }

    private static bool TryColor(string value, out string expression, out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        if (value.Length is not (7 or 9) || value[0] != '#')
        {
            error = $"Color literal '{value}' is invalid.";
            return false;
        }

        var alpha = (byte)255;
        if (!TryByte(value, 1, out var red) || !TryByte(value, 3, out var green) || !TryByte(value, 5, out var blue))
        {
            error = $"Color literal '{value}' is invalid.";
            return false;
        }

        if (value.Length == 9 && !TryByte(value, 7, out alpha))
        {
            error = $"Color literal '{value}' is invalid.";
            return false;
        }

        expression = $"new global::Delta.XAML.UiColor({red}, {green}, {blue}, {alpha})";
        return true;
    }

    private static bool TryByte(string value, int start, out byte result)
    {
        return byte.TryParse(value.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryThickness(string value, out string expression, out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            error = $"Thickness literal '{value}' is invalid.";
            return false;
        }

        var values = new float[4];
        for (var i = 0; i < values.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !float.IsFinite(values[i]))
            {
                error = $"Thickness literal '{value}' is invalid.";
                return false;
            }
        }

        expression = $"new global::Delta.XAML.UiThickness({values[0].ToString("R", CultureInfo.InvariantCulture)}f, {values[1].ToString("R", CultureInfo.InvariantCulture)}f, {values[2].ToString("R", CultureInfo.InvariantCulture)}f, {values[3].ToString("R", CultureInfo.InvariantCulture)}f)";
        return true;
    }

    private static bool TryGridLengths(string value, out string expression, out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = $"Grid length list '{value}' is invalid.";
            return false;
        }

        var values = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (string.Equals(parts[i], "Auto", StringComparison.OrdinalIgnoreCase))
            {
                values[i] = "global::Delta.XAML.UiGridLength.Auto";
            }
            else if (parts[i].EndsWith('*') && float.TryParse(parts[i][..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) && float.IsFinite(weight) && weight > 0)
            {
                values[i] = $"global::Delta.XAML.UiGridLength.Star({weight.ToString("R", CultureInfo.InvariantCulture)}f)";
            }
            else if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels) && float.IsFinite(pixels) && pixels >= 0)
            {
                values[i] = $"global::Delta.XAML.UiGridLength.Pixel({pixels.ToString("R", CultureInfo.InvariantCulture)}f)";
            }
            else
            {
                error = $"Grid length list '{value}' is invalid.";
                return false;
            }
        }

        expression = "new global::Delta.XAML.UiGridLength[] { " + string.Join(", ", values) + " }";
        return true;
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(character))
                    {
                        builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
