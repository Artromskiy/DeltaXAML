using System.Text;

internal static class ArchitectureGate
{
    private const string PassMessage = "[architecture] PASS: all designated types and runtime-path rules comply.";

    public static void Run()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();
        var pending = new List<string>();

        InspectArea(root, "Internal/State", "*State.cs", "State", pending, violations, ValidateState);
        InspectArea(root, "Internal/Mixins", "*Mixin.cs", "Mixins", pending, violations, ValidateMixin);
        InspectArea(root, "Internal/Descriptors", "*.cs", "Descriptors", pending, violations, ValidateDescriptor);
        InspectArea(root, "UserApi/Controls", "Ui*.cs", "Controls", pending, violations, ValidateControl);
        InspectRuntimePaths(root, violations);
        InspectNewTypePaths(root, violations);

        foreach (var message in pending)
        {
            Console.WriteLine($"[architecture] ACTIVE: {message}");
        }

        if (violations.Count == 0 && pending.Count == 0)
        {
            Console.Out.WriteLine(PassMessage.AsSpan());
            return;
        }

        var report = new StringBuilder("Architecture gate failed:");
        foreach (var message in pending)
        {
            report.AppendLine();
            report.Append(" - PENDING: ").Append(message);
        }

        foreach (var violation in violations)
        {
            report.AppendLine();
            report.Append(" - ").Append(violation);
        }

        throw new InvalidOperationException(report.ToString());
    }

    private static void InspectArea(
        string root,
        string relativeDirectory,
        string pattern,
        string areaName,
        List<string> pending,
        List<string> violations,
        Action<TypeShape, List<string>> validator)
    {
        var directory = Path.Combine(root, "src", "DeltaXAML", relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
        {
            pending.Add($"{relativeDirectory}/ is absent; the {areaName} check remains armed and expects future types in this exact location.");
            return;
        }

        var files = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length == 0)
        {
            pending.Add($"{relativeDirectory}/ contains no C# files; the {areaName} check remains armed and expects future types.");
            return;
        }

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (!MatchesPattern(fileName, pattern))
            {
                violations.Add($"{RelativePath(root, file)}: file name '{fileName}' is outside the designated {areaName} pattern '{pattern}'.");
                continue;
            }

            var types = SourceShapeParser.Parse(file, File.ReadAllText(file));
            if (types.Count == 0)
            {
                violations.Add($"{RelativePath(root, file)}: no type declaration was found in a designated {areaName} file.");
                continue;
            }

            foreach (var type in types)
            {
                validator(type, violations);
            }
        }
    }

    private static void ValidateState(TypeShape type, List<string> violations)
    {
        if (type.Kind != "struct" || !type.Name.EndsWith("State", StringComparison.Ordinal))
        {
            violations.Add($"{type.Location}: '{type.Name}' must be a *State struct, not {type.Kind}.");
            return;
        }

        foreach (var member in type.Members)
        {
            if (member.IsMethod || member.IsProperty || member.IsEvent || member.IsNestedType || member.HasDelegate || member.HasService)
            {
                violations.Add($"{member.Location}: '{type.Name}' state contains '{member.Name}', but state files allow fields and constants only.");
            }

            if (member.HasAllocation)
            {
                violations.Add($"{member.Location}: '{type.Name}.{member.Name}' allocates; state types must contain fields/constants only.");
            }
        }
    }

    private static void ValidateMixin(TypeShape type, List<string> violations)
    {
        if (type.Kind == "interface")
        {
            if (!type.Name.StartsWith('I'))
            {
                violations.Add($"{type.Location}: '{type.Name}' must be an I-prefixed capability interface.");
            }

            foreach (var member in type.Members)
            {
                if (member.IsMethod && member.HasBody)
                {
                    violations.Add($"{member.Location}: mixin interface '{type.Name}' contains default algorithm '{member.Name}'; use static abstract shape plus a readonly struct implementation.");
                }

                if (member.IsField || member.IsEvent || member.IsNestedType)
                {
                    violations.Add($"{member.Location}: mixin interface '{type.Name}' declares storage or a nested type through '{member.Name}'.");
                }
            }

            return;
        }

        if (type.Kind != "struct" || !type.IsReadonly || !type.Name.EndsWith("Mixin", StringComparison.Ordinal))
        {
            violations.Add($"{type.Location}: '{type.Name}' must be a stateless readonly *Mixin struct or I-prefixed capability interface.");
            return;
        }

        foreach (var member in type.Members)
        {
            if ((member.IsField && !member.IsConst) || member.IsProperty || member.IsEvent || member.IsNestedType || member.HasAllocation)
            {
                violations.Add($"{member.Location}: mixin '{type.Name}' owns instance state or behavior through '{member.Name}'.");
            }

            if (member.IsMethod && !member.IsStatic)
            {
                violations.Add($"{member.Location}: mixin '{type.Name}' declares instance algorithm '{member.Name}'; mixin methods must be static.");
            }
        }
    }

    private static void ValidateDescriptor(TypeShape type, List<string> violations)
    {
        if (type.Name.Contains("Descriptor", StringComparison.Ordinal) ||
            type.Name.EndsWith("Generated", StringComparison.Ordinal) ||
            type.Name.EndsWith("Index", StringComparison.Ordinal))
        {
            return;
        }

        violations.Add($"{type.Location}: descriptor-area type '{type.Name}' must be a descriptor or generated companion.");
    }

    private static void ValidateControl(TypeShape type, List<string> violations)
    {
        if (type.Kind != "class" || !type.Name.StartsWith("Ui", StringComparison.Ordinal) || type.IsAbstract ||
            (type.BaseType is not null && !string.Equals(type.BaseType, "UiElement", StringComparison.Ordinal)))
        {
            violations.Add($"{type.Location}: '{type.Name}' must be a concrete flat Ui class with UiElement as its only base.");
            return;
        }

        foreach (var member in type.Members)
        {
            if (member.IsNestedType)
            {
                violations.Add($"{member.Location}: control '{type.Name}' declares nested type '{member.Name}'; keep composition in state/mixins.");
                continue;
            }

            if (member.IsEvent || member.HasDelegate || member.HasSubscription)
            {
                violations.Add($"{member.Location}: control '{type.Name}' owns a per-instance delegate/subscription through '{member.Name}'.");
            }

            if (member.IsMethod && !member.IsConstructor && !member.IsForwarder)
            {
                violations.Add($"{member.Location}: control '{type.Name}' declares domain/helper method '{member.Name}'; only constructors, accessors/events and explicit mixin forwarding are allowed.");
            }
        }
    }

    private static void InspectRuntimePaths(string root, List<string> violations)
    {
        var directories = new[] { "Internal/Layout", "Internal/Input", "Internal/Visuals", "Internal/Descriptors" };
        foreach (var relativeDirectory in directories)
        {
            var directory = Path.Combine(root, "src", "DeltaXAML", relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
            {
                continue;
            }

            var files = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var tokens = SourceShapeParser.TokenizeForGate(File.ReadAllText(file));
                if (HasSequence(tokens, "Dictionary", "<", "Type", ",", "object") ||
                    HasSequence(tokens, "Dictionary", "<", "string", ",", "object") ||
                    HasSequence(tokens, "List", "<", "object") ||
                    HasSequence(tokens, "using", "System", ".", "Linq") ||
                    ContainsAny(tokens, "object", "Activator", "BindingFlags", "GetProperty", "PropertyInfo", "propertyName", "Select", "Where", "First", "Single", "ToList", "ToArray"))
                {
                    violations.Add($"{RelativePath(root, file)}: runtime stage contains forbidden object/type/reflection/LINQ lookup or allocation shape.");
                }

                if (ContainsAny(tokens, "Action", "Func", "event", "+="))
                {
                    violations.Add($"{RelativePath(root, file)}: runtime stage contains per-instance behavior delegate or subscription.");
                }
            }
        }
    }

    private static void InspectNewTypePaths(string root, List<string> violations)
    {
        var directories = new[] { "Internal/State", "Internal/Mixins", "Internal/Descriptors", "UserApi/Controls" };
        foreach (var relativeDirectory in directories)
        {
            var directory = Path.Combine(root, "src", "DeltaXAML", relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
            {
                continue;
            }

            var files = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var tokens = SourceShapeParser.TokenizeForGate(File.ReadAllText(file));
                if (ContainsAny(tokens, "Dictionary", "object", "Type", "Activator", "BindingFlags", "GetProperty", "PropertyInfo", "propertyName", "Select", "Where", "First", "Single", "ToList", "ToArray") ||
                    ContainsAny(tokens, "Action", "Func", "event", "+="))
                {
                    violations.Add($"{RelativePath(root, file)}: designated path contains runtime object/type/reflection/LINQ lookup or per-instance behavior state.");
                }

                if (ContainsAny(tokens, "Legacy", "Compatibility", "IUiPropertySource", "_legacyFrame"))
                {
                    violations.Add($"{RelativePath(root, file)}: new/migrated path references a compatibility symbol.");
                }

                if (relativeDirectory == "UserApi/Controls" && ContainsAny(tokens, "partial"))
                {
                    violations.Add($"{RelativePath(root, file)}: user control must not require partial generated behavior.");
                }
            }
        }
    }

    private static bool ContainsAny(IReadOnlyList<Token> tokens, params string[] values)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
            {
                if (tokens[i].Text == values[valueIndex])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSequence(IReadOnlyList<Token> tokens, params string[] values)
    {
        if (values.Length == 0 || values.Length > tokens.Count)
        {
            return false;
        }

        for (var start = 0; start <= tokens.Count - values.Length; start++)
        {
            var matches = true;
            for (var i = 0; i < values.Length; i++)
            {
                if (tokens[start + i].Text != values[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        return pattern switch
        {
            "*State.cs" => fileName.EndsWith("State.cs", StringComparison.Ordinal),
            "*Mixin.cs" => fileName.EndsWith("Mixin.cs", StringComparison.Ordinal),
            "*.cs" => fileName.EndsWith(".cs", StringComparison.Ordinal),
            "Ui*.cs" => fileName.StartsWith("Ui", StringComparison.Ordinal) && fileName.EndsWith(".cs", StringComparison.Ordinal),
            _ => false,
        };
    }

    private static string FindRepositoryRoot()
    {
        var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var start in starts)
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DeltaXAML.slnx")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src", "DeltaXAML")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Architecture gate could not locate the DeltaXAML repository root.");
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private sealed class TypeShape
    {
        public required string Kind { get; init; }
        public required string Name { get; init; }
        public required string Location { get; init; }
        public required bool IsAbstract { get; init; }
        public required bool IsReadonly { get; init; }
        public string? BaseType { get; init; }
        public required IReadOnlyList<MemberShape> Members { get; init; }
    }

    private sealed class MemberShape
    {
        public required string Name { get; init; }
        public required string Location { get; init; }
        public required bool IsMethod { get; init; }
        public required bool HasBody { get; init; }
        public required bool IsConstructor { get; init; }
        public required bool IsForwarder { get; init; }
        public required bool IsStatic { get; init; }
        public required bool IsConst { get; init; }
        public required bool IsProperty { get; init; }
        public required bool IsField { get; init; }
        public required bool IsEvent { get; init; }
        public required bool IsNestedType { get; init; }
        public required bool HasDelegate { get; init; }
        public required bool HasService { get; init; }
        public required bool HasSubscription { get; init; }
        public required bool HasAllocation { get; init; }
    }

    private readonly record struct Token(string Text, int Line);

    private static class SourceShapeParser
    {
        public static List<TypeShape> Parse(string file, string source)
        {
            var tokens = Tokenize(source);
            var types = new List<TypeShape>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var kind = tokens[i].Text;
                if (kind is not ("class" or "struct" or "interface" or "record" or "enum"))
                {
                    continue;
                }

                var nameIndex = i + 1;
                if (kind == "record" && nameIndex < tokens.Count && (tokens[nameIndex].Text == "class" || tokens[nameIndex].Text == "struct"))
                {
                    nameIndex++;
                }

                if (nameIndex >= tokens.Count || !IsIdentifier(tokens[nameIndex].Text))
                {
                    continue;
                }

                var open = FindNext(tokens, nameIndex + 1, "{");
                if (open < 0)
                {
                    continue;
                }

                var close = FindMatching(tokens, open, "{", "}");
                if (close < 0)
                {
                    continue;
                }

                var actualKind = kind == "record" && i + 1 < tokens.Count && (tokens[i + 1].Text == "class" || tokens[i + 1].Text == "struct")
                    ? $"record {tokens[i + 1].Text}"
                    : kind;
                types.Add(new TypeShape
                {
                    Kind = actualKind,
                    Name = tokens[nameIndex].Text,
                    Location = $"{file}:{tokens[i].Line}",
                    IsAbstract = HasModifierBefore(tokens, i, "abstract"),
                    IsReadonly = HasModifierBefore(tokens, i, "readonly"),
                    BaseType = FindBaseType(tokens, nameIndex, open),
                    Members = ParseMembers(tokens, open + 1, close, tokens[nameIndex].Text, file),
                });
            }

            return types;
        }

        private static List<MemberShape> ParseMembers(IReadOnlyList<Token> tokens, int start, int end, string typeName, string file)
        {
            var members = new List<MemberShape>();
            var memberStart = start;
            var parentheses = 0;
            var brackets = 0;
            for (var i = start; i < end; i++)
            {
                switch (tokens[i].Text)
                {
                    case "(":
                        parentheses++;
                        break;
                    case ")":
                        parentheses--;
                        break;
                    case "[":
                        brackets++;
                        break;
                    case "]":
                        brackets--;
                        break;
                }

                if (parentheses != 0 || brackets != 0)
                {
                    continue;
                }

                if (tokens[i].Text == "{")
                {
                    var close = FindMatching(tokens, i, "{", "}");
                    if (close < 0 || close > end)
                    {
                        break;
                    }

                    members.Add(Classify(tokens, memberStart, i, close, typeName, file));
                    memberStart = close + 1;
                    i = close;
                }
                else if (tokens[i].Text == ";")
                {
                    members.Add(Classify(tokens, memberStart, i, -1, typeName, file));
                    memberStart = i + 1;
                }
            }

            if (memberStart < end)
            {
                members.Add(Classify(tokens, memberStart, end - 1, -1, typeName, file));
            }

            return members;
        }

        private static MemberShape Classify(IReadOnlyList<Token> tokens, int start, int headerEnd, int bodyEnd, string typeName, string file)
        {
            while (start <= headerEnd && tokens[start].Text is ";" or "}")
            {
                start++;
            }

            if (start > headerEnd)
            {
                return EmptyMember(file, tokens[Math.Min(headerEnd, tokens.Count - 1)].Line);
            }

            var line = tokens[start].Line;
            var openParen = FindFirst(tokens, start, headerEnd, "(");
            var assignment = FindFirst(tokens, start, openParen < 0 ? headerEnd : openParen, "=", "=>");
            var nestedType = FindFirst(tokens, start, headerEnd, "class", "struct", "interface", "record", "enum") >= 0;
            var hasEvent = FindFirst(tokens, start, headerEnd, "event") >= 0;
            var hasAllocation = HasHeapAllocation(tokens, start, bodyEnd >= 0 ? bodyEnd : headerEnd);
            var hasDelegate = FindFirst(tokens, start, bodyEnd >= 0 ? bodyEnd : headerEnd, "Action", "Func", "delegate") >= 0;
            var hasService = HasTokenEnding(tokens, start, bodyEnd >= 0 ? bodyEnd : headerEnd, "Service");
            var hasSubscription = FindFirst(tokens, start, bodyEnd >= 0 ? bodyEnd : headerEnd, "+=") >= 0;
            if (openParen >= 0 && assignment < 0)
            {
                var name = PreviousIdentifier(tokens, openParen - 1);
                var explicitInterface = openParen >= start + 3 &&
                    tokens[openParen - 2].Text == "." &&
                    tokens[openParen - 3].Text.StartsWith('I');
                var forwarder = explicitInterface || ContainsMixinForwarding(tokens, openParen, bodyEnd >= 0 ? bodyEnd : headerEnd);
                return new MemberShape
                {
                    Name = name,
                    Location = $"{file}:{line}",
                    IsMethod = true,
                    HasBody = bodyEnd >= 0 || FindFirst(tokens, openParen + 1, headerEnd, "=>") >= 0,
                    IsConstructor = string.Equals(name, typeName, StringComparison.Ordinal),
                    IsForwarder = forwarder,
                    IsStatic = FindFirst(tokens, start, headerEnd, "static") >= 0,
                    IsConst = false,
                    IsProperty = false,
                    IsField = false,
                    IsEvent = false,
                    IsNestedType = nestedType,
                    HasDelegate = hasDelegate,
                    HasService = hasService,
                    HasSubscription = hasSubscription,
                    HasAllocation = hasAllocation,
                };
            }

            var hasExpressionBody = FindFirst(tokens, start, headerEnd, "=>") >= 0;
            var isProperty = (bodyEnd >= 0 || hasExpressionBody) && assignment < 0;
            var nameForMember = PreviousIdentifier(tokens, headerEnd - (bodyEnd >= 0 ? 1 : 0));
            return new MemberShape
            {
                Name = nameForMember,
                Location = $"{file}:{line}",
                IsMethod = false,
                HasBody = bodyEnd >= 0,
                IsConstructor = false,
                IsForwarder = false,
                IsStatic = FindFirst(tokens, start, headerEnd, "static") >= 0,
                IsConst = FindFirst(tokens, start, headerEnd, "const") >= 0,
                IsProperty = isProperty,
                IsField = !isProperty && !nestedType,
                IsEvent = hasEvent,
                IsNestedType = nestedType,
                HasDelegate = hasDelegate,
                HasService = hasService,
                HasSubscription = hasSubscription,
                HasAllocation = hasAllocation,
            };
        }

        private static MemberShape EmptyMember(string file, int line) => new()
        {
            Name = "<empty>",
            Location = $"{file}:{line}",
            IsMethod = false,
            HasBody = false,
            IsConstructor = false,
            IsForwarder = false,
            IsStatic = false,
            IsConst = false,
            IsProperty = false,
            IsField = false,
            IsEvent = false,
            IsNestedType = false,
            HasDelegate = false,
            HasService = false,
            HasSubscription = false,
            HasAllocation = false,
        };

        private static bool ContainsMixinForwarding(IReadOnlyList<Token> tokens, int start, int end)
        {
            var hasThis = false;
            var hasInterfaceCast = false;
            for (var i = start; i <= end; i++)
            {
                if (tokens[i].Text == "this")
                {
                    hasThis = true;
                }

                if (tokens[i].Text == "(" && i + 2 <= end && tokens[i + 1].Text.StartsWith('I') && tokens[i + 2].Text == ")")
                {
                    hasInterfaceCast = true;
                }

                if (tokens[i].Text is "new" or "for" or "foreach" or "while" or "if" or "switch" or "try" or "catch" or "lock")
                {
                    return false;
                }
            }

            return hasThis && hasInterfaceCast;
        }

        private static string PreviousIdentifier(IReadOnlyList<Token> tokens, int index)
        {
            for (var i = index; i >= 0; i--)
            {
                if (IsIdentifier(tokens[i].Text))
                {
                    return tokens[i].Text;
                }
            }

            return "<unknown>";
        }

        private static bool IsIdentifier(string value) =>
            value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_');

        private static int FindNext(IReadOnlyList<Token> tokens, int start, string value)
        {
            for (var i = start; i < tokens.Count; i++)
            {
                if (tokens[i].Text == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindFirst(IReadOnlyList<Token> tokens, int start, int end, params string[] values)
        {
            for (var i = start; i <= end && i < tokens.Count; i++)
            {
                for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
                {
                    if (tokens[i].Text == values[valueIndex])
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static bool HasModifierBefore(IReadOnlyList<Token> tokens, int typeIndex, string modifier)
        {
            for (var i = typeIndex - 1; i >= 0 && i >= typeIndex - 8; i--)
            {
                if (tokens[i].Text == modifier)
                {
                    return true;
                }

                if (tokens[i].Text is ";" or "{" or "}" or "class" or "struct" or "interface" or "record" or "enum")
                {
                    break;
                }
            }

            return false;
        }

        private static string? FindBaseType(IReadOnlyList<Token> tokens, int nameIndex, int bodyOpen)
        {
            var colon = FindFirst(tokens, nameIndex + 1, bodyOpen - 1, ":");
            if (colon < 0)
            {
                return null;
            }

            for (var i = colon + 1; i < bodyOpen; i++)
            {
                if (IsIdentifier(tokens[i].Text) && tokens[i].Text is not "where")
                {
                    return tokens[i].Text;
                }
            }

            return null;
        }

        private static bool HasTokenEnding(IReadOnlyList<Token> tokens, int start, int end, string suffix)
        {
            for (var i = start; i <= end && i < tokens.Count; i++)
            {
                if (tokens[i].Text.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasHeapAllocation(IReadOnlyList<Token> tokens, int start, int end)
        {
            for (var i = start; i <= end && i < tokens.Count; i++)
            {
                if (tokens[i].Text != "new" || i + 1 > end)
                {
                    continue;
                }

                if (tokens[i + 1].Text is "[" or "{" or "object" or "List" or "Dictionary" or "StringBuilder" or "Array")
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindMatching(IReadOnlyList<Token> tokens, int open, string openValue, string closeValue)
        {
            var depth = 0;
            for (var i = open; i < tokens.Count; i++)
            {
                if (tokens[i].Text == openValue)
                {
                    depth++;
                }
                else if (tokens[i].Text == closeValue && --depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        public static List<Token> TokenizeForGate(string source) => Tokenize(source);

        private static List<Token> Tokenize(string source)
        {
            var tokens = new List<Token>();
            var line = 1;
            for (var i = 0; i < source.Length;)
            {
                var current = source[i];
                if (current == '\n')
                {
                    line++;
                    i++;
                    continue;
                }

                if (char.IsWhiteSpace(current))
                {
                    i++;
                    continue;
                }

                if (current == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    i += 2;
                    while (i < source.Length && source[i] != '\n')
                    {
                        i++;
                    }

                    continue;
                }

                if (current == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                    {
                        if (source[i] == '\n')
                        {
                            line++;
                        }

                        i++;
                    }

                    i = Math.Min(source.Length, i + 2);
                    continue;
                }

                if (current is '"' or '\'')
                {
                    i = SkipQuoted(source, i, current, ref line);
                    continue;
                }

                if (char.IsLetter(current) || current == '_' || current == '@')
                {
                    var start = i;
                    i++;
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                    {
                        i++;
                    }

                    var text = source[start..i].TrimStart('@');
                    tokens.Add(new Token(text, line));
                    continue;
                }

                if (current == '=' && i + 1 < source.Length && source[i + 1] == '>')
                {
                    tokens.Add(new Token("=>", line));
                    i += 2;
                    continue;
                }

                tokens.Add(new Token(current.ToString(), line));
                i++;
            }

            return tokens;
        }

        private static int SkipQuoted(string source, int start, char quote, ref int line)
        {
            var i = start + 1;
            while (i < source.Length)
            {
                if (source[i] == '\n')
                {
                    line++;
                }

                if (source[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (source[i] == quote)
                {
                    return i + 1;
                }

                i++;
            }

            return i;
        }
    }
}
