using Delta;
using Delta.Diagnostics;
using Delta.XAML.Contract;
using DeltaXAML.Compiler;
using Retained = DeltaXAML.Internal;
using RetainedElement = DeltaXAML.Internal.UiElement;

namespace Delta.XAML;

public sealed class XamlLoadResult
{
    internal XamlLoadResult(UiElement? root, ReadOnlyMemory<Diagnostic> diagnostics)
    {
        Root = root;
        Diagnostics = diagnostics;
        Success = root is not null && diagnostics.Length == 0;
    }

    public UiElement? Root { get; }

    public ReadOnlyMemory<Diagnostic> Diagnostics { get; }

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
        var registry = XamlSemanticRegistry.CreateBuiltIns();
        var plan = XamlCompiler.Compile(
            SourceId.Empty,
            source,
            registry,
            allowUnregisteredTypes: true,
            allowUnregisteredResources: true);
        var diagnostics = new List<Diagnostic>(plan.Diagnostics);
        var rootName = ReadRootElementName(source);
        if (IsCompilerOnlyDeclaration(rootName))
        {
            diagnostics.Clear();
            diagnostics.Add(CreateDiagnostic("XAML020", $"Declaration '{rootName}' requires the generated XAML path."));
            return new(null, diagnostics.ToArray());
        }

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

        if (plan.Root is null)
        {
            return new(null, diagnostics.ToArray());
        }

        var materialized = Retained.XamlPlanMaterializer.Read(
            plan,
            (namespaceUri, localName) => CreateCustomElement(namespaceUri, localName, typeResolver, views),
            resources);
        diagnostics.AddRange(ConvertDiagnostics(materialized.Diagnostics));
        if (materialized.Root is not null)
        {
            AttachBindingSpecs(materialized.Root, context.Bindings, diagnostics);
        }

        return new(diagnostics.Count == 0 && materialized.Root is not null ? UiElement.Wrap(materialized.Root, views) : null, diagnostics.ToArray());
    }

    private static void AttachBindingSpecs(
        RetainedElement element,
        IUiBindingResolver? resolver,
        List<Diagnostic> diagnostics)
    {
        foreach (var spec in element.BindingSpecs)
        {
            IUiValueConverter? converter = null;
            if (spec.ConverterKey is not null && (resolver is null || !resolver.TryResolveConverter(spec.ConverterKey, out converter)))
            {
                diagnostics.Add(CreateDiagnostic("XAML009", $"Binding converter '{spec.ConverterKey}' was not registered."));
                continue;
            }

            element.AttachBinding(new Retained.UiInterpretedBinding(
                spec.Property,
                new UiBindingExpression(
                    spec.Path,
                    BindingModeMap.ToPublic(spec.Mode),
                    converter,
                    spec.StringFormat,
                    spec.CultureName is null ? null : System.Globalization.CultureInfo.GetCultureInfo(spec.CultureName))));
        }

        foreach (var child in element.Children)
        {
            if (child is RetainedElement retainedChild)
            {
                AttachBindingSpecs(retainedChild, resolver, diagnostics);
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
        Retained.UiResourceStore? resources = null;
        for (var i = 0; i < plan.ResourceSlots.Length; i++)
        {
            var slot = plan.ResourceSlots[i];
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

    private static bool IsCompilerOnlyDeclaration(string? name) => name is
        "Resource" or "ResourceDictionary" or "Style" or "Setter" or "Template" or "Trigger" or "Behavior" or "VisualState";

    private static string? ReadRootElementName(string source)
    {
        var offset = 0;
        while ((offset = source.IndexOf('<', offset)) >= 0)
        {
            if (source.AsSpan(offset).StartsWith("<!--", StringComparison.Ordinal))
            {
                var endComment = source.IndexOf("-->", offset + 4, StringComparison.Ordinal);
                offset = endComment < 0 ? source.Length : endComment + 3;
                continue;
            }

            if (source.AsSpan(offset).StartsWith("<?", StringComparison.Ordinal))
            {
                var endInstruction = source.IndexOf("?>", offset + 2, StringComparison.Ordinal);
                offset = endInstruction < 0 ? source.Length : endInstruction + 2;
                continue;
            }

            var start = offset + 1;
            if (start < source.Length && source[start] == '/')
            {
                offset = start + 1;
                continue;
            }

            var end = start;
            while (end < source.Length && (char.IsLetterOrDigit(source[end]) || source[end] is '_' or ':' or '-' or '.'))
            {
                end++;
            }

            if (end == start)
            {
                return null;
            }

            var name = source[start..end];
            var separator = name.LastIndexOf(':');
            return separator < 0 ? name : name[(separator + 1)..];
        }

        return null;
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
