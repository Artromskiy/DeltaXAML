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
    Integer,
    ResourceId,
    Brush,
    Color,
    Thickness,
    CornerRadii,
    GridLengthList,
    Enum,
    Binding,
    MultiBinding,
    ResourceReference,
    ItemsSource,
}

internal enum XamlBindingSourceKind
{
    Context,
    Self,
    TemplateOwner,
    Name,
    Ancestor,
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
    string? SetterExpression = null,
    string? MemberName = null,
    string? AttachedPropertyExpression = null);

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
        string? contentAttachmentExpression = null,
        string? childAttachmentMember = null,
        string? contentAttachmentMember = null)
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
        ChildAttachmentMember = childAttachmentMember;
        ContentAttachmentMember = contentAttachmentMember;
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

            if (property.MemberName is { } member && !IsIdentifier(member))
            {
                throw new ArgumentException($"The XAML property member '{member}' must be a C# identifier.", nameof(properties));
            }

            if (property.SetterExpression is not null && property.MemberName is not null)
            {
                throw new ArgumentException($"The XAML property '{property.Name}' cannot use both a static setter and a direct member.", nameof(properties));
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

        if (childAttachmentMember is { } childMember && !IsIdentifier(childMember))
        {
            throw new ArgumentException($"The XAML child attachment member '{childMember}' must be a C# identifier.", nameof(childAttachmentMember));
        }

        if (contentAttachmentMember is { } contentMember && !IsIdentifier(contentMember))
        {
            throw new ArgumentException($"The XAML content attachment member '{contentMember}' must be a C# identifier.", nameof(contentAttachmentMember));
        }

        if (childAttachmentExpression is not null && childAttachmentMember is not null)
        {
            throw new ArgumentException("A child owner cannot use both static and instance attachment members.", nameof(childAttachmentMember));
        }

        if (contentAttachmentExpression is not null && contentAttachmentMember is not null)
        {
            throw new ArgumentException("A content owner cannot use both static and instance attachment members.", nameof(contentAttachmentMember));
        }

        if (childAttachmentExpression is not null && contentKind != XamlContentKind.Children)
        {
            throw new ArgumentException("A child attachment thunk requires Children content.", nameof(childAttachmentExpression));
        }

        if (contentAttachmentExpression is not null && contentKind != XamlContentKind.SingleContent)
        {
            throw new ArgumentException("A content attachment thunk requires SingleContent content.", nameof(contentAttachmentExpression));
        }

        if (childAttachmentMember is not null && contentKind != XamlContentKind.Children)
        {
            throw new ArgumentException("A child attachment member requires Children content.", nameof(childAttachmentMember));
        }

        if (contentAttachmentMember is not null && contentKind != XamlContentKind.SingleContent)
        {
            throw new ArgumentException("A content attachment member requires SingleContent content.", nameof(contentAttachmentMember));
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

    /// <summary>Direct instance method used by an attributed custom children owner.</summary>
    internal string? ChildAttachmentMember { get; }

    /// <summary>Direct instance method used by an attributed custom content owner.</summary>
    internal string? ContentAttachmentMember { get; }

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
    string? StringFormat,
    string? CultureName,
    XamlBindingSourceKind SourceKind = XamlBindingSourceKind.Context,
    string? SourceArgument = null);

internal readonly record struct XamlMultiBindingSourcePlan(
    XamlBindingSourceKind SourceKind,
    string? SourceArgument,
    string Path);

internal readonly record struct XamlMultiBindingPlan(
    ImmutableArray<XamlMultiBindingSourcePlan> Sources,
    string? FunctionKey,
    string? StringFormat,
    string? CultureName);

internal sealed record XamlBindingDefinition(
    string Path,
    string SourceTypeName,
    string ValueTypeName,
    string ReadExpression,
    string? WriteExpression,
    string? ConverterKey = null,
    string? CollectionItemTypeName = null);

internal sealed record XamlTemplateSelectorDefinition(
    string Key,
    string ItemTypeName,
    string SelectExpression,
    bool PassByIn,
    ImmutableArray<string> TemplateKeys);

internal sealed record XamlBindingFunctionDefinition(
    string Key,
    string MethodExpression,
    ImmutableArray<string> ParameterTypeNames,
    string ReturnTypeName);

internal sealed record XamlBehaviorDefinition(
    string Key,
    string PlanTypeName,
    string StateTypeName);

internal readonly record struct XamlValuePlan(
    XamlValueKind Kind,
    XamlLiteralValue Literal,
    XamlResourceReferencePlan Resource,
    XamlBindingPlan Binding,
    XamlMultiBindingPlan MultiBinding = default)
{
    internal static XamlValuePlan FromLiteral(XamlLiteralValue literal) =>
        new(literal.Kind, literal, default, default);

    internal static XamlValuePlan FromResource(XamlResourceReferencePlan resource) =>
        new(XamlValueKind.ResourceReference, default, resource, default);

    internal static XamlValuePlan FromBinding(XamlBindingPlan binding) =>
        new(XamlValueKind.Binding, default, default, binding);

    internal static XamlValuePlan FromItemsSource(XamlBindingPlan binding) =>
        new(XamlValueKind.ItemsSource, default, default, binding);

    internal static XamlValuePlan FromMultiBinding(XamlMultiBindingPlan binding) =>
        new(XamlValueKind.MultiBinding, default, default, default, binding);
}

internal readonly record struct XamlMemberPlan(
    UiPropertyId Property,
    string Name,
    XamlValuePlan Value,
    SourceRange Range,
    string? AttachedPropertyExpression = null);

internal sealed record XamlObjectPlan(
    UiTypeId Type,
    XamlQualifiedName Name,
    string? ScopeName,
    SourceRange Range,
    ImmutableArray<XamlMemberPlan> Members,
    ImmutableArray<XamlObjectPlan> Children,
    ImmutableArray<XamlTextSpanPlan> TextSpans = default);

internal readonly record struct XamlTextSpanPlan(
    string Text,
    string FontKey,
    float FontSize,
    string Color,
    Guid Command,
    string? Argument,
    SourceRange Range);

internal sealed record XamlResourcePlan(
    UiResourceId Id,
    string Key,
    XamlObjectPlan Value,
    SourceRange Range);

internal sealed record XamlScalarResourcePlan(
    UiResourceId Id,
    string Key,
    XamlValuePlan Value,
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
    SourceRange Range,
    string? ItemTypeName = null);

internal sealed record XamlTriggerPlan(
    string TargetName,
    ImmutableArray<XamlMultiBindingSourcePlan> Sources,
    ImmutableArray<string> ExpectedValues,
    string TargetProperty,
    string SetValue,
    string? Action,
    string? Argument,
    SourceRange Range);

internal sealed record XamlBehaviorPlan(
    string TargetName,
    string Key,
    SourceRange Range);

internal sealed record XamlDocumentPlan(
    SourceId Source,
    string? BindingSourceTypeName,
    XamlObjectPlan? Root,
    ImmutableArray<XamlResourcePlan> Resources,
    ImmutableArray<XamlScalarResourcePlan> ScalarResources,
    ImmutableArray<XamlStylePlan> Styles,
    ImmutableArray<XamlTemplatePlan> Templates,
    ImmutableArray<XamlTriggerPlan> Triggers,
    ImmutableArray<XamlBehaviorPlan> Behaviors,
    ImmutableArray<XamlResourceSlotPlan> ResourceSlots,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal bool Success =>
        (Root is not null || Resources.Length != 0 || ScalarResources.Length != 0 || Styles.Length != 0 || Templates.Length != 0) &&
        Diagnostics.All(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

internal sealed class XamlSemanticRegistry
{
    private readonly Dictionary<XamlQualifiedName, XamlTypeDefinition> _types = new();
    private readonly Dictionary<UiTypeId, XamlQualifiedName> _typeIds = new();
    private readonly Dictionary<UiTypeId, XamlTypeDefinition> _definitions = new();
    private readonly Dictionary<string, UiResourceId> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<UiResourceId, string> _resourceIds = new();
    private readonly Dictionary<(string SourceType, string Path, string? Converter), XamlBindingDefinition> _bindings = new();
    private readonly Dictionary<(string Path, string? Converter), XamlBindingDefinition> _bindingsByPath = new();
    private readonly Dictionary<string, XamlTemplateSelectorDefinition> _templateSelectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XamlBindingFunctionDefinition> _bindingFunctions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XamlBehaviorDefinition> _behaviors = new(StringComparer.Ordinal);
    private readonly Dictionary<XamlQualifiedName, XamlPropertyDefinition> _attachedProperties = new();

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

    internal void RegisterAttachedProperty(in XamlQualifiedName name, XamlPropertyDefinition definition)
    {
        if (!definition.Id.IsValid || string.IsNullOrWhiteSpace(name.LocalName) ||
            string.IsNullOrWhiteSpace(definition.AttachedPropertyExpression))
        {
            throw new ArgumentException("An attached property requires stable identity, XAML name, and a direct descriptor expression.", nameof(definition));
        }

        if (!_attachedProperties.TryAdd(name, definition))
        {
            throw new ArgumentException($"The attached property '{name.LocalName}' is registered twice.", nameof(definition));
        }
    }

    internal bool TryResolveAttachedProperty(in XamlQualifiedName name, out XamlPropertyDefinition definition) =>
        _attachedProperties.TryGetValue(name, out definition);

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

        var key = (NormalizeTypeName(definition.SourceTypeName), definition.Path, definition.ConverterKey);
        if (_bindings.TryGetValue(key, out var existing))
        {
            if (existing == definition)
            {
                return;
            }

            throw new ArgumentException($"The XAML binding path '{definition.Path}' is registered with conflicting definitions for '{definition.SourceTypeName}'.", nameof(definition));
        }

        if (!_bindings.TryAdd(key, definition))
        {
            throw new ArgumentException($"The XAML binding path '{definition.Path}' is registered twice for '{definition.SourceTypeName}'.", nameof(definition));
        }

        _bindingsByPath.TryAdd((definition.Path, definition.ConverterKey), definition);
    }

    internal bool TryResolveBindingVariant(
        string path,
        string? converterKey,
        [NotNullWhen(true)] out XamlBindingDefinition? definition) =>
        _bindingsByPath.TryGetValue((path, converterKey), out definition);

    internal bool TryResolveBinding(string path, [NotNullWhen(true)] out XamlBindingDefinition? definition) =>
        TryResolveBindingVariant(path, null, out definition);

    internal bool TryResolveBindingVariant(
        string sourceTypeName,
        string path,
        string? converterKey,
        [NotNullWhen(true)] out XamlBindingDefinition? definition) =>
        _bindings.TryGetValue((NormalizeTypeName(sourceTypeName), path, converterKey), out definition);

    internal bool TryResolveBinding(
        string sourceTypeName,
        string path,
        [NotNullWhen(true)] out XamlBindingDefinition? definition) =>
        TryResolveBindingVariant(sourceTypeName, path, null, out definition);

    internal void RegisterTemplateSelector(XamlTemplateSelectorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ItemTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.SelectExpression);
        if (!_templateSelectors.TryAdd(definition.Key, definition))
        {
            throw new ArgumentException($"The XAML template selector '{definition.Key}' is registered twice.", nameof(definition));
        }
    }

    internal bool TryResolveTemplateSelector(
        string key,
        [NotNullWhen(true)] out XamlTemplateSelectorDefinition? definition) =>
        _templateSelectors.TryGetValue(key, out definition);

    internal void RegisterBindingFunction(XamlBindingFunctionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key);
        if (!_bindingFunctions.TryAdd(definition.Key, definition))
        {
            throw new ArgumentException($"The XAML binding function '{definition.Key}' is registered twice.", nameof(definition));
        }
    }

    internal bool TryResolveBindingFunction(
        string key,
        [NotNullWhen(true)] out XamlBindingFunctionDefinition? definition) =>
        _bindingFunctions.TryGetValue(key, out definition);

    internal void RegisterBehavior(XamlBehaviorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key);
        if (!_behaviors.TryAdd(definition.Key, definition))
        {
            throw new ArgumentException($"The XAML behavior '{definition.Key}' is registered twice.", nameof(definition));
        }
    }

    internal bool TryResolveBehavior(
        string key,
        [NotNullWhen(true)] out XamlBehaviorDefinition? definition) =>
        _behaviors.TryGetValue(key, out definition);

    private static string NormalizeTypeName(string sourceTypeName) =>
        sourceTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? sourceTypeName[8..]
            : sourceTypeName;

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
            Property("TemplateKey", "10000000-0000-4000-8000-000000000009", XamlValueKind.String));
        common = common.AddRange(ImmutableArray.Create(
            Property("BackgroundBrush", "10000000-0000-4000-8000-00000000000A", XamlValueKind.Brush),
            Property("CornerRadius", "10000000-0000-4000-8000-000000000013", XamlValueKind.CornerRadii),
            Property("AutomationName", "10000000-0000-4000-8000-00000000000B", XamlValueKind.String),
            Property("AutomationRole", "10000000-0000-4000-8000-00000000000C", XamlValueKind.Enum),
            Property("Gestures", "10000000-0000-4000-8000-00000000000D", XamlValueKind.Enum),
            Property("Command", "10000000-0000-4000-8000-00000000000E", XamlValueKind.String),
            Property("CommandKey", "10000000-0000-4000-8000-00000000000F", XamlValueKind.String),
            Property("IsFocusScope", "10000000-0000-4000-8000-000000000010", XamlValueKind.Boolean)));
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
        Register(registry, "Slider", "22222222-2222-2222-2222-22222222220E", XamlContentKind.None,
            common.AddRange(ImmutableArray.Create(
                Property("Value", "70000000-0000-4000-8000-000000000001", XamlValueKind.Double),
                Property("Minimum", "70000000-0000-4000-8000-000000000002", XamlValueKind.Double),
                Property("Maximum", "70000000-0000-4000-8000-000000000003", XamlValueKind.Double),
                Property("Step", "70000000-0000-4000-8000-000000000004", XamlValueKind.Double),
                Property("Orientation", "70000000-0000-4000-8000-000000000005", XamlValueKind.Enum))));
        Register(registry, "Image", "22222222-2222-2222-2222-22222222220F", XamlContentKind.None,
            common.AddRange(ImmutableArray.Create(
                Property("Source", "71000000-0000-4000-8000-000000000001", XamlValueKind.ResourceId),
                Property("Tint", "71000000-0000-4000-8000-000000000002", XamlValueKind.Color),
                Property("Stretch", "71000000-0000-4000-8000-000000000003", XamlValueKind.Enum),
                Property("Placeholder", "71000000-0000-4000-8000-000000000004", XamlValueKind.ResourceId),
                Property("ErrorSource", "71000000-0000-4000-8000-000000000005", XamlValueKind.ResourceId))));
        Register(registry, "Overlay", "22222222-2222-2222-2222-222222222210", XamlContentKind.Children,
            common.Add(Property("IsOpen", "72000000-0000-4000-8000-000000000001", XamlValueKind.Boolean)));
        Register(registry, "CollectionView", "22222222-2222-2222-2222-222222222211", XamlContentKind.None,
            common.AddRange(ImmutableArray.Create(
                Property("SelectedIndex", "72000000-0000-4000-8000-000000000002", XamlValueKind.Integer),
                Property("ItemsSource", "73000000-0000-4000-8000-000000000001", XamlValueKind.ItemsSource),
                Property("ItemTemplate", "73000000-0000-4000-8000-000000000002", XamlValueKind.String),
                Property("ItemTemplateSelector", "73000000-0000-4000-8000-000000000005", XamlValueKind.String),
                Property("VirtualizationStart", "73000000-0000-4000-8000-000000000003", XamlValueKind.Integer),
                Property("VirtualizationCount", "73000000-0000-4000-8000-000000000004", XamlValueKind.Integer),
                Property("ItemExtent", "73000000-0000-4000-8000-000000000006", XamlValueKind.Single))));
        Register(registry, "Picker", "22222222-2222-2222-2222-222222222212", XamlContentKind.None,
            common.AddRange(ImmutableArray.Create(
                Property("SelectedIndex", "72000000-0000-4000-8000-000000000003", XamlValueKind.Integer),
                Property("IsOpen", "72000000-0000-4000-8000-000000000004", XamlValueKind.Boolean),
                Property("ItemsSource", "73000000-0000-4000-8000-000000000007", XamlValueKind.ItemsSource),
                Property("ItemTemplate", "73000000-0000-4000-8000-000000000008", XamlValueKind.String),
                Property("ItemTemplateSelector", "73000000-0000-4000-8000-000000000009", XamlValueKind.String),
                Property("VirtualizationStart", "73000000-0000-4000-8000-00000000000A", XamlValueKind.Integer),
                Property("VirtualizationCount", "73000000-0000-4000-8000-00000000000B", XamlValueKind.Integer),
                Property("ItemExtent", "73000000-0000-4000-8000-00000000000C", XamlValueKind.Single))));
        Register(registry, "TabView", "22222222-2222-2222-2222-222222222213", XamlContentKind.None,
            common.Add(Property("SelectedIndex", "72000000-0000-4000-8000-000000000005", XamlValueKind.Integer)));
        Register(registry, "Menu", "22222222-2222-2222-2222-222222222214", XamlContentKind.None, common);
        Register(registry, "RichTextBlock", "22222222-2222-2222-2222-222222222215", XamlContentKind.None, common);
        RegisterAttached(registry, "Grid.Row", "60000000-0000-4000-8000-000000000002", XamlValueKind.Integer, "global::Delta.XAML.UiGridAttachedProperties.Row");
        RegisterAttached(registry, "Grid.Column", "60000000-0000-4000-8000-000000000003", XamlValueKind.Integer, "global::Delta.XAML.UiGridAttachedProperties.Column");
        RegisterAttached(registry, "Grid.RowSpan", "60000000-0000-4000-8000-000000000004", XamlValueKind.Integer, "global::Delta.XAML.UiGridAttachedProperties.RowSpan");
        RegisterAttached(registry, "Grid.ColumnSpan", "60000000-0000-4000-8000-000000000005", XamlValueKind.Integer, "global::Delta.XAML.UiGridAttachedProperties.ColumnSpan");
        registry.RegisterType(new(
            new UiTypeId(Guid.Parse("22222222-2222-2222-2222-22222222220D")),
            new XamlQualifiedName(string.Empty, "ResourceDictionary"),
            XamlContentKind.Children,
            ImmutableArray<XamlPropertyDefinition>.Empty));
        return registry;
    }

    private static XamlPropertyDefinition Property(string name, string id, XamlValueKind kind) =>
        new(new UiPropertyId(Guid.Parse(id)), name, kind);

    private static void RegisterAttached(
        XamlSemanticRegistry registry,
        string name,
        string id,
        XamlValueKind kind,
        string descriptorExpression) =>
        registry.RegisterAttachedProperty(
            new XamlQualifiedName(string.Empty, name),
            new XamlPropertyDefinition(
                new UiPropertyId(Guid.Parse(id)),
                name,
                kind,
                AttachedPropertyExpression: descriptorExpression));

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
            name is "TextBlock" or "TextBox"
                ? $"new global::Delta.XAML.{name}()"
                : $"new global::Delta.XAML.Ui{name}()"));
}
