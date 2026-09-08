using System.Diagnostics.CodeAnalysis;
using Delta;
using Delta.Diagnostics;
using Delta.Text.Contract;
using Delta.XAML.Contract;
using Retained = DeltaXAML.Internal;
using RetainedElement = DeltaXAML.Internal.UiElement;

namespace Delta.XAML;

/// <summary>Shares text metrics, shaping and paragraph layout between measure and extraction.</summary>
internal sealed class UiTextLayoutCache : IDisposable
{
    private readonly ITextService _textService;
    private readonly IUiFontResolver _fontResolver;
    private readonly Dictionary<string, FontInstanceId> _fontInstances = new(StringComparer.Ordinal);
    private UiFontSlot[] _fontSlots = Array.Empty<UiFontSlot>();
    private UiTextCacheEntry?[] _textCache = Array.Empty<UiTextCacheEntry?>();
    private UiRichTextLayout?[] _richTextCache = Array.Empty<UiRichTextLayout?>();
    private readonly FontInstanceId[] _singleFontFallback = new FontInstanceId[1];
    private UiLocalizationContext _localization = UiLocalizationContext.Invariant;
    private int _textCacheCount;
    private bool _disposed;

    internal UiTextLayoutCache(ITextService textService, IUiFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(textService);
        ArgumentNullException.ThrowIfNull(fontResolver);
        _textService = textService;
        _fontResolver = fontResolver;
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
        Array.Clear(_richTextCache);
        _textCacheCount = 0;
    }

    internal bool TryMeasureText(
        Retained.UiRuntimeTypeIndex runtimeType,
        RetainedElement element,
        out Retained.UiTextMeasureMetrics metrics)
    {
        metrics = default;
        if (element is Retained.RichTextBlock richText)
        {
            var spans = richText.Spans.Span;
            var direction = ResolveParagraphDirection(spans, _localization.Direction);
            if (!TryGetRichTextLayout(richText, spans, direction, out var layout, out _))
            {
                return false;
            }

            var width = 0f;
            var height = 0f;
            for (var i = 0; i < spans.Length; i++)
            {
                width += Advance(layout.Shaped[i], layout.Bounds[i]);
                height = Maths.Max(height, layout.LineHeights[i]);
            }

            var scale = EffectiveScale(richText.LayoutScale);
            metrics = new(width / scale, height / scale, true);
            return true;
        }

        if (!Retained.UiDescriptorCatalog.TryGetTextRun(
                runtimeType,
                element,
                new(element.Id, element.Generation, element.LayoutScale, element.TextRunVersion),
                out var run) || !TryGetTextCache(run, out var plainCache, out _))
        {
            return false;
        }

        var layoutScale = run.LayoutScale > 0 ? run.LayoutScale : 1;
        metrics = new(
            Advance(plainCache.Shaped, plainCache.Bounds) / layoutScale,
            plainCache.LineHeight / layoutScale,
            true);
        return true;
    }

    internal bool TryBuildTextDraw(Retained.UiTextRun run, out UiTextDraw draw, out Diagnostic? diagnostic)
    {
        draw = default;
        if (!TryGetTextCache(run, out var cache, out diagnostic))
        {
            return false;
        }

        var scale = EffectiveScale(run.LayoutScale);
        var textLayoutBounds = run.TextBounds.Width > 0 || run.TextBounds.Height > 0 ? run.TextBounds : run.Bounds;
        var advanceWidth = AdvanceWidth(cache.Shaped) / scale;
        var naturalLineHeight = cache.LineHeight / scale;
        var lineHeight = run.LineHeight > 0 ? run.LineHeight / scale : naturalLineHeight;
        var leading = Maths.Max(0, lineHeight - naturalLineHeight) * 0.5f;
        var horizontalOffset = (textLayoutBounds.Width - advanceWidth) * (run.HorizontalAlignment switch
        {
            UiTextHorizontalAlignment.Center => 0.5f,
            UiTextHorizontalAlignment.Right => 1f,
            _ => 0f,
        });
        var verticalOffset = (textLayoutBounds.Height - lineHeight) * (run.VerticalAlignment switch
        {
            UiTextVerticalAlignment.Center => 0.5f,
            UiTextVerticalAlignment.Bottom => 1f,
            _ => 0f,
        });
        var baseline = new float2(
            textLayoutBounds.X + horizontalOffset,
            textLayoutBounds.Y + verticalOffset + cache.Ascent / scale + leading);
        var clip = run.ClipId.Value == 0 ? UiClipId.None : new UiClipId(checked((int)run.ClipId.Value - 1));
        draw = UiTextDraw.WithPaint(
            cache.Shaped,
            baseline,
            new UiTextPaint(ToColor(run.Color), ToColor(run.OutlineColor), run.OutlineWidth,
                new UiResourceId(run.TextEffectResource))
            {
                EffectSet = run.EffectSet,
            },
            clip);
        return true;
    }

    internal bool TryGetRichTextLayout(
        Retained.RichTextBlock owner,
        ReadOnlySpan<UiTextSpan> spans,
        TextDirection direction,
        [NotNullWhen(true)] out UiRichTextLayout? layout,
        out Diagnostic? diagnostic)
    {
        var ownerIndex = checked((int)owner.Id.Value);
        EnsureOwnerCapacity(ownerIndex);
        layout = _richTextCache[ownerIndex];
        if (layout is not null && layout.MatchesShaping(owner, spans, _localization, direction))
        {
            diagnostic = null;
            return true;
        }

        var shaped = new ShapedText[spans.Length];
        var origins = new float2[spans.Length];
        var bounds = new TextBounds[spans.Length];
        var lineHeights = new float[spans.Length];
        for (var i = 0; i < spans.Length; i++)
        {
            var span = spans[i];
            if (!TryResolveFontKey(span.FontKey, out var font, out diagnostic))
            {
                layout = null;
                return false;
            }

            var pixelsPerEm = span.FontSize * owner.LayoutScale;
            if (!TryShape(
                    span.Text.AsMemory(),
                    pixelsPerEm,
                    font,
                    direction,
                    "XAML_RICH_TEXT_SHAPE_FAILED",
                    "XAML_RICH_TEXT_SHAPING_UNSUPPORTED",
                    out var shapedSpan,
                    out var fontMetrics,
                    out diagnostic))
            {
                layout = null;
                return false;
            }

            shaped[i] = shapedSpan;
            bounds[i] = ShapedBounds(shapedSpan);
            lineHeights[i] = fontMetrics.Ascent + fontMetrics.Descent + fontMetrics.LineGap;
        }

        layout = new(owner, spans, _localization, direction, shaped, origins, bounds, lineHeights);
        _richTextCache[ownerIndex] = layout;
        diagnostic = null;
        return true;
    }

    internal bool TryGetRichTextLayout(
        Retained.RichTextBlock owner,
        [NotNullWhen(true)] out UiRichTextLayout? layout,
        out Diagnostic? diagnostic)
    {
        var spans = owner.Spans.Span;
        var direction = ResolveParagraphDirection(spans, _localization.Direction);
        return TryGetRichTextLayout(owner, spans, direction, out layout, out diagnostic);
    }

    internal void Prune(Retained.UiRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        for (var index = 1; index < _textCache.Length; index++)
        {
            var cache = _textCache[index];
            var richCache = _richTextCache[index];
            if (cache is null && richCache is null)
            {
                continue;
            }

            var generation = cache?.Generation ?? richCache?.Generation ?? 0;
            if (!runtime.TryGetNode(new Retained.UiNodeId((uint)index, generation), out _))
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
        _richTextCache = Array.Empty<UiRichTextLayout?>();
        _textCacheCount = 0;
    }

    private bool TryGetTextCache(
        Retained.UiTextRun run,
        [NotNullWhen(true)] out UiTextCacheEntry? cache,
        out Diagnostic? diagnostic)
    {
        cache = null;
        if (!TryResolveFont(run, out var font, out diagnostic))
        {
            return false;
        }

        var ownerIndex = checked((int)run.Owner.Value);
        EnsureOwnerCapacity(ownerIndex);
        cache = _textCache[ownerIndex];
        if (cache is null || !cache.Matches(run))
        {
            if (!TryShape(
                    run.Text.AsMemory(),
                    run.FontSize,
                    font,
                    _localization.Direction,
                    "XAML_TEXT_SHAPE_FAILED",
                    "XAML_TEXT_SHAPING_UNSUPPORTED",
                    out var shaped,
                    out var metrics,
                    out diagnostic))
            {
                return false;
            }

            cache = new UiTextCacheEntry(run, shaped, metrics);
            if (_textCache[ownerIndex] is null)
            {
                _textCacheCount++;
            }

            _textCache[ownerIndex] = cache;
        }

        diagnostic = null;
        return true;
    }

    private bool TryShape(
        ReadOnlyMemory<char> text,
        float pixelsPerEm,
        FontInstanceId font,
        TextDirection direction,
        string shapeFailureCode,
        string unsupportedCode,
        [NotNullWhen(true)] out ShapedText? shaped,
        out FontMetrics metrics,
        out Diagnostic? diagnostic)
    {
        try
        {
            _singleFontFallback[0] = font;
            shaped = _textService.Shape(new TextShapeRequest(
                text,
                pixelsPerEm,
                _singleFontFallback,
                direction,
                Language: _localization.Language));
            metrics = _textService.GetFontMetrics(font, pixelsPerEm);
            diagnostic = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            shaped = null;
            metrics = default;
            diagnostic = TextDiagnostic(shapeFailureCode, exception.Message);
            return false;
        }
        catch (InvalidOperationException exception)
        {
            shaped = null;
            metrics = default;
            diagnostic = TextDiagnostic(shapeFailureCode, exception.Message);
            return false;
        }
        catch (NotSupportedException exception)
        {
            shaped = null;
            metrics = default;
            diagnostic = TextDiagnostic(unsupportedCode, exception.Message);
            return false;
        }
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

        if (!TryResolveFontKey(run.FontKey, out font, out diagnostic))
        {
            return false;
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

        var length = Maths.Max(ownerIndex + 1, Maths.Max(8, _fontSlots.Length * 2));
        Array.Resize(ref _fontSlots, length);
        Array.Resize(ref _textCache, length);
        Array.Resize(ref _richTextCache, length);
    }

    internal static TextBounds ShapedBounds(ShapedText shaped)
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
        var penX = runs[0].AdvanceX;
        var penY = runs[0].AdvanceY;
        for (var i = 1; i < runs.Length; i++)
        {
            var bounds = runs[i].Bounds;
            left = Maths.Min(left, penX + bounds.Left);
            top = Maths.Min(top, penY + bounds.Top);
            right = Maths.Max(right, penX + bounds.Right);
            bottom = Maths.Max(bottom, penY + bounds.Bottom);
            penX += runs[i].AdvanceX;
            penY += runs[i].AdvanceY;
        }

        return new TextBounds(left, top, right, bottom);
    }

    internal static float AdvanceWidth(ShapedText shaped)
    {
        var runs = shaped.Runs.Span;
        var advance = 0f;
        for (var i = 0; i < runs.Length; i++)
        {
            advance += Maths.Abs(runs[i].AdvanceX);
        }

        return advance;
    }

    internal static float Advance(ShapedText shaped, TextBounds bounds)
    {
        return Maths.Max(bounds.Width, AdvanceWidth(shaped));
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
                ascent = Maths.Max(ascent, Maths.Max(0, -bounds[run].Top));
                descent = Maths.Max(descent, Maths.Max(0, bounds[run].Bottom));
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

            lineTop += Maths.Max(1, ascent + descent);
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

    private static float EffectiveScale(float scale) => scale > 0 ? scale : 1;

    private static float4 ToColor(Retained.UiColor value) => new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
    private static float4 ToColor(UiColor value) => new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);

    private readonly record struct UiFontSlot(uint Generation, string FontKey, FontInstanceId Font)
    {
        internal bool IsValid => Generation != 0 && Font.IsValid;
    }

    private sealed class UiTextCacheEntry
    {
        internal UiTextCacheEntry(Retained.UiTextRun run, ShapedText shaped, FontMetrics metrics)
        {
            Generation = run.OwnerGeneration;
            GlyphRunKey = run.GlyphRunKey;
            FontKey = run.FontKey;
            Text = run.Text;
            FontSize = run.FontSize;
            Weight = run.Weight;
            Style = run.Style;
            Shaped = shaped;
            Bounds = ShapedBounds(shaped);
            Ascent = metrics.Ascent;
            LineHeight = metrics.Ascent + metrics.Descent + metrics.LineGap;
        }

        internal uint Generation { get; }
        internal string GlyphRunKey { get; }
        internal string FontKey { get; }
        internal string Text { get; }
        internal float FontSize { get; }
        internal Delta.XAML.UiFontWeight Weight { get; }
        internal Delta.XAML.UiFontStyle Style { get; }
        internal ShapedText Shaped { get; }
        internal TextBounds Bounds { get; }
        internal float Ascent { get; }
        internal float LineHeight { get; }

        internal bool Matches(Retained.UiTextRun run) =>
            Generation == run.OwnerGeneration &&
            string.Equals(GlyphRunKey, run.GlyphRunKey, StringComparison.Ordinal) &&
            string.Equals(FontKey, run.FontKey, StringComparison.Ordinal) &&
            string.Equals(Text, run.Text, StringComparison.Ordinal) &&
            FontSize.Equals(run.FontSize) &&
            Weight == run.Weight &&
            Style == run.Style;
    }

    internal sealed class UiRichTextLayout
    {
        private readonly uint _generation;
        private readonly float _scale;
        private readonly TextDirection _direction;
        private readonly string? _language;
        private readonly string[] _text;
        private readonly string[] _fontKeys;
        private readonly float[] _fontSizes;
        private float _layoutWidth = float.NaN;

        internal UiRichTextLayout(
            Retained.RichTextBlock owner,
            ReadOnlySpan<UiTextSpan> spans,
            UiLocalizationContext localization,
            TextDirection direction,
            ShapedText[] shaped,
            float2[] origins,
            TextBounds[] bounds,
            float[] lineHeights)
        {
            _generation = owner.Generation;
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
            LineHeights = lineHeights;
            HitRanges = Array.Empty<UiInlineHitRange>();
        }

        internal ShapedText[] Shaped { get; }
        internal float2[] Origins { get; }
        internal TextBounds[] Bounds { get; }
        internal float[] LineHeights { get; }
        internal UiInlineHitRange[] HitRanges { get; set; }
        internal uint Generation => _generation;

        internal void EnsureLayout(float width)
        {
            if (_layoutWidth.Equals(width))
            {
                return;
            }

            LayoutRichRuns(Shaped, Bounds, Origins, width, _direction);
            _layoutWidth = width;
        }

        internal bool MatchesShaping(
            Retained.RichTextBlock owner,
            ReadOnlySpan<UiTextSpan> spans,
            UiLocalizationContext localization,
            TextDirection direction)
        {
            if (_generation != owner.Generation || !_scale.Equals(owner.LayoutScale) ||
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
}
