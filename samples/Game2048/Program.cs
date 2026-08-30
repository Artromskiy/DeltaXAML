using Delta.Maths;
using Delta.Text;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Samples.Game2048.Generated;

namespace DeltaXaml.Samples.Game2048;

internal static class Program
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"2048 sample font was not found: {fontPath}");
        }

        var fonts = new UiFontCatalog();
        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));
        using var textService = new SixLaborsTextService();
        using var page = new Game2048Artifact(textService, fonts);
        var view = new Game2048View(page);
        var game = new Game2048State();

        game.LoadDesignBoard();
        view.Render(game);
        var designFrame = LayoutAndBuild(page.Document);
        var board = Find<UiBorder>(page, "Board");
        for (var index = 0; index < designFrame.Visuals.Length; index++)
        {
            var visual = designFrame.Visuals[index];
            Console.WriteLine($"visual[{index}] bounds={visual.Bounds} fill={visual.Paint.FillColor}");
        }
        Require(designFrame.Visuals.Length >= 17, "2048 design must emit board and tile visuals.");
        Require(designFrame.Clips.Length > 0, "2048 design must emit a root clip.");
        Require(designFrame.Text.Length >= 20, "2048 design must emit neutral text requests.");
        Require(designFrame.Order.Length == designFrame.Visuals.Length + designFrame.Text.Length, "Display order must cover every 2048 draw.");
        Require(board.CornerRadius == UiCornerRadii.Uniform(16), "Generated XAML must apply the board corner radius.");

        game.Reset();
        view.Render(game);
        Require(game.Cells[0] != 0 && game.Cells[1] != 0, "Deterministic new game must start with two tiles.");
        Require(game.TryMove(MoveDirection.Left), "The first left move must change the deterministic board.");
        view.Render(game);
        var playableFrame = LayoutAndBuild(page.Document);
        Require(game.Score == 4, "Merging two starting tiles must award four points.");
        Require(playableFrame.Text.Length == designFrame.Text.Length, "Game updates must reuse the fixed XAML text slots.");

        var resetButton = Find<UiButton>(page, "ResetButton");
        var newGameButton = Find<UiButton>(page, "NewGameButton");
        resetButton.Click += (_, _) =>
        {
            game.Reset();
            view.Render(game);
        };
        newGameButton.Click += (_, _) =>
        {
            game.Reset();
            view.Render(game);
        };

        var key = new UiKeyEvent(
            UiKeyEventKind.Down,
            new UiPhysicalKey(39),
            new UiLogicalKey(39),
            default,
            false);
        page.Document.Dispatch(UiInputEvent.FromKey(in key));
        if (DirectionFor(key.PhysicalKey) is { } direction)
        {
            game.TryMove(direction);
            view.Render(game);
        }

        var finalFrame = LayoutAndBuild(page.Document);
        Console.WriteLine("DeltaXAML 2048 headless sample");
        Console.WriteLine($"score={game.Score}, best={game.Best}, won={game.Won}, game-over={game.IsGameOver}");
        Console.WriteLine($"display list: visuals={finalFrame.Visuals.Length}, clips={finalFrame.Clips.Length}, text={finalFrame.Text.Length}, order={finalFrame.Order.Length}");
        Console.WriteLine("acceptance: generated XAML layout, 4x4 moves and retained display-list output");
        return 0;
    }

    private static T Find<T>(Game2048Artifact page, string name)
        where T : UiElement
    {
        if (page.TryFindName(name, out var element) && element is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"Generated 2048 namescope entry '{name}' is missing or has the wrong type.");
    }

    private static UiDisplayList LayoutAndBuild(UiDocument document)
    {
        document.Layout(new float2(960, 720), 1);
        return document.BuildDisplayList();
    }

    private static MoveDirection? DirectionFor(UiPhysicalKey key) => key.Value switch
    {
        37 => MoveDirection.Left,
        38 => MoveDirection.Up,
        39 => MoveDirection.Right,
        40 => MoveDirection.Down,
        65 => MoveDirection.Left,
        87 => MoveDirection.Up,
        68 => MoveDirection.Right,
        83 => MoveDirection.Down,
        _ => null,
    };

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
