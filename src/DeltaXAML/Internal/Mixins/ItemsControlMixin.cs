namespace DeltaXAML.Internal;

internal readonly struct ItemsControlMutationMixin
{
    public static void Apply<T>(
        IUiItemSource<T> source,
        IUiItemSource<T>? previous,
        IUiItemFactory<T> factory,
        List<UiElement> previousRealized,
        List<UiElement> nextRealized)
    {
        ArgumentNullException.ThrowIfNull(previousRealized);
        ArgumentNullException.ThrowIfNull(nextRealized);
        nextRealized.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            if (previous is not null && i < previousRealized.Count && source.Matches(previous, i))
            {
                nextRealized.Add(previousRealized[i]);
            }
            else
            {
                nextRealized.Add(factory.Create(source, i));
            }
        }
    }
}
