using Delta;
using Delta.XAML.Contract;

namespace Delta.XAML;

/// <summary>Stable application command identity emitted by controls and gestures.</summary>
public readonly record struct UiCommandId(Guid Value)
{
    public static UiCommandId Empty => default;

    public bool IsValid => Value != Guid.Empty;
}

[Flags]
public enum UiGestureKind
{
    None = 0,
    Tap = 1 << 0,
    MultipleTap = 1 << 1,
    LongPress = 1 << 2,
    Drag = 1 << 3,
    Pan = 1 << 4,
    Swipe = 1 << 5,
    Pinch = 1 << 6,
}

public enum UiSemanticActionKind
{
    None,
    Command,
    Tap,
    MultipleTap,
    LongPress,
    DragStarted,
    DragDelta,
    DragCompleted,
    Swipe,
    Pinch,
    Hyperlink,
    Navigate,
    OpenUri,
    BeginDragDrop,
    OpenFileDialog,
    RequestAsset,
}

/// <summary>Value payload produced by the document-owned gesture arena.</summary>
public readonly record struct UiGestureData(
    UiGestureKind Kind,
    float2 Position,
    float2 Delta,
    float Scale,
    int TapCount);

/// <summary>Borrowed semantic output published after a runtime stage completes.</summary>
public readonly record struct UiSemanticCommand(
    UiCommandId Command,
    UiElement Source,
    UiSemanticActionKind Action,
    UiGestureData Gesture,
    string? Argument = null);

/// <summary>Canonical input event plus a host-owned monotonic timestamp.</summary>
public readonly record struct UiInputSample(UiInputEvent Event, TimeSpan Timestamp);

/// <summary>Physical keyboard chord routed to a semantic command.</summary>
public readonly record struct UiKeyGesture(UiPhysicalKey Key, UiModifierState Modifiers);

/// <summary>Renderer-neutral image metadata used for measure and placeholder state.</summary>
public readonly record struct UiImageMetadata(float Width, float Height, bool IsReady, bool HasError);

public enum UiImageStatus
{
    Empty,
    Loading,
    Ready,
    Error,
}

public enum UiImageStretch
{
    None,
    Fill,
    Uniform,
    UniformToFill,
}

public interface IUiImageMetadataResolver
{
    bool TryGetMetadata(UiResourceId image, out UiImageMetadata metadata);
}

public interface IUiNavigationService
{
    ValueTask NavigateAsync(string route, CancellationToken cancellationToken = default);
}

public interface IUiUriActivationService
{
    ValueTask OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}

public interface IUiFileDialogService
{
    ValueTask<string?> OpenFileAsync(string? filter, CancellationToken cancellationToken = default);
}

public interface IUiDragDropService
{
    ValueTask BeginDragAsync(string format, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}

public interface IUiAssetService
{
    ValueTask<ReadOnlyMemory<byte>> LoadAsync(UiResourceId resource, CancellationToken cancellationToken = default);
}
