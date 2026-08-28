namespace DeltaXAML.Internal;

internal sealed class UiInputRouter : IUiInputRouter
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

    internal void Dispatch(in UiInputPacket packet)
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
        var target = _captured ?? (input.Kind is UiPointerEventKind.Leave or UiPointerEventKind.CaptureLost
            ? _hovered
            : _runtime.FindHit(input.Position));
        switch (input.Kind)
        {
            case UiPointerEventKind.Enter:
            case UiPointerEventKind.Move:
                SetHovered(target);
                break;
            case UiPointerEventKind.Leave:
                SetHovered(null);
                break;
            case UiPointerEventKind.Down:
                _captured = target;
                SetFocused(target);
                break;
            case UiPointerEventKind.Cancel:
            case UiPointerEventKind.CaptureLost:
                _captured = null;
                SetHovered(null);
                break;
        }

        if (target is not null)
        {
            Raise(target, new UiRoutedEvent(target.Id, UiRoutedEventPhase.Preview, input.Kind, input.Position, input.WheelDelta));
            Raise(target, new UiRoutedEvent(target.Id, UiRoutedEventPhase.Bubble, input.Kind, input.Position, input.WheelDelta));
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
            var packet = UiInputPacket.From(input);
            UiDescriptorCatalog.ProcessInput(text, in packet);
        }
    }

    public void RouteText(in UiTextInput input)
    {
        PruneDetachedState();
        if (_focused is TextBox text)
        {
            var packet = UiInputPacket.From(input);
            UiDescriptorCatalog.ProcessInput(text, in packet);
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
        _runtime.CollectRoute(target, _routePath);

        if (routedEvent.Phase == UiRoutedEventPhase.Preview)
        {
            for (var i = _routePath.Count - 1; i >= 0; i--)
            {
                UiDescriptorCatalog.ProcessRoutedEvent(_routePath[i], in routedEvent);
            }
        }
        else
        {
            for (var i = 0; i < _routePath.Count; i++)
            {
                UiDescriptorCatalog.ProcessRoutedEvent(_routePath[i], in routedEvent);
            }
        }
    }
}
