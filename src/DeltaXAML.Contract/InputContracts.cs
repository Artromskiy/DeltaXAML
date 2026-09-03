using Delta;
using Delta.Text.Contract;

namespace Delta.XAML.Contract;

/// <summary>Kind of platform-neutral input delivered to a retained UI document.</summary>
public enum UiInputEventKind : byte
{
    PointingDevice,
    Key,
    Text,
    Composition,
}

/// <summary>Stack-friendly tagged input packet. Read only the payload selected by <see cref="Kind"/>.</summary>
public readonly record struct UiInputEvent
{
    private UiInputEvent(
        UiInputEventKind kind,
        UiPointerEvent pointingDevice,
        UiKeyEvent key,
        UiTextInput text,
        UiCompositionEvent composition)
    {
        Kind = kind;
        PointingDevice = pointingDevice;
        Key = key;
        Text = text;
        Composition = composition;
    }

    public UiInputEventKind Kind { get; }

    public UiPointerEvent PointingDevice { get; }

    public UiKeyEvent Key { get; }

    public UiTextInput Text { get; }

    public UiCompositionEvent Composition { get; }

    public static UiInputEvent FromPointingDevice(in UiPointerEvent value) =>
        new(UiInputEventKind.PointingDevice, value, default, default, default);

    public static UiInputEvent FromKey(in UiKeyEvent value) =>
        new(UiInputEventKind.Key, default, value, default, default);

    public static UiInputEvent FromText(in UiTextInput value) =>
        new(UiInputEventKind.Text, default, default, value, default);

    public static UiInputEvent FromComposition(in UiCompositionEvent value) =>
        new(UiInputEventKind.Composition, default, default, default, value);
}

public enum UiPointerEventKind : byte
{
    Enter,
    Leave,
    Move,
    ButtonDown,
    ButtonUp,
    Wheel,
    Cancel,
    CaptureLost,
}

public enum UiPointerDeviceKind : byte
{
    Unknown,
    Mouse,
    Touch,
    Pen,
}

/// <summary>Extensible device button number; zero means no changed button.</summary>
public readonly record struct UiPointerButton(uint Value)
{
    public static UiPointerButton None => default;

    public static UiPointerButton Primary => new(1);

    public static UiPointerButton Secondary => new(2);

    public static UiPointerButton Middle => new(3);
}

/// <summary>Pressed-state snapshot for the first 64 device buttons.</summary>
public readonly record struct UiPointerButtons(ulong Bits)
{
    public bool Contains(UiPointerButton button) =>
        button.Value is > 0 and <= 64 && (Bits & (1UL << ((int)button.Value - 1))) != 0;
}

/// <summary>Pointer coordinates are logical UI units; pressure is normalized to zero through one.</summary>
public readonly record struct UiPointerEvent(
    UiPointerEventKind Kind,
    UiPointerDeviceKind Device,
    ulong PointerId,
    float2 Position,
    float2 Delta,
    float2 WheelDelta,
    UiPointerButton ChangedButton,
    UiPointerButtons PressedButtons,
    float Pressure,
    float2 Tilt);

/// <summary>Normalized physical key identity supplied by the platform adapter.</summary>
public readonly record struct UiPhysicalKey(uint Value);

/// <summary>Normalized logical key identity after applying the active keyboard layout.</summary>
public readonly record struct UiLogicalKey(uint Value);

/// <summary>Snapshot of conventional modifier state; arbitrary chords use the pressed physical-key set.</summary>
public readonly record struct UiModifierState(ulong Bits)
{
    public bool Contains(ulong mask) => (Bits & mask) == mask;
}

public static class UiModifierBits
{
    public const ulong Shift = 1UL << 0;
    public const ulong Control = 1UL << 1;
    public const ulong Alt = 1UL << 2;
    public const ulong Super = 1UL << 3;
    public const ulong CapsLock = 1UL << 4;
    public const ulong NumLock = 1UL << 5;
}

public enum UiKeyEventKind : byte
{
    Down,
    Up,
}

public readonly record struct UiKeyEvent(
    UiKeyEventKind Kind,
    UiPhysicalKey PhysicalKey,
    UiLogicalKey LogicalKey,
    UiModifierState Modifiers,
    bool IsRepeat);

/// <summary>Committed UTF-16 text ready for insertion; this is not a physical key event.</summary>
public readonly record struct UiTextInput(ReadOnlyMemory<char> Text);

public enum UiCompositionStage : byte
{
    Started,
    Updated,
    Finished,
    Cancelled,
}

/// <summary>Uncommitted IME pre-edit text and its UTF-16 selection.</summary>
public readonly record struct UiCompositionEvent(
    UiCompositionStage Stage,
    ReadOnlyMemory<char> Preedit,
    TextRange Selection);
