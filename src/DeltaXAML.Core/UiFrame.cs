using DeltaXAML.Abstractions;

namespace DeltaXAML.Core;

public sealed class UiFrame : IUiFrame
{
    private readonly DrawList _drawList = new();
    private readonly UiInputRouter _input;
    private readonly List<UiMutation> _mutations = new();
    public UiFrame(IUiElement root) { ArgumentNullException.ThrowIfNull(root); Root = root; _input = new(this); }
    public IUiElement Root { get; }
    public IUiInputRouter Input => _input;
    public int AppliedMutationCount { get; private set; }
    public int RejectedMutationCount { get; private set; }
    public int PendingMutationCount => _mutations.Count;
    public void Enqueue(in UiMutation mutation) => _mutations.Add(mutation);
    public void ApplyMutations()
    {
        AppliedMutationCount = 0; RejectedMutationCount = 0;
        for (var i = 0; i < _mutations.Count; i++)
        {
            var mutation = _mutations[i];
            if (Find(Root, mutation.Target.Element) is UiElement element && element.TrySet(mutation.Target, mutation.Value, mutation.Invalidation, out _))
            {
                AppliedMutationCount++;
            }
            else
            {
                RejectedMutationCount++;
            }
        }
        _mutations.Clear();
    }
    public void Layout(UiSize viewport, float dpiScale) { if (Root is UiElement element) { element.SetLayoutScale(dpiScale); } var scaled = new UiSize(viewport.Width * dpiScale, viewport.Height * dpiScale); Root.Measure(scaled); Root.Arrange(new(0, 0, viewport.Width, viewport.Height)); }
    public IUiDrawList ExtractDrawList(in UiFrameContext context) { _drawList.Build(Root, new(0, 0, context.Viewport.Width, context.Viewport.Height)); return _drawList; }
    // Temporary O(n) lookup: correct for the current small retained trees. Replace with a
    // frame-local neutral index after profiling shows mutation volume warrants its maintenance cost.
    private static UiElement? Find(IUiElement root, UiElementId id) { if (root.Id == id) { return root as UiElement; } foreach (var child in root.Children) { if (Find(child, id) is { } found) { return found; } } return null; }
}

internal sealed class DrawList : IUiDrawList
{
    private UiDrawCommand[] _commands = Array.Empty<UiDrawCommand>();
    private UiClipEntry[] _clips = Array.Empty<UiClipEntry>();
    private UiTextRun[] _textRuns = Array.Empty<UiTextRun>();
    private UiDrawCommand[] _previousCommands = Array.Empty<UiDrawCommand>();
    private UiClipEntry[] _previousClips = Array.Empty<UiClipEntry>();
    private UiTextRun[] _previousTextRuns = Array.Empty<UiTextRun>();
    private int _commandCount, _clipCount, _textCount;
    private int _previousCommandCount, _previousClipCount, _previousTextCount;
    private uint _previousVersion;
    public ReadOnlyMemory<UiDrawCommand> Commands => _commands.AsMemory(0, _commandCount);
    public ReadOnlyMemory<UiClipEntry> Clips => _clips.AsMemory(0, _clipCount);
    public ReadOnlyMemory<UiTextRun> TextRuns => _textRuns.AsMemory(0, _textCount);
    public uint Version { get; private set; }
    public UiDrawDelta GetDeltaSince(uint version)
    {
        if (version == Version)
        {
            return EmptyDelta(version);
        }

        if (version != _previousVersion)
        {
            return new(new(0, _commandCount), new(0, _textCount), version, Version) { Clips = new(0, _clipCount) };
        }

        return new(ChangedRange<UiDrawCommand>(_previousCommands.AsSpan(0, _previousCommandCount), _commands.AsSpan(0, _commandCount)), ChangedRange<UiTextRun>(_previousTextRuns.AsSpan(0, _previousTextCount), _textRuns.AsSpan(0, _textCount)), version, Version)
        {
            Clips = ChangedRange<UiClipEntry>(_previousClips.AsSpan(0, _previousClipCount), _clips.AsSpan(0, _clipCount))
        };
    }
    public void Build(IUiElement root, UiRect clip)
    {
        CopyCurrentToPrevious();
        _commandCount = 0;
        _clipCount = 0;
        _textCount = 0;
        Visit(root, clip, new(0));
        _previousVersion = Version;
        if (!Same<UiDrawCommand>(_previousCommands.AsSpan(0, _previousCommandCount), _commands.AsSpan(0, _commandCount)) ||
            !Same<UiClipEntry>(_previousClips.AsSpan(0, _previousClipCount), _clips.AsSpan(0, _clipCount)) ||
            !Same<UiTextRun>(_previousTextRuns.AsSpan(0, _previousTextCount), _textRuns.AsSpan(0, _textCount)))
        {
            Version++;
        }
    }
    private void Visit(IUiElement e, UiRect clip, UiClipId parent)
    {
        if (e.Visibility != UiVisibility.Visible)
        {
            return;
        }

        var effective = UiRect.Intersect(clip, e.Bounds);
        var id = new UiClipId((uint)_clipCount + 1);
        EnsureClip();
        _clips[_clipCount++] = new(id, effective, parent);
        if (e.Background.A > 0)
        {
            EnsureCommand();
            _commands[_commandCount] = new(UiDrawKind.Rectangle, e.Bounds, effective, id, default, e.Background, null, 0, (uint)_commandCount, e.Id);
            _commandCount++;
        }
        if (e is UiElement element && element.TryGetTextRun(out var run))
        {
            EnsureText();
            _textRuns[_textCount++] = run with { Bounds = e.Bounds, Clip = effective, Owner = e.Id, OwnerGeneration = e.Generation };
        }
        foreach (var child in e.Children)
        {
            Visit(child, effective, id);
        }
    }
    private void EnsureCommand() { if (_commandCount < _commands.Length) { return; } Array.Resize(ref _commands, Math.Max(8, _commands.Length * 2)); }
    private void EnsureClip() { if (_clipCount < _clips.Length) { return; } Array.Resize(ref _clips, Math.Max(8, _clips.Length * 2)); }
    private void EnsureText() { if (_textCount < _textRuns.Length) { return; } Array.Resize(ref _textRuns, Math.Max(8, _textRuns.Length * 2)); }
    private void CopyCurrentToPrevious()
    {
        EnsurePreviousCommands(_commandCount);
        EnsurePreviousClips(_clipCount);
        EnsurePreviousText(_textCount);
        _previousCommandCount = _commandCount;
        _previousClipCount = _clipCount;
        _previousTextCount = _textCount;
        for (var i = 0; i < _commandCount; i++)
        {
            _previousCommands[i] = _commands[i];
        }

        for (var i = 0; i < _clipCount; i++)
        {
            _previousClips[i] = _clips[i];
        }

        for (var i = 0; i < _textCount; i++)
        {
            _previousTextRuns[i] = _textRuns[i];
        }
    }
    private void EnsurePreviousClips(int count)
    {
        if (_previousClips.Length < count)
        {
            Array.Resize(ref _previousClips, Math.Max(8, Math.Max(count, _previousClips.Length * 2)));
        }
    }
    private void EnsurePreviousCommands(int count)
    {
        if (_previousCommands.Length < count)
        {
            Array.Resize(ref _previousCommands, Math.Max(8, Math.Max(count, _previousCommands.Length * 2)));
        }
    }
    private void EnsurePreviousText(int count)
    {
        if (_previousTextRuns.Length < count)
        {
            Array.Resize(ref _previousTextRuns, Math.Max(8, Math.Max(count, _previousTextRuns.Length * 2)));
        }
    }
    private static UiDrawDelta EmptyDelta(uint version) => new(new(0, 0), new(0, 0), version, version) { Clips = new(0, 0) };
    private static bool Same<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right) => left.SequenceEqual(right);
    private static UiDrawRange ChangedRange<T>(ReadOnlySpan<T> previous, ReadOnlySpan<T> current)
    {
        var start = -1;
        var end = -1;
        var limit = Math.Max(previous.Length, current.Length);
        for (var i = 0; i < limit; i++)
        {
            var prev = i < previous.Length ? previous[i] : default;
            var next = i < current.Length ? current[i] : default;
            if (!EqualityComparer<T>.Default.Equals(prev, next))
            {
                if (start < 0)
                {
                    start = i;
                }

                end = i + 1;
            }
        }
        return start < 0 ? new UiDrawRange(0, 0) : new UiDrawRange(start, end - start);
    }
}

public sealed class UiInputRouter : IUiInputRouter, IUiInputDispatcher
{
    private readonly UiFrame _frame;
    private UiElement? _focused, _captured, _hovered;
    public UiInputRouter(UiFrame frame) { ArgumentNullException.ThrowIfNull(frame); _frame = frame; }
    public UiElementId? Focused => _focused?.Id;
    public UiElementId? Captured => _captured?.Id;
    public void Dispatch(in UiInputPacket packet)
    {
        switch (packet.Kind)
        {
            case UiInputPacketKind.Spatial: { var pointer = packet.Spatial; RoutePointer(in pointer); break; }
            case UiInputPacketKind.Key: { var key = packet.Key; RouteKey(in key); break; }
            case UiInputPacketKind.Text: { var text = packet.Text; RouteText(in text); break; }
            case UiInputPacketKind.Ime: { var ime = packet.Ime; RouteIme(in ime); break; }
        }
    }
    public void Focus(UiElementId? element) => _focused = element is null ? null : Find(_frame.Root, element.Value);
    public void RoutePointer(in UiPointerEvent input)
    {
        PruneDetachedState();
        var target = _captured ?? FindHit(_frame.Root, input.Position);
        if (input.Kind == UiPointerEventKind.Move)
        {
            _hovered = target;
        }

        if (input.Kind == UiPointerEventKind.Down) { _captured = target; _focused = target; }
        if (target is not null)
        {
            Raise(target, new UiRoutedEvent(target.Id, UiRoutedEventPhase.Preview, input.Kind, input.Position));
            Raise(target, new UiRoutedEvent(target.Id, UiRoutedEventPhase.Bubble, input.Kind, input.Position));
        }
        if (input.Kind == UiPointerEventKind.Up)
        {
            _captured = null;
        }
    }
    public void RouteKey(in UiKeyEvent input)
    {
        PruneDetachedState();
        if (!input.IsDown)
        {
            return;
        }

        if (input.PhysicalKey == 9) { FocusNext(); return; }
        if (_focused is TextBox text)
        {
            text.ApplyKey(input);
        }
    }
    public void RouteText(in UiTextInput input)
    {
        PruneDetachedState(); if (_focused is TextBox text)
        {
            text.ApplyText(input);
        }
    }
    public void RouteIme(in UiImeComposition input)
    {
        PruneDetachedState(); if (input.IsCommitted)
        {
            RouteText(new UiTextInput(input.Text));
        }
    }
    private void PruneDetachedState()
    {
        if (_focused is not null && Find(_frame.Root, _focused.Id) is null)
        {
            _focused = null;
        }

        if (_captured is not null && Find(_frame.Root, _captured.Id) is null)
        {
            _captured = null;
        }

        if (_hovered is not null && Find(_frame.Root, _hovered.Id) is null)
        {
            _hovered = null;
        }
    }
    private void FocusNext()
    {
        var focusable = new List<UiElement>();
        CollectFocusable(_frame.Root, focusable);
        var index = _focused is null ? -1 : focusable.IndexOf(_focused);
        _focused = focusable.Count == 0 ? null : focusable[(index + 1) % focusable.Count];
    }
    private static void CollectFocusable(IUiElement element, List<UiElement> result)
    {
        if (element is UiElement e && e.Focusable)
        {
            result.Add(e);
        }

        foreach (var child in element.Children)
        {
            CollectFocusable(child, result);
        }
    }
    private static void Raise(UiElement target, in UiRoutedEvent e)
    {
        var path = new List<UiElement>();
        for (UiElement? node = target; node is not null; node = node.Parent as UiElement)
        {
            path.Add(node);
        }

        if (e.Phase == UiRoutedEventPhase.Preview)
        {
            for (var i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] is IUiRoutedEventSink preview)
                {
                    preview.OnRoutedEvent(e);
                }
            }
        }
        else
        {
            foreach (var node in path)
            {
                if (node is IUiRoutedEventSink bubble)
                {
                    bubble.OnRoutedEvent(e);
                }
            }
        }
    }
    private static UiElement? FindHit(IUiElement root, UiPoint p) => root is UiElement e ? e.HitTest(p) as UiElement : null;
    private static UiElement? Find(IUiElement root, UiElementId id) { if (root.Id == id) { return root as UiElement; } foreach (var child in root.Children) { if (Find(child, id) is { } found) { return found; } } return null; }
}
