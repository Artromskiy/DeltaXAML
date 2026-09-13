using Delta;
using Delta.Diagnostics;
using Delta.XAML.Contract;
using DeltaXAML.Compiler;
using Retained = DeltaXAML.Internal;
using RetainedElement = DeltaXAML.Internal.UiElement;

namespace Delta.XAML;

public sealed class XamlLoadResult
{
    internal XamlLoadResult(
        UiElement? root,
        ReadOnlyMemory<Diagnostic> diagnostics,
        UiResourceCatalog? resources = null,
        UiTheme? theme = null)
    {
        Root = root;
        Diagnostics = diagnostics;
        Resources = resources;
        Theme = theme;
        Success = diagnostics.Length == 0 && (root is not null || resources is not null);
    }

    public UiElement? Root { get; }

    public ReadOnlyMemory<Diagnostic> Diagnostics { get; }

    /// <summary>Resources materialized from the cold document, when the plan declares or consumes resources.</summary>
    public UiResourceCatalog? Resources { get; }

    /// <summary>Styles and templates materialized from the cold document.</summary>
    public UiTheme? Theme { get; }

    public bool Success { get; }
}

public interface IXamlLoader
{
    XamlLoadResult Load(string source, in XamlLoadContext context);
}

public sealed class XamlLoader : IXamlLoader
{
    public XamlLoadResult Load(string source, in XamlLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context.Types);
        ArgumentNullException.ThrowIfNull(context.Resources);

        var typeResolver = context.Types;
        var resourceResolver = context.Resources;
        var views = new Dictionary<RetainedElement, UiElement>();
        Func<string, string, RetainedElement?> factory = (namespaceUri, localName) =>
            CreateCustomElement(namespaceUri, localName, typeResolver, views);
        var registry = XamlSemanticRegistry.CreateBuiltIns();
        var plan = XamlCompiler.Compile(
            SourceId.Empty,
            source,
            registry,
            allowUnregisteredTypes: true,
            allowUnregisteredResources: true);
        var diagnostics = new List<Diagnostic>(plan.Diagnostics);
        if (diagnostics.Count != 0)
        {
            return new(null, diagnostics.ToArray());
        }

        var resources = resourceResolver is UiResourceCatalog catalog
            ? catalog.Store
            : ResolveResources(plan, resourceResolver, resourceResolver as IUiNamedResourceResolver, diagnostics);
        if (diagnostics.Count != 0)
        {
            return new(null, diagnostics.ToArray());
        }

        var catalogForPlan = resources is null ? null : new UiResourceCatalog(resources);
        var theme = catalogForPlan is null ? null : Retained.XamlPlanMaterializer.MaterializeTheme(
            plan,
            catalogForPlan,
            diagnostics,
            factory,
            views,
            bindings: context.Bindings,
            selectors: context.TemplateSelectors);
        if (diagnostics.Count != 0)
        {
            return new(null, diagnostics.ToArray(), catalogForPlan, theme);
        }

        var materialized = Retained.XamlPlanMaterializer.Read(
            plan,
            factory,
            resources,
            views);
        diagnostics.AddRange(ConvertDiagnostics(materialized.Diagnostics));
        if (materialized.Root is not null)
        {
            AttachBindingSpecs(materialized.Root, context.Bindings, diagnostics, materialized.Names, theme, catalogForPlan, selectors: context.TemplateSelectors);
        }

        return new(
            diagnostics.Count == 0 && materialized.Root is not null ? UiElement.Wrap(materialized.Root, views) : null,
            diagnostics.ToArray(),
            catalogForPlan,
            theme);
    }

    private static void AttachBindingSpecs(
        RetainedElement element,
        IUiBindingResolver? resolver,
        List<Diagnostic> diagnostics,
        IReadOnlyDictionary<string, RetainedElement>? names = null,
        UiTheme? theme = null,
        UiResourceCatalog? resources = null,
        RetainedElement? templateOwner = null,
        IUiTemplateSelectorResolver? selectors = null)
    {
        foreach (var spec in element.BindingSpecs)
        {
            IUiValueConverter? converter = null;
            if (spec.ConverterKey is not null && (resolver is null || !resolver.TryResolveConverter(spec.ConverterKey, out converter)))
            {
                diagnostics.Add(CreateDiagnostic("XAML009", $"Binding converter '{spec.ConverterKey}' was not registered."));
                continue;
            }

            var expression = new UiBindingExpression(
                spec.Path,
                BindingModeMap.ToPublic(spec.Mode),
                converter,
                spec.StringFormat,
                spec.CultureName is null ? null : System.Globalization.CultureInfo.GetCultureInfo(spec.CultureName));
            if (spec.SourceKind == UiBindingSourceKind.Context)
            {
                element.AttachBinding(new Retained.UiInterpretedBinding(spec.Property, expression));
            }
            else
            {
                element.AttachExternalBinding(
                    spec.Property,
                    new Retained.UiInterpretedRelationBinding(
                        element,
                        spec.SourceKind,
                        spec.SourceArgument,
                        spec.Path,
                        BindingModeMap.ToPublic(spec.Mode),
                        converter,
                        spec.StringFormat,
                        spec.CultureName is null ? null : System.Globalization.CultureInfo.GetCultureInfo(spec.CultureName),
                        names,
                        templateOwner));
            }
        }

        for (var index = 0; index < element.MultiBindingSpecs.Count; index++)
        {
            var spec = element.MultiBindingSpecs[index];
            if (spec.FunctionKey is not null && resolver is not IUiBindingFunctionResolver)
            {
                diagnostics.Add(CreateDiagnostic("XAML020", $"MultiBinding function '{spec.FunctionKey}' is not registered for the cold path."));
                continue;
            }

            element.AttachExternalBinding(
                spec.Property,
                new Retained.UiInterpretedMultiBinding(
                    element,
                    spec.Sources,
                    spec.FunctionKey,
                    spec.StringFormat,
                    spec.CultureName is null ? null : System.Globalization.CultureInfo.GetCultureInfo(spec.CultureName),
                    names,
                    templateOwner,
                    resolver as IUiBindingFunctionResolver));
        }

        for (var index = 0; index < element.CollectionBindingSpecs.Count; index++)
        {
            if (theme is null || resources is null)
            {
                diagnostics.Add(CreateDiagnostic("XAML020", "Collection markup requires a materialized theme and resource catalog in the cold path."));
                continue;
            }

            var collection = element.CollectionBindingSpecs[index];
            if (collection.ItemTemplate is null && collection.ItemTemplateSelector is null)
            {
                diagnostics.Add(CreateDiagnostic("XAML020", "Interpreted collection markup requires ItemTemplate or ItemTemplateSelector."));
                continue;
            }

            if (collection.ItemTemplate is not null && !theme.TryGetTemplate(collection.ItemTemplate, out _))
            {
                diagnostics.Add(CreateDiagnostic("XAML020", $"Interpreted collection markup references undeclared ItemTemplate '{collection.ItemTemplate}'."));
                continue;
            }

            if (collection.ItemTemplateSelector is not null && selectors is null)
            {
                diagnostics.Add(CreateDiagnostic("XAML020", $"Interpreted item-template selector '{collection.ItemTemplateSelector}' requires a registered selector resolver."));
                continue;
            }

            element.AttachCollectionBinding(new Retained.XamlPlanMaterializer.UiInterpretedCollectionBinding(
                element,
                collection,
                theme,
                resources,
                selectors));
        }

        foreach (var child in element.Children)
        {
            if (child is RetainedElement retainedChild)
            {
                AttachBindingSpecs(retainedChild, resolver, diagnostics, names, theme, resources, templateOwner, selectors);
            }
        }
    }

    private static RetainedElement? CreateCustomElement(
        string namespaceUri,
        string localName,
        IXamlTypeResolver resolver,
        Dictionary<RetainedElement, UiElement> views)
    {
        if (!resolver.TryResolveName(new XamlQualifiedName(namespaceUri, localName), out var type) ||
            !resolver.TryCreate(type, out var element) ||
            element is null)
        {
            return null;
        }

        views[element.RetainedElement] = element;
        return element.RetainedElement;
    }

    private static Retained.UiResourceStore? ResolveResources(
        XamlDocumentPlan plan,
        IUiResourceResolver resolver,
        IUiNamedResourceResolver? namedResolver,
        List<Diagnostic> diagnostics)
    {
        Retained.UiResourceStore? resources = HasLocalDeclarations(plan) ? new Retained.UiResourceStore() : null;
        var localIds = new HashSet<Guid>();
        for (var i = 0; i < plan.ScalarResources.Length; i++)
        {
            localIds.Add(plan.ScalarResources[i].Id.Value);
        }

        for (var i = 0; i < plan.GradientResources.Length; i++)
        {
            localIds.Add(plan.GradientResources[i].Id.Value);
        }

        for (var i = 0; i < plan.GradientResources.Length; i++)
        {
            localIds.Add(plan.GradientResources[i].PayloadId.Value);
        }

        for (var i = 0; i < plan.EffectResources.Length; i++)
        {
            localIds.Add(plan.EffectResources[i].Resource.Value);
        }

        for (var i = 0; i < plan.Resources.Length; i++)
        {
            localIds.Add(plan.Resources[i].Id.Value);
        }
        for (var i = 0; i < plan.ResourceSlots.Length; i++)
        {
            var slot = plan.ResourceSlots[i];
            if (localIds.Contains(slot.Id.Value))
            {
                continue;
            }

            if (namedResolver is null || !namedResolver.TryResolve(slot.Key, out var namedValue))
            {
                diagnostics.Add(CreateDiagnostic("XAML006", $"Resource '{slot.Key}' was not resolved."));
                continue;
            }

            resources ??= new Retained.UiResourceStore();
            resources.Set(slot.Key, namedValue);
        }

        CollectGuidResources(plan.Root, resolver, ref resources, diagnostics);

        return resources;
    }

    private static bool HasLocalDeclarations(XamlDocumentPlan plan) =>
        plan.Resources.Length != 0 || plan.ScalarResources.Length != 0 || plan.GradientResources.Length != 0 ||
        plan.Styles.Length != 0 || plan.Templates.Length != 0 || plan.Triggers.Length != 0 || plan.Behaviors.Length != 0 || plan.EffectResources.Length != 0 ||
        string.Equals(plan.Root?.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal) ||
        HasImplicitEffectProperties(plan.Root);

    private static bool HasImplicitEffectProperties(XamlObjectPlan? plan)
    {
        if (plan is null)
        {
            return false;
        }

        if (plan.InlineEffect is not null)
        {
            return true;
        }

        for (var index = 0; index < plan.Members.Length; index++)
        {
            var member = plan.Members[index];
            if (member.Value.Kind is XamlValueKind.Single or XamlValueKind.Thickness &&
                member.Name is "BorderWidth" or "BorderThickness" or "StrokeWidth")
            {
                return true;
            }
        }

        for (var index = 0; index < plan.Children.Length; index++)
        {
            if (HasImplicitEffectProperties(plan.Children[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectGuidResources(
        XamlObjectPlan? plan,
        IUiResourceResolver resolver,
        ref Retained.UiResourceStore? resources,
        List<Diagnostic> diagnostics)
    {
        if (plan is null)
        {
            return;
        }

        for (var i = 0; i < plan.Members.Length; i++)
        {
            var member = plan.Members[i];
            if (member.Name == "ForegroundResource" &&
                member.Value.Kind == XamlValueKind.ResourceId &&
                Guid.TryParse(member.Value.Literal.CanonicalText, out var resourceGuid))
            {
                var resourceId = new UiResourceId(resourceGuid);
                if (!resolver.TryResolve(resourceId, out var value))
                {
                    diagnostics.Add(CreateDiagnostic("XAML006", $"Resource '{resourceGuid}' was not resolved.", member.Range.Start.Line, member.Range.Start.Column));
                }
                else
                {
                    resources ??= new Retained.UiResourceStore();
                    resources.Set(resourceGuid.ToString("D"), value);
                }
            }
        }

        for (var i = 0; i < plan.Children.Length; i++)
        {
            CollectGuidResources(plan.Children[i], resolver, ref resources, diagnostics);
        }
    }

    private static Diagnostic[] ConvertDiagnostics(IReadOnlyList<Retained.XamlMaterializerDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return Array.Empty<Diagnostic>();
        }

        var converted = new Diagnostic[diagnostics.Count];
        for (var i = 0; i < diagnostics.Count; i++)
        {
            var diagnostic = diagnostics[i];
            var line = Maths.Max(0, diagnostic.Line - 1);
            var column = Maths.Max(0, diagnostic.Column - 1);
            converted[i] = CreateDiagnostic(diagnostic.Code, diagnostic.Message, line, column);
        }

        return converted;
    }

    private static Diagnostic CreateDiagnostic(string code, string message, int line = 0, int column = 0)
    {
        var location = line > 0 || column > 0
            ? new SourceRange(SourceId.Empty, new(Maths.Max(0, line), Maths.Max(0, column), 0), new(Maths.Max(0, line), Maths.Max(0, column), 0))
            : (SourceRange?)null;
        return new(new DiagnosticCode(code), DiagnosticSeverity.Error, message, location);
    }
}
