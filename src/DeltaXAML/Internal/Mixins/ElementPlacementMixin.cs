using Delta;

namespace DeltaXAML.Internal;

/// <summary>Stateless element-slot placement shared by every retained control.</summary>
internal readonly struct ElementPlacementMixin
{
    internal static bool IsValidDimension(float value) =>
        float.IsNaN(value) || (float.IsFinite(value) && value >= 0);

    internal static bool IsFinite(UiThickness value) =>
        float.IsFinite(value.Left) && float.IsFinite(value.Top) &&
        float.IsFinite(value.Right) && float.IsFinite(value.Bottom);

    internal static UiSize MeasureAvailable(UiElement element, UiSize available)
    {
        ArgumentNullException.ThrowIfNull(element);
        var margin = element.Margin;
        return new(
            Maths.Max(0, available.Width - margin.Horizontal),
            Maths.Max(0, available.Height - margin.Vertical));
    }

    internal static UiSize IncludeMargin(UiElement element, UiSize desired)
    {
        ArgumentNullException.ThrowIfNull(element);
        var margin = element.Margin;
        return new(desired.Width + margin.Horizontal, desired.Height + margin.Vertical);
    }

    internal static UiRect Arrange(UiElement element, UiRect slot)
    {
        ArgumentNullException.ThrowIfNull(element);
        var margin = element.Margin;
        var contentSlot = new UiRect(
            slot.X + margin.Left,
            slot.Y + margin.Top,
            Maths.Max(0, slot.Width - margin.Horizontal),
            Maths.Max(0, slot.Height - margin.Vertical));
        var desired = element.DesiredSize;
        var desiredWidth = Maths.Max(0, desired.Width - margin.Horizontal);
        var desiredHeight = Maths.Max(0, desired.Height - margin.Vertical);
        var width = ResolveLength(element.Width, desiredWidth, contentSlot.Width, element.HorizontalAlignment);
        var height = ResolveLength(element.Height, desiredHeight, contentSlot.Height, element.VerticalAlignment);

        return new(
            Align(contentSlot.X, contentSlot.Width, width, element.HorizontalAlignment),
            Align(contentSlot.Y, contentSlot.Height, height, element.VerticalAlignment),
            width,
            height);
    }

    private static float ResolveLength(
        float requested,
        float desired,
        float available,
        Delta.XAML.UiHorizontalAlignment alignment) =>
        float.IsNaN(requested) && alignment == Delta.XAML.UiHorizontalAlignment.Stretch
            ? available
            : float.IsNaN(requested) ? desired : Maths.Max(0, requested);

    private static float ResolveLength(
        float requested,
        float desired,
        float available,
        Delta.XAML.UiVerticalAlignment alignment) =>
        float.IsNaN(requested) && alignment == Delta.XAML.UiVerticalAlignment.Stretch
            ? available
            : float.IsNaN(requested) ? desired : Maths.Max(0, requested);

    private static float Align(
        float origin,
        float available,
        float size,
        Delta.XAML.UiHorizontalAlignment alignment) => alignment switch
        {
            Delta.XAML.UiHorizontalAlignment.Center => origin + (available - size) * 0.5f,
            Delta.XAML.UiHorizontalAlignment.End => origin + available - size,
            _ => origin,
        };

    private static float Align(
        float origin,
        float available,
        float size,
        Delta.XAML.UiVerticalAlignment alignment) => alignment switch
        {
            Delta.XAML.UiVerticalAlignment.Center => origin + (available - size) * 0.5f,
            Delta.XAML.UiVerticalAlignment.End => origin + available - size,
            _ => origin,
        };
}
