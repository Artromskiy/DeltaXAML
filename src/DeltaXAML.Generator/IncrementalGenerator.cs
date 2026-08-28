using System.Security.Cryptography;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using Delta.XAML;
using DeltaXAML.Compiler;
using DeltaSourceId = Delta.Diagnostics.SourceId;
using DeltaSourceRange = Delta.Diagnostics.SourceRange;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DeltaXAML.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class IncrementalGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor CompilerError = new(
        "DXAMLGEN100",
        "XAML compilation failed",
        "{0}: {1}",
        "DeltaXAML",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor EmitterError = new(
        "DXAMLGEN101",
        "XAML companion emission failed",
        "{0}: {1}",
        "DeltaXAML",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MetadataError = new(
        "DXAMLGEN102",
        "XAML generation metadata is invalid",
        "{0}",
        "DeltaXAML",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var xamlFiles = context.AdditionalTextsProvider
            .Where(static file => Path.GetExtension(file.Path).Equals(".xaml", StringComparison.OrdinalIgnoreCase))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) =>
            {
                var file = pair.Left;
                var options = pair.Right.GetOptions(file);
                options.TryGetValue("build_metadata.AdditionalFiles.DeltaXamlClassName", out var className);
                options.TryGetValue("build_metadata.AdditionalFiles.DeltaXamlNamespace", out var namespaceName);
                return new XamlAdditionalText(
                    file.Path,
                    file.GetText(cancellationToken)?.ToString() ?? string.Empty,
                    className,
                    namespaceName);
            });

        context.RegisterSourceOutput(
            xamlFiles.Combine(context.CompilationProvider),
            static (sourceProductionContext, input) => Emit(sourceProductionContext, input.Left, input.Right));
    }

    private static void Emit(SourceProductionContext context, XamlAdditionalText file, Compilation compilation)
    {
        var registry = XamlSemanticRegistry.CreateBuiltIns();
        if (!TryRegisterCustomTypes(registry, compilation, out var metadataError))
        {
            context.ReportDiagnostic(Diagnostic.Create(MetadataError, Location.None, metadataError));
            return;
        }

        var sourceId = new DeltaSourceId(CreateStableGuid(file.Path));
        var plan = XamlCompiler.Compile(sourceId, file.Text, registry);
        for (var i = 0; i < plan.Diagnostics.Length; i++)
        {
            var diagnostic = plan.Diagnostics[i];
            context.ReportDiagnostic(Diagnostic.Create(
                CompilerError,
                CreateLocation(file.Path, file.Text, diagnostic.Location),
                diagnostic.Code.Value,
                diagnostic.Message));
        }

        if (!plan.Success)
        {
            return;
        }

        if (!TryRegisterBindings(registry, plan, compilation, out metadataError))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MetadataError,
                CreateLocation(file.Path, file.Text, plan.Root?.Range),
                metadataError));
            return;
        }

        var className = string.IsNullOrWhiteSpace(file.ClassName)
            ? CreateClassName(file.Path, sourceId)
            : file.ClassName;
        var namespaceName = string.IsNullOrWhiteSpace(file.NamespaceName)
            ? "DeltaXaml.Generated"
            : file.NamespaceName;
        if (!IsIdentifier(className) || !IsNamespace(namespaceName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MetadataError,
                Location.None,
                $"Generated type name '{namespaceName}.{className}' is not a valid C# declaration."));
            return;
        }

        if (!CSharpArtifactEmitter.TryEmit(plan, registry, namespaceName, className, out var source, out var emissionDiagnostic))
        {
            if (emissionDiagnostic is { } failure)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EmitterError,
                    CreateLocation(file.Path, file.Text, failure.Location),
                    failure.Code,
                    failure.Message));
            }

            return;
        }

        context.AddSource(className + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static bool TryRegisterCustomTypes(
        XamlSemanticRegistry registry,
        Compilation compilation,
        out string error)
    {
        foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
        {
            var typeAttribute = FindAttribute(type.GetAttributes(), "Delta.XAML.UiXamlTypeAttribute");
            if (typeAttribute is null)
            {
                continue;
            }

            if (!TryReadTypeAttribute(type, typeAttribute, out var definition, out error))
            {
                return false;
            }

            try
            {
                registry.RegisterType(definition);
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadTypeAttribute(
        INamedTypeSymbol type,
        AttributeData attribute,
        [NotNullWhen(true)] out XamlTypeDefinition? definition,
        out string error)
    {
        definition = null;
        if (attribute.ConstructorArguments.Length != 4 ||
            attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
            attribute.ConstructorArguments[1].Value is not string name ||
            attribute.ConstructorArguments[2].Value is not string stableId ||
            attribute.ConstructorArguments[3].Value is not byte contentValue ||
            !Guid.TryParse(stableId, out var typeId) || typeId == Guid.Empty)
        {
            error = $"Custom XAML type '{type.ToDisplayString()}' has invalid UiXamlType metadata.";
            return false;
        }

        if (!DerivesFrom(type, "Delta.XAML.UiElement") || type.IsAbstract ||
            !type.InstanceConstructors.Any(static constructor =>
                constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal))
        {
            error = $"Custom XAML type '{type.ToDisplayString()}' must be a concrete UiElement with an accessible parameterless constructor.";
            return false;
        }

        var content = contentValue switch
        {
            0 => XamlContentKind.None,
            1 => XamlContentKind.Children,
            2 => XamlContentKind.SingleContent,
            _ => (XamlContentKind)(-1),
        };
        if (content is < XamlContentKind.None or > XamlContentKind.SingleContent)
        {
            error = $"Custom XAML type '{type.ToDisplayString()}' has an unsupported content kind.";
            return false;
        }

        var properties = System.Collections.Immutable.ImmutableArray.CreateBuilder<XamlPropertyDefinition>();
        foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
        {
            var propertyAttribute = FindAttribute(member.GetAttributes(), "Delta.XAML.UiXamlPropertyAttribute");
            if (propertyAttribute is null)
            {
                continue;
            }

            if (propertyAttribute.ConstructorArguments.Length != 2 ||
                propertyAttribute.ConstructorArguments[0].Value is not string propertyIdText ||
                propertyAttribute.ConstructorArguments[1].Value is not byte valueKind ||
                !Guid.TryParse(propertyIdText, out var propertyId) || propertyId == Guid.Empty ||
                member.SetMethod is null || member.SetMethod.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
                !TryMapValueKind(valueKind, out var mappedKind))
            {
                error = $"Custom XAML property '{type.ToDisplayString()}.{member.Name}' has invalid UiXamlProperty metadata or no accessible setter.";
                return false;
            }

            properties.Add(new(new UiPropertyId(propertyId), member.Name, mappedKind, MemberName: member.Name));
        }

        var attachmentMember = content switch
        {
            XamlContentKind.Children => "Add",
            XamlContentKind.SingleContent => "SetContent",
            _ => null,
        };
        if (attachmentMember is not null && !HasAttachment(type, attachmentMember))
        {
            error = $"Custom XAML type '{type.ToDisplayString()}' declares {content} content but has no accessible {attachmentMember}(UiElement) method.";
            return false;
        }

        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        definition = new(
            new UiTypeId(typeId),
            new XamlQualifiedName(xmlNamespace, name),
            content,
            properties.ToImmutable(),
            $"new {typeName}()",
            childAttachmentMember: content == XamlContentKind.Children ? attachmentMember : null,
            contentAttachmentMember: content == XamlContentKind.SingleContent ? attachmentMember : null);
        error = string.Empty;
        return true;
    }

    private static bool TryRegisterBindings(
        XamlSemanticRegistry registry,
        XamlDocumentPlan plan,
        Compilation compilation,
        out string error)
    {
        var bindings = new Dictionary<string, BindingRequest>(StringComparer.Ordinal);
        if (!CollectBindings(plan.Root, bindings, out error))
        {
            return false;
        }

        for (var i = 0; i < plan.Templates.Length; i++)
        {
            if (!CollectBindings(plan.Templates[i].Root, bindings, out error))
            {
                return false;
            }
        }

        if (bindings.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(plan.BindingSourceTypeName))
        {
            error = "Generated bindings require x:DataType on the XAML document.";
            return false;
        }

        var metadataName = plan.BindingSourceTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? plan.BindingSourceTypeName[8..]
            : plan.BindingSourceTypeName;
        var sourceType = compilation.GetTypeByMetadataName(metadataName);
        if (sourceType is null)
        {
            error = $"Binding source type '{plan.BindingSourceTypeName}' was not found in the compilation.";
            return false;
        }

        foreach (var pair in bindings)
        {
            if (!TryCreateBindingDefinition(compilation, sourceType, pair.Key, pair.Value, out var definition, out error))
            {
                return false;
            }

            registry.RegisterBinding(definition);
        }

        error = string.Empty;
        return true;
    }

    private static bool TryCreateBindingDefinition(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        string path,
        BindingRequest request,
        [NotNullWhen(true)] out XamlBindingDefinition? definition,
        out string error)
    {
        definition = null;
        ITypeSymbol current = sourceType;
        IPropertySymbol? finalProperty = null;
        var segments = path.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            finalProperty = current.GetMembers(segments[i]).OfType<IPropertySymbol>()
                .FirstOrDefault(static property => !property.IsIndexer && property.GetMethod is not null);
            if (finalProperty is null || finalProperty.GetMethod?.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                error = $"Binding path '{path}' cannot read '{segments[i]}' on '{current.ToDisplayString()}'.";
                return false;
            }

            if (i != segments.Length - 1 && finalProperty.NullableAnnotation == NullableAnnotation.Annotated)
            {
                error = $"Binding path '{path}' contains nullable intermediate member '{segments[i]}'; add an explicit generated propagation contract.";
                return false;
            }

            current = finalProperty.Type;
        }

        var access = "source." + path;
        var valueType = current;
        var read = access;
        string? write = finalProperty?.SetMethod?.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
            ? access + " = value"
            : null;
        if (request.ConverterKey is { } converterKey)
        {
            if (!TryFindConverter(compilation, converterKey, UiXamlConverterDirection.Forward, out var forward, out error) ||
                !compilation.ClassifyConversion(current, forward.Parameters[0].Type).IsImplicit)
            {
                error = error.Length == 0
                    ? $"Converter '{converterKey}' cannot accept binding value '{current.ToDisplayString()}'."
                    : error;
                return false;
            }

            read = ConverterExpression(forward) + "(" + access + ")";
            valueType = forward.ReturnType;
            if (request.NeedsWrite)
            {
                if (!TryFindConverter(compilation, converterKey, UiXamlConverterDirection.Backward, out var backward, out error) ||
                    !compilation.ClassifyConversion(valueType, backward.Parameters[0].Type).IsImplicit ||
                    !compilation.ClassifyConversion(backward.ReturnType, current).IsImplicit)
                {
                    error = error.Length == 0
                        ? $"Converter '{converterKey}' has no compatible backward method for '{current.ToDisplayString()}'."
                        : error;
                    return false;
                }

                write = finalProperty?.SetMethod?.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
                    ? access + " = " + ConverterExpression(backward) + "(value)"
                    : null;
            }
        }

        var sourceTypeName = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var valueTypeName = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        definition = new(path, sourceTypeName, valueTypeName, read, write, request.ConverterKey);
        error = string.Empty;
        return true;
    }

    private static bool CollectBindings(
        XamlObjectPlan? node,
        Dictionary<string, BindingRequest> bindings,
        out string error)
    {
        if (node is null)
        {
            error = string.Empty;
            return true;
        }

        for (var i = 0; i < node.Members.Length; i++)
        {
            if (node.Members[i].Value.Kind == XamlValueKind.Binding)
            {
                var binding = node.Members[i].Value.Binding;
                var request = new BindingRequest(binding.ConverterKey, binding.Mode == UiBindingMode.TwoWay);
                if (bindings.TryGetValue(binding.Path, out var previous))
                {
                    if (previous.ConverterKey != request.ConverterKey)
                    {
                        error = $"Binding path '{binding.Path}' uses more than one converter in the same artifact.";
                        return false;
                    }

                    bindings[binding.Path] = previous with { NeedsWrite = previous.NeedsWrite || request.NeedsWrite };
                }
                else
                {
                    bindings.Add(binding.Path, request);
                }
            }
        }

        for (var i = 0; i < node.Children.Length; i++)
        {
            if (!CollectBindings(node.Children[i], bindings, out error))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryFindConverter(
        Compilation compilation,
        string key,
        UiXamlConverterDirection direction,
        [NotNullWhen(true)] out IMethodSymbol? method,
        out string error)
    {
        method = null;
        foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
        {
            foreach (var candidate in type.GetMembers().OfType<IMethodSymbol>())
            {
                var attribute = FindAttribute(candidate.GetAttributes(), "Delta.XAML.UiXamlConverterAttribute");
                if (attribute is null || attribute.ConstructorArguments.Length != 2 ||
                    attribute.ConstructorArguments[0].Value is not string candidateKey ||
                    attribute.ConstructorArguments[1].Value is not byte candidateDirection ||
                    !string.Equals(key, candidateKey, StringComparison.Ordinal) || candidateDirection != (byte)direction)
                {
                    continue;
                }

                if (!candidate.IsStatic || candidate.Parameters.Length != 1 || candidate.ReturnsVoid ||
                    candidate.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                {
                    error = $"Converter '{key}' method '{candidate.ToDisplayString()}' must be an accessible static one-argument function.";
                    return false;
                }

                if (method is not null)
                {
                    error = $"Converter '{key}' has more than one {direction} method.";
                    return false;
                }

                method = candidate;
            }
        }

        if (method is null)
        {
            error = $"Converter '{key}' has no registered {direction} method.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string ConverterExpression(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name;

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol scope)
    {
        foreach (var type in scope.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in EnumerateNestedTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var child in scope.GetNamespaceMembers())
        {
            foreach (var type in EnumerateTypes(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in EnumerateNestedTypes(nested))
            {
                yield return descendant;
            }
        }
    }

    private static AttributeData? FindAttribute(IEnumerable<AttributeData> attributes, string metadataName)
    {
        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string metadataName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), metadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttachment(INamedTypeSymbol type, string name)
    {
        foreach (var method in type.GetMembers(name).OfType<IMethodSymbol>())
        {
            if (!method.IsStatic && method.Parameters.Length == 1 &&
                method.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal &&
                method.Parameters[0].Type is INamedTypeSymbol parameterType &&
                (string.Equals(parameterType.ToDisplayString(), "Delta.XAML.UiElement", StringComparison.Ordinal) ||
                 DerivesFrom(parameterType, "Delta.XAML.UiElement")))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryMapValueKind(byte value, out XamlValueKind kind)
    {
        kind = value switch
        {
            0 => XamlValueKind.String,
            1 => XamlValueKind.Boolean,
            2 => XamlValueKind.Single,
            3 => XamlValueKind.Double,
            4 => XamlValueKind.Color,
            5 => XamlValueKind.Thickness,
            6 => XamlValueKind.GridLengthList,
            7 => XamlValueKind.Enum,
            _ => XamlValueKind.Invalid,
        };
        return kind != XamlValueKind.Invalid;
    }

    private static bool IsIdentifier(string value) => SyntaxFacts.IsValidIdentifier(value);

    private static bool IsNamespace(string value)
    {
        var segments = value.Split('.');
        if (segments.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < segments.Length; i++)
        {
            if (!SyntaxFacts.IsValidIdentifier(segments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static Location CreateLocation(string path, string text, DeltaSourceRange? range)
    {
        if (range is not { } sourceRange || sourceRange.Start.Offset < 0 || sourceRange.Start.Offset > text.Length)
        {
            return Location.None;
        }

        var start = sourceRange.Start.Offset;
        var end = Math.Clamp(sourceRange.End.Offset, start, text.Length);
        var sourceText = SourceText.From(text, Encoding.UTF8);
        return Location.Create(path, TextSpan.FromBounds(start, end), sourceText.Lines.GetLinePositionSpan(TextSpan.FromBounds(start, end)));
    }

    private static Guid CreateStableGuid(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
    }

    private static string CreateClassName(string path, DeltaSourceId sourceId)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var builder = new StringBuilder(fileName.Length + 18);
        builder.Append("Xaml_");
        for (var i = 0; i < fileName.Length; i++)
        {
            var character = fileName[i];
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        if (builder.Length == 5 || char.IsDigit(builder[5]))
        {
            builder.Insert(5, '_');
        }

        builder.Append('_').Append(sourceId.Value.ToString("N")[..8]);
        return builder.ToString();
    }

    private readonly record struct XamlAdditionalText(
        string Path,
        string Text,
        string? ClassName,
        string? NamespaceName);

    private readonly record struct BindingRequest(string? ConverterKey, bool NeedsWrite);
}
