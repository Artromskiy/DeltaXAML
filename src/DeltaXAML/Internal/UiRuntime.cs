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
    private readonly List<UiNodeId> _childOrder = new();
    private readonly List<UiNodeId> _stageTraversal = new();
    private readonly List<UiMeasureRequest> _measureQueue = new();
    private readonly List<UiArrangeRequest> _arrangeQueue = new();
    private float _appliedScale = float.NaN;
    private uint _scaledTreeVersion;
    private uint _bindingTreeVersion;

    public UiRuntime(IUiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _retainedRoot = root as UiElement ?? throw new ArgumentException("Root must be a DeltaXAML element.", nameof(root));
        Root = _retainedRoot;
        _nodes = new(_retainedRoot);
        _input = new(this);
        UiBindingStage.Run(_nodes, _retainedRoot, _stageTraversal, _childOrder, force: true);
        _bindingTreeVersion = _retainedRoot.TreeVersion;
    }

    public IUiElement Root { get; }
    public IUiInputRouter Input => _input;
    public int AppliedMutationCount { get; private set; }
    public int RejectedMutationCount { get; private set; }
    public int PendingMutationCount => _mutations.Count;
    internal int PendingInputCount => _inputQueue.Count;
    internal int NodeCount => _nodes.Count;

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
        var bindingTreeChanged = _bindingTreeVersion != _retainedRoot.TreeVersion;
        UiBindingStage.Run(_nodes, _retainedRoot, _stageTraversal, _childOrder, bindingTreeChanged);
        _bindingTreeVersion = _retainedRoot.TreeVersion;
        if (theme is not null && publicRoot is not null)
        {
            UiStyleStage.Run(theme, publicRoot);
        }

        if (!_appliedScale.Equals(dpiScale) || _scaledTreeVersion != _retainedRoot.TreeVersion)
        {
            UiScaleStage.Run(_nodes, _retainedRoot, dpiScale, _stageTraversal, _childOrder);
            _appliedScale = dpiScale;
            _scaledTreeVersion = _retainedRoot.TreeVersion;
        }
        var scaled = new UiSize(viewport.Width * dpiScale, viewport.Height * dpiScale);
        UiMeasureStage.Run(_nodes, _retainedRoot, scaled, _measureQueue, _childOrder);
        UiArrangeStage.Run(_nodes, _retainedRoot, new(0, 0, viewport.Width, viewport.Height), _arrangeQueue);
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

    internal bool TryCopyVisualChildren(UiNodeId parent, List<UiNodeId> destination)
    {
        _nodes.EnsureCurrent(_retainedRoot);
        return _nodes.TryCopyVisualChildren(parent, destination);
    }

    internal void CollectRoute(UiElement target, List<UiElement> destination)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(destination);
        _nodes.EnsureCurrent(_retainedRoot);
        destination.Clear();
        var id = new UiNodeId(target.Id.Value, target.Generation);
        while (_nodes.TryGetNode(id, out var node) && node.Element is { } element)
        {
            destination.Add(element);
            if (!node.LogicalParent.IsValid)
            {
                return;
            }

            id = node.LogicalParent;
        }
    }

    internal bool Contains(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return TryResolve(element.Id, out var current) && current.Generation == element.Generation;
    }

    internal UiElement? FindHit(UiPoint point)
    {
        _nodes.EnsureCurrent(_retainedRoot);
        _traversal.Clear();
        var rootId = new UiNodeId(_retainedRoot.Id.Value, _retainedRoot.Generation);
        _traversal.Add(new(rootId, false));
        while (_traversal.Count != 0)
        {
            var last = _traversal.Count - 1;
            var entry = _traversal[last];
            _traversal.RemoveAt(last);
            if (!_nodes.TryGetNode(entry.Id, out var node) || node.Element is not { } element)
            {
                continue;
            }

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

            _traversal.Add(new(node.Id, true));
            if (_nodes.TryGetFirstVisualChild(node.Id, out var child))
            {
                while (true)
                {
                    _traversal.Add(new(child.Id, false));
                    if (!_nodes.TryGetNextVisualSibling(child, out var next))
                    {
                        break;
                    }

                    child = next;
                }
            }
        }

        return null;
    }

    internal void CollectFocusable(List<UiElement> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _nodes.EnsureCurrent(_retainedRoot);
        _traversal.Clear();
        _traversal.Add(new(new UiNodeId(_retainedRoot.Id.Value, _retainedRoot.Generation), false));
        while (_traversal.Count != 0)
        {
            var last = _traversal.Count - 1;
            var entry = _traversal[last];
            _traversal.RemoveAt(last);
            if (!_nodes.TryGetNode(entry.Id, out var node) || node.Element is not { } element)
            {
                continue;
            }

            if (element.Focusable)
            {
                result.Add(element);
            }

            _childOrder.Clear();
            if (_nodes.TryGetFirstLogicalChild(node.Id, out var child))
            {
                while (true)
                {
                    _childOrder.Add(child.Id);
                    if (!_nodes.TryGetNextLogicalSibling(child, out var next))
                    {
                        break;
                    }

                    child = next;
                }
            }

            for (var i = _childOrder.Count - 1; i >= 0; i--)
            {
                _traversal.Add(new(_childOrder[i], false));
            }
        }
    }

    private readonly record struct UiTraversalEntry(UiNodeId Id, bool Exit);
}
