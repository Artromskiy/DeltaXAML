using Delta;

namespace DeltaXAML.Internal;

internal sealed class UiSemanticQueue
{
    private Delta.XAML.UiSemanticCommand[] _values = Array.Empty<Delta.XAML.UiSemanticCommand>();
    private int _count;

    internal ReadOnlySpan<Delta.XAML.UiSemanticCommand> Values => _values.AsSpan(0, _count);

    internal void Clear() => _count = 0;

    internal void Add(in Delta.XAML.UiSemanticCommand command)
    {
        if (_count == _values.Length)
        {
            Array.Resize(ref _values, Maths.Max(4, _values.Length * 2));
        }

        _values[_count++] = command;
    }
}
