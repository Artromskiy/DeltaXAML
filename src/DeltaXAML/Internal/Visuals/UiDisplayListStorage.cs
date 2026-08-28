using Delta.XAML.Contract;

namespace Delta.XAML;

/// <summary>Reusable canonical display-list buffers owned by one <see cref="UiDocument"/>.</summary>
/// <remarks>
/// The visual stage writes these buffers in place. A snapshot only exposes borrowed spans, so
/// the storage must outlive every display-list view and is invalidated by the next extraction.
/// </remarks>
internal sealed class UiDisplayListStorage
{
    internal UiVisualCommand[] Visuals = Array.Empty<UiVisualCommand>();
    internal UiClip[] Clips = Array.Empty<UiClip>();
    internal UiTextDraw[] Text = Array.Empty<UiTextDraw>();
    internal int VisualCount;
    internal int ClipCount;
    internal int TextCount;

    internal void ClearCounts()
    {
        VisualCount = 0;
        ClipCount = 0;
        TextCount = 0;
    }

    internal UiDisplayList BorrowedView() =>
        new(Visuals.AsSpan(0, VisualCount), Clips.AsSpan(0, ClipCount), Text.AsSpan(0, TextCount));
}
