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
    private uint _treeVersion;

    internal UiNodeStore(UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
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

    private void Refresh(UiElement root)
    {
        Array.Clear(_records);
        Register(root);
        _treeVersion = root.TreeVersion;
    }

    private void Register(UiElement element, UiElement? parent = null, UiElement? previousSibling = null)
    {
        var index = checked((int)element.Id.Value);
        if (index >= _records.Length)
        {
            Array.Resize(ref _records, Math.Max(index + 1, Math.Max(8, _records.Length * 2)));
        }

        var parentId = parent is null ? default : ToNodeId(parent);
        var record = new UiNodeRecord(
            element,
            ToNodeId(element),
            parentId,
            parentId,
            default,
            default,
            default,
            default,
            element.DirtyFlags);
        _records[index] = record;
        if (previousSibling is not null)
        {
            var previousIndex = checked((int)previousSibling.Id.Value);
            _records[previousIndex].NextLogicalSibling = record.Id;
            _records[previousIndex].NextVisualSibling = record.Id;
        }

        UiNodeId firstChild = default;
        UiElement? previousChild = null;
        foreach (var child in element.Children)
        {
            if (child is UiElement childElement)
            {
                Register(childElement, element, previousChild);
                firstChild = firstChild.IsValid ? firstChild : ToNodeId(childElement);
                previousChild = childElement;
            }
        }

        record.FirstLogicalChild = firstChild;
        record.FirstVisualChild = firstChild;
        _records[index] = record;
    }

    private bool TryGetRecord(UiNodeId id, out UiNodeRecord record, bool validateGeneration = true)
    {
        if (id.Index >= (uint)_records.Length)
        {
            record = default;
            return false;
        }

        var candidate = _records[(int)id.Index];
        if (candidate.Element is null || (validateGeneration && candidate.Id.Generation != id.Generation))
        {
            record = default;
            return false;
        }

        record = candidate;
        return true;
    }

    private static UiNodeId ToNodeId(UiElement element) => new(element.Id.Value, element.Generation);
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
    internal UiNodeId LogicalParent;
    internal UiNodeId VisualParent;
    internal UiNodeId FirstLogicalChild;
    internal UiNodeId FirstVisualChild;
    internal UiNodeId NextLogicalSibling;
    internal UiNodeId NextVisualSibling;
    internal UiDirtyMask Dirty;
}
