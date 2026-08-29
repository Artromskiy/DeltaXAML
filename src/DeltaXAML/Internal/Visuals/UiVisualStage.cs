using Delta.Diagnostics;
using Delta.Maths;
using Delta.Text.Contract;
using Delta.XAML.Contract;
using Retained = DeltaXAML.Internal;
using RetainedElement = DeltaXAML.Internal.UiElement;

namespace Delta.XAML;

/// <summary>Extracts the retained tree into one reusable canonical display-list storage.</summary>
/// <remarks>
/// The stage owns only traversal and shaped-text cache entries. The document owns the canonical
/// output buffers, while the retained tree, property store and runtime node index remain owned by
/// the document/runtime. Returned spans are borrowed until the next mutation or extraction, as
/// defined by <see cref="UiDisplayList"/>.
/// </remarks>
internal sealed class UiVisualStage : IDisposable
{
    private readonly Retained.UiRuntime _runtime;
    private readonly ITextService _textService;
    private readonly IUiFontResolver _fontResolver;
    private readonly UiDisplayListStorage _storage;
    private readonly Dictionary<string, FontInstanceId> _fontInstances = new(StringComparer.Ordinal);
    private UiFontSlot[] _fontSlots = Array.Empty<UiFontSlot>();
    private UiTextCacheEntry?[] _textCache = Array.Empty<UiTextCacheEntry?>();
    private UiRichTextCacheEntry?[] _richTextCache = Array.Empty<UiRichTextCacheEntry?>();
    private readonly FontInstanceId[] _singleFontFallback = new FontInstanceId[1];
    private readonly List<VisualVisit> _visualTraversal = new();
    private readonly List<Retained.UiNodeId> _visualChildOrder = new();
    private int _textCacheCount;
    private uint _displayListVersion;
    private uint _displayListTreeVersion;
    private float2 _displayListViewport;
    private UiLocalizationContext _localization = UiLocalizationContext.Invariant;
    private bool _hasDisplayList;
    private bool _disposed;

    internal UiVisualStage(
        Retained.UiRuntime runtime,
        ITextService textService,
        IUiFontResolver fontResolver,
        UiDisplayListStorage storage)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(textService);
        ArgumentNullException.ThrowIfNull(fontResolver);
        ArgumentNullException.ThrowIfNull(storage);
        _runtime = runtime;
        _textService = textService;
        _fontResolver = fontResolver;
        _storage = storage;
    }

    internal int TextCacheCount => _textCacheCount;

    internal void SetLocalization(UiLocalizationContext localization)
    {
        var validated = localization.Validate();
        if (_localization == validated)
        {
            return;
        }

        _localization = validated;
        Array.Clear(_textCache);
        _textCacheCount = 0;
        _hasDisplayList = false;
    }

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

        _storage.ClearCounts();
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
        _fontSlots = Array.Empty<UiFontSlot>();
        _textCache = Array.Empty<UiTextCacheEntry?>();
        _richTextCache = Array.Empty<UiRichTextCacheEntry?>();
        _textCacheCount = 0;
    }

    private UiDisplayList CurrentDisplayList() =>
        _storage.BorrowedView();

    private void PruneTextCache()
    {
        for (var index = 1; index < _textCache.Length; index++)
        {
            var cache = _textCache[index];
            var richCache = _richTextCache[index];
            if (cache is null && richCache is null)
            {
                continue;
            }

            var generation = cache?.Generation ?? richCache?.Generation ?? 0;
            var node = new Retained.UiNodeId((uint)index, generation);
            if (!_runtime.TryGetNode(node, out _))
            {
                if (cache is not null)
                {
                    _textCache[index] = null;
                    _textCacheCount--;
                }

                _richTextCache[index] = null;
            }
        }
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

            if (current.DisplayClipIndex < 0 || current.DisplayClipIndex >= _storage.ClipCount)
            {
                return true;
            }

            var effective = Retained.UiRect.Intersect(visit.Clip, current.Bounds);
            var expectedClip = new UiClipRegion(ToFloat4(effective), new UiClipId(visit.ParentClip.Value));
            if (!current.NeedsVisualExtraction && expectedClip.Equals(_storage.Clips[current.DisplayClipIndex]))
            {
                clipCount += current.DisplayClipCount;
                visualCount += current.DisplayVisualCount;
                textCount += current.DisplayTextCount;
                continue;
            }

            if (current is Retained.RichTextBlock)
            {
                return true;
            }

            if (current.DisplayClipIndex != clipCount)
            {
                return true;
            }

            if (!expectedClip.Equals(_storage.Clips[current.DisplayClipIndex]))
            {
                _storage.Clips[current.DisplayClipIndex] = expectedClip;
            }

            clipCount++;
            var hasVisual = TryGetVisualDraw(current, new UiClipId(current.DisplayClipIndex), out var visual);
            if (hasVisual != (current.DisplayVisualIndex >= 0) ||
                (hasVisual && current.DisplayVisualIndex != visualCount))
            {
                return true;
            }

            if (hasVisual)
            {
                if (!visual.Equals(_storage.Visuals[current.DisplayVisualIndex]))
                {
                    _storage.Visuals[current.DisplayVisualIndex] = visual;
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

                if (!draw.Equals(_storage.Text[current.DisplayTextIndex]))
                {
                    _storage.Text[current.DisplayTextIndex] = draw;
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

        if (clipCount != _storage.ClipCount || visualCount != _storage.VisualCount || textCount != _storage.TextCount ||
            _storage.OrderCount != visualCount + textCount)
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
                var text = current.DisplayOwnTextCount;
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
            EnsureCapacity(ref _storage.Clips, _storage.ClipCount + 1);
            var clipId = new UiClipId(_storage.ClipCount);
            _storage.Clips[_storage.ClipCount++] = new(ToFloat4(effective), visit.ParentClip);
            var visualIndex = -1;
            var textIndex = -1;
            var ownTextCount = 0;

            if (TryGetVisualDraw(current, clipId, out var visual))
            {
                EnsureCapacity(ref _storage.Visuals, _storage.VisualCount + 1);
                visualIndex = _storage.VisualCount;
                _storage.Visuals[_storage.VisualCount++] = visual;
                AppendOrder(new UiDrawRef(UiDrawKind.Visual, visualIndex));
            }

            if ((current.Participation & UiParticipation.Rendering) != 0 && current is Retained.RichTextBlock richText)
            {
                textIndex = _storage.TextCount;
                if (!TryAppendRichText(richText, effective, clipId, out ownTextCount, out diagnostic))
                {
                    return false;
                }

                if (ownTextCount == 0)
                {
                    textIndex = -1;
                }
                else
                {
                    for (var i = 0; i < ownTextCount; i++)
                    {
                        AppendOrder(new UiDrawRef(UiDrawKind.Text, textIndex + i));
                    }
                }
            }
            else if ((current.Participation & UiParticipation.Rendering) != 0 &&
                Retained.UiDescriptorCatalog.TryGetTextRun(
                    node.RuntimeType,
                    current,
                    new(current.Id, current.Generation, current.LayoutScale, current.TextRunVersion),
                    out var run))
            {
                EnsureCapacity(ref _storage.Text, _storage.TextCount + 1);
                textIndex = _storage.TextCount;
                run = run with { Bounds = current.Bounds, Clip = effective, ClipId = new Retained.UiClipId((uint)clipId.Value + 1) };
                if (!TryBuildTextDraw(run, out _storage.Text[_storage.TextCount], out diagnostic))
                {
                    return false;
                }

                _storage.TextCount++;
                ownTextCount = 1;
                AppendOrder(new UiDrawRef(UiDrawKind.Text, textIndex));
            }

            current.SetDisplayRange(clipId.Value, visualIndex, textIndex, ownTextCount);
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

    private void AppendOrder(UiDrawRef drawRef)
    {
        EnsureCapacity(ref _storage.Order, _storage.OrderCount + 1);
        _storage.Order[_storage.OrderCount++] = drawRef;
    }

    private static bool TryGetVisualDraw(RetainedElement element, UiClipId clip, out UiVisualDraw visual)
    {
        if ((element.Participation & UiParticipation.Rendering) == 0)
        {
            visual = default;
            return false;
        }

        if (element.HasCustomVisual)
        {
            visual = UiVisualDraw.WithPaint(
                UiVisualKind.Custom,
                new UiVisualTypeId(element.CustomVisualTypeId),
                ToFloat4(element.Bounds),
                UiVisualPaint.Solid(ToColor(element.CustomVisualColor)),
                clip,
                new UiResourceId(element.CustomVisualResourceId));
            return true;
        }

        if (element is Retained.Image image && image.DisplaySource != Guid.Empty)
        {
            visual = UiVisualDraw.WithPaint(
                UiVisualKind.Image,
                default,
                ToFloat4(image.ImageBounds),
                UiVisualPaint.Solid(ToColor(image.Tint)),
                clip,
                new UiResourceId(image.DisplaySource));
            return true;
        }

        var hasFill = element.Background.A > 0;
        var hasStroke = element.BorderWidth > 0;
        if (!hasFill && !hasStroke)
        {
            visual = default;
            return false;
        }

        var radii = element.CornerRadius;
        var rounded = radii != Delta.XAML.UiCornerRadii.Zero;
        var kind = rounded
            ? hasStroke ? UiVisualKind.Border : UiVisualKind.RoundedRectangle
            : hasStroke ? UiVisualKind.Border : UiVisualKind.SolidRectangle;
        visual = UiVisualDraw.WithPaint(
            kind,
            default,
            ToFloat4(element.Bounds),
            new UiVisualPaint(
                ToColor(element.Background),
                ToColor(element.BorderColor),
                element.BorderWidth,
                new float4(radii.TopLeft, radii.TopRight, radii.BottomRight, radii.BottomLeft)),
            clip,
            UiResourceId.Empty);
        return true;
    }

    private bool TryBuildTextDraw(Retained.UiTextRun run, out UiTextDraw draw, out Diagnostic? diagnostic)
    {
        draw = default;
        if (!TryResolveFont(run, out var font, out diagnostic))
        {
            return false;
        }

        var ownerIndex = checked((int)run.Owner.Value);
        EnsureOwnerCapacity(ownerIndex);
        var cache = _textCache[ownerIndex];
        if (cache is null || !cache.Matches(run))
        {
            try
            {
                _singleFontFallback[0] = font;
                var shaped = _textService.Shape(new TextShapeRequest(
                    run.Text.AsMemory(),
                    run.FontSize,
                    _singleFontFallback,
                    _localization.Direction,
                    Language: _localization.Language));
                cache = new UiTextCacheEntry(run, shaped);
                if (_textCache[ownerIndex] is null)
                {
                    _textCacheCount++;
                }

                _textCache[ownerIndex] = cache;
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
        draw = UiTextDraw.WithPaint(
            cache.Shaped,
            baseline,
            new UiTextPaint(
                ToColor(run.Color),
                ToColor(run.OutlineColor),
                run.OutlineWidth,
                new UiResourceId(run.TextEffectResource)),
            clip);
        diagnostic = null;
        return true;
    }

    private bool TryAppendRichText(
        Retained.RichTextBlock owner,
        Retained.UiRect clipBounds,
        UiClipId clip,
        out int count,
        out Diagnostic? diagnostic)
    {
        var ownerIndex = checked((int)owner.Id.Value);
        EnsureOwnerCapacity(ownerIndex);
        var spans = owner.Spans.Span;
        var direction = ResolveParagraphDirection(spans, _localization.Direction);
        var cache = _richTextCache[ownerIndex];
        if (cache is null || !cache.MatchesShaping(owner, spans, _localization, direction))
        {
            var shaped = new ShapedText[spans.Length];
            var origins = new float2[spans.Length];
            var bounds = new TextBounds[spans.Length];
            for (var i = 0; i < spans.Length; i++)
            {
                var span = spans[i];
                if (!TryResolveFontKey(span.FontKey, out var font, out diagnostic))
                {
                    count = 0;
                    return false;
                }

                try
                {
                    _singleFontFallback[0] = font;
                    shaped[i] = _textService.Shape(new TextShapeRequest(
                        span.Text.AsMemory(),
                        span.FontSize * owner.LayoutScale,
                        _singleFontFallback,
                        direction,
                        Language: _localization.Language));
                }
                catch (ArgumentException exception)
                {
                    diagnostic = TextDiagnostic("XAML_RICH_TEXT_SHAPE_FAILED", exception.Message);
                    count = 0;
                    return false;
                }
                catch (InvalidOperationException exception)
                {
                    diagnostic = TextDiagnostic("XAML_RICH_TEXT_SHAPE_FAILED", exception.Message);
                    count = 0;
                    return false;
                }
                catch (NotSupportedException exception)
                {
                    diagnostic = TextDiagnostic("XAML_RICH_TEXT_SHAPING_UNSUPPORTED", exception.Message);
                    count = 0;
                    return false;
                }

                bounds[i] = ShapedBounds(shaped[i]);
            }

            LayoutRichRuns(shaped, bounds, origins, owner.Bounds.Width, direction);
            cache = new(owner, spans, _localization, direction, shaped, origins, bounds);
            _richTextCache[ownerIndex] = cache;
        }

        EnsureCapacity(ref _storage.Text, _storage.TextCount + spans.Length);
        var hitCount = 0;
        var hits = cache.HitRanges;
        for (var i = 0; i < spans.Length; i++)
        {
            var origin = cache.Origins[i];
            _storage.Text[_storage.TextCount++] = UiTextDraw.WithPaint(
                cache.Shaped[i],
                new float2(owner.Bounds.X + origin.x, owner.Bounds.Y + origin.y),
                UiTextPaint.Solid(ToColor(spans[i].Color)),
                clip);
            if (spans[i].Link.IsValid)
            {
                var shapedBounds = cache.Bounds[i];
                var width = Advance(cache.Shaped[i], shapedBounds);
                if (hits.Length <= hitCount)
                {
                    Array.Resize(ref hits, Math.Max(4, hitCount + 1));
                }

                hits[hitCount++] = new(
                    new float4(
                        owner.Bounds.X + origin.x + shapedBounds.Left,
                        owner.Bounds.Y + origin.y + shapedBounds.Top,
                        width,
                        MathF.Max(1, shapedBounds.Height)),
                    spans[i].Link,
                    spans[i].LinkArgument);
            }
        }

        cache.HitRanges = hits;
        owner.SetHitRanges(hits, hitCount);

        count = spans.Length;
        diagnostic = null;
        return true;
    }

    private bool TryResolveFontKey(string fontKey, out FontInstanceId font, out Diagnostic? diagnostic)
    {
        if (_fontInstances.TryGetValue(fontKey, out font))
        {
            diagnostic = null;
            return true;
        }

        if (!_fontResolver.TryResolve(fontKey, out var request))
        {
            diagnostic = TextDiagnostic("XAML_TEXT_FONT_NOT_FOUND", $"Font key '{fontKey}' was not registered.");
            return false;
        }

        try
        {
            font = _textService.OpenFont(request);
            _fontInstances.Add(fontKey, font);
            diagnostic = null;
            return true;
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
    }

    private bool TryResolveFont(Retained.UiTextRun run, out FontInstanceId font, out Diagnostic? diagnostic)
    {
        var ownerIndex = checked((int)run.Owner.Value);
        EnsureOwnerCapacity(ownerIndex);
        ref var slot = ref _fontSlots[ownerIndex];
        if (slot.IsValid && slot.Generation == run.OwnerGeneration &&
            string.Equals(slot.FontKey, run.FontKey, StringComparison.Ordinal))
        {
            font = slot.Font;
            diagnostic = null;
            return true;
        }

        if (!_fontInstances.TryGetValue(run.FontKey, out font))
        {
            if (!_fontResolver.TryResolve(run.FontKey, out var request))
            {
                diagnostic = TextDiagnostic("XAML_TEXT_FONT_NOT_FOUND", $"Font key '{run.FontKey}' was not registered.");
                return false;
            }

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

        slot = new UiFontSlot(run.OwnerGeneration, run.FontKey, font);
        diagnostic = null;
        return true;
    }

    private void EnsureOwnerCapacity(int ownerIndex)
    {
        if (ownerIndex < _fontSlots.Length)
        {
            return;
        }

        var length = Math.Max(ownerIndex + 1, Math.Max(8, _fontSlots.Length * 2));
        Array.Resize(ref _fontSlots, length);
        Array.Resize(ref _textCache, length);
        Array.Resize(ref _richTextCache, length);
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

    private static float Advance(ShapedText shaped, TextBounds bounds)
    {
        var runs = shaped.Runs.Span;
        var advance = 0f;
        for (var i = 0; i < runs.Length; i++)
        {
            advance += MathF.Abs(runs[i].AdvanceX);
        }

        return MathF.Max(bounds.Width, advance);
    }

    private static void LayoutRichRuns(
        ShapedText[] shaped,
        TextBounds[] bounds,
        float2[] origins,
        float availableWidth,
        TextDirection direction)
    {
        var first = 0;
        var lineWidth = 0f;
        var lineTop = 0f;
        for (var i = 0; i <= shaped.Length; i++)
        {
            var advance = i < shaped.Length ? Advance(shaped[i], bounds[i]) : 0;
            var breakBefore = i == shaped.Length ||
                (i > first && availableWidth > 0 && lineWidth + advance > availableWidth);
            if (!breakBefore)
            {
                lineWidth += advance;
                continue;
            }

            var ascent = 0f;
            var descent = 0f;
            for (var run = first; run < i; run++)
            {
                ascent = MathF.Max(ascent, MathF.Max(0, -bounds[run].Top));
                descent = MathF.Max(descent, MathF.Max(0, bounds[run].Bottom));
            }

            var rightToLeft = direction == TextDirection.RightToLeft;
            var cursor = rightToLeft && availableWidth > 0 ? availableWidth : 0;
            for (var run = first; run < i; run++)
            {
                var runAdvance = Advance(shaped[run], bounds[run]);
                var left = rightToLeft ? cursor - runAdvance : cursor;
                origins[run] = new(left - bounds[run].Left, lineTop + ascent);
                cursor += rightToLeft ? -runAdvance : runAdvance;
            }

            lineTop += MathF.Max(1, ascent + descent);
            first = i;
            lineWidth = advance;
        }
    }

    private static TextDirection ResolveParagraphDirection(ReadOnlySpan<UiTextSpan> spans, TextDirection requested)
    {
        if (requested != TextDirection.Auto)
        {
            return requested;
        }

        for (var spanIndex = 0; spanIndex < spans.Length; spanIndex++)
        {
            var text = spans[spanIndex].Text;
            for (var characterIndex = 0; characterIndex < text.Length; characterIndex++)
            {
                var character = text[characterIndex];
                if (character is >= '\u0590' and <= '\u08FF')
                {
                    return TextDirection.RightToLeft;
                }

                if (char.IsLetter(character))
                {
                    return TextDirection.LeftToRight;
                }
            }
        }

        return TextDirection.LeftToRight;
    }

    private static Diagnostic TextDiagnostic(string code, string message) =>
        new(new DiagnosticCode(code), DiagnosticSeverity.Error, message, null);

    private static Diagnostic NodeDiagnostic(Retained.UiNodeId node) =>
        new(new DiagnosticCode("XAML_NODE_STALE"), DiagnosticSeverity.Error, $"Visual node {node.Index}:{node.Generation} could not be resolved.", null);

    private static float4 ToFloat4(Retained.UiRect value) => new(value.X, value.Y, value.Width, value.Height);

    private static float4 ToColor(Retained.UiColor value) => new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
    private static float4 ToColor(UiColor value) => new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);

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

    private readonly record struct UiFontSlot(uint Generation, string FontKey, FontInstanceId Font)
    {
        internal bool IsValid => Generation != 0 && Font.IsValid;
    }

    private sealed class UiTextCacheEntry
    {
        internal UiTextCacheEntry(Retained.UiTextRun run, ShapedText shaped)
        {
            Generation = run.OwnerGeneration;
            GlyphRunKey = run.GlyphRunKey;
            FontKey = run.FontKey;
            Text = run.Text;
            FontSize = run.FontSize;
            Shaped = shaped;
        }

        internal uint Generation { get; }
        internal string GlyphRunKey { get; }
        internal string FontKey { get; }
        internal string Text { get; }
        internal float FontSize { get; }
        internal ShapedText Shaped { get; }

        internal bool Matches(Retained.UiTextRun run) =>
            Generation == run.OwnerGeneration &&
            string.Equals(GlyphRunKey, run.GlyphRunKey, StringComparison.Ordinal) &&
            string.Equals(FontKey, run.FontKey, StringComparison.Ordinal) &&
            string.Equals(Text, run.Text, StringComparison.Ordinal) &&
            FontSize.Equals(run.FontSize);
    }

    private sealed class UiRichTextCacheEntry
    {
        private readonly uint _generation;
        private readonly float _width;
        private readonly float _scale;
        private readonly TextDirection _direction;
        private readonly string? _language;
        private readonly string[] _text;
        private readonly string[] _fontKeys;
        private readonly float[] _fontSizes;

        internal UiRichTextCacheEntry(
            Retained.RichTextBlock owner,
            ReadOnlySpan<UiTextSpan> spans,
            UiLocalizationContext localization,
            TextDirection direction,
            ShapedText[] shaped,
            float2[] origins,
            TextBounds[] bounds)
        {
            _generation = owner.Generation;
            _width = owner.Bounds.Width;
            _scale = owner.LayoutScale;
            _direction = direction;
            _language = localization.Language;
            _text = new string[spans.Length];
            _fontKeys = new string[spans.Length];
            _fontSizes = new float[spans.Length];
            for (var i = 0; i < spans.Length; i++)
            {
                _text[i] = spans[i].Text;
                _fontKeys[i] = spans[i].FontKey;
                _fontSizes[i] = spans[i].FontSize;
            }

            Shaped = shaped;
            Origins = origins;
            Bounds = bounds;
            HitRanges = Array.Empty<UiInlineHitRange>();
        }

        internal ShapedText[] Shaped { get; }
        internal float2[] Origins { get; }
        internal TextBounds[] Bounds { get; }
        internal UiInlineHitRange[] HitRanges { get; set; }
        internal uint Generation => _generation;

        internal bool MatchesShaping(
            Retained.RichTextBlock owner,
            ReadOnlySpan<UiTextSpan> spans,
            UiLocalizationContext localization,
            TextDirection direction)
        {
            if (_generation != owner.Generation || !_width.Equals(owner.Bounds.Width) || !_scale.Equals(owner.LayoutScale) ||
                _direction != direction || !string.Equals(_language, localization.Language, StringComparison.Ordinal) ||
                _text.Length != spans.Length)
            {
                return false;
            }

            for (var i = 0; i < spans.Length; i++)
            {
                if (!string.Equals(_text[i], spans[i].Text, StringComparison.Ordinal) ||
                    !string.Equals(_fontKeys[i], spans[i].FontKey, StringComparison.Ordinal) ||
                    !_fontSizes[i].Equals(spans[i].FontSize))
                {
                    return false;
                }
            }

            return true;
        }
    }

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
