using Delta.Diagnostics;
using Delta.Maths;
using Delta.Text.Contract;
using Delta.XAML.Contract;
using Retained = DeltaXAML.Internal;
using RetainedElement = DeltaXAML.Internal.UiElement;

namespace Delta.XAML;

/// <summary>Extracts the retained tree into one reusable canonical display-list storage.</summary>
/// <remarks>
/// The stage owns only visual output buffers and shaped-text cache entries. The retained tree,
/// property store and runtime node index remain owned by the document/runtime. Returned spans are
/// borrowed until the next mutation or extraction, as defined by <see cref="UiDisplayList"/>.
/// </remarks>
internal sealed class UiVisualStage : IDisposable
{
    private readonly Retained.UiRuntime _runtime;
    private readonly ITextService _textService;
    private readonly IUiFontResolver _fontResolver;
    private readonly Dictionary<string, FontInstanceId> _fontInstances = new(StringComparer.Ordinal);
    private readonly Dictionary<UiTextCacheKey, UiTextCacheEntry> _textCache = new();
    private readonly List<UiTextCacheKey> _staleTextCacheKeys = new();
    private readonly FontInstanceId[] _singleFontFallback = new FontInstanceId[1];
    private UiVisualCommand[] _visuals = Array.Empty<UiVisualCommand>();
    private UiClip[] _clips = Array.Empty<UiClip>();
    private UiTextDraw[] _text = Array.Empty<UiTextDraw>();
    private readonly List<VisualVisit> _visualTraversal = new();
    private readonly List<Retained.UiNodeId> _visualChildOrder = new();
    private int _visualCount;
    private int _clipCount;
    private int _textCount;
    private uint _displayListVersion;
    private uint _displayListTreeVersion;
    private float2 _displayListViewport;
    private bool _hasDisplayList;
    private bool _disposed;

    internal UiVisualStage(Retained.UiRuntime runtime, ITextService textService, IUiFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(textService);
        ArgumentNullException.ThrowIfNull(fontResolver);
        _runtime = runtime;
        _textService = textService;
        _fontResolver = fontResolver;
    }

    internal int TextCacheCount => _textCache.Count;

    internal bool TryBuild(RetainedElement retainedRoot, out UiDisplayList displayList, out Diagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(retainedRoot);
        ThrowIfDisposed();
        diagnostic = null;
        var rootId = new Retained.UiNodeId(retainedRoot.Id.Value, retainedRoot.Generation);
        var viewport = new float2(retainedRoot.Bounds.Width, retainedRoot.Bounds.Height);
        if (_hasDisplayList && _displayListVersion == retainedRoot.OutputVersion && _displayListViewport == viewport)
        {
            displayList = CurrentDisplayList();
            return true;
        }

        if (_hasDisplayList &&
            _displayListTreeVersion == retainedRoot.TreeVersion &&
            TryUpdateVisuals(rootId, new Retained.UiRect(0, 0, viewport.x, viewport.y), UiClipId.None, out var compatible, out diagnostic) &&
            compatible)
        {
            _displayListVersion = retainedRoot.OutputVersion;
            _displayListViewport = viewport;
            displayList = CurrentDisplayList();
            return true;
        }

        if (diagnostic is not null)
        {
            displayList = default;
            return false;
        }

        _visualCount = 0;
        _clipCount = 0;
        _textCount = 0;
        if (!TryExtractVisuals(rootId, new Retained.UiRect(0, 0, viewport.x, viewport.y), UiClipId.None, out diagnostic))
        {
            displayList = default;
            return false;
        }

        PruneTextCache();
        _displayListVersion = retainedRoot.OutputVersion;
        _displayListTreeVersion = retainedRoot.TreeVersion;
        _displayListViewport = viewport;
        _hasDisplayList = true;
        displayList = CurrentDisplayList();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var font in _fontInstances.Values)
        {
            _textService.CloseFont(font);
        }

        _fontInstances.Clear();
        _textCache.Clear();
        _staleTextCacheKeys.Clear();
    }

    private UiDisplayList CurrentDisplayList() =>
        new(_visuals.AsSpan(0, _visualCount), _clips.AsSpan(0, _clipCount), _text.AsSpan(0, _textCount));

    private void PruneTextCache()
    {
        if (_textCache.Count == 0)
        {
            return;
        }

        _staleTextCacheKeys.Clear();
        foreach (var pair in _textCache)
        {
            var node = new Retained.UiNodeId(pair.Key.Owner.Value, pair.Key.Generation);
            if (!_runtime.TryGetNode(node, out _))
            {
                _staleTextCacheKeys.Add(pair.Key);
            }
        }

        for (var i = 0; i < _staleTextCacheKeys.Count; i++)
        {
            _textCache.Remove(_staleTextCacheKeys[i]);
        }

        _staleTextCacheKeys.Clear();
    }

    private bool TryUpdateVisuals(
        Retained.UiNodeId root,
        Retained.UiRect clip,
        UiClipId parentClip,
        out bool compatible,
        out Diagnostic? diagnostic)
    {
        compatible = false;
        diagnostic = null;
        var clipCount = 0;
        var visualCount = 0;
        var textCount = 0;
        _visualTraversal.Clear();
        _visualTraversal.Add(new(root, clip, parentClip));
        while (_visualTraversal.Count != 0)
        {
            var last = _visualTraversal.Count - 1;
            var visit = _visualTraversal[last];
            _visualTraversal.RemoveAt(last);
            if (!_runtime.TryGetNode(visit.Id, out var node) || node.Element is not { } current)
            {
                return true;
            }

            if (current.Visibility != Retained.UiVisibility.Visible ||
                (current.Participation & UiParticipation.Layout) == 0)
            {
                if (current.DisplayClipIndex >= 0 || current.DisplayVisualIndex >= 0 || current.DisplayTextIndex >= 0)
                {
                    return true;
                }

                continue;
            }

            if (current.DisplayClipIndex < 0 || current.DisplayClipIndex >= _clipCount)
            {
                return true;
            }

            var effective = Retained.UiRect.Intersect(visit.Clip, current.Bounds);
            var expectedClip = new UiClip(ToFloat4(effective), new UiClipId(visit.ParentClip.Value));
            if (!current.NeedsVisualExtraction && expectedClip.Equals(_clips[current.DisplayClipIndex]))
            {
                clipCount += current.DisplayClipCount;
                visualCount += current.DisplayVisualCount;
                textCount += current.DisplayTextCount;
                continue;
            }

            if (current.DisplayClipIndex != clipCount)
            {
                return true;
            }

            if (!expectedClip.Equals(_clips[current.DisplayClipIndex]))
            {
                _clips[current.DisplayClipIndex] = expectedClip;
            }

            clipCount++;
            var hasVisual = TryGetVisualCommand(current, new UiClipId(current.DisplayClipIndex), out var visual);
            if (hasVisual != (current.DisplayVisualIndex >= 0) ||
                (hasVisual && current.DisplayVisualIndex != visualCount))
            {
                return true;
            }

            if (hasVisual)
            {
                if (!visual.Equals(_visuals[current.DisplayVisualIndex]))
                {
                    _visuals[current.DisplayVisualIndex] = visual;
                }

                visualCount++;
            }

            Retained.UiTextRun run = default;
            var hasText = (current.Participation & UiParticipation.Rendering) != 0 &&
                          Retained.UiDescriptorCatalog.TryGetTextRun(
                              node.RuntimeType,
                              current,
                              new(current.Id, current.Generation, current.LayoutScale, current.TextRunVersion),
                              out run);
            if (hasText != (current.DisplayTextIndex >= 0) ||
                (hasText && current.DisplayTextIndex != textCount))
            {
                return true;
            }

            if (hasText)
            {
                run = run with
                {
                    Bounds = current.Bounds,
                    Clip = effective,
                    ClipId = new Retained.UiClipId((uint)current.DisplayClipIndex + 1),
                };
                if (!TryBuildTextDraw(run, out var draw, out diagnostic))
                {
                    return false;
                }

                if (!draw.Equals(_text[current.DisplayTextIndex]))
                {
                    _text[current.DisplayTextIndex] = draw;
                }

                textCount++;
            }

            current.CompleteVisualExtraction();
            if (!_runtime.TryCopyVisualChildren(visit.Id, _visualChildOrder))
            {
                return true;
            }

            for (var i = _visualChildOrder.Count - 1; i >= 0; i--)
            {
                _visualTraversal.Add(new(_visualChildOrder[i], effective, new UiClipId(current.DisplayClipIndex)));
            }
        }

        if (clipCount != _clipCount || visualCount != _visualCount || textCount != _textCount)
        {
            return true;
        }

        compatible = true;
        return true;
    }

    private bool TryExtractVisuals(Retained.UiNodeId root, Retained.UiRect clip, UiClipId parentClip, out Diagnostic? diagnostic)
    {
        diagnostic = null;
        _visualTraversal.Clear();
        _visualTraversal.Add(new(root, clip, parentClip));
        while (_visualTraversal.Count != 0)
        {
            var last = _visualTraversal.Count - 1;
            var visit = _visualTraversal[last];
            _visualTraversal.RemoveAt(last);
            if (!_runtime.TryGetNode(visit.Id, out var node) || node.Element is not { } current)
            {
                diagnostic = NodeDiagnostic(visit.Id);
                return false;
            }

            if (visit.Exit)
            {
                var clips = 1;
                var visuals = current.DisplayVisualIndex >= 0 ? 1 : 0;
                var text = current.DisplayTextIndex >= 0 ? 1 : 0;
                if (!_runtime.TryCopyVisualChildren(visit.Id, _visualChildOrder))
                {
                    diagnostic = NodeDiagnostic(visit.Id);
                    return false;
                }

                for (var i = 0; i < _visualChildOrder.Count; i++)
                {
                    if (!_runtime.TryGetNode(_visualChildOrder[i], out var childNode) || childNode.Element is not { } child)
                    {
                        diagnostic = NodeDiagnostic(_visualChildOrder[i]);
                        return false;
                    }

                    clips += child.DisplayClipCount;
                    visuals += child.DisplayVisualCount;
                    text += child.DisplayTextCount;
                }

                current.SetDisplaySubtreeCounts(clips, visuals, text);
                continue;
            }

            if (current.Visibility != Retained.UiVisibility.Visible ||
                (current.Participation & UiParticipation.Layout) == 0)
            {
                current.ClearDisplayRange();
                current.CompleteVisualExtraction();
                continue;
            }

            var effective = Retained.UiRect.Intersect(visit.Clip, current.Bounds);
            EnsureCapacity(ref _clips, _clipCount + 1);
            var clipId = new UiClipId(_clipCount);
            _clips[_clipCount++] = new(ToFloat4(effective), visit.ParentClip);
            var visualIndex = -1;
            var textIndex = -1;

            if (TryGetVisualCommand(current, clipId, out var visual))
            {
                EnsureCapacity(ref _visuals, _visualCount + 1);
                visualIndex = _visualCount;
                _visuals[_visualCount++] = visual;
            }

            if ((current.Participation & UiParticipation.Rendering) != 0 &&
                Retained.UiDescriptorCatalog.TryGetTextRun(
                    node.RuntimeType,
                    current,
                    new(current.Id, current.Generation, current.LayoutScale, current.TextRunVersion),
                    out var run))
            {
                EnsureCapacity(ref _text, _textCount + 1);
                textIndex = _textCount;
                run = run with { Bounds = current.Bounds, Clip = effective, ClipId = new Retained.UiClipId((uint)clipId.Value + 1) };
                if (!TryBuildTextDraw(run, out _text[_textCount], out diagnostic))
                {
                    return false;
                }

                _textCount++;
            }

            current.SetDisplayRange(clipId.Value, visualIndex, textIndex);
            current.CompleteVisualExtraction();
            _visualTraversal.Add(new(visit.Id, effective, visit.ParentClip, true));

            if (!_runtime.TryCopyVisualChildren(visit.Id, _visualChildOrder))
            {
                diagnostic = NodeDiagnostic(visit.Id);
                return false;
            }

            for (var i = _visualChildOrder.Count - 1; i >= 0; i--)
            {
                _visualTraversal.Add(new(_visualChildOrder[i], effective, clipId));
            }
        }

        return true;
    }

    private static bool TryGetVisualCommand(RetainedElement element, UiClipId clip, out UiVisualCommand visual)
    {
        if ((element.Participation & UiParticipation.Rendering) == 0)
        {
            visual = default;
            return false;
        }

        if (element.HasCustomVisual)
        {
            visual = new(
                UiVisualKind.Custom,
                new UiVisualTypeId(element.CustomVisualTypeId),
                ToFloat4(element.Bounds),
                ToColor(element.CustomVisualColor),
                clip,
                new UiResourceId(element.CustomVisualResourceId));
            return true;
        }

        if (element.Background.A <= 0)
        {
            visual = default;
            return false;
        }

        visual = new(
            UiVisualKind.SolidRectangle,
            default,
            ToFloat4(element.Bounds),
            ToColor(element.Background),
            clip,
            UiResourceId.Empty);
        return true;
    }

    private bool TryBuildTextDraw(Retained.UiTextRun run, out UiTextDraw draw, out Diagnostic? diagnostic)
    {
        draw = default;
        if (!_fontResolver.TryResolve(run.FontKey, out var request))
        {
            diagnostic = TextDiagnostic("XAML_TEXT_FONT_NOT_FOUND", $"Font key '{run.FontKey}' was not registered.");
            return false;
        }

        if (!_fontInstances.TryGetValue(run.FontKey, out var font))
        {
            try
            {
                font = _textService.OpenFont(request);
            }
            catch (ArgumentException exception)
            {
                diagnostic = TextDiagnostic("XAML_TEXT_FONT_OPEN_FAILED", exception.Message);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                diagnostic = TextDiagnostic("XAML_TEXT_FONT_OPEN_FAILED", exception.Message);
                return false;
            }

            _fontInstances.Add(run.FontKey, font);
        }

        var cacheKey = new UiTextCacheKey(run.Owner, run.OwnerGeneration, run.GlyphRunKey);
        if (!_textCache.TryGetValue(cacheKey, out var cache) ||
            cache.FontKey != run.FontKey ||
            !cache.Text.Equals(run.Text, StringComparison.Ordinal) ||
            !cache.FontSize.Equals(run.FontSize))
        {
            try
            {
                _singleFontFallback[0] = font;
                var shaped = _textService.Shape(new TextShapeRequest(
                    run.Text.AsMemory(),
                    run.FontSize,
                    _singleFontFallback,
                    TextDirection.LeftToRight));
                cache = new UiTextCacheEntry(run.FontKey, run.Text, run.FontSize, shaped);
                _textCache[cacheKey] = cache;
            }
            catch (ArgumentException exception)
            {
                diagnostic = TextDiagnostic("XAML_TEXT_SHAPE_FAILED", exception.Message);
                return false;
            }
            catch (NotSupportedException exception)
            {
                diagnostic = TextDiagnostic("XAML_TEXT_SHAPING_UNSUPPORTED", exception.Message);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                diagnostic = TextDiagnostic("XAML_TEXT_SHAPE_FAILED", exception.Message);
                return false;
            }
        }

        var bounds = ShapedBounds(cache.Shaped);
        var baseline = new float2(run.Bounds.X - bounds.Left, run.Bounds.Y - bounds.Top);
        var clip = run.ClipId.Value == 0 ? UiClipId.None : new UiClipId(checked((int)run.ClipId.Value - 1));
        draw = new UiTextDraw(cache.Shaped, baseline, ToColor(run.Color), clip);
        diagnostic = null;
        return true;
    }

    private static TextBounds ShapedBounds(ShapedText shaped)
    {
        var runs = shaped.Runs.Span;
        if (runs.Length == 0)
        {
            return new TextBounds(0, 0, 0, 0);
        }

        var first = runs[0].Bounds;
        var left = first.Left;
        var top = first.Top;
        var right = first.Right;
        var bottom = first.Bottom;
        for (var i = 1; i < runs.Length; i++)
        {
            var bounds = runs[i].Bounds;
            left = MathF.Min(left, bounds.Left);
            top = MathF.Min(top, bounds.Top);
            right = MathF.Max(right, bounds.Right);
            bottom = MathF.Max(bottom, bounds.Bottom);
        }

        return new TextBounds(left, top, right, bottom);
    }

    private static Diagnostic TextDiagnostic(string code, string message) =>
        new(new DiagnosticCode(code), DiagnosticSeverity.Error, message, null);

    private static Diagnostic NodeDiagnostic(Retained.UiNodeId node) =>
        new(new DiagnosticCode("XAML_NODE_STALE"), DiagnosticSeverity.Error, $"Visual node {node.Index}:{node.Generation} could not be resolved.", null);

    private static float4 ToFloat4(Retained.UiRect value) => new(value.X, value.Y, value.Width, value.Height);

    private static float4 ToColor(Retained.UiColor value) => new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);

    private static void EnsureCapacity<T>(ref T[] storage, int count)
    {
        if (storage.Length < count)
        {
            Array.Resize(ref storage, Math.Max(8, count));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(UiVisualStage));
    }

    private readonly record struct UiTextCacheKey(Retained.UiElementId Owner, uint Generation, string GlyphRunKey);

    private sealed record UiTextCacheEntry(string FontKey, string Text, float FontSize, ShapedText Shaped);

    private readonly struct VisualVisit
    {
        public VisualVisit(Retained.UiNodeId id, Retained.UiRect clip, UiClipId parentClip, bool exit = false)
        {
            Id = id;
            Clip = clip;
            ParentClip = parentClip;
            Exit = exit;
        }

        public Retained.UiNodeId Id { get; }
        public Retained.UiRect Clip { get; }
        public UiClipId ParentClip { get; }
        public bool Exit { get; }
    }
}
