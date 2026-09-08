using System.Diagnostics.CodeAnalysis;
using Delta;
using Delta.XAML.Contract;
using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

[Flags] internal enum UiDirtyMask { None = 0, Tree = 1, Style = 2, Binding = 4, Measure = 8, Arrange = 16, Visual = 32, HitTest = 64, Resource = 128, BindingSubtree = 256, Text = 512 }
internal enum UiVisibility { Visible, Hidden, Collapsed }
internal enum UiOrientation { Horizontal, Vertical }
internal enum UiValueSource { Default, Local, Style, Trigger, Binding, Handle, Animation }
internal enum UiPropertyKey
{
    Unknown,
    Width,
    Height,
    Margin,
    HorizontalAlignment,
    VerticalAlignment,
    Background,
    BackgroundBrush,
    BorderColor,
    EffectSet,
    BorderWidth,
    BorderWidthUnits,
    CornerRadius,
    Padding,
    IsEnabled,
    IsSelected,
    Text,
    FontKey,
    FontSize,
    Foreground,
    StrokeColor,
    StrokeWidth,
    HorizontalTextAlignment,
    VerticalTextAlignment,
    TextWrapping,
    TextTrimming,
    MaxLines,
    LineHeight,
    FontWeight,
    FontStyle,
    TextDecorations,
    PlaceholderText,
    IsReadOnly,
    AcceptsReturn,
    MaxLength,
    Orientation,
    Columns,
    Rows,
    Value,
    Minimum,
    Maximum,
    Step,
    Source,
    Tint,
    SelectedIndex,
    IsOpen,
    AutomationName,
    AutomationRole,
    Gestures,
    Command,
    CommandKey,
    IsFocusScope,
    Stretch,
    Placeholder,
    ErrorSource,
}

internal static class UiPropertyKeys
{
    internal static UiPropertyKey Resolve(string name) => name switch
    {
        "Width" => UiPropertyKey.Width,
        "Height" => UiPropertyKey.Height,
        "Margin" => UiPropertyKey.Margin,
        "HorizontalAlignment" => UiPropertyKey.HorizontalAlignment,
        "VerticalAlignment" => UiPropertyKey.VerticalAlignment,
        "Background" => UiPropertyKey.Background,
        "BackgroundBrush" => UiPropertyKey.BackgroundBrush,
        "BorderColor" => UiPropertyKey.BorderColor,
        "EffectSet" => UiPropertyKey.EffectSet,
        "BorderWidth" => UiPropertyKey.BorderWidth,
        "BorderWidthUnits" => UiPropertyKey.BorderWidthUnits,
        "CornerRadius" => UiPropertyKey.CornerRadius,
        "Padding" => UiPropertyKey.Padding,
        "IsEnabled" => UiPropertyKey.IsEnabled,
        "IsSelected" => UiPropertyKey.IsSelected,
        "Text" => UiPropertyKey.Text,
        "FontKey" => UiPropertyKey.FontKey,
        "FontSize" => UiPropertyKey.FontSize,
        "Foreground" => UiPropertyKey.Foreground,
        "StrokeColor" => UiPropertyKey.StrokeColor,
        "StrokeWidth" => UiPropertyKey.StrokeWidth,
        "HorizontalTextAlignment" => UiPropertyKey.HorizontalTextAlignment,
        "VerticalTextAlignment" => UiPropertyKey.VerticalTextAlignment,
        "TextWrapping" => UiPropertyKey.TextWrapping,
        "TextTrimming" => UiPropertyKey.TextTrimming,
        "MaxLines" => UiPropertyKey.MaxLines,
        "LineHeight" => UiPropertyKey.LineHeight,
        "FontWeight" => UiPropertyKey.FontWeight,
        "FontStyle" => UiPropertyKey.FontStyle,
        "TextDecorations" => UiPropertyKey.TextDecorations,
        "PlaceholderText" => UiPropertyKey.PlaceholderText,
        "IsReadOnly" => UiPropertyKey.IsReadOnly,
        "AcceptsReturn" => UiPropertyKey.AcceptsReturn,
        "MaxLength" => UiPropertyKey.MaxLength,
        "Orientation" => UiPropertyKey.Orientation,
        "Columns" => UiPropertyKey.Columns,
        "Rows" => UiPropertyKey.Rows,
        "Value" => UiPropertyKey.Value,
        "Minimum" => UiPropertyKey.Minimum,
        "Maximum" => UiPropertyKey.Maximum,
        "Step" => UiPropertyKey.Step,
        "Source" => UiPropertyKey.Source,
        "Tint" => UiPropertyKey.Tint,
        "SelectedIndex" => UiPropertyKey.SelectedIndex,
        "IsOpen" => UiPropertyKey.IsOpen,
        "AutomationName" => UiPropertyKey.AutomationName,
        "AutomationRole" => UiPropertyKey.AutomationRole,
        "Gestures" => UiPropertyKey.Gestures,
        "Command" => UiPropertyKey.Command,
        "CommandKey" => UiPropertyKey.CommandKey,
        "IsFocusScope" => UiPropertyKey.IsFocusScope,
        "Stretch" => UiPropertyKey.Stretch,
        "Placeholder" => UiPropertyKey.Placeholder,
        "ErrorSource" => UiPropertyKey.ErrorSource,
        _ => UiPropertyKey.Unknown,
    };

    internal static Type ValueType(UiElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (element is NumericEditor && name == "Value")
        {
            return typeof(double);
        }

        if (element is TextBlock or TextBox or NumericEditor && name is "Text" or "FontKey")
        {
            return typeof(string);
        }

        return name switch
        {
            "Width" or "Height" or "FontSize" or "LineHeight" => typeof(float),
            "Margin" or "Padding" => typeof(Delta.XAML.UiThickness),
            "HorizontalAlignment" => typeof(Delta.XAML.UiHorizontalAlignment),
            "VerticalAlignment" => typeof(Delta.XAML.UiVerticalAlignment),
            "MaxLines" or "MaxLength" => typeof(int),
            "IsEnabled" or "IsSelected" => typeof(bool),
            "IsReadOnly" or "AcceptsReturn" => typeof(bool),
            "Background" or "BorderColor" or "Foreground" or "StrokeColor" => typeof(UiColor),
            "EffectSet" => typeof(UiEffectSet),
            "BorderWidth" or "StrokeWidth" => typeof(float),
            "BorderWidthUnits" => typeof(PaintUnits),
            "CornerRadius" => typeof(Delta.XAML.UiCornerRadii),
            "HorizontalTextAlignment" => typeof(Delta.XAML.UiTextHorizontalAlignment),
            "VerticalTextAlignment" => typeof(Delta.XAML.UiTextVerticalAlignment),
            "TextWrapping" => typeof(Delta.XAML.UiTextWrapping),
            "TextTrimming" => typeof(Delta.XAML.UiTextTrimming),
            "FontWeight" => typeof(Delta.XAML.UiFontWeight),
            "FontStyle" => typeof(Delta.XAML.UiFontStyle),
            "TextDecorations" => typeof(Delta.XAML.UiTextDecorations),
            "PlaceholderText" => typeof(string),
            "BackgroundBrush" => typeof(Delta.XAML.UiBrush),
            "AutomationName" => typeof(string),
            "AutomationRole" => typeof(Delta.XAML.UiSemanticRole),
            "Gestures" => typeof(Delta.XAML.UiGestureKind),
            "Command" => typeof(Delta.XAML.UiCommandId),
            "CommandKey" => typeof(Delta.XAML.UiKeyGesture),
            "IsFocusScope" => typeof(bool),
            _ => typeof(object),
        };
    }
}
internal readonly record struct UiSize(float Width, float Height);
internal readonly record struct UiPoint(float X, float Y);
internal readonly record struct UiThickness(float Left, float Top, float Right, float Bottom) { public static UiThickness Zero => default; public float Horizontal => Left + Right; public float Vertical => Top + Bottom; }
internal readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public bool Contains(UiPoint p) => p.X >= X && p.Y >= Y && p.X < X + Width && p.Y < Y + Height;
    public bool IsInside(UiRect other) => X >= other.X && Y >= other.Y && X + Width <= other.X + other.Width && Y + Height <= other.Y + other.Height;
    public static UiRect Intersect(UiRect a, UiRect b) { var x = Maths.Max(a.X, b.X); var y = Maths.Max(a.Y, b.Y); var r = Maths.Min(a.X + a.Width, b.X + b.Width); var z = Maths.Min(a.Y + a.Height, b.Y + b.Height); return new(x, y, Maths.Max(0, r - x), Maths.Max(0, z - y)); }
}
internal readonly record struct UiColor(byte R, byte G, byte B, byte A = 255);
internal readonly record struct UiElementId(uint Value) { public bool IsValid => Value != 0; }
internal readonly record struct UiPropertyHandle(UiElementId Element, uint Generation, string Name);
internal readonly record struct UiMutation(UiPropertyHandle Target, object? Value, UiDirtyFlags Invalidation);
internal enum UiControlMutationKind : byte { None, Unknown, Input, RoutedEvent }
internal readonly record struct UiControlMutation(
    UiNodeId Target,
    UiControlMutationKind Kind,
    UiInputEvent Input,
    UiRoutedEvent RoutedEvent)
{
    internal static UiControlMutation FromInput(UiNodeId target, in UiInputEvent input) =>
        new(target, UiControlMutationKind.Input, input, default);

    internal static UiControlMutation FromRoutedEvent(UiNodeId target, in UiRoutedEvent routedEvent) =>
        new(target, UiControlMutationKind.RoutedEvent, default, routedEvent);
}
internal enum UiBindingMode { OneWay, TwoWay, OneTime }
internal enum UiTextEditAction { None, SelectAll, Copy, Cut, Paste, Undo, Redo, DeleteSelection, Increment, Decrement }
internal readonly record struct UiResourceReference(string Key, Guid ResourceId = default)
{
    internal UiResourceReference(Guid resource) : this(resource.ToString("D"), resource)
    {
        if (resource == Guid.Empty)
        {
            throw new ArgumentException("A resource identity is required.", nameof(resource));
        }
    }

    internal bool HasResourceId => ResourceId != Guid.Empty;
}
internal enum UiAutomationRole { None, Unknown, Generic, Button, Window, Text, TextBox, NumericEditor, Slider, Image, List, ListItem, Menu, Tab, Link }
internal readonly record struct UiAutomationMetadata(string Name, UiAutomationRole Role, string ValueText, bool IsEnabled, bool IsInvalid);
internal enum UiVisualState { Normal, Hover, Pressed, Focused, Disabled, Invalid, Selected }
internal readonly record struct UiStateSnapshot(UiVisualState State, bool IsEnabled, bool IsInvalid, bool IsSelected, bool IsFocused, bool IsHovered, bool IsPressed);

internal readonly record struct UiClipId(uint Value);
/// <summary>Renderer-neutral text request; it is not shaped glyph data.</summary>
/// <remarks>DeltaXAML owns content, style, layout bounds, DPI-dependent text metrics and identity. Owner plus OwnerGeneration identify retained lifetime; Version identifies text/style/DPI dirtiness. Layout changes are represented by Bounds, Clip and draw-list deltas. Shaping and glyph pixels remain external.</remarks>
internal readonly record struct UiTextRun(
    string FontKey,
    float FontSize,
    string Text,
    string GlyphRunKey,
    UiColor Color,
    UiRect Bounds,
    UiRect Clip,
    UiElementId Owner,
    uint OwnerGeneration,
    uint Version,
    UiClipId ClipId = default)
{
    internal UiRect TextBounds { get; init; }
    internal Delta.XAML.UiTextHorizontalAlignment HorizontalAlignment { get; init; }
    internal Delta.XAML.UiTextVerticalAlignment VerticalAlignment { get; init; }
    internal Delta.XAML.UiTextWrapping Wrapping { get; init; }
    internal Delta.XAML.UiTextTrimming Trimming { get; init; }
    internal int MaxLines { get; init; }
    internal float LineHeight { get; init; }
    internal float LayoutScale { get; init; } = 1f;
    internal Delta.XAML.UiFontWeight Weight { get; init; }
    internal Delta.XAML.UiFontStyle Style { get; init; }
    internal Delta.XAML.UiTextDecorations Decorations { get; init; }
    internal UiEffectSet EffectSet { get; init; }
}
internal readonly record struct UiMeasureContext(
    UiSize Available,
    float DpiScale,
    IReadOnlyList<UiElement>? Children = null,
    bool MeasureChildren = true,
    UiMeasureQueueBuffer? Requests = null,
    UiNodeStore? Nodes = null,
    UiTextMeasureMetrics TextMetrics = default);
internal readonly record struct UiArrangeContext(
    UiRect Bounds,
    UiRect Clip,
    IReadOnlyList<UiElement>? Children = null,
    UiArrangeQueueBuffer? Requests = null,
    UiNodeStore? Nodes = null);
internal readonly record struct UiMeasureRequest(UiNodeId Element, UiSize Available);
internal readonly record struct UiArrangeRequest(UiNodeId Element, UiRect Bounds, UiRect Clip);
internal readonly record struct UiTextVisualContext(UiElementId Owner, uint OwnerGeneration, float LayoutScale, uint Version);
internal readonly record struct UiBindingSpec(
    string Property,
    string Path,
    UiBindingMode Mode,
    string? ConverterKey,
    string? StringFormat,
    string? CultureName);

internal interface IUiRelationBindingMetadata
{
    Delta.XAML.UiBindingSourceKind SourceKind { get; }
}

internal enum UiRoutedEventPhase { Preview, Bubble }
internal readonly record struct UiRoutedEvent(
    UiElementId Target,
    UiRoutedEventPhase Phase,
    UiPointerEventKind Kind,
    float2 Position,
    float2 WheelDelta,
    UiElement? Origin = null);
internal interface IUiClipboard
{
    string? ReadText();
    void SetText(string? text);
    bool HasText { get; }
}
internal sealed class TextChangedEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}

internal interface IUiItemSource<T>
{
    int Count { get; }
    T GetValue(int index);
    bool Matches(IUiItemSource<T> previous, int index);
}

internal interface IUiItemFactory<T>
{
    UiElement Create(IUiItemSource<T> source, int index);
}
