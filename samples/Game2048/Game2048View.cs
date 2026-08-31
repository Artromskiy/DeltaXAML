using System.Globalization;
using Delta.XAML;
using DeltaXaml.Samples.Game2048.Generated;

namespace DeltaXaml.Samples.Game2048;

internal sealed class Game2048View
{
    private readonly UiBorder[] _tiles = new UiBorder[16];
    private readonly TextBlock[] _labels = new TextBlock[16];
    private readonly TextBlock _score;
    private readonly TextBlock _best;
    private readonly TextBlock _status;

    internal Game2048View(Game2048Artifact page)
    {
        for (var index = 0; index < _tiles.Length; index++)
        {
            var name = $"{index / 4}{index % 4}";
            _tiles[index] = Find<UiBorder>(page, $"Tile{name}");
            _labels[index] = Find<TextBlock>(page, $"TileText{name}");
        }

        _score = Find<TextBlock>(page, "ScoreText");
        _best = Find<TextBlock>(page, "BestText");
        _status = Find<TextBlock>(page, "StatusText");
    }

    internal void Render(Game2048State state)
    {
        var cells = state.Cells;
        for (var index = 0; index < cells.Length; index++)
        {
            var value = cells[index];
            var text = value == 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
            if (_labels[index].Text != text)
            {
                _labels[index].Text = text;
            }

            var background = TileBackground(value);
            if (_tiles[index].Background != background)
            {
                _tiles[index].Background = background;
            }

            var foreground = TileForeground(value);
            if (_labels[index].Foreground != foreground)
            {
                _labels[index].Foreground = foreground;
            }
        }

        SetText(_score, state.Score.ToString("N0", CultureInfo.InvariantCulture));
        SetText(_best, state.Best.ToString("N0", CultureInfo.InvariantCulture));
        SetText(_status, state.Won
            ? "2048 reached — keep going!"
            : state.IsGameOver
                ? "Game over — start a new game"
                : "Use arrow keys to move");
    }

    private static void SetText(TextBlock target, string value)
    {
        if (target.Text != value)
        {
            target.Text = value;
        }
    }

    private static UiColor TileBackground(int value) => value switch
    {
        0 => new UiColor(205, 193, 180),
        2 => new UiColor(238, 228, 218),
        4 => new UiColor(237, 224, 200),
        8 => new UiColor(242, 177, 121),
        16 => new UiColor(245, 149, 99),
        32 => new UiColor(246, 124, 95),
        64 => new UiColor(246, 94, 59),
        128 => new UiColor(237, 207, 114),
        256 => new UiColor(237, 204, 97),
        512 => new UiColor(237, 200, 80),
        1024 => new UiColor(237, 197, 63),
        2048 => new UiColor(237, 194, 46),
        _ => new UiColor(60, 58, 50),
    };

    private static UiColor TileForeground(int value) => value is 0 or 2 or 4
        ? new UiColor(119, 110, 101)
        : new UiColor(249, 246, 242);

    private static T Find<T>(Game2048Artifact page, string name)
        where T : UiElement
    {
        if (page.TryFindName(name, out var element) && element is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"Generated 2048 namescope entry '{name}' is missing or has the wrong type.");
    }
}
