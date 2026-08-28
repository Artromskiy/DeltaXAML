using Delta.Maths;
using Delta.XAML.Contract;

namespace DeltaXAML.Internal;

internal sealed class UiGestureArena
{
    private const long LongPressTicks = TimeSpan.TicksPerMillisecond * 500;
    private const long MultipleTapTicks = TimeSpan.TicksPerMillisecond * 400;
    private const float DragThresholdSquared = 16f;
    private const float SwipeThresholdSquared = 900f;
    private readonly UiRuntime _runtime;
    private PointerCandidate[] _active = Array.Empty<PointerCandidate>();
    private int _activeCount;
    private UiElement? _lastTapTarget;
    private long _lastTapTicks;
    private int _tapCount;

    internal UiGestureArena(UiRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    internal void Process(UiElement? target, in UiPointerEvent input, long timestampTicks)
    {
        switch (input.Kind)
        {
            case UiPointerEventKind.ButtonDown when target is not null:
                Begin(target, in input, timestampTicks);
                break;
            case UiPointerEventKind.Move:
                Move(in input);
                break;
            case UiPointerEventKind.ButtonUp:
                Complete(in input, timestampTicks);
                break;
            case UiPointerEventKind.Cancel:
            case UiPointerEventKind.CaptureLost:
                Cancel(input.PointerId);
                break;
        }
    }

    internal void Cancel(UiElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (var i = _activeCount - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_active[i].Target, target))
            {
                RemoveAt(i);
            }
        }
    }

    private void Begin(UiElement target, in UiPointerEvent input, long timestampTicks)
    {
        var index = Find(input.PointerId);
        if (index < 0)
        {
            EnsureCapacity(_activeCount + 1);
            index = _activeCount++;
        }

        _active[index] = new(
            input.PointerId,
            target,
            timestampTicks,
            input.Position,
            input.Position,
            false,
            InitialPairDistance(target, input.PointerId, input.Position));
    }

    private void Move(in UiPointerEvent input)
    {
        var index = Find(input.PointerId);
        if (index < 0)
        {
            return;
        }

        ref var candidate = ref _active[index];
        var previous = candidate.LastPosition;
        candidate.LastPosition = input.Position;
        var total = input.Position - candidate.StartPosition;
        var delta = input.Position - previous;
        var distanceSquared = total.x * total.x + total.y * total.y;
        var gestures = candidate.Target.Gestures;
        if (!candidate.Dragging && distanceSquared >= DragThresholdSquared &&
            (gestures & (Delta.XAML.UiGestureKind.Drag | Delta.XAML.UiGestureKind.Pan)) != 0)
        {
            candidate.Dragging = true;
            Publish(candidate.Target, Delta.XAML.UiSemanticActionKind.DragStarted, Delta.XAML.UiGestureKind.Drag, input.Position, total, 1, 0);
        }

        if (candidate.Dragging)
        {
            Publish(candidate.Target, Delta.XAML.UiSemanticActionKind.DragDelta, Delta.XAML.UiGestureKind.Pan, input.Position, delta, 1, 0);
        }

        if ((gestures & Delta.XAML.UiGestureKind.Pinch) != 0 && TryPair(index, out var other))
        {
            var distance = Distance(input.Position, other.LastPosition);
            var baseline = candidate.PairDistance > 0 ? candidate.PairDistance : distance;
            if (baseline > 0)
            {
                Publish(candidate.Target, Delta.XAML.UiSemanticActionKind.Pinch, Delta.XAML.UiGestureKind.Pinch, input.Position, delta, distance / baseline, 0);
            }
        }
    }

    private void Complete(in UiPointerEvent input, long timestampTicks)
    {
        var index = Find(input.PointerId);
        if (index < 0)
        {
            return;
        }

        var candidate = _active[index];
        var total = input.Position - candidate.StartPosition;
        var distanceSquared = total.x * total.x + total.y * total.y;
        var duration = Math.Max(0, timestampTicks - candidate.DownTicks);
        var gestures = candidate.Target.Gestures;
        if (candidate.Dragging)
        {
            Publish(candidate.Target, Delta.XAML.UiSemanticActionKind.DragCompleted, Delta.XAML.UiGestureKind.Drag, input.Position, total, 1, 0);
        }
        else if ((gestures & Delta.XAML.UiGestureKind.Swipe) != 0 && distanceSquared >= SwipeThresholdSquared)
        {
            Publish(candidate.Target, Delta.XAML.UiSemanticActionKind.Swipe, Delta.XAML.UiGestureKind.Swipe, input.Position, total, 1, 0);
        }
        else if ((gestures & Delta.XAML.UiGestureKind.LongPress) != 0 && duration >= LongPressTicks)
        {
            Publish(candidate.Target, Delta.XAML.UiSemanticActionKind.LongPress, Delta.XAML.UiGestureKind.LongPress, input.Position, total, 1, 0);
        }
        else if (distanceSquared < DragThresholdSquared &&
            (gestures & (Delta.XAML.UiGestureKind.Tap | Delta.XAML.UiGestureKind.MultipleTap)) != 0)
        {
            RegisterTap(candidate.Target, input.Position, timestampTicks, gestures);
        }

        RemoveAt(index);
    }

    private void RegisterTap(UiElement target, float2 position, long timestampTicks, Delta.XAML.UiGestureKind gestures)
    {
        if (ReferenceEquals(_lastTapTarget, target) && timestampTicks >= _lastTapTicks && timestampTicks - _lastTapTicks <= MultipleTapTicks)
        {
            _tapCount++;
        }
        else
        {
            _tapCount = 1;
        }

        _lastTapTarget = target;
        _lastTapTicks = timestampTicks;
        if ((gestures & Delta.XAML.UiGestureKind.Tap) != 0)
        {
            Publish(target, Delta.XAML.UiSemanticActionKind.Tap, Delta.XAML.UiGestureKind.Tap, position, default, 1, _tapCount);
        }

        if (_tapCount > 1 && (gestures & Delta.XAML.UiGestureKind.MultipleTap) != 0)
        {
            Publish(target, Delta.XAML.UiSemanticActionKind.MultipleTap, Delta.XAML.UiGestureKind.MultipleTap, position, default, 1, _tapCount);
        }
    }

    private void Cancel(ulong pointerId)
    {
        var index = Find(pointerId);
        if (index >= 0)
        {
            RemoveAt(index);
        }
    }

    private float InitialPairDistance(UiElement target, ulong pointerId, float2 position)
    {
        for (var i = 0; i < _activeCount; i++)
        {
            if (_active[i].PointerId != pointerId && ReferenceEquals(_active[i].Target, target))
            {
                var distance = Distance(position, _active[i].LastPosition);
                _active[i].PairDistance = distance;
                return distance;
            }
        }

        return 0;
    }

    private bool TryPair(int candidateIndex, out PointerCandidate other)
    {
        var target = _active[candidateIndex].Target;
        for (var i = 0; i < _activeCount; i++)
        {
            if (i != candidateIndex && ReferenceEquals(_active[i].Target, target))
            {
                other = _active[i];
                return true;
            }
        }

        other = default;
        return false;
    }

    private int Find(ulong pointerId)
    {
        for (var i = 0; i < _activeCount; i++)
        {
            if (_active[i].PointerId == pointerId)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveAt(int index)
    {
        _activeCount--;
        _active[index] = _active[_activeCount];
        _active[_activeCount] = default;
    }

    private void EnsureCapacity(int count)
    {
        if (_active.Length < count)
        {
            Array.Resize(ref _active, Math.Max(count, Math.Max(4, _active.Length * 2)));
        }
    }

    private void Publish(
        UiElement target,
        Delta.XAML.UiSemanticActionKind action,
        Delta.XAML.UiGestureKind kind,
        float2 position,
        float2 delta,
        float scale,
        int tapCount) =>
        _runtime.PublishSemantic(target, action, new(kind, position, delta, scale, tapCount));

    private static float Distance(float2 first, float2 second)
    {
        var delta = first - second;
        return MathF.Sqrt(delta.x * delta.x + delta.y * delta.y);
    }

    private struct PointerCandidate
    {
        internal PointerCandidate(
            ulong pointerId,
            UiElement target,
            long downTicks,
            float2 startPosition,
            float2 lastPosition,
            bool dragging,
            float pairDistance)
        {
            PointerId = pointerId;
            Target = target;
            DownTicks = downTicks;
            StartPosition = startPosition;
            LastPosition = lastPosition;
            Dragging = dragging;
            PairDistance = pairDistance;
        }

        internal ulong PointerId;
        internal UiElement Target;
        internal long DownTicks;
        internal float2 StartPosition;
        internal float2 LastPosition;
        internal bool Dragging;
        internal float PairDistance;
    }
}
