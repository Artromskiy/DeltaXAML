namespace DeltaXAML.Internal;

/// <summary>Compact generated type identity used by the retained descriptor path.</summary>
internal readonly record struct UiRuntimeTypeIndex(ushort Value);

[Flags]
internal enum UiDescriptorCapabilities : byte
{
    None = 0,
    Factory = 1 << 0,
    Measure = 1 << 1,
    Visual = 1 << 2,
    PropertySetters = 1 << 3,
}

internal readonly record struct UiTypeDescriptor(UiRuntimeTypeIndex Index, UiDescriptorCapabilities Capabilities);

/// <summary>Typed companion for <see cref="TextBlock"/>; the generated path owns no instance state.</summary>
internal static class UiTextBlockGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(1),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Visual |
        UiDescriptorCapabilities.PropertySetters);

    internal static TextBlock Create() => new();

    internal static void Measure(ref TextBlockState state, in UiTextMeasureContext context) =>
        TextBlockMeasureMixin.Measure(ref state, in context);

    internal static UiTextRun EmitVisual(ref TextBlockState state, in UiTextVisualContext context) =>
        TextBlockVisualMixin.EmitVisual(ref state, in context);

    internal static bool TrySetText(ref TextBlockState state, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (state.Text == value)
        {
            return false;
        }

        state.Text = value;
        return true;
    }

    internal static bool TrySetFontKey(ref TextBlockState state, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (state.FontKey == value)
        {
            return false;
        }

        state.FontKey = value;
        return true;
    }

    internal static bool TrySetFontSize(ref TextBlockState state, float value)
    {
        if (state.FontSize.Equals(value))
        {
            return false;
        }

        state.FontSize = value;
        return true;
    }

    internal static bool TrySetForeground(ref TextBlockState state, UiColor value)
    {
        if (state.Foreground == value)
        {
            return false;
        }

        state.Foreground = value;
        return true;
    }
}
