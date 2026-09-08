namespace Delta.XAML;

using Delta.XAML.Contract;
using RetainedElement = DeltaXAML.Internal.UiElement;

/// <summary>Typed property descriptor used by the low-boilerplate user API.</summary>
public sealed class UiProperty<T> : IUiProperty<T>
{
    public UiProperty(UiPropertyId id, string name, T defaultValue)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException("A stable property identity is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        DefaultValue = defaultValue;
    }

    public UiPropertyId Id { get; }

    public string Name { get; }

    public Type ValueType => typeof(T);

    public T DefaultValue { get; }

    object? IUiProperty.DefaultValue => DefaultValue;
}

/// <summary>Typed attached-property descriptor with a stable owner and property identity.</summary>
public sealed class UiAttachedProperty<T>
{
    private readonly Func<RetainedElement, T>? _read;
    private readonly Action<RetainedElement, T>? _write;

    public UiAttachedProperty(UiTypeId owner, UiPropertyId id, string name, T defaultValue)
        : this(owner, id, name, defaultValue, null, null)
    {
    }

    internal UiAttachedProperty(
        UiTypeId owner,
        UiPropertyId id,
        string name,
        T defaultValue,
        Func<RetainedElement, T>? read,
        Action<RetainedElement, T>? write)
    {
        if (!owner.IsValid)
        {
            throw new ArgumentException("A stable attached-property owner identity is required.", nameof(owner));
        }

        if (!id.IsValid)
        {
            throw new ArgumentException("A stable property identity is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Owner = owner;
        Id = id;
        Name = name;
        DefaultValue = defaultValue;
        _read = read;
        _write = write;
    }

    public UiTypeId Owner { get; }

    public UiPropertyId Id { get; }

    public string Name { get; }

    public T DefaultValue { get; }

    internal T Read(RetainedElement element)
    {
        if (_read is not null)
        {
            return _read(element);
        }

        return element.TryGet(StorageName, out var value) && value.UntypedValue is T typed
            ? typed
            : DefaultValue;
    }

    internal void Write(RetainedElement element, T value)
    {
        if (_write is not null)
        {
            _write(element, value);
            return;
        }

        element.SetLocal(StorageName, value, DeltaXAML.Internal.UiDirtyMask.Measure | DeltaXAML.Internal.UiDirtyMask.Arrange);
    }

    private string StorageName => $"{Owner.Value:D}.{Name}";
}

/// <summary>Common typed properties available on every element.</summary>
public static class UiElementProperties
{
    public static UiProperty<float> Width { get; } = Create<float>("10000000-0000-4000-8000-000000000001", "Width", float.NaN);
    public static UiProperty<float> Height { get; } = Create<float>("10000000-0000-4000-8000-000000000002", "Height", float.NaN);
    public static UiProperty<UiThickness> Margin { get; } = Create("10000000-0000-4000-8000-000000000016", "Margin", UiThickness.Zero);
    public static UiProperty<UiHorizontalAlignment> HorizontalAlignment { get; } = Create("10000000-0000-4000-8000-000000000017", "HorizontalAlignment", UiHorizontalAlignment.Stretch);
    public static UiProperty<UiVerticalAlignment> VerticalAlignment { get; } = Create("10000000-0000-4000-8000-000000000018", "VerticalAlignment", UiVerticalAlignment.Stretch);
    public static UiProperty<UiColor> Background { get; } = Create<UiColor>("10000000-0000-4000-8000-000000000003", "Background", default);
    public static UiProperty<UiThickness> Padding { get; } = Create("10000000-0000-4000-8000-000000000004", "Padding", UiThickness.Zero);
    public static UiProperty<bool> IsEnabled { get; } = Create("10000000-0000-4000-8000-000000000006", "IsEnabled", true);
    public static UiProperty<bool> IsSelected { get; } = Create("10000000-0000-4000-8000-000000000007", "IsSelected", false);
    public static UiProperty<string?> StyleKey { get; } = Create<string?>("10000000-0000-4000-8000-000000000008", "StyleKey", null);
    public static UiProperty<string?> TemplateKey { get; } = Create<string?>("10000000-0000-4000-8000-000000000009", "TemplateKey", null);
    public static UiProperty<UiBrush> BackgroundBrush { get; } = Create("10000000-0000-4000-8000-00000000000A", "BackgroundBrush", UiBrush.None);
    public static UiProperty<UiColor> BorderColor { get; } = Create<UiColor>("10000000-0000-4000-8000-000000000011", "BorderColor", default);
    public static UiProperty<UiEffectSet> EffectSet { get; } = Create("10000000-0000-4000-8000-000000000019", "EffectSet", UiEffectSet.None);
    public static UiProperty<float> BorderWidth { get; } = Create<float>("10000000-0000-4000-8000-000000000012", "BorderWidth", 0f);
    public static UiProperty<PaintUnits> BorderWidthUnits { get; } = Create("10000000-0000-4000-8000-000000000015", "BorderWidthUnits", PaintUnits.Logical);
    public static UiProperty<UiCornerRadii> CornerRadius { get; } = Create("10000000-0000-4000-8000-000000000013", "CornerRadius", UiCornerRadii.Zero);
    public static UiProperty<string> AutomationName { get; } = Create("10000000-0000-4000-8000-00000000000B", "AutomationName", string.Empty);
    public static UiProperty<UiSemanticRole> AutomationRole { get; } = Create("10000000-0000-4000-8000-00000000000C", "AutomationRole", UiSemanticRole.Generic);
    public static UiProperty<UiGestureKind> Gestures { get; } = Create("10000000-0000-4000-8000-00000000000D", "Gestures", UiGestureKind.None);
    public static UiProperty<UiCommandId> Command { get; } = Create("10000000-0000-4000-8000-00000000000E", "Command", UiCommandId.Empty);
    public static UiProperty<UiKeyGesture> CommandKey { get; } = Create("10000000-0000-4000-8000-00000000000F", "CommandKey", default(UiKeyGesture));
    public static UiProperty<bool> IsFocusScope { get; } = Create("10000000-0000-4000-8000-000000000010", "IsFocusScope", false);

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}

/// <summary>Typed text properties for <see cref="UiTextBlock"/> and text editors.</summary>
public static class TextBlockProperties
{
    public static UiProperty<string> Text { get; } = Create("20000000-0000-4000-8000-000000000001", "Text", string.Empty);
    public static UiProperty<string> FontKey { get; } = Create("20000000-0000-4000-8000-000000000002", "FontKey", "default");
    public static UiProperty<float> FontSize { get; } = Create("20000000-0000-4000-8000-000000000003", "FontSize", 14f);
    public static UiProperty<UiColor> Foreground { get; } = Create("20000000-0000-4000-8000-000000000004", "Foreground", new UiColor(255, 255, 255));
    public static UiProperty<UiColor> OutlineColor { get; } = Create<UiColor>("20000000-0000-4000-8000-000000000005", "OutlineColor", default);
    public static UiProperty<float> OutlineWidth { get; } = Create<float>("20000000-0000-4000-8000-000000000006", "OutlineWidth", 0f);
    public static UiProperty<Delta.XAML.Contract.UiResourceId> TextEffect { get; } = Create<Delta.XAML.Contract.UiResourceId>("20000000-0000-4000-8000-000000000007", "TextEffect", Delta.XAML.Contract.UiResourceId.Empty);
    public static UiProperty<UiTextHorizontalAlignment> HorizontalTextAlignment { get; } = Create("20000000-0000-4000-8000-000000000008", "HorizontalTextAlignment", UiTextHorizontalAlignment.Left);
    public static UiProperty<UiTextVerticalAlignment> VerticalTextAlignment { get; } = Create("20000000-0000-4000-8000-000000000009", "VerticalTextAlignment", UiTextVerticalAlignment.Top);
    public static UiProperty<UiTextWrapping> TextWrapping { get; } = Create("20000000-0000-4000-8000-00000000000A", "TextWrapping", UiTextWrapping.NoWrap);
    public static UiProperty<UiTextTrimming> TextTrimming { get; } = Create("20000000-0000-4000-8000-00000000000B", "TextTrimming", UiTextTrimming.None);
    public static UiProperty<int> MaxLines { get; } = Create("20000000-0000-4000-8000-00000000000C", "MaxLines", 0);
    public static UiProperty<float> LineHeight { get; } = Create("20000000-0000-4000-8000-00000000000D", "LineHeight", 0f);
    public static UiProperty<UiFontWeight> FontWeight { get; } = Create("20000000-0000-4000-8000-00000000000E", "FontWeight", UiFontWeight.Normal);
    public static UiProperty<UiFontStyle> FontStyle { get; } = Create("20000000-0000-4000-8000-00000000000F", "FontStyle", UiFontStyle.Normal);
    public static UiProperty<UiTextDecorations> TextDecorations { get; } = Create("20000000-0000-4000-8000-000000000010", "TextDecorations", UiTextDecorations.None);

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}

/// <summary>Typed editor-only text properties shared by <see cref="UiTextBox"/> and <see cref="UiNumericEditor"/>.</summary>
public static class TextBoxProperties
{
    public static UiProperty<string> PlaceholderText { get; } = Create("21000000-0000-4000-8000-000000000001", "PlaceholderText", string.Empty);
    public static UiProperty<bool> IsReadOnly { get; } = Create("21000000-0000-4000-8000-000000000002", "IsReadOnly", false);
    public static UiProperty<bool> AcceptsReturn { get; } = Create("21000000-0000-4000-8000-000000000003", "AcceptsReturn", false);
    public static UiProperty<int> MaxLength { get; } = Create("21000000-0000-4000-8000-000000000004", "MaxLength", 0);

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}

/// <summary>Typed numeric properties for <see cref="UiNumericEditor"/>.</summary>
public static class UiNumericEditorProperties
{
    public static UiProperty<double> Value { get; } = Create("30000000-0000-4000-8000-000000000001", "Value", 0d);
    public static UiProperty<double> Minimum { get; } = Create("30000000-0000-4000-8000-000000000002", "Minimum", double.MinValue);
    public static UiProperty<double> Maximum { get; } = Create("30000000-0000-4000-8000-000000000003", "Maximum", double.MaxValue);

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}

public static class UiSliderProperties
{
    public static UiProperty<double> Value { get; } = Create("70000000-0000-4000-8000-000000000001", "Value", 0d);
    public static UiProperty<double> Minimum { get; } = Create("70000000-0000-4000-8000-000000000002", "Minimum", 0d);
    public static UiProperty<double> Maximum { get; } = Create("70000000-0000-4000-8000-000000000003", "Maximum", 1d);
    public static UiProperty<double> Step { get; } = Create("70000000-0000-4000-8000-000000000004", "Step", 0.1d);

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}

public static class UiImageProperties
{
    public static UiProperty<Delta.XAML.Contract.UiResourceId> Source { get; } =
        Create("71000000-0000-4000-8000-000000000001", "Source", Delta.XAML.Contract.UiResourceId.Empty);
    public static UiProperty<UiColor> Tint { get; } =
        Create("71000000-0000-4000-8000-000000000002", "Tint", new UiColor(255, 255, 255));
    public static UiProperty<UiImageStretch> Stretch { get; } =
        Create("71000000-0000-4000-8000-000000000003", "Stretch", UiImageStretch.Uniform);
    public static UiProperty<Delta.XAML.Contract.UiResourceId> Placeholder { get; } =
        Create("71000000-0000-4000-8000-000000000004", "Placeholder", Delta.XAML.Contract.UiResourceId.Empty);
    public static UiProperty<Delta.XAML.Contract.UiResourceId> ErrorSource { get; } =
        Create("71000000-0000-4000-8000-000000000005", "ErrorSource", Delta.XAML.Contract.UiResourceId.Empty);

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}

public static class UiSelectorProperties
{
    public static UiProperty<int> SelectedIndex { get; } =
        new(new UiPropertyId(new Guid("72000000-0000-4000-8000-000000000002")), "SelectedIndex", -1);
}

public static class UiOverlayProperties
{
    public static UiProperty<bool> IsOpen { get; } =
        new(new UiPropertyId(new Guid("72000000-0000-4000-8000-000000000001")), "IsOpen", false);
}

/// <summary>Typed orientation property for <see cref="UiStackPanel"/> styles.</summary>
public static class UiStackPanelProperties
{
    public static UiProperty<UiOrientation> Orientation { get; } =
        Create("40000000-0000-4000-8000-000000000001", "Orientation", UiOrientation.Vertical);

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}

/// <summary>Typed grid definition properties for <see cref="UiGrid"/> styles.</summary>
public static class UiGridProperties
{
    public static UiProperty<UiGridLength[]> Columns { get; } =
        Create("40000000-0000-4000-8000-000000000002", "Columns", Array.Empty<UiGridLength>());
    public static UiProperty<UiGridLength[]> Rows { get; } =
        Create("40000000-0000-4000-8000-000000000003", "Rows", Array.Empty<UiGridLength>());

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}

/// <summary>Direct generated grid metadata slots read by the grid layout capability.</summary>
public static class UiGridAttachedProperties
{
    private static readonly UiTypeId Owner = new(new Guid("60000000-0000-4000-8000-000000000001"));

    public static UiAttachedProperty<int> Row { get; } = Create(
        "60000000-0000-4000-8000-000000000002",
        "Row",
        0,
        static element => element.GridRow,
        static (element, value) => element.SetGridRow(value));

    public static UiAttachedProperty<int> Column { get; } = Create(
        "60000000-0000-4000-8000-000000000003",
        "Column",
        0,
        static element => element.GridColumn,
        static (element, value) => element.SetGridColumn(value));

    public static UiAttachedProperty<int> RowSpan { get; } = Create(
        "60000000-0000-4000-8000-000000000004",
        "RowSpan",
        1,
        static element => element.GridRowSpan,
        static (element, value) => element.SetGridRowSpan(value));

    public static UiAttachedProperty<int> ColumnSpan { get; } = Create(
        "60000000-0000-4000-8000-000000000005",
        "ColumnSpan",
        1,
        static element => element.GridColumnSpan,
        static (element, value) => element.SetGridColumnSpan(value));

    private static UiAttachedProperty<int> Create(
        string id,
        string name,
        int defaultValue,
        Func<RetainedElement, int> read,
        Action<RetainedElement, int> write) =>
        new(Owner, new UiPropertyId(Guid.Parse(id)), name, defaultValue, read, write);
}
