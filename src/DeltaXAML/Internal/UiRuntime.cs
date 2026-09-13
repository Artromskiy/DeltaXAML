using Delta;
using Delta.XAML.Contract;

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
    private readonly List<UiControlMutation> _controlMutations = new();
    private readonly List<Delta.XAML.UiInputSample> _inputQueue = new();
    private readonly UiSemanticQueue _semanticCommands = new();
    private readonly UiElement _retainedRoot;
    private readonly UiNodeStore _nodes;
    private readonly UiInputRouter _input;
    private readonly List<UiTraversalEntry> _traversal = new();
    private readonly List<UiNodeId> _childOrder = new();
    private readonly List<UiNodeId> _stageTraversal = new();
    private readonly UiMeasureQueueBuffer _measureQueue = new();
    private readonly UiArrangeQueueBuffer _arrangeQueue = new();
    private char[] _inputTextStorage = Array.Empty<char>();
    private int _inputTextCount;
    private float _appliedScale = float.NaN;
    private uint _scaledTreeVersion;
    private uint _bindingTreeVersion;
    private bool _disposed;

    public UiRuntime(UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _retainedRoot = root;
        Root = _retainedRoot;
        _nodes = new(_retainedRoot);
        _input = new(this);
        UiBindingStage.Run(_nodes, _retainedRoot, _stageTraversal, _childOrder, force: true);
        _bindingTreeVersion = _retainedRoot.TreeVersion;
    }

    public UiElement Root { get; }
    public UiInputRouter Input => _input;
    public int AppliedMutationCount { get; private set; }
    public int RejectedMutationCount { get; private set; }
    public int PendingMutationCount => _mutations.Count;
    internal int PendingInputCount => _inputQueue.Count;
    internal int NodeCount => _nodes.Count;
    internal ReadOnlySpan<Delta.XAML.UiSemanticCommand> SemanticCommands => _semanticCommands.Values;

    internal void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inputQueue.Clear();
        _controlMutations.Clear();
        _mutations.Clear();
        _inputTextCount = 0;
        _nodes.Detach();
        _retainedRoot.DisposeRuntime();
    }

    public void Enqueue(in UiMutation mutation) => _mutations.Add(mutation);

    internal void EnqueueInput(in UiInputEvent packet) =>
        EnqueueInput(new Delta.XAML.UiInputSample(packet, TimeSpan.Zero));

    internal void EnqueueInput(in Delta.XAML.UiInputSample sample)
    {
        var packet = sample.Event;
        switch (packet.Kind)
        {
            case UiInputEventKind.Text:
                _inputQueue.Add(new(
                    UiInputEvent.FromText(new UiTextInput(CopyInputText(packet.Text.Text.Span))),
                    sample.Timestamp));
                break;
            case UiInputEventKind.Composition:
                var composition = packet.Composition;
                _inputQueue.Add(new(
                    UiInputEvent.FromComposition(new UiCompositionEvent(
                        composition.Stage,
                        CopyInputText(composition.Preedit.Span),
                        composition.Selection)),
                    sample.Timestamp));
                break;
            default:
                _inputQueue.Add(sample);
                break;
        }
    }

    public void ApplyMutations()
    {
        UiMutationStage.Run(_nodes, _retainedRoot, _controlMutations, _mutations, out var applied, out var rejected);
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
        Delta.XAML.UiElement? publicRoot,
        Delta.XAML.IUiImageMetadataResolver? imageMetadataResolver = null,
        Delta.XAML.IUiGeneratedDocumentProgram? program = null,
        Delta.XAML.UiDocument? document = null,
        Delta.XAML.UiTextLayoutCache? textLayout = null)
    {
        _semanticCommands.Clear();
        UiInputStage.Run(_input, _inputQueue);
        if (program is not null)
        {
            if (document is null)
            {
                throw new InvalidOperationException("A generated document program requires its owning document stage context.");
            }

            program.RunStage(document, Delta.XAML.UiGeneratedStage.AfterInput);
        }

        UiMutationStage.Run(_nodes, _retainedRoot, _controlMutations, _mutations, out var applied, out var rejected);
        _inputTextCount = 0;
        AppliedMutationCount = applied;
        RejectedMutationCount = rejected;
        _retainedRoot.PollCollectionBindings();
        var bindingTreeChanged = _bindingTreeVersion != _retainedRoot.TreeVersion;
        UiBindingStage.Run(_nodes, _retainedRoot, _stageTraversal, _childOrder, bindingTreeChanged);
        _bindingTreeVersion = _retainedRoot.TreeVersion;
        if (program is not null)
        {
            if (document is null)
            {
                throw new InvalidOperationException("A generated document program requires its owning document stage context.");
            }

            program.RunStage(document, Delta.XAML.UiGeneratedStage.AfterBindings);
        }

        if (publicRoot is not null)
        {
            UiStyleStage.Run(theme, _nodes, publicRoot, _stageTraversal, _childOrder);
        }

        if (!_appliedScale.Equals(dpiScale) || _scaledTreeVersion != _retainedRoot.TreeVersion)
        {
            UiScaleStage.Run(_nodes, _retainedRoot, dpiScale, _stageTraversal, _childOrder);
            _appliedScale = dpiScale;
            _scaledTreeVersion = _retainedRoot.TreeVersion;
        }
        UiImageMetadataStage.Run(imageMetadataResolver, _nodes, _retainedRoot, _stageTraversal, _childOrder);
        UiMeasureStage.Run(_nodes, _retainedRoot, viewport, _measureQueue, _childOrder, textLayout);
        UiArrangeStage.Run(_nodes, _retainedRoot, new(0, 0, viewport.Width, viewport.Height), _arrangeQueue);
        UiFocusStage.Run(_input);
    }

    internal bool TryResolve(UiPropertyHandle handle, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out UiElement? element)
    {
        _nodes.EnsureCurrent(_retainedRoot);
        return _nodes.TryResolve(handle, out element);
    }

    internal void EnqueueControlInput(UiElement target, in UiInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(target);
        _controlMutations.Add(UiControlMutation.FromInput(new(target.Id.Value, target.Generation), in input));
    }

    internal void EnqueueRoutedEvent(UiElement target, in UiRoutedEvent routedEvent)
    {
        ArgumentNullException.ThrowIfNull(target);
        _controlMutations.Add(UiControlMutation.FromRoutedEvent(new(target.Id.Value, target.Generation), in routedEvent));
    }

    internal void PublishSemantic(
        UiElement source,
        Delta.XAML.UiSemanticActionKind action,
        Delta.XAML.UiGestureData gesture,
        string? argument = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var command = new Delta.XAML.UiSemanticCommand(source.Command, Delta.XAML.UiElement.Wrap(source), action, gesture, argument);
        _semanticCommands.Add(in command);
    }

    internal void PublishSemantic(
        UiElement source,
        Delta.XAML.UiCommandId commandId,
        Delta.XAML.UiSemanticActionKind action,
        string? argument = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var command = new Delta.XAML.UiSemanticCommand(commandId, Delta.XAML.UiElement.Wrap(source), action, default, argument);
        _semanticCommands.Add(in command);
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
                !element.IsEnabled ||
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

    internal void CollectFocusable(List<UiElement> result, UiElement? scope = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        _nodes.EnsureCurrent(_retainedRoot);
        _traversal.Clear();
        var traversalRoot = scope ?? _retainedRoot;
        _traversal.Add(new(new UiNodeId(traversalRoot.Id.Value, traversalRoot.Generation), false));
        while (_traversal.Count != 0)
        {
            var last = _traversal.Count - 1;
            var entry = _traversal[last];
            _traversal.RemoveAt(last);
            if (!_nodes.TryGetNode(entry.Id, out var node) || node.Element is not { } element)
            {
                continue;
            }

            if (element.Focusable &&
                element.IsEnabled &&
                element.Visibility == UiVisibility.Visible &&
                (element.Participation & Delta.XAML.UiParticipation.Layout) != 0)
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

    private ReadOnlyMemory<char> CopyInputText(ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
        {
            return ReadOnlyMemory<char>.Empty;
        }

        var required = checked(_inputTextCount + text.Length);
        if (required > _inputTextStorage.Length)
        {
            Array.Resize(ref _inputTextStorage, Maths.Max(required, Maths.Max(32, _inputTextStorage.Length * 2)));
        }

        text.CopyTo(_inputTextStorage.AsSpan(_inputTextCount));
        var result = new ReadOnlyMemory<char>(_inputTextStorage, _inputTextCount, text.Length);
        _inputTextCount = required;
        return result;
    }
}
