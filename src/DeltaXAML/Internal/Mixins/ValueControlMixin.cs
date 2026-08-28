using Delta.XAML.Contract;

namespace DeltaXAML.Internal;

internal readonly struct SliderMeasureMixin : IMeasureMixin<SliderState>
{
    public static void Measure(ref SliderState state, in UiMeasureContext context)
    {
        state.DesiredSize = state.Orientation == UiOrientation.Horizontal
            ? new UiSize(MathF.Min(120, context.Available.Width), 20)
            : new UiSize(20, MathF.Min(120, context.Available.Height));
    }
}

internal readonly struct SliderArrangeMixin : IArrangeMixin<SliderState>
{
    public static void Arrange(ref SliderState state, in UiArrangeContext context)
    {
        state.Bounds = context.Bounds;
        state.Clip = context.Clip;
    }
}

internal readonly struct SliderInputMixin
{
    public static bool TryGetPointerValue(ref SliderState state, in UiRoutedEvent input, out double value)
    {
        value = state.Value;
        if (input.Phase != UiRoutedEventPhase.Bubble ||
            input.Kind is not (UiPointerEventKind.ButtonDown or UiPointerEventKind.Move) ||
            state.Maximum <= state.Minimum)
        {
            return false;
        }

        var position = state.Orientation == UiOrientation.Horizontal
            ? (input.Position.x - state.Bounds.X) / MathF.Max(1, state.Bounds.Width)
            : 1 - ((input.Position.y - state.Bounds.Y) / MathF.Max(1, state.Bounds.Height));
        value = state.Minimum + Math.Clamp(position, 0, 1) * (state.Maximum - state.Minimum);
        return !value.Equals(state.Value);
    }

    public static bool TryGetKeyValue(ref SliderState state, in UiKeyEvent input, out double value)
    {
        value = state.Value;
        if (input.Kind != UiKeyEventKind.Down)
        {
            return false;
        }

        var direction = input.PhysicalKey.Value switch
        {
            37 or 40 => -1,
            38 or 39 => 1,
            _ => 0,
        };
        if (direction == 0)
        {
            return false;
        }

        value = Math.Clamp(state.Value + direction * state.Step, state.Minimum, state.Maximum);
        return !value.Equals(state.Value);
    }
}

internal readonly struct ImageMeasureMixin : IMeasureMixin<ImageState>
{
    public static void Measure(ref ImageState state, in UiMeasureContext context)
    {
        state.DesiredSize = new(
            MathF.Min(state.IntrinsicWidth, context.Available.Width),
            MathF.Min(state.IntrinsicHeight, context.Available.Height));
    }
}

internal readonly struct ImageArrangeMixin : IArrangeMixin<ImageState>
{
    public static void Arrange(ref ImageState state, in UiArrangeContext context)
    {
        state.Bounds = context.Bounds;
        state.Clip = context.Clip;
    }
}
