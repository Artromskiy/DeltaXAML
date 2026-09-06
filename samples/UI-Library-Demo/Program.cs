using Delta.Render.UI;
using Delta.Text.Contract;
using Delta.XAML;
using DeltaXaml.Samples.UiLibraryDemo.Generated;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal static class Program
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var fonts = LoadFonts();
        return await UiRenderHost.RunAsync(
            args,
            new UiRenderHostOptions(
                string.Empty,
                "DeltaXAML UI Library Demo",
                Width: 1280,
                Height: 860,
                DefaultHeadlessFrames: 1),
            fonts,
            (textService, _) => new UiLibraryDemoContent(textService, fonts)).ConfigureAwait(false);
    }

    private static UiFontCatalog LoadFonts()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"UI library demo font was not found: {fontPath}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        return fonts;
    }

    private sealed class UiLibraryDemoContent : IUiRenderHostContent
    {
        private readonly UiLibraryDemoArtifact _artifact;

        internal UiLibraryDemoContent(ITextService textService, UiFontCatalog fonts)
        {
            ArgumentNullException.ThrowIfNull(textService);
            ArgumentNullException.ThrowIfNull(fonts);
            _artifact = new UiLibraryDemoArtifact(textService, fonts);
            PopulateGridOverlay(_artifact);
        }

        public UiDocument Document => _artifact.Document;

        public void AdvanceFrame()
        {
        }

        public void HandleInput(in Delta.XAML.Contract.UiInputEvent input) => Document.Dispatch(in input);

        public void Dispose() => _artifact.Dispose();

        private static void PopulateGridOverlay(UiLibraryDemoArtifact artifact)
        {
            if (!artifact.TryFindName("VerticalGridLines", out var vertical) ||
                vertical is not UiItemsControl verticalLines ||
                !artifact.TryFindName("HorizontalGridLines", out var horizontal) ||
                horizontal is not UiItemsControl horizontalLines)
            {
                throw new InvalidOperationException(
                    "UI library demo grid overlay names are missing from the generated XAML artifact.");
            }

            GridOverlay.Populate(verticalLines, horizontalLines);
        }
    }
}
