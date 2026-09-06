using Delta.Text.Contract;
using Delta.XAML;

namespace DeltaXaml.Samples.Snake;

internal static class SnakeFonts
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("8C1F6D6B-0F9A-4B3A-9D38-6C9D2B6E4F51"));

    internal static UiFontCatalog Load()
    {
        var fonts = new UiFontCatalog();
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LuckiestGuy-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"Snake sample font was not found: {fontPath}");
        }

        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        return fonts;
    }
}
