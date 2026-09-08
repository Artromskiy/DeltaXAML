using Delta.XAML.Contract;

namespace DeltaXAML.Internal;

/// <summary>Typed companion for common properties owned by every retained element.</summary>
internal static class UiElementPropertiesGenerated
{
    internal static bool TrySetWidth(ref UiElementState state, float value)
    {
        if (!ElementPlacementMixin.IsValidDimension(value))
        {
            return false;
        }

        if (!state.Width.Equals(value))
        {
            state.Width = value;
        }

        return true;
    }

    internal static bool TrySetHeight(ref UiElementState state, float value)
    {
        if (!ElementPlacementMixin.IsValidDimension(value))
        {
            return false;
        }

        if (!state.Height.Equals(value))
        {
            state.Height = value;
        }

        return true;
    }

    internal static bool TrySetMargin(ref UiElementState state, UiThickness value)
    {
        if (!ElementPlacementMixin.IsFinite(value))
        {
            return false;
        }

        if (state.Margin != value)
        {
            state.Margin = value;
        }

        return true;
    }

    internal static bool TrySetHorizontalAlignment(ref UiElementState state, Delta.XAML.UiHorizontalAlignment value)
    {
        if (value is Delta.XAML.UiHorizontalAlignment.Unknown)
        {
            return false;
        }

        if (state.HorizontalAlignment != value)
        {
            state.HorizontalAlignment = value;
        }

        return true;
    }

    internal static bool TrySetVerticalAlignment(ref UiElementState state, Delta.XAML.UiVerticalAlignment value)
    {
        if (value is Delta.XAML.UiVerticalAlignment.Unknown)
        {
            return false;
        }

        if (state.VerticalAlignment != value)
        {
            state.VerticalAlignment = value;
        }

        return true;
    }

    internal static bool TrySetBackground(ref UiElementState state, UiColor value)
    {
        if (state.Background == value)
        {
            return false;
        }

        state.Background = value;
        return true;
    }

    internal static bool TrySetBorderColor(ref UiElementState state, UiColor value)
    {
        if (state.BorderColor == value) { return false; }
        state.BorderColor = value;
        return true;
    }

    internal static bool TrySetEffectSet(ref UiElementState state, UiEffectSet value)
    {
        if (value != UiEffectSet.None && !value.IsValid)
        {
            return false;
        }

        if (state.EffectSet == value) { return false; }
        state.EffectSet = value;
        return true;
    }

    internal static bool TrySetBorderWidth(ref UiElementState state, float value)
    {
        if (!float.IsFinite(value) || value < 0) { return false; }
        if (state.BorderWidth.Equals(value)) { return false; }
        state.BorderWidth = value;
        return true;
    }

    internal static bool TrySetBorderWidthUnits(ref UiElementState state, Delta.XAML.Contract.PaintUnits value)
    {
        if (value is not (Delta.XAML.Contract.PaintUnits.Logical or Delta.XAML.Contract.PaintUnits.Device) ||
            state.BorderWidthUnits == value)
        {
            return false;
        }

        state.BorderWidthUnits = value;
        return true;
    }

    internal static bool TrySetCornerRadius(ref UiElementState state, Delta.XAML.UiCornerRadii value)
    {
        if (!value.IsFiniteNonNegative) { return false; }
        if (state.CornerRadius.Equals(value)) { return false; }
        state.CornerRadius = value;
        return true;
    }

    internal static bool TrySetPadding(ref UiElementState state, UiThickness value)
    {
        if (!ElementPlacementMixin.IsFinite(value))
        {
            return false;
        }

        if (state.Padding != value)
        {
            state.Padding = value;
        }

        return true;
    }

    internal static bool TrySetEnabled(ref UiElementState state, bool value)
    {
        if (state.IsEnabled == value)
        {
            return false;
        }

        state.IsEnabled = value;
        return true;
    }

    internal static bool TrySetSelected(ref UiElementState state, bool value)
    {
        if (state.IsSelected == value)
        {
            return false;
        }

        state.IsSelected = value;
        return true;
    }

}
