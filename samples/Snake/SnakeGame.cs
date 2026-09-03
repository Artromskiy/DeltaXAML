namespace DeltaXaml.Samples.Snake;

internal enum Direction : byte
{
    Up,
    Down,
    Left,
    Right,
}

internal readonly record struct SnakeCell(int Column, int Row);

internal sealed class SnakeGame
{
    internal const int Columns = 24;
    internal const int Rows = 18;
    internal const int CellCount = Columns * Rows;
    private const int InitialLength = 3;
    private const int FoodReward = 10;
    private const uint InitialRandom = 0x9E3779B9;

    private readonly SnakeCell[] _snake = new SnakeCell[CellCount];
    private readonly bool[] _occupied = new bool[CellCount];
    private uint _random = InitialRandom;
    private SnakeCell _food;
    private Direction _direction = Direction.Right;
    private Direction _nextDirection = Direction.Right;
    private int _length;

    internal ReadOnlySpan<SnakeCell> Body => _snake.AsSpan(0, _length);

    internal SnakeCell Food => _food;

    internal int Score { get; private set; }

    internal int BestScore { get; private set; }

    internal bool IsGameOver { get; private set; }

    internal bool IsPaused { get; private set; }

    internal int TickCount { get; private set; }

    internal string StatusText => IsGameOver
        ? $"Игра окончена — Enter: новая игра · кадр {TickCount}"
        : IsPaused
            ? $"Пауза — Space: продолжить · кадр {TickCount}"
            : $"Игра идёт — стрелки или WASD · кадр {TickCount}";

    internal string PauseButtonText => IsPaused ? "Продолжить" : "Пауза";

    internal void StartNewGame()
    {
        Array.Clear(_occupied);
        _length = InitialLength;
        var centerColumn = Columns / 2;
        var centerRow = Rows / 2;
        for (var index = 0; index < _length; index++)
        {
            _snake[index] = new SnakeCell(centerColumn - index, centerRow);
            _occupied[ToIndex(_snake[index])] = true;
        }

        _direction = Direction.Right;
        _nextDirection = Direction.Right;
        _random = InitialRandom;
        Score = 0;
        IsGameOver = false;
        IsPaused = false;
        TickCount = 0;
        SpawnFood();
    }

    internal void TogglePause()
    {
        if (!IsGameOver)
        {
            IsPaused = !IsPaused;
        }
    }

    internal void SetDirection(Direction direction)
    {
        if (!IsGameOver && !IsPaused && !IsOpposite(_direction, direction))
        {
            _nextDirection = direction;
        }
    }

    internal void Tick()
    {
        TickCount++;
        if (IsGameOver || IsPaused)
        {
            return;
        }

        _direction = _nextDirection;
        var next = Move(_snake[0], _direction);
        if (_occupied[ToIndex(next)] && next != _snake[_length - 1])
        {
            IsGameOver = true;
            return;
        }

        var ateFood = next == _food;
        var oldTail = _snake[_length - 1];
        for (var index = ateFood ? _length : _length - 1; index > 0; index--)
        {
            _snake[index] = _snake[index - 1];
        }

        _snake[0] = next;
        _occupied[ToIndex(oldTail)] = ateFood;
        _occupied[ToIndex(next)] = true;

        if (ateFood)
        {
            _length++;
            Score += FoodReward;
            if (Score > BestScore)
            {
                BestScore = Score;
            }

            if (_length == CellCount)
            {
                IsGameOver = true;
                return;
            }

            SpawnFood();
        }
    }

    private void SpawnFood()
    {
        for (var attempt = 0; attempt < CellCount; attempt++)
        {
            var candidate = new SnakeCell(
                (int)(NextRandom() % Columns),
                (int)(NextRandom() % Rows));
            if (!_occupied[ToIndex(candidate)])
            {
                _food = candidate;
                return;
            }
        }

        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var candidate = new SnakeCell(column, row);
                if (!_occupied[ToIndex(candidate)])
                {
                    _food = candidate;
                    return;
                }
            }
        }

        IsGameOver = true;
    }

    private uint NextRandom()
    {
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return _random;
    }

    private static SnakeCell Move(SnakeCell cell, Direction direction)
    {
        var moved = direction switch
        {
            Direction.Up => cell with { Row = cell.Row - 1 },
            Direction.Down => cell with { Row = cell.Row + 1 },
            Direction.Left => cell with { Column = cell.Column - 1 },
            Direction.Right => cell with { Column = cell.Column + 1 },
            _ => cell,
        };

        return new SnakeCell(Wrap(moved.Column, Columns), Wrap(moved.Row, Rows));
    }

    private static bool IsOpposite(Direction first, Direction second) =>
        (first == Direction.Up && second == Direction.Down) ||
        (first == Direction.Down && second == Direction.Up) ||
        (first == Direction.Left && second == Direction.Right) ||
        (first == Direction.Right && second == Direction.Left);

    private static int Wrap(int value, int length) => value < 0
        ? length - 1
        : value == length
            ? 0
            : value;

    private static int ToIndex(SnakeCell cell) => cell.Row * Columns + cell.Column;
}
