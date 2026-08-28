using System.Globalization;
using System.Text;
using Delta.Diagnostics;
using Delta.XAML;
using Delta.XAML.Contract;
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

        var hasVisualRoot = !string.Equals(root.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal);
        var nodes = new List<XamlObjectPlan>();
        if (hasVisualRoot)
        {
            Flatten(root, nodes);
        }
        var resourceNodes = new List<XamlObjectPlan>();
        for (var resourceIndex = 0; resourceIndex < plan.Resources.Length; resourceIndex++)
        {
            Flatten(plan.Resources[resourceIndex].Value, resourceNodes);
        }

        var validationNodes = new List<XamlObjectPlan>(nodes.Count + resourceNodes.Count);
        validationNodes.AddRange(nodes);
        validationNodes.AddRange(resourceNodes);
        var bindingSites = new List<BindingSite>();
        string? bindingSourceType = null;
        for (var i = 0; i < validationNodes.Count; i++)
        {
            if (!registry.TryResolveType(validationNodes[i].Type, out var type))
            {
                diagnostic = new("DXAMLGEN001", $"No generated factory is registered for '{validationNodes[i].Name.LocalName}'.", validationNodes[i].Range);
                return false;
            }

            if (string.IsNullOrWhiteSpace(type.FactoryExpression))
            {
                diagnostic = new("DXAMLGEN001", $"Type '{type.Name.LocalName}' has no compile-time factory expression.", validationNodes[i].Range);
                return false;
            }

            if (!TryValidateFactory(type.FactoryExpression, out var factoryError))
            {
                diagnostic = new("DXAMLGEN001", factoryError, validationNodes[i].Range);
                return false;
            }

            for (var memberIndex = 0; memberIndex < validationNodes[i].Members.Length; memberIndex++)
            {
                var member = validationNodes[i].Members[memberIndex];
                if (!CanEmitMember(member, registry, out var memberError))
                {
                    diagnostic = new("DXAMLGEN002", memberError, member.Range);
                    return false;
                }

                if (i < nodes.Count && member.Value.Kind == XamlValueKind.Binding)
                {
                    if (!registry.TryResolveBinding(member.Value.Binding.Path, out var bindingDefinition))
                    {
                        diagnostic = new("DXAMLGEN002", $"Binding path '{member.Value.Binding.Path}' has no typed compile-time definition.", member.Range);
                        return false;
                    }

                    if (bindingSourceType is null)
                    {
                        bindingSourceType = bindingDefinition.SourceTypeName;
                    }
                    else if (!string.Equals(bindingSourceType, bindingDefinition.SourceTypeName, StringComparison.Ordinal))
                    {
                        diagnostic = new("DXAMLGEN002", "One generated artifact must use one typed binding source context.", member.Range);
                        return false;
                    }

                    bindingSites.Add(new(i, member, bindingDefinition));
                }
            }
        }

        for (var styleIndex = 0; styleIndex < plan.Styles.Length; styleIndex++)
        {
            var style = plan.Styles[styleIndex];
            if (!registry.TryResolveType(style.TargetType, out _))
            {
                diagnostic = new("DXAMLGEN004", $"Style '{style.Key}' targets an unknown type '{style.TargetType.LocalName}'.", style.Range);
                return false;
            }

            for (var setterIndex = 0; setterIndex < style.Setters.Length; setterIndex++)
            {
                var setter = style.Setters[setterIndex];
                if (setter.Value.Kind == XamlValueKind.Binding)
                {
                    diagnostic = new("DXAMLGEN002", "Compiled style setters cannot contain bindings.", setter.Range);
                    return false;
                }

                if (!CanEmitMember(setter, registry, out var setterError))
                {
                    diagnostic = new("DXAMLGEN002", setterError, setter.Range);
                    return false;
                }
            }

            for (var stateIndex = 0; stateIndex < style.VisualStates.Length; stateIndex++)
            {
                var state = style.VisualStates[stateIndex];
                for (var setterIndex = 0; setterIndex < state.Setters.Length; setterIndex++)
                {
                    var setter = state.Setters[setterIndex];
                    if (setter.Value.Kind == XamlValueKind.Binding)
                    {
                        diagnostic = new("DXAMLGEN002", "Compiled visual-state setters cannot contain bindings.", setter.Range);
                        return false;
                    }

                    if (!CanEmitMember(setter, registry, out var setterError))
                    {
                        diagnostic = new("DXAMLGEN002", setterError, setter.Range);
                        return false;
                    }
                }
            }
        }

        for (var templateIndex = 0; templateIndex < plan.Templates.Length; templateIndex++)
        {
            var templateNodes = new List<XamlObjectPlan>();
            Flatten(plan.Templates[templateIndex].Root, templateNodes);
            for (var nodeIndex = 0; nodeIndex < templateNodes.Count; nodeIndex++)
            {
                if (!registry.TryResolveType(templateNodes[nodeIndex].Type, out var templateType) ||
                    string.IsNullOrWhiteSpace(templateType.FactoryExpression))
                {
                    diagnostic = new("DXAMLGEN001", $"Template '{plan.Templates[templateIndex].Key}' contains a type without a compile-time factory.", templateNodes[nodeIndex].Range);
                    return false;
                }

                for (var memberIndex = 0; memberIndex < templateNodes[nodeIndex].Members.Length; memberIndex++)
                {
                    var member = templateNodes[nodeIndex].Members[memberIndex];
                    if (member.Value.Kind == XamlValueKind.Binding)
                    {
                        diagnostic = new("DXAMLGEN002", "Compiled template members cannot contain bindings.", member.Range);
                        return false;
                    }

                    if (!CanEmitMember(member, registry, out var memberError))
                    {
                        diagnostic = new("DXAMLGEN002", memberError, member.Range);
                        return false;
                    }
                }
            }
        }

        var writer = new StringBuilder(2048);
        writer.AppendLine("#nullable enable");
        writer.Append("namespace ").Append(namespaceName).AppendLine(";");
        writer.AppendLine();
        writer.Append("public sealed class ").Append(className).AppendLine(" : global::System.IDisposable");
        writer.AppendLine("{");
        writer.AppendLine("    private readonly global::Delta.XAML.UiElement[] _scopeElements;");
        if (plan.ResourceSlots.Length != 0)
        {
            writer.AppendLine("    private static readonly global::Delta.XAML.Contract.UiResourceId[] _resourceIds =");
            writer.AppendLine("    {");
            for (var slotIndex = 0; slotIndex < plan.ResourceSlots.Length; slotIndex++)
            {
                writer.Append("        ").Append(ResourceIdExpression(plan.ResourceSlots[slotIndex].Id)).AppendLine(",");
            }

            writer.AppendLine("    };");
        }

        for (var i = 0; i < bindingSites.Count; i++)
        {
            var binding = bindingSites[i];
            writer.Append("    private readonly global::Delta.XAML.UiCompiledBinding<")
                .Append(binding.Definition.SourceTypeName)
                .Append(", ")
                .Append(binding.Definition.ValueTypeName)
                .Append("> _binding")
                .Append(i)
                .AppendLine(";");
        }

        writer.AppendLine("    public global::Delta.XAML.UiResourceCatalog Resources { get; }");
        writer.AppendLine("    public global::Delta.XAML.UiTheme Theme { get; }");
        writer.Append("    public global::Delta.XAML.UiDocument").Append(hasVisualRoot ? string.Empty : "?").AppendLine(" Document { get; }");
        writer.AppendLine();
        writer.Append("    public ").Append(className).Append('(');
        if (bindingSourceType is not null)
        {
            writer.Append(bindingSourceType).Append(" context, ");
        }

        writer.AppendLine("global::Delta.Text.Contract.ITextService textService)");
        writer.AppendLine("    {");
        writer.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(textService);");
        if (bindingSourceType is not null)
        {
            writer.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(context);");
        }

        writer.AppendLine("        Resources = new global::Delta.XAML.UiResourceCatalog();");
        writer.AppendLine("        Theme = new global::Delta.XAML.UiTheme(Resources);");
        for (var i = 0; i < resourceNodes.Count; i++)
        {
            if (!registry.TryResolveType(resourceNodes[i].Type, out var resourceType))
            {
                diagnostic = new("DXAMLGEN001", $"No generated factory is registered for '{resourceNodes[i].Name.LocalName}'.", resourceNodes[i].Range);
                return false;
            }

            writer.Append("        var resource").Append(i).Append(" = ").Append(resourceType.FactoryExpression).AppendLine(";");
            for (var memberIndex = 0; memberIndex < resourceNodes[i].Members.Length; memberIndex++)
            {
                EmitMember(writer, i, resourceNodes[i].Members[memberIndex], "resource", plan.ResourceSlots);
            }
        }

        for (var resourceIndex = 0; resourceIndex < plan.Resources.Length; resourceIndex++)
        {
            var resourceRoot = plan.Resources[resourceIndex].Value;
            var rootIndex = FindNode(resourceNodes, resourceRoot);
            if (rootIndex < 0)
            {
                diagnostic = new("DXAMLGEN003", "A resource plan is not present in the flattened resource tree.", plan.Resources[resourceIndex].Range);
                return false;
            }

            writer.Append("        Resources.Set(").Append(ResourceSlotExpression(plan.Resources[resourceIndex].Id, plan.ResourceSlots)).Append(", resource").Append(rootIndex).AppendLine(");");
        }

        for (var i = 0; i < resourceNodes.Count; i++)
        {
            for (var childIndex = 0; childIndex < resourceNodes[i].Children.Length; childIndex++)
            {
                var childPosition = FindNode(resourceNodes, resourceNodes[i].Children[childIndex]);
                if (childPosition < 0)
                {
                    diagnostic = new("DXAMLGEN003", "A resource child is not present in the flattened resource tree.", resourceNodes[i].Children[childIndex].Range);
                    return false;
                }

                if (!registry.TryResolveType(resourceNodes[i].Type, out var resourceParentType))
                {
                    diagnostic = new("DXAMLGEN001", $"No generated factory is registered for '{resourceNodes[i].Name.LocalName}'.", resourceNodes[i].Range);
                    return false;
                }

                if (!TryEmitAttachment(writer, i, childPosition, resourceParentType.Name.LocalName, resourceNodes[i].Children[childIndex].Range, out var attachmentError, "resource"))
                {
                    diagnostic = new("DXAMLGEN003", attachmentError, resourceNodes[i].Children[childIndex].Range);
                    return false;
                }
            }
        }

        EmitStyles(writer, plan.Styles, plan.ResourceSlots);
        EmitTemplates(writer, plan.Templates, registry, plan.ResourceSlots);

        for (var i = 0; i < nodes.Count; i++)
        {
            if (!registry.TryResolveType(nodes[i].Type, out var type))
            {
                diagnostic = new("DXAMLGEN001", $"No generated factory is registered for '{nodes[i].Name.LocalName}'.", nodes[i].Range);
                return false;
            }

            writer.Append("        var node").Append(i).Append(" = ").Append(type.FactoryExpression).AppendLine(";");
            if (i == 0 && bindingSourceType is not null)
            {
                writer.AppendLine("        node0.BindingContext = context;");
            }

            for (var memberIndex = 0; memberIndex < nodes[i].Members.Length; memberIndex++)
            {
                EmitMember(writer, i, nodes[i].Members[memberIndex], "node", plan.ResourceSlots);
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

                if (!TryEmitAttachment(writer, i, childPosition, parentType.Name.LocalName, child.Range, out var attachmentError, "node"))
                {
                    diagnostic = new("DXAMLGEN003", attachmentError, child.Range);
                    return false;
                }
            }
        }

        for (var i = 0; i < bindingSites.Count; i++)
        {
            EmitBinding(writer, i, bindingSites[i]);
        }

        if (hasVisualRoot)
        {
            writer.AppendLine("        Theme.Apply(node0);");
            writer.Append("        Document = new global::Delta.XAML.UiDocument(node0, textService, null, Theme);").AppendLine();
        }
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
        writer.AppendLine();
        writer.AppendLine("    public void RefreshBindings()");
        writer.AppendLine("    {");
        for (var i = 0; i < bindingSites.Count; i++)
        {
            writer.Append("        _binding").Append(i).AppendLine(".NotifyChanged();");
        }

        writer.AppendLine("    }");
        writer.AppendLine();
        writer.AppendLine("    public void Dispose()");
        writer.AppendLine("    {");
        for (var i = 0; i < bindingSites.Count; i++)
        {
            writer.Append("        _binding").Append(i).AppendLine(".Dispose();");
        }

        if (hasVisualRoot)
        {
            writer.AppendLine("        Document.Dispose();");
        }
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

    private static bool CanEmitMember(XamlMemberPlan member, XamlSemanticRegistry registry, out string error)
    {
        error = string.Empty;
        if (member.Value.Kind == XamlValueKind.Binding)
        {
            if (!registry.TryResolveBinding(member.Value.Binding.Path, out var binding))
            {
                error = $"Binding path '{member.Value.Binding.Path}' has no typed compile-time definition.";
                return false;
            }

            if (member.Value.Binding.ConverterKey is not null || member.Value.Binding.StringFormat is not null)
            {
                error = $"Binding '{member.Value.Binding.Path}' uses a converter or format; only direct typed accessors are available in this slice.";
                return false;
            }

            if (member.Value.Binding.Mode == UiBindingMode.TwoWay && string.IsNullOrWhiteSpace(binding.WriteExpression))
            {
                error = $"Two-way binding '{member.Value.Binding.Path}' has no typed write expression.";
                return false;
            }

            if (!TryValidateBinding(binding, out error))
            {
                return false;
            }
        }
        else if (member.Value.Kind == XamlValueKind.ResourceReference)
        {
            if (!member.Value.Resource.Id.IsValid || string.IsNullOrWhiteSpace(member.Value.Resource.Key))
            {
                error = $"Property '{member.Name}' has an invalid compiled resource reference.";
                return false;
            }
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

        if (member.Value.Kind != XamlValueKind.Binding && member.Value.Kind != XamlValueKind.ResourceReference &&
            !TryLiteralExpression(member.Name, member.Value, out _, out error))
        {
            return false;
        }

        return true;
    }

    private static bool TryValidateBinding(XamlBindingDefinition binding, out string error)
    {
        if (binding.SourceTypeName.Contains("Type", StringComparison.Ordinal) ||
            binding.SourceTypeName.Contains("Assembly", StringComparison.Ordinal) ||
            binding.ReadExpression.Contains("GetProperty", StringComparison.Ordinal) ||
            binding.ReadExpression.Contains("Activator", StringComparison.Ordinal) ||
            binding.ReadExpression.Contains("dynamic", StringComparison.Ordinal) ||
            (binding.WriteExpression is { } write && (write.Contains("GetProperty", StringComparison.Ordinal) || write.Contains("Activator", StringComparison.Ordinal))))
        {
            error = $"Binding '{binding.Path}' contains reflection or dynamic access and cannot be emitted as a typed plan.";
            return false;
        }

        error = string.Empty;
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

    private static void EmitMember(
        StringBuilder writer,
        int nodeIndex,
        XamlMemberPlan member,
        string variablePrefix,
        IReadOnlyList<XamlResourceSlotPlan> resourceSlots)
    {
        if (member.Value.Kind == XamlValueKind.Binding)
        {
            return;
        }

        if (member.Value.Kind == XamlValueKind.ResourceReference)
        {
            writer.Append("        ").Append(variablePrefix).Append(nodeIndex).Append('.');
            writer.Append(member.Value.Resource.IsDynamic ? "SetDynamicResource" : "SetStaticResource");
            writer.Append('(').Append(Quote(member.Name)).Append(", Resources, ")
                .Append(ResourceSlotExpression(member.Value.Resource, resourceSlots)).AppendLine(");");
            return;
        }

        if (!TryLiteralExpression(member.Name, member.Value, out var expression, out var error))
        {
            throw new InvalidOperationException(error);
        }

        writer.Append("        ").Append(variablePrefix).Append(nodeIndex).Append('.');
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

    private static void EmitBinding(StringBuilder writer, int bindingIndex, BindingSite site)
    {
        var binding = site.Member.Value.Binding;
        var definition = site.Definition;
        writer.Append("        _binding").Append(bindingIndex).Append(" = new global::Delta.XAML.UiCompiledBinding<")
            .Append(definition.SourceTypeName)
            .Append(", ")
            .Append(definition.ValueTypeName)
            .Append(">(context, static source => ")
            .Append(definition.ReadExpression)
            .Append(", ");
        if (binding.Mode == UiBindingMode.TwoWay)
        {
            writer.Append("static (source, value) => ").Append(definition.WriteExpression);
        }
        else
        {
            writer.Append("null");
        }

        writer.Append(", global::Delta.XAML.UiBindingMode.").Append(binding.Mode).AppendLine(");");
        writer.Append("        node").Append(site.NodeIndex).Append(".SetBinding(")
            .Append(Quote(site.Member.Name)).Append(", _binding").Append(bindingIndex).AppendLine(");");
    }

    private static void EmitStyles(
        StringBuilder writer,
        IReadOnlyList<XamlStylePlan> styles,
        IReadOnlyList<XamlResourceSlotPlan> resourceSlots)
    {
        for (var styleIndex = 0; styleIndex < styles.Count; styleIndex++)
        {
            var style = styles[styleIndex];
            writer.Append("        var style").Append(styleIndex).Append(" = new global::Delta.XAML.UiStyle(")
                .Append(Quote(style.Key)).Append(", ").Append(Quote(style.TargetType.LocalName)).AppendLine(", Resources);");
            for (var setterIndex = 0; setterIndex < style.Setters.Length; setterIndex++)
            {
                var setter = style.Setters[setterIndex];
                if (setter.Value.Kind == XamlValueKind.ResourceReference)
                {
                    if (!TryTypedPropertyExpression(setter.Name, out var resourceProperty))
                    {
                        throw new InvalidOperationException($"Property '{setter.Name}' has no typed style descriptor.");
                    }

                    writer.Append("        style").Append(styleIndex).Append('.')
                        .Append(setter.Value.Resource.IsDynamic ? "SetResource" : "SetStaticResource")
                        .Append('(').Append(resourceProperty).Append(", ")
                        .Append(ResourceSlotExpression(setter.Value.Resource, resourceSlots)).AppendLine(");");
                    continue;
                }

                if (!TryLiteralExpression(setter.Name, setter.Value, out var expression, out var error))
                {
                    throw new InvalidOperationException(error);
                }

                if (!TryTypedPropertyExpression(setter.Name, out var property))
                {
                    throw new InvalidOperationException($"Property '{setter.Name}' has no typed style descriptor.");
                }

                writer.Append("        style").Append(styleIndex).Append(".Set(").Append(property).Append(", ").Append(expression).AppendLine(");");
            }

            for (var stateIndex = 0; stateIndex < style.VisualStates.Length; stateIndex++)
            {
                var state = style.VisualStates[stateIndex];
                for (var setterIndex = 0; setterIndex < state.Setters.Length; setterIndex++)
                {
                    var setter = state.Setters[setterIndex];
                    if (setter.Value.Kind == XamlValueKind.ResourceReference)
                    {
                        if (!TryTypedPropertyExpression(setter.Name, out var stateResourceProperty))
                        {
                            throw new InvalidOperationException($"Property '{setter.Name}' has no typed style descriptor.");
                        }

                        writer.Append("        style").Append(styleIndex).Append(".SetState").Append(setter.Value.Resource.IsDynamic ? "Resource" : "StaticResource");
                        writer.Append("(global::Delta.XAML.UiStyleState.").Append(state.State).Append(", ")
                            .Append(stateResourceProperty).Append(", ")
                            .Append(ResourceSlotExpression(setter.Value.Resource, resourceSlots)).AppendLine(");");
                        continue;
                    }

                    if (!TryLiteralExpression(setter.Name, setter.Value, out var expression, out var error))
                    {
                        throw new InvalidOperationException(error);
                    }

                    if (!TryTypedPropertyExpression(setter.Name, out var stateProperty))
                    {
                        throw new InvalidOperationException($"Property '{setter.Name}' has no typed style descriptor.");
                    }

                    writer.Append("        style").Append(styleIndex).Append(".SetState(global::Delta.XAML.UiStyleState.")
                        .Append(state.State).Append(", ").Append(stateProperty).Append(", ").Append(expression).AppendLine(");");
                }
            }

            writer.Append("        Theme.Add(style").Append(styleIndex).AppendLine(");");
        }
    }

    private static void EmitTemplates(
        StringBuilder writer,
        IReadOnlyList<XamlTemplatePlan> templates,
        XamlSemanticRegistry registry,
        IReadOnlyList<XamlResourceSlotPlan> resourceSlots)
    {
        for (var templateIndex = 0; templateIndex < templates.Count; templateIndex++)
        {
            var template = templates[templateIndex];
            var nodes = new List<XamlObjectPlan>();
            Flatten(template.Root, nodes);
            writer.Append("        Theme.RegisterTemplate(").Append(Quote(template.Key)).AppendLine(", new global::Delta.XAML.UiTemplate(owner =>");
            writer.AppendLine("        {");
            for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                if (!registry.TryResolveType(nodes[nodeIndex].Type, out var type))
                {
                    throw new InvalidOperationException($"No generated factory is registered for '{nodes[nodeIndex].Name.LocalName}'.");
                }

                writer.Append("            var template").Append(nodeIndex).Append(" = ").Append(type.FactoryExpression).AppendLine(";");
                for (var memberIndex = 0; memberIndex < nodes[nodeIndex].Members.Length; memberIndex++)
                {
                    EmitMember(writer, nodeIndex, nodes[nodeIndex].Members[memberIndex], "template", resourceSlots);
                }
            }

            for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                for (var childIndex = 0; childIndex < nodes[nodeIndex].Children.Length; childIndex++)
                {
                    var childPosition = FindNode(nodes, nodes[nodeIndex].Children[childIndex]);
                    if (childPosition < 0)
                    {
                        throw new InvalidOperationException("A template child is not present in the flattened semantic tree.");
                    }

                    if (!registry.TryResolveType(nodes[nodeIndex].Type, out var type))
                    {
                        throw new InvalidOperationException($"No generated factory is registered for '{nodes[nodeIndex].Name.LocalName}'.");
                    }

                    if (!TryEmitAttachment(writer, nodeIndex, childPosition, type.Name.LocalName, nodes[nodeIndex].Children[childIndex].Range, out var error, "template"))
                    {
                        throw new InvalidOperationException(error);
                    }
                }
            }

            writer.AppendLine("            return template0;");
            writer.AppendLine("        }));");
        }
    }

    private static bool TryEmitAttachment(
        StringBuilder writer,
        int parentIndex,
        int childIndex,
        string parentType,
        SourceRange range,
        out string error,
        string variablePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variablePrefix);
        switch (parentType)
        {
            case "Panel":
            case "StackPanel":
            case "Grid":
            case "ItemsControl":
                writer.Append("        ").Append(variablePrefix).Append(parentIndex).Append(".Add(").Append(variablePrefix).Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case "Border":
                writer.Append("        ").Append(variablePrefix).Append(parentIndex).Append(".SetChild(").Append(variablePrefix).Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case "ContentControl":
            case "Button":
            case "ToggleButton":
            case "ScrollViewer":
                writer.Append("        ").Append(variablePrefix).Append(parentIndex).Append(".SetContent(").Append(variablePrefix).Append(childIndex).AppendLine(");");
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

    private static bool TryTypedPropertyExpression(string propertyName, out string expression)
    {
        expression = propertyName switch
        {
            "Width" => "global::Delta.XAML.UiElementProperties.Width",
            "Height" => "global::Delta.XAML.UiElementProperties.Height",
            "Background" => "global::Delta.XAML.UiElementProperties.Background",
            "Padding" => "global::Delta.XAML.UiElementProperties.Padding",
            "Fill" => "global::Delta.XAML.UiElementProperties.Fill",
            "IsEnabled" => "global::Delta.XAML.UiElementProperties.IsEnabled",
            "IsSelected" => "global::Delta.XAML.UiElementProperties.IsSelected",
            "StyleKey" => "global::Delta.XAML.UiElementProperties.StyleKey",
            "TemplateKey" => "global::Delta.XAML.UiElementProperties.TemplateKey",
            "Text" => "global::Delta.XAML.UiTextBlockProperties.Text",
            "FontKey" => "global::Delta.XAML.UiTextBlockProperties.FontKey",
            "FontSize" => "global::Delta.XAML.UiTextBlockProperties.FontSize",
            "Foreground" => "global::Delta.XAML.UiTextBlockProperties.Foreground",
            "Value" => "global::Delta.XAML.UiNumericEditorProperties.Value",
            "Minimum" => "global::Delta.XAML.UiNumericEditorProperties.Minimum",
            "Maximum" => "global::Delta.XAML.UiNumericEditorProperties.Maximum",
            "Orientation" => "global::Delta.XAML.UiStackPanelProperties.Orientation",
            "Columns" => "global::Delta.XAML.UiGridProperties.Columns",
            "Rows" => "global::Delta.XAML.UiGridProperties.Rows",
            _ => string.Empty,
        };
        return expression.Length != 0;
    }

    private static string ResourceIdExpression(UiResourceId resource)
    {
        if (!resource.IsValid)
        {
            throw new InvalidOperationException("A generated resource reference requires a stable identity.");
        }

        return "new global::Delta.XAML.Contract.UiResourceId(new global::System.Guid(" + Quote(resource.Value.ToString("D")) + "))";
    }

    private static string ResourceSlotExpression(
        XamlResourceReferencePlan resource,
        IReadOnlyList<XamlResourceSlotPlan> resourceSlots)
    {
        if ((uint)resource.Slot >= (uint)resourceSlots.Count || resourceSlots[resource.Slot].Id != resource.Id)
        {
            throw new InvalidOperationException($"Resource '{resource.Key}' has no matching artifact-local slot.");
        }

        return "_resourceIds[" + resource.Slot.ToString(CultureInfo.InvariantCulture) + "]";
    }

    private static string ResourceSlotExpression(
        UiResourceId resource,
        IReadOnlyList<XamlResourceSlotPlan> resourceSlots)
    {
        for (var i = 0; i < resourceSlots.Count; i++)
        {
            if (resourceSlots[i].Id == resource)
            {
                return "_resourceIds[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            }
        }

        throw new InvalidOperationException("A generated resource declaration has no artifact-local slot.");
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

    private readonly record struct BindingSite(
        int NodeIndex,
        XamlMemberPlan Member,
        XamlBindingDefinition Definition);
}
