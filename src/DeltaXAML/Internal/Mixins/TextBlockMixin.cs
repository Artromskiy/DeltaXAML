using Delta.XAML.Contract;

namespace DeltaXAML.Internal;

internal interface IMeasureMixin<TState>
    where TState : struct
{
    static abstract void Measure(ref TState state, in UiMeasureContext context);
}

internal interface IArrangeMixin<TState>
    where TState : struct
{
    static abstract void Arrange(ref TState state, in UiArrangeContext context);
}

internal interface IInputMixin<TState>
    where TState : struct
{
    static abstract bool ProcessInput(ref TState state, in UiInputEvent input);
}

internal interface IVisualMixin<TState>
    where TState : struct
{
    static abstract UiTextRun EmitVisual(ref TState state, in UiTextVisualContext context);
}

internal readonly struct TextBlockMeasureMixin : IMeasureMixin<TextBlockState>
{
    public static void Measure(ref TextBlockState state, in UiMeasureContext context)
    {
        var scale = context.DpiScale > 0 ? context.DpiScale : 1;
        var size = state.Visual.FontSize * scale;
        var lineHeight = state.Layout.LineHeight > 0 ? state.Layout.LineHeight * scale : size * 1.25f;
        var naturalWidth = state.Text.Length * size * 0.55f;
        var availableWidth = MathF.Max(0, context.Available.Width);
        var lineCount = 1;
        if (state.Layout.Wrapping is not (Delta.XAML.UiTextWrapping.Unknown or Delta.XAML.UiTextWrapping.NoWrap) &&
            float.IsFinite(availableWidth) && availableWidth > 0 && naturalWidth > availableWidth)
        {
            lineCount = Math.Max(1, (int)MathF.Ceiling(naturalWidth / availableWidth));
        }

        if (state.Layout.MaxLines > 0)
        {
            lineCount = Math.Min(lineCount, state.Layout.MaxLines);
        }

        var desiredWidth = state.Layout.Wrapping is not (Delta.XAML.UiTextWrapping.Unknown or Delta.XAML.UiTextWrapping.NoWrap)
            ? MathF.Min(availableWidth, naturalWidth)
            : naturalWidth;
        state.Layout.DesiredSize = new(MathF.Min(availableWidth, desiredWidth), lineCount * lineHeight);
    }
}

internal readonly struct TextBlockArrangeMixin : IArrangeMixin<TextBlockState>
{
    public static void Arrange(ref TextBlockState state, in UiArrangeContext context)
    {
        state.Layout.Bounds = context.Bounds;
        state.Layout.Clip = context.Clip;
        var textWidth = MathF.Min(state.Layout.DesiredSize.Width, MathF.Max(0, context.Bounds.Width));
        var textHeight = MathF.Min(state.Layout.DesiredSize.Height, MathF.Max(0, context.Bounds.Height));
        var x = context.Bounds.X;
        var y = context.Bounds.Y;
        if (state.Layout.HorizontalAlignment == Delta.XAML.UiTextHorizontalAlignment.Center)
        {
            x += (context.Bounds.Width - textWidth) * 0.5f;
        }
        else if (state.Layout.HorizontalAlignment == Delta.XAML.UiTextHorizontalAlignment.Right)
        {
            x += context.Bounds.Width - textWidth;
        }

        if (state.Layout.VerticalAlignment == Delta.XAML.UiTextVerticalAlignment.Center)
        {
            y += (context.Bounds.Height - textHeight) * 0.5f;
        }
        else if (state.Layout.VerticalAlignment == Delta.XAML.UiTextVerticalAlignment.Bottom)
        {
            y += context.Bounds.Height - textHeight;
        }

        state.Layout.TextBounds = new(x, y, textWidth, textHeight);
    }
}

internal readonly struct TextBlockInputMixin : IInputMixin<TextBlockState>
{
    public static bool ProcessInput(ref TextBlockState state, in UiInputEvent input) => false;
}

internal readonly struct TextBlockVisualMixin : IVisualMixin<TextBlockState>
{
    public static UiTextRun EmitVisual(ref TextBlockState state, in UiTextVisualContext context) =>
        new(
            state.Visual.FontKey,
            state.Visual.FontSize * context.LayoutScale,
            state.Text,
            state.Visual.GlyphRunKey,
            state.Visual.Foreground,
            state.Layout.Bounds,
            state.Layout.Clip,
            context.Owner,
            context.OwnerGeneration,
            context.Version,
            default,
            state.Visual.OutlineColor,
            state.Visual.OutlineWidth,
            state.Visual.TextEffectResource)
        {
            TextBounds = state.Layout.TextBounds,
            HorizontalAlignment = state.Layout.HorizontalAlignment,
            VerticalAlignment = state.Layout.VerticalAlignment,
            Wrapping = state.Layout.Wrapping,
            Trimming = state.Layout.Trimming,
            MaxLines = state.Layout.MaxLines,
            LineHeight = state.Layout.LineHeight > 0 ? state.Layout.LineHeight * context.LayoutScale : 0,
            Weight = state.Visual.Weight,
            Style = state.Visual.Style,
            Decorations = state.Visual.Decorations,
        };
}
