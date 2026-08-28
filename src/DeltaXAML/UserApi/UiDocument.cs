using Delta.Diagnostics;
using Delta.Maths;
using Delta.Text.Contract;
using Delta.XAML.Contract;
using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

public sealed class UiDocument : IDisposable
{
    private readonly Retained.UiRuntime _runtime;
    private readonly UiTheme? _theme;
    private readonly UiDisplayListStorage _displayListStorage;
    private readonly UiVisualStage _visuals;
    private bool _disposed;

    internal int TextCacheCount => _visuals.TextCacheCount;
    internal UiDisplayListStorage DisplayListStorage => _displayListStorage;

    public UiDocument(UiElement root, ITextService textService)
        : this(root, textService, EmptyFontResolver.Instance)
    {
    }

    public UiDocument(UiElement root, ITextService textService, IUiFontResolver? fontResolver)
        : this(root, textService, fontResolver, null)
    {
    }

    public UiDocument(UiElement root, ITextService textService, IUiFontResolver? fontResolver, UiTheme? theme)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(textService);
        Root = root;
        var resolvedFontResolver = fontResolver ?? EmptyFontResolver.Instance;
        _theme = theme;
        _runtime = new Retained.UiRuntime(root.RetainedElement);
        _displayListStorage = new UiDisplayListStorage();
        _visuals = new UiVisualStage(_runtime, textService, resolvedFontResolver, _displayListStorage);
    }

    public UiElement Root { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.Dispose();
        _visuals.Dispose();
    }

    public void Dispatch(in UiInputEvent input)
    {
        ThrowIfDisposed();
        switch (input.Kind)
        {
            case UiInputEventKind.PointingDevice:
                {
                    var packet = Retained.UiInputPacket.From(new Retained.UiPointerEvent(
                        ToRetainedPointerKind(input.PointingDevice.Kind),
                        new(input.PointingDevice.Position.x, input.PointingDevice.Position.y),
                        (int)input.PointingDevice.ChangedButton.Value,
                        input.PointingDevice.WheelDelta.y));
                    _runtime.EnqueueInput(in packet);
                    break;
                }
            case UiInputEventKind.Key:
                {
                    var packet = Retained.UiInputPacket.From(new Retained.UiKeyEvent(
                        checked((int)input.Key.PhysicalKey.Value),
                        input.Key.Kind == UiKeyEventKind.Down,
                        input.Key.IsRepeat,
                        input.Key.Modifiers.Contains(UiModifierBits.Shift),
                        input.Key.Modifiers.Contains(UiModifierBits.Control),
                        input.Key.Modifiers.Contains(UiModifierBits.Alt),
                        input.Key.Modifiers.Contains(UiModifierBits.Super)));
                    _runtime.EnqueueInput(in packet);
                    break;
                }
            case UiInputEventKind.Text:
                {
                    var packet = Retained.UiInputPacket.From(new Retained.UiTextInput(input.Text.Text));
                    _runtime.EnqueueInput(in packet);
                    break;
                }
            case UiInputEventKind.Composition:
                {
                    var composition = input.Composition;
                    var packet = Retained.UiInputPacket.From(new Retained.UiImeComposition(
                        composition.Preedit,
                        composition.Selection.StartUtf16,
                        composition.Selection.LengthUtf16,
                        composition.Stage == UiCompositionStage.Finished));
                    _runtime.EnqueueInput(in packet);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }
    }

    public void Dispatch(ReadOnlySpan<UiInputEvent> input)
    {
        for (var i = 0; i < input.Length; i++)
        {
            Dispatch(input[i]);
        }
    }

    public void Layout(float2 viewport, float dpiScale)
    {
        ThrowIfDisposed();
        _runtime.Layout(new(viewport.x, viewport.y), dpiScale, _theme, Root);
    }

    public UiDisplayList BuildDisplayList()
    {
        if (!TryBuildDisplayList(out var displayList, out var diagnostic))
        {
            if (diagnostic is { } failure)
            {
                throw new InvalidOperationException($"{failure.Code.Value}: {failure.Message}");
            }

            throw new InvalidOperationException("The UI display list could not be built.");
        }

        return displayList;
    }

    public bool TryBuildDisplayList(out UiDisplayList displayList, out Diagnostic? diagnostic)
    {
        ThrowIfDisposed();
        return _visuals.TryBuild(Root.RetainedElement, out displayList, out diagnostic);
    }

    private sealed class EmptyFontResolver : IUiFontResolver
    {
        internal static EmptyFontResolver Instance { get; } = new();

        public bool TryResolve(string fontKey, out FontOpenRequest request)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
            request = default;
            return false;
        }
    }

    private static Retained.UiPointerEventKind ToRetainedPointerKind(UiPointerEventKind kind) => kind switch
    {
        UiPointerEventKind.Enter => Retained.UiPointerEventKind.Enter,
        UiPointerEventKind.Leave => Retained.UiPointerEventKind.Leave,
        UiPointerEventKind.Move => Retained.UiPointerEventKind.Move,
        UiPointerEventKind.ButtonDown => Retained.UiPointerEventKind.Down,
        UiPointerEventKind.ButtonUp => Retained.UiPointerEventKind.Up,
        UiPointerEventKind.Wheel => Retained.UiPointerEventKind.Wheel,
        UiPointerEventKind.Cancel => Retained.UiPointerEventKind.Cancel,
        UiPointerEventKind.CaptureLost => Retained.UiPointerEventKind.CaptureLost,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported pointer event kind."),
    };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(UiDocument));
    }
}
