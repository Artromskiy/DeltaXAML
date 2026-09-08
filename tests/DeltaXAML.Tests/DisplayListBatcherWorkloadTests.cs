using Delta;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;

internal enum UiDisplayListWorkloadFrame : byte
{
    Base = 0,
    Paint = 1,
    Clip = 2,
    Reorder = 3,
    Churn = 4,
}

internal readonly record struct UiDisplayListWorkloadExpectation(
    int VisualCount,
    int TextCount,
    int ClipCount,
    int OrderCount,
    int AddedEntries,
    int RemovedEntries,
    int ReorderedEntries,
    int PaintChanges,
    int ClipChanges,
    ulong OrderChecksum,
    ulong PayloadChecksum,
    ulong FullChecksum);

/// <summary>
/// Deterministic producer workload for validating a renderer's contiguous UI batcher.
/// </summary>
/// <remarks>
/// The fixture owns reusable payload arrays and exposes a borrowed <see cref="UiDisplayList"/>.
/// <see cref="UiDisplayList.Order"/> is the expected mixed command stream: each reference selects
/// one item from the visual or text payload span. It deliberately does not reference Render,
/// Vulkan, a GPU resource, or a shaping implementation.
/// </remarks>
internal sealed class UiDisplayListBatcherWorkload
{
    internal const int BaseEntryCount = 5_000;
    internal const int ChurnEntryCount = 5_300;
    internal const int ClipCount = 256;

    private const ulong FnvOffset = 14_695_981_039_346_656_037UL;
    private const ulong FnvPrime = 1_099_511_628_211UL;

    private readonly UiVisualDraw[] _visuals = new UiVisualDraw[ChurnEntryCount];
    private readonly UiTextDraw[] _text = new UiTextDraw[ChurnEntryCount];
    private readonly UiClipRegion[] _clips = new UiClipRegion[ClipCount];
    private readonly UiDrawRef[] _order = new UiDrawRef[ChurnEntryCount];
    private readonly UiElementIdentity[] _identities = new UiElementIdentity[ChurnEntryCount];

    internal UiDisplayList Build(UiDisplayListWorkloadFrame frame, ShapedText shapedText)
    {
        ArgumentNullException.ThrowIfNull(shapedText);
        BuildClips(frame);

        var visualCount = 0;
        var textCount = 0;
        var orderCount = 0;
        if (frame == UiDisplayListWorkloadFrame.Reorder)
        {
            for (var position = 0; position < BaseEntryCount; position++)
            {
                var entry = (position * 37) % BaseEntryCount;
                AppendEntry(frame, entry, shapedText, ref visualCount, ref textCount, ref orderCount);
            }
        }
        else
        {
            var entryLimit = frame == UiDisplayListWorkloadFrame.Churn ? ChurnEntryCount : BaseEntryCount;
            for (var entry = 0; entry < entryLimit; entry++)
            {
                if (frame == UiDisplayListWorkloadFrame.Churn && entry < BaseEntryCount && entry % 19 == 0)
                {
                    continue;
                }

                AppendEntry(frame, entry, shapedText, ref visualCount, ref textCount, ref orderCount);
            }
        }

        return new UiDisplayList(
            _visuals.AsSpan(0, visualCount),
            _clips,
            _text.AsSpan(0, textCount),
            _order.AsSpan(0, orderCount),
            _identities.AsSpan(0, orderCount));
    }

    internal static UiDisplayListWorkloadExpectation Expected(UiDisplayListWorkloadFrame frame) =>
        frame switch
        {
            UiDisplayListWorkloadFrame.Base => new(3_500, 1_500, ClipCount, BaseEntryCount, BaseEntryCount, 0, 0, 0, 0, 4_322_472_588_939_357_607UL, 1_661_072_465_338_250_153UL, 13_832_432_337_851_313_865UL),
            UiDisplayListWorkloadFrame.Paint => new(3_500, 1_500, ClipCount, BaseEntryCount, 0, 0, 0, 715, 0, 4_322_472_588_939_357_607UL, 10_105_320_343_452_927_734UL, 9_103_573_521_980_863_414UL),
            UiDisplayListWorkloadFrame.Clip => new(3_500, 1_500, ClipCount, BaseEntryCount, 0, 0, 0, 0, 52, 4_322_472_588_939_357_607UL, 11_027_676_578_378_265_513UL, 522_897_026_293_657_289UL),
            UiDisplayListWorkloadFrame.Reorder => new(3_500, 1_500, ClipCount, BaseEntryCount, 0, 0, BaseEntryCount - 1, 0, 0, 7_877_403_130_303_307_063UL, 11_250_173_101_799_841_837UL, 353_556_234_144_412_541UL),
            UiDisplayListWorkloadFrame.Churn => new(3_527, 1_509, ClipCount, 5_036, 300, 264, 0, 0, 0, 15_779_854_436_547_583_369UL, 4_898_413_177_917_673_832UL, 13_604_246_579_395_876_398UL),
            _ => throw new ArgumentOutOfRangeException(nameof(frame), frame, "Unknown workload frame."),
        };

    internal static ulong ComputeOrderChecksum(UiDisplayList displayList)
    {
        var hash = FnvOffset;
        Add(ref hash, (uint)displayList.Order.Length);
        for (var i = 0; i < displayList.Order.Length; i++)
        {
            var draw = displayList.Order[i];
            Add(ref hash, (uint)draw.Kind);
            Add(ref hash, (uint)draw.Index);
        }

        return hash;
    }

    internal static ulong ComputePayloadChecksum(UiDisplayList displayList)
    {
        var hash = FnvOffset;
        Add(ref hash, (uint)displayList.Visuals.Length);
        Add(ref hash, (uint)displayList.Clips.Length);
        Add(ref hash, (uint)displayList.Text.Length);

        for (var i = 0; i < displayList.Visuals.Length; i++)
        {
            var visual = displayList.Visuals[i];
            Add(ref hash, (uint)visual.Kind);
            AppendGuid(ref hash, visual.VisualType.Value);
            AppendFloat4(ref hash, visual.Bounds);
            AppendFloat4(ref hash, visual.Paint.FillColor);
            AppendFloat4(ref hash, visual.Paint.CornerRadii);
            AppendEffectSet(ref hash, visual.Paint.EffectSet);
            Add(ref hash, (uint)visual.Clip.Value);
            AppendGuid(ref hash, visual.Resource.Value);
        }

        for (var i = 0; i < displayList.Clips.Length; i++)
        {
            var clip = displayList.Clips[i];
            AppendFloat4(ref hash, clip.Bounds);
            Add(ref hash, (uint)clip.Parent.Value);
            Add(ref hash, (uint)clip.Kind);
            AppendFloat4(ref hash, clip.CornerRadii);
        }

        for (var i = 0; i < displayList.Text.Length; i++)
        {
            var text = displayList.Text[i];
            Add(ref hash, (uint)text.Text.TextLengthUtf16);
            Add(ref hash, (uint)text.Text.Runs.Length);
            Add(ref hash, BitConverter.SingleToUInt32Bits(text.BaselineOrigin.x));
            Add(ref hash, BitConverter.SingleToUInt32Bits(text.BaselineOrigin.y));
            AppendFloat4(ref hash, text.Paint.FillColor);
            AppendEffectSet(ref hash, text.Paint.EffectSet);
            Add(ref hash, (uint)text.Clip.Value);
        }

        return hash;
    }

    internal static ulong ComputeChecksum(UiDisplayList displayList)
    {
        var hash = ComputePayloadChecksum(displayList);
        for (var i = 0; i < displayList.Order.Length; i++)
        {
            var draw = displayList.Order[i];
            Add(ref hash, (uint)draw.Kind);
            Add(ref hash, (uint)draw.Index);
        }

        return hash;
    }

    private void BuildClips(UiDisplayListWorkloadFrame frame)
    {
        for (var i = 0; i < _clips.Length; i++)
        {
            var changed = frame == UiDisplayListWorkloadFrame.Clip && i % 5 == 0;
            var kind = i % 3 == 0 ? UiClipKind.RoundedRectangle : UiClipKind.Rectangle;
            var radius = kind == UiClipKind.RoundedRectangle
                ? new float4(8 + i % 4, 6 + i % 3, 10 + i % 5, 4 + i % 2)
                : default;
            if (changed)
            {
                radius += new float4(1, 2, 3, 1);
            }

            var bounds = new float4(0, 0, 8_192 - i % 4 * 8, 4_096 - i % 3 * 8);
            if (changed)
            {
                bounds.z -= 16;
                bounds.w -= 12;
            }

            _clips[i] = new(
                bounds,
                i == 0 ? UiClipId.None : new UiClipId((i - 1) / 2),
                kind,
                radius);
        }
    }

    private void AppendEntry(
        UiDisplayListWorkloadFrame frame,
        int entry,
        ShapedText shapedText,
        ref int visualCount,
        ref int textCount,
        ref int orderCount)
    {
        var clip = new UiClipId(entry % ClipCount);
        var paintChanged = frame == UiDisplayListWorkloadFrame.Paint && entry % 7 == 0;
        var variant = paintChanged ? 1 : 0;
        if (entry % 10 < 7)
        {
            var kind = (entry % 3) switch
            {
                0 => UiVisualKind.SolidRectangle,
                1 => UiVisualKind.RoundedRectangle,
                _ => UiVisualKind.Border,
            };
            var effectSet = kind == UiVisualKind.Border
                ? EffectSet(entry, variant, UiEffectTarget.Visual, 0)
                : UiEffectSet.None;
            var paint = new UiVisualPaint(
                Color(entry, variant),
                Radii(entry, variant),
                effectSet);
            _visuals[visualCount] = UiVisualDraw.WithPaint(
                kind,
                default,
                Bounds(entry),
                paint,
                clip,
                Resource(0x1000 + entry % 31 + variant * 97));
            var visualIndex = visualCount++;
            _order[orderCount] = new UiDrawRef(UiDrawKind.Visual, visualIndex);
            _identities[orderCount++] = new((uint)(entry + 1), entry < BaseEntryCount ? 1u : 2u, paintChanged ? 2u : 1u);
            return;
        }

        var strokeWidth = 0.5f + (entry % 3) * 0.25f + variant;
        var textPaint = new UiTextPaint(
            Color(entry + 11, variant),
            EffectSet(entry, variant, UiEffectTarget.Text, strokeWidth));
        var generation = entry < BaseEntryCount ? 1u : 2u;
        var version = paintChanged ? 2u : 1u;
        _text[textCount] = UiTextDraw.WithPaint(
            shapedText,
            new float2(entry % 100 * 8 + 2, entry / 100 * 6 + 18),
            textPaint,
            clip);
        var textIndex = textCount++;
        _order[orderCount] = new UiDrawRef(UiDrawKind.Text, textIndex);
        _identities[orderCount++] = new((uint)(entry + 1), generation, version);
    }

    private static float4 Bounds(int entry) =>
        new(entry % 100 * 8, entry / 100 * 6, 64 + entry % 4, 24 + entry % 3);

    private static UiEffectSet EffectSet(int entry, int variant, UiEffectTarget target, float outset)
    {
        var outsets = new float4(outset, outset, outset, outset);
        return new(
            Resource(0x3000 + entry % 17 + variant * 97),
            target,
            UiEffectCapabilities.Stroke,
            UiEffectQuality.Analytic,
            outsets);
    }

    private static float4 Color(int seed, int variant)
    {
        var red = (seed % 13 + 1 + variant) / 16f;
        var green = (seed % 17 + 2 + variant) / 20f;
        var blue = (seed % 19 + 3 + variant) / 22f;
        return new float4(red, green, blue, 1);
    }

    private static float4 Radii(int seed, int variant) =>
        new(seed % 5 + variant, seed % 7 + variant, seed % 11 + variant, seed % 13 + variant);

    private static UiResourceId Resource(int seed) =>
        new(new Guid(seed, 0x2048, unchecked((short)0xD00D), 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80));

    private static void Add(ref ulong hash, uint value)
    {
        hash ^= value;
        hash *= FnvPrime;
    }

    private static void AppendFloat4(ref ulong hash, float4 value)
    {
        Add(ref hash, BitConverter.SingleToUInt32Bits(value.x));
        Add(ref hash, BitConverter.SingleToUInt32Bits(value.y));
        Add(ref hash, BitConverter.SingleToUInt32Bits(value.z));
        Add(ref hash, BitConverter.SingleToUInt32Bits(value.w));
    }

    private static void AppendGuid(ref ulong hash, Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!value.TryWriteBytes(bytes))
        {
            throw new InvalidOperationException("A Guid must fit into its fixed 16-byte checksum representation.");
        }

        for (var i = 0; i < bytes.Length; i++)
        {
            Add(ref hash, bytes[i]);
        }
    }

    private static void AppendEffectSet(ref ulong hash, UiEffectSet effectSet)
    {
        AppendGuid(ref hash, effectSet.Resource.Value);
        Add(ref hash, (uint)effectSet.Target);
        Add(ref hash, (uint)effectSet.Capabilities);
        Add(ref hash, (uint)effectSet.Quality);
        AppendFloat4(ref hash, effectSet.Outsets);
    }
}

internal static class DisplayListBatcherWorkloadTests
{
    internal static void Run()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
        Assert.True(File.Exists(fontPath), "batcher workload uses the existing deterministic text fixture font");
        var fonts = new UiFontCatalog();
        fonts.Register(
            "default",
            new FontSourceId(new Guid("37B12D6F-4D2B-47F0-97C1-6AA853D0A204")),
            File.ReadAllBytes(fontPath));
        Assert.True(fonts.TryResolve("default", out var fontRequest), "batcher workload resolves its fixture font");
        using var textService = new CountingTextService();
        var font = textService.OpenFont(in fontRequest);
        var shaped = textService.Shape(new TextShapeRequest("x".AsMemory(), 16, new[] { font }, TextDirection.LeftToRight));
        var workload = new UiDisplayListBatcherWorkload();
        ulong baseOrderChecksum = 0;
        ulong basePayloadChecksum = 0;

        for (var frameValue = (byte)UiDisplayListWorkloadFrame.Base;
             frameValue <= (byte)UiDisplayListWorkloadFrame.Churn;
             frameValue++)
        {
            var frame = (UiDisplayListWorkloadFrame)frameValue;
            var displayList = workload.Build(frame, shaped);
            var expected = UiDisplayListBatcherWorkload.Expected(frame);
            var orderChecksum = UiDisplayListBatcherWorkload.ComputeOrderChecksum(displayList);
            var payloadChecksum = UiDisplayListBatcherWorkload.ComputePayloadChecksum(displayList);
            Assert.Equal(expected.VisualCount, displayList.Visuals.Length, $"{frame} visual count");
            Assert.Equal(expected.TextCount, displayList.Text.Length, $"{frame} text count");
            Assert.Equal(expected.ClipCount, displayList.Clips.Length, $"{frame} clip count");
            Assert.Equal(expected.OrderCount, displayList.Order.Length, $"{frame} canonical order count");
            Assert.Equal(displayList.Order.Length, displayList.Identities.Length, $"{frame} identity alignment");
            Assert.True(expected.AddedEntries >= 0 && expected.RemovedEntries >= 0, $"{frame} transition counts are non-negative");
            Assert.Equal(expected.OrderChecksum, orderChecksum, $"{frame} ordered command checksum");
            Assert.Equal(expected.PayloadChecksum, payloadChecksum, $"{frame} payload checksum");
            Assert.Equal(expected.FullChecksum, UiDisplayListBatcherWorkload.ComputeChecksum(displayList), $"{frame} full ordered display checksum");
            ValidateOrder(displayList, frame);

            if (frame == UiDisplayListWorkloadFrame.Base)
            {
                baseOrderChecksum = orderChecksum;
                basePayloadChecksum = payloadChecksum;
            }
            else if (frame == UiDisplayListWorkloadFrame.Paint)
            {
                var changedTextVersions = 0;
                for (var i = 0; i < displayList.Order.Length; i++)
                {
                    if (displayList.Order[i].Kind == UiDrawKind.Text && displayList.Identities[i].Version == 2)
                    {
                        changedTextVersions++;
                    }
                }

                Assert.Equal(215, changedTextVersions, "paint frame changes only the expected text identity versions");
                Assert.Equal(baseOrderChecksum, orderChecksum, "paint changes preserve the actual canonical command order");
                Assert.True(basePayloadChecksum != payloadChecksum, "paint changes modify the actual payload checksum");
            }
            else if (frame == UiDisplayListWorkloadFrame.Clip)
            {
                Assert.Equal(baseOrderChecksum, orderChecksum, "clip changes preserve the actual canonical command order");
                Assert.True(basePayloadChecksum != payloadChecksum, "clip changes modify the actual payload checksum");
            }
            else if (frame == UiDisplayListWorkloadFrame.Reorder)
            {
                Assert.True(basePayloadChecksum != payloadChecksum, "reorder changes repack the actual payload spans in the new order");
                Assert.True(baseOrderChecksum != orderChecksum, "reorder changes modify the actual canonical command order");
            }
        }

        for (var warmup = 0; warmup < 16; warmup++)
        {
            _ = workload.Build(UiDisplayListWorkloadFrame.Base, shaped);
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 128; frame++)
        {
            _ = workload.Build(UiDisplayListWorkloadFrame.Paint, shaped);
        }

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(0L, allocatedBytes, "warm workload rebuild does not replace payload arrays or allocate per frame");
    }

    private static void ValidateOrder(UiDisplayList displayList, UiDisplayListWorkloadFrame frame)
    {
        for (var i = 0; i < displayList.Order.Length; i++)
        {
            var draw = displayList.Order[i];
            var identity = displayList.Identities[i];
            Assert.True(draw.IsValid, $"{frame} order entry {i} is valid");
            Assert.True(identity.Value != 0 && identity.Generation != 0, $"{frame} identity entry {i} is generation-safe");
            if (draw.Kind == UiDrawKind.Visual)
            {
                Assert.True(draw.Index < displayList.Visuals.Length, $"{frame} visual order entry {i} is in range");
            }
            else
            {
                Assert.True(draw.Index < displayList.Text.Length, $"{frame} text order entry {i} is in range");
            }
        }
    }
}
