using Delta.Text.Contract;
using Delta.XAML;
using DeltaXaml.Samples.YageUiLibrary.Generated;

namespace DeltaXaml.Samples.YageUiLibrary;

internal static class Program
{
    private static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        try
        {
            var outputPath = GetOption(args, "--layout-json", "/tmp/delta-yage-ui-layout.json");
            using var textService = new EmptyTextService();
            using var artifact = new YageArtifact(textService);
            artifact.Document.Layout(new(1280, 860), 1);
            var json = artifact.Document.BuildLayoutDiagnosticsJson();
            File.WriteAllText(outputPath, json);

            Console.WriteLine("DeltaXAML YAGE UI library sample");
            Console.WriteLine($"layout: 1280x860, json: {Path.GetFullPath(outputPath)}");
            Console.WriteLine($"root: {artifact.Document.Root.GetType().Name}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static string GetOption(string[] args, string name, string fallback)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return fallback;
    }

    private sealed class EmptyTextService : ITextService
    {
        public FontInstanceId OpenFont(in FontOpenRequest request) => throw NotSupported();

        public void CloseFont(FontInstanceId font)
        {
        }

        public FontMetrics GetFontMetrics(FontInstanceId font, float pixelsPerEm) => throw NotSupported();

        public ShapedText Shape(in TextShapeRequest request) => throw NotSupported();

        public GlyphImage GenerateGlyphImage(in GlyphImageRequest request) => throw NotSupported();

        public void Dispose()
        {
        }

        private static NotSupportedException NotSupported() =>
            new("The layout-only sample does not rasterize text.");
    }
}
