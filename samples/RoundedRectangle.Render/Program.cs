using Delta;
using Delta.Render.UI;
using Delta.Text;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;

namespace DeltaXaml.Samples.RoundedRectangle.Render;

internal static class Program
{
    private static readonly FontSourceId DefaultFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static readonly FontSourceId MonoFontId =
        new(new Guid("122538D2-EFAD-4627-BA67-A77E5A5CBB3F"));

    private static readonly FontSourceId DotoFontId =
        new(new Guid("DA5D2E6B-5936-4C68-9CE4-FA74D8C9A2A1"));

    private static readonly FontVariation[] DotoVariations =
    [
        new(new OpenTypeTag(0x524F4E44u), 100),
    ];

    private static readonly UiResourceId SpectrumGradientId =
        new(new Guid("6E5B9F52-1B6C-4AF0-A3C5-0A2F7E4E5A10"));

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var fonts = LoadFonts();
            var resources = CreateResources();
            return await UiRenderHost.RunAsync(
                args,
                new UiRenderHostOptions(
                    "RoundedRectangle.dxaml",
                    "DeltaXAML Rounded Rectangle",
                    Width: 800,
                    Height: 500,
                    HeadlessDpiScale: 2),
                fonts,
                new XamlLoadContext(new XamlTypeCatalog(), resources)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            return 1;
        }
    }

    private static UiResourceCatalog CreateResources()
    {
        var resources = new UiResourceCatalog();
        var accent = new UiColor(255, 138, 0);
        resources.Set("Accent", accent);

        var gradient = UiLinearGradient.Relative(
                110,
                new UiGradientStop[]
                {
                    new(0, accent),
                    new(0.3f, new UiColor(255, 75, 49)),
                    new(0.56f, new UiColor(216, 60, 165)),
                    new(1, new UiColor(121, 41, 216)),
                })
            .WithOutline(accent);
        resources.Set(SpectrumGradientId, gradient);
        resources.Set("SpectrumBrush", UiBrush.LinearGradient(SpectrumGradientId));
        return resources;
    }

    private static UiFontCatalog LoadFonts()
    {
        var fonts = new UiFontCatalog();
        fonts.Register("default", DefaultFontId, ReadFont("NotoSans-Regular.ttf"));
        fonts.Register("mono", MonoFontId, ReadFont("JetBrainsMono-Medium.ttf"));
        fonts.Register("doto", DotoFontId, ReadFont("Doto-Variable.ttf"), variations: DotoVariations);
        return fonts;
    }

    private static byte[] ReadFont(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        return File.Exists(path)
            ? File.ReadAllBytes(path)
            : throw new FileNotFoundException($"Rounded rectangle sample font was not found: {path}");
    }
}
