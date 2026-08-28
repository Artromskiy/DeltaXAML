namespace DeltaXAML.Internal;

internal sealed class UiInputRouter : IUiInputRouter, IUiInputDispatcher
{
    private readonly UiRuntime _runtime;
    private readonly List<UiElement> _focusable = new();
    private readonly List<UiElement> _routePath = new();
    private UiElement? _focused;
    private UiElement? _captured;
    private UiElement? _hovered;

    public UiInputRouter(UiRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    public UiElementId? Focused => _focused?.Id;
    public UiElementId? Captured => _captured?.Id;

    public void Dispatch(in UiInputPacket packet)
    {
        switch (packet.Kind)
        {
            case UiInputPacketKind.Spatial:
                var pointer = packet.Spatial;
                RoutePointer(in pointer);
                break;
            case UiInputPacketKind.Key:
                var key = packet.Key;
                RouteKey(in key);
                break;
            case UiInputPacketKind.Text:
                var text = packet.Text;
                RouteText(in text);
                break;
            case UiInputPacketKind.Ime:
                var ime = packet.Ime;
                RouteIme(in ime);
                break;
        }
    }

    public void Focus(UiElementId? element)
    {
        var next = element is { } id && _runtime.TryResolve(id, out var resolved) ? resolved : null;
        SetFocused(next);
    }

    internal void RepairFocusAndCapture() => PruneDetachedState();

    public void RoutePointer(in UiPointerEvent input)
    {
        PruneDetachedState();
        var target = _captured ?? _runtime.FindHit(input.Position);
        if (input.Kind == UiPointerEventKind.Move)
        {
            SetHovered(target);
        }

        if (input.Kind == UiPointerEventKind.Down)
        {
            _captured = target;
            SetFocused(target);
        }

        if (target is not null)
        {
            Raise(target, new UiRoutedEvent(target.Id, UiRoutedEventPhase.Preview, input.Kind, input.Position));
            Raise(target, new UiRoutedEvent(target.Id, UiRoutedEventPhase.Bubble, input.Kind, input.Position));
        }

        if (input.Kind == UiPointerEventKind.Up)
        {
            _captured = null;
        }
    }

    public void RouteKey(in UiKeyEvent input)
    {
        PruneDetachedState();
        if (!input.IsDown)
        {
            return;
        }

        if (input.PhysicalKey == 9)
        {
            FocusNext();
            return;
        }

        if (_focused is TextBox text)
        {
            text.ApplyKey(input);
        }
    }

    public void RouteText(in UiTextInput input)
    {
        PruneDetachedState();
        if (_focused is TextBox text)
        {
            text.ApplyText(input);
        }
    }

    public void RouteIme(in UiImeComposition input)
    {
        PruneDetachedState();
        if (input.IsCommitted)
        {
            RouteText(new UiTextInput(input.Text));
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
            _captured = null;
        }

        if (_hovered is not null && (!_runtime.Contains(_hovered) || !_hovered.IsEnabled || _hovered.Visibility != UiVisibility.Visible))
        {
            SetHovered(null);
        }
    }

    private void FocusNext()
    {
        _focusable.Clear();
        _runtime.CollectFocusable(_focusable);
        var index = _focused is null ? -1 : _focusable.IndexOf(_focused);
        SetFocused(_focusable.Count == 0 ? null : _focusable[(index + 1) % _focusable.Count]);
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

    private void Raise(UiElement target, in UiRoutedEvent routedEvent)
    {
        _routePath.Clear();
        for (UiElement? node = target; node is not null; node = node.Parent as UiElement)
        {
            _routePath.Add(node);
        }

        if (routedEvent.Phase == UiRoutedEventPhase.Preview)
        {
            for (var i = _routePath.Count - 1; i >= 0; i--)
            {
                if (_routePath[i] is IUiRoutedEventSink preview)
                {
                    preview.OnRoutedEvent(routedEvent);
                }
            }
        }
        else
        {
            for (var i = 0; i < _routePath.Count; i++)
            {
                if (_routePath[i] is IUiRoutedEventSink bubble)
                {
                    bubble.OnRoutedEvent(routedEvent);
                }
            }
        }
    }
}
