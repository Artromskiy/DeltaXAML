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

internal enum XamlVisualStateName
{
    None,
    Unknown,
    Normal,
    Hover,
    Pressed,
    Focused,
    Disabled,
    Invalid,
    Selected,
}

internal readonly record struct XamlPropertyDefinition(
    UiPropertyId Id,
    string Name,
    XamlValueKind ValueKind,
    string? SetterExpression = null);

internal sealed class XamlTypeDefinition
{
    private readonly Dictionary<string, XamlPropertyDefinition> _properties;

    internal XamlTypeDefinition(
        UiTypeId id,
        XamlQualifiedName name,
        XamlContentKind contentKind,
        ImmutableArray<XamlPropertyDefinition> properties,
        string? factoryExpression = null,
        string? childAttachmentExpression = null,
        string? contentAttachmentExpression = null)
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
        ChildAttachmentExpression = childAttachmentExpression;
        ContentAttachmentExpression = contentAttachmentExpression;
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

            if (property.SetterExpression is { } setter && !IsDirectSetterExpression(setter))
            {
                throw new ArgumentException($"The XAML property setter '{setter}' must be a qualified static method name.", nameof(properties));
            }
        }

        if (childAttachmentExpression is { } childAttachment && !IsDirectSetterExpression(childAttachment))
        {
            throw new ArgumentException($"The XAML child attachment '{childAttachment}' must be a qualified static method name.", nameof(childAttachmentExpression));
        }

        if (contentAttachmentExpression is { } contentAttachment && !IsDirectSetterExpression(contentAttachment))
        {
            throw new ArgumentException($"The XAML content attachment '{contentAttachment}' must be a qualified static method name.", nameof(contentAttachmentExpression));
        }

        if (childAttachmentExpression is not null && contentKind != XamlContentKind.Children)
        {
            throw new ArgumentException("A child attachment thunk requires Children content.", nameof(childAttachmentExpression));
        }

        if (contentAttachmentExpression is not null && contentKind != XamlContentKind.SingleContent)
        {
            throw new ArgumentException("A content attachment thunk requires SingleContent content.", nameof(contentAttachmentExpression));
        }
    }

    internal UiTypeId Id { get; }

    internal XamlQualifiedName Name { get; }

    internal XamlContentKind ContentKind { get; }

    internal ImmutableArray<XamlPropertyDefinition> Properties { get; }

    /// <summary>Trusted generated construction expression; null means no compiled factory is registered.</summary>
    internal string? FactoryExpression { get; }

    /// <summary>Trusted generated child attachment thunk for custom children owners.</summary>
    internal string? ChildAttachmentExpression { get; }

    /// <summary>Trusted generated content attachment thunk for custom single-content owners.</summary>
    internal string? ContentAttachmentExpression { get; }

    internal bool TryGetProperty(string name, out XamlPropertyDefinition property) =>
        _properties.TryGetValue(name, out property);

    private static bool IsDirectSetterExpression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var start = value.StartsWith("global::", StringComparison.Ordinal) ? 8 : 0;
        if (start == value.Length)
        {
            return false;
        }

        var segments = value[start..].Split('.');
        if (segments.Length < 2)
        {
            return false;
        }

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0 || (!char.IsLetter(segment[0]) && segment[0] != '_'))
            {
                return false;
            }

            for (var character = 1; character < segment.Length; character++)
            {
                if (!char.IsLetterOrDigit(segment[character]) && segment[character] != '_')
                {
                    return false;
                }
            }
        }

        return true;
    }
}

internal readonly record struct XamlLiteralValue(
    XamlValueKind Kind,
    string CanonicalText);

internal readonly record struct XamlResourceReferencePlan(
    UiResourceId Id,
    string Key,
    bool IsDynamic,
    int Slot = -1);

internal readonly record struct XamlResourceSlotPlan(
    UiResourceId Id,
    string Key,
    int LocalIndex,
    bool IsDynamic);

internal readonly record struct XamlBindingPlan(
    string Path,
    UiBindingMode Mode,
    string? ConverterKey,
    string? StringFormat);

internal sealed record XamlBindingDefinition(
    string Path,
    string SourceTypeName,
    string ValueTypeName,
    string ReadExpression,
    string? WriteExpression,
    string? ConverterKey = null);

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
    UiStyleId Id,
    XamlQualifiedName TargetType,
    UiTypeId TargetTypeId,
    ImmutableArray<XamlMemberPlan> Setters,
    ImmutableArray<XamlVisualStatePlan> VisualStates,
    SourceRange Range);

internal sealed record XamlVisualStatePlan(
    XamlVisualStateName State,
    ImmutableArray<XamlMemberPlan> Setters,
    SourceRange Range);

internal sealed record XamlTemplatePlan(
    string Key,
    UiTemplateId Id,
    XamlObjectPlan Root,
    SourceRange Range);

internal sealed record XamlDocumentPlan(
    SourceId Source,
    XamlObjectPlan? Root,
    ImmutableArray<XamlResourcePlan> Resources,
    ImmutableArray<XamlStylePlan> Styles,
    ImmutableArray<XamlTemplatePlan> Templates,
    ImmutableArray<XamlResourceSlotPlan> ResourceSlots,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal bool Success =>
        (Root is not null || Resources.Length != 0 || Styles.Length != 0 || Templates.Length != 0) &&
        Diagnostics.All(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

internal sealed class XamlSemanticRegistry
{
    private readonly Dictionary<XamlQualifiedName, XamlTypeDefinition> _types = new();
    private readonly Dictionary<UiTypeId, XamlQualifiedName> _typeIds = new();
    private readonly Dictionary<UiTypeId, XamlTypeDefinition> _definitions = new();
    private readonly Dictionary<string, UiResourceId> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<UiResourceId, string> _resourceIds = new();
    private readonly Dictionary<string, XamlBindingDefinition> _bindings = new(StringComparer.Ordinal);

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

    internal void RegisterBinding(XamlBindingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Path);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.SourceTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ValueTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ReadExpression);
        if (definition.ConverterKey is { } converterKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(converterKey);
        }

        if (!_bindings.TryAdd(definition.Path, definition))
        {
            throw new ArgumentException($"The XAML binding path '{definition.Path}' is registered twice.", nameof(definition));
        }
    }

    internal bool TryResolveBinding(string path, [NotNullWhen(true)] out XamlBindingDefinition? definition) =>
        _bindings.TryGetValue(path, out definition);

    internal static XamlSemanticRegistry CreateBuiltIns()
    {
        var registry = new XamlSemanticRegistry();
        var common = ImmutableArray.Create(
            Property("Width", "10000000-0000-4000-8000-000000000001", XamlValueKind.Single),
            Property("Height", "10000000-0000-4000-8000-000000000002", XamlValueKind.Single),
            Property("Background", "10000000-0000-4000-8000-000000000003", XamlValueKind.Color),
            Property("Padding", "10000000-0000-4000-8000-000000000004", XamlValueKind.Thickness),
            Property("Fill", "10000000-0000-4000-8000-000000000005", XamlValueKind.Boolean),
            Property("IsEnabled", "10000000-0000-4000-8000-000000000006", XamlValueKind.Boolean),
            Property("IsSelected", "10000000-0000-4000-8000-000000000007", XamlValueKind.Boolean),
            Property("StyleKey", "10000000-0000-4000-8000-000000000008", XamlValueKind.String),
            Property("TemplateKey", "10000000-0000-4000-8000-000000000009", XamlValueKind.String),
            Property("AutomationName", "11111111-1111-1111-1111-11111111110A", XamlValueKind.String),
            Property("AutomationRole", "11111111-1111-1111-1111-11111111110B", XamlValueKind.Enum));
        Register(registry, "Panel", "22222222-2222-2222-2222-222222222201", XamlContentKind.Children, common);
        Register(registry, "Border", "22222222-2222-2222-2222-222222222202", XamlContentKind.SingleContent, common);
        Register(registry, "ContentControl", "22222222-2222-2222-2222-222222222203", XamlContentKind.SingleContent, common);
        Register(registry, "ScrollViewer", "22222222-2222-2222-2222-222222222204", XamlContentKind.SingleContent, common);

        Register(registry, "StackPanel", "22222222-2222-2222-2222-222222222205", XamlContentKind.Children,
            common.Add(Property("Orientation", "40000000-0000-4000-8000-000000000001", XamlValueKind.Enum)));
        Register(registry, "Grid", "22222222-2222-2222-2222-222222222206", XamlContentKind.Children,
            common.AddRange(ImmutableArray.Create(
                Property("Columns", "40000000-0000-4000-8000-000000000002", XamlValueKind.GridLengthList),
                Property("Rows", "40000000-0000-4000-8000-000000000003", XamlValueKind.GridLengthList))));
        Register(registry, "ItemsControl", "22222222-2222-2222-2222-222222222207", XamlContentKind.Children, common);
        Register(registry, "Button", "22222222-2222-2222-2222-222222222208", XamlContentKind.SingleContent, common);
        Register(registry, "ToggleButton", "22222222-2222-2222-2222-222222222209", XamlContentKind.SingleContent, common);

        var text = common.AddRange(ImmutableArray.Create(
            Property("Text", "20000000-0000-4000-8000-000000000001", XamlValueKind.String),
            Property("FontKey", "20000000-0000-4000-8000-000000000002", XamlValueKind.String),
            Property("FontSize", "20000000-0000-4000-8000-000000000003", XamlValueKind.Single),
            Property("Foreground", "20000000-0000-4000-8000-000000000004", XamlValueKind.Color)));
        Register(registry, "TextBlock", "22222222-2222-2222-2222-22222222220A", XamlContentKind.None, text);
        Register(registry, "TextBox", "22222222-2222-2222-2222-22222222220B", XamlContentKind.None, text);

        Register(registry, "NumericEditor", "22222222-2222-2222-2222-22222222220C", XamlContentKind.None,
            text.AddRange(ImmutableArray.Create(
                Property("Value", "30000000-0000-4000-8000-000000000001", XamlValueKind.Double),
                Property("Minimum", "30000000-0000-4000-8000-000000000002", XamlValueKind.Double),
                Property("Maximum", "30000000-0000-4000-8000-000000000003", XamlValueKind.Double))));
        registry.RegisterType(new(
            new UiTypeId(Guid.Parse("22222222-2222-2222-2222-22222222220D")),
            new XamlQualifiedName(string.Empty, "ResourceDictionary"),
            XamlContentKind.Children,
            ImmutableArray<XamlPropertyDefinition>.Empty));
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
