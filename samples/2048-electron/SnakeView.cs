using System.Globalization;
using Delta.XAML;
using DeltaXaml.Samples.Snake.Generated;

namespace DeltaXaml.Samples.Snake;

internal sealed class SnakeView
{
    private static readonly UiColor EmptyColor = new(26, 35, 56);
    private static readonly UiColor BodyColor = new(56, 161, 111);
    private static readonly UiColor HeadColor = new(91, 255, 227);
    private static readonly UiColor FoodColor = new(255, 107, 107);

    private readonly UiBorder[] _cells = new UiBorder[SnakeGame.CellCount];
    private readonly UiTextBlock _score;
    private readonly UiTextBlock _best;
    private readonly UiTextBlock _status;
    private readonly UiButton _newGame;
    private readonly UiButton _pause;
    private readonly UiTextBlock _pauseLabel;

    internal SnakeView(SnakeArtifact page)
    {
        for (var row = 0; row < SnakeGame.Rows; row++)
        {
            for (var column = 0; column < SnakeGame.Columns; column++)
            {
                _cells[row * SnakeGame.Columns + column] =
                    Find<UiBorder>(page, $"CellR{row:00}C{column:00}");
            }
        }

        _score = Find<UiTextBlock>(page, "ScoreText");
        _best = Find<UiTextBlock>(page, "BestText");
        _status = Find<UiTextBlock>(page, "StatusText");
        _newGame = Find<UiButton>(page, "NewGameButton");
        _pause = Find<UiButton>(page, "PauseButton");
        _pauseLabel = _pause.Content is UiTextBlock label
            ? label
            : throw new InvalidOperationException("The generated PauseButton content must be a TextBlock.");
    }

    internal UiButton NewGameButton => _newGame;

    internal UiButton PauseButton => _pause;

    internal void Render(SnakeGame game)
    {
        for (var index = 0; index < _cells.Length; index++)
        {
            _cells[index].Background = EmptyColor;
        }

        var body = game.Body;
        for (var index = 1; index < body.Length; index++)
        {
            _cells[ToIndex(body[index])].Background = BodyColor;
        }

        if (body.Length > 0)
        {
            _cells[ToIndex(body[0])].Background = HeadColor;
        }

        _cells[ToIndex(game.Food)].Background = FoodColor;
        _score.Text = game.Score.ToString(CultureInfo.InvariantCulture);
        _best.Text = game.BestScore.ToString(CultureInfo.InvariantCulture);
        _status.Text = game.StatusText;
        _pauseLabel.Text = game.PauseButtonText;
    }

    private static int ToIndex(SnakeCell cell) => cell.Row * SnakeGame.Columns + cell.Column;

    private static T Find<T>(SnakeArtifact page, string name)
        where T : UiElement
    {
        if (page.TryFindName(name, out var element) && element is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"Generated Snake namescope entry '{name}' is missing or has the wrong type.");
    }
}
