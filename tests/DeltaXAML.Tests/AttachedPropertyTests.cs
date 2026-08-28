using Delta.XAML;

internal static class AttachedPropertyTests
{
    public static void Run()
    {
        GridReadsDirectAttachedSlots();
        CustomAttachedPropertyUsesExistingPropertyStore();
    }

    private static void GridReadsDirectAttachedSlots()
    {
        var grid = new UiGrid();
        grid.SetColumns(UiGridLength.Pixel(40), UiGridLength.Star());
        grid.SetRows(UiGridLength.Pixel(20), UiGridLength.Star());
        var first = new UiPanel();
        var second = new UiPanel();
        second.SetAttachedValue(UiGridAttachedProperties.Column, 1);
        second.SetAttachedValue(UiGridAttachedProperties.Row, 1);
        grid.Add(first);
        grid.Add(second);

        RetainedLayoutTest.Layout(
            grid.RetainedElement,
            new DeltaXAML.Internal.UiSize(100, 60),
            new DeltaXAML.Internal.UiRect(0, 0, 100, 60));

        Assert.Equal(new DeltaXAML.Internal.UiRect(0, 0, 40, 20), first.RetainedElement.Bounds, "default attached slots place the first child at row zero and column zero");
        Assert.Equal(new DeltaXAML.Internal.UiRect(40, 20, 60, 40), second.RetainedElement.Bounds, "grid layout reads generated row and column slots directly");
        Assert.Equal(1, second.GetAttachedValue(UiGridAttachedProperties.Row), "the public typed attached property reads the same slot");
    }

    private static void CustomAttachedPropertyUsesExistingPropertyStore()
    {
        var property = new UiAttachedProperty<int>(
            new(new Guid("4749EF84-4B6D-445E-AD7F-A9993ACDAA18")),
            new(new Guid("F06CC963-9504-4DA7-AEBB-F79F01F548EB")),
            "Priority",
            3);
        var element = new UiPanel();

        Assert.Equal(3, element.GetAttachedValue(property), "a custom attached property starts at its typed default");
        element.SetAttachedValue(property, 8);
        Assert.Equal(8, element.GetAttachedValue(property), "a custom cold-path attached property reuses the canonical property store");
    }
}
