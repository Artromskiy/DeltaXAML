namespace Delta.XAML;

/// <summary>Horizontal alignment of a text block inside its arranged bounds.</summary>
public enum UiTextHorizontalAlignment : byte
{
    Unknown,
    Left,
    Center,
    Right,
    Justify,
}

/// <summary>Vertical alignment of a text block inside its arranged bounds.</summary>
public enum UiTextVerticalAlignment : byte
{
    Unknown,
    Top,
    Center,
    Bottom,
}

/// <summary>Line-breaking policy used while measuring text.</summary>
public enum UiTextWrapping : byte
{
    Unknown,
    NoWrap,
    Word,
    Character,
}

/// <summary>Overflow policy used when text exceeds its arranged bounds.</summary>
public enum UiTextTrimming : byte
{
    Unknown,
    None,
    CharacterEllipsis,
    WordEllipsis,
}

/// <summary>Logical weight of a text face selected by the host font catalog.</summary>
public enum UiFontWeight : byte
{
    Unknown,
    Normal,
    Medium,
    SemiBold,
    Bold,
}

/// <summary>Logical style of a text face selected by the host font catalog.</summary>
public enum UiFontStyle : byte
{
    Unknown,
    Normal,
    Italic,
    Oblique,
}

/// <summary>Decorations applied to a text run.</summary>
[Flags]
public enum UiTextDecorations : byte
{
    None = 0,
    Underline = 1 << 0,
    Strikethrough = 1 << 1,
}
