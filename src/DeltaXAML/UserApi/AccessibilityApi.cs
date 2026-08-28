using System.Globalization;
using Delta.Maths;
using Delta.Text.Contract;

namespace Delta.XAML;

public enum UiSemanticRole
{
    None,
    Unknown,
    Generic,
    Window,
    Text,
    TextBox,
    NumericEditor,
    Button,
    Slider,
    Image,
    List,
    ListItem,
    Menu,
    Tab,
    Link,
}

[Flags]
public enum UiSemanticActions
{
    None = 0,
    Focus = 1 << 0,
    Invoke = 1 << 1,
    SetValue = 1 << 2,
    Increment = 1 << 3,
    Decrement = 1 << 4,
    Select = 1 << 5,
    Scroll = 1 << 6,
}

/// <summary>One platform-neutral accessibility node in document order.</summary>
public readonly record struct UiSemanticNode(
    uint ElementId,
    uint Generation,
    UiSemanticRole Role,
    string Name,
    string Value,
    float4 Bounds,
    UiSemanticActions Actions,
    bool IsEnabled,
    bool IsFocused,
    bool IsInvalid,
    int PositionInSet,
    int SetSize);

/// <summary>Borrowed semantic output valid until the next extraction or document mutation.</summary>
public readonly ref struct UiSemanticSnapshot
{
    internal UiSemanticSnapshot(ReadOnlySpan<UiSemanticNode> nodes, uint version)
    {
        Nodes = nodes;
        Version = version;
    }

    public ReadOnlySpan<UiSemanticNode> Nodes { get; }

    public uint Version { get; }
}

/// <summary>Explicit text culture and direction used by shaping and generated formatting.</summary>
public readonly record struct UiLocalizationContext(CultureInfo Culture, TextDirection Direction, string? Language = null)
{
    public static UiLocalizationContext Invariant { get; } = new(CultureInfo.InvariantCulture, TextDirection.Auto);

    public UiLocalizationContext Validate()
    {
        ArgumentNullException.ThrowIfNull(Culture);
        return this;
    }
}
