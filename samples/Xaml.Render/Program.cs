using Delta.Render.UI;
using Delta.Text.Contract;
using Delta.XAML;

namespace DeltaXaml.Samples.Xaml.Render;

internal static class Program
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));
    private static readonly FontSourceId DotoFontId =
        new(new Guid("DA5D2E6B-5936-4C68-9CE4-FA74D8C9A2A1"));

    private static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var fonts = LoadFonts();
        return await UiRenderHost.RunAsync(
            args,
            new UiRenderHostOptions(
                GetSamplePath(args),
                "DeltaXAML XAML Render",
                Width: 1280,
                Height: 860,
                DefaultHeadlessFrames: 1),
            fonts).ConfigureAwait(false);
    }

    private static UiFontCatalog LoadFonts()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"XAML render sample font was not found: {fontPath}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        var dotoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Doto-Variable.ttf");
        if (!File.Exists(dotoPath))
        {
            throw new FileNotFoundException($"XAML render sample font was not found: {dotoPath}");
        }

        fonts.Register("doto", DotoFontId, File.ReadAllBytes(dotoPath));
        return fonts;
    }

    private static string GetSamplePath(string[] args) =>
        GetOption(args, "--sample")?.Trim().ToUpperInvariant() switch
        {
            null or "MAIN" or "MAIN-WINDOW" => "MainWindow.dxaml",
            "UI-LIBRARY-DEMO" or "UI-LIBRARY" => "Sources/UI-Library-Demo/MainWindow.dxaml",
            "GRID-ONLY" or "GRID" => "Sources/UI-Library-Demo/GridOnly.dxaml",
            "ROUNDED" or "ROUNDED-RECTANGLE" => "Sources/RoundedRectangle/RoundedRectangle.dxaml",
            var sample => throw new ArgumentException(
                $"Unknown XAML sample '{sample}'. Use main-window, ui-library-demo, grid-only or rounded.",
                nameof(args)),
        };

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(args[index + 1])
                    ? throw new ArgumentException($"{name} requires a non-empty value.", nameof(args))
                    : args[index + 1];
            }
        }

        return null;
    }
}
