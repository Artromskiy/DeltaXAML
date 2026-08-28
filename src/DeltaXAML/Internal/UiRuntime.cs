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

    public UiRuntime(IUiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _retainedRoot = root as UiElement ?? throw new ArgumentException("Root must be a DeltaXAML element.", nameof(root));
        Root = _retainedRoot;
        _nodes = new(_retainedRoot);
        _input = new(this);
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
        AppliedMutationCount = 0;
        RejectedMutationCount = 0;
        for (var i = 0; i < _mutations.Count; i++)
        {
            var mutation = _mutations[i];
            if (TryResolve(mutation.Target, out var element) && element.TrySet(mutation.Target, mutation.Value, mutation.Invalidation, out _))
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

    public void Layout(UiSize viewport, float dpiScale)
    {
        ApplyInput();
        ApplyMutations();
        _retainedRoot.SetLayoutScale(dpiScale);
        var scaled = new UiSize(viewport.Width * dpiScale, viewport.Height * dpiScale);
        Root.Measure(scaled);
        Root.Arrange(new(0, 0, viewport.Width, viewport.Height));
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

    private void ApplyInput()
    {
        for (var i = 0; i < _inputQueue.Count; i++)
        {
            _input.Dispatch(_inputQueue[i]);
        }

        _inputQueue.Clear();
    }
}
