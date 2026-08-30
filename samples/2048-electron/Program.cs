using Delta.Maths;
using Delta.Text;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Samples.Snake.Generated;

namespace DeltaXaml.Samples.Snake;

internal static class Program
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static int Main()
    {
        var fonts = new UiFontCatalog();
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"Snake sample font was not found: {fontPath}");
        }

        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        using var textService = new SixLaborsTextService();
        using var page = new SnakeArtifact(textService, fonts);
        var game = new SnakeGame();
        var view = new SnakeView(page);

        view.NewGameButton.Click += (_, _) =>
        {
            game.StartNewGame();
            view.Render(game);
        };
        view.PauseButton.Click += (_, _) =>
        {
            game.TogglePause();
            view.Render(game);
        };

        game.StartNewGame();
        view.Render(game);
        var initial = LayoutAndBuild(page.Document);
        Require(initial.Visuals.Length >= SnakeGame.CellCount, "Snake must emit one retained visual per board cell.");
        Require(initial.Text.Length >= 5, "Snake must emit its retained HUD text.");

        game.SetDirection(Direction.Down);
        for (var tick = 0; tick < 8; tick++)
        {
            game.Tick();
            view.Render(game);
        }

        var frame = LayoutAndBuild(page.Document);
        Require(frame.Visuals.Length == initial.Visuals.Length, "Ticks must reuse the fixed XAML visual slots.");
        Require(frame.Text.Length == initial.Text.Length, "Ticks must reuse the fixed XAML text slots.");
        Require(frame.Order.Length == frame.Visuals.Length + frame.Text.Length, "Display order must cover every Snake draw.");

        Console.WriteLine("DeltaXAML Snake sample");
        Console.WriteLine($"score={game.Score}, best={game.BestScore}, status={game.StatusText}");
        Console.WriteLine($"display list: visuals={frame.Visuals.Length}, clips={frame.Clips.Length}, text={frame.Text.Length}, order={frame.Order.Length}");
        return 0;
    }

    private static UiDisplayList LayoutAndBuild(UiDocument document)
    {
        document.Layout(new float2(980, 760), 1);
        return document.BuildDisplayList();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
