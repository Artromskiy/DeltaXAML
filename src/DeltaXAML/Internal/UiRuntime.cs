namespace DeltaXAML.Internal;

/// <summary>Owns the retained document stages used by the library path.</summary>
/// <remarks>
/// The runtime owns one retained root and one generation-safe node index. It
/// applies queued input and mutations before layout; display extraction is
/// owned by the public document and writes its borrowed storage directly.
/// </remarks>
internal sealed class UiRuntime
{
    private readonly List<UiMutation> _mutations = new();
    private readonly List<UiInputPacket> _inputQueue = new();
    private readonly UiElement _retainedRoot;
    private readonly UiNodeStore _nodes;
    private readonly UiInputRouter _input;
    private readonly List<UiTraversalEntry> _traversal = new();
    private readonly List<UiElement> _stageTraversal = new();
    private readonly List<UiMeasureRequest> _measureQueue = new();
    private readonly List<UiArrangeRequest> _arrangeQueue = new();

    public UiRuntime(IUiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _retainedRoot = root as UiElement ?? throw new ArgumentException("Root must be a DeltaXAML element.", nameof(root));
        Root = _retainedRoot;
        _nodes = new(_retainedRoot);
        _input = new(this);
        UiBindingStage.Run(_retainedRoot, _stageTraversal);
    }

    public IUiElement Root { get; }
    public IUiInputRouter Input => _input;
    public int AppliedMutationCount { get; private set; }
    public int RejectedMutationCount { get; private set; }
    public int PendingMutationCount => _mutations.Count;
    internal int PendingInputCount => _inputQueue.Count;

    public void Enqueue(in UiMutation mutation) => _mutations.Add(mutation);

    internal void EnqueueInput(in UiInputPacket packet) => _inputQueue.Add(packet);

    public void ApplyMutations()
    {
        UiMutationStage.Run(_nodes, _retainedRoot, _mutations, out var applied, out var rejected);
        AppliedMutationCount = applied;
        RejectedMutationCount = rejected;
    }

    public void Layout(UiSize viewport, float dpiScale)
    {
        Layout(viewport, dpiScale, null, null);
    }

    internal void Layout(
        UiSize viewport,
        float dpiScale,
        Delta.XAML.UiTheme? theme,
        Delta.XAML.UiElement? publicRoot)
    {
        UiInputStage.Run(_input, _inputQueue);
        UiMutationStage.Run(_nodes, _retainedRoot, _mutations, out var applied, out var rejected);
        AppliedMutationCount = applied;
        RejectedMutationCount = rejected;
        UiBindingStage.Run(_retainedRoot, _stageTraversal);
        if (theme is not null && publicRoot is not null)
        {
            UiStyleStage.Run(theme, publicRoot);
        }

        UiScaleStage.Run(_retainedRoot, dpiScale, _stageTraversal);
        var scaled = new UiSize(viewport.Width * dpiScale, viewport.Height * dpiScale);
        UiMeasureStage.Run(_retainedRoot, scaled, _measureQueue);
        UiArrangeStage.Run(_retainedRoot, new(0, 0, viewport.Width, viewport.Height), _arrangeQueue);
        UiFocusStage.Run(_input);
    }

    internal bool TryResolve(UiPropertyHandle handle, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out UiElement? element)
    {
        _nodes.EnsureCurrent(_retainedRoot);
        return _nodes.TryResolve(handle, out element);
    }

    internal bool TryResolve(UiElementId id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out UiElement? element)
    {
        _nodes.EnsureCurrent(_retainedRoot);
        return _nodes.TryResolve(id, out element);
    }

    internal bool TryGetNode(UiNodeId id, out UiNodeRecord record)
    {
        _nodes.EnsureCurrent(_retainedRoot);
        return _nodes.TryGetNode(id, out record);
    }

    internal bool Contains(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return TryResolve(element.Id, out var current) && current.Generation == element.Generation;
    }

    internal UiElement? FindHit(UiPoint point)
    {
        _traversal.Clear();
        _traversal.Add(new(_retainedRoot, false));
        while (_traversal.Count != 0)
        {
            var last = _traversal.Count - 1;
            var entry = _traversal[last];
            _traversal.RemoveAt(last);
            var element = entry.Element;
            if (entry.Exit)
            {
                if ((element.Participation & Delta.XAML.UiParticipation.HitTesting) != 0)
                {
                    return element;
                }

                continue;
            }

            if (element.Visibility != UiVisibility.Visible ||
                (element.Participation & Delta.XAML.UiParticipation.Layout) == 0 ||
                !element.Clip.Contains(point))
            {
                continue;
            }

            _traversal.Add(new(element, true));
            for (var i = 0; i < element.Children.Count; i++)
            {
                if (element.Children[i] is UiElement child)
                {
                    _traversal.Add(new(child, false));
                }
            }
        }

        return null;
    }

    internal void CollectFocusable(List<UiElement> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _traversal.Clear();
        _traversal.Add(new(_retainedRoot, false));
        while (_traversal.Count != 0)
        {
            var last = _traversal.Count - 1;
            var element = _traversal[last].Element;
            _traversal.RemoveAt(last);
            if (element.Focusable)
            {
                result.Add(element);
            }

            for (var i = element.Children.Count - 1; i >= 0; i--)
            {
                if (element.Children[i] is UiElement child)
                {
                    _traversal.Add(new(child, false));
                }
            }
        }
    }

    private readonly record struct UiTraversalEntry(UiElement Element, bool Exit);
}
