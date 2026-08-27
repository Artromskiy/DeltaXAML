namespace DeltaXAML.Internal;

/// <summary>Typed companion for common properties owned by every retained element.</summary>
internal static class UiElementPropertiesGenerated
{
    internal static bool TrySetWidth(ref UiElementState state, float value)
    {
        if (state.Width.Equals(value))
        {
            return false;
        }

        state.Width = value;
        return true;
    }

    internal static bool TrySetHeight(ref UiElementState state, float value)
    {
        if (state.Height.Equals(value))
        {
            return false;
        }

        state.Height = value;
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

    internal static bool TrySetPadding(ref UiElementState state, UiThickness value)
    {
        if (state.Padding == value)
        {
            return false;
        }

        state.Padding = value;
        return true;
    }

    internal static bool TrySetFill(ref UiElementState state, bool value)
    {
        if (state.Fill == value)
        {
            return false;
        }

        state.Fill = value;
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
