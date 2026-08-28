using Delta.XAML.Contract;

namespace DeltaXAML.Internal;

internal sealed class UiInputRouter
{
    private readonly UiRuntime _runtime;
    private readonly List<UiElement> _focusable = new();
    private readonly List<UiElement> _routePath = new();
    private readonly UiGestureArena _gestures;
    private UiElement? _focused;
    private UiElement? _captured;
    private UiElement? _hovered;

    public UiInputRouter(UiRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        _gestures = new(runtime);
    }

    public UiElementId? Focused => _focused?.Id;
    public UiElementId? Captured => _captured?.Id;

    internal void Dispatch(in Delta.XAML.UiInputSample sample)
    {
        var packet = sample.Event;
        switch (packet.Kind)
        {
            case UiInputEventKind.PointingDevice:
                var pointer = packet.PointingDevice;
                RoutePointer(in pointer, sample.Timestamp.Ticks);
                break;
            case UiInputEventKind.Key:
                var key = packet.Key;
                RouteKey(in key);
                break;
            case UiInputEventKind.Text:
                var text = packet.Text;
                RouteText(in text);
                break;
            case UiInputEventKind.Composition:
                var ime = packet.Composition;
                RouteIme(in ime);
                break;
        }
    }

    public void Focus(UiElementId? element)
    {
        var next = element is { } id &&
                   _runtime.TryResolve(id, out var resolved) &&
                   CanReceiveFocus(resolved)
            ? resolved
            : null;
        SetFocused(next);
    }

    internal void RepairFocusAndCapture() => PruneDetachedState();

    public void RoutePointer(in UiPointerEvent input, long timestampTicks = 0)
    {
        PruneDetachedState();
        var point = new UiPoint(input.Position.x, input.Position.y);
        var target = _captured ?? (input.Kind is UiPointerEventKind.Leave or UiPointerEventKind.CaptureLost
            ? _hovered
            : _runtime.FindHit(point));
        switch (input.Kind)
        {
            case UiPointerEventKind.Enter:
            case UiPointerEventKind.Move:
                SetHovered(target);
                break;
            case UiPointerEventKind.Leave:
                SetHovered(null);
                break;
            case UiPointerEventKind.ButtonDown:
                _captured = target;
                SetFocused(FindFocusable(target));
                break;
            case UiPointerEventKind.Cancel:
            case UiPointerEventKind.CaptureLost:
                _captured = null;
                SetHovered(null);
                break;
        }

        if (target is not null)
        {
            Raise(target, new UiRoutedEvent(target.Id, UiRoutedEventPhase.Preview, input.Kind, input.Position, input.WheelDelta, target));
            Raise(target, new UiRoutedEvent(target.Id, UiRoutedEventPhase.Bubble, input.Kind, input.Position, input.WheelDelta, target));
        }

        _gestures.Process(target, in input, timestampTicks);

        if (input.Kind == UiPointerEventKind.ButtonUp)
        {
            if (target is RichTextBlock richText &&
                richText.TryGetLink(point, out var command, out var argument))
            {
                _runtime.PublishSemantic(richText, command, Delta.XAML.UiSemanticActionKind.Hyperlink, argument);
            }

            PublishButtonCommand(target);

            _captured = null;
        }
    }

    public void RouteKey(in UiKeyEvent input)
    {
        PruneDetachedState();
        if (input.Kind != UiKeyEventKind.Down)
        {
            return;
        }

        if (input.PhysicalKey.Value == 9)
        {
            FocusNext(input.Modifiers.Contains(UiModifierBits.Shift));
            return;
        }


        if (_focused is not null)
        {
            _runtime.CollectRoute(_focused, _routePath);
            for (var i = 0; i < _routePath.Count; i++)
            {
                var key = _routePath[i].CommandKey;
                if (_routePath[i].Command.IsValid && key.Key == input.PhysicalKey && key.Modifiers == input.Modifiers)
                {
                    _runtime.PublishSemantic(_routePath[i], Delta.XAML.UiSemanticActionKind.Command, default);
                    return;
                }
            }

            var packet = UiInputEvent.FromKey(input);
            for (var i = 0; i < _routePath.Count; i++)
            {
                _runtime.EnqueueControlInput(_routePath[i], in packet);
            }

            if (input.PhysicalKey.Value is 13 or 32)
            {
                PublishButtonCommand(_focused);
            }
        }
    }

    public void RouteText(in UiTextInput input)
    {
        PruneDetachedState();
        if (_focused is TextBox or NumericEditor)
        {
            var packet = UiInputEvent.FromText(input);
            _runtime.EnqueueControlInput(_focused, in packet);
        }
    }

    public void RouteIme(in UiCompositionEvent input)
    {
        PruneDetachedState();
        if (_focused is TextBox or NumericEditor)
        {
            var packet = UiInputEvent.FromComposition(input);
            _runtime.EnqueueControlInput(_focused, in packet);
        }
    }

    private void PruneDetachedState()
    {
        if (_focused is not null && (!_runtime.Contains(_focused) || !_focused.IsEnabled || _focused.Visibility != UiVisibility.Visible))
        {
            SetFocused(null);
        }

        if (_captured is not null && (!_runtime.Contains(_captured) || !_captured.IsEnabled || _captured.Visibility != UiVisibility.Visible))
        {
            var captured = _captured;
            _captured = null;
            _gestures.Cancel(captured);
            var captureLost = new UiRoutedEvent(
                captured.Id,
                UiRoutedEventPhase.Bubble,
                UiPointerEventKind.CaptureLost,
                default,
                default,
                captured);
            UiDescriptorCatalog.ProcessRoutedEvent(captured, in captureLost);
        }

        if (_hovered is not null && (!_runtime.Contains(_hovered) || !_hovered.IsEnabled || _hovered.Visibility != UiVisibility.Visible))
        {
            SetHovered(null);
        }
    }

    private void FocusNext(bool reverse)
    {
        _focusable.Clear();
        UiElement? scope = null;
        if (_focused is not null)
        {
            _runtime.CollectRoute(_focused, _routePath);
            for (var i = 0; i < _routePath.Count; i++)
            {
                if (_routePath[i].IsFocusScope)
                {
                    scope = _routePath[i];
                    break;
                }
            }
        }

        _runtime.CollectFocusable(_focusable, scope);
        var index = _focused is null ? -1 : _focusable.IndexOf(_focused);
        if (_focusable.Count == 0)
        {
            SetFocused(null);
            return;
        }

        var next = reverse
            ? index <= 0 ? _focusable.Count - 1 : index - 1
            : (index + 1) % _focusable.Count;
        SetFocused(_focusable[next]);
    }

    private void SetFocused(UiElement? next)
    {
        if (ReferenceEquals(_focused, next))
        {
            return;
        }

        _focused?.SetFocused(false);
        _focused = next;
        _focused?.SetFocused(true);
    }

    private void SetHovered(UiElement? next)
    {
        if (ReferenceEquals(_hovered, next))
        {
            return;
        }

        _hovered?.SetHovered(false);
        _hovered = next;
        _hovered?.SetHovered(true);
    }

    private static bool CanReceiveFocus(UiElement element) =>
        element.Focusable &&
        element.IsEnabled &&
        element.Visibility == UiVisibility.Visible &&
        (element.Participation & Delta.XAML.UiParticipation.Layout) != 0;

    private static UiElement? FindFocusable(UiElement? target)
    {
        for (var current = target; current is not null; current = current.Parent)
        {
            if (CanReceiveFocus(current))
            {
                return current;
            }
        }

        return null;
    }

    private void PublishButtonCommand(UiElement? target)
    {
        if (target is null)
        {
            return;
        }

        _runtime.CollectRoute(target, _routePath);
        for (var i = 0; i < _routePath.Count; i++)
        {
            if (_routePath[i] is Button or ToggleButton)
            {
                if (_routePath[i].Command.IsValid && _routePath[i].Gestures == Delta.XAML.UiGestureKind.None)
                {
                    _runtime.PublishSemantic(_routePath[i], Delta.XAML.UiSemanticActionKind.Command, default);
                }

                return;
            }
        }
    }

    private void Raise(UiElement target, in UiRoutedEvent routedEvent)
    {
        _runtime.CollectRoute(target, _routePath);

        if (routedEvent.Phase == UiRoutedEventPhase.Preview)
        {
            for (var i = _routePath.Count - 1; i >= 0; i--)
            {
                _runtime.EnqueueRoutedEvent(_routePath[i], in routedEvent);
            }
        }
        else
        {
            for (var i = 0; i < _routePath.Count; i++)
            {
                _runtime.EnqueueRoutedEvent(_routePath[i], in routedEvent);
            }
        }
    }
}
