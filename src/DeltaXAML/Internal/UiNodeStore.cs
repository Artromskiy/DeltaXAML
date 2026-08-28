using System.Diagnostics.CodeAnalysis;

namespace DeltaXAML.Internal;

/// <summary>Resolves retained element identities through one dense relation index.</summary>
/// <remarks>
/// The store is initialized from the composed tree once. Later structural changes are applied
/// directly by the owner mutation path; frame stages only read these records and validate the
/// element generation on every handle lookup.
/// </remarks>
internal sealed class UiNodeStore
{
    private UiNodeRecord[] _records = Array.Empty<UiNodeRecord>();
    private int[] _activePositions = Array.Empty<int>();
    private readonly List<RegistrationVisit> _registrationQueue = new();
    private readonly List<int> _activeIndices = new();
    private readonly List<UiNodeId> _layoutChildIds = new();
    private readonly List<UiNodeId> _removalQueue = new();
    private readonly NodeChildrenView _layoutChildren;
    private readonly UiElement _root;
    private uint _treeVersion;

    internal UiNodeStore(UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        _layoutChildren = new(this, _layoutChildIds);
        Refresh(root);
        root.AttachNodeStore(this);
    }

    internal void EnsureCurrent(UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!ReferenceEquals(root, _root))
        {
            throw new InvalidOperationException("The node store is attached to a different retained root.");
        }

        if (_treeVersion != root.TreeVersion)
        {
            throw new InvalidOperationException("A retained tree mutation bypassed the node store.");
        }
    }

    internal void Detach()
    {
        _root.DetachNodeStore(this);
    }

    internal void ApplyChildAdded(UiElement parent, UiElement child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        EnsureCurrentVersionBeforeMutation(parent);
        if (!ReferenceEquals(child.Parent, parent) || !TryGetRecord(ToNodeId(parent), out var parentRecord))
        {
            throw new InvalidOperationException("The added element does not belong to the indexed parent.");
        }

        var childId = ToNodeId(child);
        if (TryGetRecord(childId, out _))
        {
            throw new InvalidOperationException("The added element is already registered in the node store.");
        }

        var previous = parent.Children.Count > 1 && parent.Children[parent.Children.Count - 2] is UiElement previousChild
            ? previousChild
            : null;
        if (!parentRecord.FirstLogicalChild.IsValid)
        {
            parentRecord.FirstLogicalChild = childId;
            parentRecord.FirstVisualChild = childId;
            _records[(int)parentRecord.Id.Index] = parentRecord;
        }

        RegisterSubtree(child, parent, previous);
        _treeVersion = _root.TreeVersion;
    }

    internal void ApplyChildRemoved(UiElement parent, UiElement child, int index)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        EnsureCurrentVersionBeforeMutation(parent);
        var parentId = ToNodeId(parent);
        var childId = ToNodeId(child);
        if (!TryGetRecord(parentId, out var parentRecord) || !TryGetRecord(childId, out var childRecord))
        {
            throw new InvalidOperationException("The removed element is not registered in the node store.");
        }

        var next = childRecord.NextLogicalSibling;
        if (index == 0)
        {
            parentRecord.FirstLogicalChild = next;
            parentRecord.FirstVisualChild = next;
        }
        else if (parent.Children[index - 1] is UiElement previous &&
                 TryGetRecord(ToNodeId(previous), out var previousRecord))
        {
            previousRecord.NextLogicalSibling = next;
            previousRecord.NextVisualSibling = childRecord.NextVisualSibling;
            _records[(int)previousRecord.Id.Index] = previousRecord;
        }
        else
        {
            throw new InvalidOperationException("The removed element has no indexed preceding sibling.");
        }

        _records[(int)parentRecord.Id.Index] = parentRecord;
        RemoveSubtree(childId);
        _treeVersion = _root.TreeVersion;
    }

    internal bool TryResolve(UiPropertyHandle handle, [NotNullWhen(true)] out UiElement? element)
    {
        if (!TryGetRecord(new UiNodeId(handle.Element.Value, handle.Generation), out var record))
        {
            element = null;
            return false;
        }

        if (record.Element is not { } resolved)
        {
            element = null;
            return false;
        }

        element = resolved;
        return true;
    }

    internal bool TryResolve(UiElementId id, [NotNullWhen(true)] out UiElement? element)
    {
        if (!TryGetRecord(new UiNodeId(id.Value, 0), out var record, validateGeneration: false))
        {
            element = null;
            return false;
        }

        if (record.Element is not { } resolved)
        {
            element = null;
            return false;
        }

        element = resolved;
        return true;
    }

    internal bool TryGetNode(UiNodeId id, out UiNodeRecord record) => TryGetRecord(id, out record);

    internal int Count => _activeIndices.Count;

    internal bool TryGetFirstLogicalChild(UiNodeId parent, out UiNodeRecord child) =>
        TryGetFirstChild(parent, visual: false, out child);

    internal bool TryGetFirstVisualChild(UiNodeId parent, out UiNodeRecord child) =>
        TryGetFirstChild(parent, visual: true, out child);

    internal bool TryGetNextLogicalSibling(UiNodeRecord current, out UiNodeRecord sibling) =>
        TryGetNextSibling(current, visual: false, out sibling);

    internal bool TryGetNextVisualSibling(UiNodeRecord current, out UiNodeRecord sibling) =>
        TryGetNextSibling(current, visual: true, out sibling);

    internal bool TryCopyLogicalChildren(UiNodeId parent, List<UiNodeId> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        if (!TryGetRecord(parent, out var parentRecord))
        {
            return false;
        }

        var childId = parentRecord.FirstLogicalChild;
        while (childId.IsValid)
        {
            if (!TryGetRecord(childId, out var child))
            {
                destination.Clear();
                return false;
            }

            destination.Add(child.Id);
            childId = child.NextLogicalSibling;
        }

        return true;
    }

    internal bool TryCopyVisualChildren(UiNodeId parent, List<UiNodeId> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        if (!TryGetRecord(parent, out var parentRecord))
        {
            return false;
        }

        var childId = parentRecord.FirstVisualChild;
        while (childId.IsValid)
        {
            if (!TryGetRecord(childId, out var child))
            {
                destination.Clear();
                return false;
            }

            destination.Add(child.Id);
            childId = child.NextVisualSibling;
        }

        return true;
    }

    internal IReadOnlyList<UiElement> GetLogicalChildren(UiNodeId parent)
    {
        if (!TryCopyLogicalChildren(parent, _layoutChildIds))
        {
            throw new InvalidOperationException($"Logical node {parent.Index}:{parent.Generation} could not be resolved.");
        }

        return _layoutChildren;
    }

    private bool TryGetFirstChild(UiNodeId parent, bool visual, out UiNodeRecord child)
    {
        if (!TryGetRecord(parent, out var record))
        {
            child = default;
            return false;
        }

        var firstChild = visual ? record.FirstVisualChild : record.FirstLogicalChild;
        child = default;
        return firstChild.IsValid && TryGetRecord(firstChild, out child);
    }

    private bool TryGetNextSibling(UiNodeRecord current, bool visual, out UiNodeRecord sibling)
    {
        var nextSibling = visual ? current.NextVisualSibling : current.NextLogicalSibling;
        if (!nextSibling.IsValid)
        {
            sibling = default;
            return false;
        }

        return TryGetRecord(nextSibling, out sibling);
    }

    private void Refresh(UiElement root)
    {
        for (var i = 0; i < _activeIndices.Count; i++)
        {
            var index = _activeIndices[i];
            _records[index] = default;
            _activePositions[index] = -1;
        }

        _activeIndices.Clear();
        RegisterSubtree(root, null, null);
        _treeVersion = root.TreeVersion;
    }

    private void RegisterSubtree(UiElement root, UiElement? parent, UiElement? previousSibling)
    {
        _registrationQueue.Clear();
        _registrationQueue.Add(new(root, parent, previousSibling));
        for (var visitIndex = 0; visitIndex < _registrationQueue.Count; visitIndex++)
        {
            var visit = _registrationQueue[visitIndex];
            var element = visit.Element;
            var index = checked((int)element.Id.Value);
            if (index >= _records.Length)
            {
                Array.Resize(ref _records, Math.Max(index + 1, Math.Max(8, _records.Length * 2)));
                Array.Resize(ref _activePositions, _records.Length);
            }

            var parentId = visit.Parent is null ? default : ToNodeId(visit.Parent);
            UiDescriptorCatalog.TryResolve(element, out var runtimeType);
            var record = new UiNodeRecord(
                element,
                ToNodeId(element),
                runtimeType,
                parentId,
                parentId,
                default,
                default,
                default,
                default,
                element.DirtyFlags);
            _records[index] = record;
            _activeIndices.Add(index);
            _activePositions[index] = _activeIndices.Count - 1;
            if (visit.PreviousSibling is not null && TryGetRecord(ToNodeId(visit.PreviousSibling), out var previousRecord))
            {
                previousRecord.NextLogicalSibling = record.Id;
                previousRecord.NextVisualSibling = record.Id;
                _records[(int)previousRecord.Id.Index] = previousRecord;
            }

            UiNodeId firstChild = default;
            UiElement? previousChild = null;
            for (var childIndex = 0; childIndex < element.Children.Count; childIndex++)
            {
                var child = element.Children[childIndex];
                var childId = ToNodeId(child);
                firstChild = firstChild.IsValid ? firstChild : childId;
                _registrationQueue.Add(new(child, element, previousChild));
                previousChild = child;
            }

            record.FirstLogicalChild = firstChild;
            record.FirstVisualChild = firstChild;
            _records[index] = record;
        }
    }

    private void RemoveSubtree(UiNodeId root)
    {
        _removalQueue.Clear();
        _removalQueue.Add(root);
        while (_removalQueue.Count != 0)
        {
            var last = _removalQueue.Count - 1;
            var id = _removalQueue[last];
            _removalQueue.RemoveAt(last);
            if (!TryGetRecord(id, out var record))
            {
                continue;
            }

            var child = record.FirstLogicalChild;
            while (child.IsValid)
            {
                _removalQueue.Add(child);
                if (!TryGetRecord(child, out var childRecord))
                {
                    break;
                }

                child = childRecord.NextLogicalSibling;
            }

            _records[(int)id.Index] = default;
            RemoveActiveIndex((int)id.Index);
        }
    }

    private void RemoveActiveIndex(int index)
    {
        if ((uint)index >= (uint)_activePositions.Length)
        {
            return;
        }

        var position = _activePositions[index];
        if ((uint)position >= (uint)_activeIndices.Count || _activeIndices[position] != index)
        {
            return;
        }

        var lastPosition = _activeIndices.Count - 1;
        var lastIndex = _activeIndices[lastPosition];
        _activeIndices[position] = lastIndex;
        _activeIndices.RemoveAt(lastPosition);
        _activePositions[lastIndex] = position;
        _activePositions[index] = -1;
    }

    private void EnsureCurrentVersionBeforeMutation(UiElement parent)
    {
        if (!ReferenceEquals(parent.RootElement, _root) || _treeVersion != _root.TreeVersion - 1)
        {
            throw new InvalidOperationException("The retained tree mutation is not owned by this node store.");
        }
    }

    private bool TryGetRecord(UiNodeId id, out UiNodeRecord record, bool validateGeneration = true)
    {
        if (id.Index >= (uint)_records.Length)
        {
            record = default;
            return false;
        }

        var candidate = _records[(int)id.Index];
        if (candidate.Element is not { } element ||
            (validateGeneration && candidate.Id.Generation != id.Generation))
        {
            record = default;
            return false;
        }

        record = candidate;
        record.Dirty = element.DirtyFlags;
        return true;
    }

    private static UiNodeId ToNodeId(UiElement element) => new(element.Id.Value, element.Generation);

    private readonly record struct RegistrationVisit(UiElement Element, UiElement? Parent, UiElement? PreviousSibling);

    private sealed class NodeChildrenView(UiNodeStore owner, List<UiNodeId> ids) : IReadOnlyList<UiElement>
    {
        public int Count => ids.Count;

        public UiElement this[int index]
        {
            get
            {
                if ((uint)index >= (uint)ids.Count || !owner.TryGetNode(ids[index], out var node) || node.Element is not { } element)
                {
                    throw new InvalidOperationException("A logical child node could not be resolved.");
                }

                return element;
            }
        }

        public IEnumerator<UiElement> GetEnumerator()
        {
            for (var i = 0; i < ids.Count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

internal readonly record struct UiNodeId(uint Index, uint Generation)
{
    internal bool IsValid => Index != 0 && Generation != 0;
}

internal struct UiNodeRecord
{
    internal UiNodeRecord(
        UiElement element,
        UiNodeId id,
        UiRuntimeTypeIndex runtimeType,
        UiNodeId logicalParent,
        UiNodeId visualParent,
        UiNodeId firstLogicalChild,
        UiNodeId firstVisualChild,
        UiNodeId nextLogicalSibling,
        UiNodeId nextVisualSibling,
        UiDirtyMask dirty)
    {
        Element = element;
        Id = id;
        RuntimeType = runtimeType;
        LogicalParent = logicalParent;
        VisualParent = visualParent;
        FirstLogicalChild = firstLogicalChild;
        FirstVisualChild = firstVisualChild;
        NextLogicalSibling = nextLogicalSibling;
        NextVisualSibling = nextVisualSibling;
        Dirty = dirty;
    }

    internal UiElement? Element;
    internal UiNodeId Id;
    internal UiRuntimeTypeIndex RuntimeType;
    internal UiNodeId LogicalParent;
    internal UiNodeId VisualParent;
    internal UiNodeId FirstLogicalChild;
    internal UiNodeId FirstVisualChild;
    internal UiNodeId NextLogicalSibling;
    internal UiNodeId NextVisualSibling;
    internal UiDirtyMask Dirty;
}
