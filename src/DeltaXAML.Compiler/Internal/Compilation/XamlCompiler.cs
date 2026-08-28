using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using Delta.Diagnostics;
using Delta.XAML;
using Delta.XAML.Contract;

namespace DeltaXAML.Compiler;

internal static class XamlCompiler
{
    internal static XamlDocumentPlan Compile(
        SourceId source,
        string text,
        XamlSemanticRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(registry);
        return new Parser(source, text, registry).Parse();
    }

    private sealed class Parser
    {
        private readonly SourceId _source;
        private readonly string _text;
        private readonly XamlSemanticRegistry _registry;
        private readonly ImmutableArray<Diagnostic>.Builder _diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        private readonly ImmutableArray<XamlResourcePlan>.Builder _resources = ImmutableArray.CreateBuilder<XamlResourcePlan>();
        private readonly ImmutableArray<XamlStylePlan>.Builder _styles = ImmutableArray.CreateBuilder<XamlStylePlan>();
        private readonly ImmutableArray<XamlTemplatePlan>.Builder _templates = ImmutableArray.CreateBuilder<XamlTemplatePlan>();
        private readonly List<XamlResourceSlotPlan> _resourceSlots = new();
        private readonly Dictionary<UiResourceId, int> _resourceSlotIndices = new();
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);
        private int _offset;

        internal Parser(SourceId source, string text, XamlSemanticRegistry registry)
        {
            _source = source;
            _text = text;
            _registry = registry;
        }

        internal XamlDocumentPlan Parse()
        {
            SkipTrivia();
            var root = _offset < _text.Length && Current == '<'
                ? ParseElement(new Dictionary<string, string>(StringComparer.Ordinal))
                : null;
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
                root,
                _resources.ToImmutable(),
                _styles.ToImmutable(),
                _templates.ToImmutable(),
                _resourceSlots.ToImmutableArray(),
                _diagnostics.ToImmutable());
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
                Report("XAML016", "An element name is required.", elementStart, Math.Min(_offset + 1, _text.Length));
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

            if (lexicalName == "Template")
            {
                ParseTemplate(attributes, selfClosing, elementStart, namespaces);
                return null;
            }

            var hasType = _registry.TryResolveType(name, out var type);
            if (!hasType)
            {
                Report("XAML002", $"Unsupported element '{lexicalName}'.", nameStart, nameStart + lexicalName.Length);
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

                if (type is null || !type.TryGetProperty(attribute.LocalName, out var property))
                {
                    Report("XAML003", $"Unsupported property '{attribute.LocalName}' on '{lexicalName}'.", attribute.Range);
                    continue;
                }

                if (TryParseValue(attribute.Value, property.ValueKind, attribute.Range, out var value))
                {
                    members.Add(new(property.Id, property.Name, value, attribute.Range));
                }
            }

            var children = ImmutableArray.CreateBuilder<XamlObjectPlan>();
            if (!selfClosing)
            {
                ParseChildren(lexicalName, name, type, namespaces, children);
            }

            var elementRange = Range(elementStart, _offset);
            var plan = new XamlObjectPlan(
                type?.Id ?? default,
                name,
                scopeName,
                elementRange,
                members.ToImmutable(),
                children.ToImmutable());
            if (resourceKey is not null)
            {
                if (_registry.TryResolveResource(resourceKey, out var resourceId))
                {
                    RegisterResourceSlot(resourceId, resourceKey, false);
                    _resources.Add(new(resourceId, resourceKey, plan, resourceRange));
                }
                else
                {
                    Report("XAML006", $"Resource '{resourceKey}' has no registered stable identity.", resourceRange);
                }
            }

            return plan;
        }

        private void ParseStyle(
            List<AttributeSyntax> attributes,
            bool selfClosing,
            int elementStart,
            Dictionary<string, string> namespaces)
        {
            string? key = null;
            string? targetType = null;
            foreach (var attribute in attributes)
            {
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.IsKey)
                {
                    key = attribute.Value;
                }
                else if (attribute.Prefix.Length == 0 && attribute.LocalName == "TargetType")
                {
                    targetType = attribute.Value;
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
                _styles.Add(new(
                    key,
                    ToQualifiedName(targetType, namespaces),
                    setters.ToImmutable(),
                    Range(elementStart, _offset)));
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

            if (TryParseValue(valueText, property.ValueKind, valueRange, out var value))
            {
                setters.Add(new(property.Id, property.Name, value, valueRange));
            }
        }

        private void ParseTemplate(
            List<AttributeSyntax> attributes,
            bool selfClosing,
            int elementStart,
            Dictionary<string, string> namespaces)
        {
            string? key = null;
            foreach (var attribute in attributes)
            {
                if (attribute.IsNamespace)
                {
                    continue;
                }

                if (attribute.IsKey)
                {
                    key = attribute.Value;
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
                _templates.Add(new(key, root, Range(elementStart, _offset)));
            }
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
            ImmutableArray<XamlObjectPlan>.Builder children)
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
                    Report("XAML017", "An attribute name is required.", attributeStart, Math.Min(attributeStart + 1, _text.Length));
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
                Report("XAML020", "The closing tag is not closed.", _offset, Math.Min(_offset + 1, _text.Length));
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
                if (!_registry.TryResolveResource(resource.Key, out var id))
                {
                    Report("XAML006", $"Resource '{resource.Key}' has no registered stable identity.", range);
                    plan = default;
                    return false;
                }

                plan = XamlValuePlan.FromResource(new(id, resource.Key, resource.IsDynamic, RegisterResourceSlot(id, resource.Key, resource.IsDynamic)));
                return true;
            }

            if (TryParseBinding(value, out var binding, out var bindingError))
            {
                plan = XamlValuePlan.FromBinding(binding);
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
                Report("XAML008", $"Markup extension '{value}' is not supported.", range);
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
                case XamlValueKind.Color when TryColor(value, out var color):
                    literal = new(expected, color);
                    return true;
                case XamlValueKind.Thickness when TryThickness(value, out var thickness):
                    literal = new(expected, thickness);
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
                        format = option;
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

            binding = new(path, mode, converter, format);
            return true;
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
            var limit = Math.Clamp(offset, 0, _text.Length);
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

        private static bool TryThickness(string value, out string canonical)
        {
            var parts = value.Split(',', StringSplitOptions.TrimEntries);
            canonical = string.Empty;
            if (parts.Length != 4)
            {
                return false;
            }

            var parsed = new float[4];
            for (var i = 0; i < parsed.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]) || !float.IsFinite(parsed[i]))
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
                else if (part.EndsWith('*') && float.TryParse(part[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) && float.IsFinite(weight) && weight > 0)
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

        private readonly record struct AttributeSyntax(
            string Prefix,
            string LocalName,
            string Value,
            int Start,
            int End,
            bool IsNamespace,
            bool IsName,
            bool IsKey,
            SourceRange Range)
        {
            internal bool IsDefaultNamespace => Prefix.Length == 0 && LocalName == "xmlns";
        }

        private readonly record struct ResourceSyntax(string Key, bool IsDynamic);
    }
}
