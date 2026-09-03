using Delta;

namespace Delta.XAML;

/// <summary>Structural change reported by a typed collection source.</summary>
public enum UiCollectionChangeKind
{
    None,
    Reset,
    Add,
    Remove,
    Move,
    Replace,
}

/// <summary>A bounded structural range change between two source versions.</summary>
public readonly record struct UiCollectionChange(
    UiCollectionChangeKind Kind,
    int Index,
    int Count,
    int PreviousIndex = -1)
{
    public static UiCollectionChange Reset => new(UiCollectionChangeKind.Reset, 0, 0);
}

/// <summary>Typed source view consumed by generated collection artifacts.</summary>
/// <remarks>
/// Implementations keep item access typed and expose a monotonically changing structural
/// version. Returning <see langword="false"/> from <see cref="TryGetChange"/> requests a
/// conservative visible-range reconciliation; it never permits item boxing.
/// </remarks>
public interface IUiItemsSource<TItem>
{
    int Count { get; }

    ulong Version { get; }

    ulong GetKey(int index);

    TItem GetItem(int index);

    bool TryGetChange(ulong previousVersion, out UiCollectionChange change);
}

/// <summary>
/// Static typed item-template and deterministic selector operations emitted by the XAML
/// generator. One plan may select several stable template identities.
/// </summary>
public interface IUiItemTemplatePlan<TPlan, TItem>
    where TPlan : IUiItemTemplatePlan<TPlan, TItem>
{
    static abstract UiTemplateId SelectTemplate(in TItem item);

    static abstract UiElement Create(UiTemplateId templateId, in TItem item, UiResourceCatalog resources);

    static abstract void Bind(UiElement element, UiTemplateId templateId, in TItem item, UiResourceCatalog resources);

    static abstract void Unbind(UiElement element, UiTemplateId templateId);
}

/// <summary>Half-open source range requested by a virtualizing layout policy.</summary>
public readonly record struct UiRealizationRange(int Start, int Count)
{
    public int End => checked(Start + Count);
}

/// <summary>Observable counters for one bounded realization pass.</summary>
public readonly record struct UiRealizationResult(
    int Created,
    int Reused,
    int Recycled,
    int Rebound,
    UiRealizationRange Range);

/// <summary>Deterministic fixed-extent virtualizing policy shared by collection controls.</summary>
public static class UiVirtualizingLayout
{
    public static UiRealizationRange Vertical(float offset, float viewportExtent, float itemExtent, int itemCount, int overscan = 1)
    {
        if (!float.IsFinite(offset) || offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (!float.IsFinite(viewportExtent) || viewportExtent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportExtent));
        }

        if (!float.IsFinite(itemExtent) || itemExtent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemExtent));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(overscan);
        var firstVisible = Maths.Min(itemCount, (int)Maths.Floor(offset / itemExtent));
        var start = Maths.Max(0, firstVisible - overscan);
        var visibleCount = (int)Maths.Ceil(viewportExtent / itemExtent);
        var count = Maths.Min(itemCount - start, visibleCount + overscan * 2);
        return new(start, Maths.Max(0, count));
    }
}

/// <summary>
/// Typed, allocation-bounded realization owner used by generated ItemsSource/DataTemplate
/// artifacts. Selection, scrolling and chrome remain separate policies.
/// </summary>
public sealed class UiVirtualizingPresenter<TItem, TSource, TPlan>
    where TSource : IUiItemsSource<TItem>
    where TPlan : IUiItemTemplatePlan<TPlan, TItem>
{
    private readonly UiItemsControl _host;
    private readonly UiResourceCatalog _resources;
    private TSource _source;
    private RealizedItem[] _current = Array.Empty<RealizedItem>();
    private RealizedItem[] _next = Array.Empty<RealizedItem>();
    private RealizedItem[] _pool = Array.Empty<RealizedItem>();
    private int _currentCount;
    private int _poolCount;
    private ulong _sourceVersion = ulong.MaxValue;
    private UiRealizationRange _range;

    public UiVirtualizingPresenter(UiItemsControl host, TSource source, UiResourceCatalog? resources = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(source);
        _host = host;
        _source = source;
        _resources = resources ?? new UiResourceCatalog();
    }

    public UiItemsControl Host => _host;

    public TSource Source
    {
        get => _source;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _source = value;
            _sourceVersion = ulong.MaxValue;
        }
    }

    public UiRealizationRange Range => _range;

    public int RealizedCount => _currentCount;

    public UiElement GetRealizedElement(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, _currentCount);

        return _current[offset].Element;
    }

    public ulong GetRealizedKey(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, _currentCount);

        return _current[offset].Key;
    }

    /// <summary>Reconciles only the requested viewport range and reuses retained rows by key.</summary>
    public UiRealizationResult Realize(UiRealizationRange range)
    {
        ValidateRange(range, _source.Count);
        var version = _source.Version;
        if (_sourceVersion == version && _range == range)
        {
            return new(0, _currentCount, 0, 0, range);
        }

        EnsureCapacity(ref _next, range.Count);
        var sourceChanged = _sourceVersion != version;
        var change = default(UiCollectionChange);
        var hasDelta = sourceChanged && _sourceVersion != ulong.MaxValue &&
            _source.TryGetChange(_sourceVersion, out change);
        var created = 0;
        var reused = 0;
        var rebound = 0;
        var nextCount = 0;
        for (var sourceIndex = range.Start; sourceIndex < range.End; sourceIndex++)
        {
            var item = _source.GetItem(sourceIndex);
            var key = _source.GetKey(sourceIndex);
            var template = TPlan.SelectTemplate(in item);
            if (!template.IsValid)
            {
                throw new InvalidOperationException($"Template selection for item {sourceIndex} returned an empty identity.");
            }

            var currentIndex = Find(_current, _currentCount, key, template);
            RealizedItem realized;
            var needsInitialBind = false;
            if (currentIndex >= 0)
            {
                realized = _current[currentIndex];
                _current[currentIndex].Claimed = true;
                reused++;
            }
            else
            {
                var poolIndex = FindTemplate(_pool, _poolCount, template);
                if (poolIndex >= 0)
                {
                    realized = TakePooled(poolIndex);
                    reused++;
                    needsInitialBind = true;
                }
                else
                {
                    realized = new(TPlan.Create(template, in item, _resources), key, sourceIndex, template, false);
                    if (realized.Element is null)
                    {
                        throw new InvalidOperationException("The generated item template returned a null element.");
                    }

                    created++;
                    needsInitialBind = true;
                }
            }

            var mustBind = needsInitialBind || realized.Key != key ||
                ShouldRebind(sourceIndex, sourceChanged, hasDelta, change);
            realized.Key = key;
            realized.SourceIndex = sourceIndex;
            realized.Template = template;
            realized.Claimed = false;
            realized.Element.SetCollectionIndex(sourceIndex);
            realized.Element.AutomationRole = UiSemanticRole.ListItem;
            ApplySelection(realized.Element, sourceIndex);
            if (mustBind)
            {
                TPlan.Bind(realized.Element, template, in item, _resources);
                rebound++;
            }

            _next[nextCount++] = realized;
        }

        var firstChanged = FirstChanged(_current, _currentCount, _next, nextCount);
        for (var i = _currentCount - 1; i >= firstChanged; i--)
        {
            _host.Remove(_current[i].Element);
        }

        var recycled = 0;
        for (var i = 0; i < _currentCount; i++)
        {
            if (_current[i].Claimed)
            {
                _current[i].Claimed = false;
                continue;
            }

            var retainedInNext = FindElement(_next, nextCount, _current[i].Element) >= 0;
            if (retainedInNext)
            {
                continue;
            }

            TPlan.Unbind(_current[i].Element, _current[i].Template);
            _current[i].Element.SetCollectionIndex(-1);
            EnsureCapacity(ref _pool, _poolCount + 1);
            _pool[_poolCount++] = _current[i];
            recycled++;
        }

        for (var i = firstChanged; i < nextCount; i++)
        {
            _host.Add(_next[i].Element);
        }

        (_current, _next) = (_next, _current);
        _currentCount = nextCount;
        _sourceVersion = version;
        _range = range;
        return new(created, reused, recycled, rebound, range);
    }

    private static bool ShouldRebind(
        int sourceIndex,
        bool sourceChanged,
        bool hasDelta,
        UiCollectionChange change)
    {
        if (!sourceChanged)
        {
            return false;
        }

        if (!hasDelta || change.Kind == UiCollectionChangeKind.Reset)
        {
            return true;
        }

        return change.Kind switch
        {
            UiCollectionChangeKind.Replace =>
                sourceIndex >= change.Index && sourceIndex < checked(change.Index + Maths.Max(1, change.Count)),
            UiCollectionChangeKind.Add or UiCollectionChangeKind.Remove or UiCollectionChangeKind.Move => false,
            _ => false,
        };
    }

    private static int FirstChanged(RealizedItem[] current, int currentCount, RealizedItem[] next, int nextCount)
    {
        var common = Maths.Min(currentCount, nextCount);
        var index = 0;
        while (index < common && ReferenceEquals(current[index].Element, next[index].Element))
        {
            index++;
        }

        return index;
    }

    private static int Find(RealizedItem[] values, int count, ulong key, UiTemplateId template)
    {
        for (var i = 0; i < count; i++)
        {
            if (!values[i].Claimed && values[i].Key == key && values[i].Template == template)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindTemplate(RealizedItem[] values, int count, UiTemplateId template)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            if (values[i].Template == template)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindElement(RealizedItem[] values, int count, UiElement element)
    {
        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(values[i].Element, element))
            {
                return i;
            }
        }

        return -1;
    }

    private RealizedItem TakePooled(int index)
    {
        var result = _pool[index];
        _poolCount--;
        _pool[index] = _pool[_poolCount];
        _pool[_poolCount] = default;
        return result;
    }

    private static void EnsureCapacity(ref RealizedItem[] values, int count)
    {
        if (values.Length >= count)
        {
            return;
        }

        Array.Resize(ref values, Maths.Max(count, Maths.Max(4, values.Length * 2)));
    }

    private static void ValidateRange(UiRealizationRange range, int sourceCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(range.Start);
        ArgumentOutOfRangeException.ThrowIfNegative(range.Count);
        if (range.Start > sourceCount || range.Count > sourceCount - range.Start)
        {
            throw new ArgumentOutOfRangeException(nameof(range), "The realization range must fit the typed source.");
        }
    }

    private void ApplySelection(UiElement element, int sourceIndex)
    {
        for (var owner = _host.Parent; owner is not null; owner = owner.Parent)
        {
            if (owner is UiCollectionView collection)
            {
                element.IsSelected = collection.SelectedIndex == sourceIndex;
                return;
            }
        }
    }

    private struct RealizedItem
    {
        internal RealizedItem(UiElement element, ulong key, int sourceIndex, UiTemplateId template, bool claimed)
        {
            Element = element;
            Key = key;
            SourceIndex = sourceIndex;
            Template = template;
            Claimed = claimed;
        }

        internal UiElement Element;
        internal ulong Key;
        internal int SourceIndex;
        internal UiTemplateId Template;
        internal bool Claimed;
    }
}

/// <summary>Typed selected-item presenter used by generated picker artifacts.</summary>
/// <remarks>
/// The presenter reuses one retained header element by template identity. It never boxes the
/// selected item and only rebinds after selection or source-version changes.
/// </remarks>
public sealed class UiPickerSelectionPresenter<TItem, TSource, TPlan> : IDisposable
    where TSource : IUiItemsSource<TItem>
    where TPlan : IUiItemTemplatePlan<TPlan, TItem>
{
    private readonly UiPicker _picker;
    private readonly TSource _source;
    private readonly UiResourceCatalog _resources;
    private UiElement? _element;
    private UiTemplateId _template;
    private ulong _sourceVersion = ulong.MaxValue;
    private int _selectedIndex = int.MinValue;

    public UiPickerSelectionPresenter(UiPicker picker, TSource source, UiResourceCatalog resources)
    {
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resources);
        _picker = picker;
        _source = source;
        _resources = resources;
    }

    public bool Update()
    {
        var selectedIndex = _picker.SelectedIndex;
        if (selectedIndex < -1 || selectedIndex >= _source.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        }

        var sourceVersion = _source.Version;
        if (_selectedIndex == selectedIndex && _sourceVersion == sourceVersion)
        {
            return false;
        }

        if (selectedIndex < 0)
        {
            Clear();
            _selectedIndex = selectedIndex;
            _sourceVersion = sourceVersion;
            return true;
        }

        var item = _source.GetItem(selectedIndex);
        var template = TPlan.SelectTemplate(in item);
        if (!template.IsValid)
        {
            throw new InvalidOperationException("Picker template selection returned an empty identity.");
        }

        if (_element is null || _template != template)
        {
            Clear();
            _element = TPlan.Create(template, in item, _resources) ??
                throw new InvalidOperationException("The generated picker template returned a null element.");
            _template = template;
            _picker.Header.SetContent(_element);
        }

        TPlan.Bind(_element, template, in item, _resources);
        _selectedIndex = selectedIndex;
        _sourceVersion = sourceVersion;
        return true;
    }

    public void Dispose() => Clear();

    private void Clear()
    {
        if (_element is not null)
        {
            TPlan.Unbind(_element, _template);
            _picker.Header.SetContent(null);
            _element = null;
            _template = default;
        }
    }
}
