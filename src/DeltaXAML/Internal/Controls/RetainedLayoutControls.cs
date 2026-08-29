using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal class Panel : UiElement
{
    private PanelState _state;

    internal ref PanelState State => ref _state;

    public Panel(string typeName = "Panel") : base(typeName) { }
}
internal sealed class StackPanel : UiElement
{
    private StackPanelState _state = new() { Orientation = UiOrientation.Vertical };

    internal ref StackPanelState State => ref _state;

    public StackPanel() : base("StackPanel") => SetDefaultProperty("Orientation", _state.Orientation, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    public UiOrientation Orientation
    {
        get => _state.Orientation;
        set => SetLocalProperty("Orientation", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

}


internal class Border : UiElement
{
    private BorderState _state;

    internal ref BorderState State => ref _state;

    public Border() : base("Border") { }
    public UiElement? Child => Children.Count == 0 ? null : Children[0];
}

internal readonly record struct GridLength(float Value, GridUnitType Unit) { public static GridLength Fixed(float v) => new(v, GridUnitType.Pixel); public static GridLength Auto => new(1, GridUnitType.Auto); public static GridLength Star(float weight = 1) => new(weight, GridUnitType.Star); }
internal enum GridUnitType { Pixel, Auto, Star }

internal sealed class Grid : UiElement
{
    private GridState _state;

    internal ref GridState State => ref _state;

    public Grid() : base("Grid")
    {
        _state.Columns = Array.Empty<GridLength>();
        _state.Rows = Array.Empty<GridLength>();
        _state.MeasuredColumns = Array.Empty<float>();
        _state.MeasuredRows = Array.Empty<float>();
        _state.ResolvedColumns = new float[1];
        _state.ResolvedRows = new float[1];
        SetDefaultProperty("Columns", _state.Columns, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
        SetDefaultProperty("Rows", _state.Rows, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

    public int ColumnCount => _state.Columns.Length;
    public int RowCount => _state.Rows.Length;
    public void SetColumns(params GridLength[] columns)
        => SetLocalProperty("Columns", columns, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);

    public void SetRows(params GridLength[] rows)
        => SetLocalProperty("Rows", rows, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);

}

internal class ContentControl : UiElement
{
    private ContentControlState _state;

    internal ref ContentControlState State => ref _state;

    public ContentControl(string typeName = "ContentControl") : base(typeName) { }
    public UiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
}
