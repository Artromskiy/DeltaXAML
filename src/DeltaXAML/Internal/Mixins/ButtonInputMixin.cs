namespace DeltaXAML.Internal;

internal interface IButtonInputMixin<TState>
    where TState : struct
{
    static abstract bool Process(ref TState state, in UiRoutedEvent routedEvent);
}

internal readonly struct ButtonInputMixin : IButtonInputMixin<ButtonState>
{
    public static bool Process(ref ButtonState state, in UiRoutedEvent routedEvent)
    {
        if (routedEvent.Phase != UiRoutedEventPhase.Bubble)
        {
            return false;
        }

        if (routedEvent.Kind == UiPointerEventKind.Down)
        {
            state.IsPressed = true;
            return false;
        }

        if (routedEvent.Kind == UiPointerEventKind.Up)
        {
            state.IsPressed = false;
            return true;
        }

        return false;
    }
}

internal readonly struct ToggleButtonInputMixin : IButtonInputMixin<ToggleButtonState>
{
    public static bool Process(ref ToggleButtonState state, in UiRoutedEvent routedEvent)
    {
        if (routedEvent.Phase == UiRoutedEventPhase.Bubble && routedEvent.Kind == UiPointerEventKind.Up)
        {
            state.IsChecked = !state.IsChecked;
            return true;
        }

        return false;
    }
}
