namespace DeltaXaml.Samples.Game2048;

internal enum MoveDirection
{
    Left,
    Right,
    Up,
    Down,
}

internal sealed class Game2048State
{
    private const int Side = 4;
    private const int CellCount = Side * Side;

    private readonly int[] _cells = new int[CellCount];
    private readonly int[] _line = new int[Side];
    private readonly int[] _result = new int[Side];
    private uint _random = 0x9E3779B9;

    public ReadOnlySpan<int> Cells => _cells;

    public int Score { get; private set; }

    public int Best { get; private set; }

    public bool Won { get; private set; }

    public bool IsGameOver => !HasEmptyCell() && !HasMerge();

    public void LoadDesignBoard()
    {
        Array.Clear(_cells);
        _cells[0] = 2;
        _cells[1] = 4;
        _cells[2] = 8;
        _cells[3] = 2;
        _cells[4] = 16;
        _cells[5] = 32;
        _cells[6] = 64;
        _cells[7] = 8;
        _cells[8] = 128;
        _cells[9] = 256;
        _cells[10] = 512;
        _cells[11] = 32;
        _cells[12] = 2;
        _cells[13] = 4;
        _cells[14] = 8;
        _cells[15] = 2048;
        Score = 12_456;
        Best = 48_192;
        Won = true;
    }

    public void Reset()
    {
        Array.Clear(_cells);
        Score = 0;
        Won = false;
        _random = 0x9E3779B9;
        _cells[0] = 2;
        _cells[1] = 2;
    }

    public bool TryMove(MoveDirection direction)
    {
        var moved = false;
        for (var line = 0; line < Side; line++)
        {
            moved |= MoveLine(line, direction);
        }

        if (!moved)
        {
            return false;
        }

        AddRandomTile();
        if (Score > Best)
        {
            Best = Score;
        }

        return true;
    }

    private bool MoveLine(int line, MoveDirection direction)
    {
        var vertical = direction is MoveDirection.Up or MoveDirection.Down;
        var reverse = direction is MoveDirection.Right or MoveDirection.Down;
        for (var offset = 0; offset < Side; offset++)
        {
            var position = reverse ? Side - offset - 1 : offset;
            _line[offset] = ReadCell(line, position, vertical);
            _result[offset] = 0;
        }

        var write = 0;
        var previous = 0;
        for (var offset = 0; offset < Side; offset++)
        {
            var value = _line[offset];
            if (value == 0)
            {
                continue;
            }

            if (previous == value)
            {
                _result[write - 1] = value * 2;
                Score += value * 2;
                if (value * 2 == 2048)
                {
                    Won = true;
                }

                previous = 0;
                continue;
            }

            _result[write++] = value;
            previous = value;
        }

        var changed = false;
        for (var offset = 0; offset < Side; offset++)
        {
            var position = reverse ? Side - offset - 1 : offset;
            if (ReadCell(line, position, vertical) != _result[offset])
            {
                changed = true;
            }

            WriteCell(line, position, vertical, _result[offset]);
        }

        return changed;
    }

    private int ReadCell(int line, int position, bool vertical) =>
        vertical ? _cells[position * Side + line] : _cells[line * Side + position];

    private void WriteCell(int line, int position, bool vertical, int value)
    {
        if (vertical)
        {
            _cells[position * Side + line] = value;
        }
        else
        {
            _cells[line * Side + position] = value;
        }
    }

    private void AddRandomTile()
    {
        var empty = 0;
        for (var index = 0; index < CellCount; index++)
        {
            if (_cells[index] == 0)
            {
                empty++;
            }
        }

        if (empty == 0)
        {
            return;
        }

        var target = (int)(NextRandom() % (uint)empty);
        for (var index = 0; index < CellCount; index++)
        {
            if (_cells[index] != 0)
            {
                continue;
            }

            if (target == 0)
            {
                _cells[index] = NextRandom() % 10 == 0 ? 4 : 2;
                return;
            }

            target--;
        }
    }

    private uint NextRandom()
    {
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return _random;
    }

    private bool HasEmptyCell()
    {
        for (var index = 0; index < CellCount; index++)
        {
            if (_cells[index] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasMerge()
    {
        for (var row = 0; row < Side; row++)
        {
            for (var column = 0; column < Side; column++)
            {
                var value = _cells[row * Side + column];
                if ((column + 1 < Side && value == _cells[row * Side + column + 1]) ||
                    (row + 1 < Side && value == _cells[(row + 1) * Side + column]))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
