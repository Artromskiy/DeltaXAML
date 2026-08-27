namespace DeltaXAML.Internal;

internal readonly struct ItemsControlMutationMixin
{
    public static void Apply(
        IUiItemSource source,
        IUiItemSource? previous,
        IUiItemFactory factory,
        List<UiElement> previousRealized,
        List<UiElement> nextRealized)
    {
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
