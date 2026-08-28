namespace DeltaXAML.Internal;

/// <summary>Reusable dense measure work queue deduplicated by retained node index.</summary>
internal sealed class UiMeasureQueueBuffer
{
    private readonly List<UiMeasureRequest> _items = new();
    private int[] _positions = Array.Empty<int>();
    private int[] _stamps = Array.Empty<int>();
    private int _stamp = 1;

    internal int Count => _items.Count;

    internal UiMeasureRequest this[int index] => _items[index];

    internal void Clear()
    {
        _items.Clear();
        if (_stamp == int.MaxValue)
        {
            Array.Clear(_stamps);
            _stamp = 1;
        }
        else
        {
            _stamp++;
        }
    }

    internal void Add(in UiMeasureRequest request)
    {
        var index = GetIndex(request.Element);
        EnsureCapacity(index);
        if (_stamps[index] == _stamp)
        {
            var position = _positions[index];
            if (_items[position].Element.Generation == request.Element.Generation)
            {
                _items[position] = request;
                return;
            }
        }

        _stamps[index] = _stamp;
        _positions[index] = _items.Count;
        _items.Add(request);
    }

    private void EnsureCapacity(int index)
    {
        if (index < _stamps.Length)
        {
            return;
        }

        var length = Math.Max(index + 1, Math.Max(8, _stamps.Length * 2));
        Array.Resize(ref _stamps, length);
        Array.Resize(ref _positions, length);
    }

    private static int GetIndex(UiNodeId id) => checked((int)id.Index);
}

/// <summary>Reusable dense arrange work queue deduplicated by retained node index.</summary>
internal sealed class UiArrangeQueueBuffer
{
    private readonly List<UiArrangeRequest> _items = new();
    private int[] _positions = Array.Empty<int>();
    private int[] _stamps = Array.Empty<int>();
    private int _stamp = 1;

    internal int Count => _items.Count;

    internal UiArrangeRequest this[int index] => _items[index];

    internal void Clear()
    {
        _items.Clear();
        if (_stamp == int.MaxValue)
        {
            Array.Clear(_stamps);
            _stamp = 1;
        }
        else
        {
            _stamp++;
        }
    }

    internal void Add(in UiArrangeRequest request)
    {
        var index = GetIndex(request.Element);
        EnsureCapacity(index);
        if (_stamps[index] == _stamp)
        {
            var position = _positions[index];
            if (_items[position].Element.Generation == request.Element.Generation)
            {
                _items[position] = request;
                return;
            }
        }

        _stamps[index] = _stamp;
        _positions[index] = _items.Count;
        _items.Add(request);
    }

    private void EnsureCapacity(int index)
    {
        if (index < _stamps.Length)
        {
            return;
        }

        var length = Math.Max(index + 1, Math.Max(8, _stamps.Length * 2));
        Array.Resize(ref _stamps, length);
        Array.Resize(ref _positions, length);
    }

    private static int GetIndex(UiNodeId id) => checked((int)id.Index);
}
