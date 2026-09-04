namespace Delta.XAML;

/// <summary>Placement of an element inside the slot supplied by its parent.</summary>
/// <remarks>Start and End are logical edges. They do not imply a left-to-right flow direction.</remarks>
public enum UiHorizontalAlignment : byte
{
    Unknown,
    Start,
    Center,
    End,
    Stretch,
}

/// <summary>Placement of an element inside the slot supplied by its parent.</summary>
public enum UiVerticalAlignment : byte
{
    Unknown,
    Start,
    Center,
    End,
    Stretch,
}
