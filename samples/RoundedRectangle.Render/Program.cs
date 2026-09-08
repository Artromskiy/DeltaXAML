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

    private static readonly FontSourceId DotoFontId =
        new(new Guid("E4A8C3D7-8F2B-4E6A-9D41-2C7B6F5A0139"));

    private static readonly FontVariation[] DotoVariations =
    [
        new(new OpenTypeTag(0x524F4E44u), 100f),
    ];

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var fonts = LoadFonts();
            return await UiRenderHost.RunAsync(
                args,
                new UiRenderHostOptions(
                    "RoundedRectangle.dxaml",
                    "DeltaXAML Rounded Rectangle",
                    Width: 800,
                    Height: 500,
                    HeadlessDpiScale: 2),
                fonts,
                new XamlLoadContext(new XamlTypeCatalog(), new UiResourceCatalog())).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            return 1;
        }
    }

    private static UiFontCatalog LoadFonts()
    {
        var fonts = new UiFontCatalog();
        fonts.Register("default", DefaultFontId, ReadFont("NotoSans-Regular.ttf"));
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
