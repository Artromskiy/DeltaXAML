using Delta.Diagnostics;
using Delta;
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
    private readonly IUiImageMetadataResolver? _imageMetadataResolver;
    private readonly IUiGeneratedDocumentProgram? _program;
    private readonly Retained.UiAccessibilityStage _accessibility = new();
    private UiLocalizationContext _localization = UiLocalizationContext.Invariant;
    private bool _hasLayout;
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

    public UiDocument(
        UiElement root,
        ITextService textService,
        IUiFontResolver? fontResolver,
        UiTheme? theme,
        IUiImageMetadataResolver? imageMetadataResolver = null,
        IUiGeneratedDocumentProgram? program = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(textService);
        Root = root;
        var resolvedFontResolver = fontResolver ?? EmptyFontResolver.Instance;
        _theme = theme;
        _imageMetadataResolver = imageMetadataResolver;
        _program = program;
        _runtime = new Retained.UiRuntime(root.RetainedElement);
        _displayListStorage = new UiDisplayListStorage();
        _visuals = new UiVisualStage(_runtime, textService, resolvedFontResolver, _displayListStorage);
    }

    public UiElement Root { get; }

    /// <summary>Most recently validated logical viewport; updated before generated stages run.</summary>
    public float2 Viewport { get; private set; }

    public float DpiScale { get; private set; } = 1;

    public UiLocalizationContext Localization
    {
        get => _localization;
        set
        {
            var validated = value.Validate();
            if (_localization == validated)
            {
                return;
            }

            _localization = validated;
            Root.RetainedElement.InvalidateChanged(Retained.UiDirtyMask.Measure | Retained.UiDirtyMask.Text | Retained.UiDirtyMask.Visual);
            _visuals.SetLocalization(validated);
        }
    }

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
        _runtime.EnqueueInput(new UiInputSample(input, TimeSpan.Zero));
    }

    /// <summary>Queues canonical input with a host-provided monotonic gesture timestamp.</summary>
    public void Dispatch(in UiInputSample input)
    {
        ThrowIfDisposed();
        _runtime.EnqueueInput(input);
    }

    public void Dispatch(ReadOnlySpan<UiInputEvent> input)
    {
        for (var i = 0; i < input.Length; i++)
        {
            Dispatch(input[i]);
        }
    }

    public void Dispatch(ReadOnlySpan<UiInputSample> input)
    {
        for (var i = 0; i < input.Length; i++)
        {
            Dispatch(input[i]);
        }
    }

    /// <summary>Borrowed commands remain valid until the next document layout or disposal.</summary>
    public ReadOnlySpan<UiSemanticCommand> SemanticCommands
    {
        get
        {
            ThrowIfDisposed();
            return _runtime.SemanticCommands;
        }
    }

    public void Layout(float2 viewport, float dpiScale)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(viewport.x) || !float.IsFinite(viewport.y) || viewport.x < 0 || viewport.y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), "Viewport dimensions must be finite and non-negative.");
        }

        if (!float.IsFinite(dpiScale) || dpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale), "DPI scale must be finite and positive.");
        }

        Viewport = viewport;
        DpiScale = dpiScale;
        _displayListStorage.DpiScale = dpiScale;
        _runtime.Layout(new(viewport.x, viewport.y), dpiScale, _theme, Root, _imageMetadataResolver, _program, this);
        _hasLayout = true;
    }

    /// <summary>
    /// Returns a detached JSON snapshot of the retained hierarchy and its latest layout.
    /// </summary>
    /// <remarks>
    /// This is a cold debug operation. Call it after <see cref="Layout"/> to inspect
    /// the actual bounds and clips produced for the current viewport. Before the first
    /// layout, <c>layoutCompleted</c> is <c>false</c> and layout fields contain defaults.
    /// The returned string is independent of the document and remains valid after the
    /// next layout or disposal.
    /// </remarks>
    public string BuildLayoutDiagnosticsJson(bool indented = true)
    {
        ThrowIfDisposed();
        return Retained.UiLayoutDiagnosticsJson.Write(Root.RetainedElement, Viewport, DpiScale, _hasLayout, indented);
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
        if (Root.RetainedElement.TryGetResourceDiagnostic(out var code, out var message))
        {
            displayList = default;
            diagnostic = new(new DiagnosticCode(code), DiagnosticSeverity.Error, message, null);
            return false;
        }

        return _visuals.TryBuild(Root.RetainedElement, out displayList, out diagnostic);
    }

    /// <summary>Returns a borrowed platform-neutral accessibility snapshot in logical tree order.</summary>
    public UiSemanticSnapshot BuildSemanticSnapshot()
    {
        ThrowIfDisposed();
        return _accessibility.Extract(Root.RetainedElement);
    }

    internal void PublishGenerated(UiElement source, UiSemanticActionKind action, string? argument) =>
        _runtime.PublishSemantic(source.RetainedElement, action, default, argument);

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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(UiDocument));
    }
}
