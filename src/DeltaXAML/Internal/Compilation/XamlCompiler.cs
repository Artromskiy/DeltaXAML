using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Delta.Diagnostics;
using Delta;
using Delta.XAML;
using Delta.XAML.Contract;

namespace DeltaXAML.Compiler;

internal static class XamlCompiler
{
    internal static XamlDocumentPlan Compile(
        SourceId source,
        string text,
        XamlSemanticRegistry registry,
        bool allowUnregisteredTypes = false,
        bool allowUnregisteredResources = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(registry);
        return new Parser(source, text, registry, allowUnregisteredTypes, allowUnregisteredResources).Parse();
    }

    private sealed class Parser
    {
        private readonly SourceId _source;
        private readonly string _text;
        private readonly XamlSemanticRegistry _registry;
        private readonly bool _allowUnregisteredTypes;
        private readonly bool _allowUnregisteredResources;
        private readonly ImmutableArray<Diagnostic>.Builder _diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        private readonly ImmutableArray<XamlResourcePlan>.Builder _resources = ImmutableArray.CreateBuilder<XamlResourcePlan>();
        private readonly ImmutableArray<XamlScalarResourcePlan>.Builder _scalarResources = ImmutableArray.CreateBuilder<XamlScalarResourcePlan>();
        private readonly ImmutableArray<XamlGradientResourcePlan>.Builder _gradientResources = ImmutableArray.CreateBuilder<XamlGradientResourcePlan>();
        private readonly ImmutableArray<XamlStylePlan>.Builder _styles = ImmutableArray.CreateBuilder<XamlStylePlan>();
        private readonly ImmutableArray<XamlTemplatePlan>.Builder _templates = ImmutableArray.CreateBuilder<XamlTemplatePlan>();
        private readonly ImmutableArray<XamlTriggerPlan>.Builder _triggers = ImmutableArray.CreateBuilder<XamlTriggerPlan>();
        private readonly ImmutableArray<XamlBehaviorPlan>.Builder _behaviors = ImmutableArray.CreateBuilder<XamlBehaviorPlan>();
        private readonly ImmutableArray<XamlEffectPlan>.Builder _effectResources = ImmutableArray.CreateBuilder<XamlEffectPlan>();
        private readonly List<XamlResourceSlotPlan> _resourceSlots = new();
        private readonly Dictionary<UiResourceId, int> _resourceSlotIndices = new();
        private readonly HashSet<(string Key, string? Variant)> _styleKeys = new();
        private readonly HashSet<string> _templateKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);
        private string? _bindingSourceTypeName;
        private int _offset;

        internal Parser(
            SourceId source,
            string text,
            XamlSemanticRegistry registry,
            bool allowUnregisteredTypes,
            bool allowUnregisteredResources)
        {
            _source = source;
            _text = text;
            _registry = registry;
            _allowUnregisteredTypes = allowUnregisteredTypes;
            _allowUnregisteredResources = allowUnregisteredResources;
        }

        internal XamlDocumentPlan Parse()
        {
            SkipTrivia();
            var root = _offset < _text.Length && Current == '<'
                ? ParseElement(new Dictionary<string, string>(StringComparer.Ordinal))
                : null;
            if (root is null && (_resources.Count != 0 || _scalarResources.Count != 0 || _gradientResources.Count != 0 || _styles.Count != 0 || _templates.Count != 0 || _effectResources.Count != 0) &&
                _registry.TryResolveType(new XamlQualifiedName(string.Empty, "ResourceDictionary"), out var resourceDictionary))
            {
                root = new XamlObjectPlan(resourceDictionary.Id, resourceDictionary.Name, null, Range(0, _offset), ImmutableArray<XamlMemberPlan>.Empty, ImmutableArray<XamlObjectPlan>.Empty);
            }

            if (root is null && _text.Length == 0)
            {
                Report("XAML000", "The XAML document is empty.", 0, 0);
            }

            SkipTrivia();
            while (_offset < _text.Length)
            {
                var start = _offset;
                if (Current == '<')
                {
                    ParseElement(new Dictionary<string, string>(StringComparer.Ordinal));
                    Report("XAML014", "Only one root element is allowed.", start, _offset);
                }
                else
                {
                    ReadText();
                    Report("XAML015", "Text outside the root element is not supported.", start, _offset);
                }

                SkipTrivia();
            }

            return new(
                _source,
                _bindingSourceTypeName,
                root,
                _resources.ToImmutable(),
                _scalarResources.ToImmutable(),
                _styles.ToImmutable(),
                _templates.ToImmutable(),
                _triggers.ToImmutable(),
                _behaviors.ToImmutable(),
                _resourceSlots.ToImmutableArray(),
                _diagnostics.ToImmutable(),
                _effectResources.ToImmutable(),
                _gradientResources.ToImmutable());
        }

        private XamlObjectPlan? ParseElement(Dictionary<string, string> parentNamespaces)
        {
            var elementStart = _offset;
            if (!Consume('<'))
            {
                return null;
            }

            if (Current == '/' || Current == '!' || Current == '?')
            {
                Report("XAML016", "An element name is required.", elementStart, Maths.Min(_offset + 1, _text.Length));
                RecoverToTagEnd();
                return null;
            }

            var nameStart = _offset;
            var lexicalName = ReadName();
            if (lexicalName.Length == 0)
            {
                Report("XAML016", "An element name is required.", nameStart, _offset);
                RecoverToTagEnd();
                return null;
            }

            var attributes = new List<AttributeSyntax>();
            var selfClosing = ParseStartTag(attributes, elementStart);
            var namespaces = new Dictionary<string, string>(parentNamespaces, StringComparer.Ordinal);
            foreach (var attribute in attributes)
            {
                if (attribute.IsDefaultNamespace)
                {
                    namespaces[string.Empty] = attribute.Value;
                }
                else if (attribute.Prefix == "xmlns")
                {
                    namespaces[attribute.LocalName] = attribute.Value;
                }
            }

            var name = ToQualifiedName(lexicalName, namespaces);
            if (lexicalName == "Style")
            {
                ParseStyle(attributes, selfClosing, elementStart, namespaces);
                return null;
            }

            if (lexicalName == "Resource")
            {
                ParseScalarResource(attributes, selfClosing, elementStart);
                return null;
            }

            if (lexicalName is "LinearGradientBrush" or "RadialGradientBrush")
            {
                ParseGradientResource(lexicalName, attributes, selfClosing, elementStart, namespaces);
                return null;
            }

            if (lexicalName == "Template")
            {
                ParseTemplate(attributes, selfClosing, elementStart, namespaces);
                return null;
            }

            if (lexicalName == "Trigger")
            {
                ParseTrigger(attributes, selfClosing, elementStart);
                return null;
            }

            if (lexicalName == "Behavior")
            {
                ParseBehavior(attributes, selfClosing, elementStart);
                return null;
            }

            if (lexicalName == "EffectSet")
            {
                var effect = ParseEffectSet(attributes, selfClosing, elementStart, UiEffectTarget.None, requireKey: true);
                if (effect is not null)
                {
                    _effectResources.Add(effect);
                }

                return null;
            }

            var hasType = _registry.TryResolveType(name, out var type);
            if (!hasType && !_allowUnregisteredTypes)
            {
                Report(
                    "XAML002",
                    GetUnsupportedElementMessage(lexicalName),
                    nameStart,
                    nameStart + lexicalName.Length);
            }

            var members = ImmutableArray.CreateBuilder<XamlMemberPlan>();
            string? resourceKey = null;
            string? scopeName = null;
            var resourceRange = Range(elementStart, _offset);
            foreach (var attribute in attributes)
            {
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.IsName)
                {
                    RegisterName(attribute.Value, attribute.Range);
                    scopeName = attribute.Value;
                    continue;
                }

                if (attribute.IsKey)
                {
                    resourceKey = attribute.Value;
                    resourceRange = attribute.Range;
                    continue;
                }

                if (attribute.IsDataType)
                {
                    if (string.IsNullOrWhiteSpace(attribute.Value))
                    {
                        Report("XAML034", "x:DataType requires a source type name.", attribute.Range);
                    }
                    else if (_bindingSourceTypeName is not null &&
                             !string.Equals(_bindingSourceTypeName, attribute.Value, StringComparison.Ordinal))
                    {
                        Report("XAML034", "One generated artifact cannot declare multiple binding source types.", attribute.Range);
                    }
                    else
                    {
                        _bindingSourceTypeName = attribute.Value;
                    }

                    continue;
                }

                var attributeNamespace = attribute.Prefix.Length != 0 && namespaces.TryGetValue(attribute.Prefix, out var namespaceUri)
                    ? namespaceUri
                    : string.Empty;
                if ((type is null || !type.TryGetProperty(attribute.LocalName, out var property)) &&
                    !_registry.TryResolveAttachedProperty(new XamlQualifiedName(attributeNamespace, attribute.LocalName), out property))
                {
                    Report(
                        "XAML003",
                        GetUnsupportedPropertyMessage(lexicalName, attribute.LocalName),
                        attribute.Range);
                    continue;
                }

                if (TryParseValue(attribute.Value, property.ValueKind, attribute.Range, out var value) &&
                    ValidatePlacementLiteral(property.Name, value, attribute.Range))
                {
                    members.Add(new(property.Id, property.Name, value, attribute.Range, property.AttachedPropertyExpression));
                }
            }

            var children = ImmutableArray.CreateBuilder<XamlObjectPlan>();
            var textSpans = ImmutableArray.CreateBuilder<XamlTextSpanPlan>();
            XamlEffectPlan? inlineEffect = null;
            if (!selfClosing)
            {
                if (string.Equals(name.LocalName, "RichTextBlock", StringComparison.Ordinal))
                {
                    ParseTextSpans(lexicalName, textSpans);
                }
                else
                {
                    ParseChildren(lexicalName, name, type, namespaces, children, ref inlineEffect);
                }
            }

            if (inlineEffect is not null && members.Any(static member => member.Name == "EffectSet"))
            {
                Report("XAML047", $"Element '{lexicalName}' cannot declare EffectSet both as an attribute and a property element.", inlineEffect.Range);
            }

            var elementRange = Range(elementStart, _offset);
            var plan = new XamlObjectPlan(
                type?.Id ?? default,
                name,
                scopeName,
                elementRange,
                members.ToImmutable(),
                children.ToImmutable(),
                textSpans.ToImmutable(),
                inlineEffect);
            if (resourceKey is not null)
            {
                if (!_registry.TryResolveResource(resourceKey, out var resourceId))
                {
                    resourceId = CreateResourceId(resourceKey);
                    _registry.RegisterResource(resourceKey, resourceId);
                }

                RegisterResourceSlot(resourceId, resourceKey, false);
                _resources.Add(new(resourceId, resourceKey, plan, resourceRange));

                return null;
            }

            return plan;
        }

        private void ParseScalarResource(List<AttributeSyntax> attributes, bool selfClosing, int elementStart)
        {
            string? key = null;
            string? typeName = null;
            string? value = null;
            var keyRange = Range(elementStart, _offset);
            var valueRange = keyRange;
            for (var i = 0; i < attributes.Count; i++)
            {
                var attribute = attributes[i];
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.IsKey)
                {
                    key = attribute.Value;
                    keyRange = attribute.Range;
                }
                else if (attribute.Prefix.Length == 0 && attribute.LocalName == "Type")
                {
                    typeName = attribute.Value;
                }
                else if (attribute.Prefix.Length == 0 && attribute.LocalName == "Value")
                {
                    value = attribute.Value;
                    valueRange = attribute.Range;
                }
                else
                {
                    Report("XAML035", $"Unsupported Resource attribute '{attribute.LocalName}'.", attribute.Range);
                }
            }

            if (!selfClosing)
            {
                Report("XAML035", "A scalar Resource must be self-closing.", Range(elementStart, _offset));
                SkipElementBody("Resource");
            }

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(typeName) || value is null)
            {
                Report("XAML035", "A scalar Resource requires x:Key, Type and Value.", Range(elementStart, _offset));
                return;
            }

            if (!TryScalarResourceKind(typeName, out var kind) ||
                !TryParseValue(value, kind, valueRange, out var valuePlan) ||
                valuePlan.Kind is XamlValueKind.Binding or XamlValueKind.ResourceReference)
            {
                Report("XAML035", $"Scalar Resource '{key}' has an unsupported type or value.", valueRange);
                return;
            }

            if (!_registry.TryResolveResource(key, out var resourceId))
            {
                resourceId = CreateResourceId(key);
                _registry.RegisterResource(key, resourceId);
            }

            RegisterResourceSlot(resourceId, key, false);
            _scalarResources.Add(new(resourceId, key, valuePlan, keyRange));
        }

        private void ParseGradientResource(
            string lexicalName,
            List<AttributeSyntax> attributes,
            bool selfClosing,
            int elementStart,
            Dictionary<string, string> namespaces)
        {
            string? key = null;
            XamlValuePlan? angle = null;
            XamlValuePlan? startPoint = null;
            XamlValuePlan? endPoint = null;
            XamlValuePlan? center = null;
            XamlValuePlan? radius = null;
            XamlValuePlan? outlineColor = null;
            XamlValuePlan? outlineWidth = null;
            var keyRange = Range(elementStart, _offset);
            foreach (var attribute in attributes)
            {
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.IsKey)
                {
                    key = attribute.Value;
                    keyRange = attribute.Range;
                    continue;
                }

                var expected = attribute.LocalName switch
                {
                    "Angle" => XamlValueKind.Single,
                    "StartPoint" or "EndPoint" or "Center" => XamlValueKind.Vector2,
                    "Radius" or "OutlineWidth" => XamlValueKind.Single,
                    "OutlineColor" => XamlValueKind.Color,
                    _ => XamlValueKind.Invalid,
                };
                if (attribute.Prefix.Length != 0 || expected == XamlValueKind.Invalid)
                {
                    Report("XAML050", $"Unsupported {lexicalName} attribute '{attribute.LocalName}'.", attribute.Range);
                    continue;
                }

                if (!TryParseValue(attribute.Value, expected, attribute.Range, out var value))
                {
                    continue;
                }

                if ((expected != XamlValueKind.Color && value.Kind != expected) ||
                    (expected == XamlValueKind.Color && value.Kind is XamlValueKind.Binding or XamlValueKind.MultiBinding) ||
                    (value.Kind == XamlValueKind.ResourceReference && value.Resource.IsDynamic))
                {
                    Report("XAML057", expected == XamlValueKind.Color
                        ? "Gradient outline colors support StaticResource only."
                        : $"Gradient geometry attribute '{attribute.LocalName}' must be a literal.", attribute.Range);
                    continue;
                }

                switch (attribute.LocalName)
                {
                    case "Angle": angle = value; break;
                    case "StartPoint": startPoint = value; break;
                    case "EndPoint": endPoint = value; break;
                    case "Center": center = value; break;
                    case "Radius": radius = value; break;
                    case "OutlineColor": outlineColor = value; break;
                    case "OutlineWidth": outlineWidth = value; break;
                }
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                Report("XAML051", $"{lexicalName} requires a non-empty x:Key.", Range(elementStart, _offset));
                if (!selfClosing)
                {
                    SkipElementBody(lexicalName);
                }

                return;
            }

            var hasExistingResource = _registry.TryResolveResource(key, out var existingResourceId);
            if (hasExistingResource)
            {
                Report("XAML031", $"Resource key '{key}' is declared more than once.", keyRange);
            }

            var resourceId = hasExistingResource ? existingResourceId : CreateResourceId(key);
            if (!hasExistingResource)
            {
                _registry.RegisterResource(key, resourceId);
            }
            RegisterResourceSlot(resourceId, key, false);
            var payloadId = CreateGradientPayloadId(key);
            var stops = ImmutableArray.CreateBuilder<XamlGradientStopPlan>();
            if (!selfClosing)
            {
                var closed = false;
                while (_offset < _text.Length)
                {
                    if (StartsWith("</"))
                    {
                        var closeStart = _offset;
                        var closeName = ParseEndElement();
                        if (!string.Equals(closeName, lexicalName, StringComparison.Ordinal))
                        {
                            Report("XAML013", $"Closing element '{closeName}' does not match '{lexicalName}'.", closeStart, _offset);
                        }

                        closed = true;
                        break;
                    }

                    if (Current == '<' && StartsWith("<GradientStop", StringComparison.Ordinal))
                    {
                        ParseGradientStop(stops);
                        continue;
                    }

                    if (Current == '<')
                    {
                        var unexpected = ParseElement(namespaces);
                        if (unexpected is not null)
                        {
                            Report("XAML050", $"Only GradientStop children are supported in {lexicalName}.", unexpected.Range);
                        }

                        continue;
                    }

                    ReadText();
                }

                if (!closed)
                {
                    Report("XAML012", $"Element '{lexicalName}' is not closed.", _offset, _offset);
                }
            }

            if (stops.Count < 2)
            {
                Report("XAML052", $"{lexicalName} requires at least two GradientStop children.", Range(elementStart, _offset));
            }
            else
            {
                var previousOffset = -1f;
                for (var stopIndex = 0; stopIndex < stops.Count; stopIndex++)
                {
                    if (stops[stopIndex].Offset < previousOffset)
                    {
                        Report("XAML059", "GradientStop offsets must be ordered from 0 to 1.", stops[stopIndex].Range);
                    }

                    previousOffset = stops[stopIndex].Offset;
                }
            }

            if (lexicalName == "LinearGradientBrush" && angle is not null && (startPoint is not null || endPoint is not null))
            {
                Report("XAML053", "LinearGradientBrush cannot combine Angle with StartPoint or EndPoint.", Range(elementStart, _offset));
            }

            if (lexicalName == "LinearGradientBrush" && ((startPoint is null) != (endPoint is null)))
            {
                Report("XAML054", "LinearGradientBrush requires both StartPoint and EndPoint.", Range(elementStart, _offset));
            }

            if (lexicalName == "RadialGradientBrush" && (center is null || radius is null))
            {
                Report("XAML055", "RadialGradientBrush requires Center and Radius.", Range(elementStart, _offset));
            }
            else if (lexicalName == "RadialGradientBrush" && (outlineColor is not null || outlineWidth is not null))
            {
                Report("XAML060", "RadialGradientBrush outline is not supported by the current renderer contract.", Range(elementStart, _offset));
            }

            if (radius is { } radiusPlan && radiusPlan.Kind == XamlValueKind.Single &&
                (!float.TryParse(radiusPlan.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var radiusValue) || radiusValue <= 0))
            {
                Report("XAML061", "RadialGradientBrush Radius must be positive.", Range(elementStart, _offset));
            }

            if (outlineWidth is { } outlineWidthPlan && outlineWidthPlan.Kind == XamlValueKind.Single &&
                float.TryParse(outlineWidthPlan.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var outlineWidthValue) && outlineWidthValue < 0)
            {
                Report("XAML062", "Gradient OutlineWidth must be non-negative.", Range(elementStart, _offset));
            }

            _gradientResources.Add(new(
                resourceId,
                payloadId,
                key,
                lexicalName == "LinearGradientBrush" ? XamlGradientKind.Linear : XamlGradientKind.Radial,
                angle,
                startPoint,
                endPoint,
                center,
                radius,
                outlineColor,
                outlineWidth,
                stops.ToImmutable(),
                Range(elementStart, _offset)));
        }

        private void ParseGradientStop(ImmutableArray<XamlGradientStopPlan>.Builder stops)
        {
            var start = _offset;
            Consume('<');
            var name = ReadName();
            var attributes = new List<AttributeSyntax>();
            var selfClosing = ParseStartTag(attributes, start);
            if (!string.Equals(name, "GradientStop", StringComparison.Ordinal))
            {
                Report("XAML050", "Only GradientStop children are supported in a gradient brush.", start, _offset);
                return;
            }

            float? offset = null;
            SourceRange offsetRange = Range(start, _offset);
            XamlValuePlan? color = null;
            foreach (var attribute in attributes)
            {
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.Prefix.Length != 0)
                {
                    Report("XAML050", $"Unsupported GradientStop attribute '{attribute.LocalName}'.", attribute.Range);
                    continue;
                }

                if (attribute.LocalName == "Offset")
                {
                    offsetRange = attribute.Range;
                    if (float.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                        float.IsFinite(parsed) && parsed is >= 0 and <= 1)
                    {
                        offset = parsed;
                    }
                    else
                    {
                        Report("XAML056", $"GradientStop offset '{attribute.Value}' must be finite and between 0 and 1.", attribute.Range);
                    }
                }
                else if (attribute.LocalName == "Color" && TryParseValue(attribute.Value, XamlValueKind.Color, attribute.Range, out var parsedColor))
                {
                    if (parsedColor.Kind is XamlValueKind.Binding or XamlValueKind.MultiBinding ||
                        parsedColor.Kind == XamlValueKind.ResourceReference && parsedColor.Resource.IsDynamic)
                    {
                        Report("XAML057", "GradientStop colors support StaticResource only.", attribute.Range);
                    }
                    else
                    {
                        color = parsedColor;
                    }
                }
                else
                {
                    Report("XAML050", $"Unsupported GradientStop attribute '{attribute.LocalName}'.", attribute.Range);
                }
            }

            if (!selfClosing)
            {
                Report("XAML058", "GradientStop must be self-closing.", Range(start, _offset));
                RecoverToTagEnd();
            }

            if (offset is { } position && color is { } colorPlan)
            {
                stops.Add(new(position, colorPlan, offsetRange));
            }
        }

        private void SkipElementBody(string lexicalName)
        {
            while (_offset < _text.Length)
            {
                if (StartsWith("</"))
                {
                    var closeName = ParseEndElement();
                    if (!string.Equals(closeName, lexicalName, StringComparison.Ordinal))
                    {
                        Report("XAML013", $"Closing element '{closeName}' does not match '{lexicalName}'.", _offset, _offset);
                    }

                    return;
                }

                if (Current == '<')
                {
                    ParseElement(new Dictionary<string, string>(StringComparer.Ordinal));
                }
                else
                {
                    ReadText();
                }
            }
        }

        private static bool TryScalarResourceKind(string typeName, out XamlValueKind kind)
        {
            kind = typeName switch
            {
                "Text" or "String" => XamlValueKind.String,
                "Boolean" => XamlValueKind.Boolean,
                "Real32" or "Single" => XamlValueKind.Single,
                "Real64" or "Double" => XamlValueKind.Double,
                "Color" => XamlValueKind.Color,
                "Brush" => XamlValueKind.Brush,
                "Thickness" => XamlValueKind.Thickness,
                _ => XamlValueKind.Invalid,
            };
            return kind != XamlValueKind.Invalid;
        }

        private void ParseStyle(
            List<AttributeSyntax> attributes,
            bool selfClosing,
            int elementStart,
            Dictionary<string, string> namespaces)
        {
            string? key = null;
            var keyRange = Range(elementStart, _offset);
            string? targetType = null;
            string? basedOn = null;
            string? variant = null;
            foreach (var attribute in attributes)
            {
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.IsKey)
                {
                    key = attribute.Value;
                    keyRange = attribute.Range;
                }
                else if (attribute.Prefix.Length == 0 && attribute.LocalName == "TargetType")
                {
                    targetType = attribute.Value;
                }
                else if (attribute.Prefix.Length == 0 && attribute.LocalName == "BasedOn")
                {
                    basedOn = attribute.Value;
                }
                else if (attribute.Prefix.Length == 0 && attribute.LocalName == "Variant")
                {
                    variant = attribute.Value;
                }
                else
                {
                    Report("XAML021", $"Unsupported Style attribute '{attribute.LocalName}'.", attribute.Range);
                }
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                Report("XAML022", "A Style requires a non-empty x:Key.", Range(elementStart, _offset));
            }

            if (string.IsNullOrWhiteSpace(targetType))
            {
                Report("XAML023", "A Style requires a non-empty TargetType.", Range(elementStart, _offset));
            }

            if (basedOn is not null && string.IsNullOrWhiteSpace(basedOn))
            {
                Report("XAML036", "Style BasedOn requires a non-empty style key.", Range(elementStart, _offset));
                basedOn = null;
            }

            if (variant is not null && string.IsNullOrWhiteSpace(variant))
            {
                Report("XAML037", "Style Variant requires a non-empty name.", Range(elementStart, _offset));
                variant = null;
            }

            var setters = ImmutableArray.CreateBuilder<XamlMemberPlan>();
            var visualStates = ImmutableArray.CreateBuilder<XamlVisualStatePlan>();
            var stateNames = new HashSet<XamlVisualStateName>();
            var closed = selfClosing;
            if (!selfClosing)
            {
                while (_offset < _text.Length)
                {
                    if (StartsWith("</"))
                    {
                        var closeStart = _offset;
                        var closeName = ParseEndElement();
                        if (!string.Equals(closeName, "Style", StringComparison.Ordinal))
                        {
                            Report("XAML013", $"Closing element '{closeName}' does not match 'Style'.", closeStart, _offset);
                        }

                        closed = true;
                        break;
                    }

                    if (Current == '<')
                    {
                        if (_text.AsSpan(_offset).StartsWith("<Setter", StringComparison.Ordinal))
                        {
                            ParseSetter(targetType, namespaces, setters);
                        }
                        else if (_text.AsSpan(_offset).StartsWith("<VisualState", StringComparison.Ordinal))
                        {
                            ParseVisualState(targetType, namespaces, visualStates, stateNames);
                        }
                        else
                        {
                            var unexpected = ParseElement(namespaces);
                            if (unexpected is not null)
                            {
                                Report("XAML021", "Only Setter children are supported in a Style.", unexpected.Range);
                            }
                        }

                        continue;
                    }

                    var textStart = _offset;
                    ReadText();
                    if (!string.IsNullOrWhiteSpace(_text[textStart.._offset]))
                    {
                        Report("XAML004", "Text content is not supported in a Style.", textStart, _offset);
                    }
                }
            }

            if (!closed)
            {
                Report("XAML012", "Element 'Style' is not closed.", _offset, _offset);
            }

            if (key is not null && targetType is not null)
            {
                if (!_styleKeys.Add((key, variant)))
                {
                    Report("XAML031", $"Style '{key}' with variant '{variant ?? "default"}' is declared more than once.", keyRange);
                }
                else
                {
                    var qualifiedTargetType = ToQualifiedName(targetType, namespaces);
                    var targetTypeId = _registry.TryResolveType(qualifiedTargetType, out var targetDefinition)
                        ? targetDefinition.Id
                        : default;
                    _styles.Add(new(
                        key,
                        CreateStyleId(key, variant),
                        qualifiedTargetType,
                        targetTypeId,
                        setters.ToImmutable(),
                        visualStates.ToImmutable(),
                        Range(elementStart, _offset),
                        basedOn,
                        variant));
                }
            }
        }

        private void ParseSetter(
            string? targetType,
            Dictionary<string, string> namespaces,
            ImmutableArray<XamlMemberPlan>.Builder setters)
        {
            var start = _offset;
            Consume('<');
            var setterName = ReadName();
            var attributes = new List<AttributeSyntax>();
            var selfClosing = ParseStartTag(attributes, start);
            if (!string.Equals(setterName, "Setter", StringComparison.Ordinal))
            {
                Report("XAML021", "Only Setter elements are supported in a Style.", start, _offset);
                return;
            }

            string? propertyName = null;
            string? valueText = null;
            SourceRange valueRange = Range(start, _offset);
            foreach (var attribute in attributes)
            {
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.Prefix.Length == 0 && attribute.LocalName == "Property")
                {
                    propertyName = attribute.Value;
                }
                else if (attribute.Prefix.Length == 0 && attribute.LocalName == "Value")
                {
                    valueText = attribute.Value;
                    valueRange = attribute.Range;
                }
                else
                {
                    Report("XAML021", $"Unsupported Setter attribute '{attribute.LocalName}'.", attribute.Range);
                }
            }

            if (!selfClosing)
            {
                Report("XAML024", "Setter must be self-closing in the compiled dialect.", Range(start, _offset));
                RecoverToTagEnd();
            }

            if (string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(propertyName) || valueText is null)
            {
                Report("XAML025", "Setter requires Property and Value and its parent Style requires TargetType.", Range(start, _offset));
                return;
            }

            var qualifiedTarget = ToQualifiedName(targetType, namespaces);
            if (!_registry.TryResolveType(qualifiedTarget, out var type) || !type.TryGetProperty(propertyName, out var property))
            {
                Report("XAML003", $"Unsupported styled property '{propertyName}' on '{targetType}'.", valueRange);
                return;
            }

            if (TryParseValue(valueText, property.ValueKind, valueRange, out var value) &&
                ValidatePlacementLiteral(property.Name, value, valueRange))
            {
                setters.Add(new(property.Id, property.Name, value, valueRange));
            }
        }

        private void ParseVisualState(
            string? targetType,
            Dictionary<string, string> namespaces,
            ImmutableArray<XamlVisualStatePlan>.Builder states,
            HashSet<XamlVisualStateName> stateNames)
        {
            var start = _offset;
            Consume('<');
            var lexicalName = ReadName();
            var attributes = new List<AttributeSyntax>();
            var selfClosing = ParseStartTag(attributes, start);
            if (!string.Equals(lexicalName, "VisualState", StringComparison.Ordinal))
            {
                Report("XAML028", "Only VisualState elements are supported in a Style.", start, _offset);
                return;
            }

            XamlVisualStateName state = XamlVisualStateName.None;
            SourceRange stateRange = Range(start, _offset);
            foreach (var attribute in attributes)
            {
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.Prefix.Length == 0 && attribute.LocalName == "Name")
                {
                    stateRange = attribute.Range;
                    if (!TryParseVisualStateName(attribute.Value, out state))
                    {
                        Report("XAML029", $"VisualState '{attribute.Value}' is not a supported state name.", attribute.Range);
                    }

                    continue;
                }

                Report("XAML028", $"Unsupported VisualState attribute '{attribute.LocalName}'.", attribute.Range);
            }

            if (state == XamlVisualStateName.None)
            {
                Report("XAML030", "A VisualState requires a supported Name.", stateRange);
            }

            var setters = ImmutableArray.CreateBuilder<XamlMemberPlan>();
            var closed = selfClosing;
            if (!selfClosing)
            {
                while (_offset < _text.Length)
                {
                    if (StartsWith("</"))
                    {
                        var closeStart = _offset;
                        var closeName = ParseEndElement();
                        if (!string.Equals(closeName, "VisualState", StringComparison.Ordinal))
                        {
                            Report("XAML013", $"Closing element '{closeName}' does not match 'VisualState'.", closeStart, _offset);
                        }

                        closed = true;
                        break;
                    }

                    if (Current == '<')
                    {
                        if (_text.AsSpan(_offset).StartsWith("<Setter", StringComparison.Ordinal))
                        {
                            ParseSetter(targetType, namespaces, setters);
                        }
                        else
                        {
                            var unexpected = ParseElement(namespaces);
                            if (unexpected is not null)
                            {
                                Report("XAML028", "Only Setter children are supported in a VisualState.", unexpected.Range);
                            }
                        }

                        continue;
                    }

                    var textStart = _offset;
                    ReadText();
                    if (!string.IsNullOrWhiteSpace(_text[textStart.._offset]))
                    {
                        Report("XAML004", "Text content is not supported in a VisualState.", textStart, _offset);
                    }
                }
            }

            if (!closed)
            {
                Report("XAML012", "Element 'VisualState' is not closed.", _offset, _offset);
            }

            if (state is not (XamlVisualStateName.None or XamlVisualStateName.Unknown))
            {
                if (!stateNames.Add(state))
                {
                    Report("XAML032", $"VisualState '{state}' is declared more than once in the same Style.", stateRange);
                }
                else
                {
                    states.Add(new(state, setters.ToImmutable(), Range(start, _offset)));
                }
            }
        }

        private static bool TryParseVisualStateName(string value, out XamlVisualStateName state)
        {
            state = value switch
            {
                "Normal" => XamlVisualStateName.Normal,
                "Hover" => XamlVisualStateName.Hover,
                "Pressed" => XamlVisualStateName.Pressed,
                "Focused" => XamlVisualStateName.Focused,
                "Disabled" => XamlVisualStateName.Disabled,
                "Invalid" => XamlVisualStateName.Invalid,
                "Selected" => XamlVisualStateName.Selected,
                _ => XamlVisualStateName.Unknown,
            };
            return state is not (XamlVisualStateName.None or XamlVisualStateName.Unknown);
        }

        private void ParseTemplate(
            List<AttributeSyntax> attributes,
            bool selfClosing,
            int elementStart,
            Dictionary<string, string> namespaces)
        {
            string? key = null;
            string? itemTypeName = null;
            var keyRange = Range(elementStart, _offset);
            foreach (var attribute in attributes)
            {
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.IsKey)
                {
                    key = attribute.Value;
                    keyRange = attribute.Range;
                }
                else if (attribute.IsDataType && !string.IsNullOrWhiteSpace(attribute.Value))
                {
                    itemTypeName = attribute.Value;
                }
                else
                {
                    Report("XAML026", $"Unsupported Template attribute '{attribute.LocalName}'.", attribute.Range);
                }
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                Report("XAML027", "A Template requires a non-empty x:Key.", Range(elementStart, _offset));
            }

            XamlObjectPlan? root = null;
            var closed = selfClosing;
            if (!selfClosing)
            {
                SkipWhitespace();
                if (Current == '<')
                {
                    root = ParseElement(namespaces);
                }
                else if (_offset < _text.Length)
                {
                    var textStart = _offset;
                    ReadText();
                    if (!string.IsNullOrWhiteSpace(_text[textStart.._offset]))
                    {
                        Report("XAML004", "Text content is not supported in a Template.", textStart, _offset);
                    }
                }

                SkipWhitespace();
                if (StartsWith("</"))
                {
                    var closeStart = _offset;
                    var closeName = ParseEndElement();
                    if (!string.Equals(closeName, "Template", StringComparison.Ordinal))
                    {
                        Report("XAML013", $"Closing element '{closeName}' does not match 'Template'.", closeStart, _offset);
                    }

                    closed = true;
                }
            }

            if (!closed)
            {
                Report("XAML012", "Element 'Template' is not closed.", _offset, _offset);
            }

            if (key is not null && root is not null)
            {
                if (!_templateKeys.Add(key))
                {
                    Report("XAML033", $"Template key '{key}' is declared more than once.", keyRange);
                }
                else
                {
                    _templates.Add(new(key, CreateTemplateId(key), root, Range(elementStart, _offset), itemTypeName));
                }
            }
        }

        private static UiTemplateId CreateTemplateId(string key)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("DeltaXAML.Template/" + key));
            return new UiTemplateId(new Guid(bytes.AsSpan(0, 16)));
        }

        private void ParseTrigger(
            List<AttributeSyntax> attributes,
            bool selfClosing,
            int elementStart)
        {
            string? target = null;
            string? sourcesText = null;
            string? valuesText = null;
            string? property = null;
            string? setValue = null;
            string? action = null;
            string? argument = null;
            for (var i = 0; i < attributes.Count; i++)
            {
                var attribute = attributes[i];
                if (attribute.IsNamespace)
                {
                    continue;
                }

                switch (attribute.LocalName)
                {
                    case "Target": target = attribute.Value; break;
                    case "Sources": sourcesText = attribute.Value; break;
                    case "Values": valuesText = attribute.Value; break;
                    case "Property": property = attribute.Value; break;
                    case "Set": setValue = attribute.Value; break;
                    case "Action": action = attribute.Value; break;
                    case "Argument": argument = attribute.Value; break;
                    default: Report("XAML043", $"Unsupported Trigger attribute '{attribute.LocalName}'.", attribute.Range); break;
                }
            }

            if (!selfClosing)
            {
                Report("XAML043", "Trigger must be self-closing in the compiled dialect.", Range(elementStart, _offset));
                SkipElementBody("Trigger");
            }

            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(sourcesText) ||
                string.IsNullOrWhiteSpace(valuesText) || string.IsNullOrWhiteSpace(property) || setValue is null)
            {
                Report("XAML044", "Trigger requires Target, Sources, Values, Property and Set.", Range(elementStart, _offset));
                return;
            }

            var sourceTokens = sourcesText.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var expected = valuesText.Split('|', StringSplitOptions.TrimEntries);
            if (sourceTokens.Length == 0 || sourceTokens.Length != expected.Length)
            {
                Report("XAML044", "Trigger Sources and Values must contain the same non-zero number of entries.", Range(elementStart, _offset));
                return;
            }

            var sources = ImmutableArray.CreateBuilder<XamlMultiBindingSourcePlan>(sourceTokens.Length);
            for (var i = 0; i < sourceTokens.Length; i++)
            {
                if (!TryParseMultiBindingSource(sourceTokens[i], out var source))
                {
                    Report("XAML044", $"Trigger source '{sourceTokens[i]}' is not a typed relation path.", Range(elementStart, _offset));
                    return;
                }

                sources.Add(source);
            }

            _triggers.Add(new(
                target,
                sources.ToImmutable(),
                expected.ToImmutableArray(),
                property,
                setValue,
                action,
                argument,
                Range(elementStart, _offset)));
        }

        private void ParseBehavior(List<AttributeSyntax> attributes, bool selfClosing, int elementStart)
        {
            string? target = null;
            string? plan = null;
            for (var i = 0; i < attributes.Count; i++)
            {
                var attribute = attributes[i];
                if (attribute.IsNamespace)
                {
                    continue;
                }

                switch (attribute.LocalName)
                {
                    case "Target": target = attribute.Value; break;
                    case "Plan": plan = attribute.Value; break;
                    default: Report("XAML045", $"Unsupported Behavior attribute '{attribute.LocalName}'.", attribute.Range); break;
                }
            }

            if (!selfClosing)
            {
                Report("XAML045", "Behavior must be self-closing in the compiled dialect.", Range(elementStart, _offset));
                SkipElementBody("Behavior");
            }

            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(plan))
            {
                Report("XAML046", "Behavior requires Target and Plan.", Range(elementStart, _offset));
                return;
            }

            _behaviors.Add(new(target, plan, Range(elementStart, _offset)));
        }

        private UiResourceId CreateResourceId(string key)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"DeltaXAML.Resource/{_source.Value:D}/{key}"));
            return new UiResourceId(new Guid(bytes.AsSpan(0, 16)));
        }

        private UiResourceId CreateGradientPayloadId(string key)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"DeltaXAML.Gradient/{_source.Value:D}/{key}"));
            return new UiResourceId(new Guid(bytes.AsSpan(0, 16)));
        }

        private UiResourceId CreateInlineEffectId(int elementStart)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"DeltaXAML.Effect/{_source.Value:D}/{elementStart.ToString(CultureInfo.InvariantCulture)}"));
            return new UiResourceId(new Guid(bytes.AsSpan(0, 16)));
        }

        private static UiStyleId CreateStyleId(string key, string? variant = null)
        {
            var identity = variant is null ? key : key + "/" + variant;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("DeltaXAML.Style/" + identity));
            return new UiStyleId(new Guid(bytes.AsSpan(0, 16)));
        }

        private int RegisterResourceSlot(UiResourceId id, string key, bool isDynamic)
        {
            if (_resourceSlotIndices.TryGetValue(id, out var existing))
            {
                if (isDynamic && !_resourceSlots[existing].IsDynamic)
                {
                    _resourceSlots[existing] = _resourceSlots[existing] with { IsDynamic = true };
                }

                return existing;
            }

            var index = _resourceSlots.Count;
            _resourceSlotIndices.Add(id, index);
            _resourceSlots.Add(new(id, key, index, isDynamic));
            return index;
        }

        private void ParseChildren(
            string lexicalParent,
            XamlQualifiedName parent,
            XamlTypeDefinition? type,
            Dictionary<string, string> namespaces,
            ImmutableArray<XamlObjectPlan>.Builder children,
            ref XamlEffectPlan? inlineEffect)
        {
            var closed = false;
            while (_offset < _text.Length)
            {
                if (StartsWith("</"))
                {
                    var closeStart = _offset;
                    var closeName = ParseEndElement();
                    if (!string.Equals(closeName, lexicalParent, StringComparison.Ordinal))
                    {
                        Report("XAML013", $"Closing element '{closeName}' does not match '{lexicalParent}'.", closeStart, _offset);
                    }

                    closed = true;
                    break;
                }

                if (Current == '<')
                {
                    if (StartsWith("<!--"))
                    {
                        SkipComment();
                        continue;
                    }

                    if (StartsWith("<?"))
                    {
                        SkipProcessingInstruction();
                        continue;
                    }

                    if (string.Equals(PeekStartElementName(), lexicalParent + ".EffectSet", StringComparison.Ordinal))
                    {
                        var target = IsTextElement(parent.LocalName) ? UiEffectTarget.Text : UiEffectTarget.Visual;
                        var parsed = ParseEffectPropertyElement(lexicalParent, target);
                        if (inlineEffect is not null)
                        {
                            Report("XAML047", $"Element '{lexicalParent}' declares EffectSet more than once.", parsed?.Range ?? Range(start: _offset, end: _offset));
                        }
                        else
                        {
                            inlineEffect = parsed;
                        }

                        continue;
                    }

                    var child = ParseElement(namespaces);
                    if (child is not null)
                    {
                        children.Add(child);
                        if (type?.ContentKind == XamlContentKind.None)
                        {
                            Report("XAML004", $"Element '{lexicalParent}' does not accept child elements.", child.Range);
                        }
                        else if (type?.ContentKind == XamlContentKind.SingleContent && children.Count > 1)
                        {
                            Report("XAML005", $"Element '{lexicalParent}' accepts only one child.", child.Range);
                        }
                    }

                    continue;
                }

                var textStart = _offset;
                ReadText();
                if (!string.IsNullOrWhiteSpace(_text[textStart.._offset]))
                {
                    Report("XAML004", $"Text content is not supported in '{parent.LocalName}'.", textStart, _offset);
                }
            }

            if (!closed)
            {
                Report("XAML012", $"Element '{lexicalParent}' is not closed.", _offset, _offset);
            }
        }

        private XamlEffectPlan? ParseEffectPropertyElement(string lexicalOwner, UiEffectTarget target)
        {
            var propertyStart = _offset;
            Consume('<');
            var propertyName = ReadName();
            var attributes = new List<AttributeSyntax>();
            var selfClosing = ParseStartTag(attributes, propertyStart);
            for (var i = 0; i < attributes.Count; i++)
            {
                if (!attributes[i].IsNamespace)
                {
                    Report("XAML047", $"Property element '{propertyName}' does not accept attributes.", attributes[i].Range);
                }
            }

            if (selfClosing)
            {
                Report("XAML047", $"Property element '{propertyName}' requires one EffectSet value.", Range(propertyStart, _offset));
                return null;
            }

            XamlEffectPlan? effect = null;
            var closed = false;
            while (_offset < _text.Length)
            {
                SkipWhitespace();
                if (StartsWith("<!--"))
                {
                    SkipComment();
                    continue;
                }

                if (StartsWith("</"))
                {
                    var closeStart = _offset;
                    var closeName = ParseEndElement();
                    if (!string.Equals(closeName, propertyName, StringComparison.Ordinal))
                    {
                        Report("XAML013", $"Closing element '{closeName}' does not match '{propertyName}'.", closeStart, _offset);
                    }

                    closed = true;
                    break;
                }

                if (Current != '<')
                {
                    var textStart = _offset;
                    ReadText();
                    if (!string.IsNullOrWhiteSpace(_text[textStart.._offset]))
                    {
                        Report("XAML047", $"Property element '{propertyName}' accepts only one EffectSet value.", textStart, _offset);
                    }

                    continue;
                }

                var effectStart = _offset;
                Consume('<');
                var childName = ReadName();
                var childAttributes = new List<AttributeSyntax>();
                var childSelfClosing = ParseStartTag(childAttributes, effectStart);
                if (!string.Equals(childName, "EffectSet", StringComparison.Ordinal))
                {
                    Report("XAML047", $"Property element '{propertyName}' accepts EffectSet, not '{childName}'.", effectStart, _offset);
                    if (!childSelfClosing)
                    {
                        SkipElementBody(childName);
                    }

                    continue;
                }

                var parsed = ParseEffectSet(childAttributes, childSelfClosing, effectStart, target, requireKey: false);
                if (effect is not null)
                {
                    Report("XAML047", $"Property element '{propertyName}' accepts exactly one EffectSet.", parsed?.Range ?? Range(effectStart, _offset));
                }
                else
                {
                    effect = parsed;
                }
            }

            if (!closed)
            {
                Report("XAML012", $"Element '{lexicalOwner}.EffectSet' is not closed.", _offset, _offset);
            }

            return effect;
        }

        private XamlEffectPlan? ParseEffectSet(
            List<AttributeSyntax> attributes,
            bool selfClosing,
            int elementStart,
            UiEffectTarget inferredTarget,
            bool requireKey)
        {
            string? key = null;
            var target = inferredTarget;
            var quality = UiEffectQuality.Analytic;
            var units = PaintUnits.Logical;
            XamlValuePlan? outsets = null;
            var cachedMask = UiResourceId.Empty;
            for (var i = 0; i < attributes.Count; i++)
            {
                var attribute = attributes[i];
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.IsKey)
                {
                    key = attribute.Value;
                    continue;
                }

                switch (attribute.LocalName)
                {
                    case "Target" when Enum.TryParse(attribute.Value, true, out UiEffectTarget parsedTarget) &&
                                            parsedTarget is UiEffectTarget.Visual or UiEffectTarget.Text:
                        if (inferredTarget != UiEffectTarget.None && inferredTarget != parsedTarget)
                        {
                            Report("XAML047", $"Inline EffectSet target must be {inferredTarget} for this element.", attribute.Range);
                        }
                        else
                        {
                            target = parsedTarget;
                        }

                        break;
                    case "Quality" when Enum.TryParse(attribute.Value, true, out UiEffectQuality parsedQuality):
                        quality = parsedQuality;
                        break;
                    case "Units" when Enum.TryParse(attribute.Value, true, out PaintUnits parsedUnits) &&
                                        parsedUnits is PaintUnits.Logical or PaintUnits.Device:
                        units = parsedUnits;
                        break;
                    case "Outsets":
                        if (TryParseValue(attribute.Value, XamlValueKind.Thickness, attribute.Range, out var parsedOutsets) &&
                            parsedOutsets.Kind != XamlValueKind.Binding)
                        {
                            outsets = parsedOutsets;
                        }
                        else if (parsedOutsets.Kind == XamlValueKind.Binding)
                        {
                            Report("XAML047", "EffectSet Outsets cannot be bound; they are derived from bound layer parameters.", attribute.Range);
                        }

                        break;
                    case "CachedMask" when Guid.TryParse(attribute.Value, out var mask) && mask != Guid.Empty:
                        cachedMask = new UiResourceId(mask);
                        break;
                    default:
                        Report("XAML047", $"Unsupported EffectSet attribute '{attribute.LocalName}'.", attribute.Range);
                        break;
                }
            }

            if (requireKey && string.IsNullOrWhiteSpace(key))
            {
                Report("XAML047", "A reusable EffectSet requires x:Key.", Range(elementStart, _offset));
            }

            if (target is not (UiEffectTarget.Visual or UiEffectTarget.Text))
            {
                Report("XAML047", "A reusable EffectSet requires Target=\"Visual\" or Target=\"Text\".", Range(elementStart, _offset));
            }

            var layers = ImmutableArray.CreateBuilder<XamlEffectLayerPlan>();
            if (!selfClosing)
            {
                ParseEffectLayers(target, layers);
            }

            if (layers.Count == 0)
            {
                Report("XAML047", "EffectSet requires at least one effect layer.", Range(elementStart, _offset));
                return null;
            }

            var seen = new HashSet<XamlEffectLayerKind>();
            for (var i = 0; i < layers.Count; i++)
            {
                if (!seen.Add(layers[i].Kind))
                {
                    Report("XAML047", $"EffectSet declares '{layers[i].Kind}' more than once.", layers[i].Range);
                }
            }

            var resource = key is null ? CreateInlineEffectId(elementStart) : CreateResourceId(key);
            if (key is not null)
            {
                if (!_registry.TryResolveResource(key, out _))
                {
                    _registry.RegisterResource(key, resource);
                }

                RegisterResourceSlot(resource, key, false);
            }

            return new(
                resource,
                key,
                target,
                quality,
                units,
                outsets,
                cachedMask,
                layers.ToImmutable(),
                Range(elementStart, _offset));
        }

        private void ParseEffectLayers(
            UiEffectTarget target,
            ImmutableArray<XamlEffectLayerPlan>.Builder layers)
        {
            var closed = false;
            while (_offset < _text.Length)
            {
                SkipWhitespace();
                if (StartsWith("<!--"))
                {
                    SkipComment();
                    continue;
                }

                if (StartsWith("</"))
                {
                    var closeStart = _offset;
                    var closeName = ParseEndElement();
                    if (!string.Equals(closeName, "EffectSet", StringComparison.Ordinal))
                    {
                        Report("XAML013", $"Closing element '{closeName}' does not match 'EffectSet'.", closeStart, _offset);
                    }

                    closed = true;
                    break;
                }

                if (Current != '<')
                {
                    var textStart = _offset;
                    ReadText();
                    if (!string.IsNullOrWhiteSpace(_text[textStart.._offset]))
                    {
                        Report("XAML047", "EffectSet accepts only effect layer elements.", textStart, _offset);
                    }

                    continue;
                }

                var layerStart = _offset;
                Consume('<');
                var layerName = ReadName();
                var attributes = new List<AttributeSyntax>();
                var selfClosing = ParseStartTag(attributes, layerStart);
                if (!Enum.TryParse<XamlEffectLayerKind>(layerName, false, out var kind) ||
                    !IsLayerAllowed(target, kind))
                {
                    Report("XAML047", $"Effect layer '{layerName}' is not valid for target '{target}'.", layerStart, _offset);
                    if (!selfClosing)
                    {
                        SkipElementBody(layerName);
                    }

                    continue;
                }

                if (!selfClosing)
                {
                    Report("XAML047", $"Effect layer '{layerName}' must be self-closing.", Range(layerStart, _offset));
                    SkipElementBody(layerName);
                    continue;
                }

                var members = ParseEffectLayerMembers(kind, attributes);
                layers.Add(new(kind, members, Range(layerStart, _offset)));
            }

            if (!closed)
            {
                Report("XAML012", "Element 'EffectSet' is not closed.", _offset, _offset);
            }
        }

        private ImmutableArray<XamlEffectMemberPlan> ParseEffectLayerMembers(
            XamlEffectLayerKind kind,
            List<AttributeSyntax> attributes)
        {
            var members = ImmutableArray.CreateBuilder<XamlEffectMemberPlan>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < attributes.Count; i++)
            {
                var attribute = attributes[i];
                if (attribute.IsNamespace)
                {
                    continue;
                }

                var name = attribute.LocalName == "BlurRadius" ? "Radius" : attribute.LocalName;
                if (!seen.Add(name))
                {
                    Report("XAML047", $"Effect layer '{kind}' declares '{name}' more than once.", attribute.Range);
                    continue;
                }

                var expected = name switch
                {
                    "Color" => XamlValueKind.Color,
                    "Offset" => XamlValueKind.Vector2,
                    "Width" or "Radius" or "Spread" or "Intensity" => XamlValueKind.Single,
                    _ => XamlValueKind.Invalid,
                };
                if (expected == XamlValueKind.Invalid || !IsLayerMemberAllowed(kind, name))
                {
                    Report("XAML047", $"Effect layer '{kind}' does not support '{attribute.LocalName}'.", attribute.Range);
                    continue;
                }

                if (!TryParseValue(attribute.Value, expected, attribute.Range, out var value))
                {
                    continue;
                }

                if (value.Kind == XamlValueKind.MultiBinding || value.Kind == XamlValueKind.ResourceReference ||
                    (value.Kind == XamlValueKind.Binding && value.Binding.SourceKind != XamlBindingSourceKind.Context))
                {
                    Report("XAML047", $"Effect layer '{kind}.{name}' supports literals and generated Context bindings only.", attribute.Range);
                    continue;
                }

                if (value.Kind == XamlValueKind.Binding && value.Binding.Mode == UiBindingMode.TwoWay)
                {
                    Report("XAML047", $"Effect layer '{kind}.{name}' is paint output and cannot use TwoWay binding.", attribute.Range);
                    continue;
                }

                if (value.Kind == XamlValueKind.Single && name != "Offset" &&
                    float.TryParse(value.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number < 0)
                {
                    Report("XAML047", $"Effect layer '{kind}.{name}' must be non-negative.", attribute.Range);
                    continue;
                }

                members.Add(new(name, value, attribute.Range));
            }

            RequireEffectMember(kind, "Color", seen, attributes);
            if (kind == XamlEffectLayerKind.Stroke)
            {
                RequireEffectMember(kind, "Width", seen, attributes);
            }
            else
            {
                RequireEffectMember(kind, "Radius", seen, attributes);
            }

            return members.ToImmutable();
        }

        private void RequireEffectMember(
            XamlEffectLayerKind kind,
            string name,
            HashSet<string> members,
            List<AttributeSyntax> attributes)
        {
            if (!members.Contains(name))
            {
                var range = attributes.Count == 0 ? Range(_offset, _offset) : attributes[0].Range;
                Report("XAML047", $"Effect layer '{kind}' requires '{name}'.", range);
            }
        }

        private static bool IsLayerAllowed(UiEffectTarget target, XamlEffectLayerKind kind) =>
            (target is UiEffectTarget.Visual or UiEffectTarget.Text) &&
            (kind is XamlEffectLayerKind.Stroke or XamlEffectLayerKind.OuterShadow or
                XamlEffectLayerKind.InnerShadow or XamlEffectLayerKind.OuterGlow or
                XamlEffectLayerKind.InnerGlow);

        private static bool IsLayerMemberAllowed(XamlEffectLayerKind kind, string name) => name switch
        {
            "Color" or "Intensity" => true,
            "Width" => kind == XamlEffectLayerKind.Stroke,
            "Offset" or "Radius" or "Spread" => kind is XamlEffectLayerKind.OuterShadow or
                XamlEffectLayerKind.InnerShadow or XamlEffectLayerKind.OuterGlow or
                XamlEffectLayerKind.InnerGlow,
            _ => false,
        };

        private static bool IsTextElement(string name) => name is
            "TextBlock" or "TextBox" or "NumericEditor" or "RichTextBlock";

        private void ParseTextSpans(
            string lexicalParent,
            ImmutableArray<XamlTextSpanPlan>.Builder spans)
        {
            var closed = false;
            while (_offset < _text.Length)
            {
                SkipWhitespace();
                if (StartsWith("</"))
                {
                    var closeStart = _offset;
                    var closeName = ParseEndElement();
                    if (!string.Equals(closeName, lexicalParent, StringComparison.Ordinal))
                    {
                        Report("XAML013", $"Closing element '{closeName}' does not match '{lexicalParent}'.", closeStart, _offset);
                    }

                    closed = true;
                    break;
                }

                var start = _offset;
                if (!Consume('<'))
                {
                    ReadText();
                    if (!string.IsNullOrWhiteSpace(_text[start.._offset]))
                    {
                        Report("XAML040", "RichTextBlock accepts only Span elements.", start, _offset);
                    }

                    continue;
                }

                var name = ReadName();
                var attributes = new List<AttributeSyntax>();
                var selfClosing = ParseStartTag(attributes, start);
                if (!string.Equals(name, "Span", StringComparison.Ordinal) || !selfClosing)
                {
                    Report("XAML040", "RichTextBlock accepts self-closing Span elements only.", start, _offset);
                    if (string.Equals(name, "Span", StringComparison.Ordinal) && !selfClosing)
                    {
                        Report("XAML042", "Span requires a Text property.", start, _offset);
                    }

                    if (!selfClosing)
                    {
                        SkipElementBody(name);
                    }

                    continue;
                }

                string? text = null;
                var fontKey = "default";
                var fontSize = 14f;
                var color = "#FFFFFFFF";
                var command = Guid.Empty;
                string? argument = null;
                var decorations = UiTextDecorations.None;
                for (var i = 0; i < attributes.Count; i++)
                {
                    var attribute = attributes[i];
                    switch (attribute.LocalName)
                    {
                        case "Text": text = attribute.Value; break;
                        case "FontKey" when !string.IsNullOrWhiteSpace(attribute.Value): fontKey = attribute.Value; break;
                        case "FontSize" when float.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && float.IsFinite(size) && size > 0: fontSize = size; break;
                        case "Foreground" when TryColor(attribute.Value, out _): color = attribute.Value; break;
                        case "Command" when Guid.TryParse(attribute.Value, out var parsed) && parsed != Guid.Empty: command = parsed; break;
                        case "Argument": argument = attribute.Value; break;
                        case "TextDecorations" when TryTextDecorations(attribute.Value, out var parsedDecorations): decorations = parsedDecorations; break;
                        default: Report("XAML041", $"Unsupported Span property '{attribute.LocalName}'.", attribute.Range); break;
                    }
                }

                if (text is null)
                {
                    Report("XAML042", "Span requires a Text value.", start, _offset);
                    continue;
                }

                spans.Add(new(text, fontKey, fontSize, color, command, argument, Range(start, _offset), decorations));
            }

            if (!closed)
            {
                Report("XAML012", $"Element '{lexicalParent}' is not closed.", _offset, _offset);
            }
        }

        private bool ParseStartTag(List<AttributeSyntax> attributes, int elementStart)
        {
            while (_offset < _text.Length)
            {
                SkipWhitespace();
                if (StartsWith("/>", StringComparison.Ordinal))
                {
                    _offset += 2;
                    return true;
                }

                if (Consume('>'))
                {
                    return false;
                }

                if (_offset >= _text.Length)
                {
                    Report("XAML001", "The start tag is not closed.", elementStart, _offset);
                    return true;
                }

                var attributeStart = _offset;
                var lexicalName = ReadName();
                if (lexicalName.Length == 0)
                {
                    Report("XAML017", "An attribute name is required.", attributeStart, Maths.Min(attributeStart + 1, _text.Length));
                    RecoverAttribute();
                    continue;
                }

                SkipWhitespace();
                if (!Consume('='))
                {
                    Report("XAML018", $"Attribute '{lexicalName}' must have a value.", attributeStart, _offset);
                    RecoverAttribute();
                    continue;
                }

                SkipWhitespace();
                var quote = Current;
                if (quote is not ('"' or '\''))
                {
                    Report("XAML019", $"Attribute '{lexicalName}' must use a quoted value.", attributeStart, _offset);
                    var unquotedStart = _offset;
                    while (_offset < _text.Length && !char.IsWhiteSpace(Current) && Current != '>')
                    {
                        _offset++;
                    }

                    attributes.Add(CreateAttribute(lexicalName, attributeStart, _offset, WebUtility.HtmlDecode(_text[unquotedStart.._offset])));
                    continue;
                }

                _offset++;
                var valueStart = _offset;
                while (_offset < _text.Length && Current != quote)
                {
                    _offset++;
                }

                var valueEnd = _offset;
                if (_offset >= _text.Length)
                {
                    Report("XAML001", $"Attribute '{lexicalName}' is not closed.", attributeStart, _offset);
                    attributes.Add(CreateAttribute(lexicalName, valueStart, valueEnd, WebUtility.HtmlDecode(_text[valueStart..valueEnd])));
                    return true;
                }

                _offset++;
                attributes.Add(CreateAttribute(lexicalName, attributeStart, _offset, WebUtility.HtmlDecode(_text[valueStart..valueEnd])));
            }

            Report("XAML001", "The start tag is not closed.", elementStart, _offset);
            return true;
        }

        private string ParseEndElement()
        {
            _offset += 2;
            var name = ReadName();
            SkipWhitespace();
            if (!Consume('>'))
            {
                Report("XAML020", "The closing tag is not closed.", _offset, Maths.Min(_offset + 1, _text.Length));
                RecoverToTagEnd();
            }

            return name;
        }

        private bool TryParseValue(
            string value,
            XamlValueKind expected,
            SourceRange range,
            out XamlValuePlan plan)
        {
            if (TryParseResource(value, out var resource))
            {
                if (!_registry.TryResolveResource(resource.Key, out var id) && !_allowUnregisteredResources)
                {
                    Report("XAML006", $"Resource '{resource.Key}' has no registered stable identity.", range);
                    plan = default;
                    return false;
                }

                if (!_registry.TryResolveResource(resource.Key, out id))
                {
                    id = CreateResourceId(resource.Key);
                    _registry.RegisterResource(resource.Key, id);
                }

                plan = XamlValuePlan.FromResource(new(id, resource.Key, resource.IsDynamic, RegisterResourceSlot(id, resource.Key, resource.IsDynamic)));
                return true;
            }

            if (TryParseBinding(value, out var binding, out var bindingError))
            {
                plan = expected == XamlValueKind.ItemsSource
                    ? XamlValuePlan.FromItemsSource(binding)
                    : XamlValuePlan.FromBinding(binding);
                return true;
            }

            if (TryParseTemplateBinding(value, out binding, out bindingError))
            {
                plan = XamlValuePlan.FromBinding(binding);
                return true;
            }
            if (TryParseMultiBinding(value, out var multiBinding, out bindingError))
            {
                plan = XamlValuePlan.FromMultiBinding(multiBinding);
                return true;
            }

            if (bindingError is not null)
            {
                Report("XAML008", bindingError, range);
                plan = default;
                return false;
            }

            if (value.StartsWith('{'))
            {
                var message = value.StartsWith("{x:Reference", StringComparison.Ordinal)
                    ? "The x:Reference markup extension is not supported; use a generated ElementName binding."
                    : $"Markup extension '{value}' is not supported.";
                Report("XAML008", message, range);
                plan = default;
                return false;
            }

            if (!TryCanonicalLiteral(value, expected, out var literal))
            {
                Report("XAML007", $"Value '{value}' is not valid for {expected}.", range);
                plan = default;
                return false;
            }

            plan = XamlValuePlan.FromLiteral(literal);
            return true;
        }

        private static string GetUnsupportedElementMessage(string lexicalName) => lexicalName switch
        {
            "Label" => "Unsupported element 'Label'; use TextBlock.",
            "Entry" => "Unsupported element 'Entry'; use TextBox.",
            "FormattedString" => "Unsupported element 'FormattedString'; use RichTextBlock with Span children.",
            "TapGestureRecognizer" => "Unsupported element 'TapGestureRecognizer'; use the Gestures property and a generated Command binding.",
            _ => $"Unsupported element '{lexicalName}'.",
        };

        private static string GetUnsupportedPropertyMessage(string lexicalName, string propertyName) => propertyName switch
        {
            "Click" => $"Unsupported property 'Click' on '{lexicalName}'; bind Command instead of an event-handler name.",
            "FormattedText" => $"Unsupported property 'FormattedText' on '{lexicalName}'; use RichTextBlock with Span children.",
            "GestureRecognizers" => $"Unsupported property 'GestureRecognizers' on '{lexicalName}'; use the Gestures property and generated command bindings.",
            "NativeAutomationPeer" => $"Unsupported property 'NativeAutomationPeer' on '{lexicalName}'; set neutral AutomationName and AutomationRole metadata.",
            _ => $"Unsupported property '{propertyName}' on '{lexicalName}'.",
        };

        private bool ValidatePlacementLiteral(string propertyName, XamlValuePlan value, SourceRange range)
        {
            if (propertyName is not ("Width" or "Height") || value.Kind != XamlValueKind.Single)
            {
                return true;
            }

            if (float.TryParse(value.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var dimension) &&
                global::DeltaXAML.Internal.ElementPlacementMixin.IsValidDimension(dimension))
            {
                return true;
            }

            Report("XAML007", $"Value '{value.Literal.CanonicalText}' is not a valid {propertyName}.", range);
            return false;
        }

        private static bool TryCanonicalLiteral(
            string value,
            XamlValueKind expected,
            out XamlLiteralValue literal)
        {
            switch (expected)
            {
                case XamlValueKind.String:
                case XamlValueKind.Enum:
                    if (value.Length != 0)
                    {
                        literal = new(expected, value);
                        return true;
                    }

                    break;
                case XamlValueKind.Boolean when bool.TryParse(value, out var boolean):
                    literal = new(expected, boolean ? "true" : "false");
                    return true;
                case XamlValueKind.Single when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) && float.IsFinite(single):
                    literal = new(expected, single.ToString("R", CultureInfo.InvariantCulture));
                    return true;
                case XamlValueKind.Double when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) && double.IsFinite(doubleValue):
                    literal = new(expected, doubleValue.ToString("R", CultureInfo.InvariantCulture));
                    return true;
                case XamlValueKind.Integer when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer):
                    literal = new(expected, integer.ToString(CultureInfo.InvariantCulture));
                    return true;
                case XamlValueKind.ResourceId when Guid.TryParse(value, out var resourceId) && resourceId != Guid.Empty:
                    literal = new(expected, resourceId.ToString("D"));
                    return true;
                case XamlValueKind.Brush when TryBrush(value, out var brush):
                    literal = new(expected, brush);
                    return true;
                case XamlValueKind.Color when TryColor(value, out var color):
                    literal = new(expected, color);
                    return true;
                case XamlValueKind.Thickness when TryThickness(value, out var thickness):
                    literal = new(expected, thickness);
                    return true;
                case XamlValueKind.Vector2 when TryVector2(value, out var vector):
                    literal = new(expected, vector);
                    return true;
                case XamlValueKind.CornerRadii when TryCornerRadii(value, out var cornerRadii):
                    literal = new(expected, cornerRadii);
                    return true;
                case XamlValueKind.GridLengthList when TryGridLengths(value, out var lengths):
                    literal = new(expected, lengths);
                    return true;
            }

            literal = default;
            return false;
        }

        private static bool TryParseResource(string value, out ResourceSyntax resource)
        {
            resource = default;
            var isDynamic = value.StartsWith("{DynamicResource ", StringComparison.Ordinal);
            var isStatic = value.StartsWith("{StaticResource ", StringComparison.Ordinal);
            if ((!isDynamic && !isStatic) || !value.EndsWith('}'))
            {
                return false;
            }

            var prefixLength = isDynamic ? "{DynamicResource ".Length : "{StaticResource ".Length;
            var key = value[prefixLength..^1].Trim();
            if (key.Length == 0)
            {
                return false;
            }

            resource = new(key, isDynamic);
            return true;
        }

        private static bool TryParseBinding(
            string value,
            out XamlBindingPlan binding,
            out string? error)
        {
            binding = default;
            error = null;
            if (!value.StartsWith("{Binding", StringComparison.Ordinal))
            {
                return false;
            }

            if (!value.EndsWith('}'))
            {
                error = "Binding markup is not closed.";
                return false;
            }

            var body = value[8..^1].Trim();
            var path = string.Empty;
            var mode = UiBindingMode.OneWay;
            string? converter = null;
            string? format = null;
            string? culture = null;
            var sourceKind = XamlBindingSourceKind.Context;
            string? sourceArgument = null;
            var parts = body.Split(',', StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (part.Length == 0)
                {
                    continue;
                }

                var separator = part.IndexOf('=', StringComparison.Ordinal);
                if (separator < 0)
                {
                    if (path.Length != 0)
                    {
                        error = "Binding path is specified more than once.";
                        return false;
                    }

                    path = part;
                    continue;
                }

                var key = part[..separator].Trim();
                var option = part[(separator + 1)..].Trim();
                switch (key)
                {
                    case "Path" when path.Length == 0:
                        path = option;
                        break;
                    case "Mode" when Enum.TryParse(option, true, out mode):
                        break;
                    case "Converter" when option.Length != 0:
                        converter = option;
                        break;
                    case "StringFormat":
                        format = UnquoteMarkupOption(option);
                        break;
                    case "Culture" when option.Length != 0:
                        culture = option;
                        break;
                    case "Source" when TryParseBindingSource(option, out sourceKind, out sourceArgument):
                        break;
                    case "RelativeSource" when TryParseBindingSource(option, out sourceKind, out sourceArgument):
                        break;
                    case "ElementName" when option.Length != 0:
                        sourceKind = XamlBindingSourceKind.Name;
                        sourceArgument = option;
                        break;
                    default:
                        error = $"Binding option '{key}' is not supported or is invalid.";
                        return false;
                }
            }

            if (path.Length == 0)
            {
                error = "Binding path is required.";
                return false;
            }

            if (format is not null && culture is null)
            {
                error = "StringFormat requires an explicit Culture option.";
                return false;
            }

            if (format is not null && mode == UiBindingMode.TwoWay)
            {
                error = "A formatted generated binding cannot be TwoWay; bind the editable value separately.";
                return false;
            }

            binding = new(path, mode, converter, format, culture, sourceKind, sourceArgument);
            return true;
        }

        private static bool TryParseTemplateBinding(
            string value,
            out XamlBindingPlan binding,
            out string? error)
        {
            binding = default;
            error = null;
            const string prefix = "{TemplateBinding ";
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            if (!value.EndsWith('}'))
            {
                error = "TemplateBinding markup is not closed.";
                return false;
            }

            var path = value[prefix.Length..^1].Trim();
            if (path.Length == 0 || path.Contains(',', StringComparison.Ordinal))
            {
                error = "TemplateBinding requires exactly one property path.";
                return false;
            }

            binding = new(path, UiBindingMode.OneWay, null, null, null, XamlBindingSourceKind.TemplateOwner);
            return true;
        }

        private static bool TryParseMultiBinding(
            string value,
            out XamlMultiBindingPlan binding,
            out string? error)
        {
            binding = default;
            error = null;
            const string prefix = "{MultiBinding ";
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            if (!value.EndsWith('}'))
            {
                error = "MultiBinding markup is not closed.";
                return false;
            }

            string? sourcesText = null;
            string? function = null;
            string? format = null;
            string? culture = null;
            var parts = value[prefix.Length..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var separator = parts[i].IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    error = $"MultiBinding argument '{parts[i]}' must use name=value syntax.";
                    return false;
                }

                var key = parts[i][..separator].Trim();
                var argument = parts[i][(separator + 1)..].Trim().Trim('\'', '"');
                switch (key)
                {
                    case "Sources": sourcesText = argument; break;
                    case "Function": function = argument; break;
                    case "StringFormat": format = UnquoteMarkupOption(argument); break;
                    case "Culture": culture = argument; break;
                    default:
                        error = $"MultiBinding option '{key}' is not supported.";
                        return false;
                }
            }

            if (string.IsNullOrWhiteSpace(sourcesText) || (function is null) == (format is null))
            {
                error = "MultiBinding requires Sources and exactly one Function or StringFormat.";
                return false;
            }

            if (format is not null && string.IsNullOrWhiteSpace(culture))
            {
                error = "MultiBinding StringFormat requires an explicit Culture option.";
                return false;
            }

            var sources = ImmutableArray.CreateBuilder<XamlMultiBindingSourcePlan>();
            var sourceTokens = sourcesText.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < sourceTokens.Length; i++)
            {
                if (!TryParseMultiBindingSource(sourceTokens[i], out var source))
                {
                    error = $"MultiBinding source '{sourceTokens[i]}' must be Self.Property, TemplateOwner.Property, Ancestor:Type.Property or Name.Property.";
                    return false;
                }

                sources.Add(source);
            }

            if (sources.Count < 2)
            {
                error = "MultiBinding requires at least two typed sources.";
                return false;
            }

            binding = new(sources.ToImmutable(), function, format, culture);
            return true;
        }

        private static bool TryParseMultiBindingSource(
            string value,
            out XamlMultiBindingSourcePlan source)
        {
            var separator = value.LastIndexOf('.');
            if (separator <= 0 || separator == value.Length - 1)
            {
                source = default;
                return false;
            }

            var relation = value[..separator];
            var path = value[(separator + 1)..];
            if (string.Equals(relation, "Self", StringComparison.OrdinalIgnoreCase))
            {
                source = new(XamlBindingSourceKind.Self, null, path);
                return true;
            }

            if (string.Equals(relation, "TemplateOwner", StringComparison.OrdinalIgnoreCase))
            {
                source = new(XamlBindingSourceKind.TemplateOwner, null, path);
                return true;
            }

            if (string.Equals(relation, "Context", StringComparison.OrdinalIgnoreCase))
            {
                source = new(XamlBindingSourceKind.Context, null, path);
                return true;
            }

            const string ancestorPrefix = "Ancestor:";
            if (relation.StartsWith(ancestorPrefix, StringComparison.OrdinalIgnoreCase) && relation.Length > ancestorPrefix.Length)
            {
                source = new(XamlBindingSourceKind.Ancestor, relation[ancestorPrefix.Length..], path);
                return true;
            }

            source = new(XamlBindingSourceKind.Name, relation, path);
            return true;
        }

        private static bool TryParseBindingSource(
            string value,
            out XamlBindingSourceKind source,
            out string? argument)
        {
            argument = null;
            if (string.Equals(value, "Self", StringComparison.OrdinalIgnoreCase))
            {
                source = XamlBindingSourceKind.Self;
                return true;
            }

            if (string.Equals(value, "TemplateOwner", StringComparison.OrdinalIgnoreCase))
            {
                source = XamlBindingSourceKind.TemplateOwner;
                return true;
            }

            const string ancestorPrefix = "Ancestor:";
            if (value.StartsWith(ancestorPrefix, StringComparison.OrdinalIgnoreCase) &&
                value.Length > ancestorPrefix.Length)
            {
                source = XamlBindingSourceKind.Ancestor;
                argument = value[ancestorPrefix.Length..];
                return true;
            }

            source = XamlBindingSourceKind.Context;
            return false;
        }

        private static string UnquoteMarkupOption(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
            {
                return value[1..^1];
            }

            return value;
        }

        private void RegisterName(string name, SourceRange range)
        {
            if (!_names.Add(name))
            {
                Report("XAML010", $"The name '{name}' is declared more than once.", range);
            }
        }

        private AttributeSyntax CreateAttribute(string lexicalName, int start, int end, string value)
        {
            var separator = lexicalName.IndexOf(':', StringComparison.Ordinal);
            var prefix = separator < 0 ? string.Empty : lexicalName[..separator];
            var localName = separator < 0 ? lexicalName : lexicalName[(separator + 1)..];
            return new(
                prefix,
                localName,
                value,
                start,
                end,
                prefix == "xmlns" || lexicalName == "xmlns",
                prefix == "x" && localName == "Name",
                prefix == "x" && localName == "Key",
                prefix == "x" && localName == "DataType",
                Range(start, end));
        }

        private static XamlQualifiedName ToQualifiedName(string lexicalName, Dictionary<string, string> namespaces)
        {
            var separator = lexicalName.IndexOf(':', StringComparison.Ordinal);
            var prefix = separator < 0 ? string.Empty : lexicalName[..separator];
            var localName = separator < 0 ? lexicalName : lexicalName[(separator + 1)..];
            return new(namespaces.TryGetValue(prefix, out var uri) ? uri : string.Empty, localName);
        }

        private void SkipTrivia()
        {
            while (_offset < _text.Length)
            {
                SkipWhitespace();
                if (StartsWith("<!--"))
                {
                    SkipComment();
                    continue;
                }

                if (StartsWith("<?"))
                {
                    SkipProcessingInstruction();
                    continue;
                }

                break;
            }
        }

        private void SkipComment()
        {
            var start = _offset;
            var end = _text.IndexOf("-->", _offset + 4, StringComparison.Ordinal);
            if (end < 0)
            {
                _offset = _text.Length;
                Report("XAML001", "The comment is not closed.", start, _offset);
                return;
            }

            _offset = end + 3;
        }

        private void SkipProcessingInstruction()
        {
            var start = _offset;
            var end = _text.IndexOf("?>", _offset + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                _offset = _text.Length;
                Report("XAML001", "The processing instruction is not closed.", start, _offset);
                return;
            }

            _offset = end + 2;
        }

        private void ReadText()
        {
            while (_offset < _text.Length && Current != '<')
            {
                _offset++;
            }
        }

        private string ReadName()
        {
            var start = _offset;
            while (_offset < _text.Length && IsNameCharacter(Current))
            {
                _offset++;
            }

            return _text[start.._offset];
        }

        private string PeekStartElementName()
        {
            if (Current != '<' || StartsWith("</") || StartsWith("<!--") || StartsWith("<?"))
            {
                return string.Empty;
            }

            var start = _offset + 1;
            var end = start;
            while (end < _text.Length && IsNameCharacter(_text[end]))
            {
                end++;
            }

            return _text[start..end];
        }

        private void RecoverAttribute()
        {
            while (_offset < _text.Length && Current != '>' && !StartsWith("/>", StringComparison.Ordinal))
            {
                if (char.IsWhiteSpace(Current))
                {
                    return;
                }

                _offset++;
            }
        }

        private void RecoverToTagEnd()
        {
            while (_offset < _text.Length && Current != '>')
            {
                _offset++;
            }

            if (_offset < _text.Length)
            {
                _offset++;
            }
        }

        private void SkipWhitespace()
        {
            while (_offset < _text.Length && char.IsWhiteSpace(Current))
            {
                _offset++;
            }
        }

        private bool Consume(char value)
        {
            if (_offset >= _text.Length || Current != value)
            {
                return false;
            }

            _offset++;
            return true;
        }

        private bool StartsWith(string value, StringComparison comparison = StringComparison.Ordinal) =>
            _text.AsSpan(_offset).StartsWith(value, comparison);

        private char Current => _offset < _text.Length ? _text[_offset] : '\0';

        private SourceRange Range(int start, int end) =>
            new(_source, PositionAt(start), PositionAt(end));

        private void Report(string code, string message, int start, int end) =>
            _diagnostics.Add(new(new DiagnosticCode(code), DiagnosticSeverity.Error, message, Range(start, end)));

        private void Report(string code, string message, SourceRange range) =>
            _diagnostics.Add(new(new DiagnosticCode(code), DiagnosticSeverity.Error, message, range));

        private SourcePosition PositionAt(int offset)
        {
            var line = 0;
            var column = 0;
            var limit = Maths.Clamp(offset, 0, _text.Length);
            for (var i = 0; i < limit; i++)
            {
                if (_text[i] == '\n')
                {
                    line++;
                    column = 0;
                }
                else
                {
                    column++;
                }
            }

            return new(line, column, limit);
        }

        private static bool IsNameCharacter(char value) =>
            char.IsLetterOrDigit(value) || value is '_' or ':' or '-' or '.';

        private static bool TryColor(string value, out string canonical)
        {
            canonical = string.Empty;
            if (value.Length is not (7 or 9) || value[0] != '#')
            {
                return false;
            }

            for (var i = 1; i < value.Length; i++)
            {
                if (!Uri.IsHexDigit(value[i]))
                {
                    return false;
                }
            }

            canonical = value.ToUpperInvariant();
            return true;
        }

        private static bool TryBrush(string value, out string canonical)
        {
            if (TryColor(value, out canonical))
            {
                return true;
            }

            var separator = value.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || !Guid.TryParse(value[(separator + 1)..], out var resource) || resource == Guid.Empty)
            {
                canonical = string.Empty;
                return false;
            }

            var kind = value[..separator];
            if (kind is not ("LinearGradient" or "RadialGradient" or "Image"))
            {
                canonical = string.Empty;
                return false;
            }

            canonical = kind + ":" + resource.ToString("D");
            return true;
        }

        private static bool TryThickness(string value, out string canonical)
        {
            canonical = string.Empty;
            if (!ThicknessLiteralParser.TryParse(value, out var thickness))
            {
                return false;
            }

            canonical = string.Join(',',
                thickness.Left.ToString("R", CultureInfo.InvariantCulture),
                thickness.Top.ToString("R", CultureInfo.InvariantCulture),
                thickness.Right.ToString("R", CultureInfo.InvariantCulture),
                thickness.Bottom.ToString("R", CultureInfo.InvariantCulture));
            return true;
        }

        private static bool TryVector2(string value, out string canonical)
        {
            var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            canonical = string.Empty;
            if (parts.Length != 2 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !float.IsFinite(x) || !float.IsFinite(y))
            {
                return false;
            }

            canonical = string.Join(',',
                x.ToString("R", CultureInfo.InvariantCulture),
                y.ToString("R", CultureInfo.InvariantCulture));
            return true;
        }

        private static bool TryCornerRadii(string value, out string canonical)
        {
            var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            canonical = string.Empty;
            if (parts.Length is not (1 or 4))
            {
                return false;
            }

            var parsed = new float[parts.Length];
            for (var i = 0; i < parsed.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]) ||
                    !float.IsFinite(parsed[i]) || parsed[i] < 0)
                {
                    return false;
                }
            }

            canonical = string.Join(',', parsed.Select(static value => value.ToString("R", CultureInfo.InvariantCulture)));
            return true;
        }

        private static bool TryGridLengths(string value, out string canonical)
        {
            var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var normalized = new string[parts.Length];
            canonical = string.Empty;
            if (parts.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.Equals(part, "Auto", StringComparison.OrdinalIgnoreCase))
                {
                    normalized[i] = "Auto";
                }
                else if (part.EndsWith('*') &&
                         TryStarWeight(part.AsSpan(0, part.Length - 1), out var weight))
                {
                    normalized[i] = weight.ToString("R", CultureInfo.InvariantCulture) + "*";
                }
                else if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels) && float.IsFinite(pixels) && pixels >= 0)
                {
                    normalized[i] = pixels.ToString("R", CultureInfo.InvariantCulture);
                }
                else
                {
                    return false;
                }
            }

            canonical = string.Join(',', normalized);
            return true;
        }

        private static bool TryTextDecorations(string value, out UiTextDecorations decorations)
        {
            decorations = UiTextDecorations.None;
            var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                if (!Enum.TryParse(parts[i], true, out UiTextDecorations parsed) || parsed == UiTextDecorations.None)
                {
                    decorations = default;
                    return false;
                }

                decorations |= parsed;
            }

            return true;
        }

        private static bool TryStarWeight(ReadOnlySpan<char> text, out float weight)
        {
            if (text.IsEmpty)
            {
                weight = 1;
                return true;
            }

            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out weight) &&
                   float.IsFinite(weight) &&
                   weight > 0;
        }

        private readonly record struct AttributeSyntax(
            string Prefix,
            string LocalName,
            string Value,
            int Start,
            int End,
            bool IsNamespace,
            bool IsName,
            bool IsKey,
            bool IsDataType,
            SourceRange Range)
        {
            internal bool IsDefaultNamespace => Prefix.Length == 0 && LocalName == "xmlns";
        }

        private readonly record struct ResourceSyntax(string Key, bool IsDynamic);
    }
}
