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
        new(new Guid("8C1F6D6B-0F9A-4B3A-9D38-6C9D2B6E4F51"));

    private static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!HasFlag(args, "--headless"))
        {
            return await SnakeWindowRunner.RunAsync(args).ConfigureAwait(false);
        }

        return RunHeadless(args);
    }

    private static int RunHeadless(string[] args)
    {
        var fonts = new UiFontCatalog();
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LuckiestGuy-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"Snake sample font was not found: {fontPath}");
        }

        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        using var textService = new SixLaborsTextService();
        using var page = new SnakeArtifact(textService, fonts);
        var game = new SnakeGame();
        var view = new SnakeView(page);
        var layoutJsonPath = ParsePath(args, "--layout-json", "/tmp/delta-snake-layout.json");

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
        File.WriteAllText(layoutJsonPath, page.Document.BuildLayoutDiagnosticsJson());
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
        Console.WriteLine($"layout json: {layoutJsonPath}");
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

    private static bool HasFlag(string[] args, string option)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ParsePath(string[] args, string option, string fallback)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(option);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(args[index + 1])
                    ? throw new ArgumentException($"{option} requires a non-empty path.", nameof(args))
                    : args[index + 1];
            }
        }

        return fallback;
    }
}
