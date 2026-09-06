using Delta.Render.UI;

namespace DeltaXaml.Samples.Snake;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var fonts = SnakeFonts.Load();
        var grid = SnakeArguments.ParseGrid(args);
        return await UiRenderHost.RunAsync(
            args,
            new UiRenderHostOptions(
                "MainWindow.dxaml",
                "DeltaXAML Snake",
                Width: 980,
                Height: 760,
                DefaultHeadlessFrames: 600),
            fonts,
            (textService, _) => new SnakeRenderContent(grid, textService, fonts)).ConfigureAwait(false);
    }
}
