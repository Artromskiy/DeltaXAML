using Delta.XAML.Contract;

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
    private static readonly Delta.XAML.UiTypeId[] TypeIdentities =
    [
        new(new Guid("22222222-2222-2222-2222-22222222220A")),
        new(new Guid("22222222-2222-2222-2222-222222222205")),
        new(new Guid("22222222-2222-2222-2222-222222222202")),
        new(new Guid("22222222-2222-2222-2222-222222222203")),
        new(new Guid("22222222-2222-2222-2222-222222222201")),
        new(new Guid("22222222-2222-2222-2222-222222222206")),
        new(new Guid("22222222-2222-2222-2222-222222222208")),
        new(new Guid("22222222-2222-2222-2222-222222222209")),
        new(new Guid("22222222-2222-2222-2222-22222222220B")),
        new(new Guid("22222222-2222-2222-2222-22222222220C")),
        new(new Guid("22222222-2222-2222-2222-222222222207")),
        new(new Guid("22222222-2222-2222-2222-222222222204")),
    ];

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

    internal static bool MatchesType(UiElement element, Delta.XAML.UiTypeId expected)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!expected.IsValid || !TryResolve(element, out var index))
        {
            return false;
        }

        var position = index.Value - 1;
        return (uint)position < (uint)TypeIdentities.Length && TypeIdentities[position] == expected;
    }

    internal static bool TrySetProperty(UiElement element, UiPropertyKey key, UiValue value)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(value);
        switch (key)
        {
            case UiPropertyKey.Width when value.UntypedValue is float width:
                UiElementPropertiesGenerated.TrySetWidth(ref element.CommonState, width);
                return true;
            case UiPropertyKey.Height when value.UntypedValue is float height:
                UiElementPropertiesGenerated.TrySetHeight(ref element.CommonState, height);
                return true;
            case UiPropertyKey.Background when value.UntypedValue is UiColor background:
                UiElementPropertiesGenerated.TrySetBackground(ref element.CommonState, background);
                return true;
            case UiPropertyKey.Padding when value.UntypedValue is UiThickness padding:
                UiElementPropertiesGenerated.TrySetPadding(ref element.CommonState, padding);
                return true;
            case UiPropertyKey.Fill when value.UntypedValue is bool fill:
                UiElementPropertiesGenerated.TrySetFill(ref element.CommonState, fill);
                return true;
            case UiPropertyKey.IsEnabled when value.UntypedValue is bool enabled:
                UiElementPropertiesGenerated.TrySetEnabled(ref element.CommonState, enabled);
                return true;
            case UiPropertyKey.IsSelected when value.UntypedValue is bool selected:
                UiElementPropertiesGenerated.TrySetSelected(ref element.CommonState, selected);
                return true;
            case UiPropertyKey.Width or UiPropertyKey.Height or UiPropertyKey.Background or UiPropertyKey.Padding or
                UiPropertyKey.Fill or UiPropertyKey.IsEnabled or UiPropertyKey.IsSelected:
                return false;
        }

        if (!TryResolve(element, out var type))
        {
            return true;
        }

        switch (type.Value)
        {
            case 2 when element is StackPanel stack:
                if (key == UiPropertyKey.Orientation && value.UntypedValue is UiOrientation orientation)
                {
                    UiStackPanelGenerated.TrySetOrientation(ref stack.State, orientation);
                    return true;
                }

                return key == UiPropertyKey.Orientation ? false : true;
            case 6 when element is Grid grid:
                if (key == UiPropertyKey.Columns && value.UntypedValue is GridLength[] columns)
                {
                    UiGridGenerated.TrySetColumns(ref grid.State, columns);
                    return true;
                }

                if (key == UiPropertyKey.Rows && value.UntypedValue is GridLength[] rows)
                {
                    UiGridGenerated.TrySetRows(ref grid.State, rows);
                    return true;
                }

                return key is UiPropertyKey.Columns or UiPropertyKey.Rows ? false : true;
            case 10 when element is NumericEditor numeric:
                return TrySetNumericProperty(numeric, key, value);
            case 1 or 9 when element is TextBlock text:
                return TrySetTextProperty(text, key, value);
            default:
                return true;
        }
    }

    private static bool TrySetTextProperty(TextBlock text, UiPropertyKey key, UiValue value)
    {
        switch (key)
        {
            case UiPropertyKey.Text when value.UntypedValue is string textValue:
                UiTextBlockGenerated.TrySetText(ref text.State, textValue);
                return true;
            case UiPropertyKey.FontKey when value.UntypedValue is string fontKey:
                UiTextBlockGenerated.TrySetFontKey(ref text.State, fontKey);
                return true;
            case UiPropertyKey.FontSize when value.UntypedValue is float fontSize:
                UiTextBlockGenerated.TrySetFontSize(ref text.State, fontSize);
                return true;
            case UiPropertyKey.Foreground when value.UntypedValue is UiColor foreground:
                UiTextBlockGenerated.TrySetForeground(ref text.State, foreground);
                return true;
            case UiPropertyKey.Text or UiPropertyKey.FontKey or UiPropertyKey.FontSize or UiPropertyKey.Foreground:
                return false;
            default:
                return true;
        }
    }

    private static bool TrySetNumericProperty(NumericEditor numeric, UiPropertyKey key, UiValue value)
    {
        switch (key)
        {
            case UiPropertyKey.Value when value.UntypedValue is double number:
                return UiNumericEditorGenerated.TrySetValue(numeric, number);
            case UiPropertyKey.Minimum when value.UntypedValue is double minimum:
                UiNumericEditorGenerated.TrySetMinimum(ref numeric.State, minimum);
                return true;
            case UiPropertyKey.Maximum when value.UntypedValue is double maximum:
                UiNumericEditorGenerated.TrySetMaximum(ref numeric.State, maximum);
                return true;
            case UiPropertyKey.Value or UiPropertyKey.Minimum or UiPropertyKey.Maximum:
                return false;
            default:
                return TrySetTextProperty(numeric, key, value);
        }
    }

    internal static UiSize Measure(UiRuntimeTypeIndex type, UiElement element, in UiMeasureContext context)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (type.Value)
        {
            case 1 when element is TextBlock text:
                UiTextBlockGenerated.Measure(ref text.State, in context);
                return text.State.Layout.DesiredSize;
            case 2 when element is StackPanel stack:
                UiStackPanelGenerated.Measure(ref stack.State, in context);
                return stack.State.DesiredSize;
            case 3 when element is Border border:
                border.State.Padding = border.Padding;
                UiBorderGenerated.Measure(ref border.State, in context);
                return border.State.DesiredSize;
            case 4 or 7 or 8 when element is ContentControl content:
                UiContentControlGenerated.Measure(ref content.State, in context);
                return content.State.DesiredSize;
            case 5 or 11 when element is Panel panel:
                UiPanelGenerated.Measure(ref panel.State, in context);
                return panel.State.DesiredSize;
            case 6 when element is Grid grid:
                UiGridGenerated.Measure(ref grid.State, in context);
                return grid.State.DesiredSize;
            case 9 or 10 when element is TextBlock text:
                UiTextBlockGenerated.Measure(ref text.State, in context);
                return text.State.Layout.DesiredSize;
            case 12 when element is ScrollViewer scroll:
                UiScrollViewerGenerated.Measure(ref scroll.State, in context);
                return scroll.State.DesiredSize;
            default:
                return default;
        }
    }

    internal static UiSize ChildMeasureAvailable(UiRuntimeTypeIndex type, UiElement element, UiSize available)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (type.Value == 3 && element is Border border)
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
            if (text is TextBox editor && editor.IsComposing)
            {
                var displayState = text.State;
                displayState.Text = editor.VisualText;
                run = UiTextBlockGenerated.EmitVisual(ref displayState, in context);
                return true;
            }

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
                ApplyButtonInputResult(toggle, wasPressed, clicked, toggled);
                break;
            case Button button:
                var buttonWasPressed = button.InputState.IsPressed;
                var buttonClicked = UiButtonGenerated.Process(ref button.InputState, in routedEvent);
                ApplyButtonInputResult(button, buttonWasPressed, buttonClicked, false);
                break;
            case ScrollViewer scroll when routedEvent.Phase == UiRoutedEventPhase.Bubble && routedEvent.Kind == UiPointerEventKind.Wheel:
                if (UiScrollViewerGenerated.TryScrollBy(ref scroll.State, 0, -routedEvent.WheelDelta.y))
                {
                    scroll.InvalidateChanged(UiDirtyMask.Arrange | UiDirtyMask.Visual);
                }

                break;
        }
    }

    private static void ApplyButtonInputResult(Button button, bool wasPressed, bool clicked, bool toggled)
    {
        if (wasPressed != button.InputState.IsPressed || toggled)
        {
            button.InvalidateChanged(UiDirtyMask.Visual);
        }

        if (clicked)
        {
            button.RaiseClick();
        }
    }

    internal static void ProcessInput(UiElement element, in UiInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (element)
        {
            case NumericEditor numeric when input.Kind == UiInputEventKind.Key:
                numeric.ApplyKey(input.Key);
                break;
            case TextBox text when input.Kind == UiInputEventKind.Key:
                text.ApplyKey(input.Key);
                break;
            case TextBox text when input.Kind == UiInputEventKind.Text:
                text.ApplyText(input.Text);
                break;
            case TextBox text when input.Kind == UiInputEventKind.Composition:
                text.ApplyComposition(input.Composition);
                break;
        }
    }

    internal static void Arrange(UiRuntimeTypeIndex type, UiElement element, in UiArrangeContext context)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (type.Value)
        {
            case 1 when element is TextBlock text:
                UiTextBlockGenerated.Arrange(ref text.State, in context);
                return;
            case 2 when element is StackPanel stack:
                UiStackPanelGenerated.Arrange(ref stack.State, in context);
                return;
            case 3 when element is Border border:
                border.State.Padding = border.Padding;
                UiBorderGenerated.Arrange(ref border.State, in context);
                return;
            case 4 or 7 or 8 when element is ContentControl content:
                UiContentControlGenerated.Arrange(ref content.State, in context);
                return;
            case 5 or 11 when element is Panel panel:
                UiPanelGenerated.Arrange(ref panel.State, in context);
                return;
            case 6 when element is Grid grid:
                UiGridGenerated.Arrange(ref grid.State, in context);
                return;
            case 9 or 10 when element is TextBlock text:
                UiTextBlockGenerated.Arrange(ref text.State, in context);
                return;
            case 12 when element is ScrollViewer scroll:
                UiScrollViewerGenerated.Arrange(ref scroll.State, in context);
                return;
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

    internal static bool ProcessInput(ref TextBlockState state, in UiInputEvent input) =>
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
