using Delta.XAML;
using System.ComponentModel;

namespace DeltaXaml.Samples.UiLibraryDemo;

// Data only: repeated controls are constructed by generated DXAML templates.
public readonly record struct DemoEntry(string Label, UiColor Color, UiColor Stroke, bool Enabled = true, string Glyph = "");

public sealed class DemoItems(params DemoEntry[] entries) : IUiItemsSource<DemoEntry>
{
    public int Count => entries.Length;
    public ulong Version => 0;
    public ulong GetKey(int index) => (ulong)index + 1;
    public DemoEntry GetItem(int index) => entries[index];
    public bool TryGetChange(ulong previousVersion, out UiCollectionChange change)
    {
        change = default;
        return false;
    }
}

public sealed class DemoModel : INotifyPropertyChanged
{
    private float _pageWidth;
    public float PageWidth
    {
        get => _pageWidth;
        set
        {
            if (_pageWidth == value)
            {
                return;
            }

            _pageWidth = value;
            PropertyChanged?.Invoke(this, new(nameof(PageWidth)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private static readonly UiColor Text = new(185, 187, 193);
    private static readonly UiColor Line = new(48, 50, 56);
    private static readonly UiColor Orange = new(255, 138, 0);

    public DemoItems Tabs { get; } = new(
        new("Game", new(109, 109, 109), default), new("Scene", new(109, 109, 109), default),
        new("Entities", new(109, 109, 109), default), new("Disabled", new(53, 53, 53), default, false));
    public DemoItems Actions { get; } = new(
        new("Primary", Orange, Orange), new("Secondary", default, Line),
        new("Tertiary", default, default), new("Disabled", default, Line, false));
    public DemoItems Menus { get; } = new(
        new("Create", new(24, 25, 28), Line), new("Add Component", new(24, 25, 28), Line));
    public DemoItems Icons { get; } = new(
        Icon("\uE037"), Icon("\uE047"), Icon("\uE145"), Icon("\uE3C4"), Icon("\uE8B8"), Icon("\uE53B"),
        Icon("\uE3EC"), Icon("\uEAD5"), Icon("\uE8EF"), Icon("\uE8B6"), Icon("\uE5D3"), Icon("\uE061"));
    public DemoItems Badges { get; } = new(new("v1.0.0", Text, Line), new("Active", Text, Orange), new("Web", Text, new(40, 168, 189)));
    public DemoItems Markers { get; } = new(
        new("Dot", Text, Line, Glyph: "\uE061"), new("Collapsed", Text, Line, Glyph: "\uEAD5"),
        new("Expanded", Text, Orange, Glyph: "\uE5CD"));
    public DemoItems Colors { get; } = new(
        Color("Success", 102, 196, 60), Color("Warning", 255, 148, 8), Color("Error", 255, 75, 49),
        Color("Info", 55, 135, 232), Color("Neutral", 89, 100, 108), Color("Spectrum", 216, 60, 165));
    public GridLines VerticalLines { get; } = new();
    public GridLines HorizontalLines { get; } = new();
    public GridLines PreviewVertical { get; } = new();
    public GridLines PreviewHorizontal { get; } = new();

    private static DemoEntry Icon(string text) => new(text, Text, Line);
    private static DemoEntry Color(string text, byte r, byte g, byte b) => new(text, new(r, g, b), default);
}

public sealed class GridLines : IUiItemsSource<DemoEntry>
{
    public int Count { get; private set; }
    public ulong Version { get; private set; }
    public ulong GetKey(int index) => (ulong)index + 1;
    public DemoEntry GetItem(int index) => default;
    public void Resize(int count)
    {
        if (Count != count)
        {
            Count = count;
            Version++;
        }
    }
    public bool TryGetChange(ulong previousVersion, out UiCollectionChange change)
    {
        change = UiCollectionChange.Reset;
        return true;
    }
}

public static class DemoConverters
{
    [UiXamlConverter("EnabledText")]
    public static UiColor EnabledText(bool enabled) => enabled ? new(232, 233, 235) : new(52, 54, 58);
}
