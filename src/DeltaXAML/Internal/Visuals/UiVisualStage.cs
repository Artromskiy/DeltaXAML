using Delta.Diagnostics;
using Delta;
using Delta.XAML.Contract;
using Retained = DeltaXAML.Internal;
using RetainedElement = DeltaXAML.Internal.UiElement;

namespace Delta.XAML;

/// <summary>Extracts the retained tree into one reusable canonical display-list storage.</summary>
/// <remarks>
/// The stage owns only traversal and visual extraction. Text metrics, shaping and paragraph layout
/// are shared by the document-owned cache. The document owns the canonical output buffers, while
/// the retained tree, property store and runtime node index remain owned by the document/runtime.
/// Returned spans are borrowed until the next mutation or extraction, as defined by
/// <see cref="UiDisplayList"/>.
/// </remarks>
internal sealed class UiVisualStage : IDisposable
{
    private readonly Retained.UiRuntime _runtime;
    private readonly UiTextLayoutCache _textLayout;
    private readonly UiDisplayListStorage _storage;
    private readonly List<VisualVisit> _visualTraversal = new();
    private readonly List<Retained.UiNodeId> _visualChildOrder = new();
    private uint _displayListVersion;
    private uint _displayListTreeVersion;
    private float2 _displayListViewport;
    private bool _hasDisplayList;
    private bool _disposed;

    internal UiVisualStage(
        Retained.UiRuntime runtime,
        UiTextLayoutCache textLayout,
        UiDisplayListStorage storage)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(textLayout);
        ArgumentNullException.ThrowIfNull(storage);
        _runtime = runtime;
        _textLayout = textLayout;
        _storage = storage;
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

        _textLayout.Prune(_runtime);
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
    }

    private UiDisplayList CurrentDisplayList() =>
        _storage.BorrowedView();

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
        var orderCount = 0;
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
                orderCount += current.DisplayVisualCount + current.DisplayTextCount;
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
            var selfClip = new UiClipId(current.DisplayClipIndex);
            var hasVisual = TryGetVisualDraw(current, VisualClip(current, selfClip, visit.ParentClip), out var visual);
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

                _storage.Identities[orderCount++] = VisualIdentity(current);
                visualCount++;
            }

            var hasText = TryGetElementTextRun(
                node.RuntimeType,
                current,
                effective,
                new UiClipId(current.DisplayClipIndex),
                out var run);
            if (hasText != (current.DisplayTextIndex >= 0) ||
                (hasText && current.DisplayTextIndex != textCount))
            {
                return true;
            }

            if (hasText)
            {
                if (!TryBuildTextDraw(run, out var draw, out diagnostic))
                {
                    return false;
                }

                if (!draw.Equals(_storage.Text[current.DisplayTextIndex]))
                {
                    _storage.Text[current.DisplayTextIndex] = draw;
                }

                _storage.Identities[orderCount++] = TextIdentity(run);
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
            orderCount != _storage.OrderCount || _storage.OrderCount != visualCount + textCount)
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

            if (TryGetVisualDraw(current, VisualClip(current, clipId, visit.ParentClip), out var visual))
            {
                EnsureCapacity(ref _storage.Visuals, _storage.VisualCount + 1);
                visualIndex = _storage.VisualCount;
                _storage.Visuals[_storage.VisualCount++] = visual;
                AppendOrder(new UiDrawRef(UiDrawKind.Visual, visualIndex), VisualIdentity(current));
            }

            if ((current.Participation & UiParticipation.Rendering) != 0 && current is Retained.RichTextBlock richText)
            {
                textIndex = _storage.TextCount;
                if (!TryAppendRichText(richText, clipId, out ownTextCount, out diagnostic))
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
                        AppendOrder(
                            new UiDrawRef(UiDrawKind.Text, textIndex + i),
                            TextIdentity(current));
                    }
                }
            }
            else if (TryGetElementTextRun(
                    node.RuntimeType,
                    current,
                    effective,
                    clipId,
                    out var run))
            {
                EnsureCapacity(ref _storage.Text, _storage.TextCount + 1);
                textIndex = _storage.TextCount;
                if (!TryBuildTextDraw(run, out _storage.Text[_storage.TextCount], out diagnostic))
                {
                    return false;
                }

                _storage.TextCount++;
                ownTextCount = 1;
                AppendOrder(new UiDrawRef(UiDrawKind.Text, textIndex), TextIdentity(run));
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

    private static bool TryGetElementTextRun(
        Retained.UiRuntimeTypeIndex runtimeType,
        RetainedElement element,
        Retained.UiRect clip,
        UiClipId clipId,
        out Retained.UiTextRun run)
    {
        run = default;
        if ((element.Participation & UiParticipation.Rendering) == 0 || element is Retained.RichTextBlock)
        {
            return false;
        }

        if (!Retained.UiDescriptorCatalog.TryGetTextRun(
                runtimeType,
                element,
                new(element.Id, element.Generation, element.LayoutScale, element.TextRunVersion),
                out run))
        {
            return false;
        }

        run = run with
        {
            Bounds = element.Bounds,
            Clip = clip,
            ClipId = new Retained.UiClipId((uint)clipId.Value + 1),
            EffectSet = element.EffectSet.Target == UiEffectTarget.Text
                ? element.EffectSet
                : UiEffectSet.None,
        };
        return true;
    }

    private static UiElementIdentity VisualIdentity(RetainedElement element) =>
        new(element.Id.Value, element.Generation, element.OutputVersion);

    private static UiElementIdentity TextIdentity(Retained.UiTextRun run) =>
        new(run.Owner.Value, run.OwnerGeneration, run.Version);

    private static UiElementIdentity TextIdentity(RetainedElement element) =>
        new(element.Id.Value, element.Generation, element.TextRunVersion);

    private void AppendOrder(UiDrawRef drawRef, UiElementIdentity identity)
    {
        EnsureCapacity(ref _storage.Order, _storage.OrderCount + 1);
        EnsureCapacity(ref _storage.Identities, _storage.OrderCount + 1);
        _storage.Order[_storage.OrderCount++] = drawRef;
        _storage.Identities[_storage.OrderCount - 1] = identity;
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

        var bounds = element.Bounds;
        var radii = NormalizeCornerRadii(element.CornerRadius, bounds.Width, bounds.Height);
        var rounded = radii != Delta.XAML.UiCornerRadii.Zero;
        var kind = rounded
            ? hasStroke ? UiVisualKind.Border : UiVisualKind.RoundedRectangle
            : hasStroke ? UiVisualKind.Border : UiVisualKind.SolidRectangle;
        visual = UiVisualDraw.WithPaint(
            kind,
            default,
            ToFloat4(bounds),
            new UiVisualPaint(
                ToColor(element.Background),
                ToColor(element.BorderColor),
                element.BorderWidth,
                new float4(radii.TopLeft, radii.TopRight, radii.BottomRight, radii.BottomLeft))
            {
                Units = element.BorderWidthUnits,
                EffectSet = element.EffectSet.Target == UiEffectTarget.Visual
                    ? element.EffectSet
                    : UiEffectSet.None,
            },
            clip,
            UiResourceId.Empty);
        return true;
    }

    private static UiClipId VisualClip(RetainedElement element, UiClipId self, UiClipId ancestor)
    {
        var outsets = element.EffectSet.Target == UiEffectTarget.Visual
            ? element.EffectSet.Outsets
            : default;
        return outsets.x > 0 || outsets.y > 0 || outsets.z > 0 || outsets.w > 0
            ? ancestor
            : self;
    }

    private static UiCornerRadii NormalizeCornerRadii(UiCornerRadii radii, float width, float height)
    {
        if (!radii.IsFiniteNonNegative || !float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
        {
            return UiCornerRadii.Zero;
        }

        var scale = 1f;
        scale = LimitRadiusScale(scale, width, radii.TopLeft + radii.TopRight);
        scale = LimitRadiusScale(scale, width, radii.BottomLeft + radii.BottomRight);
        scale = LimitRadiusScale(scale, height, radii.TopLeft + radii.BottomLeft);
        scale = LimitRadiusScale(scale, height, radii.TopRight + radii.BottomRight);
        if (scale >= 1f)
        {
            return radii;
        }

        return new UiCornerRadii(
            radii.TopLeft * scale,
            radii.TopRight * scale,
            radii.BottomRight * scale,
            radii.BottomLeft * scale);
    }

    private static float LimitRadiusScale(float current, float side, float adjacentSum)
    {
        return adjacentSum > 0f ? Maths.Min(current, side / adjacentSum) : current;
    }

    private bool TryBuildTextDraw(Retained.UiTextRun run, out UiTextDraw draw, out Diagnostic? diagnostic)
        => _textLayout.TryBuildTextDraw(run, out draw, out diagnostic);

    private bool TryAppendRichText(
        Retained.RichTextBlock owner,
        UiClipId clip,
        out int count,
        out Diagnostic? diagnostic)
    {
        var spans = owner.Spans.Span;
        if (!_textLayout.TryGetRichTextLayout(owner, out var cache, out diagnostic))
        {
            count = 0;
            return false;
        }

        cache.EnsureLayout(owner.Bounds.Width);

        EnsureCapacity(ref _storage.Text, _storage.TextCount + spans.Length);
        var hitCount = 0;
        var hits = cache.HitRanges;
        for (var i = 0; i < spans.Length; i++)
        {
            var origin = cache.Origins[i];
            _storage.Text[_storage.TextCount++] = UiTextDraw.WithPaint(
                cache.Shaped[i],
                new float2(owner.Bounds.X + origin.x, owner.Bounds.Y + origin.y),
                UiTextPaint.Solid(ToColor(spans[i].Color)) with
                {
                    EffectSet = owner.EffectSet.Target == UiEffectTarget.Text
                        ? owner.EffectSet
                        : UiEffectSet.None,
                },
                clip);
            if (spans[i].Link.IsValid)
            {
                var shapedBounds = cache.Bounds[i];
                var width = UiTextLayoutCache.Advance(cache.Shaped[i], shapedBounds);
                if (hits.Length <= hitCount)
                {
                    Array.Resize(ref hits, Maths.Max(4, hitCount + 1));
                }

                hits[hitCount++] = new(
                    new float4(
                        owner.Bounds.X + origin.x + shapedBounds.Left,
                        owner.Bounds.Y + origin.y + shapedBounds.Top,
                        width,
                        Maths.Max(1, shapedBounds.Height)),
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

    private static Diagnostic NodeDiagnostic(Retained.UiNodeId node) =>
        new(new DiagnosticCode("XAML_NODE_STALE"), DiagnosticSeverity.Error, $"Visual node {node.Index}:{node.Generation} could not be resolved.", null);

    private static float4 ToFloat4(Retained.UiRect value) => new(value.X, value.Y, value.Width, value.Height);

    private static float4 ToColor(Retained.UiColor value) => new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
    private static float4 ToColor(UiColor value) => new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);

    private static void EnsureCapacity<T>(ref T[] storage, int count)
    {
        if (storage.Length < count)
        {
            Array.Resize(ref storage, Maths.Max(8, count));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(UiVisualStage));
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
