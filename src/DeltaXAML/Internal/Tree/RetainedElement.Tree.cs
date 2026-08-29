using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal partial class UiElement
{
    public void Add(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);

        for (var ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, child))
            {
                throw new ArgumentException("A UI element cannot be added below itself.", nameof(child));
            }
        }

        if (child._nodeStore is not null && !ReferenceEquals(child._nodeStore, _nodeStore))
        {
            throw new InvalidOperationException("An element cannot move between attached documents without being detached first.");
        }

        if (_nodeStore is null && child._nodeStore is not null)
        {
            throw new InvalidOperationException("A retained document root cannot be reparented while its node store is attached.");
        }

        if (child.Parent is { } parent)
        {
            parent.Remove(child);
        }

        if (_hasExplicitBindingContext && !child._hasExplicitBindingContext)
        {
            child.SetBindingContext(_bindingContext, false);
        }

        if (_nodeStore is { } store)
        {
            store.AddLogicalChild(this, child);
        }
        else
        {
            child._detachedParent = this;
            _detachedChildren.Add(child);
            child.AdvanceDetachedRelationVersion();
        }

        var invalidation = UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual;
        if (child.IsStyleDirty)
        {
            invalidation |= UiDirtyFlags.Style;
        }

        InvalidateChanged(invalidation);
        _nodeStore?.CommitTreeVersion();
    }
    public bool Remove(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_nodeStore is { } store)
        {
            if (!store.RemoveLogicalChild(this, child))
            {
                return false;
            }

            InvalidateChanged(UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
            store.CommitTreeVersion();
            return true;
        }

        var index = _detachedChildren.IndexOf(child);
        if (index < 0)
        {
            return false;
        }

        _detachedChildren.RemoveAt(index);
        child._detachedParent = null;
        child.AdvanceDetachedRelationVersion();
        InvalidateChanged(UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        return true;
    }
    public void ClearChildren()
    {
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            Remove(Children[i]);
        }
    }

    internal UiElement RootElement
    {
        get
        {
            var root = this;
            while (root.Parent is { } parent)
            {
                root = parent;
            }

            return root;
        }
    }

    internal void AttachNodeStore(UiNodeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (_nodeStore is { } current && !ReferenceEquals(current, store))
        {
            throw new InvalidOperationException("A retained root can have only one authoritative node store.");
        }

        _nodeStore = store;
        _detachedParent = null;
        _detachedChildren.Clear();
        _relationVersion++;
    }

    internal void PrepareNodeStoreDetachment(UiNodeStore store)
    {
        if (!ReferenceEquals(_nodeStore, store))
        {
            throw new InvalidOperationException("The element is not owned by this node store.");
        }

        _detachedParent = null;
        _detachedChildren.Clear();
    }

    internal void AddDetachedChild(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _detachedChildren.Add(child);
        child._detachedParent = this;
    }

    internal void CompleteNodeStoreDetachment(UiNodeStore store)
    {
        if (ReferenceEquals(_nodeStore, store))
        {
            _nodeStore = null;
            _relationVersion++;
        }
    }
}
