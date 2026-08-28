namespace DeltaXAML.Internal;

internal sealed class UiFrame
{
    private readonly DrawList _drawList = new();
    private readonly UiRuntime _runtime;

    public UiFrame(IUiElement root)
    {
        _runtime = new(root);
    }
    public IUiElement Root => _runtime.Root;
    public IUiInputRouter Input => _runtime.Input;
    public int AppliedMutationCount => _runtime.AppliedMutationCount;
    public int RejectedMutationCount => _runtime.RejectedMutationCount;
    public int PendingMutationCount => _runtime.PendingMutationCount;
    internal int PendingInputCount => _runtime.PendingInputCount;
    public void Enqueue(in UiMutation mutation) => _runtime.Enqueue(in mutation);
    internal void EnqueueInput(in UiInputPacket packet) => _runtime.EnqueueInput(in packet);
    public void ApplyMutations() => _runtime.ApplyMutations();
    public void Layout(UiSize viewport, float dpiScale) => _runtime.Layout(viewport, dpiScale);
    public DrawList ExtractDrawList(in UiFrameContext context) { _drawList.Build(Root, new(0, 0, context.Viewport.Width, context.Viewport.Height)); return _drawList; }
    internal bool TryResolve(UiPropertyHandle handle, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out UiElement? element) => _runtime.TryResolve(handle, out element);
    internal bool TryResolve(UiElementId id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out UiElement? element) => _runtime.TryResolve(id, out element);
    internal bool TryGetNode(UiNodeId id, out UiNodeRecord record) => _runtime.TryGetNode(id, out record);
    internal bool Contains(UiElement element) => _runtime.Contains(element);
}

internal sealed class DrawList
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
        if (e.Visibility != UiVisibility.Visible || (e.Participation & Delta.XAML.UiParticipation.Layout) == 0)
        {
            return;
        }

        var effective = UiRect.Intersect(clip, e.Bounds);
        var id = new UiClipId((uint)_clipCount + 1);
        EnsureClip();
        _clips[_clipCount++] = new(id, effective, parent);
        if ((e.Participation & Delta.XAML.UiParticipation.Rendering) != 0 && e.Background.A > 0)
        {
            EnsureCommand();
            _commands[_commandCount] = new(UiDrawKind.Rectangle, e.Bounds, effective, id, default, e.Background, null, 0, (uint)_commandCount, e.Id);
            _commandCount++;
        }
        if ((e.Participation & Delta.XAML.UiParticipation.Rendering) != 0 && e is UiElement element && element.TryGetTextRun(out var run))
        {
            EnsureText();
            _textRuns[_textCount++] = run with { Bounds = e.Bounds, Clip = effective, Owner = e.Id, OwnerGeneration = e.Generation, ClipId = id };
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

internal sealed class UiInputRouter : IUiInputRouter, IUiInputDispatcher
{
    private readonly UiRuntime _runtime;
    private readonly List<UiElement> _focusable = new();
    private readonly List<UiElement> _routePath = new();
    private UiElement? _focused, _captured, _hovered;
    public UiInputRouter(UiRuntime runtime) { ArgumentNullException.ThrowIfNull(runtime); _runtime = runtime; }
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
    public void Focus(UiElementId? element) => _focused = element is { } id && _runtime.TryResolve(id, out var resolved) ? resolved : null;
    public void RoutePointer(in UiPointerEvent input)
    {
        PruneDetachedState();
        var target = _captured ?? _runtime.FindHit(input.Position);
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
        if (_focused is not null && !_runtime.Contains(_focused))
        {
            _focused = null;
        }

        if (_captured is not null && !_runtime.Contains(_captured))
        {
            _captured = null;
        }

        if (_hovered is not null && !_runtime.Contains(_hovered))
        {
            _hovered = null;
        }
    }
    private void FocusNext()
    {
        _focusable.Clear();
        _runtime.CollectFocusable(_focusable);
        var index = _focused is null ? -1 : _focusable.IndexOf(_focused);
        _focused = _focusable.Count == 0 ? null : _focusable[(index + 1) % _focusable.Count];
    }
    private void Raise(UiElement target, in UiRoutedEvent e)
    {
        _routePath.Clear();
        for (UiElement? node = target; node is not null; node = node.Parent as UiElement)
        {
            _routePath.Add(node);
        }

        if (e.Phase == UiRoutedEventPhase.Preview)
        {
            for (var i = _routePath.Count - 1; i >= 0; i--)
            {
                if (_routePath[i] is IUiRoutedEventSink preview)
                {
                    preview.OnRoutedEvent(e);
                }
            }
        }
        else
        {
            for (var i = 0; i < _routePath.Count; i++)
            {
                var node = _routePath[i];
                if (node is IUiRoutedEventSink bubble)
                {
                    bubble.OnRoutedEvent(e);
                }
            }
        }
    }
}
