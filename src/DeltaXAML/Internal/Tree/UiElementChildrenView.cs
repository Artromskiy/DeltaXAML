using System.Collections;

namespace DeltaXAML.Internal;

/// <summary>Stable child view over detached composition or the attached node store.</summary>
internal sealed class UiElementChildrenView(UiElement owner) : IReadOnlyList<UiElement>
{
    public int Count => owner.NodeStore is { } store
        ? store.GetLogicalChildCount(owner)
        : owner.DetachedChildren.Count;

    public UiElement this[int index] => owner.NodeStore is { } store
        ? store.GetLogicalChild(owner, index)
        : owner.DetachedChildren[index];

    public IEnumerator<UiElement> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
