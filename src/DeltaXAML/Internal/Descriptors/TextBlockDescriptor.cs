using Delta;
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
        new(new Guid("22222222-2222-2222-2222-22222222220E")),
        new(new Guid("22222222-2222-2222-2222-22222222220F")),
        new(new Guid("22222222-2222-2222-2222-222222222210")),
        new(new Guid("22222222-2222-2222-2222-222222222211")),
        new(new Guid("22222222-2222-2222-2222-222222222212")),
        new(new Guid("22222222-2222-2222-2222-222222222213")),
        new(new Guid("22222222-2222-2222-2222-222222222214")),
        new(new Guid("22222222-2222-2222-2222-222222222215")),
    ];

    private static readonly UiTypeDescriptor[] Entries =
    [
        TextBlockGenerated.Descriptor,
        UiStackPanelGenerated.Descriptor,
        UiBorderGenerated.Descriptor,
        UiContentControlGenerated.Descriptor,
        UiPanelGenerated.Descriptor,
        UiGridGenerated.Descriptor,
        UiButtonGenerated.Descriptor,
        UiToggleButtonGenerated.Descriptor,
        TextBoxGenerated.Descriptor,
        UiNumericEditorGenerated.Descriptor,
        UiItemsControlGenerated.Descriptor,
        UiScrollViewerGenerated.Descriptor,
        UiSliderGenerated.Descriptor,
        UiImageGenerated.Descriptor,
        UiOverlayGenerated.Descriptor,
        UiCollectionViewGenerated.Descriptor,
        UiPickerGenerated.Descriptor,
        UiTabViewGenerated.Descriptor,
        UiMenuGenerated.Descriptor,
        UiRichTextGenerated.Descriptor,
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
            Slider => new UiRuntimeTypeIndex(13),
            Image => new UiRuntimeTypeIndex(14),
            Overlay => new UiRuntimeTypeIndex(15),
            CollectionView => new UiRuntimeTypeIndex(16),
            Picker => new UiRuntimeTypeIndex(17),
            TabView => new UiRuntimeTypeIndex(18),
            Menu => new UiRuntimeTypeIndex(19),
            RichTextBlock => new UiRuntimeTypeIndex(20),
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

    internal static bool IsPressed(UiElement element) => element switch
    {
        ToggleButton toggle => toggle.InputState.IsPressed,
        Button button => button.InputState.IsPressed,
        _ => false,
    };

    internal static void SetPressed(UiElement element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (element)
        {
            case ToggleButton toggle when toggle.InputState.IsPressed != value:
                toggle.InputState.IsPressed = value;
                toggle.InvalidateChanged(UiDirtyMask.Style | UiDirtyMask.Visual);
                break;
            case Button button when button.InputState.IsPressed != value:
                button.InputState.IsPressed = value;
                button.InvalidateChanged(UiDirtyMask.Style | UiDirtyMask.Visual);
                break;
        }
    }

    internal static string GetAutomationValueText(UiElement element) => element switch
    {
        NumericEditor numeric => numeric.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Slider slider => slider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TextBox editor => editor.Text,
        TextBlock text => text.Text,
        RichTextBlock rich => rich.AutomationText,
        ToggleButton toggle when toggle.Content is TextBlock text => text.Text,
        Button button when button.Content is TextBlock text => text.Text,
        ContentControl content when content.Content is TextBlock text => text.Text,
        _ => string.Empty,
    };

    internal static bool TrySetProperty(UiElement element, UiPropertyKey key, UiValue value)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (key)
        {
            case UiPropertyKey.Width when value.UntypedValue is float width:
                return UiElementPropertiesGenerated.TrySetWidth(ref element.CommonState, width);
            case UiPropertyKey.Height when value.UntypedValue is float height:
                return UiElementPropertiesGenerated.TrySetHeight(ref element.CommonState, height);
            case UiPropertyKey.Margin when value.UntypedValue is UiThickness margin:
                return UiElementPropertiesGenerated.TrySetMargin(ref element.CommonState, margin);
            case UiPropertyKey.HorizontalAlignment when value.UntypedValue is Delta.XAML.UiHorizontalAlignment horizontalAlignment:
                return UiElementPropertiesGenerated.TrySetHorizontalAlignment(ref element.CommonState, horizontalAlignment);
            case UiPropertyKey.VerticalAlignment when value.UntypedValue is Delta.XAML.UiVerticalAlignment verticalAlignment:
                return UiElementPropertiesGenerated.TrySetVerticalAlignment(ref element.CommonState, verticalAlignment);
            case UiPropertyKey.Background when value.UntypedValue is UiColor background:
                UiElementPropertiesGenerated.TrySetBackground(ref element.CommonState, background);
                return true;
            case UiPropertyKey.BorderColor when value.UntypedValue is UiColor borderColor:
                UiElementPropertiesGenerated.TrySetBorderColor(ref element.CommonState, borderColor);
                return true;
            case UiPropertyKey.EffectSet when value.UntypedValue is UiEffectSet effectSet:
                return UiElementPropertiesGenerated.TrySetEffectSet(ref element.CommonState, effectSet);
            case UiPropertyKey.BorderWidth when value.UntypedValue is float borderWidth:
                return UiElementPropertiesGenerated.TrySetBorderWidth(ref element.CommonState, borderWidth);
            case UiPropertyKey.BorderWidthUnits when value.UntypedValue is PaintUnits borderWidthUnits:
                return UiElementPropertiesGenerated.TrySetBorderWidthUnits(ref element.CommonState, borderWidthUnits);
            case UiPropertyKey.CornerRadius when value.UntypedValue is Delta.XAML.UiCornerRadii cornerRadius:
                return UiElementPropertiesGenerated.TrySetCornerRadius(ref element.CommonState, cornerRadius);
            case UiPropertyKey.BackgroundBrush when value.UntypedValue is Delta.XAML.UiBrush brush:
                return TrySetBrush(element, brush);
            case UiPropertyKey.Padding when value.UntypedValue is UiThickness padding:
                return UiElementPropertiesGenerated.TrySetPadding(ref element.CommonState, padding);
            case UiPropertyKey.IsEnabled when value.UntypedValue is bool enabled:
                UiElementPropertiesGenerated.TrySetEnabled(ref element.CommonState, enabled);
                return true;
            case UiPropertyKey.IsSelected when value.UntypedValue is bool selected:
                UiElementPropertiesGenerated.TrySetSelected(ref element.CommonState, selected);
                return true;
            case UiPropertyKey.AutomationName when value.UntypedValue is string automationName:
                element.AutomationName = automationName;
                return true;
            case UiPropertyKey.AutomationRole when value.UntypedValue is Delta.XAML.UiSemanticRole automationRole:
                element.AutomationRole = ToAutomationRole(automationRole);
                return true;
            case UiPropertyKey.Gestures when value.UntypedValue is Delta.XAML.UiGestureKind gestures:
                element.Gestures = gestures;
                return true;
            case UiPropertyKey.Command when value.UntypedValue is Delta.XAML.UiCommandId command:
                element.Command = command;
                return true;
            case UiPropertyKey.CommandKey when value.UntypedValue is Delta.XAML.UiKeyGesture commandKey:
                element.CommandKey = commandKey;
                return true;
            case UiPropertyKey.IsFocusScope when value.UntypedValue is bool isFocusScope:
                element.IsFocusScope = isFocusScope;
                return true;
            case UiPropertyKey.Width or UiPropertyKey.Height or UiPropertyKey.Margin or UiPropertyKey.HorizontalAlignment or UiPropertyKey.VerticalAlignment or
                UiPropertyKey.Background or UiPropertyKey.BorderColor or UiPropertyKey.EffectSet or
                UiPropertyKey.BorderWidth or UiPropertyKey.BorderWidthUnits or UiPropertyKey.CornerRadius or UiPropertyKey.Padding or
                UiPropertyKey.IsEnabled or UiPropertyKey.IsSelected or UiPropertyKey.BackgroundBrush or
                UiPropertyKey.AutomationName or UiPropertyKey.AutomationRole or UiPropertyKey.Gestures or UiPropertyKey.Command or
                UiPropertyKey.CommandKey or UiPropertyKey.IsFocusScope:
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
            case 13 when element is Slider slider:
                return TrySetSliderProperty(ref slider.State, key, value);
            case 14 when element is Image image:
                return TrySetImageProperty(image, key, value);
            case 15 when element is Overlay overlay:
                return TrySetOverlayProperty(overlay, key, value);
            case 16 when element is CollectionView collection:
                return TrySetSelectionProperty(collection, key, value);
            case 17 when element is Picker picker:
                return TrySetPickerProperty(picker, key, value);
            case 18 when element is TabView tabs:
                return TrySetSelectionProperty(tabs, key, value);
            case 1 when element is TextBlock text:
                return TrySetTextProperty(ref text.State, key, value);
            case 9 when element is TextBox editor:
                return TrySetTextProperty(ref editor.TextState, key, value) &&
                    UiEditingPropertyResult(editor, key, value);
            default:
                return true;
        }
    }

    private static bool UiEditingPropertyResult(TextBox editor, UiPropertyKey key, UiValue value) =>
        UiEditingPropertyResult(ref editor.State, key, value);

    private static bool UiEditingPropertyResult(ref TextBoxState state, UiPropertyKey key, UiValue value) =>
        TextBoxGenerated.TrySetProperty(ref state, key, value);

    private static bool TrySetBrush(UiElement element, Delta.XAML.UiBrush brush)
    {
        switch (brush.Kind)
        {
            case Delta.XAML.UiBrushKind.None:
                element.ClearCustomVisual();
                element.Background = default;
                return true;
            case Delta.XAML.UiBrushKind.Solid:
                element.ClearCustomVisual();
                element.Background = new(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A);
                return true;
            case Delta.XAML.UiBrushKind.LinearGradient when brush.Resource.IsValid:
                element.SetCustomVisual(Delta.XAML.UiKnownVisuals.LinearGradient.Value, brush.Resource.Value, new(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A));
                return true;
            case Delta.XAML.UiBrushKind.RadialGradient when brush.Resource.IsValid:
                element.SetCustomVisual(Delta.XAML.UiKnownVisuals.RadialGradient.Value, brush.Resource.Value, new(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A));
                return true;
            case Delta.XAML.UiBrushKind.Image when brush.Resource.IsValid:
                element.SetCustomVisual(new Guid("3419D85F-C401-4DD8-86DD-D2A68359D303"), brush.Resource.Value, new(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A));
                return true;
            default:
                return false;
        }
    }

    private static UiAutomationRole ToAutomationRole(Delta.XAML.UiSemanticRole role) => role switch
    {
        Delta.XAML.UiSemanticRole.None => UiAutomationRole.None,
        Delta.XAML.UiSemanticRole.Unknown => UiAutomationRole.Unknown,
        Delta.XAML.UiSemanticRole.Generic => UiAutomationRole.Generic,
        Delta.XAML.UiSemanticRole.Window => UiAutomationRole.Window,
        Delta.XAML.UiSemanticRole.Text => UiAutomationRole.Text,
        Delta.XAML.UiSemanticRole.TextBox => UiAutomationRole.TextBox,
        Delta.XAML.UiSemanticRole.NumericEditor => UiAutomationRole.NumericEditor,
        Delta.XAML.UiSemanticRole.Button => UiAutomationRole.Button,
        Delta.XAML.UiSemanticRole.Slider => UiAutomationRole.Slider,
        Delta.XAML.UiSemanticRole.Image => UiAutomationRole.Image,
        Delta.XAML.UiSemanticRole.List => UiAutomationRole.List,
        Delta.XAML.UiSemanticRole.ListItem => UiAutomationRole.ListItem,
        Delta.XAML.UiSemanticRole.Menu => UiAutomationRole.Menu,
        Delta.XAML.UiSemanticRole.Tab => UiAutomationRole.Tab,
        Delta.XAML.UiSemanticRole.Link => UiAutomationRole.Link,
        _ => UiAutomationRole.Unknown,
    };

    private static bool TrySetTextProperty(ref TextBlockState state, UiPropertyKey key, UiValue value)
    {
        switch (key)
        {
            case UiPropertyKey.Text when value.UntypedValue is string textValue:
                TextBlockGenerated.TrySetText(ref state, textValue);
                return true;
            case UiPropertyKey.FontKey when value.UntypedValue is string fontKey:
                TextBlockGenerated.TrySetFontKey(ref state, fontKey);
                return true;
            case UiPropertyKey.FontSize when value.UntypedValue is float fontSize:
                TextBlockGenerated.TrySetFontSize(ref state, fontSize);
                return true;
            case UiPropertyKey.Foreground when value.UntypedValue is UiColor foreground:
                TextBlockGenerated.TrySetForeground(ref state, foreground);
                return true;
            case UiPropertyKey.StrokeColor when value.UntypedValue is UiColor strokeColor:
                TextBlockGenerated.TrySetStrokeColor(ref state, strokeColor);
                return true;
            case UiPropertyKey.StrokeWidth when value.UntypedValue is float strokeWidth:
                return TextBlockGenerated.TrySetStrokeWidth(ref state, strokeWidth);
            case UiPropertyKey.HorizontalTextAlignment when value.UntypedValue is Delta.XAML.UiTextHorizontalAlignment horizontal:
                return TextBlockGenerated.TrySetHorizontalTextAlignment(ref state, horizontal);
            case UiPropertyKey.VerticalTextAlignment when value.UntypedValue is Delta.XAML.UiTextVerticalAlignment vertical:
                return TextBlockGenerated.TrySetVerticalTextAlignment(ref state, vertical);
            case UiPropertyKey.TextWrapping when value.UntypedValue is Delta.XAML.UiTextWrapping wrapping:
                return TextBlockGenerated.TrySetTextWrapping(ref state, wrapping);
            case UiPropertyKey.TextTrimming when value.UntypedValue is Delta.XAML.UiTextTrimming trimming:
                return TextBlockGenerated.TrySetTextTrimming(ref state, trimming);
            case UiPropertyKey.MaxLines when value.UntypedValue is int maxLines:
                return TextBlockGenerated.TrySetMaxLines(ref state, maxLines);
            case UiPropertyKey.LineHeight when value.UntypedValue is float lineHeight:
                return TextBlockGenerated.TrySetLineHeight(ref state, lineHeight);
            case UiPropertyKey.FontWeight when value.UntypedValue is Delta.XAML.UiFontWeight weight:
                return TextBlockGenerated.TrySetFontWeight(ref state, weight);
            case UiPropertyKey.FontStyle when value.UntypedValue is Delta.XAML.UiFontStyle style:
                return TextBlockGenerated.TrySetFontStyle(ref state, style);
            case UiPropertyKey.TextDecorations when value.UntypedValue is Delta.XAML.UiTextDecorations decorations:
                return TextBlockGenerated.TrySetTextDecorations(ref state, decorations);
            case UiPropertyKey.Text or UiPropertyKey.FontKey or UiPropertyKey.FontSize or UiPropertyKey.Foreground or
                UiPropertyKey.StrokeColor or UiPropertyKey.StrokeWidth or
                UiPropertyKey.HorizontalTextAlignment or UiPropertyKey.VerticalTextAlignment or UiPropertyKey.TextWrapping or
                UiPropertyKey.TextTrimming or UiPropertyKey.MaxLines or UiPropertyKey.LineHeight or UiPropertyKey.FontWeight or
                UiPropertyKey.FontStyle or UiPropertyKey.TextDecorations:
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
                return TrySetTextProperty(ref numeric.TextState, key, value) &&
                    TextBoxGenerated.TrySetProperty(ref numeric.EditorState, key, value);
        }
    }

    private static bool TrySetSliderProperty(ref SliderState state, UiPropertyKey key, UiValue value)
    {
        switch (key)
        {
            case UiPropertyKey.Value when value.UntypedValue is double number:
                state.Value = Maths.Clamp(number, state.Minimum, state.Maximum);
                return true;
            case UiPropertyKey.Minimum when value.UntypedValue is double minimum:
                state.Minimum = minimum;
                state.Value = Maths.Max(state.Value, minimum);
                return true;
            case UiPropertyKey.Maximum when value.UntypedValue is double maximum:
                state.Maximum = maximum;
                state.Value = Maths.Min(state.Value, maximum);
                return true;
            case UiPropertyKey.Step when value.UntypedValue is double step && step > 0:
                state.Step = step;
                return true;
            case UiPropertyKey.Orientation when value.UntypedValue is UiOrientation orientation:
                state.Orientation = orientation;
                return true;
            case UiPropertyKey.Value or UiPropertyKey.Minimum or UiPropertyKey.Maximum or UiPropertyKey.Step or UiPropertyKey.Orientation:
                return false;
            default:
                return true;
        }
    }

    private static bool TrySetImageProperty(Image image, UiPropertyKey key, UiValue value)
    {
        switch (key)
        {
            case UiPropertyKey.Source when value.UntypedValue is Delta.XAML.Contract.UiResourceId source:
                image.Source = source.Value;
                return true;
            case UiPropertyKey.Tint when value.UntypedValue is UiColor tint:
                image.Tint = tint;
                return true;
            case UiPropertyKey.Stretch when value.UntypedValue is Delta.XAML.UiImageStretch stretch:
                image.Stretch = (byte)stretch;
                return true;
            case UiPropertyKey.Placeholder when value.UntypedValue is Delta.XAML.Contract.UiResourceId placeholder:
                image.Placeholder = placeholder.Value;
                return true;
            case UiPropertyKey.ErrorSource when value.UntypedValue is Delta.XAML.Contract.UiResourceId errorSource:
                image.ErrorSource = errorSource.Value;
                return true;
            case UiPropertyKey.Source or UiPropertyKey.Tint or UiPropertyKey.Stretch or UiPropertyKey.Placeholder or UiPropertyKey.ErrorSource:
                return false;
            default:
                return true;
        }
    }

    private static bool TrySetOverlayProperty(Overlay overlay, UiPropertyKey key, UiValue value)
    {
        if (key != UiPropertyKey.IsOpen)
        {
            return true;
        }

        if (value.UntypedValue is not bool open)
        {
            return false;
        }

        overlay.IsOpen = open;
        overlay.SetParticipation(open ? Delta.XAML.UiParticipation.All : Delta.XAML.UiParticipation.None);
        return true;
    }

    private static bool TrySetSelectionProperty(CollectionView owner, UiPropertyKey key, UiValue value)
    {
        if (key != UiPropertyKey.SelectedIndex)
        {
            return true;
        }

        if (value.UntypedValue is not int selected)
        {
            return false;
        }

        owner.SelectedIndex = selected;
        return true;
    }

    private static bool TrySetSelectionProperty(TabView owner, UiPropertyKey key, UiValue value)
    {
        if (key != UiPropertyKey.SelectedIndex)
        {
            return true;
        }

        if (value.UntypedValue is not int selected)
        {
            return false;
        }

        owner.SelectedIndex = selected;
        return true;
    }

    private static bool TrySetPickerProperty(Picker owner, UiPropertyKey key, UiValue value)
    {
        if (key == UiPropertyKey.SelectedIndex && value.UntypedValue is int selected)
        {
            owner.SelectedIndex = selected;
            return true;
        }

        if (key == UiPropertyKey.IsOpen && value.UntypedValue is bool open)
        {
            owner.IsOpen = open;
            if (owner.Children.Count > 1 && owner.Children[1] is Overlay overlay)
            {
                overlay.IsOpen = open;
            }

            return true;
        }

        return key is not (UiPropertyKey.SelectedIndex or UiPropertyKey.IsOpen);
    }

    internal static UiSize Measure(UiRuntimeTypeIndex type, UiElement element, in UiMeasureContext context)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (type.Value)
        {
            case 1 when element is TextBlock text:
                TextBlockGenerated.Measure(ref text.State, in context);
                return text.State.Layout.DesiredSize;
            case 2 when element is StackPanel stack:
                UiStackPanelGenerated.Measure(ref stack.State, in context);
                return stack.State.DesiredSize;
            case 3 when element is Border border:
                border.State.Padding = border.Padding;
                UiBorderGenerated.Measure(ref border.State, in context);
                return border.State.DesiredSize;
            case 4 when element is ContentControl content:
                UiContentControlGenerated.Measure(ref content.State, in context);
                return content.State.DesiredSize;
            case 7 when element is Button button:
                UiContentControlGenerated.Measure(ref button.ContentState, in context);
                return button.ContentState.DesiredSize;
            case 8 when element is ToggleButton toggle:
                UiContentControlGenerated.Measure(ref toggle.ContentState, in context);
                return toggle.ContentState.DesiredSize;
            case 5 when element is Panel panel:
                UiPanelGenerated.Measure(ref panel.State, in context);
                return panel.State.DesiredSize;
            case 11 when element is ItemsControl items:
                UiItemsControlGenerated.Measure(ref items.State, in context);
                return items.State.Layout.DesiredSize;
            case 6 when element is Grid grid:
                UiGridGenerated.Measure(ref grid.State, in context);
                return grid.State.DesiredSize;
            case 9 when element is TextBox editor:
                TextBlockGenerated.Measure(ref editor.TextState, in context);
                return editor.TextState.Layout.DesiredSize;
            case 10 when element is NumericEditor numeric:
                TextBlockGenerated.Measure(ref numeric.TextState, in context);
                return numeric.TextState.Layout.DesiredSize;
            case 12 when element is ScrollViewer scroll:
                UiScrollViewerGenerated.Measure(ref scroll.State, in context);
                return scroll.State.DesiredSize;
            case 13 when element is Slider slider:
                UiSliderGenerated.Measure(ref slider.State, in context);
                return slider.State.DesiredSize;
            case 14 when element is Image image:
                UiImageGenerated.Measure(ref image.State, in context);
                return image.State.DesiredSize;
            case 15 when element is Overlay overlay:
                UiOverlayGenerated.Measure(ref overlay.State, in context);
                return overlay.State.Layout.DesiredSize;
            case 16 when element is CollectionView collection:
                UiCollectionViewGenerated.Measure(ref collection.State, in context);
                return collection.State.Layout.DesiredSize;
            case 17 when element is Picker picker:
                UiPickerGenerated.Measure(ref picker.State, in context);
                return picker.State.Layout.DesiredSize;
            case 18 when element is TabView tabs:
                UiTabViewGenerated.Measure(ref tabs.State, in context);
                return tabs.State.Layout.DesiredSize;
            case 19 when element is Menu menu:
                UiMenuGenerated.Measure(ref menu.State, in context);
                return menu.State.Layout.DesiredSize;
            case 20 when element is RichTextBlock rich:
                UiRichTextGenerated.Measure(ref rich.State, in context);
                return rich.State.DesiredSize;
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
        if (type.Value == 1 && element is TextBlock text)
        {
            run = TextBlockGenerated.EmitVisual(ref text.State, in context);
            return true;
        }

        if (type.Value == 9 && element is TextBox editor)
        {
            if (editor.IsComposing || (editor.Text.Length == 0 && editor.PlaceholderText.Length != 0))
            {
                var displayState = editor.TextState;
                displayState.Text = editor.VisualText;
                run = TextBlockGenerated.EmitVisual(ref displayState, in context);
                return true;
            }

            run = TextBlockGenerated.EmitVisual(ref editor.TextState, in context);
            return true;
        }

        if (type.Value == 10 && element is NumericEditor numeric)
        {
            if (numeric.IsComposing || (numeric.Text.Length == 0 && numeric.PlaceholderText.Length != 0))
            {
                var displayState = numeric.TextState;
                displayState.Text = numeric.VisualText;
                run = TextBlockGenerated.EmitVisual(ref displayState, in context);
                return true;
            }

            run = TextBlockGenerated.EmitVisual(ref numeric.TextState, in context);
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
                ApplyToggleInputResult(toggle, wasPressed, clicked, toggled);
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
            case Slider slider:
                if (UiSliderGenerated.TryGetPointerValue(ref slider.State, in routedEvent, out var pointerValue))
                {
                    slider.SetUserValue(pointerValue);
                }

                break;
            case CollectionView collection when routedEvent.Phase == UiRoutedEventPhase.Bubble &&
                routedEvent.Kind == UiPointerEventKind.ButtonUp:
                _ = collection.SelectTarget(routedEvent.Origin);
                break;
            case Picker picker when routedEvent.Phase == UiRoutedEventPhase.Bubble &&
                routedEvent.Kind == UiPointerEventKind.ButtonUp:
                _ = picker.ProcessPointer(routedEvent.Origin);
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

    private static void ApplyToggleInputResult(ToggleButton button, bool wasPressed, bool clicked, bool toggled)
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
            case NumericEditor numeric when input.Kind == UiInputEventKind.Text:
                numeric.ApplyText(input.Text);
                break;
            case NumericEditor numeric when input.Kind == UiInputEventKind.Composition:
                numeric.ApplyComposition(input.Composition);
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
            case Slider slider when input.Kind == UiInputEventKind.Key:
                var key = input.Key;
                if (UiSliderGenerated.TryGetKeyValue(ref slider.State, in key, out var keyValue))
                {
                    slider.SetUserValue(keyValue);
                }

                break;
            case CollectionView collection when input.Kind == UiInputEventKind.Key:
                if (input.Key.PhysicalKey.Value == 38)
                {
                    _ = collection.MoveSelection(-1);
                }
                else if (input.Key.PhysicalKey.Value == 40)
                {
                    _ = collection.MoveSelection(1);
                }

                break;
            case Picker picker when input.Kind == UiInputEventKind.Key:
                switch (input.Key.PhysicalKey.Value)
                {
                    case 13:
                    case 32:
                        picker.IsOpen = !picker.IsOpen;
                        break;
                    case 27:
                        picker.IsOpen = false;
                        break;
                    case 38:
                        if (!picker.SynchronizeSelection())
                        {
                            _ = picker.MoveSelection(-1);
                        }
                        break;
                    case 40:
                        if (!picker.SynchronizeSelection())
                        {
                            _ = picker.MoveSelection(1);
                        }
                        break;
                }

                break;
            case Button button when input.Kind == UiInputEventKind.Key && input.Key.PhysicalKey.Value is 13 or 32:
                button.RaiseClick();
                break;
            case ToggleButton toggle when input.Kind == UiInputEventKind.Key && input.Key.PhysicalKey.Value is 13 or 32:
                toggle.IsSelected = !toggle.IsSelected;
                toggle.RaiseClick();
                break;
        }
    }

    internal static void Arrange(UiRuntimeTypeIndex type, UiElement element, in UiArrangeContext context)
    {
        ArgumentNullException.ThrowIfNull(element);
        switch (type.Value)
        {
            case 1 when element is TextBlock text:
                TextBlockGenerated.Arrange(ref text.State, in context);
                return;
            case 2 when element is StackPanel stack:
                UiStackPanelGenerated.Arrange(ref stack.State, in context);
                return;
            case 3 when element is Border border:
                border.State.Padding = border.Padding;
                UiBorderGenerated.Arrange(ref border.State, in context);
                return;
            case 4 when element is ContentControl content:
                UiContentControlGenerated.Arrange(ref content.State, in context);
                return;
            case 7 when element is Button button:
                UiContentControlGenerated.Arrange(ref button.ContentState, in context);
                return;
            case 8 when element is ToggleButton toggle:
                UiContentControlGenerated.Arrange(ref toggle.ContentState, in context);
                return;
            case 5 when element is Panel panel:
                UiPanelGenerated.Arrange(ref panel.State, in context);
                return;
            case 11 when element is ItemsControl items:
                UiItemsControlGenerated.Arrange(ref items.State, in context);
                return;
            case 6 when element is Grid grid:
                UiGridGenerated.Arrange(ref grid.State, in context);
                return;
            case 9 when element is TextBox editor:
                TextBlockGenerated.Arrange(ref editor.TextState, in context);
                return;
            case 10 when element is NumericEditor numeric:
                TextBlockGenerated.Arrange(ref numeric.TextState, in context);
                return;
            case 12 when element is ScrollViewer scroll:
                UiScrollViewerGenerated.Arrange(ref scroll.State, in context);
                return;
            case 13 when element is Slider slider:
                UiSliderGenerated.Arrange(ref slider.State, in context);
                return;
            case 14 when element is Image image:
                UiImageGenerated.Arrange(ref image.State, in context);
                return;
            case 15 when element is Overlay overlay:
                UiOverlayGenerated.Arrange(ref overlay.State, in context);
                return;
            case 16 when element is CollectionView collection:
                UiCollectionViewGenerated.Arrange(ref collection.State, in context);
                return;
            case 17 when element is Picker picker:
                UiPickerGenerated.Arrange(ref picker.State, in context);
                return;
            case 18 when element is TabView tabs:
                UiTabViewGenerated.Arrange(ref tabs.State, in context);
                return;
            case 19 when element is Menu menu:
                UiMenuGenerated.Arrange(ref menu.State, in context);
                return;
            case 20 when element is RichTextBlock rich:
                UiRichTextGenerated.Arrange(ref rich.State, in context);
                return;
        }
    }
}

/// <summary>Typed companion for <see cref="TextBlock"/>; the generated path owns no instance state.</summary>
internal static class TextBlockGenerated
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

    internal static bool TrySetStrokeColor(ref TextBlockState state, UiColor value)
    {
        if (state.Visual.StrokeColor == value) { return false; }
        state.Visual.StrokeColor = value;
        return true;
    }

    internal static bool TrySetStrokeWidth(ref TextBlockState state, float value)
    {
        if (!float.IsFinite(value) || value < 0) { return false; }
        if (state.Visual.StrokeWidth.Equals(value)) { return false; }
        state.Visual.StrokeWidth = value;
        return true;
    }

    internal static bool TrySetHorizontalTextAlignment(ref TextBlockState state, Delta.XAML.UiTextHorizontalAlignment value)
    {
        if (value is Delta.XAML.UiTextHorizontalAlignment.Unknown)
        {
            return false;
        }

        if (state.Layout.HorizontalAlignment == value) { return true; }
        state.Layout.HorizontalAlignment = value;
        return true;
    }

    internal static bool TrySetVerticalTextAlignment(ref TextBlockState state, Delta.XAML.UiTextVerticalAlignment value)
    {
        if (value is Delta.XAML.UiTextVerticalAlignment.Unknown)
        {
            return false;
        }

        if (state.Layout.VerticalAlignment == value) { return true; }
        state.Layout.VerticalAlignment = value;
        return true;
    }

    internal static bool TrySetTextWrapping(ref TextBlockState state, Delta.XAML.UiTextWrapping value)
    {
        if (value is Delta.XAML.UiTextWrapping.Unknown)
        {
            return false;
        }

        if (state.Layout.Wrapping == value) { return true; }
        state.Layout.Wrapping = value;
        return true;
    }

    internal static bool TrySetTextTrimming(ref TextBlockState state, Delta.XAML.UiTextTrimming value)
    {
        if (value is Delta.XAML.UiTextTrimming.Unknown)
        {
            return false;
        }

        if (state.Layout.Trimming == value) { return true; }
        state.Layout.Trimming = value;
        return true;
    }

    internal static bool TrySetMaxLines(ref TextBlockState state, int value)
    {
        if (value < 0) { return false; }
        if (state.Layout.MaxLines == value) { return true; }
        state.Layout.MaxLines = value;
        return true;
    }

    internal static bool TrySetLineHeight(ref TextBlockState state, float value)
    {
        if (!float.IsFinite(value) || value < 0) { return false; }
        if (state.Layout.LineHeight.Equals(value)) { return true; }
        state.Layout.LineHeight = value;
        return true;
    }

    internal static bool TrySetFontWeight(ref TextBlockState state, Delta.XAML.UiFontWeight value)
    {
        if (value is Delta.XAML.UiFontWeight.Unknown) { return false; }
        if (state.Visual.Weight == value) { return true; }
        state.Visual.Weight = value;
        return true;
    }

    internal static bool TrySetFontStyle(ref TextBlockState state, Delta.XAML.UiFontStyle value)
    {
        if (value is Delta.XAML.UiFontStyle.Unknown) { return false; }
        if (state.Visual.Style == value) { return true; }
        state.Visual.Style = value;
        return true;
    }

    internal static bool TrySetTextDecorations(ref TextBlockState state, Delta.XAML.UiTextDecorations value)
    {
        if ((value & ~(Delta.XAML.UiTextDecorations.Underline | Delta.XAML.UiTextDecorations.Strikethrough)) != 0 ||
            state.Visual.Decorations == value)
        {
            return (value & ~(Delta.XAML.UiTextDecorations.Underline | Delta.XAML.UiTextDecorations.Strikethrough)) == 0;
        }

        state.Visual.Decorations = value;
        return true;
    }
}
