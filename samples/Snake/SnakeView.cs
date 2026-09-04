using System.Globalization;
using Delta.XAML;
using DeltaXaml.Samples.Snake.Generated;

namespace DeltaXaml.Samples.Snake;

internal sealed class SnakeView
{
    private readonly UiItemsControl _cells;
    private readonly UiTextBlock _score;
    private readonly UiTextBlock _best;
    private readonly UiTextBlock _status;
    private readonly UiButton _newGame;
    private readonly UiButton _pause;
    private readonly UiTextBlock _pauseLabel;
    private int _rows;
    private int _columns;

    internal SnakeView(SnakeArtifact page)
    {
        var board = Find<UiCollectionView>(page, "BoardItems");
        _cells = board.ItemsHost;

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
        ArgumentNullException.ThrowIfNull(game);
        ResizeGrid(game.Rows, game.Columns);
        _score.Text = game.Score.ToString(CultureInfo.InvariantCulture);
        _best.Text = game.BestScore.ToString(CultureInfo.InvariantCulture);
        _status.Text = game.StatusText;
        _pauseLabel.Text = game.PauseButtonText;
    }

    private void ResizeGrid(int rows, int columns)
    {
        if (_rows == rows && _columns == columns)
        {
            return;
        }

        _cells.SetGridDimensions(columns, rows);

        _rows = rows;
        _columns = columns;
    }

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
