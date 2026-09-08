using System.ComponentModel;

namespace DeltaXaml.Tests;

public sealed class BindingModel : INotifyPropertyChanged
{
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class EffectBindingModel : INotifyPropertyChanged
{
    private Delta.XAML.UiColor _accent = new(64, 128, 255);
    private Delta.XAML.UiColor _glowColor = new(32, 96, 255);
    private float _strokeWidth = 2;
    private float _glowRadius = 6;

    public Delta.XAML.UiColor Accent
    {
        get => _accent;
        set => Set(ref _accent, value, nameof(Accent));
    }

    public Delta.XAML.UiColor GlowColor
    {
        get => _glowColor;
        set => Set(ref _glowColor, value, nameof(GlowColor));
    }

    public float StrokeWidth
    {
        get => _strokeWidth;
        set => Set(ref _strokeWidth, value, nameof(StrokeWidth));
    }

    public float GlowRadius
    {
        get => _glowRadius;
        set => Set(ref _glowRadius, value, nameof(GlowRadius));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}

public static class GeneratedBindingConverters
{
    [Delta.XAML.UiXamlConverter("Upper")]
    public static string ToUpper(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.ToUpperInvariant();
    }

    [Delta.XAML.UiXamlConverter("Upper", Delta.XAML.UiXamlConverterDirection.Backward)]
    public static string ToSource(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Trim();
    }
}

public static class GeneratedBindingFunctions
{
    [Delta.XAML.UiXamlBindingFunction("JoinText")]
    public static string Join(string first, string second) => first + ":" + second;

    [Delta.XAML.UiXamlBindingFunction("FirstColor")]
    public static Delta.XAML.UiColor FirstColor(Delta.XAML.UiColor first, Delta.XAML.UiColor _) => first;

    [Delta.XAML.UiXamlBindingFunction("ArmedValue")]
    public static string ArmedValue(bool armed, double value) =>
        (armed ? "True:" : "False:") + value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Delta.XAML.UiXamlBindingFunction("RowSummary")]
    public static string RowSummary(string label, bool alternate) => label + ":" + alternate;
}

public static class GeneratedLayout
{
    [Delta.XAML.UiXamlAttachedProperty(
        "urn:delta-tests",
        "Layout",
        "4749EF84-4B6D-445E-AD7F-A9993ACDAA18",
        "F06CC963-9504-4DA7-AEBB-F79F01F548EB",
        Delta.XAML.UiXamlValueKind.WholeNumber)]
    public static Delta.XAML.UiAttachedProperty<int> Priority { get; } = new(
        new(new Guid("4749EF84-4B6D-445E-AD7F-A9993ACDAA18")),
        new(new Guid("F06CC963-9504-4DA7-AEBB-F79F01F548EB")),
        "Priority",
        3);
}

public readonly record struct CollectionRow(ulong Key, string Label, bool Alternate = false);

public static class CollectionSelectors
{
    [Delta.XAML.UiXamlTemplateSelector("RowKind", "RowTemplate", "AlternateRowTemplate")]
    public static int Select(in CollectionRow row) => row.Alternate ? 1 : 0;
}

public sealed class CollectionRowSource(params CollectionRow[] rows) : Delta.XAML.IUiItemsSource<CollectionRow>
{
    public int Count => rows.Length;

    public ulong Version => 0;

    public ulong GetKey(int index) => rows[index].Key;

    public CollectionRow GetItem(int index) => rows[index];

    public bool TryGetChange(ulong previousVersion, out Delta.XAML.UiCollectionChange change)
    {
        change = default;
        return false;
    }
}

public sealed class CollectionModel
{
    public CollectionRowSource Rows { get; } = new(new(1, "One"), new(2, "Two", true));
}

public sealed class FullCapabilityModel : INotifyPropertyChanged
{
    private bool _armed;
    private double _volume;

    public CollectionRowSource Rows { get; } = new(
        new(1, "One"), new(2, "Two", true), new(3, "Three"), new(4, "Four"),
        new(5, "Five"), new(6, "Six"), new(7, "Seven"), new(8, "Eight"));

    public bool Armed
    {
        get => _armed;
        set
        {
            if (_armed == value)
            {
                return;
            }

            _armed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Armed)));
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (_volume.Equals(value))
            {
                return;
            }

            _volume = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Volume)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal struct GeneratedSelectionBehaviorState
{
    internal bool Attached { get; set; }
}

[Delta.XAML.UiXamlBehavior("SelectOnStage")]
internal readonly struct GeneratedSelectionBehavior : Delta.XAML.IUiBehaviorPlan<GeneratedSelectionBehavior, GeneratedSelectionBehaviorState>
{
    public static void Attach(Delta.XAML.UiElement element, ref GeneratedSelectionBehaviorState state) => state.Attached = true;

    public static void Detach(Delta.XAML.UiElement element, ref GeneratedSelectionBehaviorState state) => state.Attached = false;

    public static void Update(Delta.XAML.UiDocument document, Delta.XAML.UiElement element, ref GeneratedSelectionBehaviorState state)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (state.Attached)
        {
            element.IsSelected = true;
        }
    }
}
