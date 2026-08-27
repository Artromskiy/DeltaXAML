namespace DeltaXAML.Internal;

/// <summary>Compact generated type identity used by the retained descriptor path.</summary>
internal readonly record struct UiRuntimeTypeIndex(ushort Value)
{
    internal bool IsValid => Value != 0;
}

[Flags]
internal enum UiDescriptorCapabilities : byte
{
    None = 0,
    Factory = 1 << 0,
    Measure = 1 << 1,
    Arrange = 1 << 2,
    Input = 1 << 3,
    Visual = 1 << 4,
    PropertySetters = 1 << 5,
}

internal readonly record struct UiTypeDescriptor(UiRuntimeTypeIndex Index, UiDescriptorCapabilities Capabilities)
{
    internal bool IsValid => Index.IsValid && Capabilities != UiDescriptorCapabilities.None;

    internal bool Supports(UiDescriptorCapabilities capabilities) =>
        (Capabilities & capabilities) == capabilities;
}

/// <summary>Compact generated descriptor catalog; entries are immutable metadata only.</summary>
internal static class UiDescriptorCatalog
{
    private static readonly UiTypeDescriptor[] Entries =
    [
        UiTextBlockGenerated.Descriptor,
        UiStackPanelGenerated.Descriptor,
        UiBorderGenerated.Descriptor,
        UiContentControlGenerated.Descriptor,
        UiPanelGenerated.Descriptor,
        UiGridGenerated.Descriptor,
        UiButtonGenerated.Descriptor,
        UiToggleButtonGenerated.Descriptor,
        UiTextBoxGenerated.Descriptor,
        UiNumericEditorGenerated.Descriptor,
        UiItemsControlGenerated.Descriptor,
        UiScrollViewerGenerated.Descriptor,
    ];

    internal static bool TryResolve(UiRuntimeTypeIndex index, out UiTypeDescriptor descriptor)
    {
        if (!index.IsValid)
        {
            descriptor = default;
            return false;
        }

        var position = index.Value - 1;
        if ((uint)position >= (uint)Entries.Length)
        {
            descriptor = default;
            return false;
        }

        descriptor = Entries[position];
        return true;
    }
}

/// <summary>Typed companion for <see cref="TextBlock"/>; the generated path owns no instance state.</summary>
internal static class UiTextBlockGenerated
{
    internal static readonly UiTypeDescriptor Descriptor = new(
        new(1),
        UiDescriptorCapabilities.Factory |
        UiDescriptorCapabilities.Measure |
        UiDescriptorCapabilities.Arrange |
        UiDescriptorCapabilities.Input |
        UiDescriptorCapabilities.Visual |
        UiDescriptorCapabilities.PropertySetters);

    internal static TextBlock Create() => new();

    internal static void Measure(ref TextBlockState state, in UiMeasureContext context) =>
        TextBlockMeasureMixin.Measure(ref state, in context);

    internal static void Arrange(ref TextBlockState state, in UiArrangeContext context) =>
        TextBlockArrangeMixin.Arrange(ref state, in context);

    internal static bool ProcessInput(ref TextBlockState state, in UiInputPacket input) =>
        TextBlockInputMixin.ProcessInput(ref state, in input);

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
        if (state.Visual.FontKey == value)
        {
            return false;
        }

        state.Visual.FontKey = value;
        return true;
    }

    internal static bool TrySetFontSize(ref TextBlockState state, float value)
    {
        if (state.Visual.FontSize.Equals(value))
        {
            return false;
        }

        state.Visual.FontSize = value;
        return true;
    }

    internal static bool TrySetForeground(ref TextBlockState state, UiColor value)
    {
        if (state.Visual.Foreground == value)
        {
            return false;
        }

        state.Visual.Foreground = value;
        return true;
    }
}
