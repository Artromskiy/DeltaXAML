namespace DeltaXAML.Internal;

internal interface IMeasureMixin<TState>
    where TState : struct
{
    static abstract void Measure(ref TState state, in UiTextMeasureContext context);
}

internal interface IVisualMixin<TState>
    where TState : struct
{
    static abstract UiTextRun EmitVisual(ref TState state, in UiTextVisualContext context);
}

internal readonly struct TextBlockMeasureMixin : IMeasureMixin<TextBlockState>
{
    public static void Measure(ref TextBlockState state, in UiTextMeasureContext context)
    {
        var size = state.FontSize * context.DpiScale;
        state.DesiredSize = new(MathF.Min(context.Available.Width, state.Text.Length * size * 0.55f), size * 1.25f);
    }
}

internal readonly struct TextBlockVisualMixin : IVisualMixin<TextBlockState>
{
    public static UiTextRun EmitVisual(ref TextBlockState state, in UiTextVisualContext context) =>
        new(state.FontKey, state.FontSize * context.LayoutScale, state.Text, context.GlyphRunKey, state.Foreground, context.Bounds, context.Clip, context.Owner, context.OwnerGeneration, context.Version);
}
