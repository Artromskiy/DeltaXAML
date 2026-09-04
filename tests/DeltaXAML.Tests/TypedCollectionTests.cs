using Delta;
using Delta.XAML;
using Delta.XAML.Contract;

internal static class TypedCollectionTests
{
    public static void Run()
    {
        TypedVirtualizationPreservesIdentity();
        BoundedReplaceRebindsOnlyChangedItem();
        StructuralDeltasPreserveUnaffectedIdentity();
        ViewportPolicyProducesBoundedOverscan();
        CollectionSelectionUsesRetainedRows();
        PickerComposesTypedSelectionAndKeyboardRouting();
    }

    private static void ViewportPolicyProducesBoundedOverscan()
    {
        var first = UiVirtualizingLayout.Vertical(0, 50, 10, 100);
        var shifted = UiVirtualizingLayout.Vertical(120, 50, 10, 100);

        Assert.Equal(new UiRealizationRange(0, 7), first, "the fixed-extent policy realizes the viewport plus bounded overscan");
        Assert.Equal(new UiRealizationRange(11, 7), shifted, "scrolling advances the source range without rebuilding the full collection");
    }

    private static void TypedVirtualizationPreservesIdentity()
    {
        RowTemplate.Reset();
        var source = new RowSource(
        [
            new(10, "ten"),
            new(20, "twenty"),
            new(30, "thirty"),
            new(40, "forty"),
        ]);
        var host = new UiItemsControl();
        var presenter = new UiVirtualizingPresenter<Row, RowSource, RowTemplate>(host, source);
        var first = presenter.Realize(new(0, 3));
        var twenty = presenter.GetRealizedElement(1);
        var thirty = presenter.GetRealizedElement(2);

        var second = presenter.Realize(new(1, 3));

        Assert.Equal(3, first.Created, "the first viewport creates its three retained rows");
        Assert.Equal(2, second.Reused, "the shifted viewport reuses overlapping keys");
        Assert.Equal(1, second.Created, "the shifted viewport creates only its entering key");
        Assert.True(ReferenceEquals(twenty, presenter.GetRealizedElement(0)), "key 20 keeps its retained identity");
        Assert.True(ReferenceEquals(thirty, presenter.GetRealizedElement(1)), "key 30 keeps its retained identity");
        Assert.Equal((ulong)40, presenter.GetRealizedKey(2), "the entering key occupies the final slot");
        Assert.Equal(3, host.Children.Count, "the host contains only the bounded viewport range");
    }

    private static void BoundedReplaceRebindsOnlyChangedItem()
    {
        RowTemplate.Reset();
        var source = new RowSource([new(1, "one"), new(2, "two"), new(3, "three")]);
        var presenter = new UiVirtualizingPresenter<Row, RowSource, RowTemplate>(new UiItemsControl(), source);
        presenter.Realize(new(0, 3));
        var first = presenter.GetRealizedElement(0);
        var third = presenter.GetRealizedElement(2);
        RowTemplate.Reset();

        source.Replace(1, new(2, "TWO"));
        var result = presenter.Realize(new(0, 3));

        Assert.Equal(1, result.Rebound, "a bounded replace rebinds only the changed source slot");
        Assert.Equal(1, RowTemplate.BindCount, "the typed template receives one changed item");
        Assert.True(ReferenceEquals(first, presenter.GetRealizedElement(0)), "the preceding retained row is stable");
        Assert.True(ReferenceEquals(third, presenter.GetRealizedElement(2)), "the following retained row is stable");
        Assert.Equal("TWO", ((UiTextBlock)presenter.GetRealizedElement(1)).Text, "the changed typed value reaches its row");
        Assert.Equal(0, result.Created, "a replace does not create a new row when the key and template are stable");
    }

    private static void CollectionSelectionUsesRetainedRows()
    {
        var source = new RowSource([new(1, "one"), new(2, "two"), new(3, "three")]);
        var collection = new UiCollectionView { Width = 100, Height = 40 };
        var presenter = new UiVirtualizingPresenter<Row, RowSource, RowTemplate>(collection.ItemsHost, source);
        presenter.Realize(new(0, 3));
        using var text = new EmptyTextService();
        using var document = new UiDocument(collection, text);
        document.Layout(new float2(100, 40), 1);

        document.Dispatch(UiInputEvent.FromPointingDevice(Pointer(UiPointerEventKind.ButtonDown, 25)));
        document.Dispatch(UiInputEvent.FromPointingDevice(Pointer(UiPointerEventKind.ButtonUp, 25)));
        document.Layout(new float2(100, 40), 1);

        Assert.Equal(1, collection.SelectedIndex, "pointer routing selects the typed source index of the retained row");
        Assert.True(presenter.GetRealizedElement(1).IsSelected, "selection chrome is state on the existing row rather than a rebuilt wrapper");

        document.Dispatch(UiInputEvent.FromKey(new(
            UiKeyEventKind.Down,
            new UiPhysicalKey(40),
            default,
            default,
            false)));
        document.Layout(new float2(100, 40), 1);
        Assert.Equal(2, collection.SelectedIndex, "physical arrow routing advances collection selection without text input");
    }

    private static void PickerComposesTypedSelectionAndKeyboardRouting()
    {
        var source = new RowSource([new(1, "one"), new(2, "two"), new(3, "three")]);
        var picker = new UiPicker { Width = 100, Height = 60 };
        var resources = new UiResourceCatalog();
        var rows = new UiVirtualizingPresenter<Row, RowSource, RowTemplate>(picker.Items.ItemsHost, source, resources);
        using var selection = new UiPickerSelectionPresenter<Row, RowSource, RowTemplate>(picker, source, resources);
        rows.Realize(new(0, 3));
        selection.Update();
        using var text = new EmptyTextService();
        using var document = new UiDocument(picker, text);
        document.Layout(new float2(100, 60), 1);

        Click(document, 10);
        Assert.True(picker.IsOpen, "picker header opens its same-document overlay through canonical pointer routing");
        Click(document, 10);
        Assert.Equal(0, picker.SelectedIndex, "picker selects the retained row source index");
        Assert.True(!picker.IsOpen, "picker closes the overlay after selection");
        selection.Update();
        Assert.True(picker.Header.Content is UiTextBlock { Text: "one" }, "picker header reuses the typed item template");

        document.Dispatch(UiInputEvent.FromKey(new(
            UiKeyEventKind.Down,
            new UiPhysicalKey(40),
            default,
            default,
            false)));
        document.Layout(new float2(100, 60), 1);
        Assert.Equal(1, picker.SelectedIndex, "nested collection and picker synchronize one keyboard selection step");
        selection.Update();
        Assert.True(picker.Header.Content is UiTextBlock { Text: "two" }, "keyboard selection rebinds the stable header presentation");
    }

    private static void StructuralDeltasPreserveUnaffectedIdentity()
    {
        RowTemplate.Reset();
        var source = new RowSource([new(1, "one"), new(2, "two"), new(3, "three")]);
        var presenter = new UiVirtualizingPresenter<Row, RowSource, RowTemplate>(new UiItemsControl(), source);
        presenter.Realize(new(0, 3));
        var one = presenter.GetRealizedElement(0);
        var two = presenter.GetRealizedElement(1);
        var three = presenter.GetRealizedElement(2);

        RowTemplate.Reset();
        source.Add(1, new(4, "four"));
        var added = presenter.Realize(new(0, 4));
        Assert.Equal(1, added.Rebound, "bounded add binds only the entering key");
        Assert.True(ReferenceEquals(one, presenter.GetRealizedElement(0)), "add preserves the preceding key identity");
        Assert.True(ReferenceEquals(two, presenter.GetRealizedElement(2)), "add preserves a shifted key identity without rebinding it");
        Assert.True(ReferenceEquals(three, presenter.GetRealizedElement(3)), "add preserves the trailing key identity");

        RowTemplate.Reset();
        source.Remove(1);
        var removed = presenter.Realize(new(0, 3));
        Assert.Equal(0, removed.Rebound, "bounded remove does not rebind surviving keys");
        Assert.True(ReferenceEquals(two, presenter.GetRealizedElement(1)), "remove restores the shifted key with retained identity");

        RowTemplate.Reset();
        source.Move(0, 2);
        var moved = presenter.Realize(new(0, 3));
        Assert.Equal(0, moved.Rebound, "bounded move reorders retained keys without rebinding their item values");
        Assert.True(ReferenceEquals(one, presenter.GetRealizedElement(2)), "move carries the same retained element to its new source index");
    }

    private static UiPointerEvent Pointer(UiPointerEventKind kind, float y) => new(
        kind,
        UiPointerDeviceKind.Mouse,
        1,
        new float2(10, y),
        default,
        default,
        kind is UiPointerEventKind.ButtonDown or UiPointerEventKind.ButtonUp ? UiPointerButton.Primary : default,
        default,
        0,
        default);

    private static void Click(UiDocument document, float y)
    {
        document.Dispatch(UiInputEvent.FromPointingDevice(Pointer(UiPointerEventKind.ButtonDown, y)));
        document.Dispatch(UiInputEvent.FromPointingDevice(Pointer(UiPointerEventKind.ButtonUp, y)));
        document.Layout(new float2(100, 60), 1);
    }

    private readonly record struct Row(ulong Key, string Label);

    private sealed class RowSource(Row[] rows) : IUiItemsSource<Row>
    {
        private readonly List<Row> _rows = [.. rows];
        private UiCollectionChange _change;

        public int Count => _rows.Count;

        public ulong Version { get; private set; }

        public ulong GetKey(int index) => _rows[index].Key;

        public Row GetItem(int index) => _rows[index];

        public bool TryGetChange(ulong previousVersion, out UiCollectionChange change)
        {
            change = _change;
            return previousVersion + 1 == Version && change.Kind != UiCollectionChangeKind.None;
        }

        public void Replace(int index, Row row)
        {
            _rows[index] = row;
            _change = new(UiCollectionChangeKind.Replace, index, 1);
            Version++;
        }

        public void Add(int index, Row row)
        {
            _rows.Insert(index, row);
            _change = new(UiCollectionChangeKind.Add, index, 1);
            Version++;
        }

        public void Remove(int index)
        {
            _rows.RemoveAt(index);
            _change = new(UiCollectionChangeKind.Remove, index, 1);
            Version++;
        }

        public void Move(int previousIndex, int index)
        {
            var row = _rows[previousIndex];
            _rows.RemoveAt(previousIndex);
            _rows.Insert(index, row);
            _change = new(UiCollectionChangeKind.Move, index, 1, previousIndex);
            Version++;
        }
    }

    private readonly struct RowTemplate : IUiItemTemplatePlan<RowTemplate, Row>
    {
        private static readonly UiTemplateId Template = new(new Guid("8A0D464B-3762-44C3-BF2C-46F7F12D4618"));

        internal static int BindCount { get; private set; }

        public static UiTemplateId SelectTemplate(in Row item) => Template;

        public static UiElement Create(UiTemplateId templateId, in Row item, UiResourceCatalog resources) =>
            new UiTextBlock { Width = 100, Height = 20 };

        public static void Bind(UiElement element, UiTemplateId templateId, in Row item, UiResourceCatalog resources)
        {
            ((UiTextBlock)element).Text = item.Label;
            BindCount++;
        }

        public static void Unbind(UiElement element, UiTemplateId templateId) { }

        internal static void Reset() => BindCount = 0;
    }
}
