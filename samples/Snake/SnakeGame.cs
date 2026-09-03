using Delta.XAML;

namespace DeltaXaml.Samples.Snake;

internal enum Direction : byte
{
    Up,
    Down,
    Left,
    Right,
}

public readonly record struct SnakeCell(int Column, int Row, UiColor Color = default);

public sealed class SnakeGame : IUiItemsSource<SnakeCell>
{
    internal const int DefaultColumns = 24;
    internal const int DefaultRows = 18;
    private const int InitialLength = 3;
    private const int FoodReward = 10;
    private const uint InitialRandom = 0x9E3779B9;

    private SnakeCell[] _snake;
    private SnakeCell[] _cells;
    private bool[] _occupied;
    private uint _random = InitialRandom;
    private SnakeCell _food;
    private Direction _direction = Direction.Right;
    private Direction _nextDirection = Direction.Right;
    private int _length;
    private ulong _cellsVersion;

    internal SnakeGame(int columns = DefaultColumns, int rows = DefaultRows)
    {
        ValidateDimensions(columns, rows);
        Columns = columns;
        Rows = rows;
        var cellCount = checked(columns * rows);
        _snake = new SnakeCell[cellCount];
        _cells = new SnakeCell[cellCount];
        _occupied = new bool[cellCount];
    }

    internal int Columns { get; private set; }

    internal int Rows { get; private set; }

    internal int CellCount => Columns * Rows;

    public SnakeGame Cells => this;

    public int Count => CellCount;

    public ulong Version => _cellsVersion;

    internal ReadOnlySpan<SnakeCell> Body => _snake.AsSpan(0, _length);

    internal SnakeCell Food => _food;

    internal int Score { get; private set; }

    internal int BestScore { get; private set; }

    internal bool IsGameOver { get; private set; }

    internal bool IsPaused { get; private set; }

    internal int TickCount { get; private set; }

    internal void Resize(int columns, int rows)
    {
        ValidateDimensions(columns, rows);
        if (Columns == columns && Rows == rows)
        {
            return;
        }

        Columns = columns;
        Rows = rows;
        var cellCount = checked(columns * rows);
        _snake = new SnakeCell[cellCount];
        _cells = new SnakeCell[cellCount];
        _occupied = new bool[cellCount];
        StartNewGame();
    }

    internal string StatusText => IsGameOver
        ? $"Game over — Enter: new game · frame {TickCount}"
        : IsPaused
            ? $"Paused — Space: resume · frame {TickCount}"
            : $"Game running — arrows or WASD · frame {TickCount}";

    internal string PauseButtonText => IsPaused ? "Resume" : "Pause";

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
        RefreshCells();
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
                RefreshCells();
                return;
            }

            SpawnFood();
        }

        RefreshCells();
    }

    public ulong GetKey(int index)
    {
        ValidateCellIndex(index);
        var row = index / Columns;
        var column = index % Columns;
        return ((ulong)(uint)row << 32) | (uint)column;
    }

    public SnakeCell GetItem(int index)
    {
        ValidateCellIndex(index);
        return _cells[index];
    }

    public bool TryGetChange(ulong previousVersion, out UiCollectionChange change)
    {
        change = new(UiCollectionChangeKind.Replace, 0, CellCount);
        return previousVersion + 1 == _cellsVersion;
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

    private SnakeCell Move(SnakeCell cell, Direction direction)
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

    private int ToIndex(SnakeCell cell) => cell.Row * Columns + cell.Column;

    private void RefreshCells()
    {
        for (var index = 0; index < CellCount; index++)
        {
            var row = index / Columns;
            var column = index % Columns;
            _cells[index] = new(column, row, EmptyColor);
        }

        for (var index = 1; index < _length; index++)
        {
            SetCellColor(_snake[index], BodyColor);
        }

        if (_length > 0)
        {
            SetCellColor(_snake[0], HeadColor);
        }

        SetCellColor(_food, FoodColor);
        _cellsVersion++;
    }

    private void SetCellColor(SnakeCell cell, UiColor color) =>
        _cells[ToIndex(cell)] = cell with { Color = color };

    private void ValidateCellIndex(int index)
    {
        if ((uint)index >= (uint)CellCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private static void ValidateDimensions(int columns, int rows)
    {
        if (columns < InitialLength + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), columns, $"Columns must be at least {InitialLength + 1}.");
        }

        if (rows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "Rows must be positive.");
        }
    }

    private static readonly UiColor EmptyColor = new(26, 35, 56);
    private static readonly UiColor BodyColor = new(56, 161, 111);
    private static readonly UiColor HeadColor = new(91, 255, 227);
    private static readonly UiColor FoodColor = new(255, 107, 107);
}
