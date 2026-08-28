using System.Security.Cryptography;
using System.Text;
using DeltaXAML.Compiler;
using DeltaSourceId = Delta.Diagnostics.SourceId;
using DeltaSourceRange = Delta.Diagnostics.SourceRange;
using Microsoft.CodeAnalysis;
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

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var xamlFiles = context.AdditionalTextsProvider
            .Where(static file => Path.GetExtension(file.Path).Equals(".xaml", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellationToken) => new XamlAdditionalText(
                file.Path,
                file.GetText(cancellationToken)?.ToString() ?? string.Empty));

        context.RegisterSourceOutput(xamlFiles, static (sourceProductionContext, file) => Emit(sourceProductionContext, file));
    }

    private static void Emit(SourceProductionContext context, XamlAdditionalText file)
    {
        var registry = XamlSemanticRegistry.CreateBuiltIns();
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

        var className = CreateClassName(file.Path, sourceId);
        if (!CSharpArtifactEmitter.TryEmit(plan, registry, "DeltaXaml.Generated", className, out var source, out var emissionDiagnostic))
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

    private readonly record struct XamlAdditionalText(string Path, string Text);
}
