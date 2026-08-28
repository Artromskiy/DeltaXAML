using Delta.XAML;

internal static class RelationBindingTests
{
    public static void Run()
    {
        SelfAndFixedRelationsStayTyped();
        AncestorRelationRebindsAfterReparent();
        MultiBindingUsesFixedTypedSources();
    }

    private static void SelfAndFixedRelationsStayTyped()
    {
        var text = new UiTextBlock { Text = "self" };
        using var self = new UiRelationBinding<TextPlan, string>(text, UiBindingSource.Self, UiBindingMode.TwoWay);
        Assert.Equal("self", self.ReadValue(), "Self resolves directly to the retained target");
        Assert.True(self.TryWriteValue("changed", out var diagnostic) && diagnostic is null, "a generated two-way relation writes through its typed plan");
        Assert.Equal("changed", text.Text, "the typed relation write updates the source element");

        var owner = new UiTextBlock { Text = "owner" };
        using var templateOwner = new UiRelationBinding<TextPlan, string>(text, UiBindingSource.TemplateOwner(owner));
        Assert.Equal("owner", templateOwner.ReadValue(), "TemplateOwner is fixed during template construction");
        using var named = new UiRelationBinding<TextPlan, string>(text, UiBindingSource.NamedElement(owner));
        Assert.Equal("owner", named.ReadValue(), "a generated namescope link is a fixed typed relation");
    }

    private static void AncestorRelationRebindsAfterReparent()
    {
        var first = new UiPanel { Width = 40 };
        var second = new UiPanel { Width = 80 };
        var child = new UiTextBlock();
        first.Add(child);
        using var relation = new UiRelationBinding<WidthPlan, float>(child, UiBindingSource.Ancestor(UiKnownTypes.Panel));
        Assert.Equal(40f, relation.ReadValue(), "ancestor relation resolves from the initial structural generation");

        first.Remove(child);
        second.Add(child);
        Assert.Equal(80f, relation.ReadValue(), "reparent invalidates and lazily refreshes the cached ancestor identity");
    }

    private static void MultiBindingUsesFixedTypedSources()
    {
        UiElement[] sources = [new UiTextBlock { Text = "left" }, new UiTextBlock { Text = "right" }];
        using var binding = new UiMultiBinding<JoinPlan, string>(sources);
        Assert.Equal("left/right", binding.ReadValue(), "multi-source binding evaluates one static typed function");
        sources[0] = new UiTextBlock { Text = "mutated-array" };
        Assert.Equal("left/right", binding.ReadValue(), "multi binding owns a stable source relation snapshot");
    }

    private readonly struct TextPlan : IUiRelationBindingPlan<TextPlan, string>
    {
        public static string Read(UiElement source) => ((UiTextBlock)source).Text;

        public static bool TryWrite(UiElement source, string value)
        {
            ((UiTextBlock)source).Text = value;
            return true;
        }
    }

    private readonly struct WidthPlan : IUiRelationBindingPlan<WidthPlan, float>
    {
        public static float Read(UiElement source) => source.Width;

        public static bool TryWrite(UiElement source, float value)
        {
            source.Width = value;
            return true;
        }
    }

    private readonly struct JoinPlan : IUiMultiBindingPlan<JoinPlan, string>
    {
        public static string Read(ReadOnlySpan<UiElement> sources) =>
            $"{((UiTextBlock)sources[0]).Text}/{((UiTextBlock)sources[1]).Text}";
    }
}
