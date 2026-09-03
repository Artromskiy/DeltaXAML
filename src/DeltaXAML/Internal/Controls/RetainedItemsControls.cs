using Delta;
using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal sealed class ItemsControl : UiElement
{
    private ItemsControlState _state = new() { Layout = new() { Orientation = UiOrientation.Vertical } };
    private readonly List<object?> _items = new();
    private readonly List<UiElement> _realized = new();
    private readonly List<UiElement> _nextRealized = new();

    internal ref ItemsControlState State => ref _state;
    internal bool IsGridLayout => _state.GridLayout;

    public ItemsControl() : base("ItemsControl") { }
    public IReadOnlyList<object?> Items => _items;
    public IReadOnlyList<UiElement> RealizedItems => _realized;

    internal void SetGridDimensions(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        if (_state.GridLayout && _state.GridColumns == columns && _state.GridRows == rows)
        {
            return;
        }

        _state.GridLayout = true;
        _state.GridColumns = columns;
        _state.GridRows = rows;
        InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
    }

    public void SetItems(IReadOnlyList<object?> items, Func<object?, UiElement> factory)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(factory);

        var source = new ItemSource(items);
        var previous = new ItemSource(_items);
        var adapter = new ItemFactoryAdapter(factory);
        UiItemsControlGenerated.ApplyItems(ref _state, source, previous, adapter, _realized, _nextRealized);

        ClearChildren();
        _items.Clear();
        _items.AddRange(items);
        _realized.Clear();
        _realized.AddRange(_nextRealized);
        foreach (var child in _realized)
        {
            Add(child);
        }
    }

    private sealed class ItemSource(IReadOnlyList<object?> values) : IUiItemSource<object?>
    {
        public int Count => values.Count;
        public object? GetValue(int index) => values[index];
        public bool Matches(IUiItemSource<object?> previous, int index) => Equals(values[index], previous.GetValue(index));
    }

    private sealed class ItemFactoryAdapter(Func<object?, UiElement> factory) : IUiItemFactory<object?>
    {
        public UiElement Create(IUiItemSource<object?> source, int index)
        {
            var element = factory(source.GetValue(index));
            return element ?? throw new InvalidOperationException("The item factory returned a null UI element.");
        }
    }
}

internal sealed class CollectionView : UiElement
{
    private CollectionViewState _state = new() { SelectedIndex = -1 };
    internal ref CollectionViewState State => ref _state;
    internal CollectionView() : base("CollectionView")
    {
        AutomationRole = UiAutomationRole.List;
        Focusable = true;
    }
    internal int SelectedIndex
    {
        get => _state.SelectedIndex;
        set
        {
            if (_state.SelectedIndex == value)
            {
                return;
            }

            _state.SelectedIndex = value;
            ApplySelection();
            InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual);
        }
    }

    internal bool SelectTarget(UiElement? target)
    {
        var items = ItemsHost;
        if (items is null || target is null)
        {
            return false;
        }

        var current = target;
        while (current.Parent is { } parent && !ReferenceEquals(parent, items))
        {
            current = parent;
        }

        if (!ReferenceEquals(current.Parent, items) || current.CollectionIndex < 0)
        {
            return false;
        }

        SelectedIndex = current.CollectionIndex;
        return true;
    }

    internal bool MoveSelection(int delta)
    {
        var items = ItemsHost;
        if (items is null || items.Children.Count == 0)
        {
            return false;
        }

        var first = items.Children[0].CollectionIndex;
        var last = items.Children[^1].CollectionIndex;
        var next = SelectedIndex < first || SelectedIndex > last
            ? delta < 0 ? last : first
            : Maths.Clamp(SelectedIndex + delta, first, last);
        if (next == SelectedIndex)
        {
            return false;
        }

        SelectedIndex = next;
        return true;
    }

    private ItemsControl? ItemsHost =>
        Children.Count > 0 && Children[0] is ScrollViewer { Content: ItemsControl items } ? items : null;

    private void ApplySelection()
    {
        if (ItemsHost is not { } items)
        {
            return;
        }

        for (var i = 0; i < items.Children.Count; i++)
        {
            items.Children[i].IsSelected = items.Children[i].CollectionIndex == _state.SelectedIndex;
        }
    }
}


internal class ScrollViewer : UiElement
{
    private ScrollViewerState _state;

    internal ref ScrollViewerState State => ref _state;

    public ScrollViewer() : base("ScrollViewer") { }
    public UiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
    public UiPoint Offset => _state.Offset;
    public void ScrollBy(float x, float y)
    {
        if (UiScrollViewerGenerated.TryScrollBy(ref _state, x, y))
        {
            InvalidateChanged(UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }

}
