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

    internal static bool TryResolve(UiElement element, out UiRuntimeTypeIndex index)
    {
        ArgumentNullException.ThrowIfNull(element);
        index = element switch
        {
            NumericEditor => new UiRuntimeTypeIndex(10),
            TextBox => new UiRuntimeTypeIndex(9),
            TextBlock => new UiRuntimeTypeIndex(1),
            StackPanel => new UiRuntimeTypeIndex(2),
            Border => new UiRuntimeTypeIndex(3),
            Grid => new UiRuntimeTypeIndex(6),
            ToggleButton => new UiRuntimeTypeIndex(8),
            Button => new UiRuntimeTypeIndex(7),
            ScrollViewer => new UiRuntimeTypeIndex(12),
            ContentControl => new UiRuntimeTypeIndex(4),
            ItemsControl => new UiRuntimeTypeIndex(11),
            Panel => new UiRuntimeTypeIndex(5),
            _ => default,
        };
        return index.IsValid;
    }

    internal static UiSize Measure(UiElement element, in UiMeasureContext context)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (element)
        {
            case TextBlock text:
                UiTextBlockGenerated.Measure(ref text.State, in context);
                return text.State.Layout.DesiredSize;
            case StackPanel stack:
                UiStackPanelGenerated.Measure(ref stack.State, in context);
                return stack.State.DesiredSize;
            case Border border:
                border.State.Padding = border.Padding;
                UiBorderGenerated.Measure(ref border.State, in context);
                return border.State.DesiredSize;
            case Grid grid:
                UiGridGenerated.Measure(ref grid.State, in context);
                return grid.State.DesiredSize;
            case ScrollViewer scroll:
                UiScrollViewerGenerated.Measure(ref scroll.State, in context);
                return scroll.State.DesiredSize;
            case Panel panel:
                UiPanelGenerated.Measure(ref panel.State, in context);
                return panel.State.DesiredSize;
            case ContentControl content:
                UiContentControlGenerated.Measure(ref content.State, in context);
                return content.State.DesiredSize;
            default:
                return default;
        }
    }

    internal static UiSize ChildMeasureAvailable(UiElement element, UiSize available)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is Border border)
        {
            border.State.Padding = border.Padding;
            return UiBorderGenerated.ChildMeasureAvailable(ref border.State, available);
        }

        return available;
    }

    internal static bool TryGetTextRun(
        UiRuntimeTypeIndex type,
        UiElement element,
        in UiTextVisualContext context,
        out UiTextRun run)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (type.Value is 1 or 9 or 10 && element is TextBlock text)
        {
            run = UiTextBlockGenerated.EmitVisual(ref text.State, in context);
            return true;
        }

        run = default;
        return false;
    }

    internal static void ProcessRoutedEvent(UiElement element, in UiRoutedEvent routedEvent)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (element)
        {
            case ToggleButton toggle:
                var wasPressed = toggle.InputState.IsPressed;
                var clicked = UiButtonGenerated.Process(ref toggle.InputState, in routedEvent);
                var toggled = UiToggleButtonGenerated.Process(ref toggle.State, in routedEvent);
                toggle.ApplyInputResult(wasPressed, clicked, toggled);
                break;
            case Button button:
                var buttonWasPressed = button.InputState.IsPressed;
                var buttonClicked = UiButtonGenerated.Process(ref button.InputState, in routedEvent);
                button.ApplyInputResult(buttonWasPressed, buttonClicked, false);
                break;
            case ScrollViewer scroll when routedEvent.Phase == UiRoutedEventPhase.Bubble && routedEvent.Kind == UiPointerEventKind.Wheel:
                scroll.ApplyWheelInput(-routedEvent.WheelDelta);
                break;
        }
    }

    internal static bool Arrange(UiElement element, in UiArrangeContext context)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (element)
        {
            case TextBlock text:
                UiTextBlockGenerated.Arrange(ref text.State, in context);
                return true;
            case StackPanel stack:
                UiStackPanelGenerated.Arrange(ref stack.State, in context);
                return true;
            case Border border:
                border.State.Padding = border.Padding;
                UiBorderGenerated.Arrange(ref border.State, in context);
                return true;
            case Grid grid:
                UiGridGenerated.Arrange(ref grid.State, in context);
                return true;
            case ScrollViewer scroll:
                UiScrollViewerGenerated.Arrange(ref scroll.State, in context);
                return true;
            case Panel panel:
                UiPanelGenerated.Arrange(ref panel.State, in context);
                return true;
            case ContentControl content:
                UiContentControlGenerated.Arrange(ref content.State, in context);
                return true;
            default:
                return false;
        }
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
