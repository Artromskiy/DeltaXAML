using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Delta.Diagnostics;
using Delta.XAML;
using Delta.XAML.Contract;

namespace DeltaXAML.Compiler;

internal enum XamlValueKind
{
    Invalid,
    String,
    Boolean,
    Single,
    Double,
    Color,
    Thickness,
    GridLengthList,
    Enum,
    Binding,
    ResourceReference,
}

internal enum XamlContentKind
{
    None,
    Children,
    SingleContent,
}

internal readonly record struct XamlPropertyDefinition(
    UiPropertyId Id,
    string Name,
    XamlValueKind ValueKind);

internal sealed class XamlTypeDefinition
{
    private readonly Dictionary<string, XamlPropertyDefinition> _properties;

    internal XamlTypeDefinition(
        UiTypeId id,
        XamlQualifiedName name,
        XamlContentKind contentKind,
        ImmutableArray<XamlPropertyDefinition> properties,
        string? factoryExpression = null)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException("A stable type identity is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name.LocalName))
        {
            throw new ArgumentException("A XAML local name is required.", nameof(name));
        }

        Id = id;
        Name = name;
        ContentKind = contentKind;
        Properties = properties;
        FactoryExpression = factoryExpression;
        _properties = new(StringComparer.Ordinal);
        var propertyIds = new HashSet<UiPropertyId>();
        foreach (var property in properties)
        {
            if (!property.Id.IsValid || string.IsNullOrWhiteSpace(property.Name))
            {
                throw new ArgumentException("Every XAML property needs a stable identity and name.", nameof(properties));
            }

            if (!propertyIds.Add(property.Id))
            {
                throw new ArgumentException($"The XAML property identity '{property.Id.Value}' is registered twice.", nameof(properties));
            }

            if (!_properties.TryAdd(property.Name, property))
            {
                throw new ArgumentException($"The XAML property '{property.Name}' is registered twice.", nameof(properties));
            }
        }
    }

    internal UiTypeId Id { get; }

    internal XamlQualifiedName Name { get; }

    internal XamlContentKind ContentKind { get; }

    internal ImmutableArray<XamlPropertyDefinition> Properties { get; }

    /// <summary>Trusted generated construction expression; null means no compiled factory is registered.</summary>
    internal string? FactoryExpression { get; }

    internal bool TryGetProperty(string name, out XamlPropertyDefinition property) =>
        _properties.TryGetValue(name, out property);
}

internal readonly record struct XamlLiteralValue(
    XamlValueKind Kind,
    string CanonicalText);

internal readonly record struct XamlResourceReferencePlan(
    UiResourceId Id,
    string Key,
    bool IsDynamic);

internal readonly record struct XamlBindingPlan(
    string Path,
    UiBindingMode Mode,
    string? ConverterKey,
    string? StringFormat);

internal readonly record struct XamlValuePlan(
    XamlValueKind Kind,
    XamlLiteralValue Literal,
    XamlResourceReferencePlan Resource,
    XamlBindingPlan Binding)
{
    internal static XamlValuePlan FromLiteral(XamlLiteralValue literal) =>
        new(literal.Kind, literal, default, default);

    internal static XamlValuePlan FromResource(XamlResourceReferencePlan resource) =>
        new(XamlValueKind.ResourceReference, default, resource, default);

    internal static XamlValuePlan FromBinding(XamlBindingPlan binding) =>
        new(XamlValueKind.Binding, default, default, binding);
}

internal readonly record struct XamlMemberPlan(
    UiPropertyId Property,
    string Name,
    XamlValuePlan Value,
    SourceRange Range);

internal sealed record XamlObjectPlan(
    UiTypeId Type,
    XamlQualifiedName Name,
    string? ScopeName,
    SourceRange Range,
    ImmutableArray<XamlMemberPlan> Members,
    ImmutableArray<XamlObjectPlan> Children);

internal sealed record XamlResourcePlan(
    UiResourceId Id,
    string Key,
    XamlObjectPlan Value,
    SourceRange Range);

internal sealed record XamlStylePlan(
    string Key,
    XamlQualifiedName TargetType,
    ImmutableArray<XamlMemberPlan> Setters,
    SourceRange Range);

internal sealed record XamlTemplatePlan(
    string Key,
    XamlObjectPlan Root,
    SourceRange Range);

internal sealed record XamlDocumentPlan(
    SourceId Source,
    XamlObjectPlan? Root,
    ImmutableArray<XamlResourcePlan> Resources,
    ImmutableArray<XamlStylePlan> Styles,
    ImmutableArray<XamlTemplatePlan> Templates,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal bool Success => Root is not null && Diagnostics.All(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

internal sealed class XamlSemanticRegistry
{
    private readonly Dictionary<XamlQualifiedName, XamlTypeDefinition> _types = new();
    private readonly Dictionary<UiTypeId, XamlQualifiedName> _typeIds = new();
    private readonly Dictionary<UiTypeId, XamlTypeDefinition> _definitions = new();
    private readonly Dictionary<string, UiResourceId> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<UiResourceId, string> _resourceIds = new();

    internal void RegisterType(XamlTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (_types.ContainsKey(definition.Name))
        {
            throw new ArgumentException($"The XAML type '{definition.Name.LocalName}' is registered twice.", nameof(definition));
        }

        if (_typeIds.ContainsKey(definition.Id))
        {
            throw new ArgumentException($"The XAML type identity '{definition.Id.Value}' is registered twice.", nameof(definition));
        }

        _types.Add(definition.Name, definition);
        _typeIds.Add(definition.Id, definition.Name);
        _definitions.Add(definition.Id, definition);
    }

    internal void RegisterResource(string key, UiResourceId id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!id.IsValid)
        {
            throw new ArgumentException("A stable resource identity is required.", nameof(id));
        }

        if (!_resources.TryAdd(key, id))
        {
            throw new ArgumentException($"The XAML resource '{key}' is registered twice.", nameof(key));
        }

        if (!_resourceIds.TryAdd(id, key))
        {
            _resources.Remove(key);
            throw new ArgumentException($"The XAML resource identity '{id.Value}' is registered twice.", nameof(id));
        }
    }

    internal bool TryResolveType(
        in XamlQualifiedName name,
        [NotNullWhen(true)] out XamlTypeDefinition? definition)
    {
        if (_types.TryGetValue(name, out definition))
        {
            return true;
        }

        if (name.Namespace.Length != 0 && _types.TryGetValue(new XamlQualifiedName(string.Empty, name.LocalName), out definition))
        {
            return true;
        }

        definition = null;
        return false;
    }

    internal bool TryResolveType(
        UiTypeId id,
        [NotNullWhen(true)] out XamlTypeDefinition? definition) =>
        _definitions.TryGetValue(id, out definition);

    internal bool TryResolveResource(string key, out UiResourceId id) =>
        _resources.TryGetValue(key, out id);

    internal static XamlSemanticRegistry CreateBuiltIns()
    {
        var registry = new XamlSemanticRegistry();
        var common = ImmutableArray.Create(
            Property("Width", "11111111-1111-1111-1111-111111111101", XamlValueKind.Single),
            Property("Height", "11111111-1111-1111-1111-111111111102", XamlValueKind.Single),
            Property("Background", "11111111-1111-1111-1111-111111111103", XamlValueKind.Color),
            Property("Padding", "11111111-1111-1111-1111-111111111104", XamlValueKind.Thickness),
            Property("Fill", "11111111-1111-1111-1111-111111111105", XamlValueKind.Boolean),
            Property("IsEnabled", "11111111-1111-1111-1111-111111111106", XamlValueKind.Boolean),
            Property("IsSelected", "11111111-1111-1111-1111-111111111107", XamlValueKind.Boolean),
            Property("StyleKey", "11111111-1111-1111-1111-111111111108", XamlValueKind.String),
            Property("TemplateKey", "11111111-1111-1111-1111-111111111109", XamlValueKind.String),
            Property("AutomationName", "11111111-1111-1111-1111-11111111110A", XamlValueKind.String),
            Property("AutomationRole", "11111111-1111-1111-1111-11111111110B", XamlValueKind.Enum));
        Register(registry, "Panel", "22222222-2222-2222-2222-222222222201", XamlContentKind.Children, common);
        Register(registry, "Border", "22222222-2222-2222-2222-222222222202", XamlContentKind.SingleContent, common);
        Register(registry, "ContentControl", "22222222-2222-2222-2222-222222222203", XamlContentKind.SingleContent, common);
        Register(registry, "ScrollViewer", "22222222-2222-2222-2222-222222222204", XamlContentKind.SingleContent, common);

        Register(registry, "StackPanel", "22222222-2222-2222-2222-222222222205", XamlContentKind.Children,
            common.Add(Property("Orientation", "11111111-1111-1111-1111-11111111110C", XamlValueKind.Enum)));
        Register(registry, "Grid", "22222222-2222-2222-2222-222222222206", XamlContentKind.Children,
            common.AddRange(ImmutableArray.Create(
                Property("Columns", "11111111-1111-1111-1111-11111111110D", XamlValueKind.GridLengthList),
                Property("Rows", "11111111-1111-1111-1111-11111111110E", XamlValueKind.GridLengthList))));
        Register(registry, "ItemsControl", "22222222-2222-2222-2222-222222222207", XamlContentKind.Children, common);
        Register(registry, "Button", "22222222-2222-2222-2222-222222222208", XamlContentKind.SingleContent, common);
        Register(registry, "ToggleButton", "22222222-2222-2222-2222-222222222209", XamlContentKind.SingleContent, common);

        var text = common.AddRange(ImmutableArray.Create(
            Property("Text", "11111111-1111-1111-1111-11111111110F", XamlValueKind.String),
            Property("FontKey", "11111111-1111-1111-1111-111111111110", XamlValueKind.String),
            Property("FontSize", "11111111-1111-1111-1111-111111111111", XamlValueKind.Single),
            Property("Foreground", "11111111-1111-1111-1111-111111111112", XamlValueKind.Color)));
        Register(registry, "TextBlock", "22222222-2222-2222-2222-22222222220A", XamlContentKind.None, text);
        Register(registry, "TextBox", "22222222-2222-2222-2222-22222222220B", XamlContentKind.None, text);

        Register(registry, "NumericEditor", "22222222-2222-2222-2222-22222222220C", XamlContentKind.None,
            text.AddRange(ImmutableArray.Create(
                Property("Value", "11111111-1111-1111-1111-111111111113", XamlValueKind.Double),
                Property("Minimum", "11111111-1111-1111-1111-111111111114", XamlValueKind.Double),
                Property("Maximum", "11111111-1111-1111-1111-111111111115", XamlValueKind.Double))));
        return registry;
    }

    private static XamlPropertyDefinition Property(string name, string id, XamlValueKind kind) =>
        new(new UiPropertyId(Guid.Parse(id)), name, kind);

    private static void Register(
        XamlSemanticRegistry registry,
        string name,
        string id,
        XamlContentKind contentKind,
        ImmutableArray<XamlPropertyDefinition> properties) =>
        registry.RegisterType(new(
            new UiTypeId(Guid.Parse(id)),
            new XamlQualifiedName(string.Empty, name),
            contentKind,
            properties,
            $"new global::Delta.XAML.Ui{name}()"));
}
