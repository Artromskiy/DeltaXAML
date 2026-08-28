using System.Globalization;
using System.Diagnostics.CodeAnalysis;
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
                if (!CanEmitMember(member, registry, type, out var memberError))
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
            if (!style.TargetTypeId.IsValid || !registry.TryResolveType(style.TargetTypeId, out _))
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

                if (!CanEmitMember(setter, registry, null, out var setterError))
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

                    if (!CanEmitMember(setter, registry, null, out var setterError))
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
            string? templateBindingSourceType = null;
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
                        if (!registry.TryResolveBinding(member.Value.Binding.Path, out var bindingDefinition))
                        {
                            diagnostic = new("DXAMLGEN002", $"Binding path '{member.Value.Binding.Path}' has no typed compile-time definition.", member.Range);
                            return false;
                        }

                        if (templateBindingSourceType is null)
                        {
                            templateBindingSourceType = bindingDefinition.SourceTypeName;
                        }
                        else if (!string.Equals(templateBindingSourceType, bindingDefinition.SourceTypeName, StringComparison.Ordinal))
                        {
                            diagnostic = new("DXAMLGEN002", "One generated template must use one typed binding source context.", member.Range);
                            return false;
                        }
                    }

                    if (!CanEmitMember(member, registry, templateType, out var memberError))
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
            writer.AppendLine("    private readonly global::Delta.XAML.UiElement _bindingTarget" + i + ";");
            writer.Append("    private readonly global::Delta.XAML.UiCompiledBinding<")
                .Append(binding.Definition.SourceTypeName)
                .Append(", ")
                .Append(binding.Definition.ValueTypeName)
                .Append("> _binding")
                .Append(i)
                .AppendLine(";");
        }

        if (bindingSites.Count != 0)
        {
            writer.AppendLine("    private readonly global::System.ComponentModel.INotifyPropertyChanged? _bindingSource;");
        }

        EmitTemplateFactories(writer, plan.Templates, registry, plan.ResourceSlots);

        writer.AppendLine("    public global::Delta.XAML.UiResourceCatalog Resources { get; }");
        writer.AppendLine("    public global::Delta.XAML.UiTheme Theme { get; }");
        writer.Append("    public global::Delta.XAML.UiDocument").Append(hasVisualRoot ? string.Empty : "?").AppendLine(" Document { get; }");
        writer.AppendLine();
        writer.Append("    public ").Append(className).Append('(');
        if (bindingSourceType is not null)
        {
            writer.Append(bindingSourceType).Append(" context, ");
        }

        writer.AppendLine("global::Delta.Text.Contract.ITextService textService,");
        writer.AppendLine("        global::Delta.XAML.IUiFontResolver? fontResolver = null)");
        writer.AppendLine("    {");
        writer.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(textService);");
        if (bindingSourceType is not null)
        {
            writer.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(context);");
        }

        writer.AppendLine("        Resources = new global::Delta.XAML.UiResourceCatalog();");
        writer.AppendLine("        Theme = new global::Delta.XAML.UiTheme(Resources);");
        for (var scalarIndex = 0; scalarIndex < plan.ScalarResources.Length; scalarIndex++)
        {
            var scalar = plan.ScalarResources[scalarIndex];
            if (!TryLiteralExpression("Resource", scalar.Value, out var scalarExpression, out var scalarError))
            {
                throw new InvalidOperationException(scalarError);
            }

            writer.Append("        Resources.Set(")
                .Append(ResourceSlotExpression(scalar.Id, plan.ResourceSlots))
                .Append(", ").Append(scalarExpression).AppendLine(");");
        }

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
                EmitMember(writer, i, resourceNodes[i].Members[memberIndex], resourceType, "resource", plan.ResourceSlots);
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

                if (!TryEmitAttachment(writer, i, childPosition, resourceParentType, out var attachmentError, "resource"))
                {
                    diagnostic = new("DXAMLGEN003", attachmentError, resourceNodes[i].Children[childIndex].Range);
                    return false;
                }
            }
        }

        EmitStyles(writer, plan.Styles, plan.ResourceSlots);
        EmitTemplateRegistrations(writer, plan.Templates);

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
                EmitMember(writer, i, nodes[i].Members[memberIndex], type, "node", plan.ResourceSlots);
            }
        }

        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            var members = nodes[nodeIndex].Members;
            for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
            {
                var member = members[memberIndex];
                if (member.Name != "TemplateKey" || member.Value.Kind != XamlValueKind.String ||
                    !TryFindTemplate(plan.Templates, member.Value.Literal.CanonicalText, out var template))
                {
                    continue;
                }

                writer.Append("        node").Append(nodeIndex).Append(".SetCompiledTemplate(")
                    .Append(TemplateIdExpression(template.Id)).AppendLine(");");
            }
        }

        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            var members = nodes[nodeIndex].Members;
            for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
            {
                var member = members[memberIndex];
                if (member.Name != "StyleKey" || member.Value.Kind != XamlValueKind.String ||
                    !TryFindStyle(plan.Styles, member.Value.Literal.CanonicalText, out var style))
                {
                    continue;
                }

                writer.Append("        node").Append(nodeIndex).Append(".SetCompiledStyle(")
                    .Append(StyleIdExpression(style.Id)).AppendLine(");");
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

                if (!TryEmitAttachment(writer, i, childPosition, parentType, out var attachmentError, "node"))
                {
                    diagnostic = new("DXAMLGEN003", attachmentError, child.Range);
                    return false;
                }
            }
        }

        for (var i = 0; i < bindingSites.Count; i++)
        {
            writer.Append("        _bindingTarget").Append(i).Append(" = node").Append(bindingSites[i].NodeIndex).AppendLine(";");
        }

        for (var i = 0; i < bindingSites.Count; i++)
        {
            EmitBinding(writer, i, bindingSites[i]);
        }

        if (bindingSites.Count != 0)
        {
            writer.AppendLine("        if (context is global::System.ComponentModel.INotifyPropertyChanged observable)");
            writer.AppendLine("        {");
            writer.AppendLine("            _bindingSource = observable;");
            writer.AppendLine("            _bindingSource.PropertyChanged += OnContextPropertyChanged;");
            writer.AppendLine("        }");
        }

        if (hasVisualRoot)
        {
            writer.AppendLine("        Theme.Apply(node0);");
            writer.Append("        Document = new global::Delta.XAML.UiDocument(node0, textService, fontResolver, Theme);").AppendLine();
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
        writer.AppendLine("    public bool TryGetResourceId(string key, out global::Delta.XAML.Contract.UiResourceId resource)");
        writer.AppendLine("    {");
        writer.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(key);");
        for (var resourceIndex = 0; resourceIndex < plan.ResourceSlots.Length; resourceIndex++)
        {
            writer.Append("        if (key == ").Append(Quote(plan.ResourceSlots[resourceIndex].Key)).AppendLine(")");
            writer.AppendLine("        {");
            writer.Append("            resource = _resourceIds[").Append(resourceIndex).AppendLine("]; ");
            writer.AppendLine("            return true;");
            writer.AppendLine("        }");
        }

        writer.AppendLine("        resource = default;");
        writer.AppendLine("        return false;");
        writer.AppendLine("    }");
        writer.AppendLine();
        writer.AppendLine("    public void RefreshBindings()");
        writer.AppendLine("    {");
        for (var i = 0; i < bindingSites.Count; i++)
        {
            EmitBindingQueue(writer, i, bindingSites[i], "        ");
        }

        writer.AppendLine("    }");
        writer.AppendLine();
        if (bindingSites.Count != 0)
        {
            EmitBindingNotificationHandler(writer, bindingSites);
            writer.AppendLine();
        }

        writer.AppendLine("    public void Dispose()");
        writer.AppendLine("    {");
        if (bindingSites.Count != 0)
        {
            writer.AppendLine("        if (_bindingSource is not null)");
            writer.AppendLine("        {");
            writer.AppendLine("            _bindingSource.PropertyChanged -= OnContextPropertyChanged;");
            writer.AppendLine("        }");
        }

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

    private static bool CanEmitMember(
        XamlMemberPlan member,
        XamlSemanticRegistry registry,
        XamlTypeDefinition? type,
        out string error)
    {
        error = string.Empty;
        if (member.Value.Kind == XamlValueKind.Binding)
        {
            if (!registry.TryResolveBinding(member.Value.Binding.Path, out var binding))
            {
                error = $"Binding path '{member.Value.Binding.Path}' has no typed compile-time definition.";
                return false;
            }

            if (member.Value.Binding.ConverterKey is { } converterKey &&
                !string.Equals(binding.ConverterKey, converterKey, StringComparison.Ordinal))
            {
                error = $"Binding '{member.Value.Binding.Path}' requires registered typed converter '{converterKey}'.";
                return false;
            }

            if (member.Value.Binding.StringFormat is not null)
            {
                error = $"Binding '{member.Value.Binding.Path}' uses StringFormat; only direct typed accessors are available in this slice.";
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

        if (!TryTypedPropertyExpression(member.Name, out _) &&
            (type is null || !type.TryGetProperty(member.Name, out var customProperty) ||
             (customProperty.SetterExpression is null && customProperty.MemberName is null)))
        {
            error = $"Property '{member.Name}' has no typed generated setter in the current compile slice.";
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

        if (member.Value.Kind is XamlValueKind.Binding or XamlValueKind.ResourceReference &&
            !TryTypedPropertyExpression(member.Name, out _))
        {
            error = $"Property '{member.Name}' requires a generated typed library descriptor for bindings and resources.";
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
        XamlTypeDefinition type,
        string variablePrefix,
        IReadOnlyList<XamlResourceSlotPlan> resourceSlots)
    {
        if (member.Value.Kind == XamlValueKind.Binding)
        {
            return;
        }

        if (member.Value.Kind == XamlValueKind.ResourceReference)
        {
            if (!TryTypedPropertyExpression(member.Name, out var resourceProperty))
            {
                throw new InvalidOperationException($"Property '{member.Name}' has no typed resource descriptor.");
            }

            writer.Append("        ").Append(variablePrefix).Append(nodeIndex).Append('.');
            writer.Append(member.Value.Resource.IsDynamic ? "SetDynamicResource" : "SetStaticResource");
            writer.Append('(').Append(resourceProperty).Append(", Resources, ")
                .Append(ResourceSlotExpression(member.Value.Resource, resourceSlots)).AppendLine(");");
            return;
        }

        if (!TryLiteralExpression(member.Name, member.Value, out var expression, out var error))
        {
            throw new InvalidOperationException(error);
        }

        if (!TryTypedPropertyExpression(member.Name, out _))
        {
            if (!type.TryGetProperty(member.Name, out var customProperty))
            {
                throw new InvalidOperationException($"Property '{member.Name}' has no typed generated setter.");
            }

            if (customProperty.SetterExpression is { } setter)
            {
                writer.Append("        ").Append(setter).Append('(')
                    .Append(variablePrefix).Append(nodeIndex).Append(", ").Append(expression).AppendLine(");");
            }
            else if (customProperty.MemberName is { } memberName)
            {
                writer.Append("        ").Append(variablePrefix).Append(nodeIndex).Append('.')
                    .Append(memberName).Append(" = ").Append(expression).AppendLine(";");
            }
            else
            {
                throw new InvalidOperationException($"Property '{member.Name}' has no typed generated setter.");
            }

            return;
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
        if (!TryTypedPropertyExpression(site.Member.Name, out var property))
        {
            throw new InvalidOperationException($"Property '{site.Member.Name}' has no typed binding target.");
        }

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

        writer.Append(", global::Delta.XAML.UiBindingMode.").Append(binding.Mode).AppendLine(", false);");
        writer.Append("        node").Append(site.NodeIndex).Append(".SetCompiledBinding(")
            .Append(property).Append(", _binding").Append(bindingIndex).AppendLine(", true);");
    }

    private static void EmitBindingNotificationHandler(StringBuilder writer, IReadOnlyList<BindingSite> bindingSites)
    {
        writer.AppendLine("    private void OnContextPropertyChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs args)");
        writer.AppendLine("    {");
        writer.AppendLine("        switch (args.PropertyName)");
        writer.AppendLine("        {");
        for (var i = 0; i < bindingSites.Count; i++)
        {
            var sourceName = BindingNotificationName(bindingSites[i].Definition.Path);
            var alreadyEmitted = false;
            for (var previous = 0; previous < i; previous++)
            {
                if (string.Equals(sourceName, BindingNotificationName(bindingSites[previous].Definition.Path), StringComparison.Ordinal))
                {
                    alreadyEmitted = true;
                    break;
                }
            }

            if (alreadyEmitted)
            {
                continue;
            }

            writer.Append("            case ").Append(Quote(sourceName)).AppendLine(":");
            for (var siteIndex = i; siteIndex < bindingSites.Count; siteIndex++)
            {
                if (string.Equals(sourceName, BindingNotificationName(bindingSites[siteIndex].Definition.Path), StringComparison.Ordinal))
                {
                    EmitBindingQueue(writer, siteIndex, bindingSites[siteIndex], "                ");
                }
            }

            writer.AppendLine("                break;");
        }

        writer.AppendLine("            case null:");
        writer.AppendLine("            case \"\":");
        writer.AppendLine("                RefreshBindings();");
        writer.AppendLine("                break;");
        writer.AppendLine("            default:");
        writer.AppendLine("                break;");
        writer.AppendLine("        }");
        writer.AppendLine("    }");
    }

    private static void EmitBindingQueue(StringBuilder writer, int bindingIndex, BindingSite site, string indentation)
    {
        if (!TryTypedPropertyExpression(site.Member.Name, out var property))
        {
            throw new InvalidOperationException($"Property '{site.Member.Name}' has no typed binding target.");
        }

        writer.Append(indentation).Append("_bindingTarget").Append(bindingIndex).Append(".QueueCompiledBindingRefresh(")
            .Append(property).Append(", _binding").Append(bindingIndex).AppendLine(");");
    }

    private static string BindingNotificationName(string path)
    {
        var separator = path.IndexOf('.', StringComparison.Ordinal);
        return separator < 0 ? path : path[..separator];
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
                .Append(Quote(style.Key)).Append(", ").Append(TypeIdExpression(style.TargetTypeId)).AppendLine(", Resources);");
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

            writer.Append("        Theme.RegisterStyle(")
                .Append(StyleIdExpression(style.Id)).Append(", style")
                .Append(styleIndex).AppendLine(");");
        }
    }

    private static void EmitTemplateFactories(
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
            writer.Append("    private sealed class TemplateFactory").Append(templateIndex)
                .AppendLine(" : global::Delta.XAML.IUiTemplateFactory");
            writer.AppendLine("    {");
            writer.AppendLine("        public global::Delta.XAML.UiElement Create(global::Delta.XAML.UiElement owner, global::Delta.XAML.UiResourceCatalog Resources)");
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
                    EmitMember(writer, nodeIndex, nodes[nodeIndex].Members[memberIndex], type, "template", resourceSlots);
                }
            }

            var templateBindingSites = new List<TemplateBindingSite>();
            string? templateBindingSourceType = null;
            for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                for (var memberIndex = 0; memberIndex < nodes[nodeIndex].Members.Length; memberIndex++)
                {
                    var member = nodes[nodeIndex].Members[memberIndex];
                    if (member.Value.Kind != XamlValueKind.Binding)
                    {
                        continue;
                    }

                    if (!registry.TryResolveBinding(member.Value.Binding.Path, out var definition))
                    {
                        throw new InvalidOperationException($"Binding path '{member.Value.Binding.Path}' has no typed compile-time definition.");
                    }

                    templateBindingSourceType ??= definition.SourceTypeName;
                    templateBindingSites.Add(new(nodeIndex, member, definition));
                }
            }

            if (templateBindingSites.Count != 0)
            {
                writer.Append("            if (owner.BindingContext is not ").Append(templateBindingSourceType)
                    .AppendLine(" templateContext)");
                writer.AppendLine("            {");
                writer.Append("                throw new global::System.InvalidOperationException(\"Template '")
                    .Append(Quote(template.Key)).AppendLine("' requires its declared BindingContext type.\");");
                writer.AppendLine("            }");
                for (var bindingIndex = 0; bindingIndex < templateBindingSites.Count; bindingIndex++)
                {
                    EmitTemplateBinding(writer, bindingIndex, templateBindingSites[bindingIndex]);
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

                    if (!TryEmitAttachment(writer, nodeIndex, childPosition, type, out var error, "template"))
                    {
                        throw new InvalidOperationException(error);
                    }
                }
            }

            writer.AppendLine("            return template0;");
            writer.AppendLine("        }");
            writer.AppendLine("    }");
            writer.AppendLine();
        }
    }

    private static void EmitTemplateRegistrations(StringBuilder writer, IReadOnlyList<XamlTemplatePlan> templates)
    {
        for (var templateIndex = 0; templateIndex < templates.Count; templateIndex++)
        {
            writer.Append("        Theme.RegisterTemplate(").Append(TemplateIdExpression(templates[templateIndex].Id))
                .Append(", new global::Delta.XAML.UiTemplate(new TemplateFactory").Append(templateIndex).AppendLine("()));");
        }
    }

    private static void EmitTemplateBinding(StringBuilder writer, int bindingIndex, TemplateBindingSite site)
    {
        if (!TryTypedPropertyExpression(site.Member.Name, out var property))
        {
            throw new InvalidOperationException($"Property '{site.Member.Name}' has no typed binding target.");
        }

        writer.Append("            var templateBinding").Append(bindingIndex).Append(" = new global::Delta.XAML.UiCompiledBinding<")
            .Append(site.Definition.SourceTypeName)
            .Append(", ")
            .Append(site.Definition.ValueTypeName)
            .Append(">(templateContext, static source => ")
            .Append(site.Definition.ReadExpression)
            .Append(", ");
        if (site.Member.Value.Binding.Mode == UiBindingMode.TwoWay)
        {
            writer.Append("static (source, value) => ").Append(site.Definition.WriteExpression);
        }
        else
        {
            writer.Append("null");
        }

        writer.Append(", global::Delta.XAML.UiBindingMode.").Append(site.Member.Value.Binding.Mode).AppendLine(");");
        writer.Append("            template").Append(site.NodeIndex).Append(".SetCompiledBinding(")
            .Append(property).Append(", templateBinding").Append(bindingIndex).AppendLine(");");
    }

    private static bool TryFindTemplate(
        IReadOnlyList<XamlTemplatePlan> templates,
        string key,
        [NotNullWhen(true)] out XamlTemplatePlan? template)
    {
        for (var i = 0; i < templates.Count; i++)
        {
            if (string.Equals(templates[i].Key, key, StringComparison.Ordinal))
            {
                template = templates[i];
                return true;
            }
        }

        template = null;
        return false;
    }

    private static bool TryEmitAttachment(
        StringBuilder writer,
        int parentIndex,
        int childIndex,
        XamlTypeDefinition parentType,
        out string error,
        string variablePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variablePrefix);
        switch (parentType.ContentKind)
        {
            case XamlContentKind.Children when parentType.ChildAttachmentMember is { } childMember:
                writer.Append("        ").Append(variablePrefix).Append(parentIndex).Append('.')
                    .Append(childMember).Append('(').Append(variablePrefix).Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case XamlContentKind.Children when parentType.ChildAttachmentExpression is { } childAttachment:
                writer.Append("        ").Append(childAttachment).Append('(')
                    .Append(variablePrefix).Append(parentIndex).Append(", ").Append(variablePrefix).Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case XamlContentKind.Children when IsBuiltInChildrenOwner(parentType.Name.LocalName):
                writer.Append("        ").Append(variablePrefix).Append(parentIndex).Append(".Add(").Append(variablePrefix).Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case XamlContentKind.Children:
                error = $"Type '{parentType.Name.LocalName}' declares children content but has no generated child attachment thunk.";
                return false;
            case XamlContentKind.SingleContent when parentType.ContentAttachmentMember is { } contentMember:
                writer.Append("        ").Append(variablePrefix).Append(parentIndex).Append('.')
                    .Append(contentMember).Append('(').Append(variablePrefix).Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case XamlContentKind.SingleContent when parentType.ContentAttachmentExpression is { } contentAttachment:
                writer.Append("        ").Append(contentAttachment).Append('(')
                    .Append(variablePrefix).Append(parentIndex).Append(", ").Append(variablePrefix).Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case XamlContentKind.SingleContent when string.Equals(parentType.Name.LocalName, "Border", StringComparison.Ordinal):
                writer.Append("        ").Append(variablePrefix).Append(parentIndex).Append(".SetChild(").Append(variablePrefix).Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case XamlContentKind.SingleContent when IsBuiltInContentOwner(parentType.Name.LocalName):
                writer.Append("        ").Append(variablePrefix).Append(parentIndex).Append(".SetContent(").Append(variablePrefix).Append(childIndex).AppendLine(");");
                error = string.Empty;
                return true;
            case XamlContentKind.SingleContent:
                error = $"Type '{parentType.Name.LocalName}' declares single content but has no generated content attachment thunk.";
                return false;
            default:
                error = $"Type '{parentType.Name.LocalName}' does not accept generated child/content attachment.";
                return false;
        }
    }

    private static bool IsBuiltInChildrenOwner(string name) => name is "Panel" or "StackPanel" or "Grid" or "ItemsControl";

    private static bool IsBuiltInContentOwner(string name) => name is "ContentControl" or "Button" or "ToggleButton" or "ScrollViewer";

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

    private static string TemplateIdExpression(UiTemplateId template)
    {
        if (!template.IsValid)
        {
            throw new InvalidOperationException("A generated template identity is required.");
        }

        return "new global::Delta.XAML.UiTemplateId(new global::System.Guid(" + Quote(template.Value.ToString("D")) + "))";
    }

    private static string StyleIdExpression(UiStyleId style)
    {
        if (!style.IsValid)
        {
            throw new InvalidOperationException("A generated style identity is required.");
        }

        return "new global::Delta.XAML.UiStyleId(new global::System.Guid(" + Quote(style.Value.ToString("D")) + "))";
    }

    private static bool TryFindStyle(
        IReadOnlyList<XamlStylePlan> styles,
        string key,
        [NotNullWhen(true)] out XamlStylePlan? style)
    {
        for (var i = 0; i < styles.Count; i++)
        {
            if (string.Equals(styles[i].Key, key, StringComparison.Ordinal))
            {
                style = styles[i];
                return true;
            }
        }

        style = null;
        return false;
    }

    private static string TypeIdExpression(UiTypeId type)
    {
        if (!type.IsValid)
        {
            throw new InvalidOperationException("A generated type identity is required.");
        }

        return "new global::Delta.XAML.UiTypeId(new global::System.Guid(" + Quote(type.Value.ToString("D")) + "))";
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

    private readonly record struct TemplateBindingSite(
        int NodeIndex,
        XamlMemberPlan Member,
        XamlBindingDefinition Definition);
}
