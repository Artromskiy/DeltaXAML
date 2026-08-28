using System.Xml;
using Delta.Diagnostics;
using Delta.XAML.Contract;
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
        var contextDiagnostics = new List<Diagnostic>();
        var resources = resourceResolver is UiResourceCatalog catalog
            ? catalog.Store
            : ResolveResources(source, resourceResolver, resourceResolver as IUiNamedResourceResolver, contextDiagnostics);
        if (contextDiagnostics.Count != 0)
        {
            return new(null, contextDiagnostics.ToArray());
        }

        var retained = Retained.InterpretedXamlReader.Read(
            source,
            (namespaceUri, localName) => CreateCustomElement(namespaceUri, localName, typeResolver, views),
            resources);
        var diagnostics = new List<Diagnostic>(ConvertDiagnostics(retained.Diagnostics));
        if (retained.Root is not null)
        {
            AttachBindingSpecs(retained.Root, context.Bindings, diagnostics);
        }

        return new(diagnostics.Count == 0 && retained.Root is not null ? UiElement.Wrap(retained.Root, views) : null, diagnostics.ToArray());
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
        string source,
        IUiResourceResolver resolver,
        IUiNamedResourceResolver? namedResolver,
        List<Diagnostic> diagnostics)
    {
        Retained.UiResourceStore? resources = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                while (reader.MoveToNextAttribute())
                {
                    if (string.Equals(reader.LocalName, "ForegroundResource", StringComparison.Ordinal))
                    {
                        if (!Guid.TryParse(reader.Value, out var resourceGuid))
                        {
                            diagnostics.Add(CreateDiagnostic("XAML005", $"Resource '{reader.Value}' is not a GUID identity."));
                            continue;
                        }

                        if (!resolver.TryResolve(new UiResourceId(resourceGuid), out var value))
                        {
                            diagnostics.Add(CreateDiagnostic("XAML006", $"Resource '{resourceGuid}' was not resolved."));
                            continue;
                        }

                        resources ??= new Retained.UiResourceStore();
                        resources.Set(resourceGuid.ToString("D"), value);
                        continue;
                    }

                    if (!Retained.InterpretedXamlReader.TryParseResourceReference(reader.Value, out var resourceKey, out _))
                    {
                        continue;
                    }

                    if (namedResolver is null || !namedResolver.TryResolve(resourceKey, out var namedValue))
                    {
                        diagnostics.Add(CreateDiagnostic("XAML006", $"Resource '{resourceKey}' was not resolved."));
                        continue;
                    }

                    resources ??= new Retained.UiResourceStore();
                    resources.Set(resourceKey, namedValue);
                }

                reader.MoveToElement();
            }
        }
        catch (XmlException exception)
        {
            diagnostics.Add(CreateDiagnostic("XAML001", exception.Message));
        }

        return resources;
    }

    private static Diagnostic[] ConvertDiagnostics(IReadOnlyList<Retained.InterpretedXamlDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return Array.Empty<Diagnostic>();
        }

        var converted = new Diagnostic[diagnostics.Count];
        for (var i = 0; i < diagnostics.Count; i++)
        {
            var diagnostic = diagnostics[i];
            var line = Math.Max(0, diagnostic.Line - 1);
            var column = Math.Max(0, diagnostic.Column - 1);
            converted[i] = CreateDiagnostic(diagnostic.Code, diagnostic.Message, line, column);
        }

        return converted;
    }

    private static Diagnostic CreateDiagnostic(string code, string message, int line = 0, int column = 0)
    {
        var location = line > 0 || column > 0
            ? new SourceRange(SourceId.Empty, new(Math.Max(0, line), Math.Max(0, column), 0), new(Math.Max(0, line), Math.Max(0, column), 0))
            : (SourceRange?)null;
        return new(new DiagnosticCode(code), DiagnosticSeverity.Error, message, location);
    }
}
