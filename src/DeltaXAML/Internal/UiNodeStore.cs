using System.Diagnostics.CodeAnalysis;

namespace DeltaXAML.Internal;

/// <summary>Resolves retained element identities without maintaining a second hierarchy.</summary>
/// <remarks>
/// The retained tree remains owned by <see cref="UiElement"/>. This store is only a reusable
/// dense identity index; its entries are rebuilt after a structural version change and are
/// validated against the element generation on every handle lookup.
/// </remarks>
internal sealed class UiNodeStore
{
    private UiNodeRecord[] _records = Array.Empty<UiNodeRecord>();
    private readonly List<RegistrationVisit> _registrationQueue = new();
    private readonly List<int> _activeIndices = new();
    private readonly List<UiNodeId> _layoutChildIds = new();
    private readonly NodeChildrenView _layoutChildren;
    private uint _treeVersion;

    internal UiNodeStore(UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _layoutChildren = new(this, _layoutChildIds);
        Refresh(root);
    }

    internal void EnsureCurrent(UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_treeVersion != root.TreeVersion)
        {
            Refresh(root);
        }
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
            _records[_activeIndices[i]] = default;
        }

        _activeIndices.Clear();
        _registrationQueue.Clear();
        _registrationQueue.Add(new(root, null, null));
        for (var visitIndex = 0; visitIndex < _registrationQueue.Count; visitIndex++)
        {
            var visit = _registrationQueue[visitIndex];
            var element = visit.Element;
            var index = checked((int)element.Id.Value);
            if (index >= _records.Length)
            {
                Array.Resize(ref _records, Math.Max(index + 1, Math.Max(8, _records.Length * 2)));
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
            if (visit.PreviousSibling is not null)
            {
                var previousIndex = checked((int)visit.PreviousSibling.Id.Value);
                var previous = _records[previousIndex];
                previous.NextLogicalSibling = record.Id;
                previous.NextVisualSibling = record.Id;
                _records[previousIndex] = previous;
            }

            UiNodeId firstChild = default;
            UiElement? previousChild = null;
            for (var childIndex = 0; childIndex < element.Children.Count; childIndex++)
            {
                var child = element.Children[childIndex];
                if (child is UiElement childElement)
                {
                    var childId = ToNodeId(childElement);
                    firstChild = firstChild.IsValid ? firstChild : childId;
                    _registrationQueue.Add(new(childElement, element, previousChild));
                    previousChild = childElement;
                }
            }

            record.FirstLogicalChild = firstChild;
            record.FirstVisualChild = firstChild;
            _records[index] = record;
        }

        _treeVersion = root.TreeVersion;
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
