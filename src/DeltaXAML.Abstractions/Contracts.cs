using System.Diagnostics.CodeAnalysis;
using IUiResourceDictionary = DeltaXAML.Abstractions.IUiResourceStore;
using UiDirtyFlags = DeltaXAML.Abstractions.UiDirtyMask;

namespace DeltaXAML.Abstractions;

[Flags] public enum UiDirtyMask { None = 0, Tree = 1, Style = 2, Binding = 4, Measure = 8, Arrange = 16, Visual = 32, HitTest = 64, Resource = 128 }
public enum UiVisibility { Visible, Hidden, Collapsed }
public enum UiOrientation { Horizontal, Vertical }
public enum UiValueSource { Default, Local, Style, Binding, Handle }
public readonly record struct UiSize(float Width, float Height);
public readonly record struct UiPoint(float X, float Y);
public readonly record struct UiThickness(float Left, float Top, float Right, float Bottom) { public static UiThickness Zero => default; public float Horizontal => Left + Right; public float Vertical => Top + Bottom; }
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public bool Contains(UiPoint p) => p.X >= X && p.Y >= Y && p.X <= X + Width && p.Y <= Y + Height;
    public bool IsInside(UiRect other) => X >= other.X && Y >= other.Y && X + Width <= other.X + other.Width && Y + Height <= other.Y + other.Height;
    public static UiRect Intersect(UiRect a, UiRect b) { var x = MathF.Max(a.X, b.X); var y = MathF.Max(a.Y, b.Y); var r = MathF.Min(a.X + a.Width, b.X + b.Width); var z = MathF.Min(a.Y + a.Height, b.Y + b.Height); return new(x, y, MathF.Max(0, r - x), MathF.Max(0, z - y)); }
}
public readonly record struct UiColor(byte R, byte G, byte B, byte A = 255);
public readonly record struct UiElementId(uint Value) { public bool IsValid => Value != 0; }
public readonly record struct UiPropertyHandle(UiElementId Element, uint Generation, string Name);
public readonly record struct UiMutation(UiPropertyHandle Target, object? Value, UiDirtyFlags Invalidation);
public enum UiEditorKind { None, Unknown, Text, Numeric }
public readonly record struct UiPropertySchema(string ComponentKey, string FieldKey, string Label, string ValueType, UiEditorKind EditorKind, bool IsReadOnly = false);
public readonly record struct UiSchemaValue(UiPropertySchema Schema, object? Value, UiValueSource Source, uint Version);
public enum UiPropertySourceChangeKind { Reset, Add, Remove, Change }
public readonly record struct UiPropertySourceChange(UiPropertySourceChangeKind Kind, int Index, int Count = 1);
public sealed class UiPropertySourceChangeEventArgs(UiPropertySourceChange change) : EventArgs
{
    public UiPropertySourceChange Change { get; } = change;
}
public interface IUiPropertySource
{
    int Count { get; }
    UiSchemaValue GetValue(int index);
    bool TrySet(int index, object? value, [NotNullWhen(false)] out string? diagnostic);
    event EventHandler<UiPropertySourceChangeEventArgs>? Changed;
}
public enum UiBindingMode { OneWay, TwoWay, OneTime }
public readonly record struct UiResourceHandle(ulong Value, uint Generation);
public enum UiAutomationRole { None, Unknown, Generic, Button, Window, Text, TextBox, NumericEditor }
public readonly record struct UiAutomationMetadata(string Name, UiAutomationRole Role, string ValueText, bool IsEnabled, bool IsInvalid);
public enum UiVisualState { Normal, Hover, Pressed, Focused, Disabled, Invalid, Selected }
public readonly record struct UiStateSnapshot(UiVisualState State, bool IsEnabled, bool IsInvalid, bool IsSelected, bool IsFocused, bool IsHovered, bool IsPressed);

public interface IUiElement
{
    UiElementId Id { get; }
    uint Generation { get; }
    string TypeName { get; }
    IUiElement? Parent { get; }
    IReadOnlyList<IUiElement> Children { get; }
    UiVisibility Visibility { get; }
    bool Focusable { get; }
    float Width { get; }
    float Height { get; }
    bool Fill { get; }
    UiRect Bounds { get; }
    UiRect Clip { get; }
    UiSize DesiredSize { get; }
    UiColor Background { get; }
    UiAutomationMetadata Automation { get; }
    UiStateSnapshot VisualState { get; }
    void Measure(UiSize available); void Arrange(UiRect bounds);
}
public interface IUiPanel : IUiElement { void Add(IUiElement child); bool Remove(IUiElement child); }
public interface IUiValue
{
    object? UntypedValue { get; }
    UiValueSource Source { get; }
    UiDirtyFlags Invalidation { get; }
}
public interface IUiBinding
{
    object? Read(); bool TryWrite(object? value, [NotNullWhen(false)] out string? diagnostic); event EventHandler? Changed;
}
public interface IUiCompiledBinding : IUiBinding
{
    UiBindingMode Mode { get; }
    Type ValueType { get; }
}
public interface IUiPropertyStore
{
    void SetLocal(string name, object? value, UiDirtyFlags invalidation); void SetStyle(string name, object? value, UiDirtyFlags invalidation);
    void SetBinding(string name, IUiBinding binding, UiDirtyFlags invalidation); void SetHandle(string name, object? value, UiDirtyFlags invalidation); bool TryGet(string name, [NotNullWhen(true)] out IUiValue? value);
    UiPropertyHandle GetHandle(string name); bool TrySet(UiPropertyHandle handle, object? value, UiDirtyFlags invalidation, [NotNullWhen(false)] out string? diagnostic);
}

public enum UiDrawKind { Rectangle, TextRun, Image, Viewport }
public readonly record struct UiClipId(uint Value);
public readonly record struct UiClipEntry(UiClipId Id, UiRect Bounds, UiClipId Parent);
/// <summary>Renderer-neutral text request; it is not shaped glyph data.</summary>
/// <remarks>DeltaXAML owns content, style, layout and owner/version identity; it does not own shaping or glyph pixels.</remarks>
public readonly record struct UiTextRun(string FontKey, float FontSize, string Text, string GlyphRunKey, UiColor Color, UiRect Bounds, UiRect Clip, UiElementId Owner, uint Version);
public readonly record struct UiDrawRange(int Start, int Count);
public readonly record struct UiDrawDelta(UiDrawRange Commands, UiDrawRange TextRuns, uint BaseVersion, uint NextVersion)
{
    public UiDrawRange Clips { get; init; }
}
public readonly record struct UiDrawCommand(UiDrawKind Kind, UiRect Bounds, UiRect Clip, UiClipId ClipId, UiResourceHandle Resource, UiColor Color, string? Text, int ZIndex, uint Order, UiElementId Owner);
/// <summary>Canonical renderer-neutral producer for one retained UI frame.</summary>
/// <remarks>Memory views borrow the producing frame storage until its next extraction; copy them to retain data beyond that boundary.</remarks>
public interface IUiDrawList
{
    ReadOnlyMemory<UiDrawCommand> Commands { get; }
    ReadOnlyMemory<UiClipEntry> Clips { get; }
    ReadOnlyMemory<UiTextRun> TextRuns { get; }
    /// <summary>Changes only when commands, clips or text requests change.</summary>
    uint Version { get; }
    UiDrawDelta GetDeltaSince(uint version);
}
public readonly record struct UiFrameContext(UiSize Viewport, float DpiScale, uint FrameNumber);
public interface IUiMutationSink { void Enqueue(in UiMutation mutation); }
public interface IUiFrame : IUiMutationSink { IUiElement Root { get; } void ApplyMutations(); void Layout(UiSize viewport, float dpiScale); IUiDrawList ExtractDrawList(in UiFrameContext context); }

public enum UiPointerEventKind { Move, Down, Up, Wheel }
public readonly record struct UiPointerEvent(UiPointerEventKind Kind, UiPoint Position, int Button = 0, float WheelDelta = 0);
public readonly record struct UiKeyEvent(int PhysicalKey, bool IsDown, bool IsRepeat = false, bool Shift = false, bool Control = false, bool Alt = false, bool Meta = false);
public readonly record struct UiTextInput(string Text);
public readonly record struct UiImeComposition(string Text, int SelectionStart, int SelectionLength, bool IsCommitted);
public enum UiInputPacketKind { Spatial, Key, Text, Ime }
public readonly record struct UiInputPacket(UiPointerEvent Spatial, UiKeyEvent Key, UiTextInput Text, UiImeComposition Ime, UiInputPacketKind Kind)
{
    public static UiInputPacket From(UiPointerEvent input) => new(input, default, default, default, UiInputPacketKind.Spatial);
    public static UiInputPacket From(UiKeyEvent input) => new(default, input, default, default, UiInputPacketKind.Key);
    public static UiInputPacket From(UiTextInput input) => new(default, default, input, default, UiInputPacketKind.Text);
    public static UiInputPacket From(UiImeComposition input) => new(default, default, default, input, UiInputPacketKind.Ime);
}
public enum UiRoutedEventPhase { Preview, Bubble }
public readonly record struct UiRoutedEvent(UiElementId Target, UiRoutedEventPhase Phase, UiPointerEventKind Kind, UiPoint Position);
public interface IUiInputDispatcher { void Dispatch(in UiInputPacket packet); }
public interface IUiInputRouter
{
    UiElementId? Focused { get; }
    UiElementId? Captured { get; }
    void RoutePointer(in UiPointerEvent input); void RouteKey(in UiKeyEvent input); void RouteText(in UiTextInput input); void RouteIme(in UiImeComposition input); void Focus(UiElementId? element);
}
public interface IUiClipboard
{
    string? ReadText();
    void SetText(string? text);
    bool HasText { get; }
}
public enum UiClipboardCommand { Copy, Cut, Paste, SelectAll, Undo, Redo }
public interface IUiRoutedEventSink { void OnRoutedEvent(in UiRoutedEvent routedEvent); }
public interface IUiResourceStore
{
    bool TryGet(string key, out object? value);
}
public interface IUiTemplate
{
    IUiElement Build(IUiElement owner);
}
public interface IUiStyle
{
    string Key { get; }
    string TargetType { get; }
    void Apply(IUiElement element);
}
public interface IUiTheme
{
    IUiResourceDictionary Resources { get; }
    IReadOnlyList<IUiStyle> Styles { get; }
    IUiTemplate? GetTemplate(string key);
    void Apply(IUiElement root);
}

public sealed class TextChangedEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}
