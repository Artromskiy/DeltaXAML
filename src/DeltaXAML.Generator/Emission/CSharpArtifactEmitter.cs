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
        var relationBindingSites = new List<RelationBindingSite>();
        var multiBindingSites = new List<MultiBindingSite>();
        var collectionSites = new List<CollectionSite>();
        string? bindingSourceType = plan.BindingSourceTypeName;
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
                if (IsCollectionMetadata(member.Name))
                {
                    continue;
                }

                if (!CanEmitMember(member, registry, type, out var memberError))
                {
                    diagnostic = new("DXAMLGEN002", memberError, member.Range);
                    return false;
                }

                if (i < nodes.Count && member.Value.Kind == XamlValueKind.Binding)
                {
                    if (member.Value.Binding.SourceKind != XamlBindingSourceKind.Context)
                    {
                        relationBindingSites.Add(new(i, member));
                        continue;
                    }

                    if (!registry.TryResolveBindingVariant(member.Value.Binding.Path, member.Value.Binding.ConverterKey, out var bindingDefinition))
                    {
                        diagnostic = new("DXAMLGEN002", $"Binding path '{member.Value.Binding.Path}' has no typed compile-time definition.", member.Range);
                        return false;
                    }

                    if (bindingSourceType is null)
                    {
                        bindingSourceType = bindingDefinition.SourceTypeName;
                    }
                    else if (!SameTypeName(bindingSourceType, bindingDefinition.SourceTypeName))
                    {
                        diagnostic = new("DXAMLGEN002", "One generated artifact must use one typed binding source context.", member.Range);
                        return false;
                    }

                    bindingSourceType = bindingDefinition.SourceTypeName;

                    bindingSites.Add(new(i, member, bindingDefinition));
                }
                else if (i < nodes.Count && member.Value.Kind == XamlValueKind.MultiBinding)
                {
                    multiBindingSites.Add(new(i, member));
                }
            }

            string? collectionError = null;
            if (i < nodes.Count && TryCreateCollectionSite(i, validationNodes[i], plan.Templates, registry, out var collectionSite, out collectionError))
            {
                if (bindingSourceType is not null && !SameTypeName(bindingSourceType, collectionSite.Definition.SourceTypeName))
                {
                    diagnostic = new("DXAMLGEN005", "One generated artifact must use one typed collection context.", validationNodes[i].Range);
                    return false;
                }

                collectionSites.Add(collectionSite);
                bindingSourceType = collectionSite.Definition.SourceTypeName;
            }
            else if (collectionError is not null)
            {
                diagnostic = new("DXAMLGEN005", collectionError, validationNodes[i].Range);
                return false;
            }
        }

        var effectSites = new List<EffectSite>();
        for (var effectIndex = 0; effectIndex < plan.EffectResources.Length; effectIndex++)
        {
            if (!TryCreateEffectSite(-1, plan.EffectResources[effectIndex], registry, ref bindingSourceType,
                    out var effectSite, out var effectError))
            {
                diagnostic = new("DXAMLGEN010", effectError, plan.EffectResources[effectIndex].Range);
                return false;
            }

            if (effectSite.HasBindings && HasStaticEffectReference(validationNodes, effectSite.Plan.Resource))
            {
                diagnostic = new(
                    "DXAMLGEN010",
                    $"Bound EffectSet resource '{effectSite.Plan.Key}' must be consumed through DynamicResource so changing outsets reaches the retained element.",
                    effectSite.Plan.Range);
                return false;
            }

            effectSites.Add(effectSite);
        }

        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            if (nodes[nodeIndex].InlineEffect is not { } effect)
            {
                continue;
            }

            if (!TryCreateEffectSite(nodeIndex, effect, registry, ref bindingSourceType,
                    out var effectSite, out var effectError))
            {
                diagnostic = new("DXAMLGEN010", effectError, effect.Range);
                return false;
            }

            effectSites.Add(effectSite);
        }

        for (var templateIndex = 0; templateIndex < plan.Templates.Length; templateIndex++)
        {
            var templateNodes = new List<XamlObjectPlan>();
            Flatten(plan.Templates[templateIndex].Root, templateNodes);
            for (var nodeIndex = 0; nodeIndex < templateNodes.Count; nodeIndex++)
            {
                if (templateNodes[nodeIndex].InlineEffect is { } effect && HasBindings(effect))
                {
                    diagnostic = new(
                        "DXAMLGEN010",
                        "Bound inline effects in templates require a generated template effect plan; use a named DynamicResource effect in this compile slice.",
                        effect.Range);
                    return false;
                }
            }
        }

        for (var nodeIndex = 0; nodeIndex < resourceNodes.Count; nodeIndex++)
        {
            if (resourceNodes[nodeIndex].InlineEffect is { } effect && HasBindings(effect))
            {
                diagnostic = new(
                    "DXAMLGEN010",
                    "Bound inline effects in object resources are not shared implicitly; use a named DynamicResource EffectSet.",
                    effect.Range);
                return false;
            }
        }

        var resolvedRelationBindings = new List<ResolvedRelationBindingSite>(relationBindingSites.Count);
        for (var i = 0; i < relationBindingSites.Count; i++)
        {
            if (!TryResolveRelationBinding(relationBindingSites[i], nodes, registry, out var resolved, out var relationError))
            {
                diagnostic = new("DXAMLGEN006", relationError, relationBindingSites[i].Member.Range);
                return false;
            }

            resolvedRelationBindings.Add(resolved);
        }
        var resolvedMultiBindings = new List<ResolvedMultiBindingSite>(multiBindingSites.Count);
        for (var i = 0; i < multiBindingSites.Count; i++)
        {
            if (!TryResolveMultiBinding(multiBindingSites[i], nodes, registry, bindingSourceType, out var resolved, out var multiError))
            {
                diagnostic = new("DXAMLGEN007", multiError, multiBindingSites[i].Member.Range);
                return false;
            }

            resolvedMultiBindings.Add(resolved);
        }
        var resolvedTriggers = new List<ResolvedTriggerSite>(plan.Triggers.Length);
        for (var i = 0; i < plan.Triggers.Length; i++)
        {
            if (!TryResolveTrigger(plan.Triggers[i], nodes, registry, bindingSourceType, out var resolved, out var triggerError))
            {
                diagnostic = new("DXAMLGEN008", triggerError, plan.Triggers[i].Range);
                return false;
            }

            resolvedTriggers.Add(resolved);
        }
        var resolvedBehaviors = new List<ResolvedBehaviorSite>(plan.Behaviors.Length);
        for (var i = 0; i < plan.Behaviors.Length; i++)
        {
            var behavior = plan.Behaviors[i];
            var targetIndex = FindNamedNode(nodes, behavior.TargetName);
            if (targetIndex < 0 || !registry.TryResolveBehavior(behavior.Key, out var definition))
            {
                diagnostic = new("DXAMLGEN009", $"Behavior '{behavior.Key}' or target '{behavior.TargetName}' has no generated definition.", behavior.Range);
                return false;
            }

            resolvedBehaviors.Add(new(ScopeElementExpression(nodes, targetIndex), definition));
        }

        for (var styleIndex = 0; styleIndex < plan.Styles.Length; styleIndex++)
        {
            var style = plan.Styles[styleIndex];
            if (!style.TargetTypeId.IsValid || !registry.TryResolveType(style.TargetTypeId, out _))
            {
                diagnostic = new("DXAMLGEN004", $"Style '{style.Key}' targets an unknown type '{style.TargetType.LocalName}'.", style.Range);
                return false;
            }

            if (style.BasedOn is { } basedOn)
            {
                var baseIndex = FindStyleIndex(plan.Styles, basedOn, null);
                if (baseIndex < 0)
                {
                    baseIndex = FindStyleIndex(plan.Styles, basedOn, style.Variant);
                }

                if (baseIndex < 0)
                {
                    diagnostic = new("DXAMLGEN011", $"Style '{style.Key}' is BasedOn '{basedOn}', but that style is not declared.", style.Range);
                    return false;
                }

                if (baseIndex == styleIndex)
                {
                    diagnostic = new("DXAMLGEN011", $"Style '{style.Key}' cannot be BasedOn itself.", style.Range);
                    return false;
                }

                if (plan.Styles[baseIndex].TargetTypeId != style.TargetTypeId)
                {
                    diagnostic = new("DXAMLGEN011", $"Style '{style.Key}' and its BasedOn style '{basedOn}' must target the same type.", style.Range);
                    return false;
                }

                var visited = new HashSet<int> { styleIndex };
                for (var current = baseIndex; plan.Styles[current].BasedOn is { } next;)
                {
                    var nextIndex = FindStyleIndex(plan.Styles, next, null);
                    if (nextIndex < 0)
                    {
                        nextIndex = FindStyleIndex(plan.Styles, next, plan.Styles[current].Variant);
                    }

                    if (nextIndex < 0 || !visited.Add(nextIndex))
                    {
                        diagnostic = new("DXAMLGEN011", $"Style '{style.Key}' has a cyclic BasedOn chain.", style.Range);
                        return false;
                    }

                    current = nextIndex;
                }
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
                    if (member.Value.Kind == XamlValueKind.Binding &&
                        member.Value.Binding.SourceKind == XamlBindingSourceKind.Context)
                    {
                        if (!(plan.Templates[templateIndex].ItemTypeName is { } itemType
                                ? registry.TryResolveBindingVariant(itemType, member.Value.Binding.Path, member.Value.Binding.ConverterKey, out var bindingDefinition)
                                : registry.TryResolveBindingVariant(member.Value.Binding.Path, member.Value.Binding.ConverterKey, out bindingDefinition)))
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

                    if (!CanEmitMember(
                        member,
                        registry,
                        templateType,
                        out var memberError,
                        plan.Templates[templateIndex].ItemTypeName))
                    {
                        diagnostic = new("DXAMLGEN002", memberError, member.Range);
                        return false;
                    }
                }
            }
        }

        var hasReactiveEffects = effectSites.Any(static site => site.HasReactiveBindings);
        var hasContextNotifications = bindingSites.Count != 0 || hasReactiveEffects;
        var hasGeneratedProgram = collectionSites.Count != 0 || resolvedRelationBindings.Count != 0 ||
            resolvedMultiBindings.Count != 0 || resolvedTriggers.Count != 0 || resolvedBehaviors.Count != 0 ||
            hasReactiveEffects;
        var writer = new StringBuilder(2048);
        var implicitEffectIndex = 0;
        writer.AppendLine("#nullable enable");
        writer.AppendLine("using Delta;");
        writer.Append("namespace ").Append(namespaceName).AppendLine(";");
        writer.AppendLine();
        writer.Append("public sealed class ").Append(className).Append(" : global::System.IDisposable");
        if (hasGeneratedProgram)
        {
            writer.Append(", global::Delta.XAML.IUiGeneratedDocumentProgram");
        }

        writer.AppendLine();
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
                .Append(BindingValueType(binding))
                .Append("> _binding")
                .Append(i)
                .AppendLine(";");
        }

        for (var i = 0; i < collectionSites.Count; i++)
        {
            var site = collectionSites[i];
            writer.Append("    private readonly global::Delta.XAML.UiVirtualizingPresenter<")
                .Append(site.Definition.CollectionItemTypeName).Append(", ")
                .Append(site.Definition.ValueTypeName).Append(", ItemPlan").Append(i)
                .Append("> _collection").Append(i).AppendLine(";");
            if (site.IsPicker)
            {
                writer.Append("    private readonly global::Delta.XAML.UiPickerSelectionPresenter<")
                    .Append(site.Definition.CollectionItemTypeName).Append(", ")
                    .Append(site.Definition.ValueTypeName).Append(", ItemPlan").Append(i)
                    .Append("> _pickerSelection").Append(i).AppendLine(";");
            }
            if (site.Count < 0)
            {
                writer.Append("    private readonly global::Delta.XAML.UiScrollViewer _collectionViewport").Append(i).AppendLine(";");
            }
        }

        for (var i = 0; i < resolvedRelationBindings.Count; i++)
        {
            writer.Append("    private readonly global::Delta.XAML.UiRelationBinding<RelationPlan").Append(i).Append(", ")
                .Append(resolvedRelationBindings[i].ValueTypeName).Append("> _relation").Append(i).AppendLine(";");
        }
        for (var i = 0; i < resolvedMultiBindings.Count; i++)
        {
            var multi = resolvedMultiBindings[i];
            if (multi.ContextTypeName is { } contextType)
            {
                writer.Append("    private readonly global::Delta.XAML.UiContextMultiBinding<MultiPlan").Append(i).Append(", ")
                    .Append(contextType).Append(", ").Append(multi.ValueTypeName).Append("> _multi").Append(i).AppendLine(";");
            }
            else
            {
                writer.Append("    private readonly global::Delta.XAML.UiRelationMultiBinding<MultiPlan").Append(i).Append(", ")
                    .Append(multi.ValueTypeName).Append("> _multi").Append(i).AppendLine(";");
            }
        }
        for (var i = 0; i < resolvedTriggers.Count; i++)
        {
            writer.Append("    private bool _trigger").Append(i).AppendLine(";");
            writer.Append("    private bool _trigger").Append(i).AppendLine("Initialized;");
            for (var dependencyIndex = 0; dependencyIndex < resolvedTriggers[i].Dependencies.Length; dependencyIndex++)
            {
                writer.Append("    private ").Append(resolvedTriggers[i].Dependencies[dependencyIndex].ValueTypeName)
                    .Append(" _trigger").Append(i).Append("Dependency").Append(dependencyIndex).AppendLine(";");
            }
        }
        for (var i = 0; i < resolvedBehaviors.Count; i++)
        {
            writer.Append("    private ").Append(resolvedBehaviors[i].Definition.StateTypeName)
                .Append(" _behavior").Append(i).AppendLine("State;");
        }

        for (var siteIndex = 0; siteIndex < effectSites.Count; siteIndex++)
        {
            var site = effectSites[siteIndex];
            if (site.NodeIndex >= 0 && site.HasReactiveBindings)
            {
                writer.Append("    private readonly global::Delta.XAML.UiElement _effectTarget")
                    .Append(siteIndex).AppendLine(";");
            }

            for (var bindingIndex = 0; bindingIndex < site.Bindings.Count; bindingIndex++)
            {
                writer.Append("    private ").Append(site.Bindings[bindingIndex].Definition.ValueTypeName)
                    .Append(" _effect").Append(siteIndex).Append("Value").Append(bindingIndex).AppendLine(";");
            }

            if (site.HasReactiveBindings)
            {
                writer.Append("    private bool _effect").Append(siteIndex).AppendLine("Dirty;");
            }
        }

        if (hasContextNotifications)
        {
            writer.AppendLine("    private readonly global::System.ComponentModel.INotifyPropertyChanged? _bindingSource;");
        }
        if (bindingSourceType is not null)
        {
            writer.Append("    private readonly ").Append(bindingSourceType).AppendLine(" _context;");
        }

        EmitItemPlans(writer, collectionSites, registry, plan.ResourceSlots);
        EmitRelationPlans(writer, resolvedRelationBindings);
        EmitMultiBindingPlans(writer, resolvedMultiBindings);
        EmitTemplateFactories(writer, plan.Templates, registry, plan.ResourceSlots);

        writer.AppendLine("    public global::Delta.XAML.UiResourceCatalog Resources { get; }");
        writer.AppendLine("    public global::Delta.XAML.UiTheme Theme { get; }");
        writer.Append("    public global::Delta.XAML.UiDocument").Append(hasVisualRoot ? string.Empty : "?").AppendLine(" Document { get; }");
        if (plan.GradientResources.Length != 0)
        {
            writer.AppendLine();
            writer.AppendLine("    private static global::Delta.XAML.UiColor ResolveGradientColor(global::Delta.XAML.UiResourceCatalog resources, global::Delta.XAML.Contract.UiResourceId resource, string key)");
            writer.AppendLine("    {");
            writer.AppendLine("        if (resources.TryResolve(resource, out var value) && value is global::Delta.XAML.UiColor color)");
            writer.AppendLine("        {");
            writer.AppendLine("            return color;");
            writer.AppendLine("        }");
            writer.AppendLine("        throw new global::System.InvalidOperationException(\"Gradient color resource '\" + key + \"' was not found or is not a color.\");");
            writer.AppendLine("    }");
        }
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
            writer.AppendLine("        _context = context;");
        }

        for (var siteIndex = 0; siteIndex < effectSites.Count; siteIndex++)
        {
            var site = effectSites[siteIndex];
            for (var bindingIndex = 0; bindingIndex < site.Bindings.Count; bindingIndex++)
            {
                writer.Append("        _effect").Append(siteIndex).Append("Value").Append(bindingIndex)
                    .Append(" = ").Append(ReplaceSourceIdentifier(
                        site.Bindings[bindingIndex].Definition.ReadExpression,
                        "context")).AppendLine(";");
            }
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

        EmitGradientResources(writer, plan.GradientResources, plan.ResourceSlots);

        for (var siteIndex = 0; siteIndex < effectSites.Count; siteIndex++)
        {
            if (effectSites[siteIndex].NodeIndex < 0)
            {
                EmitEffectRegistration(writer, siteIndex, effectSites[siteIndex], null, "        ");
            }
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
            EmitImplicitVisualEffect(
                writer,
                resourceNodes[i],
                i,
                "resource",
                plan.Source,
                plan.ScalarResources,
                ref implicitEffectIndex);
            EmitImplicitTextEffect(
                writer,
                resourceNodes[i],
                i,
                "resource",
                plan.Source,
                ref implicitEffectIndex);
            if (resourceNodes[i].InlineEffect is { } resourceEffect)
            {
                EmitEffectRegistration(
                    writer,
                    effectSites.Count + i,
                    new(i, resourceEffect, Array.Empty<EffectBindingSite>()),
                    "resource" + i,
                    "        ");
            }
            EmitTextSpans(writer, i, resourceNodes[i], "resource");
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
            EmitImplicitVisualEffect(writer, nodes[i], i, "node", plan.Source, plan.ScalarResources, ref implicitEffectIndex);
            EmitImplicitTextEffect(writer, nodes[i], i, "node", plan.Source, ref implicitEffectIndex);
            for (var siteIndex = 0; siteIndex < effectSites.Count; siteIndex++)
            {
                if (effectSites[siteIndex].NodeIndex == i)
                {
                    EmitEffectRegistration(writer, siteIndex, effectSites[siteIndex], "node" + i, "        ");
                }
            }
            EmitTextSpans(writer, i, nodes[i], "node");
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

        for (var i = 0; i < collectionSites.Count; i++)
        {
            var site = collectionSites[i];
            writer.Append("        _collection").Append(i).Append(" = new global::Delta.XAML.UiVirtualizingPresenter<")
                .Append(site.Definition.CollectionItemTypeName).Append(", ")
                .Append(site.Definition.ValueTypeName).Append(", ItemPlan").Append(i).Append(">(node")
                .Append(site.NodeIndex).Append('.').Append(site.HostPath).Append(", ")
                .Append(ReplaceSourceIdentifier(site.Definition.ReadExpression, "context")).AppendLine(", Resources);");
            if (site.IsPicker)
            {
                writer.Append("        _pickerSelection").Append(i).Append(" = new global::Delta.XAML.UiPickerSelectionPresenter<")
                    .Append(site.Definition.CollectionItemTypeName).Append(", ")
                    .Append(site.Definition.ValueTypeName).Append(", ItemPlan").Append(i).Append(">(node")
                    .Append(site.NodeIndex).Append(", ")
                    .Append(ReplaceSourceIdentifier(site.Definition.ReadExpression, "context")).AppendLine(", Resources);");
            }
            if (site.Count < 0)
            {
                writer.Append("        _collectionViewport").Append(i).Append(" = node").Append(site.NodeIndex)
                    .Append('.').Append(site.ViewportPath).AppendLine(";");
            }
        }

        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            var members = nodes[nodeIndex].Members;
            for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
            {
                var member = members[memberIndex];
                if (member.Name != "StyleKey" || member.Value.Kind != XamlValueKind.String)
                {
                    continue;
                }

                var variant = members.FirstOrDefault(static candidate => candidate.Name == "Variant");
                var variantText = variant.Value.Kind == XamlValueKind.String ? variant.Value.Literal.CanonicalText : null;
                if (!TryFindStyle(plan.Styles, member.Value.Literal.CanonicalText, variantText, out var style) &&
                    !TryFindStyle(plan.Styles, member.Value.Literal.CanonicalText, null, out style))
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

        for (var i = 0; i < resolvedRelationBindings.Count; i++)
        {
            var relation = resolvedRelationBindings[i];
            writer.Append("        _relation").Append(i).Append(" = new global::Delta.XAML.UiRelationBinding<RelationPlan")
                .Append(i).Append(", ").Append(relation.ValueTypeName).Append(">(node")
                .Append(relation.NodeIndex).Append(", ").Append(relation.SourceExpression)
                .Append(", global::Delta.XAML.UiBindingMode.").Append(relation.Member.Value.Binding.Mode).AppendLine(");");
            writer.Append("        node").Append(relation.NodeIndex).Append(".SetBinding(")
                .Append(Quote(relation.Member.Name)).Append(", _relation").Append(i).AppendLine(");");
        }
        for (var i = 0; i < resolvedMultiBindings.Count; i++)
        {
            var multi = resolvedMultiBindings[i];
            writer.Append("        _multi").Append(i).Append(" = new global::Delta.XAML.");
            if (multi.ContextTypeName is { } contextType)
            {
                writer.Append("UiContextMultiBinding<MultiPlan").Append(i).Append(", ").Append(contextType).Append(", ")
                    .Append(multi.ValueTypeName).Append(">(_context, node").Append(multi.NodeIndex).Append(", ");
            }
            else
            {
                writer.Append("UiRelationMultiBinding<MultiPlan").Append(i).Append(", ").Append(multi.ValueTypeName)
                    .Append(">(node").Append(multi.NodeIndex).Append(", ");
            }
            for (var sourceIndex = 0; sourceIndex < multi.SourceExpressions.Length; sourceIndex++)
            {
                if (sourceIndex != 0)
                {
                    writer.Append(", ");
                }

                writer.Append(multi.SourceExpressions[sourceIndex]);
            }

            writer.AppendLine(");");
            writer.Append("        node").Append(multi.NodeIndex).Append(".SetBinding(")
                .Append(Quote(multi.Member.Name)).Append(", _multi").Append(i).AppendLine(");");
        }

        for (var i = 0; i < bindingSites.Count; i++)
        {
            EmitBinding(writer, i, bindingSites[i]);
        }

        if (hasContextNotifications)
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
            writer.Append("        Document = new global::Delta.XAML.UiDocument(node0, textService, fontResolver, Theme");
            if (hasGeneratedProgram)
            {
                writer.Append(", program: this");
            }

            writer.AppendLine(");");
        }
        var namedNodes = nodes.Where(static node => node.ScopeName is not null).ToList();
        writer.Append("        _scopeElements = new global::Delta.XAML.UiElement[]").AppendLine();
        writer.AppendLine("        {");
        for (var i = 0; i < namedNodes.Count; i++)
        {
            writer.Append("            node").Append(FindNode(nodes, namedNodes[i])).AppendLine(",");
        }

        writer.AppendLine("        };");
        for (var triggerIndex = 0; triggerIndex < resolvedTriggers.Count; triggerIndex++)
        {
            var trigger = resolvedTriggers[triggerIndex];
            for (var dependencyIndex = 0; dependencyIndex < trigger.Dependencies.Length; dependencyIndex++)
            {
                writer.Append("        _trigger").Append(triggerIndex).Append("Dependency").Append(dependencyIndex)
                    .Append(" = ").Append(trigger.Dependencies[dependencyIndex].ReadExpression).AppendLine(";");
            }
        }
        for (var behaviorIndex = 0; behaviorIndex < resolvedBehaviors.Count; behaviorIndex++)
        {
            var behavior = resolvedBehaviors[behaviorIndex];
            writer.Append("        global::Delta.XAML.UiGeneratedBehaviors.Attach<")
                .Append(behavior.Definition.PlanTypeName).Append(", ").Append(behavior.Definition.StateTypeName)
                .Append(">(").Append(behavior.TargetExpression).Append(", ref _behavior").Append(behaviorIndex).AppendLine("State);");
        }
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

        for (var siteIndex = 0; siteIndex < effectSites.Count; siteIndex++)
        {
            EmitEffectBindingRefresh(writer, siteIndex, effectSites[siteIndex], null, "        ");
        }

        writer.AppendLine("    }");
        writer.AppendLine();
        if (hasGeneratedProgram)
        {
            writer.AppendLine("    public void RunStage(global::Delta.XAML.UiDocument document, global::Delta.XAML.UiGeneratedStage stage)");
            writer.AppendLine("    {");
            if (resolvedRelationBindings.Count != 0 || resolvedMultiBindings.Count != 0 || resolvedBehaviors.Count != 0)
            {
                writer.AppendLine("        if (stage == global::Delta.XAML.UiGeneratedStage.AfterInput)");
                writer.AppendLine("        {");
                for (var i = 0; i < resolvedRelationBindings.Count; i++)
                {
                    writer.Append("            _relation").Append(i).AppendLine(".Refresh();");
                }
                for (var i = 0; i < resolvedMultiBindings.Count; i++)
                {
                    writer.Append("            _multi").Append(i).AppendLine(".Refresh();");
                }
                for (var i = 0; i < resolvedBehaviors.Count; i++)
                {
                    var behavior = resolvedBehaviors[i];
                    writer.Append("            global::Delta.XAML.UiGeneratedBehaviors.Update<")
                        .Append(behavior.Definition.PlanTypeName).Append(", ").Append(behavior.Definition.StateTypeName)
                        .Append(">(document, ").Append(behavior.TargetExpression).Append(", ref _behavior").Append(i).AppendLine("State);");
                }

                writer.AppendLine("            return;");
                writer.AppendLine("        }");
            }

            writer.AppendLine("        if (stage != global::Delta.XAML.UiGeneratedStage.AfterBindings)");
            writer.AppendLine("        {");
            writer.AppendLine("            return;");
            writer.AppendLine("        }");
            for (var siteIndex = 0; siteIndex < effectSites.Count; siteIndex++)
            {
                if (!effectSites[siteIndex].HasReactiveBindings)
                {
                    continue;
                }

                writer.Append("        if (_effect").Append(siteIndex).AppendLine("Dirty)");
                writer.AppendLine("        {");
                writer.Append("            _effect").Append(siteIndex).AppendLine("Dirty = false;");
                writer.Append("            UpdateEffect").Append(siteIndex).AppendLine("();");
                writer.AppendLine("        }");
            }
            for (var i = 0; i < collectionSites.Count; i++)
            {
                var site = collectionSites[i];
                writer.Append("        var sourceCount").Append(i).Append(" = _collection").Append(i).AppendLine(".Source.Count;");
                if (site.Count < 0)
                {
                    writer.Append("        if (_collection").Append(i).AppendLine(".Host.IsGridLayout)");
                    writer.AppendLine("        {");
                    writer.Append("            _collection").Append(i).Append(".Realize(new global::Delta.XAML.UiRealizationRange(0, sourceCount").Append(i).AppendLine("));");
                    writer.AppendLine("        }");
                    writer.AppendLine("        else");
                    writer.AppendLine("        {");
                    writer.Append("            var range").Append(i).Append(" = global::Delta.XAML.UiVirtualizingLayout.Vertical(_collectionViewport")
                        .Append(i).Append(".OffsetY, ");
                    if (float.IsFinite(site.ViewportExtent) && site.ViewportExtent > 0)
                    {
                        writer.Append(site.ViewportExtent.ToString("R", CultureInfo.InvariantCulture)).Append('f');
                    }
                    else
                    {
                        writer.Append("_collectionViewport").Append(i).Append(".Bounds.w > 0 ? _collectionViewport")
                            .Append(i).Append(".Bounds.w : document.Viewport.y");
                    }

                    writer.Append(", ")
                        .Append(site.ItemExtent.ToString("R", CultureInfo.InvariantCulture)).Append("f, sourceCount").Append(i).AppendLine(");");
                    writer.Append("            _collection").Append(i).Append(".Realize(range").Append(i).AppendLine(");");
                    writer.AppendLine("        }");
                }
                else
                {
                    writer.Append("        var start").Append(i).Append(" = Maths.Min(").Append(site.Start).Append(", sourceCount").Append(i).AppendLine(");");
                    writer.Append("        var count").Append(i).Append(" = Maths.Min(").Append(site.Count.ToString(CultureInfo.InvariantCulture))
                        .Append(", sourceCount").Append(i).Append(" - start").Append(i).AppendLine(");");
                    writer.Append("        _collection").Append(i).Append(".Realize(new global::Delta.XAML.UiRealizationRange(start")
                        .Append(i).Append(", count").Append(i).AppendLine("));");
                }
                if (site.IsPicker)
                {
                    writer.Append("        _pickerSelection").Append(i).AppendLine(".Update();");
                }
            }

            for (var i = 0; i < resolvedTriggers.Count; i++)
            {
                var trigger = resolvedTriggers[i];
                writer.Append("        if (!_trigger").Append(i).Append("Initialized");
                for (var dependencyIndex = 0; dependencyIndex < trigger.Dependencies.Length; dependencyIndex++)
                {
                    var dependency = trigger.Dependencies[dependencyIndex];
                    writer.Append(" || !global::System.Collections.Generic.EqualityComparer<")
                        .Append(dependency.ValueTypeName).Append(">.Default.Equals(_trigger").Append(i)
                        .Append("Dependency").Append(dependencyIndex).Append(", ").Append(dependency.ReadExpression).Append(')');
                }

                writer.AppendLine(")");
                writer.AppendLine("        {");
                writer.Append("            _trigger").Append(i).AppendLine("Initialized = true;");
                for (var dependencyIndex = 0; dependencyIndex < trigger.Dependencies.Length; dependencyIndex++)
                {
                    writer.Append("            _trigger").Append(i).Append("Dependency").Append(dependencyIndex)
                        .Append(" = ").Append(trigger.Dependencies[dependencyIndex].ReadExpression).AppendLine(";");
                }

                writer.Append("            if (global::Delta.XAML.UiGeneratedConditions.Apply(")
                    .Append(trigger.ConditionExpression).Append(", ").Append(trigger.TargetExpression).Append(", ")
                    .Append(trigger.TargetPropertyExpression).Append(", ").Append(trigger.SetValueExpression)
                    .Append(", ref _trigger").Append(i).Append(')');
                if (trigger.Action is not null)
                {
                    writer.Append(" && _trigger").Append(i);
                }

                writer.AppendLine(")");
                writer.AppendLine("            {");
                if (trigger.Action is { } action)
                {
                    writer.Append("                global::Delta.XAML.UiGeneratedActions.Publish(document, ")
                        .Append(trigger.TargetExpression).Append(", global::Delta.XAML.UiSemanticActionKind.")
                        .Append(action).Append(", ").Append(trigger.Argument is null ? "null" : Quote(trigger.Argument)).AppendLine(");");
                }

                writer.AppendLine("            }");
                writer.AppendLine("        }");
            }

            writer.AppendLine("    }");
            writer.AppendLine();
        }
        if (hasContextNotifications)
        {
            EmitBindingNotificationHandler(writer, bindingSites, effectSites);
            writer.AppendLine();
        }

        for (var siteIndex = 0; siteIndex < effectSites.Count; siteIndex++)
        {
            if (effectSites[siteIndex].HasReactiveBindings)
            {
                EmitEffectUpdateMethod(writer, siteIndex, effectSites[siteIndex]);
                writer.AppendLine();
            }
        }

        writer.AppendLine("    public void Dispose()");
        writer.AppendLine("    {");
        if (hasContextNotifications)
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
        for (var i = 0; i < resolvedMultiBindings.Count; i++)
        {
            writer.Append("        _multi").Append(i).AppendLine(".Dispose();");
        }
        for (var i = 0; i < collectionSites.Count; i++)
        {
            if (collectionSites[i].IsPicker)
            {
                writer.Append("        _pickerSelection").Append(i).AppendLine(".Dispose();");
            }
        }
        for (var i = 0; i < resolvedBehaviors.Count; i++)
        {
            var behavior = resolvedBehaviors[i];
            writer.Append("        global::Delta.XAML.UiGeneratedBehaviors.Detach<")
                .Append(behavior.Definition.PlanTypeName).Append(", ").Append(behavior.Definition.StateTypeName)
                .Append(">(").Append(behavior.TargetExpression).Append(", ref _behavior").Append(i).AppendLine("State);");
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
        out string error,
        string? bindingSourceTypeName = null)
    {
        error = string.Empty;
        if (member.AttachedPropertyExpression is not null)
        {
            if (member.Value.Kind is XamlValueKind.Binding or XamlValueKind.ResourceReference)
            {
                error = $"Attached property '{member.Name}' requires a generated literal setter in this artifact.";
                return false;
            }

            return TryLiteralExpression(member.Name, member.Value, out _, out error);
        }

        if (member.Value.Kind == XamlValueKind.MultiBinding)
        {
            if (!TryTypedPropertyExpression(member.Name, out _))
            {
                error = $"MultiBinding target '{member.Name}' has no typed library descriptor.";
                return false;
            }

            return true;
        }

        if (member.Value.Kind == XamlValueKind.Binding)
        {
            if (member.Value.Binding.SourceKind != XamlBindingSourceKind.Context)
            {
                if (member.Value.Binding.ConverterKey is not null)
                {
                    error = $"Relation binding '{member.Value.Binding.Path}' cannot use a context converter; put conversion in a generated relation plan.";
                    return false;
                }

                if (!TryTypedPropertyExpression(member.Name, out _))
                {
                    error = $"Relation binding target '{member.Name}' has no typed library descriptor.";
                    return false;
                }

                return true;
            }

            if (!(bindingSourceTypeName is not null
                    ? registry.TryResolveBindingVariant(bindingSourceTypeName, member.Value.Binding.Path, member.Value.Binding.ConverterKey, out var binding)
                    : registry.TryResolveBindingVariant(member.Value.Binding.Path, member.Value.Binding.ConverterKey, out binding)))
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

            if (member.Value.Binding.StringFormat is not null && member.Name != "Text")
            {
                error = $"Binding '{member.Value.Binding.Path}' uses StringFormat on '{member.Name}'; formatted bindings require a string Text target.";
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

        if (member.Value.Kind is not (XamlValueKind.Binding or XamlValueKind.MultiBinding or XamlValueKind.ResourceReference) &&
            !TryLiteralExpression(member.Name, member.Value, out _, out error))
        {
            return false;
        }

        if (member.Value.Kind is XamlValueKind.Binding or XamlValueKind.MultiBinding or XamlValueKind.ResourceReference &&
            !TryTypedPropertyExpression(member.Name, out _))
        {
            error = $"Property '{member.Name}' requires a generated typed library descriptor for bindings and resources.";
            return false;
        }

        return true;
    }

    private static bool TryCreateEffectSite(
        int nodeIndex,
        XamlEffectPlan plan,
        XamlSemanticRegistry registry,
        ref string? bindingSourceType,
        out EffectSite site,
        out string error)
    {
        var bindings = new List<EffectBindingSite>();
        for (var layerIndex = 0; layerIndex < plan.Layers.Length; layerIndex++)
        {
            var layer = plan.Layers[layerIndex];
            for (var memberIndex = 0; memberIndex < layer.Members.Length; memberIndex++)
            {
                var member = layer.Members[memberIndex];
                if (member.Value.Kind != XamlValueKind.Binding)
                {
                    continue;
                }

                if (!registry.TryResolveBindingVariant(
                        member.Value.Binding.Path,
                        member.Value.Binding.ConverterKey,
                        out var definition))
                {
                    site = default!;
                    error = $"Effect binding path '{member.Value.Binding.Path}' has no typed compile-time definition.";
                    return false;
                }

                if (bindingSourceType is not null && !SameTypeName(bindingSourceType, definition.SourceTypeName))
                {
                    site = default!;
                    error = "One generated artifact must use one typed binding source context.";
                    return false;
                }

                if (!TryValidateBinding(definition, out error) ||
                    !IsEffectBindingType(member.Name, definition.ValueTypeName))
                {
                    site = default!;
                    if (error.Length == 0)
                    {
                        error = $"Effect member '{layer.Kind}.{member.Name}' cannot consume generated value type '{definition.ValueTypeName}'.";
                    }

                    return false;
                }

                if (member.Value.Binding.StringFormat is not null)
                {
                    site = default!;
                    error = $"Effect member '{layer.Kind}.{member.Name}' cannot use StringFormat.";
                    return false;
                }

                bindingSourceType = definition.SourceTypeName;
                bindings.Add(new(layer.Kind, member, definition));
            }
        }

        site = new(nodeIndex, plan, bindings);
        error = string.Empty;
        return true;
    }

    private static bool IsEffectBindingType(string memberName, string typeName)
    {
        var normalized = typeName.StartsWith("global::", StringComparison.Ordinal)
            ? typeName[8..]
            : typeName;
        return memberName switch
        {
            "Color" => normalized is "Delta.XAML.UiColor" or "Delta.float4",
            "Offset" => normalized == "Delta.float2",
            _ => normalized is "float" or "System.Single",
        };
    }

    private static bool HasBindings(XamlEffectPlan plan)
    {
        for (var layerIndex = 0; layerIndex < plan.Layers.Length; layerIndex++)
        {
            for (var memberIndex = 0; memberIndex < plan.Layers[layerIndex].Members.Length; memberIndex++)
            {
                if (plan.Layers[layerIndex].Members[memberIndex].Value.Kind == XamlValueKind.Binding)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasStaticEffectReference(List<XamlObjectPlan> nodes, UiResourceId resource)
    {
        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            for (var memberIndex = 0; memberIndex < nodes[nodeIndex].Members.Length; memberIndex++)
            {
                var member = nodes[nodeIndex].Members[memberIndex];
                if (member.Name == "EffectSet" && member.Value.Kind == XamlValueKind.ResourceReference &&
                    member.Value.Resource.Id == resource && !member.Value.Resource.IsDynamic)
                {
                    return true;
                }
            }
        }

        return false;
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
        if (IsCollectionMetadata(member.Name))
        {
            return;
        }

        if (member.AttachedPropertyExpression is { } attachedProperty)
        {
            if (!TryLiteralExpression(member.Name, member.Value, out var attachedValue, out var attachedError))
            {
                throw new InvalidOperationException(attachedError);
            }

            writer.Append("        ").Append(variablePrefix).Append(nodeIndex).Append(".SetAttachedValue(")
                .Append(attachedProperty).Append(", ").Append(attachedValue).AppendLine(");");
            return;
        }

        if (member.Value.Kind is XamlValueKind.Binding or XamlValueKind.MultiBinding)
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
            case "Value" when type.Name.LocalName == "NumericEditor":
                writer.Append("Initialize(").Append(expression).AppendLine(");");
                break;
            default:
                writer.Append(member.Name).Append(" = ").Append(expression).AppendLine(";");
                break;
        }
    }

    private static void EmitEffectRegistration(
        StringBuilder writer,
        int siteIndex,
        EffectSite site,
        string? targetExpression,
        string indentation)
    {
        if (site.HasReactiveBindings)
        {
            if (targetExpression is not null)
            {
                writer.Append(indentation).Append("_effectTarget").Append(siteIndex)
                    .Append(" = ").Append(targetExpression).AppendLine(";");
            }

            writer.Append(indentation).Append("UpdateEffect").Append(siteIndex).AppendLine("();");
            return;
        }

        writer.Append(indentation).Append("var effect").Append(siteIndex).Append(" = ")
            .Append(EffectResourceExpression(siteIndex, site)).AppendLine(";");
        writer.Append(indentation).Append("Resources.Set(effect").Append(siteIndex).AppendLine(");");
        if (targetExpression is not null)
        {
            writer.Append(indentation).Append(targetExpression).Append(".EffectSet = effect")
                .Append(siteIndex).AppendLine(".Set;");
        }
    }

    private static void EmitEffectUpdateMethod(StringBuilder writer, int siteIndex, EffectSite site)
    {
        writer.Append("    private void UpdateEffect").Append(siteIndex).AppendLine("()");
        writer.AppendLine("    {");
        writer.Append("        var effect = ").Append(EffectResourceExpression(siteIndex, site)).AppendLine(";");
        writer.AppendLine("        Resources.Set(effect);");
        if (site.NodeIndex >= 0)
        {
            writer.Append("        _effectTarget").Append(siteIndex).AppendLine(".EffectSet = effect.Set;");
        }

        writer.AppendLine("    }");
    }

    private static void EmitEffectBindingRefresh(
        StringBuilder writer,
        int siteIndex,
        EffectSite site,
        string? notification,
        string indentation)
    {
        var updated = false;
        for (var bindingIndex = 0; bindingIndex < site.Bindings.Count; bindingIndex++)
        {
            var binding = site.Bindings[bindingIndex];
            if (binding.Member.Value.Binding.Mode == UiBindingMode.OneTime ||
                (notification is not null && !string.Equals(
                    notification,
                    BindingNotificationName(binding.Definition.Path),
                    StringComparison.Ordinal)))
            {
                continue;
            }

            writer.Append(indentation).Append("_effect").Append(siteIndex).Append("Value").Append(bindingIndex)
                .Append(" = ").Append(ReplaceSourceIdentifier(binding.Definition.ReadExpression, "_context"))
                .AppendLine(";");
            updated = true;
        }

        if (updated)
        {
            writer.Append(indentation).Append("_effect").Append(siteIndex).AppendLine("Dirty = true;");
        }
    }

    private static string EffectResourceExpression(int siteIndex, EffectSite site)
    {
        var stroke = EffectLayerExpression(siteIndex, site, XamlEffectLayerKind.Stroke);
        var outerShadow = EffectLayerExpression(siteIndex, site, XamlEffectLayerKind.OuterShadow);
        var innerShadow = EffectLayerExpression(siteIndex, site, XamlEffectLayerKind.InnerShadow);
        var outerGlow = EffectLayerExpression(siteIndex, site, XamlEffectLayerKind.OuterGlow);
        var innerGlow = EffectLayerExpression(siteIndex, site, XamlEffectLayerKind.InnerGlow);
        var cachedMask = site.Plan.CachedMask.IsValid
            ? ResourceIdExpression(site.Plan.CachedMask)
            : "default";
        var outsets = EffectOutsetsExpression(site.Plan.Outsets);
        var builder = new StringBuilder();
        return builder.Append(site.Plan.Target == UiEffectTarget.Text
                ? "global::Delta.XAML.UiEffects.CreateText("
                : "global::Delta.XAML.UiEffects.CreateVisual(")
            .Append(ResourceIdExpression(site.Plan.Resource)).Append(", ")
            .Append(EffectCapabilitiesExpression(site.Plan)).Append(", ")
            .Append(stroke).Append(", ")
            .Append(outerShadow).Append(", ")
            .Append(innerShadow).Append(", ")
            .Append(outerGlow).Append(", ")
            .Append(innerGlow)
            .Append(", global::Delta.XAML.Contract.PaintUnits.").Append(site.Plan.Units)
            .Append(", global::Delta.XAML.Contract.UiEffectQuality.").Append(site.Plan.Quality)
            .Append(", ").Append(cachedMask).Append(", ").Append(outsets).Append(')')
            .ToString();
    }

    private static string EffectCapabilitiesExpression(XamlEffectPlan plan)
    {
        var capabilities = new List<string>(plan.Layers.Length);
        for (var i = 0; i < plan.Layers.Length; i++)
        {
            capabilities.Add("global::Delta.XAML.Contract.UiEffectCapabilities." + plan.Layers[i].Kind);
        }

        return capabilities.Count == 0
            ? "global::Delta.XAML.Contract.UiEffectCapabilities.None"
            : string.Join(" | ", capabilities);
    }

    private static string EffectLayerExpression(int siteIndex, EffectSite site, XamlEffectLayerKind kind)
    {
        XamlEffectLayerPlan? layer = null;
        for (var i = 0; i < site.Plan.Layers.Length; i++)
        {
            if (site.Plan.Layers[i].Kind == kind)
            {
                layer = site.Plan.Layers[i];
                break;
            }
        }

        if (layer is null)
        {
            return "default";
        }

        return "new global::Delta.XAML.Contract.UiEffectLayer(" +
            EffectMemberExpression(siteIndex, site, layer, "Color", "default") + ", " +
            EffectMemberExpression(siteIndex, site, layer, "Offset", "default") + ", " +
            EffectMemberExpression(siteIndex, site, layer, "Width", "0f") + ", " +
            EffectMemberExpression(siteIndex, site, layer, "Radius", "0f") + ", " +
            EffectMemberExpression(siteIndex, site, layer, "Spread", "0f") + ", " +
            EffectMemberExpression(siteIndex, site, layer, "Intensity", "1f") + ")";
    }

    private static string EffectMemberExpression(
        int siteIndex,
        EffectSite site,
        XamlEffectLayerPlan layer,
        string name,
        string defaultExpression)
    {
        for (var memberIndex = 0; memberIndex < layer.Members.Length; memberIndex++)
        {
            var member = layer.Members[memberIndex];
            if (!string.Equals(member.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (member.Value.Kind == XamlValueKind.Binding)
            {
                for (var bindingIndex = 0; bindingIndex < site.Bindings.Count; bindingIndex++)
                {
                    var binding = site.Bindings[bindingIndex];
                    if (binding.Layer == layer.Kind && binding.Member.Equals(member))
                    {
                        var field = "_effect" + siteIndex.ToString(CultureInfo.InvariantCulture) + "Value" +
                            bindingIndex.ToString(CultureInfo.InvariantCulture);
                        var typeName = binding.Definition.ValueTypeName.StartsWith("global::", StringComparison.Ordinal)
                            ? binding.Definition.ValueTypeName[8..]
                            : binding.Definition.ValueTypeName;
                        return name == "Color" && typeName == "Delta.XAML.UiColor"
                            ? "global::Delta.XAML.UiEffects.Color(" + field + ")"
                            : field;
                    }
                }

                throw new InvalidOperationException($"Effect binding '{member.Value.Binding.Path}' was not resolved.");
            }

            return EffectLiteralExpression(member);
        }

        return defaultExpression;
    }

    private static string EffectLiteralExpression(XamlEffectMemberPlan member)
    {
        var value = member.Value.Literal.CanonicalText;
        switch (member.Value.Kind)
        {
            case XamlValueKind.Color when TryColorVector(value, out var color):
                return color;
            case XamlValueKind.Single:
                return value + "f";
            case XamlValueKind.Vector2:
                return EffectVectorExpression(value);
            default:
                throw new InvalidOperationException($"Effect member '{member.Name}' has unsupported literal kind '{member.Value.Kind}'.");
        }
    }

    private static string EffectVectorExpression(string value)
    {
        var separator = value.IndexOf(',', StringComparison.Ordinal);
        return "new global::Delta.float2(" + value[..separator] + "f, " + value[(separator + 1)..] + "f)";
    }

    private static string EffectOutsetsExpression(XamlValuePlan? outsets)
    {
        if (outsets is not { } value)
        {
            return "null";
        }

        if (value.Kind != XamlValueKind.Thickness ||
            !ThicknessLiteralParser.TryParse(value.Literal.CanonicalText, out var thickness))
        {
            throw new InvalidOperationException("Effect outsets must be a compiled thickness literal.");
        }

        return "new global::Delta.float4(" +
            thickness.Left.ToString("R", CultureInfo.InvariantCulture) + "f, " +
            thickness.Top.ToString("R", CultureInfo.InvariantCulture) + "f, " +
            thickness.Right.ToString("R", CultureInfo.InvariantCulture) + "f, " +
            thickness.Bottom.ToString("R", CultureInfo.InvariantCulture) + "f)";
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
            .Append(BindingValueType(site))
            .Append(">(context, static source => ")
            .Append(BindingReadExpression(site))
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

    private static string BindingValueType(BindingSite site) =>
        BindingValueType(site.Member.Value.Binding, site.Definition);

    private static string BindingValueType(XamlBindingPlan binding, XamlBindingDefinition definition) =>
        binding.StringFormat is null ? definition.ValueTypeName : "global::System.String";

    private static string BindingReadExpression(BindingSite site) =>
        BindingReadExpression(site.Member.Value.Binding, site.Definition);

    private static string BindingReadExpression(XamlBindingPlan binding, XamlBindingDefinition definition)
    {
        if (binding.StringFormat is not { } format)
        {
            return definition.ReadExpression;
        }

        if (binding.CultureName is not { } culture)
        {
            throw new InvalidOperationException("A generated formatted binding requires an explicit culture.");
        }

        return "global::System.String.Format(global::System.Globalization.CultureInfo.GetCultureInfo(" +
            Quote(culture) + "), " + Quote(format) + ", " + definition.ReadExpression + ")";
    }

    private static void EmitBindingNotificationHandler(
        StringBuilder writer,
        IReadOnlyList<BindingSite> bindingSites,
        IReadOnlyList<EffectSite> effectSites)
    {
        var notifications = new List<string>();
        for (var i = 0; i < bindingSites.Count; i++)
        {
            AddUnique(notifications, BindingNotificationName(bindingSites[i].Definition.Path));
        }

        for (var siteIndex = 0; siteIndex < effectSites.Count; siteIndex++)
        {
            var bindings = effectSites[siteIndex].Bindings;
            for (var bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                if (bindings[bindingIndex].Member.Value.Binding.Mode != UiBindingMode.OneTime)
                {
                    AddUnique(notifications, BindingNotificationName(bindings[bindingIndex].Definition.Path));
                }
            }
        }

        writer.AppendLine("    private void OnContextPropertyChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs args)");
        writer.AppendLine("    {");
        writer.AppendLine("        switch (args.PropertyName)");
        writer.AppendLine("        {");
        for (var notificationIndex = 0; notificationIndex < notifications.Count; notificationIndex++)
        {
            var sourceName = notifications[notificationIndex];
            writer.Append("            case ").Append(Quote(sourceName)).AppendLine(":");
            for (var siteIndex = 0; siteIndex < bindingSites.Count; siteIndex++)
            {
                if (string.Equals(sourceName, BindingNotificationName(bindingSites[siteIndex].Definition.Path), StringComparison.Ordinal))
                {
                    EmitBindingQueue(writer, siteIndex, bindingSites[siteIndex], "                ");
                }
            }

            for (var siteIndex = 0; siteIndex < effectSites.Count; siteIndex++)
            {
                EmitEffectBindingRefresh(writer, siteIndex, effectSites[siteIndex], sourceName, "                ");
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

    private static void AddUnique(List<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.Ordinal))
            {
                return;
            }
        }

        values.Add(value);
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
            if (style.Variant is { } variant)
            {
                writer.Append("        style").Append(styleIndex).Append(".SetVariant(").Append(Quote(variant)).AppendLine(");");
            }
        }

        for (var styleIndex = 0; styleIndex < styles.Count; styleIndex++)
        {
            var style = styles[styleIndex];
            if (style.BasedOn is { } basedOn)
            {
                var baseIndex = FindStyleIndex(styles, basedOn, null);
                if (baseIndex < 0)
                {
                    baseIndex = FindStyleIndex(styles, basedOn, style.Variant);
                }

                if (baseIndex >= 0)
                {
                    writer.Append("        style").Append(styleIndex).Append(".SetBasedOn(style").Append(baseIndex).AppendLine(");");
                }
            }
        }

        for (var styleIndex = 0; styleIndex < styles.Count; styleIndex++)
        {
            var style = styles[styleIndex];
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

    private static int FindStyleIndex(IReadOnlyList<XamlStylePlan> styles, string key, string? variant)
    {
        for (var i = 0; i < styles.Count; i++)
        {
            if (string.Equals(styles[i].Key, key, StringComparison.Ordinal) &&
                string.Equals(styles[i].Variant, variant, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void EmitGradientResources(
        StringBuilder writer,
        IReadOnlyList<XamlGradientResourcePlan> gradients,
        IReadOnlyList<XamlResourceSlotPlan> resourceSlots)
    {
        if (gradients.Count == 0)
        {
            return;
        }

        writer.AppendLine("        // Gradient resources use source-scoped IDs for the brush alias and payload.");
        for (var gradientIndex = 0; gradientIndex < gradients.Count; gradientIndex++)
        {
            var gradient = gradients[gradientIndex];
            var stops = new StringBuilder("new global::Delta.XAML.UiGradientStop[] { ");
            for (var stopIndex = 0; stopIndex < gradient.Stops.Length; stopIndex++)
            {
                var stop = gradient.Stops[stopIndex];
                if (!TryGradientColorExpression(stop.Color, resourceSlots, out var colorExpression, out var colorError))
                {
                    throw new InvalidOperationException(colorError);
                }

                if (stopIndex != 0)
                {
                    stops.Append(", ");
                }

                stops.Append("new global::Delta.XAML.UiGradientStop(")
                    .Append(stop.Offset.ToString("R", CultureInfo.InvariantCulture))
                    .Append("f, ").Append(colorExpression).Append(')');
            }

            stops.Append(" }");
            string gradientExpression;
            if (gradient.Kind == XamlGradientKind.Linear)
            {
                if (gradient.StartPoint is { } startPoint && gradient.EndPoint is { } endPoint &&
                    TryVectorExpression(startPoint, out var startX, out var startY) &&
                    TryVectorExpression(endPoint, out var endX, out var endY))
                {
                    gradientExpression = "new global::Delta.XAML.UiLinearGradient(" + startX + ", " + startY + ", " + endX + ", " + endY + ", " + stops + ")";
                }
                else
                {
                    var angle = gradient.Angle is { } anglePlan && anglePlan.Kind == XamlValueKind.Single
                        ? anglePlan.Literal.CanonicalText
                        : "180";
                    gradientExpression = "global::Delta.XAML.UiLinearGradient.Relative(" + angle + "f, " + stops + ")";
                }

                if (gradient.OutlineColor is { } outlineColor)
                {
                    if (!TryGradientColorExpression(outlineColor, resourceSlots, out var outlineExpression, out var outlineError))
                    {
                        throw new InvalidOperationException(outlineError);
                    }

                    var width = gradient.OutlineWidth is { } widthPlan && widthPlan.Kind == XamlValueKind.Single
                        ? widthPlan.Literal.CanonicalText + "f"
                        : "1f";
                    gradientExpression += ".WithOutline(" + outlineExpression + ", " + width + ")";
                }
            }
            else
            {
                if (gradient.Center is not { } center || gradient.Radius is not { } radius ||
                    !TryVectorExpression(center, out var centerX, out var centerY) || radius.Kind != XamlValueKind.Single)
                {
                    throw new InvalidOperationException($"Radial gradient '{gradient.Key}' has invalid geometry.");
                }

                gradientExpression = "new global::Delta.XAML.UiRadialGradient(" + centerX + ", " + centerY + ", " +
                    radius.Literal.CanonicalText + "f, " + stops + ")";

                if (gradient.OutlineColor is { } radialOutlineColor)
                {
                    if (!TryGradientColorExpression(radialOutlineColor, resourceSlots, out var outlineExpression, out var outlineError))
                    {
                        throw new InvalidOperationException(outlineError);
                    }

                    var width = gradient.OutlineWidth is { } widthPlan && widthPlan.Kind == XamlValueKind.Single
                        ? widthPlan.Literal.CanonicalText + "f"
                        : "1f";
                    gradientExpression += ".WithOutline(" + outlineExpression + ", " + width + ")";
                }
            }

            writer.Append("        Resources.Set(").Append(ResourceIdExpression(gradient.PayloadId)).Append(", ")
                .Append(gradientExpression).AppendLine(");");
            writer.Append("        Resources.Set(").Append(ResourceSlotExpression(gradient.Id, resourceSlots))
                .Append(", global::Delta.XAML.UiBrush.")
                .Append(gradient.Kind == XamlGradientKind.Linear ? "LinearGradient" : "RadialGradient")
                .Append('(').Append(ResourceIdExpression(gradient.PayloadId)).AppendLine("));");
        }
    }

    private static bool TryGradientColorExpression(
        XamlValuePlan value,
        IReadOnlyList<XamlResourceSlotPlan> resourceSlots,
        out string expression,
        out string error)
    {
        if (value.Kind == XamlValueKind.ResourceReference)
        {
            if (value.Resource.IsDynamic)
            {
                expression = string.Empty;
                error = "Gradient colors support StaticResource only.";
                return false;
            }

            expression = "ResolveGradientColor(Resources, " + ResourceSlotExpression(value.Resource, resourceSlots) + ", " + Quote(value.Resource.Key) + ")";
            error = string.Empty;
            return true;
        }

        return TryLiteralExpression("Color", value, out expression, out error);
    }

    private static bool TryVectorExpression(XamlValuePlan value, out string x, out string y)
    {
        x = string.Empty;
        y = string.Empty;
        if (value.Kind != XamlValueKind.Vector2)
        {
            return false;
        }

        var parts = value.Literal.CanonicalText.Split(',');
        if (parts.Length != 2)
        {
            return false;
        }

        x = parts[0] + "f";
        y = parts[1] + "f";
        return true;
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
            if (template.ItemTypeName is not null)
            {
                continue;
            }

            var nodes = new List<XamlObjectPlan>();
            Flatten(template.Root, nodes);
            var templateBindingSites = new List<TemplateBindingSite>();
            var templateRelationSites = new List<ResolvedRelationBindingSite>();
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

                    if (member.Value.Binding.SourceKind != XamlBindingSourceKind.Context)
                    {
                        var relationSite = new RelationBindingSite(nodeIndex, member);
                        if (!TryResolveRelationBinding(
                                relationSite,
                                nodes,
                                registry,
                                out var resolvedRelation,
                                out var relationError,
                                "template",
                                allowTemplateOwner: true))
                        {
                            throw new InvalidOperationException(relationError);
                        }

                        templateRelationSites.Add(resolvedRelation);
                        continue;
                    }

                    if (!registry.TryResolveBindingVariant(member.Value.Binding.Path, member.Value.Binding.ConverterKey, out var definition))
                    {
                        throw new InvalidOperationException($"Binding path '{member.Value.Binding.Path}' has no typed compile-time definition.");
                    }

                    templateBindingSourceType ??= definition.SourceTypeName;
                    templateBindingSites.Add(new(nodeIndex, member, definition));
                }
            }

            writer.Append("    private sealed class TemplateFactory").Append(templateIndex)
                .AppendLine(" : global::Delta.XAML.IUiTemplateFactory");
            writer.AppendLine("    {");
            EmitRelationPlans(writer, templateRelationSites);
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
                if (nodes[nodeIndex].InlineEffect is { } effect)
                {
                    EmitEffectRegistration(
                        writer,
                        nodeIndex,
                        new(nodeIndex, effect, Array.Empty<EffectBindingSite>()),
                        "template" + nodeIndex,
                        "            ");
                }
                EmitTextSpans(writer, nodeIndex, nodes[nodeIndex], "template");
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

            for (var relationIndex = 0; relationIndex < templateRelationSites.Count; relationIndex++)
            {
                var relation = templateRelationSites[relationIndex];
                writer.Append("            var relation").Append(relationIndex)
                    .Append(" = new global::Delta.XAML.UiRelationBinding<RelationPlan").Append(relationIndex).Append(", ")
                    .Append(relation.ValueTypeName).Append(">(template").Append(relation.NodeIndex).Append(", ")
                    .Append(relation.SourceExpression).Append(", global::Delta.XAML.UiBindingMode.")
                    .Append(relation.Member.Value.Binding.Mode).AppendLine(");");
                writer.Append("            template").Append(relation.NodeIndex).Append(".SetBinding(")
                    .Append(Quote(relation.Member.Name)).Append(", relation").Append(relationIndex).AppendLine(");");
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
            if (templates[templateIndex].ItemTypeName is not null)
            {
                continue;
            }

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
            .Append(BindingValueType(site.Member.Value.Binding, site.Definition))
            .Append(">(templateContext, static source => ")
            .Append(BindingReadExpression(site.Member.Value.Binding, site.Definition))
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

    private static bool TryResolveRelationBinding(
        RelationBindingSite site,
        List<XamlObjectPlan> nodes,
        XamlSemanticRegistry registry,
        out ResolvedRelationBindingSite resolved,
        out string error,
        string variablePrefix = "node",
        bool allowTemplateOwner = false)
    {
        resolved = default;
        error = string.Empty;
        var binding = site.Member.Value.Binding;
        if (!registry.TryResolveType(nodes[site.NodeIndex].Type, out var targetType) ||
            !targetType.TryGetProperty(site.Member.Name, out var targetProperty))
        {
            error = $"Relation binding target '{site.Member.Name}' has no typed property definition.";
            return false;
        }

        XamlTypeDefinition? sourceType;
        string sourceExpression;
        var commonSource = false;
        switch (binding.SourceKind)
        {
            case XamlBindingSourceKind.Self:
                sourceType = targetType;
                sourceExpression = "global::Delta.XAML.UiBindingSource.Self";
                break;
            case XamlBindingSourceKind.Name:
                var namedIndex = FindNamedNode(nodes, binding.SourceArgument);
                if (namedIndex < 0 || !registry.TryResolveType(nodes[namedIndex].Type, out sourceType))
                {
                    error = $"Relation binding namescope source '{binding.SourceArgument}' was not found.";
                    return false;
                }

                sourceExpression = $"global::Delta.XAML.UiBindingSource.NamedElement({variablePrefix}{namedIndex})";
                break;
            case XamlBindingSourceKind.Ancestor:
                if (string.IsNullOrWhiteSpace(binding.SourceArgument) ||
                    !registry.TryResolveType(new XamlQualifiedName(string.Empty, binding.SourceArgument), out sourceType))
                {
                    error = $"Relation binding ancestor type '{binding.SourceArgument}' has no generated descriptor.";
                    return false;
                }

                sourceExpression = $"global::Delta.XAML.UiBindingSource.Ancestor({TypeIdExpression(sourceType.Id)})";
                break;
            case XamlBindingSourceKind.TemplateOwner:
                if (!allowTemplateOwner ||
                    !registry.TryResolveType(new XamlQualifiedName(string.Empty, "Panel"), out sourceType))
                {
                    error = "TemplateOwner is valid only inside a compiled Template; use Self, ElementName or Ancestor in the document root.";
                    return false;
                }

                commonSource = true;
                sourceExpression = "global::Delta.XAML.UiBindingSource.TemplateOwner(owner)";
                break;
            default:
                error = $"Relation binding source '{binding.SourceKind}' is not supported in this generated site.";
                return false;
        }

        if (!sourceType.TryGetProperty(binding.Path, out var sourceProperty) ||
            !TryDirectRelationMember(sourceType, sourceProperty, commonSource, out var sourceAccess) ||
            !TryRelationValueType(sourceType, sourceProperty, out var sourceValueType))
        {
            error = $"Relation binding path '{binding.Path}' has no direct typed accessor on '{sourceType.Name.LocalName}'.";
            return false;
        }

        if (!TryRelationValueType(targetType, targetProperty, out var targetValueType))
        {
            error = $"Relation binding target '{site.Member.Name}' has no supported typed value.";
            return false;
        }

        var readExpression = sourceAccess;
        var valueType = sourceValueType;
        if (binding.StringFormat is { } format)
        {
            if (binding.CultureName is not { } culture || targetProperty.ValueKind != XamlValueKind.String)
            {
                error = "A formatted relation binding requires an explicit culture and a string target.";
                return false;
            }

            readExpression = "global::System.String.Format(global::System.Globalization.CultureInfo.GetCultureInfo(" +
                Quote(culture) + "), " + Quote(format) + ", " + sourceAccess + ")";
            valueType = "global::System.String";
        }
        else if (!string.Equals(sourceValueType, targetValueType, StringComparison.Ordinal))
        {
            error = $"Relation binding '{binding.Path}' yields '{sourceValueType}' but target '{site.Member.Name}' requires '{targetValueType}'.";
            return false;
        }

        var writeExpression = binding.Mode == UiBindingMode.TwoWay && binding.StringFormat is null
            ? sourceAccess + " = value"
            : null;
        resolved = new(site.NodeIndex, site.Member, valueType, sourceExpression, readExpression, writeExpression);
        return true;
    }

    private static void EmitRelationPlans(
        StringBuilder writer,
        IReadOnlyList<ResolvedRelationBindingSite> sites)
    {
        for (var i = 0; i < sites.Count; i++)
        {
            var site = sites[i];
            writer.Append("    private readonly struct RelationPlan").Append(i)
                .Append(" : global::Delta.XAML.IUiRelationBindingPlan<RelationPlan").Append(i).Append(", ")
                .Append(site.ValueTypeName).AppendLine(">");
            writer.AppendLine("    {");
            writer.Append("        public static ").Append(site.ValueTypeName)
                .Append(" Read(global::Delta.XAML.UiElement source) => ").Append(site.ReadExpression).AppendLine(";");
            writer.Append("        public static bool TryWrite(global::Delta.XAML.UiElement source, ")
                .Append(site.ValueTypeName).AppendLine(" value)");
            writer.AppendLine("        {");
            if (site.WriteExpression is { } write)
            {
                writer.Append("            ").Append(write).AppendLine(";");
                writer.AppendLine("            return true;");
            }
            else
            {
                writer.AppendLine("            return false;");
            }

            writer.AppendLine("        }");
            writer.AppendLine("    }");
            writer.AppendLine();
        }
    }

    private static bool TryResolveMultiBinding(
        MultiBindingSite site,
        List<XamlObjectPlan> nodes,
        XamlSemanticRegistry registry,
        string? bindingSourceType,
        out ResolvedMultiBindingSite resolved,
        out string error)
    {
        resolved = default;
        error = string.Empty;
        var binding = site.Member.Value.MultiBinding;
        if (!registry.TryResolveType(nodes[site.NodeIndex].Type, out var targetType) ||
            !targetType.TryGetProperty(site.Member.Name, out var targetProperty) ||
            !TryRelationValueType(targetType, targetProperty, out var targetValueType))
        {
            error = $"MultiBinding target '{site.Member.Name}' has no supported typed property.";
            return false;
        }

        var sourceElements = new string[binding.Sources.Length];
        var sourceReads = new string[binding.Sources.Length];
        var sourceTypes = new string[binding.Sources.Length];
        var hasContext = false;
        for (var sourceIndex = 0; sourceIndex < binding.Sources.Length; sourceIndex++)
        {
            var source = binding.Sources[sourceIndex];
            if (source.SourceKind == XamlBindingSourceKind.Context)
            {
                if (bindingSourceType is null ||
                    !registry.TryResolveBinding(bindingSourceType, source.Path, out var contextBinding))
                {
                    error = $"MultiBinding context source '{source.Path}' has no generated typed binding definition.";
                    return false;
                }

                hasContext = true;
                sourceElements[sourceIndex] = "global::Delta.XAML.UiBindingSource.Context";
                sourceReads[sourceIndex] = ReplaceSourceIdentifier(contextBinding.ReadExpression, "context");
                sourceTypes[sourceIndex] = contextBinding.ValueTypeName;
                continue;
            }

            XamlTypeDefinition? sourceType;
            string sourceExpression;
            switch (source.SourceKind)
            {
                case XamlBindingSourceKind.Self:
                    sourceType = targetType;
                    sourceExpression = "global::Delta.XAML.UiBindingSource.Self";
                    break;
                case XamlBindingSourceKind.Name:
                    var namedIndex = FindNamedNode(nodes, source.SourceArgument);
                    if (namedIndex < 0 || !registry.TryResolveType(nodes[namedIndex].Type, out sourceType))
                    {
                        error = $"MultiBinding named source '{source.SourceArgument}' does not resolve in the generated namescope.";
                        return false;
                    }

                    sourceExpression = $"global::Delta.XAML.UiBindingSource.NamedElement(node{namedIndex})";
                    break;
                case XamlBindingSourceKind.Ancestor:
                    if (string.IsNullOrWhiteSpace(source.SourceArgument) ||
                        !registry.TryResolveType(new XamlQualifiedName(string.Empty, source.SourceArgument), out sourceType))
                    {
                        error = $"MultiBinding ancestor type '{source.SourceArgument}' has no generated descriptor.";
                        return false;
                    }

                    sourceExpression = $"global::Delta.XAML.UiBindingSource.Ancestor({TypeIdExpression(sourceType.Id)})";
                    break;
                default:
                    error = $"MultiBinding source '{source.SourceKind}' is not valid in a document artifact.";
                    return false;
            }

            if (!sourceType.TryGetProperty(source.Path, out var sourceProperty) ||
                !TryDirectRelationMember(sourceType, sourceProperty, false, out var sourceAccess) ||
                !TryRelationValueType(sourceType, sourceProperty, out var sourceValueType))
            {
                error = $"MultiBinding source path '{source.Path}' has no direct typed accessor on '{sourceType.Name.LocalName}'.";
                return false;
            }

            sourceElements[sourceIndex] = sourceExpression;
            sourceReads[sourceIndex] = sourceAccess.Replace("source", $"sources[{sourceIndex}]", StringComparison.Ordinal);
            sourceTypes[sourceIndex] = sourceValueType;
        }

        string readExpression;
        string valueType;
        if (binding.FunctionKey is { } functionKey)
        {
            if (!registry.TryResolveBindingFunction(functionKey, out var function) ||
                function.ParameterTypeNames.Length != sourceTypes.Length)
            {
                error = $"MultiBinding function '{functionKey}' has no generated signature matching {sourceTypes.Length} sources.";
                return false;
            }

            for (var i = 0; i < sourceTypes.Length; i++)
            {
                if (!SameTypeName(sourceTypes[i], function.ParameterTypeNames[i]))
                {
                    error = $"MultiBinding function '{functionKey}' parameter {i} expects '{function.ParameterTypeNames[i]}' but source yields '{sourceTypes[i]}'.";
                    return false;
                }
            }

            readExpression = function.MethodExpression + "(" + string.Join(", ", sourceReads) + ")";
            valueType = function.ReturnTypeName;
        }
        else
        {
            if (binding.StringFormat is not { } format || binding.CultureName is not { } culture)
            {
                error = "MultiBinding formatting requires StringFormat and explicit Culture.";
                return false;
            }

            readExpression = "global::System.String.Format(global::System.Globalization.CultureInfo.GetCultureInfo(" +
                Quote(culture) + "), " + Quote(format) + ", " + string.Join(", ", sourceReads) + ")";
            valueType = "global::System.String";
        }

        if (!SameTypeName(valueType, targetValueType))
        {
            error = $"MultiBinding yields '{valueType}' but target '{site.Member.Name}' requires '{targetValueType}'.";
            return false;
        }

        resolved = new(
            site.NodeIndex,
            site.Member,
            valueType,
            sourceElements,
            readExpression,
            hasContext ? bindingSourceType : null);
        return true;
    }

    private static void EmitMultiBindingPlans(
        StringBuilder writer,
        IReadOnlyList<ResolvedMultiBindingSite> sites)
    {
        for (var i = 0; i < sites.Count; i++)
        {
            var site = sites[i];
            writer.Append("    private readonly struct MultiPlan").Append(i).Append(" : global::Delta.XAML.");
            if (site.ContextTypeName is { } contextType)
            {
                writer.Append("IUiContextMultiBindingPlan<MultiPlan").Append(i).Append(", ").Append(contextType).Append(", ")
                    .Append(site.ValueTypeName).AppendLine(">");
            }
            else
            {
                writer.Append("IUiMultiBindingPlan<MultiPlan").Append(i).Append(", ").Append(site.ValueTypeName).AppendLine(">");
            }
            writer.AppendLine("    {");
            writer.Append("        public static ").Append(site.ValueTypeName).Append(" Read(");
            if (site.ContextTypeName is { } planContextType)
            {
                writer.Append(planContextType).Append(" context, ");
            }

            writer.Append("global::System.ReadOnlySpan<global::Delta.XAML.UiElement> sources) => ")
                .Append(site.ReadExpression).AppendLine(";");
            writer.AppendLine("    }");
            writer.AppendLine();
        }
    }

    private static bool TryResolveTrigger(
        XamlTriggerPlan trigger,
        List<XamlObjectPlan> nodes,
        XamlSemanticRegistry registry,
        string? bindingSourceType,
        out ResolvedTriggerSite resolved,
        out string error)
    {
        resolved = default;
        error = string.Empty;
        var targetIndex = FindNamedNode(nodes, trigger.TargetName);
        if (targetIndex < 0 || !registry.TryResolveType(nodes[targetIndex].Type, out var targetType) ||
            !targetType.TryGetProperty(trigger.TargetProperty, out var targetProperty) ||
            !TryTypedPropertyExpression(trigger.TargetProperty, out var targetPropertyExpression))
        {
            error = $"Trigger target '{trigger.TargetName}.{trigger.TargetProperty}' has no generated typed property.";
            return false;
        }

        if (!TryLiteralExpression(
                trigger.TargetProperty,
                XamlValuePlan.FromLiteral(new(targetProperty.ValueKind, trigger.SetValue)),
                out var setValueExpression,
                out error))
        {
            return false;
        }

        var condition = new StringBuilder();
        var dependencies = new List<ResolvedTriggerDependency>(trigger.Sources.Length);
        for (var sourceIndex = 0; sourceIndex < trigger.Sources.Length; sourceIndex++)
        {
            var source = trigger.Sources[sourceIndex];
            if (source.SourceKind == XamlBindingSourceKind.Context)
            {
                if (bindingSourceType is null ||
                    !registry.TryResolveBinding(bindingSourceType, source.Path, out var contextBinding) ||
                    !TryLiteralForType(
                        contextBinding.ValueTypeName,
                        source.Path,
                        trigger.ExpectedValues[sourceIndex],
                        out var contextExpected,
                        out error))
                {
                    error = error.Length == 0
                        ? $"Trigger context source '{source.Path}' has no generated typed binding definition."
                        : error;
                    return false;
                }

                var contextRead = ReplaceSourceIdentifier(contextBinding.ReadExpression, "_context");
                if (sourceIndex != 0)
                {
                    condition.Append(" && ");
                }

                dependencies.Add(new(contextBinding.ValueTypeName, contextRead));
                condition.Append(contextRead).Append(" == ").Append(contextExpected);
                continue;
            }

            var nodeIndex = source.SourceKind switch
            {
                XamlBindingSourceKind.Self => targetIndex,
                XamlBindingSourceKind.Name => FindNamedNode(nodes, source.SourceArgument),
                _ => -1,
            };
            if (nodeIndex < 0 || !registry.TryResolveType(nodes[nodeIndex].Type, out var sourceType) ||
                !sourceType.TryGetProperty(source.Path, out var sourceProperty) ||
                !TryDirectRelationMember(sourceType, sourceProperty, false, out var sourceAccess) ||
                !TryRelationValueType(sourceType, sourceProperty, out var sourceValueType) ||
                !TryLiteralExpression(
                    source.Path,
                    XamlValuePlan.FromLiteral(new(sourceProperty.ValueKind, trigger.ExpectedValues[sourceIndex])),
                    out var expectedExpression,
                    out error))
            {
                error = error.Length == 0
                    ? $"Trigger source '{source.SourceArgument}.{source.Path}' has no generated typed accessor or literal."
                    : error;
                return false;
            }

            if (sourceIndex != 0)
            {
                condition.Append(" && ");
            }

            var readExpression = sourceAccess.Replace("source", ScopeElementExpression(nodes, nodeIndex), StringComparison.Ordinal);
            dependencies.Add(new(sourceValueType, readExpression));
            condition.Append(readExpression)
                .Append(" == ").Append(expectedExpression);
        }

        string? action = null;
        if (trigger.Action is { } actionText)
        {
            if (!Enum.TryParse<Delta.XAML.UiSemanticActionKind>(actionText, false, out var parsedAction) ||
                parsedAction == Delta.XAML.UiSemanticActionKind.None)
            {
                error = $"Trigger action '{actionText}' is not a supported semantic action.";
                return false;
            }

            action = parsedAction.ToString();
        }

        resolved = new(
            ScopeElementExpression(nodes, targetIndex),
            targetPropertyExpression,
            setValueExpression,
            condition.ToString(),
            dependencies.ToArray(),
            action,
            trigger.Argument);
        return true;
    }

    private static bool TryLiteralForType(
        string typeName,
        string propertyName,
        string value,
        out string expression,
        out string error)
    {
        var kind = TrimGlobal(typeName) switch
        {
            "System.String" => XamlValueKind.String,
            "System.Boolean" => XamlValueKind.Boolean,
            "System.Single" => XamlValueKind.Single,
            "System.Double" => XamlValueKind.Double,
            "System.Int32" => XamlValueKind.Integer,
            "Delta.XAML.UiColor" => XamlValueKind.Color,
            "Delta.XAML.UiThickness" => XamlValueKind.Thickness,
            "Delta.XAML.UiCornerRadii" => XamlValueKind.CornerRadii,
            "Delta.XAML.Contract.UiBlendMode" => XamlValueKind.Enum,
            _ => XamlValueKind.Invalid,
        };
        if (kind == XamlValueKind.Invalid)
        {
            expression = string.Empty;
            error = $"Trigger literal type '{typeName}' is not supported.";
            return false;
        }

        return TryLiteralExpression(propertyName, XamlValuePlan.FromLiteral(new(kind, value)), out expression, out error);
    }

    private static int FindNamedNode(List<XamlObjectPlan> nodes, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return -1;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            if (string.Equals(nodes[i].ScopeName, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string ScopeElementExpression(List<XamlObjectPlan> nodes, int nodeIndex)
    {
        var scopeIndex = 0;
        for (var i = 0; i < nodeIndex; i++)
        {
            if (nodes[i].ScopeName is not null)
            {
                scopeIndex++;
            }
        }

        return $"_scopeElements[{scopeIndex}]";
    }

    private static bool TryDirectRelationMember(
        XamlTypeDefinition type,
        XamlPropertyDefinition property,
        bool commonSource,
        out string expression)
    {
        var member = property.MemberName ?? property.Name;
        if (!IsIdentifier(member) || member is "Columns" or "Rows")
        {
            expression = string.Empty;
            return false;
        }

        expression = commonSource
            ? "source." + member
            : "((" + PublicTypeName(type) + ")source)." + member;
        return true;
    }

    private static bool TryRelationValueType(
        XamlTypeDefinition type,
        XamlPropertyDefinition property,
        out string typeName)
    {
        typeName = property.Name switch
        {
            "BackgroundBrush" => "global::Delta.XAML.UiBrush",
            "EffectSet" => "global::Delta.XAML.Contract.UiEffectSet",
            "BlendMode" => "global::Delta.XAML.Contract.UiBlendMode",
            "AutomationRole" => "global::Delta.XAML.UiSemanticRole",
            "Gestures" => "global::Delta.XAML.UiGestureKind",
            "Command" => "global::Delta.XAML.UiCommandId",
            "CommandKey" => "global::Delta.XAML.UiKeyGesture",
            "BorderWidthUnits" => "global::Delta.XAML.Contract.PaintUnits",
            "Margin" => "global::Delta.XAML.UiThickness",
            "BorderThickness" => "global::Delta.XAML.UiThickness",
            "HorizontalAlignment" => "global::Delta.XAML.UiHorizontalAlignment",
            "VerticalAlignment" => "global::Delta.XAML.UiVerticalAlignment",
            "IsFocusScope" => "global::System.Boolean",
            "Stretch" => "global::Delta.XAML.UiImageStretch",
            "HorizontalTextAlignment" => "global::Delta.XAML.UiTextHorizontalAlignment",
            "VerticalTextAlignment" => "global::Delta.XAML.UiTextVerticalAlignment",
            "TextWrapping" => "global::Delta.XAML.UiTextWrapping",
            "TextTrimming" => "global::Delta.XAML.UiTextTrimming",
            "FontWeight" => "global::Delta.XAML.UiFontWeight",
            "FontStyle" => "global::Delta.XAML.UiFontStyle",
            "TextDecorations" => "global::Delta.XAML.UiTextDecorations",
            "MaxLines" or "MaxLength" => "global::System.Int32",
            "LineHeight" => "global::System.Single",
            "PlaceholderText" => "global::System.String",
            "IsReadOnly" or "AcceptsReturn" => "global::System.Boolean",
            _ => string.Empty,
        };
        if (typeName.Length != 0)
        {
            return true;
        }

        var fallbackTypeName = property.ValueKind switch
        {
            XamlValueKind.String when property.Name is "StyleKey" or "TemplateKey" => "global::System.String?",
            XamlValueKind.String => "global::System.String",
            XamlValueKind.Boolean => "global::System.Boolean",
            XamlValueKind.Single => "global::System.Single",
            XamlValueKind.Double => "global::System.Double",
            XamlValueKind.Integer => "global::System.Int32",
            XamlValueKind.ResourceId => "global::Delta.XAML.Contract.UiResourceId",
            XamlValueKind.Brush => "global::Delta.XAML.UiBrush",
            XamlValueKind.Color => "global::Delta.XAML.UiColor",
            XamlValueKind.Thickness => "global::Delta.XAML.UiThickness",
            XamlValueKind.CornerRadii => "global::Delta.XAML.UiCornerRadii",
            XamlValueKind.Enum when property.Name == "Orientation" && type.Name.LocalName is "StackPanel" or "Slider" => "global::Delta.XAML.UiOrientation",
            XamlValueKind.Enum when property.Name == "BlendMode" => "global::Delta.XAML.Contract.UiBlendMode",
            _ => string.Empty,
        };
        if (fallbackTypeName.Length == 0)
        {
            typeName = string.Empty;
            return false;
        }

        typeName = fallbackTypeName;
        return true;
    }

    private static bool TryCreateCollectionSite(
        int nodeIndex,
        XamlObjectPlan node,
        IReadOnlyList<XamlTemplatePlan> templates,
        XamlSemanticRegistry registry,
        out CollectionSite site,
        out string? error)
    {
        site = default;
        error = null;
        XamlMemberPlan? sourceMember = null;
        string? templateKey = null;
        string? selectorKey = null;
        var start = 0;
        var count = -1;
        var itemExtent = 24f;
        var viewportExtent = float.NaN;
        for (var i = 0; i < node.Members.Length; i++)
        {
            var member = node.Members[i];
            switch (member.Name)
            {
                case "ItemsSource": sourceMember = member; break;
                case "ItemTemplate" when member.Value.Kind == XamlValueKind.String:
                    templateKey = member.Value.Literal.CanonicalText;
                    break;
                case "ItemTemplateSelector" when member.Value.Kind == XamlValueKind.String:
                    selectorKey = member.Value.Literal.CanonicalText;
                    break;
                case "VirtualizationStart" when int.TryParse(member.Value.Literal.CanonicalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStart):
                    start = parsedStart;
                    break;
                case "VirtualizationCount" when int.TryParse(member.Value.Literal.CanonicalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount):
                    count = parsedCount;
                    break;
                case "ItemExtent" when float.TryParse(member.Value.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedExtent):
                    itemExtent = parsedExtent;
                    break;
                case "Height" when member.Value.Kind == XamlValueKind.Single &&
                    float.TryParse(member.Value.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedViewport):
                    viewportExtent = parsedViewport;
                    break;
            }
        }

        if (sourceMember is null)
        {
            return false;
        }

        if (node.Name.LocalName is not ("CollectionView" or "Picker"))
        {
            error = "ItemsSource is valid only on CollectionView or Picker in the generated retained dialect.";
            return false;
        }

        var source = sourceMember.Value;
        if (source.Value.Kind != XamlValueKind.ItemsSource ||
            !registry.TryResolveBinding(source.Value.Binding.Path, out var definition) ||
            string.IsNullOrWhiteSpace(definition.CollectionItemTypeName))
        {
            error = $"ItemsSource '{source.Value.Binding.Path}' has no generated IUiItemsSource<TItem> definition.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(templateKey) || !TryFindTemplate(templates, templateKey, out var template) ||
            string.IsNullOrWhiteSpace(template.ItemTypeName))
        {
            error = $"{node.Name.LocalName} requires ItemTemplate naming a Template with x:DataType.";
            return false;
        }

        if (!SameTypeName(definition.CollectionItemTypeName, template.ItemTypeName))
        {
            error = $"ItemTemplate '{template.Key}' expects '{template.ItemTypeName}' but ItemsSource yields '{definition.CollectionItemTypeName}'.";
            return false;
        }

        XamlTemplateSelectorDefinition? selector = null;
        if (selectorKey is not null)
        {
            if (!registry.TryResolveTemplateSelector(selectorKey, out selector))
            {
                error = $"ItemTemplateSelector '{selectorKey}' has no generated static selector definition.";
                return false;
            }

            if (!SameTypeName(definition.CollectionItemTypeName, selector.ItemTypeName))
            {
                error = $"ItemTemplateSelector '{selectorKey}' accepts '{selector.ItemTypeName}' but ItemsSource yields '{definition.CollectionItemTypeName}'.";
                return false;
            }
        }

        if (start < 0 || count == 0 || count < -1 || !float.IsFinite(itemExtent) || itemExtent <= 0)
        {
            error = "VirtualizationStart must be non-negative, VirtualizationCount positive when specified and ItemExtent finite and positive.";
            return false;
        }

        XamlTemplatePlan[] selectedTemplates;
        if (selector is null)
        {
            selectedTemplates = new[] { template };
        }
        else
        {
            selectedTemplates = new XamlTemplatePlan[selector.TemplateKeys.Length];
            var containsDefault = false;
            for (var selectorIndex = 0; selectorIndex < selector.TemplateKeys.Length; selectorIndex++)
            {
                var selectedKey = selector.TemplateKeys[selectorIndex];
                if (!TryFindTemplate(templates, selectedKey, out var selectedTemplate) ||
                    selectedTemplate.ItemTypeName is null ||
                    !SameTypeName(selectedTemplate.ItemTypeName, definition.CollectionItemTypeName))
                {
                    error = $"ItemTemplateSelector '{selector.Key}' references missing or incompatible template '{selectedKey}'.";
                    return false;
                }

                selectedTemplates[selectorIndex] = selectedTemplate;
                containsDefault |= string.Equals(selectedKey, template.Key, StringComparison.Ordinal);
            }

            if (!containsDefault)
            {
                error = $"ItemTemplateSelector '{selector.Key}' must include fallback ItemTemplate '{template.Key}'.";
                return false;
            }
        }
        var hostPath = node.Name.LocalName == "Picker" ? "Items.ItemsHost" : "ItemsHost";
        var viewportPath = node.Name.LocalName == "Picker" ? "Items.Viewport" : "Viewport";
        site = new(nodeIndex, source.Value, definition, template, selector, selectedTemplates, start, count, itemExtent, viewportExtent, hostPath, viewportPath, node.Name.LocalName == "Picker");
        return true;
    }

    private static void EmitItemPlans(
        StringBuilder writer,
        IReadOnlyList<CollectionSite> sites,
        XamlSemanticRegistry registry,
        IReadOnlyList<XamlResourceSlotPlan> resourceSlots)
    {
        for (var siteIndex = 0; siteIndex < sites.Count; siteIndex++)
        {
            var site = sites[siteIndex];
            writer.Append("    private readonly struct ItemPlan").Append(siteIndex)
                .Append(" : global::Delta.XAML.IUiItemTemplatePlan<ItemPlan").Append(siteIndex).Append(", ")
                .Append(site.Definition.CollectionItemTypeName).AppendLine(">");
            writer.AppendLine("    {");
            for (var templateIndex = 0; templateIndex < site.Templates.Length; templateIndex++)
            {
                writer.Append("        private static global::Delta.XAML.UiTemplateId TemplateId").Append(templateIndex).Append(" => ")
                    .Append(TemplateIdExpression(site.Templates[templateIndex].Id)).AppendLine(";");
            }

            writer.Append("        public static global::Delta.XAML.UiTemplateId SelectTemplate(in ")
                .Append(site.Definition.CollectionItemTypeName).AppendLine(" item)");
            writer.AppendLine("        {");
            if (site.Selector is { } selector)
            {
                writer.Append("            return ").Append(selector.SelectExpression).Append('(');
                if (selector.PassByIn)
                {
                    writer.Append("in ");
                }

                writer.AppendLine("item) switch");
                writer.AppendLine("            {");
                for (var templateIndex = 0; templateIndex < site.Templates.Length; templateIndex++)
                {
                    writer.Append("                ").Append(templateIndex).Append(" => TemplateId").Append(templateIndex).AppendLine(",");
                }

                writer.AppendLine("                _ => throw new global::System.InvalidOperationException(\"The generated template selector returned an index outside its declared template range.\"),");
                writer.AppendLine("            };");
            }
            else
            {
                writer.AppendLine("            return TemplateId0;");
            }
            writer.AppendLine("        }");

            writer.Append("        public static global::Delta.XAML.UiElement Create(global::Delta.XAML.UiTemplateId templateId, in ")
                .Append(site.Definition.CollectionItemTypeName)
                .AppendLine(" item, global::Delta.XAML.UiResourceCatalog resources)");
            writer.AppendLine("        {");
            for (var templateIndex = 0; templateIndex < site.Templates.Length; templateIndex++)
            {
                writer.Append("            if (templateId == TemplateId").Append(templateIndex).AppendLine(")");
                writer.AppendLine("            {");
                writer.Append("                return CreateTemplate").Append(templateIndex).AppendLine("(in item, resources);");
                writer.AppendLine("            }");
            }

            writer.AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(templateId));");
            writer.AppendLine("        }");
            writer.Append("        public static void Bind(global::Delta.XAML.UiElement element, global::Delta.XAML.UiTemplateId templateId, in ")
                .Append(site.Definition.CollectionItemTypeName)
                .AppendLine(" item, global::Delta.XAML.UiResourceCatalog resources)");
            writer.AppendLine("        {");
            for (var templateIndex = 0; templateIndex < site.Templates.Length; templateIndex++)
            {
                writer.Append("            if (templateId == TemplateId").Append(templateIndex).AppendLine(")");
                writer.AppendLine("            {");
                writer.Append("                BindTemplate").Append(templateIndex).AppendLine("(element, in item, resources);");
                writer.AppendLine("                return;");
                writer.AppendLine("            }");
            }

            writer.AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(templateId));");
            writer.AppendLine("        }");
            writer.AppendLine("        public static void Unbind(global::Delta.XAML.UiElement element, global::Delta.XAML.UiTemplateId templateId)");
            writer.AppendLine("        {");
            writer.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(element);");
            writer.Append("            if (");
            for (var templateIndex = 0; templateIndex < site.Templates.Length; templateIndex++)
            {
                if (templateIndex != 0)
                {
                    writer.Append(" && ");
                }

                writer.Append("templateId != TemplateId").Append(templateIndex);
            }

            writer.AppendLine(")");
            writer.AppendLine("            {");
            writer.AppendLine("                throw new global::System.ArgumentOutOfRangeException(nameof(templateId));");
            writer.AppendLine("            }");
            writer.AppendLine("        }");

            for (var templateIndex = 0; templateIndex < site.Templates.Length; templateIndex++)
            {
                EmitItemTemplate(writer, site, site.Templates[templateIndex], templateIndex, registry, resourceSlots);
            }

            writer.AppendLine("    }");
            writer.AppendLine();
        }
    }

    private static void EmitItemTemplate(
        StringBuilder writer,
        CollectionSite site,
        XamlTemplatePlan template,
        int templateIndex,
        XamlSemanticRegistry registry,
        IReadOnlyList<XamlResourceSlotPlan> resourceSlots)
    {
        var nodes = new List<XamlObjectPlan>();
        Flatten(template.Root, nodes);
        writer.Append("        private static global::Delta.XAML.UiElement CreateTemplate").Append(templateIndex).Append("(in ")
            .Append(site.Definition.CollectionItemTypeName)
            .AppendLine(" item, global::Delta.XAML.UiResourceCatalog Resources)");
        writer.AppendLine("        {");
        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            if (!registry.TryResolveType(nodes[nodeIndex].Type, out var type) || string.IsNullOrWhiteSpace(type.FactoryExpression))
            {
                throw new InvalidOperationException($"Item template '{template.Key}' contains a type without a generated factory.");
            }

            writer.Append("            var itemNode").Append(nodeIndex).Append(" = ").Append(type.FactoryExpression).AppendLine(";");
            for (var memberIndex = 0; memberIndex < nodes[nodeIndex].Members.Length; memberIndex++)
            {
                if (nodes[nodeIndex].Members[memberIndex].Value.Kind is not (XamlValueKind.Binding or XamlValueKind.MultiBinding))
                {
                    EmitMember(writer, nodeIndex, nodes[nodeIndex].Members[memberIndex], type, "itemNode", resourceSlots);
                }
            }

            if (nodes[nodeIndex].InlineEffect is { } effect)
            {
                EmitEffectRegistration(
                    writer,
                    nodeIndex,
                    new(nodeIndex, effect, Array.Empty<EffectBindingSite>()),
                    "itemNode" + nodeIndex,
                    "            ");
            }

            EmitTextSpans(writer, nodeIndex, nodes[nodeIndex], "itemNode");
        }

        EmitAttachments(writer, nodes, registry, "itemNode");
        writer.Append("            BindTemplate").Append(templateIndex).AppendLine("(itemNode0, in item, Resources);");
        writer.AppendLine("            return itemNode0;");
        writer.AppendLine("        }");
        writer.Append("        private static void BindTemplate").Append(templateIndex).Append("(global::Delta.XAML.UiElement element, in ")
            .Append(site.Definition.CollectionItemTypeName)
            .AppendLine(" item, global::Delta.XAML.UiResourceCatalog Resources)");
        writer.AppendLine("        {");
        EmitItemNodeCasts(writer, nodes, registry);
        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            for (var memberIndex = 0; memberIndex < nodes[nodeIndex].Members.Length; memberIndex++)
            {
                var member = nodes[nodeIndex].Members[memberIndex];
                if (member.Value.Kind == XamlValueKind.MultiBinding)
                {
                    if (!TryItemMultiExpression(member, template, registry, out var multiExpression, out var multiError))
                    {
                        throw new InvalidOperationException(multiError);
                    }

                    writer.Append("            itemNode").Append(nodeIndex).Append('.').Append(member.Name)
                        .Append(" = ").Append(multiExpression).AppendLine(";");
                    continue;
                }

                if (member.Value.Kind != XamlValueKind.Binding)
                {
                    continue;
                }

                if (!(template.ItemTypeName is { } itemType
                        ? registry.TryResolveBindingVariant(itemType, member.Value.Binding.Path, member.Value.Binding.ConverterKey, out var binding)
                        : registry.TryResolveBindingVariant(member.Value.Binding.Path, member.Value.Binding.ConverterKey, out binding)))
                {
                    throw new InvalidOperationException($"Item binding '{member.Value.Binding.Path}' has no typed definition.");
                }

                var expression = ReplaceSourceIdentifier(BindingReadExpression(member.Value.Binding, binding), "item");
                writer.Append("            itemNode").Append(nodeIndex).Append('.').Append(member.Name)
                    .Append(" = ").Append(expression).AppendLine(";");
            }
        }

        writer.AppendLine("        }");
    }

    private static bool TryItemMultiExpression(
        XamlMemberPlan member,
        XamlTemplatePlan template,
        XamlSemanticRegistry registry,
        out string expression,
        out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        if (template.ItemTypeName is not { } itemType)
        {
            error = $"Item MultiBinding target '{member.Name}' requires x:DataType on its Template.";
            return false;
        }

        var binding = member.Value.MultiBinding;
        var reads = new string[binding.Sources.Length];
        var types = new string[binding.Sources.Length];
        for (var i = 0; i < binding.Sources.Length; i++)
        {
            var source = binding.Sources[i];
            if (source.SourceKind != XamlBindingSourceKind.Context ||
                !registry.TryResolveBinding(itemType, source.Path, out var definition))
            {
                error = $"Item MultiBinding source '{source.SourceKind}.{source.Path}' must be a generated typed item-context path.";
                return false;
            }

            reads[i] = ReplaceSourceIdentifier(definition.ReadExpression, "item");
            types[i] = definition.ValueTypeName;
        }

        if (binding.FunctionKey is { } functionKey)
        {
            if (!registry.TryResolveBindingFunction(functionKey, out var function) ||
                function.ParameterTypeNames.Length != types.Length)
            {
                error = $"Item MultiBinding function '{functionKey}' has no generated signature matching {types.Length} sources.";
                return false;
            }

            for (var i = 0; i < types.Length; i++)
            {
                if (!SameTypeName(types[i], function.ParameterTypeNames[i]))
                {
                    error = $"Item MultiBinding function '{functionKey}' parameter {i} expects '{function.ParameterTypeNames[i]}' but source yields '{types[i]}'.";
                    return false;
                }
            }

            expression = function.MethodExpression + "(" + string.Join(", ", reads) + ")";
            return true;
        }

        if (binding.StringFormat is not { } format || binding.CultureName is not { } culture)
        {
            error = "Item MultiBinding without a function requires StringFormat and explicit Culture.";
            return false;
        }

        expression = "global::System.String.Format(global::System.Globalization.CultureInfo.GetCultureInfo(" +
            Quote(culture) + "), " + Quote(format) + ", " + string.Join(", ", reads) + ")";
        return true;
    }

    private static void EmitAttachments(
        StringBuilder writer,
        List<XamlObjectPlan> nodes,
        XamlSemanticRegistry registry,
        string prefix)
    {
        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            if (!registry.TryResolveType(nodes[nodeIndex].Type, out var type))
            {
                throw new InvalidOperationException($"No generated type exists for '{nodes[nodeIndex].Name.LocalName}'.");
            }

            for (var childIndex = 0; childIndex < nodes[nodeIndex].Children.Length; childIndex++)
            {
                var child = FindNode(nodes, nodes[nodeIndex].Children[childIndex]);
                if (child < 0)
                {
                    throw new InvalidOperationException("An item template child is missing from the flattened tree.");
                }

                if (!TryEmitAttachment(writer, nodeIndex, child, type, out var error, prefix))
                {
                    throw new InvalidOperationException(error);
                }
            }
        }
    }

    private static void EmitItemNodeCasts(StringBuilder writer, List<XamlObjectPlan> nodes, XamlSemanticRegistry registry)
    {
        if (!registry.TryResolveType(nodes[0].Type, out var rootType))
        {
            throw new InvalidOperationException("The item template root type is unavailable.");
        }

        writer.Append("            var itemNode0 = (").Append(PublicTypeName(rootType)).AppendLine(")element;");
        for (var nodeIndex = 1; nodeIndex < nodes.Count; nodeIndex++)
        {
            if (!TryFindParent(nodes, nodeIndex, out var parent, out var childIndex) ||
                !registry.TryResolveType(nodes[nodeIndex].Type, out var type))
            {
                throw new InvalidOperationException("The item template child relation is invalid.");
            }

            writer.Append("            var itemNode").Append(nodeIndex).Append(" = (").Append(PublicTypeName(type))
                .Append(")itemNode").Append(parent).Append(".Children[").Append(childIndex).AppendLine("];");
        }
    }

    private static bool TryFindParent(List<XamlObjectPlan> nodes, int childPosition, out int parent, out int childIndex)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = 0; j < nodes[i].Children.Length; j++)
            {
                if (ReferenceEquals(nodes[i].Children[j], nodes[childPosition]))
                {
                    parent = i;
                    childIndex = j;
                    return true;
                }
            }
        }

        parent = -1;
        childIndex = -1;
        return false;
    }

    private static string PublicTypeName(XamlTypeDefinition type)
    {
        if (type.Name.Namespace.Length == 0)
        {
            return type.Name.LocalName switch
            {
                "TextBlock" => "global::Delta.XAML.UiTextBlock",
                "TextBox" => "global::Delta.XAML.UiTextBox",
                _ => "global::Delta.XAML.Ui" + type.Name.LocalName,
            };
        }

        return type.FactoryExpression switch
        {
            { } factory when factory.StartsWith("new ", StringComparison.Ordinal) && factory.EndsWith("()", StringComparison.Ordinal) => factory[4..^2],
            { } factory => throw new InvalidOperationException($"Factory '{factory}' cannot provide a direct public type name."),
            _ => throw new InvalidOperationException("A generated public type requires a factory."),
        };
    }

    private static bool IsCollectionMetadata(string name) =>
        name is "ItemsSource" or "ItemTemplate" or "ItemTemplateSelector" or "VirtualizationStart" or "VirtualizationCount" or "ItemExtent";

    private static bool SameTypeName(string first, string second) =>
        string.Equals(TrimGlobal(first), TrimGlobal(second), StringComparison.Ordinal);

    private static string TrimGlobal(string value)
    {
        var normalized = value.StartsWith("global::", StringComparison.Ordinal) ? value[8..] : value;
        return normalized switch
        {
            "string" => "System.String",
            "bool" => "System.Boolean",
            "float" => "System.Single",
            "double" => "System.Double",
            "int" => "System.Int32",
            _ => normalized,
        };
    }

    private static string ReplaceSourceIdentifier(string expression, string replacement) =>
        expression.Replace("source.", replacement + ".", StringComparison.Ordinal);

    private static bool TryEmitAttachment(
        StringBuilder writer,
        int parentIndex,
        int childIndex,
        XamlTypeDefinition parentType,
        out string error,
        string variablePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variablePrefix);
        var invocation = parentType.ContentKind switch
        {
            XamlContentKind.Children when parentType.ChildAttachmentMember is { } childMember =>
                $"{variablePrefix}{parentIndex}.{childMember}({variablePrefix}{childIndex});",
            XamlContentKind.Children when parentType.ChildAttachmentExpression is { } childAttachment =>
                $"{childAttachment}({variablePrefix}{parentIndex}, {variablePrefix}{childIndex});",
            XamlContentKind.Children when IsBuiltInChildrenOwner(parentType.Name.LocalName) =>
                $"{variablePrefix}{parentIndex}.Add({variablePrefix}{childIndex});",
            XamlContentKind.SingleContent when parentType.ContentAttachmentMember is { } contentMember =>
                $"{variablePrefix}{parentIndex}.{contentMember}({variablePrefix}{childIndex});",
            XamlContentKind.SingleContent when parentType.ContentAttachmentExpression is { } contentAttachment =>
                $"{contentAttachment}({variablePrefix}{parentIndex}, {variablePrefix}{childIndex});",
            XamlContentKind.SingleContent when string.Equals(parentType.Name.LocalName, "Border", StringComparison.Ordinal) =>
                $"{variablePrefix}{parentIndex}.SetChild({variablePrefix}{childIndex});",
            XamlContentKind.SingleContent when IsBuiltInContentOwner(parentType.Name.LocalName) =>
                $"{variablePrefix}{parentIndex}.SetContent({variablePrefix}{childIndex});",
            _ => null,
        };

        if (invocation is null)
        {
            error = parentType.ContentKind switch
            {
                XamlContentKind.Children => $"Type '{parentType.Name.LocalName}' declares children content but has no generated child attachment thunk.",
                XamlContentKind.SingleContent => $"Type '{parentType.Name.LocalName}' declares single content but has no generated content attachment thunk.",
                _ => $"Type '{parentType.Name.LocalName}' does not accept generated child/content attachment.",
            };
            return false;
        }

        writer.Append("        ").Append(invocation).AppendLine();
        error = string.Empty;
        return true;
    }

    private static void EmitTextSpans(StringBuilder writer, int nodeIndex, XamlObjectPlan node, string variablePrefix)
    {
        if (node.TextSpans.IsDefaultOrEmpty)
        {
            return;
        }

        writer.Append("        ").Append(variablePrefix).Append(nodeIndex).AppendLine(".Spans = new global::Delta.XAML.UiTextSpan[]");
        writer.AppendLine("        {");
        for (var i = 0; i < node.TextSpans.Length; i++)
        {
            var span = node.TextSpans[i];
            if (!TryColor(span.Color, out var color, out var error))
            {
                throw new InvalidOperationException(error);
            }

            writer.Append("            new global::Delta.XAML.UiTextSpan(")
                .Append(Quote(span.Text)).Append(", ")
                .Append(Quote(span.FontKey)).Append(", ")
                .Append(span.FontSize.ToString("R", CultureInfo.InvariantCulture)).Append("f, ")
                .Append(color).Append(", ");
            if (span.Command == Guid.Empty)
            {
                writer.Append("default");
            }
            else
            {
                writer.Append("new global::Delta.XAML.UiCommandId(new global::System.Guid(")
                    .Append(Quote(span.Command.ToString("D"))).Append("))");
            }

            writer.Append(", ").Append(span.Argument is null ? "null" : Quote(span.Argument)).Append(", ")
                .Append("global::Delta.XAML.UiTextDecorations.").Append(span.Decorations).AppendLine("),");
        }

        writer.AppendLine("        };");
    }

    private static void EmitImplicitVisualEffect(
        StringBuilder writer,
        XamlObjectPlan node,
        int nodeIndex,
        string variablePrefix,
        SourceId source,
        IReadOnlyList<XamlScalarResourcePlan> scalarResources,
        ref int effectIndex)
    {
        if (node.InlineEffect is not null || HasMember(node, "EffectSet"))
        {
            return;
        }

        var hasSideWidths = TryGetLiteralMember(node, "BorderThickness", out var sideWidthsMember) &&
            sideWidthsMember.Kind == XamlValueKind.Thickness;
        var width = 0f;
        var hasUniformWidth = TryGetLiteralMember(node, "BorderWidth", out var widthMember) &&
            widthMember.Kind == XamlValueKind.Single &&
            float.TryParse(widthMember.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out width) &&
            float.IsFinite(width) && width > 0;
        if (!hasSideWidths && !hasUniformWidth)
        {
            return;
        }

        var sideWidthsExpression = string.Empty;
        if (hasSideWidths && !TryThicknessVector(sideWidthsMember.Literal.CanonicalText, out sideWidthsExpression, out _))
        {
            return;
        }

        var color = "new global::Delta.float4(0, 0, 0, 0)";
        if (TryGetColorExpression(node, scalarResources, out var colorExpression))
        {
            color = colorExpression;
        }

        var units = "global::Delta.XAML.Contract.PaintUnits.Logical";
        if (TryGetLiteralMember(node, "BorderWidthUnits", out var unitsMember))
        {
            if (unitsMember.Kind != XamlValueKind.Enum ||
                !Enum.TryParse<PaintUnits>(unitsMember.Literal.CanonicalText, false, out var parsedUnits) ||
                parsedUnits is not (PaintUnits.Logical or PaintUnits.Device))
            {
                return;
            }

            units = "global::Delta.XAML.Contract.PaintUnits." + parsedUnits;
        }

        var resource = XamlImplicitEffectIdentity.Create(source, node);
        var variable = "implicitEffect" + effectIndex.ToString(CultureInfo.InvariantCulture);
        effectIndex++;
        writer.Append("        var ").Append(variable)
            .Append(" = global::Delta.XAML.Contract.UiEffectResource.CreateVisualStroke(")
            .Append(ResourceIdExpression(resource)).Append(", ")
            .Append(color).Append(", ")
            .Append(hasSideWidths
                ? sideWidthsExpression
                : width.ToString("R", CultureInfo.InvariantCulture) + "f").Append(", ")
            .Append(units).AppendLine(");");
        writer.Append("        Resources.Set(").Append(variable).AppendLine(");");
        writer.Append("        ").Append(variablePrefix).Append(nodeIndex).Append(".EffectSet = ")
            .Append(variable).AppendLine(".Set;");
    }

    private static void EmitImplicitTextEffect(
        StringBuilder writer,
        XamlObjectPlan node,
        int nodeIndex,
        string variablePrefix,
        SourceId source,
        ref int effectIndex)
    {
        if (node.InlineEffect is not null || HasMember(node, "EffectSet") || HasMember(node, "BorderWidth") || HasMember(node, "BorderThickness") ||
            !TryGetLiteralMember(node, "StrokeWidth", out var widthMember) ||
            widthMember.Kind != XamlValueKind.Single ||
            !float.TryParse(widthMember.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !float.IsFinite(width) || width <= 0)
        {
            return;
        }

        var color = "new global::Delta.float4(0, 0, 0, 0)";
        if (TryGetLiteralMember(node, "StrokeColor", out var colorMember))
        {
            if (colorMember.Kind != XamlValueKind.Color ||
                !TryColorVector(colorMember.Literal.CanonicalText, out color))
            {
                return;
            }
        }

        var resource = XamlImplicitEffectIdentity.Create(source, node, "text");
        var variable = "implicitEffect" + effectIndex.ToString(CultureInfo.InvariantCulture);
        effectIndex++;
        writer.Append("        var ").Append(variable)
            .Append(" = global::Delta.XAML.Contract.UiEffectResource.CreateTextStroke(")
            .Append(ResourceIdExpression(resource)).Append(", ")
            .Append(color).Append(", ")
            .Append(width.ToString("R", CultureInfo.InvariantCulture)).AppendLine("f);");
        writer.Append("        Resources.Set(").Append(variable).AppendLine(");");
        writer.Append("        ").Append(variablePrefix).Append(nodeIndex).Append(".EffectSet = ")
            .Append(variable).AppendLine(".Set;");
    }

    private static bool HasMember(XamlObjectPlan node, string name)
    {
        for (var i = 0; i < node.Members.Length; i++)
        {
            if (string.Equals(node.Members[i].Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetLiteralMember(XamlObjectPlan node, string name, out XamlValuePlan value)
    {
        for (var i = 0; i < node.Members.Length; i++)
        {
            var member = node.Members[i];
            if (string.Equals(member.Name, name, StringComparison.Ordinal))
            {
                value = member.Value;
                return value.Kind is not (XamlValueKind.Binding or XamlValueKind.MultiBinding or XamlValueKind.ResourceReference);
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetColorExpression(
        XamlObjectPlan node,
        IReadOnlyList<XamlScalarResourcePlan> scalarResources,
        out string expression)
    {
        expression = string.Empty;
        for (var i = 0; i < node.Members.Length; i++)
        {
            var member = node.Members[i];
            if (!string.Equals(member.Name, "BorderColor", StringComparison.Ordinal))
            {
                continue;
            }

            if (member.Value.Kind == XamlValueKind.Color)
            {
                return TryColorVector(member.Value.Literal.CanonicalText, out expression);
            }

            if (member.Value.Kind != XamlValueKind.ResourceReference || member.Value.Resource.IsDynamic)
            {
                return false;
            }

            for (var resourceIndex = 0; resourceIndex < scalarResources.Count; resourceIndex++)
            {
                var resource = scalarResources[resourceIndex];
                if (resource.Id == member.Value.Resource.Id && resource.Value.Kind == XamlValueKind.Color)
                {
                    return TryColorVector(resource.Value.Literal.CanonicalText, out expression);
                }
            }

            return false;
        }

        return false;
    }

    private static bool TryColorVector(string value, out string expression)
    {
        expression = string.Empty;
        if (value.Length is not (7 or 9) || value[0] != '#' ||
            !byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        var alpha = value.Length == 9 && byte.TryParse(
            value.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedAlpha)
            ? parsedAlpha
            : (byte)255;
        expression = string.Concat(
            "new global::Delta.float4(",
            (red / 255f).ToString("R", CultureInfo.InvariantCulture), "f, ",
            (green / 255f).ToString("R", CultureInfo.InvariantCulture), "f, ",
            (blue / 255f).ToString("R", CultureInfo.InvariantCulture), "f, ",
            (alpha / 255f).ToString("R", CultureInfo.InvariantCulture), "f)");
        return true;
    }

    private static bool IsBuiltInChildrenOwner(string name) => name is "Panel" or "StackPanel" or "Grid" or "ItemsControl" or "Overlay";

    private static bool IsBuiltInContentOwner(string name) => name is "ContentControl" or "Button" or "ToggleButton" or "ScrollViewer";

    private static bool TryLiteralExpression(
        string propertyName,
        XamlValuePlan value,
        out string expression,
        out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        if (value.Kind is not (XamlValueKind.Invalid or XamlValueKind.String or XamlValueKind.Boolean or
            XamlValueKind.Single or XamlValueKind.Double or XamlValueKind.Color or XamlValueKind.Integer or
            XamlValueKind.ResourceId or XamlValueKind.Thickness or XamlValueKind.CornerRadii or
            XamlValueKind.GridLengthList or XamlValueKind.Enum or XamlValueKind.Brush))
        {
            error = $"Property '{propertyName}' is not a literal value in this compile slice.";
            return false;
        }

        var literal = value.Literal.CanonicalText;
        if (propertyName == "Command" && Guid.TryParse(literal, out var command) && command != Guid.Empty)
        {
            expression = "new global::Delta.XAML.UiCommandId(new global::System.Guid(" + Quote(command.ToString("D")) + "))";
            return true;
        }

        if (propertyName == "CommandKey" && TryKeyGesture(literal, out expression, out error))
        {
            return true;
        }

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
            case XamlValueKind.Integer when int.TryParse(literal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer):
                expression = integer.ToString(CultureInfo.InvariantCulture);
                return true;
            case XamlValueKind.ResourceId when Guid.TryParse(literal, out var resource) && resource != Guid.Empty:
                expression = "new global::Delta.XAML.Contract.UiResourceId(new global::System.Guid(" + Quote(resource.ToString("D")) + "))";
                return true;
            case XamlValueKind.Brush:
                return TryBrush(literal, out expression, out error);
            case XamlValueKind.Color:
                return TryColor(literal, out expression, out error);
            case XamlValueKind.Thickness:
                return TryThickness(literal, out expression, out error);
            case XamlValueKind.CornerRadii:
                return TryCornerRadii(literal, out expression, out error);
            case XamlValueKind.GridLengthList:
                return TryGridLengths(literal, out expression, out error);
            case XamlValueKind.Enum when propertyName == "Orientation" && (literal == "Horizontal" || literal == "Vertical"):
                expression = "global::Delta.XAML.UiOrientation." + literal;
                return true;
            case XamlValueKind.Enum when propertyName == "BorderWidthUnits" &&
                Enum.TryParse<Delta.XAML.Contract.PaintUnits>(literal, false, out var borderWidthUnits) &&
                borderWidthUnits is Delta.XAML.Contract.PaintUnits.Logical or Delta.XAML.Contract.PaintUnits.Device:
                expression = "global::Delta.XAML.Contract.PaintUnits." + borderWidthUnits;
                return true;
            case XamlValueKind.Enum when propertyName == "BlendMode" &&
                Enum.TryParse<Delta.XAML.Contract.UiBlendMode>(literal, false, out var blendMode) &&
                blendMode is Delta.XAML.Contract.UiBlendMode.Opaque or Delta.XAML.Contract.UiBlendMode.Alpha or
                Delta.XAML.Contract.UiBlendMode.PremultipliedAlpha or Delta.XAML.Contract.UiBlendMode.Additive or
                Delta.XAML.Contract.UiBlendMode.Multiply:
                expression = "global::Delta.XAML.Contract.UiBlendMode." + blendMode;
                return true;
            case XamlValueKind.Enum when propertyName == "HorizontalAlignment" &&
                Enum.TryParse<Delta.XAML.UiHorizontalAlignment>(literal, false, out var elementHorizontalAlignment) &&
                elementHorizontalAlignment != Delta.XAML.UiHorizontalAlignment.Unknown:
                expression = "global::Delta.XAML.UiHorizontalAlignment." + elementHorizontalAlignment;
                return true;
            case XamlValueKind.Enum when propertyName == "VerticalAlignment" &&
                Enum.TryParse<Delta.XAML.UiVerticalAlignment>(literal, false, out var elementVerticalAlignment) &&
                elementVerticalAlignment != Delta.XAML.UiVerticalAlignment.Unknown:
                expression = "global::Delta.XAML.UiVerticalAlignment." + elementVerticalAlignment;
                return true;
            case XamlValueKind.Enum when propertyName == "HorizontalTextAlignment" && Enum.TryParse<Delta.XAML.UiTextHorizontalAlignment>(literal, false, out var horizontalAlignment) && horizontalAlignment != Delta.XAML.UiTextHorizontalAlignment.Unknown:
                expression = "global::Delta.XAML.UiTextHorizontalAlignment." + horizontalAlignment;
                return true;
            case XamlValueKind.Enum when propertyName == "VerticalTextAlignment" && Enum.TryParse<Delta.XAML.UiTextVerticalAlignment>(literal, false, out var verticalAlignment) && verticalAlignment != Delta.XAML.UiTextVerticalAlignment.Unknown:
                expression = "global::Delta.XAML.UiTextVerticalAlignment." + verticalAlignment;
                return true;
            case XamlValueKind.Enum when propertyName == "TextWrapping" && Enum.TryParse<Delta.XAML.UiTextWrapping>(literal, false, out var wrapping) && wrapping != Delta.XAML.UiTextWrapping.Unknown:
                expression = "global::Delta.XAML.UiTextWrapping." + wrapping;
                return true;
            case XamlValueKind.Enum when propertyName == "TextTrimming" && Enum.TryParse<Delta.XAML.UiTextTrimming>(literal, false, out var trimming) && trimming != Delta.XAML.UiTextTrimming.Unknown:
                expression = "global::Delta.XAML.UiTextTrimming." + trimming;
                return true;
            case XamlValueKind.Enum when propertyName == "FontWeight" && Enum.TryParse<Delta.XAML.UiFontWeight>(literal, false, out var weight) && weight != Delta.XAML.UiFontWeight.Unknown:
                expression = "global::Delta.XAML.UiFontWeight." + weight;
                return true;
            case XamlValueKind.Enum when propertyName == "FontStyle" && Enum.TryParse<Delta.XAML.UiFontStyle>(literal, false, out var style) && style != Delta.XAML.UiFontStyle.Unknown:
                expression = "global::Delta.XAML.UiFontStyle." + style;
                return true;
            case XamlValueKind.Enum when propertyName == "TextDecorations" && TryTextDecorations(literal, out expression):
                return true;
            case XamlValueKind.Enum when propertyName == "AutomationRole" &&
                Enum.TryParse<Delta.XAML.UiSemanticRole>(literal, false, out var role):
                expression = "global::Delta.XAML.UiSemanticRole." + role;
                return true;
            case XamlValueKind.Enum when propertyName == "Gestures" && TryGestureFlags(literal, out expression):
                return true;
            case XamlValueKind.Enum when propertyName == "Stretch" &&
                Enum.TryParse<Delta.XAML.UiImageStretch>(literal, false, out var stretch):
                expression = "global::Delta.XAML.UiImageStretch." + stretch;
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
            "Margin" => "global::Delta.XAML.UiElementProperties.Margin",
            "HorizontalAlignment" => "global::Delta.XAML.UiElementProperties.HorizontalAlignment",
            "VerticalAlignment" => "global::Delta.XAML.UiElementProperties.VerticalAlignment",
            "Background" => "global::Delta.XAML.UiElementProperties.Background",
            "Padding" => "global::Delta.XAML.UiElementProperties.Padding",
            "IsEnabled" => "global::Delta.XAML.UiElementProperties.IsEnabled",
            "IsSelected" => "global::Delta.XAML.UiElementProperties.IsSelected",
            "StyleKey" => "global::Delta.XAML.UiElementProperties.StyleKey",
            "Variant" => "global::Delta.XAML.UiElementProperties.Variant",
            "TemplateKey" => "global::Delta.XAML.UiElementProperties.TemplateKey",
            "BackgroundBrush" => "global::Delta.XAML.UiElementProperties.BackgroundBrush",
            "EffectSet" => "global::Delta.XAML.UiElementProperties.EffectSet",
            "BlendMode" => "global::Delta.XAML.UiElementProperties.BlendMode",
            "BorderColor" => "global::Delta.XAML.UiElementProperties.BorderColor",
            "BorderWidth" => "global::Delta.XAML.UiElementProperties.BorderWidth",
            "BorderThickness" => "global::Delta.XAML.UiElementProperties.BorderThickness",
            "BorderWidthUnits" => "global::Delta.XAML.UiElementProperties.BorderWidthUnits",
            "CornerRadius" => "global::Delta.XAML.UiElementProperties.CornerRadius",
            "AutomationName" => "global::Delta.XAML.UiElementProperties.AutomationName",
            "AutomationRole" => "global::Delta.XAML.UiElementProperties.AutomationRole",
            "Gestures" => "global::Delta.XAML.UiElementProperties.Gestures",
            "Command" => "global::Delta.XAML.UiElementProperties.Command",
            "CommandKey" => "global::Delta.XAML.UiElementProperties.CommandKey",
            "IsFocusScope" => "global::Delta.XAML.UiElementProperties.IsFocusScope",
            "Text" => "global::Delta.XAML.TextBlockProperties.Text",
            "FontKey" => "global::Delta.XAML.TextBlockProperties.FontKey",
            "FontSize" => "global::Delta.XAML.TextBlockProperties.FontSize",
            "Foreground" => "global::Delta.XAML.TextBlockProperties.Foreground",
            "StrokeColor" => "global::Delta.XAML.TextBlockProperties.StrokeColor",
            "StrokeWidth" => "global::Delta.XAML.TextBlockProperties.StrokeWidth",
            "HorizontalTextAlignment" => "global::Delta.XAML.TextBlockProperties.HorizontalTextAlignment",
            "VerticalTextAlignment" => "global::Delta.XAML.TextBlockProperties.VerticalTextAlignment",
            "TextWrapping" => "global::Delta.XAML.TextBlockProperties.TextWrapping",
            "TextTrimming" => "global::Delta.XAML.TextBlockProperties.TextTrimming",
            "MaxLines" => "global::Delta.XAML.TextBlockProperties.MaxLines",
            "LineHeight" => "global::Delta.XAML.TextBlockProperties.LineHeight",
            "FontWeight" => "global::Delta.XAML.TextBlockProperties.FontWeight",
            "FontStyle" => "global::Delta.XAML.TextBlockProperties.FontStyle",
            "TextDecorations" => "global::Delta.XAML.TextBlockProperties.TextDecorations",
            "PlaceholderText" => "global::Delta.XAML.TextBoxProperties.PlaceholderText",
            "IsReadOnly" => "global::Delta.XAML.TextBoxProperties.IsReadOnly",
            "AcceptsReturn" => "global::Delta.XAML.TextBoxProperties.AcceptsReturn",
            "MaxLength" => "global::Delta.XAML.TextBoxProperties.MaxLength",
            "Value" => "global::Delta.XAML.UiNumericEditorProperties.Value",
            "Minimum" => "global::Delta.XAML.UiNumericEditorProperties.Minimum",
            "Maximum" => "global::Delta.XAML.UiNumericEditorProperties.Maximum",
            "Step" => "global::Delta.XAML.UiSliderProperties.Step",
            "Orientation" => "global::Delta.XAML.UiStackPanelProperties.Orientation",
            "Columns" => "global::Delta.XAML.UiGridProperties.Columns",
            "Rows" => "global::Delta.XAML.UiGridProperties.Rows",
            "Source" => "global::Delta.XAML.UiImageProperties.Source",
            "Tint" => "global::Delta.XAML.UiImageProperties.Tint",
            "Stretch" => "global::Delta.XAML.UiImageProperties.Stretch",
            "Placeholder" => "global::Delta.XAML.UiImageProperties.Placeholder",
            "ErrorSource" => "global::Delta.XAML.UiImageProperties.ErrorSource",
            "SelectedIndex" => "global::Delta.XAML.UiSelectorProperties.SelectedIndex",
            "IsOpen" => "global::Delta.XAML.UiOverlayProperties.IsOpen",
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
        string? variant,
        [NotNullWhen(true)] out XamlStylePlan? style)
    {
        for (var i = 0; i < styles.Count; i++)
        {
            if (string.Equals(styles[i].Key, key, StringComparison.Ordinal) &&
                string.Equals(styles[i].Variant, variant, StringComparison.Ordinal))
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

    private static bool TryBrush(string value, out string expression, out string error)
    {
        if (value.StartsWith('#'))
        {
            if (!TryColor(value, out var color, out error))
            {
                expression = string.Empty;
                return false;
            }

            expression = "global::Delta.XAML.UiBrush.Solid(" + color + ")";
            return true;
        }

        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || !Guid.TryParse(value[(separator + 1)..], out var resource) || resource == Guid.Empty)
        {
            expression = string.Empty;
            error = $"Brush literal '{value}' is invalid.";
            return false;
        }

        var factory = value[..separator] switch
        {
            "LinearGradient" => "LinearGradient",
            "RadialGradient" => "RadialGradient",
            "Image" => "Image",
            _ => string.Empty,
        };
        if (factory.Length == 0)
        {
            expression = string.Empty;
            error = $"Brush literal '{value}' has an unsupported kind.";
            return false;
        }

        expression = "global::Delta.XAML.UiBrush." + factory +
            "(new global::Delta.XAML.Contract.UiResourceId(new global::System.Guid(" +
            Quote(resource.ToString("D")) + ")))";
        error = string.Empty;
        return true;
    }

    private static bool TryGestureFlags(string value, out string expression)
    {
        expression = string.Empty;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (!Enum.TryParse<Delta.XAML.UiGestureKind>(parts[i], false, out var gesture) || gesture == Delta.XAML.UiGestureKind.None)
            {
                return false;
            }

            if (i != 0)
            {
                builder.Append(" | ");
            }

            builder.Append("global::Delta.XAML.UiGestureKind.").Append(gesture);
        }

        expression = builder.ToString();
        return true;
    }

    private static bool TryTextDecorations(string value, out string expression)
    {
        expression = string.Empty;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (!Enum.TryParse<Delta.XAML.UiTextDecorations>(parts[i], false, out var decoration) || decoration == Delta.XAML.UiTextDecorations.None)
            {
                return false;
            }

            if (i != 0)
            {
                builder.Append(" | ");
            }

            builder.Append("global::Delta.XAML.UiTextDecorations.").Append(decoration);
        }

        expression = builder.ToString();
        return true;
    }

    private static bool TryKeyGesture(string value, out string expression, out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !uint.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var physicalKey))
        {
            error = $"Key gesture '{value}' must start with a physical key number.";
            return false;
        }

        var modifiers = new StringBuilder("0UL");
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i] is not ("Shift" or "Control" or "Alt" or "Super" or "CapsLock" or "NumLock"))
            {
                error = $"Key gesture modifier '{parts[i]}' is not supported.";
                return false;
            }

            modifiers.Append(" | global::Delta.XAML.Contract.UiModifierBits.").Append(parts[i]);
        }

        expression = "new global::Delta.XAML.UiKeyGesture(new global::Delta.XAML.Contract.UiPhysicalKey(" +
            physicalKey.ToString(CultureInfo.InvariantCulture) + "), new global::Delta.XAML.Contract.UiModifierState(" +
            modifiers + "))";
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
        if (!ThicknessLiteralParser.TryParse(value, out var thickness))
        {
            error = $"Thickness literal '{value}' must contain one, two or four finite values.";
            return false;
        }

        expression = $"new global::Delta.XAML.UiThickness({thickness.Left.ToString("R", CultureInfo.InvariantCulture)}f, {thickness.Top.ToString("R", CultureInfo.InvariantCulture)}f, {thickness.Right.ToString("R", CultureInfo.InvariantCulture)}f, {thickness.Bottom.ToString("R", CultureInfo.InvariantCulture)}f)";
        return true;
    }

    private static bool TryThicknessVector(string value, out string expression, out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        if (!ThicknessLiteralParser.TryParse(value, out var thickness) ||
            thickness.Left < 0 || thickness.Top < 0 || thickness.Right < 0 || thickness.Bottom < 0)
        {
            error = $"Border thickness literal '{value}' must contain finite non-negative values.";
            return false;
        }

        expression = $"new global::Delta.float4({thickness.Left.ToString("R", CultureInfo.InvariantCulture)}f, {thickness.Top.ToString("R", CultureInfo.InvariantCulture)}f, {thickness.Right.ToString("R", CultureInfo.InvariantCulture)}f, {thickness.Bottom.ToString("R", CultureInfo.InvariantCulture)}f)";
        return true;
    }

    private static bool TryCornerRadii(string value, out string expression, out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (1 or 4))
        {
            error = $"Corner radius literal '{value}' must contain one or four values.";
            return false;
        }

        var values = new float[parts.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                !float.IsFinite(values[i]) || values[i] < 0)
            {
                error = $"Corner radius literal '{value}' is invalid.";
                return false;
            }
        }

        expression = values.Length == 1
            ? $"global::Delta.XAML.UiCornerRadii.Uniform({values[0].ToString("R", CultureInfo.InvariantCulture)}f)"
            : $"new global::Delta.XAML.UiCornerRadii({values[0].ToString("R", CultureInfo.InvariantCulture)}f, {values[1].ToString("R", CultureInfo.InvariantCulture)}f, {values[2].ToString("R", CultureInfo.InvariantCulture)}f, {values[3].ToString("R", CultureInfo.InvariantCulture)}f)";
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

    private readonly record struct RelationBindingSite(
        int NodeIndex,
        XamlMemberPlan Member);

    private readonly record struct ResolvedRelationBindingSite(
        int NodeIndex,
        XamlMemberPlan Member,
        string ValueTypeName,
        string SourceExpression,
        string ReadExpression,
        string? WriteExpression);

    private readonly record struct MultiBindingSite(
        int NodeIndex,
        XamlMemberPlan Member);

    private readonly record struct ResolvedMultiBindingSite(
        int NodeIndex,
        XamlMemberPlan Member,
        string ValueTypeName,
        string[] SourceExpressions,
        string ReadExpression,
        string? ContextTypeName);

    private readonly record struct ResolvedTriggerSite(
        string TargetExpression,
        string TargetPropertyExpression,
        string SetValueExpression,
        string ConditionExpression,
        ResolvedTriggerDependency[] Dependencies,
        string? Action,
        string? Argument);

    private readonly record struct ResolvedTriggerDependency(
        string ValueTypeName,
        string ReadExpression);

    private readonly record struct ResolvedBehaviorSite(
        string TargetExpression,
        XamlBehaviorDefinition Definition);

    private sealed record EffectSite(
        int NodeIndex,
        XamlEffectPlan Plan,
        IReadOnlyList<EffectBindingSite> Bindings)
    {
        internal bool HasBindings => Bindings.Count != 0;

        internal bool HasReactiveBindings
        {
            get
            {
                for (var i = 0; i < Bindings.Count; i++)
                {
                    if (Bindings[i].Member.Value.Binding.Mode != UiBindingMode.OneTime)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    private readonly record struct EffectBindingSite(
        XamlEffectLayerKind Layer,
        XamlEffectMemberPlan Member,
        XamlBindingDefinition Definition);

    private readonly record struct CollectionSite(
        int NodeIndex,
        XamlValuePlan Source,
        XamlBindingDefinition Definition,
        XamlTemplatePlan DefaultTemplate,
        XamlTemplateSelectorDefinition? Selector,
        XamlTemplatePlan[] Templates,
        int Start,
        int Count,
        float ItemExtent,
        float ViewportExtent,
        string HostPath,
        string ViewportPath,
        bool IsPicker);
}
