using System.Diagnostics.CodeAnalysis;
using IUiResourceDictionary = DeltaXAML.Internal.IUiResourceStore;
using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

[Flags] internal enum UiDirtyMask { None = 0, Tree = 1, Style = 2, Binding = 4, Measure = 8, Arrange = 16, Visual = 32, HitTest = 64, Resource = 128 }
internal enum UiVisibility { Visible, Hidden, Collapsed }
internal enum UiOrientation { Horizontal, Vertical }
internal enum UiValueSource { Default, Local, Style, Binding, Handle }
internal readonly record struct UiSize(float Width, float Height);
internal readonly record struct UiPoint(float X, float Y);
internal readonly record struct UiThickness(float Left, float Top, float Right, float Bottom) { public static UiThickness Zero => default; public float Horizontal => Left + Right; public float Vertical => Top + Bottom; }
internal readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public bool Contains(UiPoint p) => p.X >= X && p.Y >= Y && p.X <= X + Width && p.Y <= Y + Height;
    public bool IsInside(UiRect other) => X >= other.X && Y >= other.Y && X + Width <= other.X + other.Width && Y + Height <= other.Y + other.Height;
    public static UiRect Intersect(UiRect a, UiRect b) { var x = MathF.Max(a.X, b.X); var y = MathF.Max(a.Y, b.Y); var r = MathF.Min(a.X + a.Width, b.X + b.Width); var z = MathF.Min(a.Y + a.Height, b.Y + b.Height); return new(x, y, MathF.Max(0, r - x), MathF.Max(0, z - y)); }
}
internal readonly record struct UiColor(byte R, byte G, byte B, byte A = 255);
internal readonly record struct UiElementId(uint Value) { public bool IsValid => Value != 0; }
internal readonly record struct UiPropertyHandle(UiElementId Element, uint Generation, string Name);
internal readonly record struct UiMutation(UiPropertyHandle Target, object? Value, UiDirtyFlags Invalidation);
internal enum UiBindingMode { OneWay, TwoWay, OneTime }
internal readonly record struct UiResourceHandle(ulong Value, uint Generation);
internal readonly record struct UiResourceReference(string Key);
internal enum UiAutomationRole { None, Unknown, Generic, Button, Window, Text, TextBox, NumericEditor }
internal readonly record struct UiAutomationMetadata(string Name, UiAutomationRole Role, string ValueText, bool IsEnabled, bool IsInvalid);
internal enum UiVisualState { Normal, Hover, Pressed, Focused, Disabled, Invalid, Selected }
internal readonly record struct UiStateSnapshot(UiVisualState State, bool IsEnabled, bool IsInvalid, bool IsSelected, bool IsFocused, bool IsHovered, bool IsPressed);

internal interface IUiElement
{
    UiElementId Id { get; }
    uint Generation { get; }
    string TypeName { get; }
    IUiElement? Parent { get; }
    IReadOnlyList<IUiElement> Children { get; }
    UiVisibility Visibility { get; }
    Delta.XAML.UiParticipation Participation { get; }
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
internal interface IUiPanel : IUiElement { void Add(IUiElement child); bool Remove(IUiElement child); }
internal interface IUiValue
{
    object? UntypedValue { get; }
    UiValueSource Source { get; }
    UiDirtyFlags Invalidation { get; }
}
internal interface IUiBinding
{
    object? Read(); bool TryWrite(object? value, [NotNullWhen(false)] out string? diagnostic); event EventHandler? Changed;
}
internal interface IUiCompiledBinding : IUiBinding
{
    UiBindingMode Mode { get; }
    Type ValueType { get; }
}
internal interface IUiPropertyStore
{
    void SetDefault(string name, object? value, UiDirtyFlags invalidation); void SetLocal(string name, object? value, UiDirtyFlags invalidation); void SetStyle(string name, object? value, UiDirtyFlags invalidation);
    void SetBinding(string name, IUiBinding binding, UiDirtyFlags invalidation); void SetHandle(string name, object? value, UiDirtyFlags invalidation); void Clear(string name, UiValueSource source); bool TryGet(string name, [NotNullWhen(true)] out IUiValue? value);
    UiPropertyHandle GetHandle(string name); bool TrySet(UiPropertyHandle handle, object? value, UiDirtyFlags invalidation, [NotNullWhen(false)] out string? diagnostic);
}

internal enum UiDrawKind { Rectangle, TextRun, Image, Viewport }
internal readonly record struct UiClipId(uint Value);
internal readonly record struct UiClipEntry(UiClipId Id, UiRect Bounds, UiClipId Parent);
/// <summary>Renderer-neutral text request; it is not shaped glyph data.</summary>
/// <remarks>DeltaXAML owns content, style, layout bounds, DPI-dependent text metrics and identity. Owner plus OwnerGeneration identify retained lifetime; Version identifies text/style/DPI dirtiness. Layout changes are represented by Bounds, Clip and draw-list deltas. Shaping and glyph pixels remain external.</remarks>
internal readonly record struct UiTextRun(string FontKey, float FontSize, string Text, string GlyphRunKey, UiColor Color, UiRect Bounds, UiRect Clip, UiElementId Owner, uint OwnerGeneration, uint Version, UiClipId ClipId = default);
internal readonly record struct UiTextMeasureContext(UiSize Available, float DpiScale);
internal readonly record struct UiTextVisualContext(UiElementId Owner, uint OwnerGeneration, UiRect Bounds, UiRect Clip, float LayoutScale, string GlyphRunKey, uint Version);
internal readonly record struct UiBindingSpec(string Property, string Path, UiBindingMode Mode, string? ConverterKey, string? StringFormat);
internal readonly record struct UiDrawRange(int Start, int Count);
internal readonly record struct UiDrawDelta(UiDrawRange Commands, UiDrawRange TextRuns, uint BaseVersion, uint NextVersion)
{
    public UiDrawRange Clips { get; init; }
}
internal readonly record struct UiDrawCommand(UiDrawKind Kind, UiRect Bounds, UiRect Clip, UiClipId ClipId, UiResourceHandle Resource, UiColor Color, string? Text, int ZIndex, uint Order, UiElementId Owner);
/// <summary>Canonical renderer-neutral producer for one retained UI frame.</summary>
/// <remarks>Memory views borrow the producing frame storage until its next extraction; copy them to retain data beyond that boundary.</remarks>
internal interface IUiDrawList
{
    ReadOnlyMemory<UiDrawCommand> Commands { get; }
    ReadOnlyMemory<UiClipEntry> Clips { get; }
    ReadOnlyMemory<UiTextRun> TextRuns { get; }
    /// <summary>Changes only when commands, clips or text requests change.</summary>
    uint Version { get; }
    UiDrawDelta GetDeltaSince(uint version);
}
internal readonly record struct UiFrameContext(UiSize Viewport, float DpiScale, uint FrameNumber);
internal interface IUiMutationSink { void Enqueue(in UiMutation mutation); }
internal interface IUiFrame : IUiMutationSink { IUiElement Root { get; } void ApplyMutations(); void Layout(UiSize viewport, float dpiScale); IUiDrawList ExtractDrawList(in UiFrameContext context); }

internal enum UiPointerEventKind { Move, Down, Up, Wheel }
internal readonly record struct UiPointerEvent(UiPointerEventKind Kind, UiPoint Position, int Button = 0, float WheelDelta = 0);
internal readonly record struct UiKeyEvent(int PhysicalKey, bool IsDown, bool IsRepeat = false, bool Shift = false, bool Control = false, bool Alt = false, bool Meta = false);
internal readonly record struct UiTextInput(string Text);
internal readonly record struct UiImeComposition(string Text, int SelectionStart, int SelectionLength, bool IsCommitted);
internal enum UiInputPacketKind { Spatial, Key, Text, Ime }
internal readonly record struct UiInputPacket(UiPointerEvent Spatial, UiKeyEvent Key, UiTextInput Text, UiImeComposition Ime, UiInputPacketKind Kind)
{
    public static UiInputPacket From(UiPointerEvent input) => new(input, default, default, default, UiInputPacketKind.Spatial);
    public static UiInputPacket From(UiKeyEvent input) => new(default, input, default, default, UiInputPacketKind.Key);
    public static UiInputPacket From(UiTextInput input) => new(default, default, input, default, UiInputPacketKind.Text);
    public static UiInputPacket From(UiImeComposition input) => new(default, default, default, input, UiInputPacketKind.Ime);
}
internal enum UiRoutedEventPhase { Preview, Bubble }
internal readonly record struct UiRoutedEvent(UiElementId Target, UiRoutedEventPhase Phase, UiPointerEventKind Kind, UiPoint Position);
internal interface IUiInputDispatcher { void Dispatch(in UiInputPacket packet); }
internal interface IUiInputRouter
{
    UiElementId? Focused { get; }
    UiElementId? Captured { get; }
    void RoutePointer(in UiPointerEvent input); void RouteKey(in UiKeyEvent input); void RouteText(in UiTextInput input); void RouteIme(in UiImeComposition input); void Focus(UiElementId? element);
}
internal interface IUiClipboard
{
    string? ReadText();
    void SetText(string? text);
    bool HasText { get; }
}
internal enum UiClipboardCommand { Copy, Cut, Paste, SelectAll, Undo, Redo }
internal interface IUiRoutedEventSink { void OnRoutedEvent(in UiRoutedEvent routedEvent); }
internal interface IUiResourceStore
{
    bool TryGet(string key, out object? value);
}
internal interface IUiTemplate
{
    IUiElement Build(IUiElement owner);
}
internal interface IUiStyle
{
    string Key { get; }
    string TargetType { get; }
    void Apply(IUiElement element);
}
internal interface IUiTheme
{
    IUiResourceDictionary Resources { get; }
    IReadOnlyList<IUiStyle> Styles { get; }
    IUiTemplate? GetTemplate(string key);
    void Apply(IUiElement root);
}

internal sealed class TextChangedEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}
