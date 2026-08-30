using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Threading;

namespace SnakeAvalonia;

public static class SnakeConfig
{
    public const int Columns = 24;
    public const int Rows = 18;
    public const int CellSize = 28;
    public const int StartDelayMs = 140;
    public const int MinDelayMs = 60;
}

public enum Direction
{
    Up,
    Down,
    Left,
    Right
}

public readonly record struct SnakeCell(int X, int Y);

public sealed class SnakeSegment : INotifyPropertyChanged
{
    private int _x;
    private int _y;

    public int X
    {
        get => _x;
        set => _x = value;
    }

    public int Y
    {
        get => _y;
        set => _y = value;
    }

    public SnakeSegment(int x, int y)
    {
        _x = x;
        _y = y;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class TileToPixelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int cell)
        {
            return (double)(cell * SnakeConfig.CellSize);
        }

        return 0d;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class SnakeViewModel : INotifyPropertyChanged
{
    private readonly Random _random = new();
    private readonly DispatcherTimer _timer;
    private readonly List<SnakeCell> _snake = new();
    private SnakeCell _food;
    private Direction _direction = Direction.Right;
    private Direction _nextDirection = Direction.Right;
    private bool _isGameOver;
    private bool _isPaused;
    private int _score;
    private int _bestScore;
    private int _delayMs = SnakeConfig.StartDelayMs;

    public ObservableCollection<SnakeSegment> SnakeBody { get; } = new();

    public int BoardWidthPx => SnakeConfig.Columns * SnakeConfig.CellSize;

    public int BoardHeightPx => SnakeConfig.Rows * SnakeConfig.CellSize;

    public int Score
    {
        get => _score;
        private set
        {
            _score = value;
            Raise(nameof(Score));
        }
    }

    public int BestScore
    {
        get => _bestScore;
        private set
        {
            _bestScore = value;
            Raise(nameof(BestScore));
        }
    }

    public bool IsGameOver
    {
        get => _isGameOver;
        private set
        {
            _isGameOver = value;
            Raise(nameof(IsGameOver));
            Raise(nameof(StatusText));
            Raise(nameof(PauseButtonText));
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            _isPaused = value;
            Raise(nameof(IsPaused));
            Raise(nameof(StatusText));
            Raise(nameof(PauseButtonText));
        }
    }

    public string PauseButtonText => IsPaused ? "Продолжить" : "Пауза";

    public string StatusText => IsGameOver ? "Проигрыш — нажми Enter" : (IsPaused ? "Пауза" : "Идёт игра");

    public int HeadX => _snake.Count == 0 ? 0 : _snake[0].X;

    public int HeadY => _snake.Count == 0 ? 0 : _snake[0].Y;

    public int FoodX => _food.X;

    public int FoodY => _food.Y;

    public SnakeViewModel()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_delayMs)
        };
        _timer.Tick += (_, __) => Tick();

        StartNewGame();
        _timer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void StartNewGame()
    {
        _timer.Stop();

        _snake.Clear();
        SnakeBody.Clear();

        var cx = SnakeConfig.Columns / 2;
        var cy = SnakeConfig.Rows / 2;
        _snake.Add(new SnakeCell(cx, cy));
        _snake.Add(new SnakeCell(cx - 1, cy));
        _snake.Add(new SnakeCell(cx - 2, cy));

        _direction = Direction.Right;
        _nextDirection = Direction.Right;
        _delayMs = SnakeConfig.StartDelayMs;
        _timer.Interval = TimeSpan.FromMilliseconds(_delayMs);

        Score = 0;
        IsGameOver = false;
        IsPaused = false;
        SpawnFood();
        PushViewFromModel();

        _timer.Start();
    }

    public void TogglePause()
    {
        if (IsGameOver)
        {
            return;
        }

        IsPaused = !IsPaused;
        if (IsPaused)
        {
            _timer.Stop();
        }
        else
        {
            _timer.Start();
        }
    }

    public void HandleKey(Key key)
    {
        if (key == Key.Enter)
        {
            StartNewGame();
            return;
        }

        if (key == Key.Space)
        {
            TogglePause();
            return;
        }

        if (IsGameOver || IsPaused)
        {
            return;
        }

        Direction? requested = key switch
        {
            Key.Up or Key.W or Key.I => Direction.Up,
            Key.Down or Key.S or Key.K => Direction.Down,
            Key.Left or Key.A or Key.J => Direction.Left,
            Key.Right or Key.D or Key.L => Direction.Right,
            _ => null
        };

        if (requested is null)
        {
            return;
        }

        if (!IsReverse(_direction, requested.Value))
        {
            _nextDirection = requested.Value;
        }
    }

    private static bool IsReverse(Direction current, Direction next)
        => current == Direction.Up && next == Direction.Down
            || current == Direction.Down && next == Direction.Up
            || current == Direction.Left && next == Direction.Right
            || current == Direction.Right && next == Direction.Left;

    private void Tick()
    {
        if (_isGameOver || _isPaused)
        {
            return;
        }

        _direction = _nextDirection;
        var head = _snake[0];
        var next = _direction switch
        {
            Direction.Up => head with { Y = head.Y - 1 },
            Direction.Down => head with { Y = head.Y + 1 },
            Direction.Left => head with { X = head.X - 1 },
            Direction.Right => head with { X = head.X + 1 },
            _ => head
        };

        if (next.X < 0 || next.X >= SnakeConfig.Columns || next.Y < 0 || next.Y >= SnakeConfig.Rows)
        {
            SetGameOver();
            return;
        }

        if (_snake.Contains(next))
        {
            SetGameOver();
            return;
        }

        _snake.Insert(0, next);

        if (next == _food)
        {
            Score += 10;
            if (Score > BestScore)
            {
                BestScore = Score;
            }

            SpeedUp();
            SpawnFood();
        }
        else
        {
            _snake.RemoveAt(_snake.Count - 1);
        }

        PushViewFromModel();
    }

    private void SpeedUp()
    {
        _delayMs = Math.Max(SnakeConfig.MinDelayMs, _delayMs - 4);
        _timer.Interval = TimeSpan.FromMilliseconds(_delayMs);
    }

    private void SpawnFood()
    {
        if (_snake.Count >= SnakeConfig.Columns * SnakeConfig.Rows)
        {
            SetGameOver();
            return;
        }

        while (true)
        {
            var candidate = new SnakeCell(
                _random.Next(SnakeConfig.Columns),
                _random.Next(SnakeConfig.Rows));

            if (!_snake.Contains(candidate))
            {
                _food = candidate;
                Raise(nameof(FoodX));
                Raise(nameof(FoodY));
                break;
            }
        }
    }

    private void SetGameOver()
    {
        IsGameOver = true;
        IsPaused = false;
        _timer.Stop();
    }

    private void PushViewFromModel()
    {
        SnakeBody.Clear();
        for (var i = 1; i < _snake.Count; i++)
        {
            var item = _snake[i];
            SnakeBody.Add(new SnakeSegment(item.X, item.Y));
        }

        Raise(nameof(HeadX));
        Raise(nameof(HeadY));
        Raise(nameof(FoodX));
        Raise(nameof(FoodY));
        Raise(nameof(StatusText));
    }

    private void Raise(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
